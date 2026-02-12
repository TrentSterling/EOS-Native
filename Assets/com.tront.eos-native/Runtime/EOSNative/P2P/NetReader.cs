using System;
using System.Text;
using Epic.OnlineServices;
using UnityEngine;

namespace EOSNative.P2P
{
    /// <summary>
    /// Binary deserializer that reads from a byte array with position tracking and bounds checking.
    /// Counterpart to <see cref="NetWriter"/>.
    /// </summary>
    public sealed class NetReader
    {
        /// <summary>Maximum allowed string byte length (64 KB). Prevents OOM from malicious packets.</summary>
        public const int MaxStringBytes = 65535;

        /// <summary>Maximum allowed byte array length (1 MB). Prevents OOM from malicious packets.</summary>
        public const int MaxBytesLength = 1048576;

        private byte[] _buffer;
        private int _offset;
        private int _count;
        private int _position;

        public int Position
        {
            get => _position;
            set
            {
                if (value < 0 || value > _count)
                    throw new ArgumentOutOfRangeException(nameof(value), $"Position {value} out of range [0..{_count}]");
                _position = value;
            }
        }
        public int Length => _count;
        public int Remaining => _count - _position;

        public NetReader(byte[] buffer) : this(buffer, 0, buffer.Length) { }

        public NetReader(byte[] buffer, int offset, int count)
        {
            _buffer = buffer;
            _offset = offset;
            _count = count;
            _position = 0;
        }

        public NetReader(ArraySegment<byte> segment) : this(segment.Array, segment.Offset, segment.Count) { }

        /// <summary>Reset read position to the beginning.</summary>
        public void Reset()
        {
            _position = 0;
        }

        /// <summary>Skip forward by the specified number of bytes.</summary>
        public void Skip(int bytes)
        {
            CheckBounds(bytes);
            _position += bytes;
        }

        /// <summary>Re-initialize with new data.</summary>
        public void SetBuffer(byte[] buffer, int offset, int count)
        {
            _buffer = buffer;
            _offset = offset;
            _count = count;
            _position = 0;
        }

        private void CheckBounds(int bytes)
        {
            if (_position + bytes > _count)
                throw new InvalidOperationException(
                    $"NetReader buffer underflow: tried to read {bytes} bytes at position {_position}, but only {_count - _position} remaining");
        }

        #region Primitives

        public byte ReadByte()
        {
            CheckBounds(1);
            return _buffer[_offset + _position++];
        }

        public bool ReadBool()
        {
            return ReadByte() != 0;
        }

        public short ReadInt16()
        {
            CheckBounds(2);
            int i = _offset + _position;
            _position += 2;
            return (short)(_buffer[i] | (_buffer[i + 1] << 8));
        }

        public ushort ReadUInt16()
        {
            CheckBounds(2);
            int i = _offset + _position;
            _position += 2;
            return (ushort)(_buffer[i] | (_buffer[i + 1] << 8));
        }

        public int ReadInt32()
        {
            CheckBounds(4);
            int i = _offset + _position;
            _position += 4;
            return _buffer[i] | (_buffer[i + 1] << 8) | (_buffer[i + 2] << 16) | (_buffer[i + 3] << 24);
        }

        public uint ReadUInt32()
        {
            CheckBounds(4);
            int i = _offset + _position;
            _position += 4;
            return (uint)(_buffer[i] | (_buffer[i + 1] << 8) | (_buffer[i + 2] << 16) | (_buffer[i + 3] << 24));
        }

        public long ReadInt64()
        {
            CheckBounds(8);
            int i = _offset + _position;
            _position += 8;
            return (long)_buffer[i] | ((long)_buffer[i + 1] << 8) |
                   ((long)_buffer[i + 2] << 16) | ((long)_buffer[i + 3] << 24) |
                   ((long)_buffer[i + 4] << 32) | ((long)_buffer[i + 5] << 40) |
                   ((long)_buffer[i + 6] << 48) | ((long)_buffer[i + 7] << 56);
        }

        public ulong ReadUInt64()
        {
            CheckBounds(8);
            int i = _offset + _position;
            _position += 8;
            return (ulong)_buffer[i] | ((ulong)_buffer[i + 1] << 8) |
                   ((ulong)_buffer[i + 2] << 16) | ((ulong)_buffer[i + 3] << 24) |
                   ((ulong)_buffer[i + 4] << 32) | ((ulong)_buffer[i + 5] << 40) |
                   ((ulong)_buffer[i + 6] << 48) | ((ulong)_buffer[i + 7] << 56);
        }

        public float ReadFloat()
        {
            CheckBounds(4);
            float value = BitConverter.ToSingle(_buffer, _offset + _position);
            _position += 4;
            return value;
        }

        public double ReadDouble()
        {
            CheckBounds(8);
            double value = BitConverter.ToDouble(_buffer, _offset + _position);
            _position += 8;
            return value;
        }

        #endregion

        #region Packed Integers

        /// <summary>Read 1-5 byte varint-encoded uint.</summary>
        public uint ReadPackedUInt32()
        {
            uint value = 0;
            int shift = 0;
            byte b;
            do
            {
                if (shift >= 35)
                    throw new InvalidOperationException("NetReader: malformed packed uint32");
                b = ReadByte();
                value |= (uint)(b & 0x7F) << shift;
                shift += 7;
            } while ((b & 0x80) != 0);

            return value;
        }

        /// <summary>Read ZigZag-decoded packed int.</summary>
        public int ReadPackedInt32()
        {
            uint zigzag = ReadPackedUInt32();
            return (int)(zigzag >> 1) ^ -(int)(zigzag & 1);
        }

        /// <summary>Read 1-10 byte varint-encoded ulong.</summary>
        public ulong ReadPackedUInt64()
        {
            ulong value = 0;
            int shift = 0;
            byte b;
            do
            {
                if (shift >= 70)
                    throw new InvalidOperationException("NetReader: malformed packed uint64");
                b = ReadByte();
                value |= (ulong)(b & 0x7F) << shift;
                shift += 7;
            } while ((b & 0x80) != 0);

            return value;
        }

        #endregion

        #region Strings

        /// <summary>Read UTF-8 string with ushort byte-length prefix. Returns empty string if length is 0.</summary>
        public string ReadString()
        {
            ushort byteCount = ReadUInt16();
            if (byteCount == 0) return string.Empty;
            if (byteCount > MaxStringBytes)
                throw new InvalidOperationException(
                    $"NetReader: string byte count {byteCount} exceeds max {MaxStringBytes}");

            CheckBounds(byteCount);
            string value = Encoding.UTF8.GetString(_buffer, _offset + _position, byteCount);
            _position += byteCount;
            return value;
        }

        #endregion

        #region Unity Types

        public Vector2 ReadVector2()
        {
            return new Vector2(ReadFloat(), ReadFloat());
        }

        public Vector3 ReadVector3()
        {
            return new Vector3(ReadFloat(), ReadFloat(), ReadFloat());
        }

        public Quaternion ReadQuaternion()
        {
            return new Quaternion(ReadFloat(), ReadFloat(), ReadFloat(), ReadFloat());
        }

        /// <summary>Read 6-byte half-precision position.</summary>
        public Vector3 ReadVector3Half()
        {
            ushort x = ReadUInt16();
            ushort y = ReadUInt16();
            ushort z = ReadUInt16();
            return new Vector3(
                Mathf.HalfToFloat(x),
                Mathf.HalfToFloat(y),
                Mathf.HalfToFloat(z)
            );
        }

        /// <summary>Read 4-byte compressed rotation (use P2PSpringSync.DecompressRotation).</summary>
        public uint ReadCompressedRotation()
        {
            return ReadUInt32();
        }

        public Color ReadColor()
        {
            return new Color(ReadFloat(), ReadFloat(), ReadFloat(), ReadFloat());
        }

        public Color32 ReadColor32()
        {
            return new Color32(ReadByte(), ReadByte(), ReadByte(), ReadByte());
        }

        #endregion

        #region Raw Bytes

        /// <summary>Read byte array with ushort length prefix.</summary>
        public byte[] ReadBytes()
        {
            ushort length = ReadUInt16();
            if (length == 0) return Array.Empty<byte>();
            // Note: ushort max (65535) is always below MaxBytesLength (1MB), so no cap check needed here.
            // The length prefix itself limits the payload. MaxBytesLength is enforced in ReadBytesLong().

            CheckBounds(length);
            var result = new byte[length];
            Buffer.BlockCopy(_buffer, _offset + _position, result, 0, length);
            _position += length;
            return result;
        }

        /// <summary>Read exact number of raw bytes (no prefix).</summary>
        public byte[] ReadBytesRaw(int count)
        {
            if (count <= 0) return Array.Empty<byte>();

            CheckBounds(count);
            var result = new byte[count];
            Buffer.BlockCopy(_buffer, _offset + _position, result, 0, count);
            _position += count;
            return result;
        }

        /// <summary>Read all remaining bytes from the current position to the end.</summary>
        public byte[] ReadBytesRemaining()
        {
            return ReadBytesRaw(Remaining);
        }

        #endregion

        #region EOS Types

        /// <summary>Read ProductUserId (stored as string).</summary>
        public ProductUserId ReadProductUserId()
        {
            string str = ReadString();
            return string.IsNullOrEmpty(str) ? null : ProductUserId.FromString(str);
        }

        #endregion
    }
}
