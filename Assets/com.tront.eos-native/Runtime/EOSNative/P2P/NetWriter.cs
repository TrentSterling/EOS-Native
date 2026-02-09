using System;
using System.Collections.Generic;
using System.Text;
using Epic.OnlineServices;
using UnityEngine;

namespace EOSNative.P2P
{
    /// <summary>
    /// Binary serializer with auto-growing buffer and packed integer support.
    /// Use <see cref="NetWriterPool"/> for allocation-free usage.
    /// </summary>
    public sealed class NetWriter
    {
        private byte[] _buffer;
        private int _position;

        public int Position => _position;
        public int Capacity => _buffer.Length;

        public NetWriter(int initialCapacity = 64)
        {
            _buffer = new byte[initialCapacity];
        }

        public void Reset()
        {
            if (_position > 0)
                Array.Clear(_buffer, 0, _position);
            _position = 0;
        }

        #region Output

        /// <summary>Returns a view into the internal buffer (no copy).</summary>
        public ArraySegment<byte> ToArraySegment()
        {
            return new ArraySegment<byte>(_buffer, 0, _position);
        }

        /// <summary>Returns an exact-size copy of the written data.</summary>
        public byte[] ToArray()
        {
            var result = new byte[_position];
            Buffer.BlockCopy(_buffer, 0, result, 0, _position);
            return result;
        }

        #endregion

        #region Buffer Management

        private void EnsureCapacity(int additionalBytes)
        {
            int required = _position + additionalBytes;
            if (required <= _buffer.Length) return;

            int newSize = _buffer.Length;
            while (newSize < required)
                newSize *= 2;

            var newBuffer = new byte[newSize];
            Buffer.BlockCopy(_buffer, 0, newBuffer, 0, _position);
            _buffer = newBuffer;
        }

        #endregion

        #region Primitives

        public void WriteByte(byte value)
        {
            EnsureCapacity(1);
            _buffer[_position++] = value;
        }

        public void WriteBool(bool value)
        {
            WriteByte(value ? (byte)1 : (byte)0);
        }

        public void WriteInt16(short value)
        {
            EnsureCapacity(2);
            _buffer[_position++] = (byte)value;
            _buffer[_position++] = (byte)(value >> 8);
        }

        public void WriteUInt16(ushort value)
        {
            EnsureCapacity(2);
            _buffer[_position++] = (byte)value;
            _buffer[_position++] = (byte)(value >> 8);
        }

        public void WriteInt32(int value)
        {
            EnsureCapacity(4);
            _buffer[_position++] = (byte)value;
            _buffer[_position++] = (byte)(value >> 8);
            _buffer[_position++] = (byte)(value >> 16);
            _buffer[_position++] = (byte)(value >> 24);
        }

        public void WriteUInt32(uint value)
        {
            EnsureCapacity(4);
            _buffer[_position++] = (byte)value;
            _buffer[_position++] = (byte)(value >> 8);
            _buffer[_position++] = (byte)(value >> 16);
            _buffer[_position++] = (byte)(value >> 24);
        }

        public void WriteInt64(long value)
        {
            EnsureCapacity(8);
            _buffer[_position++] = (byte)value;
            _buffer[_position++] = (byte)(value >> 8);
            _buffer[_position++] = (byte)(value >> 16);
            _buffer[_position++] = (byte)(value >> 24);
            _buffer[_position++] = (byte)(value >> 32);
            _buffer[_position++] = (byte)(value >> 40);
            _buffer[_position++] = (byte)(value >> 48);
            _buffer[_position++] = (byte)(value >> 56);
        }

        public void WriteUInt64(ulong value)
        {
            EnsureCapacity(8);
            _buffer[_position++] = (byte)value;
            _buffer[_position++] = (byte)(value >> 8);
            _buffer[_position++] = (byte)(value >> 16);
            _buffer[_position++] = (byte)(value >> 24);
            _buffer[_position++] = (byte)(value >> 32);
            _buffer[_position++] = (byte)(value >> 40);
            _buffer[_position++] = (byte)(value >> 48);
            _buffer[_position++] = (byte)(value >> 56);
        }

        public void WriteFloat(float value)
        {
            var bytes = BitConverter.GetBytes(value);
            EnsureCapacity(4);
            Buffer.BlockCopy(bytes, 0, _buffer, _position, 4);
            _position += 4;
        }

        public void WriteDouble(double value)
        {
            var bytes = BitConverter.GetBytes(value);
            EnsureCapacity(8);
            Buffer.BlockCopy(bytes, 0, _buffer, _position, 8);
            _position += 8;
        }

        #endregion

        #region Packed Integers

        /// <summary>Write uint as 1-5 bytes using 7-bit + continuation encoding.</summary>
        public void WritePackedUInt32(uint value)
        {
            EnsureCapacity(5);
            while (value > 0x7F)
            {
                _buffer[_position++] = (byte)(value | 0x80);
                value >>= 7;
            }
            _buffer[_position++] = (byte)value;
        }

        /// <summary>Write int as ZigZag-encoded packed uint.</summary>
        public void WritePackedInt32(int value)
        {
            WritePackedUInt32((uint)((value << 1) ^ (value >> 31)));
        }

        /// <summary>Write ulong as 1-10 bytes using 7-bit + continuation encoding.</summary>
        public void WritePackedUInt64(ulong value)
        {
            EnsureCapacity(10);
            while (value > 0x7F)
            {
                _buffer[_position++] = (byte)(value | 0x80);
                value >>= 7;
            }
            _buffer[_position++] = (byte)value;
        }

        #endregion

        #region Strings

        /// <summary>Write UTF-8 string with ushort byte-length prefix. Null/empty writes 0.</summary>
        public void WriteString(string value)
        {
            if (string.IsNullOrEmpty(value))
            {
                WriteUInt16(0);
                return;
            }

            int byteCount = Encoding.UTF8.GetByteCount(value);
            WriteUInt16((ushort)byteCount);
            EnsureCapacity(byteCount);
            Encoding.UTF8.GetBytes(value, 0, value.Length, _buffer, _position);
            _position += byteCount;
        }

        #endregion

        #region Unity Types

        public void WriteVector2(Vector2 value)
        {
            WriteFloat(value.x);
            WriteFloat(value.y);
        }

        public void WriteVector3(Vector3 value)
        {
            WriteFloat(value.x);
            WriteFloat(value.y);
            WriteFloat(value.z);
        }

        public void WriteQuaternion(Quaternion value)
        {
            WriteFloat(value.x);
            WriteFloat(value.y);
            WriteFloat(value.z);
            WriteFloat(value.w);
        }

        /// <summary>Write position as 6 bytes using half-precision floats.</summary>
        public void WriteVector3Half(Vector3 value)
        {
            var half = new Vector3Half(value);
            WriteUInt16(half.x);
            WriteUInt16(half.y);
            WriteUInt16(half.z);
        }

        /// <summary>Write pre-compressed rotation (from P2PSpringSync.CompressRotation).</summary>
        public void WriteCompressedRotation(uint compressed)
        {
            WriteUInt32(compressed);
        }

        public void WriteColor(Color value)
        {
            WriteFloat(value.r);
            WriteFloat(value.g);
            WriteFloat(value.b);
            WriteFloat(value.a);
        }

        public void WriteColor32(Color32 value)
        {
            EnsureCapacity(4);
            _buffer[_position++] = value.r;
            _buffer[_position++] = value.g;
            _buffer[_position++] = value.b;
            _buffer[_position++] = value.a;
        }

        #endregion

        #region Raw Bytes

        /// <summary>Write byte array with ushort length prefix.</summary>
        public void WriteBytes(byte[] value)
        {
            if (value == null || value.Length == 0)
            {
                WriteUInt16(0);
                return;
            }
            WriteUInt16((ushort)value.Length);
            WriteBytesRaw(value, 0, value.Length);
        }

        /// <summary>Write raw bytes without length prefix.</summary>
        public void WriteBytesRaw(byte[] value, int offset, int count)
        {
            if (count <= 0) return;
            EnsureCapacity(count);
            Buffer.BlockCopy(value, offset, _buffer, _position, count);
            _position += count;
        }

        /// <summary>Write raw bytes from ArraySegment without length prefix.</summary>
        public void WriteBytesRaw(ArraySegment<byte> value)
        {
            if (value.Count <= 0) return;
            EnsureCapacity(value.Count);
            Buffer.BlockCopy(value.Array, value.Offset, _buffer, _position, value.Count);
            _position += value.Count;
        }

        #endregion

        #region EOS Types

        /// <summary>Write ProductUserId as string.</summary>
        public void WriteProductUserId(ProductUserId value)
        {
            WriteString(value?.ToString());
        }

        #endregion
    }

    /// <summary>
    /// Object pool for NetWriter instances to avoid allocations.
    /// </summary>
    public static class NetWriterPool
    {
        private static readonly Stack<NetWriter> _pool = new();

        public static NetWriter Get()
        {
            if (_pool.Count > 0)
            {
                var writer = _pool.Pop();
                writer.Reset();
                return writer;
            }
            return new NetWriter();
        }

        public static void Return(NetWriter writer)
        {
            if (writer == null) return;
            writer.Reset();
            _pool.Push(writer);
        }
    }
}
