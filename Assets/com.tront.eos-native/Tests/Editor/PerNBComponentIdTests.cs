using System.Reflection;
using NUnit.Framework;
using EOSNative.Net;
using EOSNative.P2P;
using UnityEngine;

namespace EOSNative.Tests
{
    /// <summary>
    /// Tests per-NetworkBehaviour component IDs (v2.40.0).
    /// Verifies: ComponentIndex assignment, RPC registration with componentIndex,
    /// RPC routing to correct NB via ExecuteRPCLocal (offline mode), and legacy backward compat.
    /// </summary>
    public class PerNBComponentIdTests
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

        private NetworkObject CreateRegisteredObject()
        {
            EnableOfflineMode();
            var prefab = new GameObject("Prefab");
            var prefabNo = prefab.AddComponent<NetworkObject>();
            _nm.RegisterPrefab(prefab, 0);
            var spawned = _nm.Spawn(prefab, Vector3.zero, Quaternion.identity);
            return spawned.GetComponent<NetworkObject>();
        }

        #region ComponentIndex Assignment

        [Test]
        public void ComponentIndex_ZeroBeforeSpawn()
        {
            var go = new GameObject("PreSpawn");
            go.AddComponent<NetworkObject>();
            var health = go.AddComponent<HealthBehaviour>();
            var combat = go.AddComponent<CombatBehaviour>();
            // Before NotifyNetworkSpawn, ComponentIndex is default (0)
            Assert.AreEqual(0, health.ComponentIndex);
            Assert.AreEqual(0, combat.ComponentIndex);
            Object.DestroyImmediate(go);
        }

        [Test]
        public void ComponentIndex_AssignedInGetComponentsOrder()
        {
            var go = new GameObject("Order");
            var netObj = go.AddComponent<NetworkObject>();
            var first = go.AddComponent<HealthBehaviour>();
            var second = go.AddComponent<CombatBehaviour>();
            first.InitForTest();
            second.InitForTest();

            netObj.NotifyNetworkSpawn();

            Assert.AreEqual(0, first.ComponentIndex);
            Assert.AreEqual(1, second.ComponentIndex);
            Object.DestroyImmediate(go);
        }

        [Test]
        public void ComponentIndex_ByteRange()
        {
            // ComponentIndex is a byte — verify it's assigned correctly
            var go = new GameObject("ByteRange");
            var netObj = go.AddComponent<NetworkObject>();
            var nb = go.AddComponent<HealthBehaviour>();
            nb.InitForTest();

            netObj.NotifyNetworkSpawn();

            Assert.IsInstanceOf<byte>(nb.ComponentIndex.GetType() == typeof(byte) ? (object)nb.ComponentIndex : null,
                "ComponentIndex should be of type byte");
            Assert.LessOrEqual(nb.ComponentIndex, 254, "ComponentIndex should be <= 254 (0xFF reserved for SELF)");
            Object.DestroyImmediate(go);
        }

        #endregion

        #region RPC Registration with ComponentIndex

        [Test]
        public void RegisterRPC_WithComponentIndex_NoThrow()
        {
            var netObj = CreateRegisteredObject();

            bool called = false;
            _nm.RegisterRPC(netObj, 0, 0x12345678u, "TestRPC", r => called = true);

            // Should not throw
            Assert.IsFalse(called); // Not called yet
            Object.DestroyImmediate(netObj.gameObject);
        }

        [Test]
        public void RegisterRPC_SameHashDifferentComponent_NoCollision()
        {
            var netObj = CreateRegisteredObject();

            bool calledA = false, calledB = false;
            uint hash = 0xDEADBEEF;
            _nm.RegisterRPC(netObj, 0, hash, "RpcOnNB0", r => calledA = true);
            _nm.RegisterRPC(netObj, 1, hash, "RpcOnNB1", r => calledB = true);

            // Both registered without collision (different componentIndex)
            Assert.IsFalse(calledA);
            Assert.IsFalse(calledB);
            Object.DestroyImmediate(netObj.gameObject);
        }

        [Test]
        public void RegisterRPC_SelfComponentIndex()
        {
            var netObj = CreateRegisteredObject();

            bool called = false;
            _nm.RegisterRPC(netObj, NetworkObject.SELF_COMPONENT_INDEX, 0xAAAA0000u, "SelfRpc", r => called = true);
            Assert.IsFalse(called);
            Object.DestroyImmediate(netObj.gameObject);
        }

        [Test]
        public void RegisterRPC_LegacyOverload_UsesSelfComponentIndex()
        {
            var netObj = CreateRegisteredObject();

            bool called = false;
            // Legacy 4-param overload (no componentIndex) should default to SELF
            _nm.RegisterRPC(netObj, 0xBBBB0000u, "LegacyRpc", r => called = true);

            // Dispatch with SELF_COMPONENT_INDEX should find it
            _nm.SendRPCWeaved(netObj, NetworkObject.SELF_COMPONENT_INDEX, 0xBBBB0000u, RPCTarget.All, new byte[0]);
            Assert.IsTrue(called, "Legacy RPC should be callable with SELF_COMPONENT_INDEX");
            Object.DestroyImmediate(netObj.gameObject);
        }

        #endregion

        #region RPC Routing (Offline Mode)

        [Test]
        public void RPC_RoutesToCorrectComponent_Index0()
        {
            var netObj = CreateRegisteredObject();

            bool calledA = false, calledB = false;
            uint hash = 0x11111111u;
            _nm.RegisterRPC(netObj, 0, hash, "RpcA", r => calledA = true);
            _nm.RegisterRPC(netObj, 1, hash, "RpcB", r => calledB = true);

            // Send to component 0
            _nm.SendRPCWeaved(netObj, 0, hash, RPCTarget.All, new byte[0]);

            Assert.IsTrue(calledA, "RPC should route to component 0");
            Assert.IsFalse(calledB, "Component 1 should NOT be called");
            Object.DestroyImmediate(netObj.gameObject);
        }

        [Test]
        public void RPC_RoutesToCorrectComponent_Index1()
        {
            var netObj = CreateRegisteredObject();

            bool calledA = false, calledB = false;
            uint hash = 0x22222222u;
            _nm.RegisterRPC(netObj, 0, hash, "RpcA", r => calledA = true);
            _nm.RegisterRPC(netObj, 1, hash, "RpcB", r => calledB = true);

            // Send to component 1
            _nm.SendRPCWeaved(netObj, 1, hash, RPCTarget.All, new byte[0]);

            Assert.IsFalse(calledA, "Component 0 should NOT be called");
            Assert.IsTrue(calledB, "RPC should route to component 1");
            Object.DestroyImmediate(netObj.gameObject);
        }

        [Test]
        public void RPC_RoutesToSelf_0xFF()
        {
            var netObj = CreateRegisteredObject();

            bool calledSelf = false, calledNB = false;
            uint hash = 0x33333333u;
            _nm.RegisterRPC(netObj, NetworkObject.SELF_COMPONENT_INDEX, hash, "SelfRpc", r => calledSelf = true);
            _nm.RegisterRPC(netObj, 0, hash, "NBRpc", r => calledNB = true);

            // Send with SELF component index
            _nm.SendRPCWeaved(netObj, NetworkObject.SELF_COMPONENT_INDEX, hash, RPCTarget.All, new byte[0]);

            Assert.IsTrue(calledSelf, "SELF RPC should be called");
            Assert.IsFalse(calledNB, "NB RPC should NOT be called");
            Object.DestroyImmediate(netObj.gameObject);
        }

        [Test]
        public void RPC_UnregisteredComponentIndex_Fallback()
        {
            var netObj = CreateRegisteredObject();

            bool called = false;
            uint hash = 0x44444444u;
            // Only register for SELF
            _nm.RegisterRPC(netObj, NetworkObject.SELF_COMPONENT_INDEX, hash, "FallbackRpc", r => called = true);

            // Send with component 5 (not registered) — should fallback to SELF
            _nm.SendRPCWeaved(netObj, 5, hash, RPCTarget.All, new byte[0]);

            Assert.IsTrue(called, "Should fallback to SELF when componentIndex not found");
            Object.DestroyImmediate(netObj.gameObject);
        }

        [Test]
        public void RPC_WithArgData_PassedToHandler()
        {
            var netObj = CreateRegisteredObject();

            int receivedValue = -1;
            uint hash = 0x55555555u;
            _nm.RegisterRPC(netObj, 0, hash, "ArgRpc", r => receivedValue = r.ReadInt32());

            // Prepare arg data
            var w = new NetWriter();
            w.WriteInt32(42);
            var argData = w.ToArray();

            _nm.SendRPCWeaved(netObj, 0, hash, RPCTarget.All, argData);

            Assert.AreEqual(42, receivedValue, "Handler should receive the correct arg data");
            Object.DestroyImmediate(netObj.gameObject);
        }

        [Test]
        public void RPC_MultipleCalls_EachRoutesCorrectly()
        {
            var netObj = CreateRegisteredObject();

            int countA = 0, countB = 0, countSelf = 0;
            uint hashA = 0x66660000u, hashB = 0x77770000u;
            _nm.RegisterRPC(netObj, 0, hashA, "RpcA", r => countA++);
            _nm.RegisterRPC(netObj, 1, hashB, "RpcB", r => countB++);
            _nm.RegisterRPC(netObj, NetworkObject.SELF_COMPONENT_INDEX, hashA, "SelfA", r => countSelf++);

            _nm.SendRPCWeaved(netObj, 0, hashA, RPCTarget.All, new byte[0]); // A
            _nm.SendRPCWeaved(netObj, 1, hashB, RPCTarget.All, new byte[0]); // B
            _nm.SendRPCWeaved(netObj, 1, hashB, RPCTarget.All, new byte[0]); // B again
            _nm.SendRPCWeaved(netObj, NetworkObject.SELF_COMPONENT_INDEX, hashA, RPCTarget.All, new byte[0]); // Self

            Assert.AreEqual(1, countA);
            Assert.AreEqual(2, countB);
            Assert.AreEqual(1, countSelf);
            Object.DestroyImmediate(netObj.gameObject);
        }

        #endregion

        #region RPCKey Uniqueness

        [Test]
        public void RPCKey_DifferentComponents_DifferentKeys()
        {
            // Test that same NetworkId + same MethodHash but different ComponentIndex
            // produces different handler registrations (i.e., both can coexist)
            var netObj = CreateRegisteredObject();

            int countA = 0, countB = 0;
            uint hash = 0x99999999u;
            _nm.RegisterRPC(netObj, 0, hash, "SameHash0", r => countA++);
            _nm.RegisterRPC(netObj, 1, hash, "SameHash1", r => countB++);

            _nm.SendRPCWeaved(netObj, 0, hash, RPCTarget.All, new byte[0]);
            _nm.SendRPCWeaved(netObj, 1, hash, RPCTarget.All, new byte[0]);

            Assert.AreEqual(1, countA, "Component 0 should be called once");
            Assert.AreEqual(1, countB, "Component 1 should be called once");
            Object.DestroyImmediate(netObj.gameObject);
        }

        [Test]
        public void RPCKey_SameComponent_DifferentHash()
        {
            var netObj = CreateRegisteredObject();

            int countA = 0, countB = 0;
            _nm.RegisterRPC(netObj, 0, 0xAAAA1111u, "Rpc1", r => countA++);
            _nm.RegisterRPC(netObj, 0, 0xBBBB2222u, "Rpc2", r => countB++);

            _nm.SendRPCWeaved(netObj, 0, 0xAAAA1111u, RPCTarget.All, new byte[0]);

            Assert.AreEqual(1, countA);
            Assert.AreEqual(0, countB);
            Object.DestroyImmediate(netObj.gameObject);
        }

        #endregion

        #region Validated RPC with ComponentIndex

        [Test]
        public void ValidatedRPC_OfflineMode_ExecutesLocally()
        {
            var netObj = CreateRegisteredObject();

            bool called = false;
            uint hash = 0xCCCCCCCCu;
            _nm.RegisterRPC(netObj, 0, hash, "ValidatedRpc", r => called = true);
            _nm.MarkRPCValidated(hash);

            _nm.SendRPCValidated(netObj, 0, hash, RPCTarget.All, new byte[0]);

            Assert.IsTrue(called, "Validated RPC should execute locally in offline mode");
            Object.DestroyImmediate(netObj.gameObject);
        }

        #endregion
    }
}
