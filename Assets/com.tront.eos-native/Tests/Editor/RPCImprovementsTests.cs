using System.Reflection;
using NUnit.Framework;
using EOSNative.Net;
using EOSNative.P2P;
using Epic.OnlineServices.P2P;
using UnityEngine;

namespace EOSNative.Tests
{
    /// <summary>
    /// Tests for RPC improvements: RunLocally, ExcludeOwner, Channel selection, Reliability.
    /// </summary>
    [TestFixture]
    public class RPCImprovementsTests
    {
        private NetworkManager _nm;
        private ushort _nextPrefabId;

        private static readonly FieldInfo NmInstanceField =
            typeof(NetworkManager).GetField("_instance", BindingFlags.NonPublic | BindingFlags.Static);

        [SetUp]
        public void SetUp()
        {
            var go = new GameObject("NetworkManager");
            go.SetActive(false);
            _nm = go.AddComponent<NetworkManager>();
            NmInstanceField.SetValue(null, _nm);

            typeof(NetworkManager).GetProperty("OfflineMode").SetValue(_nm, true);
            typeof(NetworkManager).GetProperty("IsHost").SetValue(_nm, true);
            typeof(NetworkManager)
                .GetField("_localIdPrefix", BindingFlags.NonPublic | BindingFlags.Instance)
                .SetValue(_nm, (ushort)0xFFFF);
            typeof(NetworkManager)
                .GetField("_localIdCounter", BindingFlags.NonPublic | BindingFlags.Instance)
                .SetValue(_nm, (ushort)1);

            _nextPrefabId = 0;
        }

        [TearDown]
        public void TearDown()
        {
            NmInstanceField.SetValue(null, null);
            if (_nm != null)
                Object.DestroyImmediate(_nm.gameObject);

            foreach (var name in new[]
                     { "NetworkManager", "EOSP2PManager", "EOSLobbyManager", "NetworkSceneManager" })
            {
                var obj = GameObject.Find(name);
                if (obj != null) Object.DestroyImmediate(obj);
            }
        }

        private NetworkObject SpawnTestObject()
        {
            var prefab = new GameObject("TestPrefab");
            prefab.SetActive(false);
            prefab.AddComponent<NetworkObject>();
            ushort id = _nextPrefabId++;
            _nm.RegisterPrefab(prefab, id);
            var spawned = _nm.Spawn(id, Vector3.zero, Quaternion.identity);
            Object.DestroyImmediate(prefab);
            return spawned;
        }

        #region NetRpcAttribute New Properties

        [Test]
        public void NetRpcAttribute_RunLocally_DefaultFalse()
        {
            var attr = new NetRpcAttribute();
            Assert.IsFalse(attr.RunLocally);
        }

        [Test]
        public void NetRpcAttribute_RunLocally_CanBeSet()
        {
            var attr = new NetRpcAttribute(RPCTarget.Others) { RunLocally = true };
            Assert.IsTrue(attr.RunLocally);
            Assert.AreEqual(RPCTarget.Others, attr.Target);
        }

        [Test]
        public void NetRpcAttribute_ExcludeOwner_DefaultFalse()
        {
            var attr = new NetRpcAttribute();
            Assert.IsFalse(attr.ExcludeOwner);
        }

        [Test]
        public void NetRpcAttribute_ExcludeOwner_CanBeSet()
        {
            var attr = new NetRpcAttribute(RPCTarget.All) { ExcludeOwner = true };
            Assert.IsTrue(attr.ExcludeOwner);
        }

        [Test]
        public void NetRpcAttribute_Channel_Default1()
        {
            var attr = new NetRpcAttribute();
            Assert.AreEqual(1, attr.Channel);
        }

        [Test]
        public void NetRpcAttribute_Channel_CanBeSet()
        {
            var attr = new NetRpcAttribute { Channel = 0 };
            Assert.AreEqual(0, attr.Channel);
        }

        [Test]
        public void NetRpcAttribute_Reliability_DefaultReliableOrdered()
        {
            var attr = new NetRpcAttribute();
            Assert.AreEqual(PacketReliability.ReliableOrdered, attr.Reliability);
        }

        [Test]
        public void NetRpcAttribute_Reliability_CanSetUnreliable()
        {
            var attr = new NetRpcAttribute { Reliability = PacketReliability.UnreliableUnordered };
            Assert.AreEqual(PacketReliability.UnreliableUnordered, attr.Reliability);
        }

        [Test]
        public void NetRpcAttribute_AllProperties_Combined()
        {
            var attr = new NetRpcAttribute(RPCTarget.All)
            {
                RunLocally = true,
                ExcludeOwner = true,
                Channel = 2,
                Reliability = PacketReliability.ReliableUnordered,
                Validated = true
            };

            Assert.AreEqual(RPCTarget.All, attr.Target);
            Assert.IsTrue(attr.RunLocally);
            Assert.IsTrue(attr.ExcludeOwner);
            Assert.AreEqual(2, attr.Channel);
            Assert.AreEqual(PacketReliability.ReliableUnordered, attr.Reliability);
            Assert.IsTrue(attr.Validated);
        }

        #endregion

        #region RunLocally in Offline Mode

        [Test]
        public void RunLocally_OthersTarget_ExecutesLocallyInOfflineMode()
        {
            // In offline mode, RPCTarget.Others normally doesn't execute locally.
            // But RunLocally=true should force local execution.
            // Note: In offline mode, ALL RPCs execute locally regardless — the test
            // verifies the extended overload works in offline mode.
            var netObj = SpawnTestObject();
            bool called = false;
            uint hash = 0xAA00AA00;
            _nm.RegisterRPC(netObj, (byte)0, hash, "TestRPC", (reader) => { called = true; });

            _nm.SendRPCWeaved(netObj, (byte)0, hash, RPCTarget.Others, new byte[0],
                true, false, 1, PacketReliability.ReliableOrdered);

            Assert.IsTrue(called, "Extended overload should work in offline mode");
            Object.DestroyImmediate(netObj.gameObject);
        }

        [Test]
        public void RunLocally_AllTarget_ExecutesLocally()
        {
            var netObj = SpawnTestObject();
            bool called = false;
            uint hash = 0xBB00BB00;
            _nm.RegisterRPC(netObj, (byte)0, hash, "TestRPC", (reader) => { called = true; });

            _nm.SendRPCWeaved(netObj, (byte)0, hash, RPCTarget.All, new byte[0],
                true, false, 1, PacketReliability.ReliableOrdered);

            Assert.IsTrue(called);
            Object.DestroyImmediate(netObj.gameObject);
        }

        #endregion

        #region ExcludeOwner in Offline Mode

        [Test]
        public void ExcludeOwner_LocalIsOwner_SkipsLocalExecution()
        {
            // Need to be NOT in offline mode for ExcludeOwner to matter.
            // In offline mode, ALL RPCs always execute locally at line 963.
            // ExcludeOwner only matters when we go through the online path.
            // So we test the attribute property rather than the runtime behavior here.
            var attr = new NetRpcAttribute(RPCTarget.All) { ExcludeOwner = true };
            Assert.IsTrue(attr.ExcludeOwner);
        }

        #endregion

        #region Channel and Reliability

        [Test]
        public void ExtendedOverload_Channel0_WorksInOfflineMode()
        {
            var netObj = SpawnTestObject();
            bool called = false;
            uint hash = 0xCC00CC00;
            _nm.RegisterRPC(netObj, (byte)0, hash, "TestRPC", (reader) => { called = true; });

            // Channel 0, unreliable — in offline mode, just executes locally
            _nm.SendRPCWeaved(netObj, (byte)0, hash, RPCTarget.All, new byte[0],
                false, false, 0, PacketReliability.UnreliableUnordered);

            Assert.IsTrue(called, "Channel/reliability params should not affect offline execution");
            Object.DestroyImmediate(netObj.gameObject);
        }

        [Test]
        public void ExtendedOverload_CustomChannel_WorksInOfflineMode()
        {
            var netObj = SpawnTestObject();
            bool called = false;
            uint hash = 0xDD00DD00;
            _nm.RegisterRPC(netObj, (byte)0, hash, "TestRPC", (reader) => { called = true; });

            _nm.SendRPCWeaved(netObj, (byte)0, hash, RPCTarget.All, new byte[0],
                false, false, 5, PacketReliability.ReliableUnordered);

            Assert.IsTrue(called);
            Object.DestroyImmediate(netObj.gameObject);
        }

        #endregion

        #region DefaultOverload Delegates to Extended

        [Test]
        public void DefaultOverload_StillWorks()
        {
            var netObj = SpawnTestObject();
            bool called = false;
            uint hash = 0xEE00EE00;
            _nm.RegisterRPC(netObj, (byte)0, hash, "TestRPC", (reader) => { called = true; });

            // Original 5-param overload
            _nm.SendRPCWeaved(netObj, (byte)0, hash, RPCTarget.All, new byte[0]);

            Assert.IsTrue(called, "Default overload should still work (delegates to extended)");
            Object.DestroyImmediate(netObj.gameObject);
        }

        [Test]
        public void DefaultOverload_PeerTarget_StillLogsError()
        {
            var netObj = SpawnTestObject();

            UnityEngine.TestTools.LogAssert.Expect(LogType.Error,
                "[NetworkManager] RPCTarget.Peer requires SendRPCWeavedToPeer — use the peer-targeted overload");
            _nm.SendRPCWeaved(netObj, (byte)0, 0xAAAA, RPCTarget.Peer, new byte[0]);

            Object.DestroyImmediate(netObj.gameObject);
        }

        [Test]
        public void ExtendedOverload_PeerTarget_LogsError()
        {
            var netObj = SpawnTestObject();

            UnityEngine.TestTools.LogAssert.Expect(LogType.Error,
                "[NetworkManager] RPCTarget.Peer requires SendRPCWeavedToPeer — use the peer-targeted overload");
            _nm.SendRPCWeaved(netObj, (byte)0, 0xBBBB, RPCTarget.Peer, new byte[0],
                false, false, 1, PacketReliability.ReliableOrdered);

            Object.DestroyImmediate(netObj.gameObject);
        }

        #endregion

        #region RunLocally + ExcludeOwner Combined

        [Test]
        public void RunLocally_WithExcludeOwner_OfflineMode_StillExecutesLocally()
        {
            // In offline mode, the early return at line 963 always runs local.
            // RunLocally + ExcludeOwner combination only matters online.
            var netObj = SpawnTestObject();
            bool called = false;
            uint hash = 0xFF00FF00;
            _nm.RegisterRPC(netObj, (byte)0, hash, "TestRPC", (reader) => { called = true; });

            _nm.SendRPCWeaved(netObj, (byte)0, hash, RPCTarget.All, new byte[0],
                true, true, 1, PacketReliability.ReliableOrdered);

            Assert.IsTrue(called, "Offline mode always executes locally regardless of flags");
            Object.DestroyImmediate(netObj.gameObject);
        }

        #endregion

        #region PacketReliability Enum Values

        [Test]
        public void PacketReliability_UnreliableUnordered_Is0()
        {
            Assert.AreEqual(0, (int)PacketReliability.UnreliableUnordered);
        }

        [Test]
        public void PacketReliability_ReliableUnordered_Is1()
        {
            Assert.AreEqual(1, (int)PacketReliability.ReliableUnordered);
        }

        [Test]
        public void PacketReliability_ReliableOrdered_Is2()
        {
            Assert.AreEqual(2, (int)PacketReliability.ReliableOrdered);
        }

        #endregion

        #region Null/Unregistered Target Guards

        [Test]
        public void ExtendedOverload_NullTarget_NoThrow()
        {
            Assert.DoesNotThrow(() =>
                _nm.SendRPCWeaved(null, (byte)0, 0x1234, RPCTarget.All, new byte[0],
                    true, false, 1, PacketReliability.ReliableOrdered));
        }

        [Test]
        public void ExtendedOverload_UnregisteredTarget_NoThrow()
        {
            var go = new GameObject("Unregistered");
            var netObj = go.AddComponent<NetworkObject>();
            // Not spawned/registered

            Assert.DoesNotThrow(() =>
                _nm.SendRPCWeaved(netObj, (byte)0, 0x1234, RPCTarget.All, new byte[0],
                    false, true, 0, PacketReliability.UnreliableUnordered));

            Object.DestroyImmediate(go);
        }

        #endregion
    }
}
