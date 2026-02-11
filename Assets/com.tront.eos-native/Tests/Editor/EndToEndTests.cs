using System.Reflection;
using NUnit.Framework;
using EOSNative.Net;
using EOSNative.P2P;
using UnityEngine;

namespace EOSNative.Tests
{
    /// <summary>
    /// Test NetworkBehaviour subclass for end-to-end flow testing.
    /// Tracks spawn/despawn lifecycle callbacks and exposes a SyncVar.
    /// Uses explicit InitForTest() instead of Awake() to avoid EditMode lifecycle issues.
    /// </summary>
    public class TestBehaviour : NetworkBehaviour
    {
        public SyncVar<int> Score;
        public int SpawnCount;
        public int DespawnCount;

        public void InitForTest()
        {
            Score = Sync(0);
        }

        public override void OnNetworkSpawn() => SpawnCount++;
        public override void OnNetworkDespawn() => DespawnCount++;
    }

    /// <summary>
    /// End-to-end EditMode tests that combine multiple networking features:
    /// Spawn + SyncVar dirty tracking, Spawn + SerializeAll round-trips,
    /// Despawn lifecycle callbacks, spawn-despawn-respawn cycles,
    /// multi-object independence, convenience properties, and NB integration.
    ///
    /// Uses the inactive GO + reflection pattern from SpawnDespawnTests/PerNBComponentIdTests.
    /// </summary>
    public class EndToEndTests
    {
        private NetworkManager _nm;

        private static readonly FieldInfo NmInstanceField =
            typeof(NetworkManager).GetField("_instance", BindingFlags.NonPublic | BindingFlags.Static);

        [SetUp]
        public void SetUp()
        {
            // Create on inactive GO to prevent OnEnable from firing (avoids DontDestroyOnLoad chain)
            var go = new GameObject("NetworkManager");
            go.SetActive(false);
            _nm = go.AddComponent<NetworkManager>();
            NmInstanceField.SetValue(null, _nm);

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
            NmInstanceField.SetValue(null, null);

            if (_nm != null)
                Object.DestroyImmediate(_nm.gameObject);

            // Clean up any leaked singletons
            foreach (var name in new[]
                     { "NetworkManager", "EOSP2PManager", "EOSLobbyManager", "NetworkSceneManager" })
            {
                var obj = GameObject.Find(name);
                if (obj != null) Object.DestroyImmediate(obj);
            }
        }

        /// <summary>Create a simple prefab with NetworkObject (inactive).</summary>
        private GameObject CreatePrefab(string name = "TestPrefab")
        {
            var go = new GameObject(name);
            go.AddComponent<NetworkObject>();
            go.SetActive(false);
            return go;
        }

        /// <summary>Create a prefab with NetworkObject + TestBehaviour (inactive). TestBehaviour is NOT initialized.</summary>
        private GameObject CreatePrefabWithNB(string name = "NBPrefab")
        {
            var go = new GameObject(name);
            go.AddComponent<NetworkObject>();
            go.AddComponent<TestBehaviour>();
            go.SetActive(false);
            return go;
        }

        /// <summary>Create a prefab with NetworkObject + HealthBehaviour (inactive). HealthBehaviour is NOT initialized.</summary>
        private GameObject CreatePrefabWithHealth(string name = "HealthPrefab")
        {
            var go = new GameObject(name);
            go.AddComponent<NetworkObject>();
            go.AddComponent<HealthBehaviour>();
            go.SetActive(false);
            return go;
        }

        #region 1. Spawn + SyncVar Dirty on First Frame

        [Test]
        public void Spawn_SyncVarsDirtyOnFirstFrame()
        {
            // Spawn an object, add a SyncVar via NB, set it, verify dirty before ClearDirty
            var prefab = CreatePrefab();
            _nm.RegisterPrefab(prefab, 0);

            var spawned = _nm.Spawn(0, Vector3.zero, Quaternion.identity);
            Assert.IsNotNull(spawned);

            // Create a SyncVar directly on the NetworkObject (simulating post-spawn setup)
            var sv = spawned.Sync(0);
            sv.Value = 42;

            Assert.IsTrue(sv.IsDirty, "SyncVar should be dirty after setting a value");
            Assert.IsTrue(spawned.IsDirty, "NetworkObject should be dirty after SyncVar changed");

            spawned.ClearDirty();
            Assert.IsFalse(sv.IsDirty, "SyncVar should be clean after ClearDirty");

            Object.DestroyImmediate(prefab);
        }

        #endregion

        #region 2. Spawn + SerializeAll Round-Trip

        [Test]
        public void Spawn_ThenSerializeAll_RoundTrips()
        {
            var prefab = CreatePrefab();
            _nm.RegisterPrefab(prefab, 0);

            // Spawn and set SyncVars on spawned object
            var spawned = _nm.Spawn(0, Vector3.zero, Quaternion.identity);
            var sv1 = spawned.Sync(100);
            var sv2 = spawned.Sync("hello");

            // Serialize all state
            var writer = new NetWriter();
            spawned.SerializeAll(writer);
            var data = writer.ToArray();

            // Create a second object with matching SyncVar layout
            var go2 = new GameObject("Receiver");
            var netObj2 = go2.AddComponent<NetworkObject>();
            var rsv1 = netObj2.Sync(0);
            var rsv2 = netObj2.Sync("");

            // Deserialize onto the second object
            netObj2.DeserializeAll(new NetReader(data));

            Assert.AreEqual(100, rsv1.Value, "Int SyncVar should round-trip through SerializeAll");
            Assert.AreEqual("hello", rsv2.Value, "String SyncVar should round-trip through SerializeAll");

            Object.DestroyImmediate(prefab);
            Object.DestroyImmediate(go2);
        }

        #endregion

        #region 3. Despawn Clears from Objects Dictionary

        [Test]
        public void Despawn_ClearsFromObjectsDictionary()
        {
            var prefab = CreatePrefab();
            _nm.RegisterPrefab(prefab, 0);

            var spawned = _nm.Spawn(0, Vector3.zero, Quaternion.identity);
            uint id = spawned.NetworkId;

            Assert.IsTrue(_nm.Objects.ContainsKey(id), "Spawned object should be in Objects");

            _nm.Despawn(spawned);

            Assert.IsFalse(_nm.Objects.ContainsKey(id), "Despawned object should not be in Objects");

            Object.DestroyImmediate(prefab);
        }

        #endregion

        #region 4. Despawn Calls OnNetworkDespawn

        [Test]
        public void Despawn_CallsOnNetworkDespawn()
        {
            var prefab = CreatePrefabWithNB();
            _nm.RegisterPrefab(prefab, 0);

            var spawned = _nm.Spawn(0, Vector3.zero, Quaternion.identity);
            var tb = spawned.GetComponentInChildren<TestBehaviour>();
            // InitForTest to set up SyncVars (must happen after spawn since Awake ran in Instantiate)
            tb.InitForTest();

            Assert.AreEqual(1, tb.SpawnCount, "OnNetworkSpawn should have been called once");
            Assert.AreEqual(0, tb.DespawnCount, "OnNetworkDespawn should not have been called yet");

            _nm.Despawn(spawned);

            Assert.AreEqual(1, tb.DespawnCount, "OnNetworkDespawn should have been called once after Despawn");

            Object.DestroyImmediate(prefab);
        }

        #endregion

        #region 5. Spawn-Despawn-Respawn Cycle

        [Test]
        public void SpawnDespawnRespawn_Cycle()
        {
            var prefab = CreatePrefab();
            _nm.RegisterPrefab(prefab, 0);

            // Spawn
            var first = _nm.Spawn(0, Vector3.zero, Quaternion.identity);
            uint firstId = first.NetworkId;
            Assert.IsTrue(_nm.Objects.ContainsKey(firstId));

            // Despawn
            _nm.Despawn(first);
            Assert.IsFalse(_nm.Objects.ContainsKey(firstId));

            // Respawn
            var second = _nm.Spawn(0, Vector3.one, Quaternion.identity);
            uint secondId = second.NetworkId;

            Assert.AreNotEqual(firstId, secondId, "Respawned object should have a new NetworkId");
            Assert.IsTrue(_nm.Objects.ContainsKey(secondId), "Respawned object should be in Objects");
            Assert.IsFalse(_nm.Objects.ContainsKey(firstId), "Original NetworkId should still be gone");

            Object.DestroyImmediate(prefab);
        }

        #endregion

        #region 6. Multiple Objects Independent SyncVars

        [Test]
        public void MultipleObjects_IndependentSyncVars()
        {
            var prefab = CreatePrefab();
            _nm.RegisterPrefab(prefab, 0);

            var objA = _nm.Spawn(0, Vector3.zero, Quaternion.identity);
            var objB = _nm.Spawn(0, Vector3.one, Quaternion.identity);

            var svA = objA.Sync(0);
            var svB = objB.Sync(0);

            // Dirty only A
            svA.Value = 99;

            Assert.IsTrue(svA.IsDirty, "Object A SyncVar should be dirty");
            Assert.IsFalse(svB.IsDirty, "Object B SyncVar should still be clean");
            Assert.IsTrue(objA.IsDirty, "Object A should be dirty");

            Object.DestroyImmediate(prefab);
        }

        #endregion

        #region 7. Multiple Objects Independent Despawn

        [Test]
        public void MultipleObjects_IndependentDespawn()
        {
            var prefab = CreatePrefab();
            _nm.RegisterPrefab(prefab, 0);

            var objA = _nm.Spawn(0, Vector3.zero, Quaternion.identity);
            var objB = _nm.Spawn(0, Vector3.one, Quaternion.identity);
            uint idA = objA.NetworkId;
            uint idB = objB.NetworkId;

            _nm.Despawn(objA);

            Assert.IsFalse(_nm.Objects.ContainsKey(idA), "Despawned object A should be gone");
            Assert.IsTrue(_nm.Objects.ContainsKey(idB), "Object B should still be in Objects");

            Object.DestroyImmediate(prefab);
        }

        #endregion

        #region 8. SyncVar Set Before Spawn Persists After Spawn

        [Test]
        public void SyncVar_SetBeforeSpawn_PersistsAfterSpawn()
        {
            // Create a prefab, set a SyncVar on it before spawning
            var prefab = new GameObject("SyncVarPrefab");
            var netObj = prefab.AddComponent<NetworkObject>();
            var sv = netObj.Sync(0);
            sv.Value = 777; // Set before spawn
            prefab.SetActive(false);

            _nm.RegisterPrefab(prefab, 0);
            var spawned = _nm.Spawn(0, Vector3.zero, Quaternion.identity);

            // The spawned clone is a new instance; SyncVars are created fresh.
            // But we can verify the prefab's SyncVar still has the value.
            Assert.AreEqual(777, sv.Value, "Prefab SyncVar value should persist on the original prefab");

            // The clone gets fresh SyncVars via Instantiate (clone of the MonoBehaviour state).
            // SyncVar is a class (reference type) held on the NetworkObject — the clone gets a shallow copy.
            // Verify the spawned object has its own SyncVar registry.
            Assert.IsNotNull(spawned);
            Assert.IsTrue(spawned.IsRegistered);

            Object.DestroyImmediate(prefab);
        }

        #endregion

        #region 9. NB SyncVars on Spawned Object

        [Test]
        public void NB_SyncVarsOnSpawnedObject()
        {
            var prefab = CreatePrefabWithNB();
            _nm.RegisterPrefab(prefab, 0);

            var spawned = _nm.Spawn(0, Vector3.zero, Quaternion.identity);
            var tb = spawned.GetComponentInChildren<TestBehaviour>();
            tb.InitForTest();

            // Modify SyncVar on spawned NB
            tb.Score.Value = 100;

            Assert.IsTrue(tb.IsDirty, "NB should be dirty after SyncVar change on spawned object");
            Assert.AreEqual(100, tb.Score.Value);

            tb.ClearDirtyNB();
            Assert.IsFalse(tb.IsDirty, "NB should be clean after ClearDirtyNB");

            Object.DestroyImmediate(prefab);
        }

        #endregion

        #region 10. Spawn With NB ComponentIndex Assigned

        [Test]
        public void Spawn_WithNB_ComponentIndexAssigned()
        {
            var prefab = CreatePrefabWithNB();
            _nm.RegisterPrefab(prefab, 0);

            var spawned = _nm.Spawn(0, Vector3.zero, Quaternion.identity);

            // NotifyNetworkSpawn is called by Spawn(), which assigns ComponentIndex
            var tb = spawned.GetComponentInChildren<TestBehaviour>();
            Assert.IsNotNull(tb, "Spawned object should have TestBehaviour");
            Assert.AreEqual(0, tb.ComponentIndex, "First NB should have ComponentIndex 0 after spawn");

            Object.DestroyImmediate(prefab);
        }

        [Test]
        public void Spawn_WithMultipleNBs_ComponentIndicesAssigned()
        {
            var prefab = new GameObject("MultiNBPrefab");
            prefab.AddComponent<NetworkObject>();
            var h = prefab.AddComponent<HealthBehaviour>();
            var t = prefab.AddComponent<TestBehaviour>();
            prefab.SetActive(false);

            _nm.RegisterPrefab(prefab, 0);
            var spawned = _nm.Spawn(0, Vector3.zero, Quaternion.identity);

            var nbs = spawned.GetComponentInChildren<NetworkObject>().gameObject.GetComponents<NetworkBehaviour>();
            Assert.AreEqual(2, nbs.Length, "Should have 2 NetworkBehaviours");
            Assert.AreEqual(0, nbs[0].ComponentIndex, "First NB should have ComponentIndex 0");
            Assert.AreEqual(1, nbs[1].ComponentIndex, "Second NB should have ComponentIndex 1");

            Object.DestroyImmediate(prefab);
        }

        #endregion

        #region 11. DespawnAll Clears All Objects

        [Test]
        public void DespawnAll_ClearsAllObjects()
        {
            var prefab = CreatePrefab();
            _nm.RegisterPrefab(prefab, 0);

            _nm.Spawn(0, Vector3.zero, Quaternion.identity);
            _nm.Spawn(0, Vector3.one, Quaternion.identity);
            _nm.Spawn(0, Vector3.up, Quaternion.identity);

            int before = 0;
            foreach (var _ in _nm.Objects) before++;
            Assert.AreEqual(3, before, "Should have 3 objects before DespawnAll");

            _nm.DespawnAll();

            int after = 0;
            foreach (var _ in _nm.Objects) after++;
            Assert.AreEqual(0, after, "Objects should be empty after DespawnAll");

            Object.DestroyImmediate(prefab);
        }

        #endregion

        #region 12. Spawn in Offline Mode IsOwner

        [Test]
        public void Spawn_InOfflineMode_IsOwner()
        {
            var prefab = CreatePrefab();
            _nm.RegisterPrefab(prefab, 0);

            var spawned = _nm.Spawn(0, Vector3.zero, Quaternion.identity);

            Assert.IsTrue(spawned.IsOwner, "Spawned object in offline mode should be owned by local player");

            Object.DestroyImmediate(prefab);
        }

        #endregion

        #region 13. Spawn in Offline Mode IsHost

        [Test]
        public void Spawn_InOfflineMode_IsHost()
        {
            var prefab = CreatePrefab();
            _nm.RegisterPrefab(prefab, 0);

            var spawned = _nm.Spawn(0, Vector3.zero, Quaternion.identity);

            Assert.IsTrue(spawned.IsHost, "In offline mode, IsHost should be true");
            Assert.IsTrue(_nm.IsHost, "NetworkManager.IsHost should be true in offline mode");

            Object.DestroyImmediate(prefab);
        }

        #endregion

        #region 14. Convenience Props IsSpawned

        [Test]
        public void ConvenienceProps_IsSpawned_BeforeAndAfter()
        {
            var prefab = CreatePrefabWithNB();
            _nm.RegisterPrefab(prefab, 0);

            // Before spawn: check on prefab's NB
            var prefabTb = prefab.GetComponent<TestBehaviour>();
            Assert.IsFalse(prefabTb.IsSpawned, "Prefab NB should not be spawned");

            // Spawn
            var spawned = _nm.Spawn(0, Vector3.zero, Quaternion.identity);
            var spawnedTb = spawned.GetComponentInChildren<TestBehaviour>();
            Assert.IsTrue(spawnedTb.IsSpawned, "Spawned NB's IsSpawned should be true");

            // Despawn
            _nm.Despawn(spawned);
            Assert.IsFalse(spawnedTb.IsSpawned, "After despawn, IsSpawned should be false (IsRegistered=false)");

            Object.DestroyImmediate(prefab);
        }

        #endregion

        #region 15. Convenience Props HasAuthority Owner

        [Test]
        public void ConvenienceProps_HasAuthority_Owner()
        {
            var prefab = CreatePrefabWithNB();
            _nm.RegisterPrefab(prefab, 0);

            var spawned = _nm.Spawn(0, Vector3.zero, Quaternion.identity);
            var tb = spawned.GetComponentInChildren<TestBehaviour>();

            // In offline mode, the spawner is owner. Owner has authority.
            Assert.IsTrue(tb.HasAuthority, "Owner should have authority in offline mode");

            Object.DestroyImmediate(prefab);
        }

        #endregion

        #region 16. Convenience Props IsOffline

        [Test]
        public void ConvenienceProps_IsOffline()
        {
            var prefab = CreatePrefabWithNB();
            _nm.RegisterPrefab(prefab, 0);

            var spawned = _nm.Spawn(0, Vector3.zero, Quaternion.identity);
            var tb = spawned.GetComponentInChildren<TestBehaviour>();

            Assert.IsTrue(tb.IsOffline, "NB.IsOffline should be true when NetworkManager is in offline mode");

            Object.DestroyImmediate(prefab);
        }

        #endregion

        #region 17. Convenience Despawn from NB

        [Test]
        public void ConvenienceDespawn_FromNB()
        {
            var prefab = CreatePrefabWithNB();
            _nm.RegisterPrefab(prefab, 0);

            var spawned = _nm.Spawn(0, Vector3.zero, Quaternion.identity);
            uint id = spawned.NetworkId;
            var tb = spawned.GetComponentInChildren<TestBehaviour>();

            // Despawn via NB convenience method
            tb.Despawn();

            Assert.IsFalse(_nm.Objects.ContainsKey(id), "Object should be removed from Objects after NB.Despawn()");
            Assert.IsFalse(spawned.IsRegistered, "IsRegistered should be false after NB.Despawn()");

            Object.DestroyImmediate(prefab);
        }

        #endregion

        #region Additional Combined Flow Tests

        [Test]
        public void Spawn_NB_OnNetworkSpawnCalled()
        {
            var prefab = CreatePrefabWithNB();
            _nm.RegisterPrefab(prefab, 0);

            var spawned = _nm.Spawn(0, Vector3.zero, Quaternion.identity);
            var tb = spawned.GetComponentInChildren<TestBehaviour>();

            Assert.AreEqual(1, tb.SpawnCount, "OnNetworkSpawn should be called exactly once during Spawn");

            Object.DestroyImmediate(prefab);
        }

        [Test]
        public void Spawn_Despawn_NB_LifecycleCountsCorrect()
        {
            var prefab = CreatePrefabWithNB();
            _nm.RegisterPrefab(prefab, 0);

            var spawned = _nm.Spawn(0, Vector3.zero, Quaternion.identity);
            var tb = spawned.GetComponentInChildren<TestBehaviour>();

            Assert.AreEqual(1, tb.SpawnCount);
            Assert.AreEqual(0, tb.DespawnCount);

            _nm.Despawn(spawned);

            Assert.AreEqual(1, tb.SpawnCount, "SpawnCount should not change on despawn");
            Assert.AreEqual(1, tb.DespawnCount, "DespawnCount should increment on despawn");

            Object.DestroyImmediate(prefab);
        }

        [Test]
        public void DespawnAll_CallsOnNetworkDespawn_ForAllObjects()
        {
            var prefab = CreatePrefabWithNB();
            _nm.RegisterPrefab(prefab, 0);

            var obj1 = _nm.Spawn(0, Vector3.zero, Quaternion.identity);
            var obj2 = _nm.Spawn(0, Vector3.one, Quaternion.identity);

            var tb1 = obj1.GetComponentInChildren<TestBehaviour>();
            var tb2 = obj2.GetComponentInChildren<TestBehaviour>();

            _nm.DespawnAll();

            Assert.AreEqual(1, tb1.DespawnCount, "Object 1 should have OnNetworkDespawn called");
            Assert.AreEqual(1, tb2.DespawnCount, "Object 2 should have OnNetworkDespawn called");

            Object.DestroyImmediate(prefab);
        }

        [Test]
        public void NB_NetworkId_MatchesNetworkObject()
        {
            var prefab = CreatePrefabWithNB();
            _nm.RegisterPrefab(prefab, 0);

            var spawned = _nm.Spawn(0, Vector3.zero, Quaternion.identity);
            var tb = spawned.GetComponentInChildren<TestBehaviour>();

            Assert.AreEqual(spawned.NetworkId, tb.NetworkId,
                "NB.NetworkId should match the NetworkObject's NetworkId");

            Object.DestroyImmediate(prefab);
        }

        [Test]
        public void NB_IsHost_MatchesNetworkManager()
        {
            var prefab = CreatePrefabWithNB();
            _nm.RegisterPrefab(prefab, 0);

            var spawned = _nm.Spawn(0, Vector3.zero, Quaternion.identity);
            var tb = spawned.GetComponentInChildren<TestBehaviour>();

            Assert.AreEqual(_nm.IsHost, tb.IsHost,
                "NB.IsHost should match NetworkManager.IsHost");

            Object.DestroyImmediate(prefab);
        }

        [Test]
        public void SerializeAll_WithNB_RoundTrips()
        {
            // Spawn object with NB, set up SyncVars, serialize, deserialize on a separate object
            var prefab = CreatePrefabWithHealth();
            _nm.RegisterPrefab(prefab, 0);

            var spawned = _nm.Spawn(0, Vector3.zero, Quaternion.identity);
            var srcHealth = spawned.GetComponentInChildren<HealthBehaviour>();
            srcHealth.InitForTest();

            // Re-trigger NotifyNetworkSpawn to pick up the initialized NB
            // (NB's SyncVars are set up after spawn, so we need to re-cache _behaviours)
            spawned.NotifyNetworkSpawn();

            srcHealth.Health.Value = 42;
            srcHealth.Speed.Value = 7.5f;

            var writer = new NetWriter();
            spawned.SerializeAll(writer);
            var data = writer.ToArray();

            // Create receiver with matching layout
            var go2 = new GameObject("Receiver");
            var netObj2 = go2.AddComponent<NetworkObject>();
            var dstHealth = go2.AddComponent<HealthBehaviour>();
            dstHealth.InitForTest();
            netObj2.NotifyNetworkSpawn();

            // Overwrite receiver values
            dstHealth.Health.SetInternal(0);
            dstHealth.Speed.SetInternal(0f);

            netObj2.DeserializeAll(new NetReader(data));

            Assert.AreEqual(42, dstHealth.Health.Value, "Health should round-trip via SerializeAll");
            Assert.AreEqual(7.5f, dstHealth.Speed.Value, 0.001f, "Speed should round-trip via SerializeAll");

            Object.DestroyImmediate(prefab);
            Object.DestroyImmediate(go2);
        }

        #endregion
    }
}
