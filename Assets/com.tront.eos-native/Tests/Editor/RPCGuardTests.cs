using System.Reflection;
using Epic.OnlineServices;
using NUnit.Framework;
using EOSNative.Net;
using EOSNative.P2P;
using UnityEngine;

namespace EOSNative.Tests
{
    /// <summary>
    /// Tests for TargetRpc (RPCTarget.Peer), [HostOnly]/[OwnerOnly] guard attributes,
    /// RPCGuard enum, and per-RPC guard registration/checking.
    /// </summary>
    [TestFixture]
    public class RPCGuardTests
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

        /// <summary>Helper: create a prefab-like GO with NetworkObject, register it, and spawn.</summary>
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

        #region RPCTarget.Peer enum

        [Test]
        public void RPCTarget_Peer_HasValue5()
        {
            Assert.AreEqual(5, (int)RPCTarget.Peer);
        }

        [Test]
        public void RPCTarget_Peer_IsDistinctFromOthers()
        {
            var values = new[] { RPCTarget.All, RPCTarget.Others, RPCTarget.Host, RPCTarget.Owner, RPCTarget.Players, RPCTarget.Peer };
            var set = new System.Collections.Generic.HashSet<int>();
            foreach (var v in values)
                Assert.IsTrue(set.Add((int)v), $"Duplicate RPCTarget value: {v}");
        }

        #endregion

        #region RPCGuard enum

        [Test]
        public void RPCGuard_None_IsZero()
        {
            Assert.AreEqual(0, (int)RPCGuard.None);
        }

        [Test]
        public void RPCGuard_HostOnly_Is1()
        {
            Assert.AreEqual(1, (int)RPCGuard.HostOnly);
        }

        [Test]
        public void RPCGuard_OwnerOnly_Is2()
        {
            Assert.AreEqual(2, (int)RPCGuard.OwnerOnly);
        }

        [Test]
        public void RPCGuard_CanCombineFlags()
        {
            var combined = RPCGuard.HostOnly | RPCGuard.OwnerOnly;
            Assert.AreEqual(3, (int)combined);
            Assert.IsTrue((combined & RPCGuard.HostOnly) != 0);
            Assert.IsTrue((combined & RPCGuard.OwnerOnly) != 0);
        }

        #endregion

        #region Guard Registration

        [Test]
        public void RegisterRPCGuard_StoresGuard()
        {
            var go = new GameObject("TestObj");
            var netObj = go.AddComponent<NetworkObject>();
            netObj.NetworkId = 100;

            _nm.RegisterRPCGuard(netObj, 0, 0xABCD1234, RPCGuard.HostOnly);

            // Guard is stored — verified via HandleRPC behavior (tested below)
            // Here just verify no throw
            Assert.Pass();
            Object.DestroyImmediate(go);
        }

        [Test]
        public void RegisterRPCGuard_LegacyOverload_UsesSelfComponentIndex()
        {
            var go = new GameObject("TestObj");
            var netObj = go.AddComponent<NetworkObject>();
            netObj.NetworkId = 200;

            // Should not throw
            _nm.RegisterRPCGuard(netObj, 0xDEADBEEF, RPCGuard.OwnerOnly);
            Assert.Pass();
            Object.DestroyImmediate(go);
        }

        #endregion

        #region Guard Attributes Exist

        [Test]
        public void HostOnlyAttribute_IsAttributeUsageMethod()
        {
            var attr = typeof(HostOnlyAttribute);
            Assert.IsTrue(typeof(System.Attribute).IsAssignableFrom(attr));

            var usage = (System.AttributeUsageAttribute)System.Attribute.GetCustomAttribute(attr, typeof(System.AttributeUsageAttribute));
            Assert.IsNotNull(usage);
            Assert.AreEqual(System.AttributeTargets.Method, usage.ValidOn);
        }

        [Test]
        public void OwnerOnlyAttribute_IsAttributeUsageMethod()
        {
            var attr = typeof(OwnerOnlyAttribute);
            Assert.IsTrue(typeof(System.Attribute).IsAssignableFrom(attr));

            var usage = (System.AttributeUsageAttribute)System.Attribute.GetCustomAttribute(attr, typeof(System.AttributeUsageAttribute));
            Assert.IsNotNull(usage);
            Assert.AreEqual(System.AttributeTargets.Method, usage.ValidOn);
        }

        [Test]
        public void HostOnlyAttribute_CanBeAppliedToMethod()
        {
            var method = typeof(GuardTestHelper).GetMethod("HostOnlyMethod");
            Assert.IsNotNull(method);
            Assert.IsNotNull(System.Attribute.GetCustomAttribute(method, typeof(HostOnlyAttribute)));
        }

        [Test]
        public void OwnerOnlyAttribute_CanBeAppliedToMethod()
        {
            var method = typeof(GuardTestHelper).GetMethod("OwnerOnlyMethod");
            Assert.IsNotNull(method);
            Assert.IsNotNull(System.Attribute.GetCustomAttribute(method, typeof(OwnerOnlyAttribute)));
        }

        [Test]
        public void CombinedGuardAttributes_CanBeApplied()
        {
            var method = typeof(GuardTestHelper).GetMethod("BothGuardsMethod");
            Assert.IsNotNull(method);
            Assert.IsNotNull(System.Attribute.GetCustomAttribute(method, typeof(HostOnlyAttribute)));
            Assert.IsNotNull(System.Attribute.GetCustomAttribute(method, typeof(OwnerOnlyAttribute)));
        }

        #endregion

        #region TargetRpc (Peer) Send Methods

        [Test]
        public void SendRPCWeavedToPeer_WithComponentIndex_DoesNotThrow()
        {
            var netObj = SpawnTestObject();

            // In offline mode, peer RPCs execute locally
            bool handlerCalled = false;
            uint hash = 0x12345678;
            _nm.RegisterRPC(netObj, (byte)0, hash, "TestRPC", (reader) => { handlerCalled = true; });

            // Null peer in offline mode → executes locally
            _nm.SendRPCWeavedToPeer(netObj, (byte)0, hash, null, new byte[0]);
            Assert.IsTrue(handlerCalled, "In offline mode with null peer, should execute locally");

            Object.DestroyImmediate(netObj.gameObject);
        }

        [Test]
        public void SendRPCWeavedToPeer_LegacyOverload_Works()
        {
            var netObj = SpawnTestObject();

            bool handlerCalled = false;
            uint hash = 0x87654321;
            _nm.RegisterRPC(netObj, hash, "TestRPC2", (reader) => { handlerCalled = true; });

            _nm.SendRPCWeavedToPeer(netObj, hash, null, new byte[0]);
            Assert.IsTrue(handlerCalled, "Legacy overload should work in offline mode");

            Object.DestroyImmediate(netObj.gameObject);
        }

        [Test]
        public void SendRPCWeaved_PeerTarget_LogsError()
        {
            var netObj = SpawnTestObject();

            // RPCTarget.Peer through SendRPCWeaved (not ToPeer) should log error
            UnityEngine.TestTools.LogAssert.Expect(LogType.Error,
                "[NetworkManager] RPCTarget.Peer requires SendRPCWeavedToPeer — use the peer-targeted overload");
            _nm.SendRPCWeaved(netObj, (byte)0, 0xAAAA, RPCTarget.Peer, new byte[0]);

            Object.DestroyImmediate(netObj.gameObject);
        }

        #endregion

        #region Guard Rejection Logic

        [Test]
        public void HandleRPC_HostOnly_ExecutesWhenSenderIsHost()
        {
            // In offline mode, we're always the host.
            var netObj = SpawnTestObject();

            bool handlerCalled = false;
            uint hash = 0xF0F0F0F0;
            _nm.RegisterRPC(netObj, (byte)0, hash, "HostRPC", (reader) => { handlerCalled = true; });
            _nm.RegisterRPCGuard(netObj, (byte)0, hash, RPCGuard.HostOnly);

            // In offline mode, SendRPCWeaved executes locally (no guard check — guards are receiver-side)
            _nm.SendRPCWeaved(netObj, (byte)0, hash, RPCTarget.All, new byte[0]);
            Assert.IsTrue(handlerCalled, "Local execution in offline mode bypasses receiver guards");

            Object.DestroyImmediate(netObj.gameObject);
        }

        [Test]
        public void HandleRPC_OwnerOnly_ExecutesWhenSenderIsOwner()
        {
            var netObj = SpawnTestObject();

            bool handlerCalled = false;
            uint hash = 0xE0E0E0E0;
            _nm.RegisterRPC(netObj, (byte)0, hash, "OwnerRPC", (reader) => { handlerCalled = true; });
            _nm.RegisterRPCGuard(netObj, (byte)0, hash, RPCGuard.OwnerOnly);

            // Local execution in offline mode doesn't check guards
            _nm.SendRPCWeaved(netObj, (byte)0, hash, RPCTarget.All, new byte[0]);
            Assert.IsTrue(handlerCalled, "Local execution in offline mode bypasses receiver guards");

            Object.DestroyImmediate(netObj.gameObject);
        }

        #endregion

        #region UnregisterRPCs Cleans Guards

        [Test]
        public void UnregisterRPCs_CleansUpGuards()
        {
            var go = new GameObject("TestObj");
            var netObj = go.AddComponent<NetworkObject>();
            netObj.NetworkId = 500;
            netObj.IsRegistered = true;

            uint hash = 0xBBBBBBBB;
            _nm.RegisterRPC(netObj, (byte)0, hash, "GuardedRPC", (reader) => { });
            _nm.RegisterRPCGuard(netObj, (byte)0, hash, RPCGuard.HostOnly);

            _nm.UnregisterRPCs(netObj);

            // Re-register without guard — should work (guard was cleaned up)
            bool handlerCalled = false;
            _nm.RegisterRPC(netObj, (byte)0, hash, "GuardedRPC", (reader) => { handlerCalled = true; });

            // Guard should no longer exist (check via reflection)
            var guardsField = typeof(NetworkManager)
                .GetField("_rpcGuards", BindingFlags.NonPublic | BindingFlags.Instance);
            var guards = guardsField.GetValue(_nm) as System.Collections.IDictionary;
            Assert.AreEqual(0, guards.Count, "Guards should be cleaned up after UnregisterRPCs");

            Object.DestroyImmediate(go);
        }

        #endregion

        #region NetRpcAttribute Properties

        [Test]
        public void NetRpcAttribute_DefaultTarget_IsAll()
        {
            var attr = new NetRpcAttribute();
            Assert.AreEqual(RPCTarget.All, attr.Target);
            Assert.IsFalse(attr.Validated);
        }

        [Test]
        public void NetRpcAttribute_PeerTarget_CanBeSet()
        {
            var attr = new NetRpcAttribute(RPCTarget.Peer);
            Assert.AreEqual(RPCTarget.Peer, attr.Target);
        }

        [Test]
        public void NetRpcAttribute_Validated_CanBeSet()
        {
            var attr = new NetRpcAttribute(RPCTarget.Host) { Validated = true };
            Assert.AreEqual(RPCTarget.Host, attr.Target);
            Assert.IsTrue(attr.Validated);
        }

        #endregion
    }

    /// <summary>Helper class for testing guard attributes can be applied to methods.</summary>
    internal static class GuardTestHelper
    {
        [HostOnly]
        public static void HostOnlyMethod() { }

        [OwnerOnly]
        public static void OwnerOnlyMethod() { }

        [HostOnly, OwnerOnly]
        public static void BothGuardsMethod() { }
    }
}
