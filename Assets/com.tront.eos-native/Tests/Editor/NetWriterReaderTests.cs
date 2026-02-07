using System;
using NUnit.Framework;
using EOSNative.P2P;
using UnityEngine;

namespace EOSNative.Tests
{
    public class NetWriterReaderTests
    {
        #region Primitives

        [Test]
        public void Byte_RoundTrip()
        {
            var w = new NetWriter();
            w.WriteByte(0);
            w.WriteByte(127);
            w.WriteByte(255);
            var r = new NetReader(w.ToArray());
            Assert.AreEqual(0, r.ReadByte());
            Assert.AreEqual(127, r.ReadByte());
            Assert.AreEqual(255, r.ReadByte());
        }

        [Test]
        public void Bool_RoundTrip()
        {
            var w = new NetWriter();
            w.WriteBool(true);
            w.WriteBool(false);
            var r = new NetReader(w.ToArray());
            Assert.IsTrue(r.ReadBool());
            Assert.IsFalse(r.ReadBool());
        }

        [Test]
        public void Int16_RoundTrip()
        {
            var w = new NetWriter();
            w.WriteInt16(short.MinValue);
            w.WriteInt16(0);
            w.WriteInt16(short.MaxValue);
            var r = new NetReader(w.ToArray());
            Assert.AreEqual(short.MinValue, r.ReadInt16());
            Assert.AreEqual(0, r.ReadInt16());
            Assert.AreEqual(short.MaxValue, r.ReadInt16());
        }

        [Test]
        public void UInt16_RoundTrip()
        {
            var w = new NetWriter();
            w.WriteUInt16(0);
            w.WriteUInt16(ushort.MaxValue);
            var r = new NetReader(w.ToArray());
            Assert.AreEqual(0, r.ReadUInt16());
            Assert.AreEqual(ushort.MaxValue, r.ReadUInt16());
        }

        [Test]
        public void Int32_RoundTrip()
        {
            var w = new NetWriter();
            w.WriteInt32(int.MinValue);
            w.WriteInt32(0);
            w.WriteInt32(int.MaxValue);
            w.WriteInt32(-42);
            var r = new NetReader(w.ToArray());
            Assert.AreEqual(int.MinValue, r.ReadInt32());
            Assert.AreEqual(0, r.ReadInt32());
            Assert.AreEqual(int.MaxValue, r.ReadInt32());
            Assert.AreEqual(-42, r.ReadInt32());
        }

        [Test]
        public void UInt32_RoundTrip()
        {
            var w = new NetWriter();
            w.WriteUInt32(0);
            w.WriteUInt32(uint.MaxValue);
            w.WriteUInt32(0xDEADBEEF);
            var r = new NetReader(w.ToArray());
            Assert.AreEqual(0u, r.ReadUInt32());
            Assert.AreEqual(uint.MaxValue, r.ReadUInt32());
            Assert.AreEqual(0xDEADBEEFu, r.ReadUInt32());
        }

        [Test]
        public void Int64_RoundTrip()
        {
            var w = new NetWriter();
            w.WriteInt64(long.MinValue);
            w.WriteInt64(0);
            w.WriteInt64(long.MaxValue);
            var r = new NetReader(w.ToArray());
            Assert.AreEqual(long.MinValue, r.ReadInt64());
            Assert.AreEqual(0L, r.ReadInt64());
            Assert.AreEqual(long.MaxValue, r.ReadInt64());
        }

        [Test]
        public void UInt64_RoundTrip()
        {
            var w = new NetWriter();
            w.WriteUInt64(0);
            w.WriteUInt64(ulong.MaxValue);
            var r = new NetReader(w.ToArray());
            Assert.AreEqual(0UL, r.ReadUInt64());
            Assert.AreEqual(ulong.MaxValue, r.ReadUInt64());
        }

        [Test]
        public void Float_RoundTrip()
        {
            var w = new NetWriter();
            w.WriteFloat(0f);
            w.WriteFloat(3.14159f);
            w.WriteFloat(-1.5f);
            w.WriteFloat(float.MaxValue);
            var r = new NetReader(w.ToArray());
            Assert.AreEqual(0f, r.ReadFloat());
            Assert.AreEqual(3.14159f, r.ReadFloat(), 0.00001f);
            Assert.AreEqual(-1.5f, r.ReadFloat());
            Assert.AreEqual(float.MaxValue, r.ReadFloat());
        }

        [Test]
        public void Double_RoundTrip()
        {
            var w = new NetWriter();
            w.WriteDouble(0.0);
            w.WriteDouble(3.141592653589793);
            w.WriteDouble(double.MaxValue);
            var r = new NetReader(w.ToArray());
            Assert.AreEqual(0.0, r.ReadDouble());
            Assert.AreEqual(3.141592653589793, r.ReadDouble(), 0.0000000001);
            Assert.AreEqual(double.MaxValue, r.ReadDouble());
        }

        #endregion

        #region Packed Integers

        [Test]
        public void PackedUInt32_SmallValues()
        {
            var w = new NetWriter();
            w.WritePackedUInt32(0);
            w.WritePackedUInt32(1);
            w.WritePackedUInt32(127);
            Assert.AreEqual(3, w.Position, "Values 0-127 should each take 1 byte");
            var r = new NetReader(w.ToArray());
            Assert.AreEqual(0u, r.ReadPackedUInt32());
            Assert.AreEqual(1u, r.ReadPackedUInt32());
            Assert.AreEqual(127u, r.ReadPackedUInt32());
        }

        [Test]
        public void PackedUInt32_LargeValues()
        {
            var w = new NetWriter();
            w.WritePackedUInt32(128);
            w.WritePackedUInt32(16383);
            w.WritePackedUInt32(uint.MaxValue);
            var r = new NetReader(w.ToArray());
            Assert.AreEqual(128u, r.ReadPackedUInt32());
            Assert.AreEqual(16383u, r.ReadPackedUInt32());
            Assert.AreEqual(uint.MaxValue, r.ReadPackedUInt32());
        }

        [Test]
        public void PackedInt32_Negative()
        {
            var w = new NetWriter();
            w.WritePackedInt32(0);
            w.WritePackedInt32(-1);
            w.WritePackedInt32(1);
            w.WritePackedInt32(-42);
            w.WritePackedInt32(int.MinValue);
            w.WritePackedInt32(int.MaxValue);
            var r = new NetReader(w.ToArray());
            Assert.AreEqual(0, r.ReadPackedInt32());
            Assert.AreEqual(-1, r.ReadPackedInt32());
            Assert.AreEqual(1, r.ReadPackedInt32());
            Assert.AreEqual(-42, r.ReadPackedInt32());
            Assert.AreEqual(int.MinValue, r.ReadPackedInt32());
            Assert.AreEqual(int.MaxValue, r.ReadPackedInt32());
        }

        [Test]
        public void PackedUInt64_RoundTrip()
        {
            var w = new NetWriter();
            w.WritePackedUInt64(0);
            w.WritePackedUInt64(127);
            w.WritePackedUInt64(ulong.MaxValue);
            var r = new NetReader(w.ToArray());
            Assert.AreEqual(0UL, r.ReadPackedUInt64());
            Assert.AreEqual(127UL, r.ReadPackedUInt64());
            Assert.AreEqual(ulong.MaxValue, r.ReadPackedUInt64());
        }

        #endregion

        #region Strings

        [Test]
        public void String_RoundTrip()
        {
            var w = new NetWriter();
            w.WriteString("Hello, World!");
            var r = new NetReader(w.ToArray());
            Assert.AreEqual("Hello, World!", r.ReadString());
        }

        [Test]
        public void String_Empty()
        {
            var w = new NetWriter();
            w.WriteString("");
            w.WriteString(null);
            var r = new NetReader(w.ToArray());
            Assert.AreEqual("", r.ReadString());
            Assert.AreEqual("", r.ReadString());
        }

        [Test]
        public void String_Unicode()
        {
            var w = new NetWriter();
            w.WriteString("Hello \u00e9\u00e8\u00ea \u4e16\u754c");
            var r = new NetReader(w.ToArray());
            Assert.AreEqual("Hello \u00e9\u00e8\u00ea \u4e16\u754c", r.ReadString());
        }

        #endregion

        #region Unity Types

        [Test]
        public void Vector2_RoundTrip()
        {
            var w = new NetWriter();
            w.WriteVector2(new Vector2(1.5f, -3.7f));
            var r = new NetReader(w.ToArray());
            var v = r.ReadVector2();
            Assert.AreEqual(1.5f, v.x, 0.001f);
            Assert.AreEqual(-3.7f, v.y, 0.001f);
        }

        [Test]
        public void Vector3_RoundTrip()
        {
            var w = new NetWriter();
            w.WriteVector3(new Vector3(1f, 2f, 3f));
            var r = new NetReader(w.ToArray());
            var v = r.ReadVector3();
            Assert.AreEqual(1f, v.x);
            Assert.AreEqual(2f, v.y);
            Assert.AreEqual(3f, v.z);
        }

        [Test]
        public void Quaternion_RoundTrip()
        {
            var q = Quaternion.Euler(45f, 90f, 180f);
            var w = new NetWriter();
            w.WriteQuaternion(q);
            var r = new NetReader(w.ToArray());
            var result = r.ReadQuaternion();
            Assert.AreEqual(q.x, result.x, 0.0001f);
            Assert.AreEqual(q.y, result.y, 0.0001f);
            Assert.AreEqual(q.z, result.z, 0.0001f);
            Assert.AreEqual(q.w, result.w, 0.0001f);
        }

        [Test]
        public void Color_RoundTrip()
        {
            var w = new NetWriter();
            w.WriteColor(new Color(0.1f, 0.2f, 0.3f, 0.4f));
            var r = new NetReader(w.ToArray());
            var c = r.ReadColor();
            Assert.AreEqual(0.1f, c.r, 0.001f);
            Assert.AreEqual(0.2f, c.g, 0.001f);
            Assert.AreEqual(0.3f, c.b, 0.001f);
            Assert.AreEqual(0.4f, c.a, 0.001f);
        }

        [Test]
        public void Color32_RoundTrip()
        {
            var w = new NetWriter();
            w.WriteColor32(new Color32(10, 20, 30, 40));
            var r = new NetReader(w.ToArray());
            var c = r.ReadColor32();
            Assert.AreEqual(10, c.r);
            Assert.AreEqual(20, c.g);
            Assert.AreEqual(30, c.b);
            Assert.AreEqual(40, c.a);
        }

        #endregion

        #region Byte Arrays

        [Test]
        public void ByteArray_RoundTrip()
        {
            var data = new byte[] { 1, 2, 3, 4, 5 };
            var w = new NetWriter();
            w.WriteBytes(data);
            var r = new NetReader(w.ToArray());
            var result = r.ReadBytes();
            Assert.AreEqual(data, result);
        }

        [Test]
        public void ByteArray_Empty()
        {
            var w = new NetWriter();
            w.WriteBytes(null);
            w.WriteBytes(new byte[0]);
            var r = new NetReader(w.ToArray());
            Assert.AreEqual(0, r.ReadBytes().Length);
            Assert.AreEqual(0, r.ReadBytes().Length);
        }

        #endregion

        #region Buffer Management

        [Test]
        public void AutoGrow_LargeWrite()
        {
            var w = new NetWriter(4); // Start with 4 bytes
            for (int i = 0; i < 100; i++)
                w.WriteInt32(i);
            Assert.AreEqual(400, w.Position);
            Assert.GreaterOrEqual(w.Capacity, 400);

            var r = new NetReader(w.ToArray());
            for (int i = 0; i < 100; i++)
                Assert.AreEqual(i, r.ReadInt32());
        }

        [Test]
        public void Reset_ClearsPosition()
        {
            var w = new NetWriter();
            w.WriteInt32(42);
            Assert.AreEqual(4, w.Position);
            w.Reset();
            Assert.AreEqual(0, w.Position);
        }

        [Test]
        public void ToArraySegment_SharesBuffer()
        {
            var w = new NetWriter();
            w.WriteInt32(42);
            var segment = w.ToArraySegment();
            Assert.AreEqual(4, segment.Count);
            Assert.AreEqual(0, segment.Offset);
        }

        [Test]
        public void ToArray_IsCopy()
        {
            var w = new NetWriter();
            w.WriteInt32(42);
            var arr = w.ToArray();
            Assert.AreEqual(4, arr.Length);
            // Modifying copy shouldn't affect writer
            arr[0] = 0xFF;
            var segment = w.ToArraySegment();
            Assert.AreNotEqual(0xFF, segment.Array[0]);
        }

        #endregion

        #region Pool

        [Test]
        public void Pool_GetAndReturn()
        {
            var w1 = NetWriterPool.Get();
            w1.WriteInt32(42);
            Assert.AreEqual(4, w1.Position);
            NetWriterPool.Return(w1);

            // Getting again should return a reset writer (possibly the same instance)
            var w2 = NetWriterPool.Get();
            Assert.AreEqual(0, w2.Position);
            NetWriterPool.Return(w2);
        }

        #endregion

        #region Reader Bounds

        [Test]
        public void Reader_Underflow_Throws()
        {
            var r = new NetReader(new byte[] { 1 });
            r.ReadByte(); // OK
            Assert.Throws<InvalidOperationException>(() => r.ReadByte());
        }

        [Test]
        public void Reader_Remaining()
        {
            var r = new NetReader(new byte[] { 1, 2, 3, 4 });
            Assert.AreEqual(4, r.Remaining);
            r.ReadByte();
            Assert.AreEqual(3, r.Remaining);
        }

        [Test]
        public void Reader_Skip()
        {
            var w = new NetWriter();
            w.WriteInt32(1);
            w.WriteInt32(42);
            var r = new NetReader(w.ToArray());
            r.Skip(4); // Skip first int
            Assert.AreEqual(42, r.ReadInt32());
        }

        [Test]
        public void Reader_WithOffset()
        {
            var data = new byte[] { 0xFF, 0xFF, 42, 0 };
            var r = new NetReader(data, 2, 2);
            Assert.AreEqual(42, r.ReadByte());
            Assert.AreEqual(0, r.ReadByte());
        }

        #endregion
    }
}
