using System.Reflection;
using NUnit.Framework;
using EOSNative.Net;
using EOSNative.P2P;
using UnityEngine;

namespace EOSNative.Tests
{
    /// <summary>
    /// Tests for InstanceFinder static accessor class.
    /// Uses inactive GO pattern to avoid DontDestroyOnLoad in EditMode.
    /// Pre-creates all singletons that InstanceFinder accessors might auto-create.
    /// </summary>
    public class InstanceFinderTests
    {
        private NetworkManager _nm;
        private EOSP2PManager _p2p;
        private TickSimulation _tick;

        private static readonly FieldInfo NmInstanceField =
            typeof(NetworkManager).GetField("_instance", BindingFlags.NonPublic | BindingFlags.Static);

        private static readonly FieldInfo P2PInstanceField =
            typeof(EOSP2PManager).GetField("_instance", BindingFlags.NonPublic | BindingFlags.Static);

        private static readonly FieldInfo TickInstanceField =
            typeof(TickSimulation).GetField("_instance", BindingFlags.NonPublic | BindingFlags.Static);

        [SetUp]
        public void SetUp()
        {
            // Pre-create all singletons on inactive GOs to prevent auto-create → DontDestroyOnLoad
            var nmGo = new GameObject("NetworkManager");
            nmGo.SetActive(false);
            _nm = nmGo.AddComponent<NetworkManager>();
            NmInstanceField.SetValue(null, _nm);

            var p2pGo = new GameObject("EOSP2PManager");
            p2pGo.SetActive(false);
            _p2p = p2pGo.AddComponent<EOSP2PManager>();
            P2PInstanceField.SetValue(null, _p2p);

            var tickGo = new GameObject("TickSimulation");
            tickGo.SetActive(false);
            _tick = tickGo.AddComponent<TickSimulation>();
            TickInstanceField.SetValue(null, _tick);
        }

        [TearDown]
        public void TearDown()
        {
            NmInstanceField.SetValue(null, null);
            P2PInstanceField.SetValue(null, null);
            TickInstanceField.SetValue(null, null);

            if (_nm != null) Object.DestroyImmediate(_nm.gameObject);
            if (_p2p != null) Object.DestroyImmediate(_p2p.gameObject);
            if (_tick != null) Object.DestroyImmediate(_tick.gameObject);

            foreach (var name in new[] { "NetworkManager", "EOSP2PManager", "TickSimulation",
                "EOSLobbyManager", "NetworkSceneManager" })
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

        [Test]
        public void NetworkManager_ReturnsSameAsInstance()
        {
            Assert.AreSame(_nm, InstanceFinder.NetworkManager);
        }

        [Test]
        public void IsHost_MatchesNetworkManager()
        {
            Assert.IsFalse(InstanceFinder.IsHost);
            EnableOfflineMode();
            Assert.IsTrue(InstanceFinder.IsHost);
        }

        [Test]
        public void IsOffline_MatchesNetworkManager()
        {
            Assert.IsFalse(InstanceFinder.IsOffline);
            EnableOfflineMode();
            Assert.IsTrue(InstanceFinder.IsOffline);
        }

        [Test]
        public void CurrentTick_DefaultsZero()
        {
            Assert.AreEqual(0u, InstanceFinder.CurrentTick);
        }

        [Test]
        public void FixedTickTime_DefaultsZero()
        {
            Assert.AreEqual(0f, InstanceFinder.FixedTickTime);
        }

        [Test]
        public void NullSafe_WhenNoManager()
        {
            // Clear all singletons
            NmInstanceField.SetValue(null, null);
            P2PInstanceField.SetValue(null, null);
            TickInstanceField.SetValue(null, null);

            // Use _shuttingDown to prevent auto-create during null checks
            var shuttingDown = typeof(NetworkManager).GetField("_shuttingDown", BindingFlags.NonPublic | BindingFlags.Static);
            var p2pShuttingDown = typeof(EOSP2PManager).GetField("_shuttingDown", BindingFlags.NonPublic | BindingFlags.Static);
            var tickShuttingDown = typeof(TickSimulation).GetField("_shuttingDown", BindingFlags.NonPublic | BindingFlags.Static);

            shuttingDown?.SetValue(null, true);
            p2pShuttingDown?.SetValue(null, true);
            tickShuttingDown?.SetValue(null, true);

            try
            {
                Assert.IsNull(InstanceFinder.NetworkManager);
                Assert.IsFalse(InstanceFinder.IsHost);
                Assert.IsFalse(InstanceFinder.IsOffline);
                Assert.AreEqual(0u, InstanceFinder.CurrentTick);
                Assert.AreEqual(0f, InstanceFinder.FixedTickTime);
            }
            finally
            {
                shuttingDown?.SetValue(null, false);
                p2pShuttingDown?.SetValue(null, false);
                tickShuttingDown?.SetValue(null, false);
            }
        }

        [Test]
        public void IsOnline_FalseWhenNotConnected()
        {
            Assert.IsFalse(InstanceFinder.IsOnline);
        }

        [Test]
        public void P2PManager_DoesNotThrow()
        {
            // P2PManager is pre-created in SetUp, so this shouldn't throw
            var p2p = InstanceFinder.P2PManager;
            Assert.AreSame(_p2p, p2p);
        }
    }
}
