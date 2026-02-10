using System.Reflection;
using NUnit.Framework;
using EOSNative.Net;
using UnityEngine;

namespace EOSNative.Tests
{
    /// <summary>
    /// Tests for NetworkManager core functionality: prefab registry, offline mode,
    /// prefab table auto-registration, RPC registration, and properties.
    /// Uses inactive GO to avoid DontDestroyOnLoad in EditMode.
    /// </summary>
    public class NetworkManagerCoreTests
    {
        private NetworkManager _nm;

        private static readonly FieldInfo InstanceField =
            typeof(NetworkManager).GetField("_instance", BindingFlags.NonPublic | BindingFlags.Static);

        [SetUp]
        public void SetUp()
        {
            var go = new GameObject("NetworkManager");
            go.SetActive(false);
            _nm = go.AddComponent<NetworkManager>();
            InstanceField.SetValue(null, _nm);
        }

        [TearDown]
        public void TearDown()
        {
            InstanceField.SetValue(null, null);
            if (_nm != null)
                Object.DestroyImmediate(_nm.gameObject);
            foreach (var name in new[] { "NetworkManager", "EOSP2PManager", "EOSLobbyManager", "NetworkSceneManager" })
            {
                var obj = GameObject.Find(name);
                if (obj != null) Object.DestroyImmediate(obj);
            }
        }

        private void EnableOfflineMode()
        {
            typeof(NetworkManager).GetProperty("OfflineMode").SetValue(_nm, true);
            typeof(NetworkManager).GetProperty("IsHost").SetValue(_nm, true);
            typeof(NetworkManager)
                .GetField("_localIdPrefix", BindingFlags.NonPublic | BindingFlags.Instance)
                .SetValue(_nm, (ushort)0xFFFF);
            typeof(NetworkManager)
                .GetField("_localIdCounter", BindingFlags.NonPublic | BindingFlags.Instance)
                .SetValue(_nm, (ushort)1);
        }

        #region RegisterPrefab & GetPrefab

        [Test]
        public void RegisterPrefab_GetPrefab_RoundTrip()
        {
            var go = new GameObject("Prefab");
            go.AddComponent<NetworkObject>();

            _nm.RegisterPrefab(go, 0);
            Assert.AreEqual(go, _nm.GetPrefab(0));

            Object.DestroyImmediate(go);
        }

        [Test]
        public void GetPrefab_Unregistered_ReturnsNull()
        {
            Assert.IsNull(_nm.GetPrefab(99));
        }

        [Test]
        public void RegisterPrefab_MultiplePrefabs()
        {
            var a = new GameObject("A");
            var b = new GameObject("B");
            var c = new GameObject("C");

            _nm.RegisterPrefab(a, 0);
            _nm.RegisterPrefab(b, 1);
            _nm.RegisterPrefab(c, 2);

            Assert.AreEqual(a, _nm.GetPrefab(0));
            Assert.AreEqual(b, _nm.GetPrefab(1));
            Assert.AreEqual(c, _nm.GetPrefab(2));

            Object.DestroyImmediate(a);
            Object.DestroyImmediate(b);
            Object.DestroyImmediate(c);
        }

        [Test]
        public void RegisterPrefab_SparseIds_FillsGaps()
        {
            var go = new GameObject("Sparse");
            _nm.RegisterPrefab(go, 5);

            Assert.IsNull(_nm.GetPrefab(0));
            Assert.IsNull(_nm.GetPrefab(4));
            Assert.AreEqual(go, _nm.GetPrefab(5));

            Object.DestroyImmediate(go);
        }

        [Test]
        public void RegisterPrefab_OverwriteId()
        {
            var first = new GameObject("First");
            var second = new GameObject("Second");

            _nm.RegisterPrefab(first, 0);
            _nm.RegisterPrefab(second, 0);

            Assert.AreEqual(second, _nm.GetPrefab(0));

            Object.DestroyImmediate(first);
            Object.DestroyImmediate(second);
        }

        #endregion

        #region PrefabTable

        [Test]
        public void PrefabTable_SetGet()
        {
            var table = ScriptableObject.CreateInstance<NetworkPrefabTable>();
            _nm.PrefabTable = table;
            Assert.AreEqual(table, _nm.PrefabTable);
            Object.DestroyImmediate(table);
        }

        [Test]
        public void PrefabTable_NullByDefault()
        {
            Assert.IsNull(_nm.PrefabTable);
        }

        #endregion

        #region Offline Mode

        [Test]
        public void OfflineMode_DefaultFalse()
        {
            Assert.IsFalse(_nm.OfflineMode);
        }

        [Test]
        public void OfflineMode_SetViaReflection_Works()
        {
            EnableOfflineMode();
            Assert.IsTrue(_nm.OfflineMode);
            Assert.IsTrue(_nm.IsHost);
        }

        [Test]
        public void OfflineMode_SpawnAndOwnership()
        {
            EnableOfflineMode();
            var prefab = new GameObject("Prefab");
            prefab.AddComponent<NetworkObject>();
            _nm.RegisterPrefab(prefab, 0);

            var spawned = _nm.Spawn(0, Vector3.zero, Quaternion.identity);
            Assert.IsNotNull(spawned);
            Assert.IsTrue(spawned.IsOwner, "Should be owner in offline mode");
            Assert.IsNull(spawned.OwnerId, "OwnerId should be null in offline mode");

            Object.DestroyImmediate(prefab);
        }

        [Test]
        public void OfflineMode_NetworkIdHasFFFFPrefix()
        {
            EnableOfflineMode();
            var prefab = new GameObject("Prefab");
            prefab.AddComponent<NetworkObject>();
            _nm.RegisterPrefab(prefab, 0);

            var spawned = _nm.Spawn(0, Vector3.zero, Quaternion.identity);

            ushort prefix = (ushort)(spawned.NetworkId >> 16);
            Assert.AreEqual(0xFFFF, prefix, "Offline mode should use 0xFFFF prefix");

            Object.DestroyImmediate(prefab);
        }

        #endregion

        #region Spectator

        [Test]
        public void IsSpectator_DefaultFalse()
        {
            Assert.IsFalse(_nm.IsSpectator);
        }

        [Test]
        public void IsSpectator_Settable()
        {
            _nm.IsSpectator = true;
            Assert.IsTrue(_nm.IsSpectator);
            _nm.IsSpectator = false;
            Assert.IsFalse(_nm.IsSpectator);
        }

        #endregion

        #region RPC Registration

        [Test]
        public void RegisterRPC_NoException()
        {
            EnableOfflineMode();
            var go = new GameObject("RPCTarget");
            var netObj = go.AddComponent<NetworkObject>();
            _nm.RegisterExisting(netObj, 0xAA000001);

            Assert.DoesNotThrow(() =>
                _nm.RegisterRPC(netObj, "TestMethod", reader => { })
            );

            Object.DestroyImmediate(go);
        }

        [Test]
        public void RegisterRPC_DuplicateSameName_NoException()
        {
            EnableOfflineMode();
            var go = new GameObject("RPCTarget");
            var netObj = go.AddComponent<NetworkObject>();
            _nm.RegisterExisting(netObj, 0xAA000001);

            _nm.RegisterRPC(netObj, "TestMethod", reader => { });
            Assert.DoesNotThrow(() =>
                _nm.RegisterRPC(netObj, "TestMethod", reader => { })
            );

            Object.DestroyImmediate(go);
        }

        #endregion

        #region Rate Limiting

        [Test]
        public void MaxMessagesPerPeerPerSecond_DefaultZero()
        {
            Assert.AreEqual(0, _nm.MaxMessagesPerPeerPerSecond);
        }

        [Test]
        public void MaxMessagesPerPeerPerSecond_Settable()
        {
            _nm.MaxMessagesPerPeerPerSecond = 100;
            Assert.AreEqual(100, _nm.MaxMessagesPerPeerPerSecond);
        }

        #endregion

        #region InterestManagement

        [Test]
        public void InterestManagementEnabled_DefaultFalse()
        {
            Assert.IsFalse(_nm.InterestManagementEnabled);
        }

        [Test]
        public void InterestManagementEnabled_Settable()
        {
            _nm.InterestManagementEnabled = true;
            Assert.IsTrue(_nm.InterestManagementEnabled);
        }

        #endregion
    }
}
