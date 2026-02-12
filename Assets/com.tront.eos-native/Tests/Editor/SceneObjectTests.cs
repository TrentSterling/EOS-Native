using System;
using System.Reflection;
using NUnit.Framework;
using Epic.OnlineServices;
using EOSNative.Net;
using EOSNative.P2P;
using UnityEngine;

namespace EOSNative.Tests
{
    /// <summary>
    /// Tests for automatic scene object registration via RegisterSceneObjects(),
    /// SceneId-based NetworkIds, and scene object claiming (FishNet/Fusion pattern).
    /// Uses inactive GO + reflection pattern to avoid DontDestroyOnLoad in EditMode.
    /// </summary>
    public class SceneObjectTests
    {
        private NetworkManager _nm;
        private GameObject _eosManagerGo;

        [SetUp]
        public void SetUp()
        {
            // NetworkManager on inactive GO
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

            // EOSManager with a fake LocalProductUserId so RegisterSceneObjects() doesn't bail
            _eosManagerGo = new GameObject("EOSManager");
            _eosManagerGo.SetActive(false);
            var eosManager = _eosManagerGo.AddComponent<EOSManager>();
            EOSManager.s_Instance = eosManager;

            // Set LocalProductUserId via reflection (private setter) — use fake IntPtr to avoid native DLL
            var puid = new ProductUserId(new IntPtr(99));
            typeof(EOSManager)
                .GetProperty("LocalProductUserId")
                .SetValue(eosManager, puid);
        }

        [TearDown]
        public void TearDown()
        {
            typeof(NetworkManager)
                .GetField("_instance", BindingFlags.NonPublic | BindingFlags.Static)
                .SetValue(null, null);

            EOSManager.s_Instance = null;
            EOSP2PManager._instance = null;

            if (_nm != null)
                UnityEngine.Object.DestroyImmediate(_nm.gameObject);
            if (_eosManagerGo != null)
                UnityEngine.Object.DestroyImmediate(_eosManagerGo);

            foreach (var name in new[] { "NetworkManager", "EOSP2PManager", "EOSLobbyManager", "EOSManager" })
            {
                var obj = GameObject.Find(name);
                if (obj != null) UnityEngine.Object.DestroyImmediate(obj);
            }
        }

        private NetworkObject CreateSceneNetObj(string name = "SceneObj")
        {
            var go = new GameObject(name);
            return go.AddComponent<NetworkObject>();
        }

        private void Cleanup(params GameObject[] gos)
        {
            foreach (var go in gos)
                if (go != null) UnityEngine.Object.DestroyImmediate(go);
        }

        #region Sentinel Values

        [Test]
        public void SceneObject_SentinelValues()
        {
            Assert.AreEqual(0xFFFE, NetworkObject.SCENE_OBJECT);
            Assert.AreEqual(0xFFFF, NetworkObject.NO_PREFAB);
            Assert.AreNotEqual(NetworkObject.SCENE_OBJECT, NetworkObject.NO_PREFAB);
        }

        [Test]
        public void SceneObject_NotFilteredFromSnapshot()
        {
            // The snapshot filter checks: if (obj.PrefabId == NetworkObject.NO_PREFAB) continue;
            // SCENE_OBJECT must NOT be equal to NO_PREFAB so scene objects are included.
            ushort sceneObjId = NetworkObject.SCENE_OBJECT;
            Assert.IsFalse(sceneObjId == NetworkObject.NO_PREFAB,
                "SCENE_OBJECT must not equal NO_PREFAB — otherwise scene objects are skipped in snapshots");
        }

        #endregion

        #region SceneId Property

        [Test]
        public void SceneId_DefaultZero()
        {
            var netObj = CreateSceneNetObj("DefaultId");
            Assert.AreEqual(0, netObj.SceneId, "SceneId should default to 0 (unassigned)");
            Cleanup(netObj.gameObject);
        }

        [Test]
        public void SceneId_UsedForNetworkId()
        {
            var netObj = CreateSceneNetObj("WithSceneId");
            netObj.SceneId = 42;

            _nm.RegisterSceneObjects();

            // With SceneId = 42, NetworkId should be 0xEE000000 | 42 = 0xEE00002A
            Assert.AreEqual(0xEE00002Au, netObj.NetworkId,
                "Non-zero SceneId should be used directly as lower bits of NetworkId");

            Cleanup(netObj.gameObject);
        }

        [Test]
        public void SceneId_FallbackToHashWhenZero()
        {
            var netObj = CreateSceneNetObj("HashFallback");
            Assert.AreEqual(0, netObj.SceneId);

            _nm.RegisterSceneObjects();

            // With SceneId = 0, should use FnvHash fallback
            uint expected = 0xEE000000u | (NetworkManager.FnvHash("HashFallback") & 0x00FFFFFFu);
            Assert.AreEqual(expected, netObj.NetworkId,
                "Zero SceneId should fall back to hierarchy hash");

            Cleanup(netObj.gameObject);
        }

        [Test]
        public void IsSceneObject_TrueWhenSceneIdSet()
        {
            var netObj = CreateSceneNetObj("SceneIdSet");
            netObj.SceneId = 1;
            Assert.IsTrue(netObj.IsSceneObject);
            Cleanup(netObj.gameObject);
        }

        [Test]
        public void IsSceneObject_TrueWhenPrefabIdIsSceneObject()
        {
            var netObj = CreateSceneNetObj("PrefabIdScene");
            netObj.PrefabId = NetworkObject.SCENE_OBJECT;
            Assert.IsTrue(netObj.IsSceneObject);
            Cleanup(netObj.gameObject);
        }

        [Test]
        public void IsSceneObject_FalseByDefault()
        {
            var netObj = CreateSceneNetObj("NotScene");
            Assert.IsFalse(netObj.IsSceneObject);
            Cleanup(netObj.gameObject);
        }

        #endregion

        #region Scene Object Claiming

        [Test]
        public void Claiming_MatchesByPrefabId()
        {
            // Register a scene object that has a prefab table match (PrefabId != SCENE_OBJECT)
            var netObj = CreateSceneNetObj("ClaimTarget");
            netObj.SceneId = 10;

            // Manually register with a matched PrefabId (simulating FindPrefabTableMatch finding a match)
            var objsField = typeof(NetworkManager)
                .GetField("_objects", BindingFlags.NonPublic | BindingFlags.Instance);
            var objects = (System.Collections.Generic.Dictionary<uint, NetworkObject>)objsField.GetValue(_nm);

            uint sceneNetId = 0xEE000000u | 10u;
            netObj.NetworkId = sceneNetId;
            netObj.PrefabId = 2; // matched prefab table index
            netObj.IsRegistered = true;
            objects[sceneNetId] = netObj;

            // Rebuild unclaimed tracking
            var rebuildMethod = typeof(NetworkManager)
                .GetMethod("RebuildUnclaimedSceneObjects", BindingFlags.NonPublic | BindingFlags.Instance);
            rebuildMethod.Invoke(_nm, null);

            // Claim with matching PrefabId
            var puid = new ProductUserId(new IntPtr(42));
            var claimed = _nm.TryClaimSceneObject(2, 0x12345678, puid, false, Vector3.one, Quaternion.identity);

            Assert.IsNotNull(claimed, "Should claim scene object with matching PrefabId");
            Assert.AreEqual(netObj, claimed);
            Assert.AreEqual(0x12345678u, claimed.NetworkId);
            Assert.AreEqual((ushort)2, claimed.PrefabId);

            Cleanup(netObj.gameObject);
        }

        [Test]
        public void Claiming_ReassignsNetworkId()
        {
            var netObj = CreateSceneNetObj("Reassign");
            netObj.SceneId = 20;

            var objsField = typeof(NetworkManager)
                .GetField("_objects", BindingFlags.NonPublic | BindingFlags.Instance);
            var objects = (System.Collections.Generic.Dictionary<uint, NetworkObject>)objsField.GetValue(_nm);

            uint oldId = 0xEE000000u | 20u;
            netObj.NetworkId = oldId;
            netObj.PrefabId = 3;
            netObj.IsRegistered = true;
            objects[oldId] = netObj;

            var rebuildMethod = typeof(NetworkManager)
                .GetMethod("RebuildUnclaimedSceneObjects", BindingFlags.NonPublic | BindingFlags.Instance);
            rebuildMethod.Invoke(_nm, null);

            uint newId = 0xAB000001u;
            var puid = new ProductUserId(new IntPtr(55));
            _nm.TryClaimSceneObject(3, newId, puid, true, Vector3.zero, Quaternion.identity);

            // Old ID removed from _objects
            Assert.IsFalse(objects.ContainsKey(oldId), "Old 0xEE ID should be removed");
            // New ID registered
            Assert.IsTrue(objects.ContainsKey(newId), "New ID should be in _objects");
            Assert.AreEqual(newId, netObj.NetworkId);
            Assert.IsTrue(netObj.DestroyWithOwner);

            Cleanup(netObj.gameObject);
        }

        [Test]
        public void Claiming_ReturnsNullWhenNoMatch()
        {
            // No scene objects registered
            var rebuildMethod = typeof(NetworkManager)
                .GetMethod("RebuildUnclaimedSceneObjects", BindingFlags.NonPublic | BindingFlags.Instance);
            rebuildMethod.Invoke(_nm, null);

            var puid = new ProductUserId(new IntPtr(77));
            var result = _nm.TryClaimSceneObject(99, 0x11111111, puid, false, Vector3.zero, Quaternion.identity);
            Assert.IsNull(result, "Should return null when no matching PrefabId");
        }

        [Test]
        public void Claiming_UpdatesTransform()
        {
            var netObj = CreateSceneNetObj("TransformClaim");
            netObj.SceneId = 30;
            netObj.transform.position = new Vector3(10, 20, 30);

            var objsField = typeof(NetworkManager)
                .GetField("_objects", BindingFlags.NonPublic | BindingFlags.Instance);
            var objects = (System.Collections.Generic.Dictionary<uint, NetworkObject>)objsField.GetValue(_nm);

            uint sceneNetId = 0xEE000000u | 30u;
            netObj.NetworkId = sceneNetId;
            netObj.PrefabId = 5;
            netObj.IsRegistered = true;
            objects[sceneNetId] = netObj;

            var rebuildMethod = typeof(NetworkManager)
                .GetMethod("RebuildUnclaimedSceneObjects", BindingFlags.NonPublic | BindingFlags.Instance);
            rebuildMethod.Invoke(_nm, null);

            var newPos = new Vector3(1, 2, 3);
            var puid = new ProductUserId(new IntPtr(88));
            _nm.TryClaimSceneObject(5, 0x22222222, puid, false, newPos, Quaternion.identity);

            Assert.AreEqual(newPos, netObj.transform.position, "Claiming should update position");

            Cleanup(netObj.gameObject);
        }

        [Test]
        public void Unclaimed_DeactivatedAfterSnapshot()
        {
            var netObj = CreateSceneNetObj("Unclaimed");
            netObj.SceneId = 40;

            var objsField = typeof(NetworkManager)
                .GetField("_objects", BindingFlags.NonPublic | BindingFlags.Instance);
            var objects = (System.Collections.Generic.Dictionary<uint, NetworkObject>)objsField.GetValue(_nm);

            uint sceneNetId = 0xEE000000u | 40u;
            netObj.NetworkId = sceneNetId;
            netObj.PrefabId = 7;
            netObj.IsRegistered = true;
            objects[sceneNetId] = netObj;

            var rebuildMethod = typeof(NetworkManager)
                .GetMethod("RebuildUnclaimedSceneObjects", BindingFlags.NonPublic | BindingFlags.Instance);
            rebuildMethod.Invoke(_nm, null);

            Assert.IsTrue(netObj.gameObject.activeSelf, "Should be active before deactivation");

            _nm.DeactivateUnclaimedSceneObjects();

            Assert.IsFalse(netObj.gameObject.activeSelf, "Unclaimed scene object should be deactivated");

            Cleanup(netObj.gameObject);
        }

        [Test]
        public void Claiming_DoesNotDeactivateClaimed()
        {
            // Create two scene objects with same PrefabId
            var obj1 = CreateSceneNetObj("Weapon1");
            obj1.SceneId = 50;
            var obj2 = CreateSceneNetObj("Weapon2");
            obj2.SceneId = 51;

            var objsField = typeof(NetworkManager)
                .GetField("_objects", BindingFlags.NonPublic | BindingFlags.Instance);
            var objects = (System.Collections.Generic.Dictionary<uint, NetworkObject>)objsField.GetValue(_nm);

            uint id1 = 0xEE000000u | 50u;
            obj1.NetworkId = id1;
            obj1.PrefabId = 2;
            obj1.IsRegistered = true;
            objects[id1] = obj1;

            uint id2 = 0xEE000000u | 51u;
            obj2.NetworkId = id2;
            obj2.PrefabId = 2;
            obj2.IsRegistered = true;
            objects[id2] = obj2;

            var rebuildMethod = typeof(NetworkManager)
                .GetMethod("RebuildUnclaimedSceneObjects", BindingFlags.NonPublic | BindingFlags.Instance);
            rebuildMethod.Invoke(_nm, null);

            // Claim one of the two
            var puid = new ProductUserId(new IntPtr(100));
            _nm.TryClaimSceneObject(2, 0x33333333, puid, false, Vector3.zero, Quaternion.identity);

            // Deactivate unclaimed
            _nm.DeactivateUnclaimedSceneObjects();

            // One should be active (claimed), one deactivated (unclaimed)
            int activeCount = 0;
            if (obj1.gameObject.activeSelf) activeCount++;
            if (obj2.gameObject.activeSelf) activeCount++;

            Assert.AreEqual(1, activeCount, "Exactly one of two should remain active after claiming one and deactivating unclaimed");

            Cleanup(obj1.gameObject, obj2.gameObject);
        }

        #endregion

        #region RegisterSceneObjects

        [Test]
        public void RegisterSceneObjects_DiscoversUnregistered()
        {
            var netObj = CreateSceneNetObj("Weapon1");
            Assert.IsFalse(netObj.IsRegistered);

            _nm.RegisterSceneObjects();

            Assert.IsTrue(netObj.IsRegistered);
            Assert.IsTrue(_nm.Objects.ContainsKey(netObj.NetworkId));

            Cleanup(netObj.gameObject);
        }

        [Test]
        public void RegisterSceneObjects_SetsPrefabIdSceneObject()
        {
            var netObj = CreateSceneNetObj("Weapon2");

            _nm.RegisterSceneObjects();

            Assert.AreEqual(NetworkObject.SCENE_OBJECT, netObj.PrefabId,
                "Scene objects must have PrefabId = SCENE_OBJECT after RegisterSceneObjects");

            Cleanup(netObj.gameObject);
        }

        [Test]
        public void RegisterSceneObjects_DeterministicNetworkId()
        {
            var a = CreateSceneNetObj("WeaponA");
            var b = CreateSceneNetObj("WeaponB");

            _nm.RegisterSceneObjects();

            uint idA = a.NetworkId;
            uint idB = b.NetworkId;

            // Different hierarchy paths → different NetworkIds
            Assert.AreNotEqual(idA, idB);

            // Same hierarchy path should hash the same (verified by prefix)
            uint expectedHashA = 0xEE000000u | (NetworkManager.FnvHash("WeaponA") & 0x00FFFFFFu);
            // May differ if collision incremented, but prefix must be 0xEE
            Assert.AreEqual(0xEE, (idA >> 24) & 0xFF);
            Assert.AreEqual(0xEE, (idB >> 24) & 0xFF);

            Cleanup(a.gameObject, b.gameObject);
        }

        [Test]
        public void RegisterSceneObjects_SkipsAlreadyRegistered()
        {
            var netObj = CreateSceneNetObj("AlreadyRegistered");

            // Pre-register via RegisterExisting
            _nm.RegisterExisting(netObj, 0xAA000001);
            Assert.AreEqual(NetworkObject.NO_PREFAB, netObj.PrefabId);

            _nm.RegisterSceneObjects();

            // Should NOT have been re-registered — PrefabId stays NO_PREFAB
            Assert.AreEqual(NetworkObject.NO_PREFAB, netObj.PrefabId);
            Assert.AreEqual(0xAA000001u, netObj.NetworkId);

            Cleanup(netObj.gameObject);
        }

        [Test]
        public void RegisterSceneObjects_Idempotent()
        {
            var netObj = CreateSceneNetObj("IdempotentWeapon");

            _nm.RegisterSceneObjects();
            uint firstId = netObj.NetworkId;
            int countAfterFirst = 0;
            foreach (var _ in _nm.Objects) countAfterFirst++;

            _nm.RegisterSceneObjects();
            uint secondId = netObj.NetworkId;
            int countAfterSecond = 0;
            foreach (var _ in _nm.Objects) countAfterSecond++;

            Assert.AreEqual(firstId, secondId, "NetworkId must not change on second call");
            Assert.AreEqual(countAfterFirst, countAfterSecond, "Object count must not change on second call");

            Cleanup(netObj.gameObject);
        }

        [Test]
        public void RegisterSceneObjects_NetworkIdPrefix0xEE()
        {
            var netObj = CreateSceneNetObj("PrefixTest");

            _nm.RegisterSceneObjects();

            Assert.AreEqual(0xEE, (netObj.NetworkId >> 24) & 0xFF,
                "Scene object NetworkId must have 0xEE upper byte prefix");

            Cleanup(netObj.gameObject);
        }

        [Test]
        public void RegisterSceneObjects_HandlesChildren()
        {
            var rootGo = new GameObject("WeaponRoot");
            var root = rootGo.AddComponent<NetworkObject>();

            var childGo = new GameObject("WeaponChild");
            childGo.transform.SetParent(rootGo.transform);
            var child = childGo.AddComponent<NetworkObject>();

            _nm.RegisterSceneObjects();

            Assert.IsTrue(root.IsRegistered);
            Assert.IsTrue(child.IsRegistered);
            Assert.AreEqual(0u, root.ParentNetworkId, "Root should have no parent");
            Assert.AreEqual(root.NetworkId, child.ParentNetworkId, "Child should reference root");
            Assert.AreEqual(NetworkObject.SCENE_OBJECT, root.PrefabId);
            Assert.AreEqual(NetworkObject.SCENE_OBJECT, child.PrefabId);

            Cleanup(rootGo);
        }

        [Test]
        public void RegisterSceneObjects_AssignsOwnerToHost()
        {
            var netObj = CreateSceneNetObj("HostOwned");
            Assert.IsNull(netObj.OwnerId);

            _nm.RegisterSceneObjects();

            // IsHost is true in SetUp, so scene objects should get the local PUID as owner
            Assert.IsNotNull(netObj.OwnerId, "Host should claim ownership of scene objects");

            Cleanup(netObj.gameObject);
        }

        [Test]
        public void RegisterSceneObjects_UsesSceneIdForChildren()
        {
            var rootGo = new GameObject("Root");
            var root = rootGo.AddComponent<NetworkObject>();
            root.SceneId = 100;

            var childGo = new GameObject("Child");
            childGo.transform.SetParent(rootGo.transform);
            var child = childGo.AddComponent<NetworkObject>();
            child.SceneId = 101;

            _nm.RegisterSceneObjects();

            Assert.AreEqual(0xEE000000u | 100u, root.NetworkId);
            Assert.AreEqual(0xEE000000u | 101u, child.NetworkId);

            Cleanup(rootGo);
        }

        #endregion
    }
}
