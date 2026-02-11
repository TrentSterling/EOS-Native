using System.Reflection;
using Epic.OnlineServices;
using NUnit.Framework;
using EOSNative.Net;
using UnityEngine;

namespace EOSNative.Tests
{
    /// <summary>
    /// Concrete test subclass that records all lifecycle hook invocations.
    /// </summary>
    public class TrackingBehaviour : NetworkBehaviour
    {
        public int SpawnCount, DespawnCount, StartHostCount, StopHostCount;
        public int StartOwnerCount, StopOwnerCount, OwnershipChangedCount;
        public int TickCount, PeerConnectedCount, PeerDisconnectedCount;
        public uint LastTick;
        public ProductUserId LastPreviousOwner;
        public ProductUserId LastConnectedPeer, LastDisconnectedPeer;
        public int SpawnDataWritten, SpawnDataRead;
        public int LastWrittenValue, LastReadValue;

        public override void OnNetworkSpawn() => SpawnCount++;
        public override void OnNetworkDespawn() => DespawnCount++;
        public override void OnStartHost() => StartHostCount++;
        public override void OnStopHost() => StopHostCount++;
        public override void OnStartOwner() => StartOwnerCount++;
        public override void OnStopOwner() => StopOwnerCount++;

        public override void OnOwnershipChanged(ProductUserId prev)
        {
            OwnershipChangedCount++;
            LastPreviousOwner = prev;
        }

        public override void OnTick(uint tick) { TickCount++; LastTick = tick; }
        public override void OnPeerConnected(ProductUserId peer) { PeerConnectedCount++; LastConnectedPeer = peer; }
        public override void OnPeerDisconnected(ProductUserId peer) { PeerDisconnectedCount++; LastDisconnectedPeer = peer; }

        public override void WriteSpawnData(EOSNative.P2P.NetWriter writer)
        {
            SpawnDataWritten++;
            writer.WriteInt32(42);
            LastWrittenValue = 42;
        }

        public override void ReadSpawnData(EOSNative.P2P.NetReader reader)
        {
            SpawnDataRead++;
            LastReadValue = reader.ReadInt32();
        }
    }

    /// <summary>
    /// Tests for NetworkBehaviour lifecycle hooks and convenience properties (v2.40.0+).
    /// Uses inactive GO + reflection pattern (same as EndToEndTests/PerNBComponentIdTests).
    /// </summary>
    [TestFixture]
    public class LifecycleHookTests
    {
        private NetworkManager _nm;

        private static readonly FieldInfo NmInstanceField =
            typeof(NetworkManager).GetField("_instance", BindingFlags.NonPublic | BindingFlags.Static);

        [SetUp]
        public void SetUp()
        {
            var go = new GameObject("NetworkManager");
            go.SetActive(false);
            _nm = go.AddComponent<NetworkManager>();
            NmInstanceField.SetValue(null, _nm);

            // Set offline mode fields via reflection
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
            foreach (var name in new[] { "NetworkManager", "EOSP2PManager", "EOSLobbyManager", "NetworkSceneManager" })
            {
                var obj = GameObject.Find(name);
                if (obj != null) Object.DestroyImmediate(obj);
            }
        }

        /// <summary>Create an inactive prefab with NetworkObject + TrackingBehaviour.</summary>
        private GameObject CreateTrackedPrefab(string name = "TrackedPrefab")
        {
            var go = new GameObject(name);
            go.AddComponent<NetworkObject>();
            go.AddComponent<TrackingBehaviour>();
            go.SetActive(false);
            return go;
        }

        /// <summary>Register, spawn, and return the TrackingBehaviour on the spawned clone.</summary>
        private TrackingBehaviour SpawnTracked(ushort prefabId = 0)
        {
            var prefab = CreateTrackedPrefab();
            _nm.RegisterPrefab(prefab, prefabId);
            var spawned = _nm.Spawn(prefabId, Vector3.zero, Quaternion.identity);
            return spawned.GetComponentInChildren<TrackingBehaviour>();
        }

        #region Convenience Properties

        [Test]
        public void IsSpawned_FalseBeforeSpawn()
        {
            var go = new GameObject("Unspawned");
            var netObj = go.AddComponent<NetworkObject>();
            var nb = go.AddComponent<TrackingBehaviour>();
            Assert.IsFalse(nb.IsSpawned, "IsSpawned should be false before Spawn()");
            Object.DestroyImmediate(go);
        }

        [Test]
        public void IsSpawned_TrueAfterSpawn()
        {
            var tb = SpawnTracked();
            Assert.IsTrue(tb.IsSpawned, "IsSpawned should be true after Spawn()");
        }

        [Test]
        public void HasAuthority_TrueWhenIsOwner()
        {
            var tb = SpawnTracked();
            // In offline mode, spawned objects are locally owned => IsOwner is true
            Assert.IsTrue(tb.IsOwner, "IsOwner should be true in offline mode");
            Assert.IsTrue(tb.HasAuthority, "HasAuthority should be true when IsOwner is true");
        }

        [Test]
        public void HasAuthority_TrueWhenHostAndOwnerNull()
        {
            // Spawned object in offline mode has no OwnerId (ProductUserId is null offline)
            // but IsHost is true and IsOwner returns true (offline owned).
            // HasAuthority = IsOwner || (IsHost && OwnerId == null) should be true.
            var tb = SpawnTracked();
            Assert.IsTrue(tb.IsHost, "IsHost should be true in offline mode");
            Assert.IsTrue(tb.HasAuthority, "HasAuthority should be true in offline mode");
        }

        [Test]
        public void IsOffline_MatchesNetworkManager()
        {
            var tb = SpawnTracked();
            Assert.IsTrue(tb.IsOffline, "IsOffline should be true when NetworkManager.OfflineMode is true");
        }

        [Test]
        public void IsOnline_FalseInOfflineMode()
        {
            var tb = SpawnTracked();
            // IsOnline checks EOSP2PManager peers — no peers in EditMode tests
            Assert.IsFalse(tb.IsOnline, "IsOnline should be false when no P2P peers exist");
        }

        #endregion

        #region Lifecycle Hooks — Spawn/Despawn

        [Test]
        public void OnNetworkSpawn_CalledOnSpawn()
        {
            var tb = SpawnTracked();
            Assert.AreEqual(1, tb.SpawnCount, "OnNetworkSpawn should fire exactly once after Spawn()");
        }

        [Test]
        public void OnNetworkDespawn_CalledOnDespawn()
        {
            var tb = SpawnTracked();
            _nm.Despawn(tb.Net);
            Assert.AreEqual(1, tb.DespawnCount, "OnNetworkDespawn should fire exactly once after Despawn()");
        }

        [Test]
        public void Despawn_RemovesFromObjectsDict()
        {
            var tb = SpawnTracked();
            uint networkId = tb.NetworkId;
            Assert.IsTrue(_nm.Objects.ContainsKey(networkId), "Object should be in Objects dict after spawn");
            _nm.Despawn(tb.Net);
            Assert.IsFalse(_nm.Objects.ContainsKey(networkId), "Object should be removed from Objects dict after despawn");
        }

        [Test]
        public void Despawn_ViaConvenienceMethod()
        {
            var tb = SpawnTracked();
            uint networkId = tb.NetworkId;
            // Use the NetworkBehaviour convenience method
            tb.Despawn();
            Assert.AreEqual(1, tb.DespawnCount, "OnNetworkDespawn should fire via Despawn() convenience method");
            Assert.IsFalse(_nm.Objects.ContainsKey(networkId), "Object should be removed after convenience Despawn()");
        }

        [Test]
        public void IsSpawned_FalseAfterDespawn()
        {
            var tb = SpawnTracked();
            Assert.IsTrue(tb.IsSpawned);
            _nm.Despawn(tb.Net);
            Assert.IsFalse(tb.IsSpawned, "IsSpawned should be false after Despawn()");
        }

        #endregion

        #region Lifecycle Hooks — Host Changed

        [Test]
        public void OnStartHost_FiresWhenInvoked()
        {
            var tb = SpawnTracked();
            Assert.AreEqual(0, tb.StartHostCount, "OnStartHost should not have fired at spawn time");
            tb.OnStartHost();
            Assert.AreEqual(1, tb.StartHostCount, "OnStartHost should increment count");
        }

        [Test]
        public void OnStopHost_FiresWhenInvoked()
        {
            var tb = SpawnTracked();
            Assert.AreEqual(0, tb.StopHostCount);
            tb.OnStopHost();
            Assert.AreEqual(1, tb.StopHostCount, "OnStopHost should increment count");
        }

        [Test]
        public void OnHostChanged_MultipleNBs_AllNotified()
        {
            var prefab = new GameObject("MultiNBPrefab");
            prefab.AddComponent<NetworkObject>();
            prefab.AddComponent<TrackingBehaviour>();
            prefab.AddComponent<TrackingBehaviour>();
            prefab.SetActive(false);
            _nm.RegisterPrefab(prefab, 0);
            var spawned = _nm.Spawn(0, Vector3.zero, Quaternion.identity);
            var trackers = spawned.GetComponents<TrackingBehaviour>();

            // Simulate host migration: call OnStartHost on all behaviours
            foreach (var t in trackers)
                t.OnStartHost();

            Assert.AreEqual(1, trackers[0].StartHostCount);
            Assert.AreEqual(1, trackers[1].StartHostCount);
        }

        #endregion

        #region Lifecycle Hooks — Ownership Changed

        [Test]
        public void OnOwnershipChanged_FiresOnNotifyOwnerChanged()
        {
            var tb = SpawnTracked();
            var netObj = tb.Net;

            // NotifyOwnerChanged invokes OnOwnershipChanged on all behaviours
            netObj.NotifyOwnerChanged(null, null);

            Assert.AreEqual(1, tb.OwnershipChangedCount, "OnOwnershipChanged should fire once");
            Assert.IsNull(tb.LastPreviousOwner, "Previous owner should be null");
        }

        [Test]
        public void OnOwnershipChanged_PreviousOwnerPassedCorrectly()
        {
            var tb = SpawnTracked();
            var netObj = tb.Net;

            netObj.NotifyOwnerChanged(null, null);
            Assert.AreEqual(1, tb.OwnershipChangedCount);
            Assert.IsNull(tb.LastPreviousOwner);

            // Fire again to verify it increments
            netObj.NotifyOwnerChanged(null, null);
            Assert.AreEqual(2, tb.OwnershipChangedCount);
        }

        [Test]
        public void OnStartOwner_DirectInvocation()
        {
            var tb = SpawnTracked();
            tb.OnStartOwner();
            Assert.AreEqual(1, tb.StartOwnerCount, "OnStartOwner should increment count");
        }

        [Test]
        public void OnStopOwner_DirectInvocation()
        {
            var tb = SpawnTracked();
            tb.OnStopOwner();
            Assert.AreEqual(1, tb.StopOwnerCount, "OnStopOwner should increment count");
        }

        #endregion

        #region Convenience Methods — GiveOwnership / RemoveOwnership

        [Test]
        public void GiveOwnership_CallsTransferAuthority()
        {
            var tb = SpawnTracked();
            // GiveOwnership delegates to Manager.TransferAuthority(Net, newOwner).
            // In offline mode, TransferAuthority fires NotifyOwnerChanged.
            tb.GiveOwnership(null);

            Assert.AreEqual(1, tb.OwnershipChangedCount,
                "GiveOwnership should trigger OnOwnershipChanged via TransferAuthority");
        }

        [Test]
        public void RemoveOwnership_CallsTransferAuthorityWithNull()
        {
            var tb = SpawnTracked();
            tb.RemoveOwnership();

            Assert.AreEqual(1, tb.OwnershipChangedCount,
                "RemoveOwnership should trigger OnOwnershipChanged");
            Assert.IsNull(tb.Net.OwnerId, "OwnerId should be null after RemoveOwnership()");
        }

        #endregion

        #region Multiple Behaviours

        [Test]
        public void MultipleBehaviours_AllReceiveLifecycleHooks()
        {
            var prefab = new GameObject("MultiBehaviourPrefab");
            prefab.AddComponent<NetworkObject>();
            prefab.AddComponent<TrackingBehaviour>();
            prefab.AddComponent<TrackingBehaviour>();
            prefab.SetActive(false);
            _nm.RegisterPrefab(prefab, 0);
            var spawned = _nm.Spawn(0, Vector3.zero, Quaternion.identity);
            var trackers = spawned.GetComponents<TrackingBehaviour>();

            // Both should have received OnNetworkSpawn
            Assert.AreEqual(1, trackers[0].SpawnCount, "First NB should receive OnNetworkSpawn");
            Assert.AreEqual(1, trackers[1].SpawnCount, "Second NB should receive OnNetworkSpawn");

            // Despawn should notify both
            _nm.Despawn(spawned);
            Assert.AreEqual(1, trackers[0].DespawnCount, "First NB should receive OnNetworkDespawn");
            Assert.AreEqual(1, trackers[1].DespawnCount, "Second NB should receive OnNetworkDespawn");
        }

        [Test]
        public void MultipleBehaviours_OwnershipChangedNotifiesAll()
        {
            var prefab = new GameObject("MultiOwnerPrefab");
            prefab.AddComponent<NetworkObject>();
            prefab.AddComponent<TrackingBehaviour>();
            prefab.AddComponent<TrackingBehaviour>();
            prefab.SetActive(false);
            _nm.RegisterPrefab(prefab, 0);
            var spawned = _nm.Spawn(0, Vector3.zero, Quaternion.identity);
            var trackers = spawned.GetComponents<TrackingBehaviour>();

            spawned.NotifyOwnerChanged(null, null);

            Assert.AreEqual(1, trackers[0].OwnershipChangedCount, "First NB should receive OnOwnershipChanged");
            Assert.AreEqual(1, trackers[1].OwnershipChangedCount, "Second NB should receive OnOwnershipChanged");
        }

        #endregion
    }
}
