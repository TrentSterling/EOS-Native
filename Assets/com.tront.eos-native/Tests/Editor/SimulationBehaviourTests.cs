using System.Reflection;
using NUnit.Framework;
using EOSNative.Net;
using UnityEngine;

namespace EOSNative.Tests
{
    /// <summary>
    /// Tests for SimulationBehaviour abstract base class.
    /// Uses a concrete TestSimBehaviour to verify callbacks and accessors.
    /// </summary>
    public class SimulationBehaviourTests
    {
        private NetworkManager _nm;
        private GameObject _simGo;

        private static readonly FieldInfo InstanceField =
            typeof(NetworkManager).GetField("_instance", BindingFlags.NonPublic | BindingFlags.Static);

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
            InstanceField.SetValue(null, _nm);

            _simGo = new GameObject("TestSim");
            _simGo.SetActive(false);
        }

        [TearDown]
        public void TearDown()
        {
            InstanceField.SetValue(null, null);
            if (_nm != null)
                Object.DestroyImmediate(_nm.gameObject);
            if (_simGo != null)
                Object.DestroyImmediate(_simGo);
            foreach (var name in new[] { "NetworkManager", "EOSP2PManager", "EOSLobbyManager", "NetworkSceneManager" })
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
            InstanceField.SetValue(null, null);
            Assert.IsNull(sim.TestManager);
        }

        [Test]
        public void IsHost_FalseByDefault()
        {
            var sim = _simGo.AddComponent<TestSimBehaviour>();
            Assert.IsFalse(sim.TestIsHost);
        }
    }
}
