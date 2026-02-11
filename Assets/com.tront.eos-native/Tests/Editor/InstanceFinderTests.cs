using System.Reflection;
using NUnit.Framework;
using EOSNative.Net;
using UnityEngine;

namespace EOSNative.Tests
{
    /// <summary>
    /// Tests for InstanceFinder static accessor class.
    /// Uses inactive GO pattern to avoid DontDestroyOnLoad in EditMode.
    /// </summary>
    public class InstanceFinderTests
    {
        private NetworkManager _nm;

        private static readonly FieldInfo InstanceField =
            typeof(NetworkManager).GetField("_instance", BindingFlags.NonPublic | BindingFlags.Static);

        [SetUp]
        public void SetUp()
        {
            var go = new GameObject("NetworkManager");
            go.SetActive(false);
            _nm = go.AddComponent<NetworkManager>();
            InstanceField.SetValue(null, _nm);
        }

        [TearDown]
        public void TearDown()
        {
            InstanceField.SetValue(null, null);
            if (_nm != null)
                Object.DestroyImmediate(_nm.gameObject);
            foreach (var name in new[] { "NetworkManager", "EOSP2PManager", "EOSLobbyManager", "NetworkSceneManager" })
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
            InstanceField.SetValue(null, null);

            Assert.IsNull(InstanceFinder.NetworkManager);
            Assert.IsFalse(InstanceFinder.IsHost);
            Assert.IsFalse(InstanceFinder.IsOnline);
            Assert.IsFalse(InstanceFinder.IsOffline);
            Assert.AreEqual(0u, InstanceFinder.CurrentTick);
            Assert.AreEqual(0f, InstanceFinder.FixedTickTime);
        }

        [Test]
        public void IsOnline_FalseWhenNotConnected()
        {
            Assert.IsFalse(InstanceFinder.IsOnline);
        }

        [Test]
        public void P2PManager_DoesNotThrow()
        {
            // Just verify the accessor doesn't throw — may return null in test
            var _ = InstanceFinder.P2PManager;
        }
    }
}
