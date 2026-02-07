using System;
using NUnit.Framework;
using EOSNative.Net;
using EOSNative.P2P;
using UnityEngine;

namespace EOSNative.Tests
{
    public class NetSerializersTests
    {
        [Test]
        public void Byte_RoundTrip()
        {
            AssertRoundTrip<byte>(42);
        }

        [Test]
        public void Bool_RoundTrip()
        {
            AssertRoundTrip(true);
            AssertRoundTrip(false);
        }

        [Test]
        public void Short_RoundTrip()
        {
            AssertRoundTrip<short>(-1234);
        }

        [Test]
        public void UShort_RoundTrip()
        {
            AssertRoundTrip<ushort>(65535);
        }

        [Test]
        public void Int_RoundTrip()
        {
            AssertRoundTrip(-999999);
        }

        [Test]
        public void UInt_RoundTrip()
        {
            AssertRoundTrip(0xDEADBEEFu);
        }

        [Test]
        public void Long_RoundTrip()
        {
            AssertRoundTrip(long.MinValue);
        }

        [Test]
        public void ULong_RoundTrip()
        {
            AssertRoundTrip(ulong.MaxValue);
        }

        [Test]
        public void Float_RoundTrip()
        {
            AssertRoundTrip(3.14f);
        }

        [Test]
        public void Double_RoundTrip()
        {
            AssertRoundTrip(3.141592653589793);
        }

        [Test]
        public void String_RoundTrip()
        {
            AssertRoundTrip("Hello, World!");
        }

        [Test]
        public void String_Empty_RoundTrip()
        {
            var w = new NetWriter();
            NetSerializers.Write(w, string.Empty);
            var r = new NetReader(w.ToArray());
            Assert.AreEqual("", NetSerializers.Read<string>(r));
        }

        [Test]
        public void Vector2_RoundTrip()
        {
            var w = new NetWriter();
            var v = new Vector2(1.5f, -2.5f);
            NetSerializers.Write(w, v);
            var r = new NetReader(w.ToArray());
            var result = NetSerializers.Read<Vector2>(r);
            Assert.AreEqual(v.x, result.x, 0.001f);
            Assert.AreEqual(v.y, result.y, 0.001f);
        }

        [Test]
        public void Vector3_RoundTrip()
        {
            var w = new NetWriter();
            var v = new Vector3(1f, 2f, 3f);
            NetSerializers.Write(w, v);
            var r = new NetReader(w.ToArray());
            var result = NetSerializers.Read<Vector3>(r);
            Assert.AreEqual(v, result);
        }

        [Test]
        public void Quaternion_RoundTrip()
        {
            var q = Quaternion.Euler(30f, 60f, 90f);
            var w = new NetWriter();
            NetSerializers.Write(w, q);
            var r = new NetReader(w.ToArray());
            var result = NetSerializers.Read<Quaternion>(r);
            Assert.AreEqual(q.x, result.x, 0.0001f);
            Assert.AreEqual(q.y, result.y, 0.0001f);
            Assert.AreEqual(q.z, result.z, 0.0001f);
            Assert.AreEqual(q.w, result.w, 0.0001f);
        }

        [Test]
        public void Color_RoundTrip()
        {
            var w = new NetWriter();
            var c = new Color(0.1f, 0.2f, 0.3f, 0.4f);
            NetSerializers.Write(w, c);
            var r = new NetReader(w.ToArray());
            var result = NetSerializers.Read<Color>(r);
            Assert.AreEqual(c.r, result.r, 0.001f);
            Assert.AreEqual(c.g, result.g, 0.001f);
            Assert.AreEqual(c.b, result.b, 0.001f);
            Assert.AreEqual(c.a, result.a, 0.001f);
        }

        [Test]
        public void Color32_RoundTrip()
        {
            AssertRoundTrip(new Color32(10, 20, 30, 255));
        }

        [Test]
        public void ByteArray_RoundTrip()
        {
            var data = new byte[] { 1, 2, 3, 4, 5 };
            var w = new NetWriter();
            NetSerializers.Write(w, data);
            var r = new NetReader(w.ToArray());
            var result = NetSerializers.Read<byte[]>(r);
            Assert.AreEqual(data, result);
        }

        [Test]
        public void TypeId_Consistency()
        {
            Assert.IsTrue(NetSerializers.TryGetTypeId<byte>(out byte byteId));
            Assert.IsTrue(NetSerializers.TryGetTypeId<int>(out byte intId));
            Assert.IsTrue(NetSerializers.TryGetTypeId<string>(out byte strId));
            Assert.AreNotEqual(byteId, intId);
            Assert.AreNotEqual(intId, strId);
        }

        [Test]
        public void TypeId_ByType()
        {
            Assert.IsTrue(NetSerializers.TryGetTypeId(typeof(float), out byte floatId));
            Assert.IsTrue(NetSerializers.TryGetTypeId<float>(out byte genericId));
            Assert.AreEqual(floatId, genericId);
        }

        [Test]
        public void BoxedWrite_Read()
        {
            var w = new NetWriter();
            NetSerializers.WriteBoxed(w, typeof(int), 42);
            var r = new NetReader(w.ToArray());
            NetSerializers.TryGetTypeId<int>(out byte typeId);
            var result = NetSerializers.ReadBoxed(r, typeId);
            Assert.AreEqual(42, result);
        }

        [Test]
        public void INetSerializable_RoundTrip()
        {
            var original = new TestSerializable { X = 10, Y = 20 };
            var w = new NetWriter();
            NetSerializers.Write(w, original);
            var r = new NetReader(w.ToArray());
            var result = NetSerializers.Read<TestSerializable>(r);
            Assert.AreEqual(10, result.X);
            Assert.AreEqual(20, result.Y);
        }

        private struct TestSerializable : INetSerializable
        {
            public int X;
            public int Y;

            public void Serialize(NetWriter writer)
            {
                writer.WriteInt32(X);
                writer.WriteInt32(Y);
            }

            public void Deserialize(NetReader reader)
            {
                X = reader.ReadInt32();
                Y = reader.ReadInt32();
            }
        }

        #region Helpers

        private static void AssertRoundTrip<T>(T value)
        {
            var w = new NetWriter();
            NetSerializers.Write(w, value);
            var r = new NetReader(w.ToArray());
            var result = NetSerializers.Read<T>(r);
            Assert.AreEqual(value, result);
        }

        #endregion
    }
}
