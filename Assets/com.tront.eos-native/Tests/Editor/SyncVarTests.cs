using NUnit.Framework;
using EOSNative.Net;
using EOSNative.P2P;
using UnityEngine;

namespace EOSNative.Tests
{
    public class SyncVarTests
    {
        [TearDown]
        public void TearDown()
        {
            // Destroy any auto-created singletons leaked by NetworkObject/NetworkManager
            foreach (var name in new[] { "NetworkManager", "EOSP2PManager", "EOSLobbyManager", "NetworkSceneManager" })
            {
                var obj = GameObject.Find(name);
                if (obj != null) Object.DestroyImmediate(obj);
            }
        }

        // Helper: create a SyncVar<T> via NetworkObject (which is its only valid constructor path)
        // Since NetworkObject is a MonoBehaviour, we create a temporary GameObject
        private (NetworkObject netObj, SyncVar<T> syncVar) CreateSyncVar<T>(T defaultValue = default)
        {
            var go = new GameObject("TestNetObj");
            var netObj = go.AddComponent<NetworkObject>();
            var sv = netObj.Sync(defaultValue);
            return (netObj, sv);
        }

        private void Cleanup(NetworkObject netObj)
        {
            Object.DestroyImmediate(netObj.gameObject);
        }

        #region Basic

        [Test]
        public void DefaultValue()
        {
            var (netObj, sv) = CreateSyncVar(42);
            Assert.AreEqual(42, sv.Value);
            Cleanup(netObj);
        }

        [Test]
        public void SetValue_MarksDirty()
        {
            var (netObj, sv) = CreateSyncVar(0);
            Assert.IsFalse(sv.IsDirty);
            sv.Value = 10;
            Assert.IsTrue(sv.IsDirty);
            Cleanup(netObj);
        }

        [Test]
        public void SetSameValue_NotDirty()
        {
            var (netObj, sv) = CreateSyncVar(5);
            sv.Value = 5;
            Assert.IsFalse(sv.IsDirty);
            Cleanup(netObj);
        }

        [Test]
        public void ClearDirty()
        {
            var (netObj, sv) = CreateSyncVar(0);
            sv.Value = 10;
            Assert.IsTrue(sv.IsDirty);
            sv.ClearDirty();
            Assert.IsFalse(sv.IsDirty);
            Cleanup(netObj);
        }

        #endregion

        #region OnChanged

        [Test]
        public void OnChanged_FiresWithCorrectValues()
        {
            var (netObj, sv) = CreateSyncVar(0);
            int oldVal = -1, newVal = -1;
            sv.OnChanged += (o, n) => { oldVal = o; newVal = n; };
            sv.Value = 42;
            Assert.AreEqual(0, oldVal);
            Assert.AreEqual(42, newVal);
            Cleanup(netObj);
        }

        [Test]
        public void OnChanged_DoesNotFireOnSameValue()
        {
            var (netObj, sv) = CreateSyncVar(5);
            bool fired = false;
            sv.OnChanged += (_, _) => fired = true;
            sv.Value = 5;
            Assert.IsFalse(fired);
            Cleanup(netObj);
        }

        #endregion

        #region SetInternal

        [Test]
        public void SetInternal_BypassesOwnerGuard()
        {
            var (netObj, sv) = CreateSyncVar(0);
            // Even if registered and not owner, SetInternal should work
            sv.SetInternal(99);
            Assert.AreEqual(99, sv.Value);
            Cleanup(netObj);
        }

        [Test]
        public void SetInternal_FiresOnChanged()
        {
            var (netObj, sv) = CreateSyncVar(0);
            int newVal = -1;
            sv.OnChanged += (_, n) => newVal = n;
            sv.SetInternal(42);
            Assert.AreEqual(42, newVal);
            Cleanup(netObj);
        }

        #endregion

        #region Serialization Round-Trip

        [Test]
        public void Serialize_Int_RoundTrip()
        {
            var (netObj1, sv1) = CreateSyncVar(0);
            sv1.Value = 42;

            var w = new NetWriter();
            sv1.WriteTo(w);

            var (netObj2, sv2) = CreateSyncVar(0);
            var r = new NetReader(w.ToArray());
            sv2.ReadFrom(r);
            Assert.AreEqual(42, sv2.Value);

            Cleanup(netObj1);
            Cleanup(netObj2);
        }

        [Test]
        public void Serialize_String_RoundTrip()
        {
            var (netObj1, sv1) = CreateSyncVar("hello");

            var w = new NetWriter();
            sv1.WriteTo(w);

            var (netObj2, sv2) = CreateSyncVar("");
            var r = new NetReader(w.ToArray());
            sv2.ReadFrom(r);
            Assert.AreEqual("hello", sv2.Value);

            Cleanup(netObj1);
            Cleanup(netObj2);
        }

        [Test]
        public void Serialize_Float_RoundTrip()
        {
            var (netObj1, sv1) = CreateSyncVar(0f);
            sv1.Value = 3.14f;

            var w = new NetWriter();
            sv1.WriteTo(w);

            var (netObj2, sv2) = CreateSyncVar(0f);
            var r = new NetReader(w.ToArray());
            sv2.ReadFrom(r);
            Assert.AreEqual(3.14f, sv2.Value, 0.001f);

            Cleanup(netObj1);
            Cleanup(netObj2);
        }

        [Test]
        public void Serialize_Vector3_RoundTrip()
        {
            var (netObj1, sv1) = CreateSyncVar(Vector3.zero);
            sv1.Value = new Vector3(1, 2, 3);

            var w = new NetWriter();
            sv1.WriteTo(w);

            var (netObj2, sv2) = CreateSyncVar(Vector3.zero);
            var r = new NetReader(w.ToArray());
            sv2.ReadFrom(r);
            Assert.AreEqual(new Vector3(1, 2, 3), sv2.Value);

            Cleanup(netObj1);
            Cleanup(netObj2);
        }

        [Test]
        public void Serialize_Color_RoundTrip()
        {
            var (netObj1, sv1) = CreateSyncVar(Color.white);
            sv1.Value = Color.red;

            var w = new NetWriter();
            sv1.WriteTo(w);

            var (netObj2, sv2) = CreateSyncVar(Color.white);
            var r = new NetReader(w.ToArray());
            sv2.ReadFrom(r);
            Assert.AreEqual(Color.red.r, sv2.Value.r, 0.001f);
            Assert.AreEqual(Color.red.g, sv2.Value.g, 0.001f);
            Assert.AreEqual(Color.red.b, sv2.Value.b, 0.001f);
            Assert.AreEqual(Color.red.a, sv2.Value.a, 0.001f);

            Cleanup(netObj1);
            Cleanup(netObj2);
        }

        #endregion

        #region FullState

        [Test]
        public void FullState_WriteThenRead()
        {
            var (netObj1, sv1) = CreateSyncVar(99);

            var w = new NetWriter();
            sv1.WriteFullState(w);

            var (netObj2, sv2) = CreateSyncVar(0);
            var r = new NetReader(w.ToArray());
            sv2.ReadFullState(r);
            Assert.AreEqual(99, sv2.Value);

            Cleanup(netObj1);
            Cleanup(netObj2);
        }

        #endregion

        #region Multiple SyncVars

        [Test]
        public void MultipleSyncVars_IndexAssignment()
        {
            var go = new GameObject("TestMulti");
            var netObj = go.AddComponent<NetworkObject>();
            var sv0 = netObj.Sync(0);
            var sv1 = netObj.Sync("hello");
            var sv2 = netObj.Sync(3.14f);
            Assert.AreEqual(0, sv0.Index);
            Assert.AreEqual(1, sv1.Index);
            Assert.AreEqual(2, sv2.Index);
            Assert.AreEqual(3, netObj.SyncVarCount);
            Object.DestroyImmediate(go);
        }

        [Test]
        public void MultipleSyncVars_DirtyMask()
        {
            var go = new GameObject("TestMask");
            var netObj = go.AddComponent<NetworkObject>();
            var sv0 = netObj.Sync(0);
            var sv1 = netObj.Sync("hello");
            var sv2 = netObj.Sync(3.14f);

            // Only change sv1
            sv1.Value = "world";

            // Serialize dirty
            var w = new NetWriter();
            netObj.SerializeDirty(w);

            // Deserialize into a second object
            var go2 = new GameObject("TestMask2");
            var netObj2 = go2.AddComponent<NetworkObject>();
            var sv0b = netObj2.Sync(0);
            var sv1b = netObj2.Sync("");
            var sv2b = netObj2.Sync(0f);

            var r = new NetReader(w.ToArray());
            netObj2.DeserializeDirty(r);

            // Only sv1 should have changed
            Assert.AreEqual(0, sv0b.Value); // unchanged
            Assert.AreEqual("world", sv1b.Value); // updated
            Assert.AreEqual(0f, sv2b.Value); // unchanged

            Object.DestroyImmediate(go);
            Object.DestroyImmediate(go2);
        }

        [Test]
        public void SerializeAll_DeserializeAll()
        {
            var go = new GameObject("TestAll");
            var netObj = go.AddComponent<NetworkObject>();
            var sv0 = netObj.Sync(42);
            var sv1 = netObj.Sync("test");

            var w = new NetWriter();
            netObj.SerializeAll(w);

            var go2 = new GameObject("TestAll2");
            var netObj2 = go2.AddComponent<NetworkObject>();
            var sv0b = netObj2.Sync(0);
            var sv1b = netObj2.Sync("");

            var r = new NetReader(w.ToArray());
            netObj2.DeserializeAll(r);

            Assert.AreEqual(42, sv0b.Value);
            Assert.AreEqual("test", sv1b.Value);

            Object.DestroyImmediate(go);
            Object.DestroyImmediate(go2);
        }

        #endregion

        #region ToString

        [Test]
        public void ToString_ShowsValue()
        {
            var (netObj, sv) = CreateSyncVar(42);
            Assert.AreEqual("42", sv.ToString());
            Cleanup(netObj);
        }

        [Test]
        public void ToString_NullShowsNull()
        {
            var (netObj, sv) = CreateSyncVar<string>(null);
            Assert.AreEqual("null", sv.ToString());
            Cleanup(netObj);
        }

        #endregion
    }
}
