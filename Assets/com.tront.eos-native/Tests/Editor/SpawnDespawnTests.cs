using System.Reflection;
using NUnit.Framework;
using EOSNative.Net;
using UnityEngine;
using UnityEngine.TestTools;

namespace EOSNative.Tests
{
    /// <summary>
    /// Tests for NetworkManager.Spawn(), Despawn(), DespawnAll() using offline mode.
    /// Uses reflection to set offline mode fields, avoiding DontDestroyOnLoad in EditMode.
    /// </summary>
    public class SpawnDespawnTests
    {
        private NetworkManager _nm;

        [SetUp]
        public void SetUp()
        {
            // Create on inactive GO to prevent OnEnable from firing (avoids DontDestroyOnLoad chain)
            var go = new GameObject("NetworkManager");
            go.SetActive(false);
            _nm = go.AddComponent<NetworkManager>();

            // Set static _instance via reflection so NetworkObject.IsOwner can find it
            typeof(NetworkManager)
                .GetField("_instance", BindingFlags.NonPublic | BindingFlags.Static)
                .SetValue(null, _nm);

            // Set offline mode fields via reflection (bypasses EnsureRoomState/EnsureLocalPlayerState)
            typeof(NetworkManager).GetProperty("OfflineMode").SetValue(_nm, true);
            typeof(NetworkManager).GetProperty("IsHost").SetValue(_nm, true);
            typeof(NetworkManager)
                .GetField("_localIdPrefix", BindingFlags.NonPublic | BindingFlags.Instance)
                .SetValue(_nm, (ushort)0xFFFF);
            typeof(NetworkManager)
                .GetField("_localIdCounter", BindingFlags.NonPublic | BindingFlags.Instance)
                .SetValue(_nm, (ushort)1);
        }

        [TearDown]
        public void TearDown()
        {
            // Clear static instance
            typeof(NetworkManager)
                .GetField("_instance", BindingFlags.NonPublic | BindingFlags.Static)
                .SetValue(null, null);

            if (_nm != null)
                Object.DestroyImmediate(_nm.gameObject);

            // Clean up any leaked singletons
            foreach (var name in new[] { "NetworkManager", "EOSP2PManager", "EOSLobbyManager", "NetworkSceneManager" })
            {
                var obj = GameObject.Find(name);
                if (obj != null) Object.DestroyImmediate(obj);
            }
        }

        private GameObject CreatePrefab(string name = "TestPrefab")
        {
            var go = new GameObject(name);
            go.AddComponent<NetworkObject>();
            go.SetActive(false);
            return go;
        }

        private GameObject CreatePrefabWithChild(string rootName = "Root", string childName = "Child")
        {
            var root = new GameObject(rootName);
            root.AddComponent<NetworkObject>();

            var child = new GameObject(childName);
            child.transform.SetParent(root.transform);
            child.AddComponent<NetworkObject>();

            root.SetActive(false);
            return root;
        }

        #region Spawn

        [Test]
        public void Spawn_ReturnsNetworkObject()
        {
            var prefab = CreatePrefab();
            _nm.RegisterPrefab(prefab, 0);

            var spawned = _nm.Spawn(0, Vector3.zero, Quaternion.identity);

            Assert.IsNotNull(spawned);
            Assert.IsTrue(spawned.IsRegistered);
            Assert.AreNotEqual(0u, spawned.NetworkId);

            Object.DestroyImmediate(prefab);
        }

        [Test]
        public void Spawn_SetsPosition()
        {
            var prefab = CreatePrefab();
            _nm.RegisterPrefab(prefab, 0);

            var pos = new Vector3(5f, 10f, 15f);
            var spawned = _nm.Spawn(0, pos, Quaternion.identity);

            Assert.AreEqual(pos.x, spawned.transform.position.x, 0.01f);
            Assert.AreEqual(pos.y, spawned.transform.position.y, 0.01f);
            Assert.AreEqual(pos.z, spawned.transform.position.z, 0.01f);

            Object.DestroyImmediate(prefab);
        }

        [Test]
        public void Spawn_SetsRotation()
        {
            var prefab = CreatePrefab();
            _nm.RegisterPrefab(prefab, 0);

            var rot = Quaternion.Euler(0, 90, 0);
            var spawned = _nm.Spawn(0, Vector3.zero, rot);

            Assert.AreEqual(rot.y, spawned.transform.rotation.y, 0.01f);

            Object.DestroyImmediate(prefab);
        }

        [Test]
        public void Spawn_SetsPrefabId()
        {
            var prefab = CreatePrefab();
            _nm.RegisterPrefab(prefab, 7);

            var spawned = _nm.Spawn(7, Vector3.zero, Quaternion.identity);
            Assert.AreEqual(7, spawned.PrefabId);

            Object.DestroyImmediate(prefab);
        }

        [Test]
        public void Spawn_IsInObjectsDictionary()
        {
            var prefab = CreatePrefab();
            _nm.RegisterPrefab(prefab, 0);

            var spawned = _nm.Spawn(0, Vector3.zero, Quaternion.identity);
            Assert.IsTrue(_nm.Objects.ContainsKey(spawned.NetworkId));

            Object.DestroyImmediate(prefab);
        }

        [Test]
        public void Spawn_IsOwnerInOfflineMode()
        {
            var prefab = CreatePrefab();
            _nm.RegisterPrefab(prefab, 0);

            var spawned = _nm.Spawn(0, Vector3.zero, Quaternion.identity);
            Assert.IsTrue(spawned.IsOwner);

            Object.DestroyImmediate(prefab);
        }

        [Test]
        public void Spawn_UnregisteredPrefab_ReturnsNull()
        {
            LogAssert.Expect(LogType.Error, "[NetworkManager] No prefab registered for ID 99");
            var result = _nm.Spawn(99, Vector3.zero, Quaternion.identity);
            Assert.IsNull(result);
        }

        [Test]
        public void Spawn_MultipleObjects_UniqueIds()
        {
            var prefab = CreatePrefab();
            _nm.RegisterPrefab(prefab, 0);

            var a = _nm.Spawn(0, Vector3.zero, Quaternion.identity);
            var b = _nm.Spawn(0, Vector3.one, Quaternion.identity);
            var c = _nm.Spawn(0, Vector3.up, Quaternion.identity);

            Assert.AreNotEqual(a.NetworkId, b.NetworkId);
            Assert.AreNotEqual(b.NetworkId, c.NetworkId);
            Assert.AreNotEqual(a.NetworkId, c.NetworkId);

            Object.DestroyImmediate(prefab);
        }

        [Test]
        public void Spawn_InheritsInactivePrefabState()
        {
            var prefab = CreatePrefab();
            // CreatePrefab sets inactive — clone should inherit inactive state (no auto-activate)
            _nm.RegisterPrefab(prefab, 0);

            var spawned = _nm.Spawn(0, Vector3.zero, Quaternion.identity);
            Assert.IsFalse(spawned.gameObject.activeSelf, "Clone should inherit prefab inactive state");

            Object.DestroyImmediate(prefab);
        }

        [Test]
        public void Spawn_IsRootNetworkObject()
        {
            var prefab = CreatePrefab();
            _nm.RegisterPrefab(prefab, 0);

            var spawned = _nm.Spawn(0, Vector3.zero, Quaternion.identity);
            Assert.IsTrue(spawned.IsRootNetworkObject);
            Assert.AreEqual(0u, spawned.ParentNetworkId);

            Object.DestroyImmediate(prefab);
        }

        [Test]
        public void Spawn_NetworkIdHasFFFFPrefix()
        {
            var prefab = CreatePrefab();
            _nm.RegisterPrefab(prefab, 0);

            var spawned = _nm.Spawn(0, Vector3.zero, Quaternion.identity);
            ushort prefix = (ushort)(spawned.NetworkId >> 16);
            Assert.AreEqual(0xFFFF, prefix, "Offline mode should use 0xFFFF prefix");

            Object.DestroyImmediate(prefab);
        }

        #endregion

        #region Spawn with Children

        [Test]
        public void Spawn_WithChild_RegistersBoth()
        {
            var prefab = CreatePrefabWithChild();
            _nm.RegisterPrefab(prefab, 0);

            var root = _nm.Spawn(0, Vector3.zero, Quaternion.identity);

            // Root + 1 child = at least 2 objects
            int count = 0;
            foreach (var _ in _nm.Objects) count++;
            Assert.GreaterOrEqual(count, 2);

            Object.DestroyImmediate(prefab);
        }

        [Test]
        public void Spawn_WithChild_ChildHasParentId()
        {
            var prefab = CreatePrefabWithChild();
            _nm.RegisterPrefab(prefab, 0);

            var root = _nm.Spawn(0, Vector3.zero, Quaternion.identity);

            NetworkObject child = null;
            foreach (var kvp in _nm.Objects)
            {
                if (kvp.Value.ParentNetworkId == root.NetworkId)
                {
                    child = kvp.Value;
                    break;
                }
            }

            Assert.IsNotNull(child, "Child should be registered");
            Assert.IsTrue(child.IsChildNetworkObject);
            Assert.AreEqual(root.NetworkId, child.ParentNetworkId);
            Assert.AreEqual(root.NetworkId, child.OriginalParentNetworkId);

            Object.DestroyImmediate(prefab);
        }

        #endregion

        #region Spectator Guard

        [Test]
        public void Spawn_Spectator_ReturnsNull()
        {
            var prefab = CreatePrefab();
            _nm.RegisterPrefab(prefab, 0);
            _nm.IsSpectator = true;

            var result = _nm.Spawn(0, Vector3.zero, Quaternion.identity);
            Assert.IsNull(result);

            _nm.IsSpectator = false;
            Object.DestroyImmediate(prefab);
        }

        #endregion

        #region Despawn

        [Test]
        public void Despawn_RemovesFromObjects()
        {
            var prefab = CreatePrefab();
            _nm.RegisterPrefab(prefab, 0);

            var spawned = _nm.Spawn(0, Vector3.zero, Quaternion.identity);
            uint id = spawned.NetworkId;

            _nm.Despawn(spawned);
            Assert.IsFalse(_nm.Objects.ContainsKey(id));
        }

        [Test]
        public void Despawn_ClearsIsRegistered()
        {
            var prefab = CreatePrefab();
            _nm.RegisterPrefab(prefab, 0);

            var spawned = _nm.Spawn(0, Vector3.zero, Quaternion.identity);
            Assert.IsTrue(spawned.IsRegistered);

            _nm.Despawn(spawned);
            Assert.IsFalse(spawned.IsRegistered);

            Object.DestroyImmediate(prefab);
        }

        [Test]
        public void Despawn_Null_NoException()
        {
            Assert.DoesNotThrow(() => _nm.Despawn(null));
        }

        [Test]
        public void Despawn_Unregistered_NoException()
        {
            var go = new GameObject("NotRegistered");
            var netObj = go.AddComponent<NetworkObject>();
            Assert.DoesNotThrow(() => _nm.Despawn(netObj));
            Object.DestroyImmediate(go);
        }

        [Test]
        public void Despawn_WithChildren_CascadesToChildren()
        {
            var prefab = CreatePrefabWithChild();
            _nm.RegisterPrefab(prefab, 0);

            var root = _nm.Spawn(0, Vector3.zero, Quaternion.identity);
            uint rootId = root.NetworkId;

            var childIds = new System.Collections.Generic.List<uint>();
            foreach (var kvp in _nm.Objects)
            {
                if (kvp.Value.ParentNetworkId == rootId)
                    childIds.Add(kvp.Key);
            }
            Assert.Greater(childIds.Count, 0, "Should have at least one child");

            _nm.Despawn(root);

            Assert.IsFalse(_nm.Objects.ContainsKey(rootId), "Root should be removed");
            foreach (var cid in childIds)
                Assert.IsFalse(_nm.Objects.ContainsKey(cid), $"Child {cid} should be removed");

            Object.DestroyImmediate(prefab);
        }

        #endregion

        #region DespawnAll

        [Test]
        public void DespawnAll_RemovesAllOwned()
        {
            var prefab = CreatePrefab();
            _nm.RegisterPrefab(prefab, 0);

            _nm.Spawn(0, Vector3.zero, Quaternion.identity);
            _nm.Spawn(0, Vector3.one, Quaternion.identity);
            _nm.Spawn(0, Vector3.up, Quaternion.identity);

            int before = 0;
            foreach (var _ in _nm.Objects) before++;
            Assert.GreaterOrEqual(before, 3);

            _nm.DespawnAll();

            int after = 0;
            foreach (var _ in _nm.Objects) after++;
            Assert.AreEqual(0, after);

            Object.DestroyImmediate(prefab);
        }

        [Test]
        public void DespawnAll_EmptyObjects_NoException()
        {
            Assert.DoesNotThrow(() => _nm.DespawnAll());
        }

        #endregion
    }
}
