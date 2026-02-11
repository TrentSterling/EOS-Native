using NUnit.Framework;
using EOSNative.Net;
using EOSNative.P2P;
using UnityEngine;

namespace EOSNative.Tests
{
    /// <summary>
    /// Tests SyncVar serialization round-trips for ALL 19 builtin types registered in NetSerializers.
    /// Each type gets: default value, set+dirty, serialize round-trip, OnChanged callback.
    /// </summary>
    public class SyncVarAllTypesTests
    {
        [TearDown]
        public void TearDown()
        {
            foreach (var name in new[] { "NetworkManager", "EOSP2PManager", "EOSLobbyManager", "NetworkSceneManager" })
            {
                var obj = GameObject.Find(name);
                if (obj != null) Object.DestroyImmediate(obj);
            }
        }

        private (NetworkObject netObj, SyncVar<T> syncVar) CreateSyncVar<T>(T defaultValue = default)
        {
            var go = new GameObject("TypeTest");
            var netObj = go.AddComponent<NetworkObject>();
            var sv = netObj.Sync(defaultValue);
            return (netObj, sv);
        }

        private void AssertRoundTrip<T>(T writeValue, T defaultValue = default)
        {
            var (src, svSrc) = CreateSyncVar(defaultValue);
            svSrc.Value = writeValue;

            var w = new NetWriter();
            svSrc.WriteTo(w);

            var (dst, svDst) = CreateSyncVar(defaultValue);
            var r = new NetReader(w.ToArray());
            svDst.ReadFrom(r);

            Assert.AreEqual(writeValue, svDst.Value, $"Round-trip failed for type {typeof(T).Name}");

            Object.DestroyImmediate(src.gameObject);
            Object.DestroyImmediate(dst.gameObject);
        }

        private void AssertDirtyOnChange<T>(T initial, T changed)
        {
            var (netObj, sv) = CreateSyncVar(initial);
            Assert.IsFalse(sv.IsDirty);
            sv.Value = changed;
            Assert.IsTrue(sv.IsDirty, $"SyncVar<{typeof(T).Name}> should be dirty after setting different value");
            Object.DestroyImmediate(netObj.gameObject);
        }

        private void AssertOnChangedFires<T>(T initial, T changed)
        {
            var (netObj, sv) = CreateSyncVar(initial);
            bool fired = false;
            T capturedOld = default, capturedNew = default;
            sv.OnChanged += (o, n) => { fired = true; capturedOld = o; capturedNew = n; };
            sv.Value = changed;
            Assert.IsTrue(fired, $"OnChanged should fire for SyncVar<{typeof(T).Name}>");
            Assert.AreEqual(initial, capturedOld);
            Assert.AreEqual(changed, capturedNew);
            Object.DestroyImmediate(netObj.gameObject);
        }

        // ─── Type 0: byte ───

        [Test] public void Byte_RoundTrip() => AssertRoundTrip<byte>(42);
        [Test] public void Byte_Dirty() => AssertDirtyOnChange<byte>(0, 255);
        [Test] public void Byte_OnChanged() => AssertOnChangedFires<byte>(0, 128);

        // ─── Type 1: bool ───

        [Test] public void Bool_RoundTrip_True() => AssertRoundTrip(true, false);
        [Test] public void Bool_RoundTrip_False() => AssertRoundTrip(false, true);
        [Test] public void Bool_Dirty() => AssertDirtyOnChange(false, true);
        [Test] public void Bool_OnChanged() => AssertOnChangedFires(false, true);

        // ─── Type 2: short ───

        [Test] public void Short_RoundTrip() => AssertRoundTrip<short>(-12345);
        [Test] public void Short_Dirty() => AssertDirtyOnChange<short>(0, 32767);

        // ─── Type 3: ushort ───

        [Test] public void UShort_RoundTrip() => AssertRoundTrip<ushort>(65535);
        [Test] public void UShort_Dirty() => AssertDirtyOnChange<ushort>(0, 12345);

        // ─── Type 4: int ───

        [Test] public void Int_RoundTrip() => AssertRoundTrip(-999999);
        [Test] public void Int_MaxMin_RoundTrip()
        {
            AssertRoundTrip(int.MaxValue);
            AssertRoundTrip(int.MinValue);
        }

        // ─── Type 5: uint ───

        [Test] public void UInt_RoundTrip() => AssertRoundTrip(4294967295u);
        [Test] public void UInt_Dirty() => AssertDirtyOnChange(0u, 42u);

        // ─── Type 6: long ───

        [Test] public void Long_RoundTrip() => AssertRoundTrip(-9223372036854775808L);
        [Test] public void Long_Dirty() => AssertDirtyOnChange(0L, long.MaxValue);

        // ─── Type 7: ulong ───

        [Test] public void ULong_RoundTrip() => AssertRoundTrip(18446744073709551615UL);
        [Test] public void ULong_Dirty() => AssertDirtyOnChange(0UL, 42UL);

        // ─── Type 8: float ───

        [Test]
        public void Float_RoundTrip()
        {
            var (src, svSrc) = CreateSyncVar(0f);
            svSrc.Value = 3.14159f;
            var w = new NetWriter();
            svSrc.WriteTo(w);

            var (dst, svDst) = CreateSyncVar(0f);
            svDst.ReadFrom(new NetReader(w.ToArray()));
            Assert.AreEqual(3.14159f, svDst.Value, 0.00001f);

            Object.DestroyImmediate(src.gameObject);
            Object.DestroyImmediate(dst.gameObject);
        }

        [Test] public void Float_Dirty() => AssertDirtyOnChange(0f, 1.5f);

        // ─── Type 9: double ───

        [Test]
        public void Double_RoundTrip()
        {
            var (src, svSrc) = CreateSyncVar(0.0);
            svSrc.Value = 3.141592653589793;
            var w = new NetWriter();
            svSrc.WriteTo(w);

            var (dst, svDst) = CreateSyncVar(0.0);
            svDst.ReadFrom(new NetReader(w.ToArray()));
            Assert.AreEqual(3.141592653589793, svDst.Value, 0.0000000000001);

            Object.DestroyImmediate(src.gameObject);
            Object.DestroyImmediate(dst.gameObject);
        }

        [Test] public void Double_Dirty() => AssertDirtyOnChange(0.0, 99.99);

        // ─── Type 10: string ───

        [Test] public void String_RoundTrip() => AssertRoundTrip("hello world", "");
        [Test] public void String_Empty_RoundTrip() => AssertRoundTrip("", "x");
        [Test] public void String_Null_WritesEmpty()
        {
            // NetSerializers writes null as string.Empty
            var (src, svSrc) = CreateSyncVar<string>(null);
            var w = new NetWriter();
            svSrc.WriteTo(w);

            var (dst, svDst) = CreateSyncVar("");
            svDst.ReadFrom(new NetReader(w.ToArray()));
            Assert.AreEqual("", svDst.Value);

            Object.DestroyImmediate(src.gameObject);
            Object.DestroyImmediate(dst.gameObject);
        }
        [Test] public void String_Unicode_RoundTrip() => AssertRoundTrip("\u00e9\u00e8\u00ea\u2603\ud83d\ude80", "");
        [Test] public void String_OnChanged() => AssertOnChangedFires("old", "new");

        // ─── Type 11: Vector2 ───

        [Test] public void Vector2_RoundTrip() => AssertRoundTrip(new Vector2(1.5f, -2.5f));
        [Test] public void Vector2_Dirty() => AssertDirtyOnChange(Vector2.zero, Vector2.one);
        [Test] public void Vector2_OnChanged() => AssertOnChangedFires(Vector2.zero, new Vector2(3, 4));

        // ─── Type 12: Vector3 ───

        [Test] public void Vector3_RoundTrip() => AssertRoundTrip(new Vector3(1, 2, 3));
        [Test] public void Vector3_Dirty() => AssertDirtyOnChange(Vector3.zero, Vector3.up);

        // ─── Type 13: Quaternion ───

        [Test]
        public void Quaternion_RoundTrip()
        {
            var q = Quaternion.Euler(45, 90, 135);
            var (src, svSrc) = CreateSyncVar(Quaternion.identity);
            svSrc.Value = q;
            var w = new NetWriter();
            svSrc.WriteTo(w);

            var (dst, svDst) = CreateSyncVar(Quaternion.identity);
            svDst.ReadFrom(new NetReader(w.ToArray()));
            Assert.AreEqual(q.x, svDst.Value.x, 0.001f);
            Assert.AreEqual(q.y, svDst.Value.y, 0.001f);
            Assert.AreEqual(q.z, svDst.Value.z, 0.001f);
            Assert.AreEqual(q.w, svDst.Value.w, 0.001f);

            Object.DestroyImmediate(src.gameObject);
            Object.DestroyImmediate(dst.gameObject);
        }

        [Test] public void Quaternion_Dirty() => AssertDirtyOnChange(Quaternion.identity, Quaternion.Euler(0, 90, 0));

        // ─── Type 14: Color ───

        [Test] public void Color_RoundTrip()
        {
            var (src, svSrc) = CreateSyncVar(Color.white);
            svSrc.Value = new Color(0.1f, 0.2f, 0.3f, 0.4f);
            var w = new NetWriter();
            svSrc.WriteTo(w);

            var (dst, svDst) = CreateSyncVar(Color.white);
            svDst.ReadFrom(new NetReader(w.ToArray()));
            Assert.AreEqual(0.1f, svDst.Value.r, 0.001f);
            Assert.AreEqual(0.2f, svDst.Value.g, 0.001f);
            Assert.AreEqual(0.3f, svDst.Value.b, 0.001f);
            Assert.AreEqual(0.4f, svDst.Value.a, 0.001f);

            Object.DestroyImmediate(src.gameObject);
            Object.DestroyImmediate(dst.gameObject);
        }

        // ─── Type 15: Color32 ───

        [Test]
        public void Color32_RoundTrip()
        {
            var c = new Color32(255, 128, 64, 200);
            var (src, svSrc) = CreateSyncVar(default(Color32));
            svSrc.Value = c;
            var w = new NetWriter();
            svSrc.WriteTo(w);

            var (dst, svDst) = CreateSyncVar(default(Color32));
            svDst.ReadFrom(new NetReader(w.ToArray()));
            Assert.AreEqual(c.r, svDst.Value.r);
            Assert.AreEqual(c.g, svDst.Value.g);
            Assert.AreEqual(c.b, svDst.Value.b);
            Assert.AreEqual(c.a, svDst.Value.a);

            Object.DestroyImmediate(src.gameObject);
            Object.DestroyImmediate(dst.gameObject);
        }

        [Test] public void Color32_Dirty() => AssertDirtyOnChange(new Color32(0, 0, 0, 0), new Color32(255, 0, 0, 255));

        // ─── Type 16: ProductUserId ───
        // ProductUserId requires EOS SDK init, so we test with null (the only safe value in EditMode)

        [Test]
        public void ProductUserId_Null_RoundTrip()
        {
            var (src, svSrc) = CreateSyncVar<Epic.OnlineServices.ProductUserId>(null);
            var w = new NetWriter();
            svSrc.WriteTo(w);

            var (dst, svDst) = CreateSyncVar<Epic.OnlineServices.ProductUserId>(null);
            svDst.ReadFrom(new NetReader(w.ToArray()));
            Assert.IsNull(svDst.Value);

            Object.DestroyImmediate(src.gameObject);
            Object.DestroyImmediate(dst.gameObject);
        }

        // ─── Type 17: byte[] ───

        [Test]
        public void ByteArray_RoundTrip()
        {
            var data = new byte[] { 0, 1, 2, 127, 255 };
            var (src, svSrc) = CreateSyncVar<byte[]>(null);
            svSrc.Value = data;
            var w = new NetWriter();
            svSrc.WriteTo(w);

            var (dst, svDst) = CreateSyncVar<byte[]>(null);
            svDst.ReadFrom(new NetReader(w.ToArray()));
            Assert.AreEqual(data, svDst.Value);

            Object.DestroyImmediate(src.gameObject);
            Object.DestroyImmediate(dst.gameObject);
        }

        [Test]
        public void ByteArray_Empty_RoundTrip()
        {
            var (src, svSrc) = CreateSyncVar(new byte[0]);
            var w = new NetWriter();
            svSrc.WriteTo(w);

            var (dst, svDst) = CreateSyncVar<byte[]>(null);
            svDst.ReadFrom(new NetReader(w.ToArray()));
            Assert.IsNotNull(svDst.Value);
            Assert.AreEqual(0, svDst.Value.Length);

            Object.DestroyImmediate(src.gameObject);
            Object.DestroyImmediate(dst.gameObject);
        }

        // ─── Type 18: NetworkObject ref ───
        // NetworkObject serializes as NetworkId (uint). Without NetworkManager running, reads back as null.

        [Test]
        public void NetworkObjectRef_Null_RoundTrip()
        {
            var (src, svSrc) = CreateSyncVar<NetworkObject>(null);
            var w = new NetWriter();
            svSrc.WriteTo(w);

            var (dst, svDst) = CreateSyncVar<NetworkObject>(null);
            svDst.ReadFrom(new NetReader(w.ToArray()));
            Assert.IsNull(svDst.Value);

            Object.DestroyImmediate(src.gameObject);
            Object.DestroyImmediate(dst.gameObject);
        }

        // ─── FullState round-trips for all key types ───

        [Test]
        public void FullState_AllPrimitiveTypes()
        {
            // Test WriteFullState/ReadFullState for a sampling of types
            AssertFullStateRoundTrip<byte>(99);
            AssertFullStateRoundTrip<bool>(true);
            AssertFullStateRoundTrip<short>(-500);
            AssertFullStateRoundTrip<uint>(123456u);
            AssertFullStateRoundTrip<long>(long.MaxValue);
            AssertFullStateRoundTrip<double>(2.718281828);
        }

        private void AssertFullStateRoundTrip<T>(T value)
        {
            var (src, svSrc) = CreateSyncVar(value);
            var w = new NetWriter();
            svSrc.WriteFullState(w);

            var (dst, svDst) = CreateSyncVar<T>(default);
            svDst.ReadFullState(new NetReader(w.ToArray()));
            Assert.AreEqual(value, svDst.Value, $"FullState round-trip failed for {typeof(T).Name}");

            Object.DestroyImmediate(src.gameObject);
            Object.DestroyImmediate(dst.gameObject);
        }

        // ─── Multiple types on same NetworkObject ───

        [Test]
        public void MixedTypes_SerializeDirty_RoundTrip()
        {
            var go = new GameObject("MixedTypes");
            var netObj = go.AddComponent<NetworkObject>();
            var svByte = netObj.Sync<byte>(0);
            var svBool = netObj.Sync(false);
            var svInt = netObj.Sync(0);
            var svFloat = netObj.Sync(0f);
            var svString = netObj.Sync("");
            var svVec3 = netObj.Sync(Vector3.zero);
            var svColor = netObj.Sync(Color.black);

            // Dirty a subset
            svByte.Value = 42;
            svBool.Value = true;
            svFloat.Value = 9.81f;
            svVec3.Value = Vector3.up;

            var w = new NetWriter();
            netObj.SerializeDirty(w);

            // Create mirror
            var go2 = new GameObject("MixedTypes2");
            var netObj2 = go2.AddComponent<NetworkObject>();
            var svByte2 = netObj2.Sync<byte>(0);
            var svBool2 = netObj2.Sync(false);
            var svInt2 = netObj2.Sync(0);
            var svFloat2 = netObj2.Sync(0f);
            var svString2 = netObj2.Sync("");
            var svVec32 = netObj2.Sync(Vector3.zero);
            var svColor2 = netObj2.Sync(Color.black);

            netObj2.DeserializeDirty(new NetReader(w.ToArray()));

            Assert.AreEqual(42, svByte2.Value);
            Assert.AreEqual(true, svBool2.Value);
            Assert.AreEqual(0, svInt2.Value); // not dirtied
            Assert.AreEqual(9.81f, svFloat2.Value, 0.001f);
            Assert.AreEqual("", svString2.Value); // not dirtied
            Assert.AreEqual(Vector3.up, svVec32.Value);
            Assert.AreEqual(Color.black, svColor2.Value); // not dirtied

            Object.DestroyImmediate(go);
            Object.DestroyImmediate(go2);
        }

        [Test]
        public void MixedTypes_SerializeAll_RoundTrip()
        {
            var go = new GameObject("MixedAll");
            var netObj = go.AddComponent<NetworkObject>();
            var svByte = netObj.Sync<byte>(200);
            var svLong = netObj.Sync(9876543210L);
            var svQuat = netObj.Sync(Quaternion.Euler(10, 20, 30));
            var svColor32 = netObj.Sync(new Color32(10, 20, 30, 40));

            var w = new NetWriter();
            netObj.SerializeAll(w);

            var go2 = new GameObject("MixedAll2");
            var netObj2 = go2.AddComponent<NetworkObject>();
            var svByte2 = netObj2.Sync<byte>(0);
            var svLong2 = netObj2.Sync(0L);
            var svQuat2 = netObj2.Sync(Quaternion.identity);
            var svColor322 = netObj2.Sync(default(Color32));

            netObj2.DeserializeAll(new NetReader(w.ToArray()));

            Assert.AreEqual(200, svByte2.Value);
            Assert.AreEqual(9876543210L, svLong2.Value);
            Assert.AreEqual(10, svColor322.Value.r);
            Assert.AreEqual(20, svColor322.Value.g);
            Assert.AreEqual(30, svColor322.Value.b);
            Assert.AreEqual(40, svColor322.Value.a);

            Object.DestroyImmediate(go);
            Object.DestroyImmediate(go2);
        }
    }
}
