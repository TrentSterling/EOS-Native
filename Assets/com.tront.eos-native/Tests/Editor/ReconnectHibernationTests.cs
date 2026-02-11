using System.Collections.Generic;
using System.Reflection;
using NUnit.Framework;
using EOSNative.Net;
using UnityEngine;

namespace EOSNative.Tests
{
    /// <summary>
    /// Tests for reconnect-without-despawning (hibernation) feature.
    /// Verifies PersistOnDisconnect property, hibernation dictionary, grace period,
    /// and OnHostChanged event.
    /// </summary>
    public class ReconnectHibernationTests
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

        #region PersistOnDisconnect Property

        [Test]
        public void PersistOnDisconnect_DefaultFalse()
        {
            var go = new GameObject("TestObj");
            var netObj = go.AddComponent<NetworkObject>();
            Assert.IsFalse(netObj.PersistOnDisconnect);
            Object.DestroyImmediate(go);
        }

        [Test]
        public void PersistOnDisconnect_SetTrue_Persists()
        {
            var go = new GameObject("TestObj");
            var netObj = go.AddComponent<NetworkObject>();
            netObj.PersistOnDisconnect = true;
            Assert.IsTrue(netObj.PersistOnDisconnect);
            Object.DestroyImmediate(go);
        }

        [Test]
        public void PersistOnDisconnect_IndependentOfDestroyWithOwner()
        {
            var go = new GameObject("TestObj");
            var netObj = go.AddComponent<NetworkObject>();
            netObj.PersistOnDisconnect = true;
            netObj.DestroyWithOwner = true;
            Assert.IsTrue(netObj.PersistOnDisconnect);
            Assert.IsTrue(netObj.DestroyWithOwner);
            Object.DestroyImmediate(go);
        }

        #endregion

        #region ReconnectGracePeriod

        [Test]
        public void ReconnectGracePeriod_DefaultThirtySeconds()
        {
            Assert.AreEqual(30f, _nm.ReconnectGracePeriod);
        }

        [Test]
        public void ReconnectGracePeriod_CanBeSet()
        {
            _nm.ReconnectGracePeriod = 60f;
            Assert.AreEqual(60f, _nm.ReconnectGracePeriod);
        }

        [Test]
        public void ReconnectGracePeriod_ZeroIsValid()
        {
            _nm.ReconnectGracePeriod = 0f;
            Assert.AreEqual(0f, _nm.ReconnectGracePeriod);
        }

        #endregion

        #region Hibernation Dictionary

        [Test]
        public void HibernatedPeers_EmptyByDefault()
        {
            Assert.AreEqual(0, _nm._hibernatedPeers.Count);
        }

        [Test]
        public void HibernatedPeers_CanAddAndRetrieve()
        {
            var peer = new NetworkManager.HibernatedPeer
            {
                Puid = "test-puid",
                ExpireTime = Time.time + 30f,
                OwnedObjectIds = new List<uint> { 1, 2, 3 }
            };
            _nm._hibernatedPeers["test-puid"] = peer;

            Assert.AreEqual(1, _nm._hibernatedPeers.Count);
            Assert.IsTrue(_nm._hibernatedPeers.ContainsKey("test-puid"));
            Assert.AreEqual(3, _nm._hibernatedPeers["test-puid"].OwnedObjectIds.Count);
        }

        [Test]
        public void HibernatedPeers_RemoveWorks()
        {
            _nm._hibernatedPeers["test"] = new NetworkManager.HibernatedPeer
            {
                Puid = "test",
                ExpireTime = Time.time + 30f,
                OwnedObjectIds = new List<uint>()
            };
            _nm._hibernatedPeers.Remove("test");
            Assert.AreEqual(0, _nm._hibernatedPeers.Count);
        }

        #endregion

        #region OnHostChanged Event

        [Test]
        public void OnHostChanged_CanSubscribeAndReceive()
        {
            bool received = false;
            bool hostValue = false;
            _nm.OnHostChanged += (isHost) =>
            {
                received = true;
                hostValue = isHost;
            };

            // Fire the event via the compiler-generated backing field
            var eventField = typeof(NetworkManager)
                .GetField("OnHostChanged", BindingFlags.NonPublic | BindingFlags.Instance);
            var del = (System.Action<bool>)eventField.GetValue(_nm);
            del?.Invoke(true);

            Assert.IsTrue(received);
            Assert.IsTrue(hostValue);
        }

        [Test]
        public void OnHostChanged_MultipleSubscribers()
        {
            int callCount = 0;
            _nm.OnHostChanged += (_) => callCount++;
            _nm.OnHostChanged += (_) => callCount++;

            var eventField = typeof(NetworkManager)
                .GetField("OnHostChanged", BindingFlags.NonPublic | BindingFlags.Instance);
            var del = (System.Action<bool>)eventField.GetValue(_nm);
            del?.Invoke(true);

            Assert.AreEqual(2, callCount);
        }

        #endregion

        #region Spawn Integration

        [Test]
        public void SpawnedObject_PersistOnDisconnect_InObjectsDictionary()
        {
            EnableOfflineMode();

            var prefab = new GameObject("Prefab");
            prefab.AddComponent<NetworkObject>();
            prefab.SetActive(false);
            _nm.RegisterPrefab(prefab, 0);

            var spawned = _nm.Spawn(0, Vector3.zero, Quaternion.identity);
            Assert.IsNotNull(spawned);

            spawned.PersistOnDisconnect = true;

            Assert.IsTrue(spawned.PersistOnDisconnect);
            Assert.IsTrue(_nm.Objects.ContainsKey(spawned.NetworkId));

            _nm.DespawnAll();
            Object.DestroyImmediate(prefab);
        }

        #endregion
    }
}
