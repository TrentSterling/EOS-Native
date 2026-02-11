using System.Reflection;
using NUnit.Framework;
using EOSNative.Net;
using UnityEngine;
using UnityEngine.TestTools;

namespace EOSNative.Tests
{
    /// <summary>
    /// Tests for Spawn(GameObject, ...), Spawn(string, ...), and GetPrefabId().
    /// </summary>
    public class SpawnOverloadTests
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

        private GameObject CreatePrefab(string name = "TestPrefab")
        {
            var go = new GameObject(name);
            go.AddComponent<NetworkObject>();
            go.SetActive(false);
            return go;
        }

        #region GetPrefabId

        [Test]
        public void GetPrefabId_Registered_ReturnsIndex()
        {
            var prefab = CreatePrefab();
            _nm.RegisterPrefab(prefab, 3);

            Assert.AreEqual(3, _nm.GetPrefabId(prefab));

            Object.DestroyImmediate(prefab);
        }

        [Test]
        public void GetPrefabId_NotRegistered_ReturnsNegativeOne()
        {
            var prefab = CreatePrefab();

            Assert.AreEqual(-1, _nm.GetPrefabId(prefab));

            Object.DestroyImmediate(prefab);
        }

        #endregion

        #region Spawn(GameObject)

        [Test]
        public void Spawn_ByGameObject_Works()
        {
            var prefab = CreatePrefab();
            _nm.RegisterPrefab(prefab, 0);

            var spawned = _nm.Spawn(prefab, Vector3.one, Quaternion.identity);

            Assert.IsNotNull(spawned);
            Assert.IsTrue(spawned.IsRegistered);
            Assert.AreEqual(0, spawned.PrefabId);

            Object.DestroyImmediate(prefab);
        }

        [Test]
        public void Spawn_ByGameObject_AutoRegisters()
        {
            var prefab = CreatePrefab();

            // Not registered — should auto-register
            Assert.AreEqual(-1, _nm.GetPrefabId(prefab));

            var spawned = _nm.Spawn(prefab, Vector3.zero, Quaternion.identity);

            Assert.IsNotNull(spawned);
            Assert.IsTrue(spawned.IsRegistered);
            Assert.GreaterOrEqual(_nm.GetPrefabId(prefab), 0);

            Object.DestroyImmediate(prefab);
        }

        #endregion

        #region Spawn(string)

        [Test]
        public void Spawn_ByName_Works()
        {
            var prefab = CreatePrefab("MyPrefab");
            _nm.RegisterPrefab(prefab, 0);

            var spawned = _nm.Spawn("MyPrefab", Vector3.zero, Quaternion.identity);

            Assert.IsNotNull(spawned);
            Assert.IsTrue(spawned.IsRegistered);

            Object.DestroyImmediate(prefab);
        }

        [Test]
        public void Spawn_ByName_NotFound_ReturnsNull()
        {
            LogAssert.Expect(LogType.Error, "[NetworkManager] No prefab registered with name 'DoesNotExist'");

            var result = _nm.Spawn("DoesNotExist", Vector3.zero, Quaternion.identity);

            Assert.IsNull(result);
        }

        #endregion
    }
}
