using System.Reflection;
using NUnit.Framework;
using EOSNative.Net;
using EOSNative.P2P;
using UnityEngine;

namespace EOSNative.Tests
{
    /// <summary>
    /// Tests for SimulationBehaviour abstract base class.
    /// Uses a concrete TestSimBehaviour to verify callbacks and accessors.
    /// Pre-creates singletons on inactive GOs to avoid DontDestroyOnLoad in EditMode.
    /// </summary>
    public class SimulationBehaviourTests
    {
        private NetworkManager _nm;
        private EOSP2PManager _p2p;
        private TickSimulation _tick;
        private GameObject _simGo;

        private static readonly FieldInfo NmInstanceField =
            typeof(NetworkManager).GetField("_instance", BindingFlags.NonPublic | BindingFlags.Static);

        private static readonly FieldInfo P2PInstanceField =
            typeof(EOSP2PManager).GetField("_instance", BindingFlags.NonPublic | BindingFlags.Static);

        private static readonly FieldInfo TickInstanceField =
            typeof(TickSimulation).GetField("_instance", BindingFlags.NonPublic | BindingFlags.Static);

        /// <summary>Concrete test subclass to track callback invocations.</summary>
        private class TestSimBehaviour : SimulationBehaviour
        {
            public bool BecameHostCalled;
            public bool LostHostCalled;
            public int TickCount;

            protected override void OnBecameHost() => BecameHostCalled = true;
            protected override void OnLostHost() => LostHostCalled = true;
            protected override void OnTick(uint tick, float deltaTime) => TickCount++;

            // Expose protected accessors for testing
            public bool TestIsHost => IsHost;
            public bool TestIsOnline => IsOnline;
            public uint TestCurrentTick => CurrentTick;
            public NetworkManager TestManager => Manager;
        }

        [SetUp]
        public void SetUp()
        {
            var go = new GameObject("NetworkManager");
            go.SetActive(false);
            _nm = go.AddComponent<NetworkManager>();
            NmInstanceField.SetValue(null, _nm);

            var p2pGo = new GameObject("EOSP2PManager");
            p2pGo.SetActive(false);
            _p2p = p2pGo.AddComponent<EOSP2PManager>();
            P2PInstanceField.SetValue(null, _p2p);

            var tickGo = new GameObject("TickSimulation");
            tickGo.SetActive(false);
            _tick = tickGo.AddComponent<TickSimulation>();
            TickInstanceField.SetValue(null, _tick);

            _simGo = new GameObject("TestSim");
            _simGo.SetActive(false);
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
            if (_simGo != null) Object.DestroyImmediate(_simGo);

            foreach (var name in new[] { "NetworkManager", "EOSP2PManager", "TickSimulation", "EOSLobbyManager", "NetworkSceneManager" })
            {
                var obj = GameObject.Find(name);
                if (obj != null) Object.DestroyImmediate(obj);
            }
        }

        [Test]
        public void ConvenienceAccessors_MatchNetworkManager()
        {
            var sim = _simGo.AddComponent<TestSimBehaviour>();
            Assert.AreSame(_nm, sim.TestManager);
            Assert.IsFalse(sim.TestIsHost);
            Assert.IsFalse(sim.TestIsOnline);
            Assert.AreEqual(0u, sim.TestCurrentTick);
        }

        [Test]
        public void OnBecameHost_FiresOnHostChange()
        {
            var sim = _simGo.AddComponent<TestSimBehaviour>();

            // Test via HandleHostChanged directly (OnEnable won't fire since GO is inactive)
            var handle = typeof(SimulationBehaviour)
                .GetMethod("HandleHostChanged", BindingFlags.NonPublic | BindingFlags.Instance);
            handle.Invoke(sim, new object[] { true });
            Assert.IsTrue(sim.BecameHostCalled);
        }

        [Test]
        public void OnLostHost_FiresOnHostLost()
        {
            var sim = _simGo.AddComponent<TestSimBehaviour>();
            // Set _wasHost to true first
            var wasHostField = typeof(SimulationBehaviour)
                .GetField("_wasHost", BindingFlags.NonPublic | BindingFlags.Instance);
            wasHostField.SetValue(sim, true);

            var handle = typeof(SimulationBehaviour)
                .GetMethod("HandleHostChanged", BindingFlags.NonPublic | BindingFlags.Instance);
            handle.Invoke(sim, new object[] { false });
            Assert.IsTrue(sim.LostHostCalled);
        }

        [Test]
        public void HandleHostChanged_NoDoubleCall()
        {
            var sim = _simGo.AddComponent<TestSimBehaviour>();
            var handle = typeof(SimulationBehaviour)
                .GetMethod("HandleHostChanged", BindingFlags.NonPublic | BindingFlags.Instance);

            // Call twice with true — should only fire OnBecameHost once
            handle.Invoke(sim, new object[] { true });
            Assert.IsTrue(sim.BecameHostCalled);

            sim.BecameHostCalled = false;
            handle.Invoke(sim, new object[] { true }); // same state, no change
            Assert.IsFalse(sim.BecameHostCalled);
        }

        [Test]
        public void Manager_ReturnsNull_WhenNoInstance()
        {
            var sim = _simGo.AddComponent<TestSimBehaviour>();

            // Clear singletons and use _shuttingDown to prevent auto-create
            NmInstanceField.SetValue(null, null);
            P2PInstanceField.SetValue(null, null);
            var shuttingDown = typeof(NetworkManager).GetField("_shuttingDown", BindingFlags.NonPublic | BindingFlags.Static);
            shuttingDown?.SetValue(null, true);

            try
            {
                Assert.IsNull(sim.TestManager);
            }
            finally
            {
                shuttingDown?.SetValue(null, false);
            }
        }

        [Test]
        public void IsHost_FalseByDefault()
        {
            var sim = _simGo.AddComponent<TestSimBehaviour>();
            Assert.IsFalse(sim.TestIsHost);
        }
    }
}
