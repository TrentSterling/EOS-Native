using System.Reflection;
using NUnit.Framework;
using EOSNative.Net;
using UnityEngine;

namespace EOSNative.Tests
{
    /// <summary>
    /// Tests for NetworkManager.RegisterExisting() and RegisterExistingHierarchy().
    /// Uses inactive GO to avoid DontDestroyOnLoad in EditMode.
    /// </summary>
    public class RegisterExistingTests
    {
        private NetworkManager _nm;

        [SetUp]
        public void SetUp()
        {
            var go = new GameObject("NetworkManager");
            go.SetActive(false);
            _nm = go.AddComponent<NetworkManager>();

            typeof(NetworkManager)
                .GetField("_instance", BindingFlags.NonPublic | BindingFlags.Static)
                .SetValue(null, _nm);

            // Set offline mode to enable GenerateNetworkId for hierarchy tests
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
            typeof(NetworkManager)
                .GetField("_instance", BindingFlags.NonPublic | BindingFlags.Static)
                .SetValue(null, null);

            if (_nm != null)
                Object.DestroyImmediate(_nm.gameObject);

            foreach (var name in new[] { "NetworkManager", "EOSP2PManager", "EOSLobbyManager", "NetworkSceneManager" })
            {
                var obj = GameObject.Find(name);
                if (obj != null) Object.DestroyImmediate(obj);
            }
        }

        private NetworkObject CreateNetObj(string name = "TestObj")
        {
            var go = new GameObject(name);
            return go.AddComponent<NetworkObject>();
        }

        private void Cleanup(params NetworkObject[] objs)
        {
            foreach (var obj in objs)
                if (obj != null && obj.gameObject != null)
                    Object.DestroyImmediate(obj.gameObject);
        }

        #region RegisterExisting

        [Test]
        public void RegisterExisting_SetsNetworkId()
        {
            var netObj = CreateNetObj();
            _nm.RegisterExisting(netObj, 0xAA000001);
            Assert.AreEqual(0xAA000001u, netObj.NetworkId);
            Cleanup(netObj);
        }

        [Test]
        public void RegisterExisting_SetsIsRegistered()
        {
            var netObj = CreateNetObj();
            Assert.IsFalse(netObj.IsRegistered);
            _nm.RegisterExisting(netObj, 0xAA000002);
            Assert.IsTrue(netObj.IsRegistered);
            Cleanup(netObj);
        }

        [Test]
        public void RegisterExisting_InObjectsDictionary()
        {
            var netObj = CreateNetObj();
            _nm.RegisterExisting(netObj, 0xAA000003);
            Assert.IsTrue(_nm.Objects.ContainsKey(0xAA000003));
            Assert.AreEqual(netObj, _nm.Objects[0xAA000003]);
            Cleanup(netObj);
        }

        [Test]
        public void RegisterExisting_DeterministicId_BallFormat()
        {
            var netObj = CreateNetObj("DemoBall");
            uint ballId = 0xBB000000u | (NetworkManager.FnvHash("test_puid") & 0x00FFFFFFu);
            _nm.RegisterExisting(netObj, ballId);

            Assert.AreEqual(ballId, netObj.NetworkId);
            Assert.AreEqual(0xBB, (netObj.NetworkId >> 24) & 0xFF);
            Cleanup(netObj);
        }

        [Test]
        public void RegisterExisting_DeterministicId_WeaponFormat()
        {
            var netObj = CreateNetObj("SceneWeapon");
            uint weaponId = 0xCC000000u | 0;
            _nm.RegisterExisting(netObj, weaponId);

            Assert.AreEqual(weaponId, netObj.NetworkId);
            Assert.AreEqual(0xCC, (netObj.NetworkId >> 24) & 0xFF);
            Cleanup(netObj);
        }

        [Test]
        public void RegisterExisting_MultipleObjects_AllTracked()
        {
            var a = CreateNetObj("A");
            var b = CreateNetObj("B");
            var c = CreateNetObj("C");

            _nm.RegisterExisting(a, 0xAA000001);
            _nm.RegisterExisting(b, 0xAA000002);
            _nm.RegisterExisting(c, 0xAA000003);

            Assert.AreEqual(a, _nm.Objects[0xAA000001]);
            Assert.AreEqual(b, _nm.Objects[0xAA000002]);
            Assert.AreEqual(c, _nm.Objects[0xAA000003]);

            Cleanup(a, b, c);
        }

        [Test]
        public void RegisterExisting_OverwritesSameId()
        {
            var first = CreateNetObj("First");
            var second = CreateNetObj("Second");

            _nm.RegisterExisting(first, 0xAA000001);
            _nm.RegisterExisting(second, 0xAA000001);

            Assert.AreEqual(second, _nm.Objects[0xAA000001]);

            Cleanup(first, second);
        }

        [Test]
        public void RegisterExisting_SetsPrefabId_NoPrefix()
        {
            var netObj = CreateNetObj();
            _nm.RegisterExisting(netObj, 0xAA000010);
            Assert.AreEqual(NetworkObject.NO_PREFAB, netObj.PrefabId);
            Cleanup(netObj);
        }

        #endregion

        #region RegisterExistingHierarchy

        [Test]
        public void RegisterExistingHierarchy_RootRegistered()
        {
            var rootGo = new GameObject("Root");
            var root = rootGo.AddComponent<NetworkObject>();

            var childGo = new GameObject("Child");
            childGo.transform.SetParent(rootGo.transform);
            childGo.AddComponent<NetworkObject>();

            _nm.RegisterExistingHierarchy(root, 0xDD000001);

            Assert.IsTrue(root.IsRegistered);
            Assert.AreEqual(0xDD000001u, root.NetworkId);
            Assert.AreEqual(0u, root.ParentNetworkId);

            Object.DestroyImmediate(rootGo);
        }

        [Test]
        public void RegisterExistingHierarchy_ChildRegistered()
        {
            var rootGo = new GameObject("Root");
            var root = rootGo.AddComponent<NetworkObject>();

            var childGo = new GameObject("Child");
            childGo.transform.SetParent(rootGo.transform);
            var child = childGo.AddComponent<NetworkObject>();

            _nm.RegisterExistingHierarchy(root, 0xDD000001);

            Assert.IsTrue(child.IsRegistered);
            Assert.AreNotEqual(0u, child.NetworkId);
            Assert.AreEqual(0xDD000001u, child.ParentNetworkId);
            Assert.AreEqual(0xDD000001u, child.OriginalParentNetworkId);

            Object.DestroyImmediate(rootGo);
        }

        [Test]
        public void RegisterExistingHierarchy_MultipleChildren()
        {
            var rootGo = new GameObject("Root");
            var root = rootGo.AddComponent<NetworkObject>();

            for (int i = 0; i < 3; i++)
            {
                var cGo = new GameObject($"Child{i}");
                cGo.transform.SetParent(rootGo.transform);
                cGo.AddComponent<NetworkObject>();
            }

            _nm.RegisterExistingHierarchy(root, 0xDD000001);

            // Root + 3 children = 4 total
            int count = 0;
            foreach (var _ in _nm.Objects) count++;
            Assert.GreaterOrEqual(count, 4);

            Object.DestroyImmediate(rootGo);
        }

        [Test]
        public void RegisterExistingHierarchy_ChildrenInheritPrefabId()
        {
            var rootGo = new GameObject("Root");
            var root = rootGo.AddComponent<NetworkObject>();
            root.PrefabId = 42;

            var childGo = new GameObject("Child");
            childGo.transform.SetParent(rootGo.transform);
            var child = childGo.AddComponent<NetworkObject>();

            _nm.RegisterExistingHierarchy(root, 0xDD000001);

            Assert.AreEqual(42, child.PrefabId);

            Object.DestroyImmediate(rootGo);
        }

        [Test]
        public void RegisterExistingHierarchy_NoChildren_JustRoot()
        {
            var rootGo = new GameObject("StandaloneRoot");
            var root = rootGo.AddComponent<NetworkObject>();

            _nm.RegisterExistingHierarchy(root, 0xDD000001);

            Assert.IsTrue(root.IsRegistered);
            Assert.AreEqual(0xDD000001u, root.NetworkId);

            Object.DestroyImmediate(rootGo);
        }

        #endregion
    }
}
