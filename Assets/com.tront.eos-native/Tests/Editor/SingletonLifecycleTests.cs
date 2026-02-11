using System.Reflection;
using NUnit.Framework;
using EOSNative.Net;
using EOSNative.P2P;
using UnityEngine;

namespace EOSNative.Tests
{
    /// <summary>
    /// Tests for singleton lifecycle patterns across EOS-Native managers.
    /// Verifies _shuttingDown protection, direct field access vs auto-create,
    /// cross-singleton non-creation, and SyncVar pool limits.
    ///
    /// NOTE: Auto-create tests that would trigger DontDestroyOnLoad are not possible
    /// in EditMode. Instead we test the singleton LOGIC via inactive GOs + reflection.
    /// </summary>
    [TestFixture]
    public class SingletonLifecycleTests
    {
        [TearDown]
        public void TearDown()
        {
            // Reset all singleton _instance fields and _shuttingDown flags
            ResetSingleton<NetworkManager>("_instance", "_shuttingDown");
            ResetSingleton<EOSP2PManager>("_instance", "_shuttingDown");

            foreach (var name in new[]
            {
                "NetworkManager", "EOSP2PManager", "EOSLobbyManager",
                "NetworkSceneManager", "TickSimulation", "NetworkStats",
                "InterestManager", "P2PDemoManager"
            })
            {
                var obj = GameObject.Find(name);
                if (obj != null) Object.DestroyImmediate(obj);
            }
        }

        private static void ResetSingleton<T>(string instanceField, string shuttingDownField = null)
        {
            var type = typeof(T);
            var inst = type.GetField(instanceField, BindingFlags.Static | BindingFlags.NonPublic | BindingFlags.Public);
            inst?.SetValue(null, null);

            if (shuttingDownField != null)
            {
                var sd = type.GetField(shuttingDownField, BindingFlags.Static | BindingFlags.NonPublic);
                sd?.SetValue(null, false);
            }
        }

        private static void SetShuttingDown<T>(bool value)
        {
            var sd = typeof(T).GetField("_shuttingDown", BindingFlags.Static | BindingFlags.NonPublic);
            sd?.SetValue(null, value);
        }

        /// <summary>Create a singleton on an inactive GO via reflection (no Awake, no DontDestroyOnLoad).</summary>
        private static T CreateInactiveSingleton<T>(string name, string instanceField) where T : Component
        {
            var go = new GameObject(name);
            go.SetActive(false);
            var comp = go.AddComponent<T>();

            var field = typeof(T).GetField(instanceField, BindingFlags.Static | BindingFlags.NonPublic | BindingFlags.Public);
            field?.SetValue(null, comp);

            return comp;
        }

        #region NetworkManager Singleton

        [Test]
        public void NetworkManager_InactiveGO_InstanceFieldSet()
        {
            Assert.IsNull(NetworkManager._instance, "Precondition: _instance should be null");

            var nm = CreateInactiveSingleton<NetworkManager>("NetworkManager", "_instance");
            Assert.IsNotNull(NetworkManager._instance, "_instance should be set via reflection");
            Assert.AreSame(nm, NetworkManager._instance, "Should be the same object");
        }

        [Test]
        public void NetworkManager_Instance_ReturnsSameObject()
        {
            var nm = CreateInactiveSingleton<NetworkManager>("NetworkManager", "_instance");
            var a = NetworkManager.Instance;
            var b = NetworkManager.Instance;
            Assert.AreSame(a, b, "Multiple Instance calls should return the same object");
            Assert.AreSame(nm, a, "Should return the pre-created instance");
        }

        [Test]
        public void NetworkManager_ShuttingDown_ReturnsExistingInstance()
        {
            var nm = CreateInactiveSingleton<NetworkManager>("NetworkManager", "_instance");
            SetShuttingDown<NetworkManager>(true);
            var result = NetworkManager.Instance;
            Assert.AreSame(nm, result, "Instance should return existing _instance when _shuttingDown (no auto-create)");
        }

        [Test]
        public void NetworkManager_ShuttingDown_ReturnsNull_WhenNoInstance()
        {
            SetShuttingDown<NetworkManager>(true);
            // Don't create any instance — _instance is null, _shuttingDown is true
            var result = NetworkManager.Instance;
            Assert.IsNull(result, "Instance should return null when _shuttingDown and no existing instance");
        }

        [Test]
        public void NetworkManager_ShuttingDown_DoesNotAutoCreate()
        {
            SetShuttingDown<NetworkManager>(true);
            _ = NetworkManager.Instance;
            Assert.IsNull(NetworkManager._instance, "_instance should remain null when _shuttingDown");
        }

        [Test]
        public void NetworkManager_AntiDuplicate_SecondInstanceNotStored()
        {
            var first = CreateInactiveSingleton<NetworkManager>("NetworkManager", "_instance");

            // Create second inactive instance — set _instance manually to first
            var go2 = new GameObject("NetworkManager2");
            go2.SetActive(false);
            var second = go2.AddComponent<NetworkManager>();

            // _instance should still be the first
            Assert.AreSame(first, NetworkManager._instance,
                "_instance should remain the first created instance");
        }

        [Test]
        public void NetworkManager_DirectFieldAccess_NoAutoCreate()
        {
            var inst = NetworkManager._instance;
            Assert.IsNull(inst, "Direct _instance access should not auto-create");
        }

        [Test]
        public void NetworkManager_Destroy_AllowsNewInstance()
        {
            var first = CreateInactiveSingleton<NetworkManager>("NetworkManager", "_instance");
            Assert.IsNotNull(NetworkManager._instance);

            Object.DestroyImmediate(first.gameObject);
            NetworkManager._instance = null;

            var second = CreateInactiveSingleton<NetworkManager>("NetworkManager2", "_instance");
            Assert.IsNotNull(NetworkManager._instance, "Should be able to set new instance after clearing");
            Assert.AreNotSame(first, second, "New instance should be a different object");
        }

        [Test]
        public void NetworkManager_ShuttingDown_CanBeResetViaReflection()
        {
            SetShuttingDown<NetworkManager>(true);

            var sd = typeof(NetworkManager).GetField("_shuttingDown", BindingFlags.Static | BindingFlags.NonPublic);
            Assert.IsTrue((bool)sd.GetValue(null), "Should be true after setting");

            sd.SetValue(null, false);
            Assert.IsFalse((bool)sd.GetValue(null), "Should be false after reset");
        }

        #endregion

        #region EOSP2PManager Singleton

        [Test]
        public void P2PManager_InactiveGO_InstanceFieldSet()
        {
            Assert.IsNull(EOSP2PManager._instance, "Precondition: _instance should be null");

            var p2p = CreateInactiveSingleton<EOSP2PManager>("EOSP2PManager", "_instance");
            Assert.IsNotNull(EOSP2PManager._instance, "_instance should be set via reflection");
        }

        [Test]
        public void P2PManager_Instance_ReturnsSameObject()
        {
            var p2p = CreateInactiveSingleton<EOSP2PManager>("EOSP2PManager", "_instance");
            var a = EOSP2PManager.Instance;
            var b = EOSP2PManager.Instance;
            Assert.AreSame(a, b, "Multiple Instance calls should return the same object");
        }

        [Test]
        public void P2PManager_ShuttingDown_ReturnsNull_WhenNoInstance()
        {
            SetShuttingDown<EOSP2PManager>(true);
            var p2p = EOSP2PManager.Instance;
            Assert.IsNull(p2p, "Instance should return null when _shuttingDown and no instance");
        }

        [Test]
        public void P2PManager_ShuttingDown_DoesNotAutoCreate()
        {
            SetShuttingDown<EOSP2PManager>(true);
            _ = EOSP2PManager.Instance;
            Assert.IsNull(EOSP2PManager._instance, "_instance should remain null when _shuttingDown");
        }

        [Test]
        public void P2PManager_DirectFieldAccess_NoAutoCreate()
        {
            var inst = EOSP2PManager._instance;
            Assert.IsNull(inst, "Direct _instance access should not auto-create");
        }

        [Test]
        public void P2PManager_Destroy_AllowsNewInstance()
        {
            var first = CreateInactiveSingleton<EOSP2PManager>("EOSP2PManager", "_instance");
            Assert.IsNotNull(EOSP2PManager._instance);

            Object.DestroyImmediate(first.gameObject);
            EOSP2PManager._instance = null;

            var second = CreateInactiveSingleton<EOSP2PManager>("EOSP2PManager2", "_instance");
            Assert.IsNotNull(EOSP2PManager._instance, "Should be able to set new instance after clearing");
        }

        #endregion

        #region Cross-Singleton Interactions (Critical: no auto-create leaks)

        [Test]
        public void NetworkManager_IsOnline_DoesNotAutoCreateP2P()
        {
            var nm = CreateInactiveSingleton<NetworkManager>("NetworkManager", "_instance");

            var online = nm.IsOnline;
            Assert.IsFalse(online, "IsOnline should be false with no P2P");
            Assert.IsNull(EOSP2PManager._instance, "Accessing IsOnline should not auto-create EOSP2PManager");
        }

        [Test]
        public void NetworkBehaviour_IsOnline_DoesNotAutoCreateManagers()
        {
            var go = new GameObject("TestNB");
            var netObj = go.AddComponent<NetworkObject>();
            var nb = go.AddComponent<TrackingBehaviour>();

            var online = nb.IsOnline;
            Assert.IsFalse(online, "IsOnline should be false with no managers");
            Assert.IsNull(NetworkManager._instance, "NB.IsOnline should not auto-create NetworkManager");
            Assert.IsNull(EOSP2PManager._instance, "NB.IsOnline should not auto-create EOSP2PManager");

            Object.DestroyImmediate(go);
        }

        [Test]
        public void NetworkBehaviour_IsOffline_DoesNotAutoCreateManagers()
        {
            var go = new GameObject("TestNB");
            go.AddComponent<NetworkObject>();
            var nb = go.AddComponent<TrackingBehaviour>();

            var offline = nb.IsOffline;
            Assert.IsFalse(offline, "IsOffline should be false with no NetworkManager");
            Assert.IsNull(NetworkManager._instance, "NB.IsOffline should not auto-create NetworkManager");

            Object.DestroyImmediate(go);
        }

        [Test]
        public void NetworkObject_IsOwner_DoesNotAutoCreateManagers()
        {
            var go = new GameObject("TestObj");
            var netObj = go.AddComponent<NetworkObject>();

            var isOwner = netObj.IsOwner;
            Assert.IsFalse(isOwner, "IsOwner should be false with no NetworkManager");
            Assert.IsNull(NetworkManager._instance, "NO.IsOwner should not auto-create NetworkManager");

            Object.DestroyImmediate(go);
        }

        [Test]
        public void NetworkObject_IsHost_DoesNotAutoCreateManagers()
        {
            var go = new GameObject("TestObj");
            var netObj = go.AddComponent<NetworkObject>();

            var isHost = netObj.IsHost;
            Assert.IsFalse(isHost, "IsHost should be false with no NetworkManager");
            Assert.IsNull(NetworkManager._instance, "NO.IsHost should not auto-create NetworkManager");

            Object.DestroyImmediate(go);
        }

        [Test]
        public void NetworkBehaviour_IsSpawned_DoesNotAutoCreateManagers()
        {
            var go = new GameObject("TestNB");
            go.AddComponent<NetworkObject>();
            var nb = go.AddComponent<TrackingBehaviour>();

            var isSpawned = nb.IsSpawned;
            Assert.IsFalse(isSpawned, "IsSpawned should be false on unspawned object");
            Assert.IsNull(NetworkManager._instance, "NB.IsSpawned should not auto-create NetworkManager");

            Object.DestroyImmediate(go);
        }

        [Test]
        public void NetworkBehaviour_HasAuthority_DoesNotAutoCreateManagers()
        {
            var go = new GameObject("TestNB");
            go.AddComponent<NetworkObject>();
            var nb = go.AddComponent<TrackingBehaviour>();

            var hasAuth = nb.HasAuthority;
            Assert.IsFalse(hasAuth, "HasAuthority should be false on unspawned object");
            Assert.IsNull(NetworkManager._instance, "NB.HasAuthority should not auto-create NetworkManager");

            Object.DestroyImmediate(go);
        }

        #endregion

        #region SyncVar Pool Limits

        [Test]
        public void NetworkObject_SyncVarPool_MaxIs32()
        {
            var go = new GameObject("TestObj");
            var netObj = go.AddComponent<NetworkObject>();

            for (int i = 0; i < 32; i++)
                netObj.Sync(i);

            Assert.AreEqual(32, netObj.SyncVarCount, "Should have 32 SyncVars");

            Assert.Throws<System.InvalidOperationException>(() => netObj.Sync(33),
                "33rd SyncVar should throw InvalidOperationException");

            Object.DestroyImmediate(go);
        }

        [Test]
        public void NetworkBehaviour_SyncVarPool_MaxIs32()
        {
            var go = new GameObject("TestObj");
            go.SetActive(false);
            go.AddComponent<NetworkObject>();
            var nb = go.AddComponent<TrackingBehaviour>();

            var syncMethod = typeof(NetworkBehaviour).GetMethod("Sync",
                BindingFlags.NonPublic | BindingFlags.Instance);
            var generic = syncMethod.MakeGenericMethod(typeof(int));

            for (int i = 0; i < 32; i++)
                generic.Invoke(nb, new object[] { i, SyncVarWriteAccess.Owner });

            Assert.AreEqual(32, nb.SyncVarCount, "Should have 32 SyncVars");

            Assert.Throws<TargetInvocationException>(() =>
                generic.Invoke(nb, new object[] { 33, SyncVarWriteAccess.Owner }),
                "33rd SyncVar should throw");

            Object.DestroyImmediate(go);
        }

        [Test]
        public void NetworkObject_SyncList_CountsTowardPool()
        {
            var go = new GameObject("TestObj");
            var netObj = go.AddComponent<NetworkObject>();

            netObj.Sync(0);
            netObj.SyncList<int>();
            netObj.SyncDictionary<string, int>();
            netObj.SyncHashSet<int>();

            Assert.AreEqual(4, netObj.SyncVarCount, "SyncVar + SyncList + SyncDict + SyncHashSet = 4");

            Object.DestroyImmediate(go);
        }

        #endregion
    }
}
