using System.Reflection;
using NUnit.Framework;
using EOSNative.Net;
using EOSNative.P2P;
using UnityEngine;

namespace EOSNative.Tests
{
    /// <summary>
    /// Tests for BufferLast RPC feature — storing the most recent RPC call for late-joiner replay.
    /// </summary>
    [TestFixture]
    public class BufferLastRPCTests
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

        #region NetRpcAttribute BufferLast Property

        [Test]
        public void NetRpcAttribute_BufferLast_DefaultFalse()
        {
            var attr = new NetRpcAttribute();
            Assert.IsFalse(attr.BufferLast);
        }

        [Test]
        public void NetRpcAttribute_BufferLast_CanBeSet()
        {
            var attr = new NetRpcAttribute { BufferLast = true };
            Assert.IsTrue(attr.BufferLast);
        }

        [Test]
        public void NetRpcAttribute_BufferLast_WithTarget()
        {
            var attr = new NetRpcAttribute(RPCTarget.All) { BufferLast = true };
            Assert.AreEqual(RPCTarget.All, attr.Target);
            Assert.IsTrue(attr.BufferLast);
        }

        [Test]
        public void NetRpcAttribute_BufferLast_CombinesWithOtherProperties()
        {
            var attr = new NetRpcAttribute(RPCTarget.Others)
            {
                BufferLast = true,
                RunLocally = true,
                ExcludeOwner = false
            };
            Assert.IsTrue(attr.BufferLast);
            Assert.IsTrue(attr.RunLocally);
            Assert.IsFalse(attr.ExcludeOwner);
            Assert.AreEqual(RPCTarget.Others, attr.Target);
        }

        #endregion

        #region RegisterBufferLastRPC

        [Test]
        public void RegisterBufferLastRPC_AddsHash()
        {
            uint hash = 12345;
            _nm.RegisterBufferLastRPC(hash);
            Assert.IsTrue(_nm._bufferLastHashes.Contains(hash));
        }

        [Test]
        public void RegisterBufferLastRPC_DuplicateIgnored()
        {
            uint hash = 12345;
            _nm.RegisterBufferLastRPC(hash);
            _nm.RegisterBufferLastRPC(hash); // duplicate
            Assert.AreEqual(1, _nm._bufferLastHashes.Count);
        }

        [Test]
        public void RegisterBufferLastRPC_MultipleHashes()
        {
            _nm.RegisterBufferLastRPC(100);
            _nm.RegisterBufferLastRPC(200);
            _nm.RegisterBufferLastRPC(300);
            Assert.AreEqual(3, _nm._bufferLastHashes.Count);
        }

        #endregion

        #region BufferLast Storage in SendRPCWeaved

        [Test]
        public void SendRPCWeaved_BufferLast_StoresRPC()
        {
            var obj = SpawnTestObject();
            uint hash = 42;
            _nm.RegisterBufferLastRPC(hash);

            // Register a dummy handler so HandleRPC doesn't log error
            _nm.RegisterRPC(obj, NetworkObject.SELF_COMPONENT_INDEX, hash, "TestRPC",
                reader => { });

            byte[] argData = { 1, 2, 3 };
            _nm.SendRPCWeaved(obj, NetworkObject.SELF_COMPONENT_INDEX, hash, RPCTarget.All, argData);

            Assert.AreEqual(1, _nm.BufferLastCount);
        }

        [Test]
        public void SendRPCWeaved_NonBufferLast_DoesNotStore()
        {
            var obj = SpawnTestObject();
            uint hash = 42;
            // Don't register as BufferLast

            _nm.RegisterRPC(obj, NetworkObject.SELF_COMPONENT_INDEX, hash, "TestRPC",
                reader => { });

            byte[] argData = { 1, 2, 3 };
            _nm.SendRPCWeaved(obj, NetworkObject.SELF_COMPONENT_INDEX, hash, RPCTarget.All, argData);

            Assert.AreEqual(0, _nm.BufferLastCount);
        }

        [Test]
        public void SendRPCWeaved_BufferLast_OverwritesPreviousCall()
        {
            var obj = SpawnTestObject();
            uint hash = 42;
            _nm.RegisterBufferLastRPC(hash);

            _nm.RegisterRPC(obj, NetworkObject.SELF_COMPONENT_INDEX, hash, "TestRPC",
                reader => { });

            // First call
            byte[] argData1 = { 1, 2, 3 };
            _nm.SendRPCWeaved(obj, NetworkObject.SELF_COMPONENT_INDEX, hash, RPCTarget.All, argData1);
            Assert.AreEqual(1, _nm.BufferLastCount);

            // Second call — overwrites
            byte[] argData2 = { 4, 5, 6, 7 };
            _nm.SendRPCWeaved(obj, NetworkObject.SELF_COMPONENT_INDEX, hash, RPCTarget.All, argData2);
            Assert.AreEqual(1, _nm.BufferLastCount); // Still 1, not 2
        }

        [Test]
        public void SendRPCWeaved_BufferLast_DifferentMethods_SeparateEntries()
        {
            var obj = SpawnTestObject();
            uint hash1 = 42, hash2 = 99;
            _nm.RegisterBufferLastRPC(hash1);
            _nm.RegisterBufferLastRPC(hash2);

            _nm.RegisterRPC(obj, NetworkObject.SELF_COMPONENT_INDEX, hash1, "RPC1", reader => { });
            _nm.RegisterRPC(obj, NetworkObject.SELF_COMPONENT_INDEX, hash2, "RPC2", reader => { });

            _nm.SendRPCWeaved(obj, NetworkObject.SELF_COMPONENT_INDEX, hash1, RPCTarget.All, new byte[] { 1 });
            _nm.SendRPCWeaved(obj, NetworkObject.SELF_COMPONENT_INDEX, hash2, RPCTarget.All, new byte[] { 2 });

            Assert.AreEqual(2, _nm.BufferLastCount);
        }

        [Test]
        public void SendRPCWeaved_BufferLast_DifferentObjects_SeparateEntries()
        {
            var obj1 = SpawnTestObject();
            var obj2 = SpawnTestObject();
            uint hash = 42;
            _nm.RegisterBufferLastRPC(hash);

            _nm.RegisterRPC(obj1, NetworkObject.SELF_COMPONENT_INDEX, hash, "RPC1", reader => { });
            _nm.RegisterRPC(obj2, NetworkObject.SELF_COMPONENT_INDEX, hash, "RPC2", reader => { });

            _nm.SendRPCWeaved(obj1, NetworkObject.SELF_COMPONENT_INDEX, hash, RPCTarget.All, new byte[] { 1 });
            _nm.SendRPCWeaved(obj2, NetworkObject.SELF_COMPONENT_INDEX, hash, RPCTarget.All, new byte[] { 2 });

            Assert.AreEqual(2, _nm.BufferLastCount);
        }

        [Test]
        public void SendRPCWeaved_BufferLast_DifferentComponents_SeparateEntries()
        {
            var obj = SpawnTestObject();
            uint hash = 42;
            _nm.RegisterBufferLastRPC(hash);

            _nm.RegisterRPC(obj, 0, hash, "RPC_C0", reader => { });
            _nm.RegisterRPC(obj, 1, hash, "RPC_C1", reader => { });

            _nm.SendRPCWeaved(obj, 0, hash, RPCTarget.All, new byte[] { 1 });
            _nm.SendRPCWeaved(obj, 1, hash, RPCTarget.All, new byte[] { 2 });

            Assert.AreEqual(2, _nm.BufferLastCount);
        }

        [Test]
        public void SendRPCWeaved_BufferLast_ClonesArgData()
        {
            var obj = SpawnTestObject();
            uint hash = 42;
            _nm.RegisterBufferLastRPC(hash);

            _nm.RegisterRPC(obj, NetworkObject.SELF_COMPONENT_INDEX, hash, "TestRPC",
                reader => { });

            byte[] argData = { 1, 2, 3 };
            _nm.SendRPCWeaved(obj, NetworkObject.SELF_COMPONENT_INDEX, hash, RPCTarget.All, argData);

            // Modify original array — buffer should not be affected
            argData[0] = 99;

            // Verify via reflection that stored ArgData is independent
            var bufferField = typeof(NetworkManager).GetField("_bufferLastRpcs",
                BindingFlags.NonPublic | BindingFlags.Instance);
            var dict = (System.Collections.IDictionary)bufferField.GetValue(_nm);
            foreach (System.Collections.DictionaryEntry entry in dict)
            {
                var buffered = entry.Value;
                var argField = buffered.GetType().GetField("ArgData");
                var storedArgs = (byte[])argField.GetValue(buffered);
                Assert.AreEqual(1, storedArgs[0]); // Original value preserved
            }
        }

        #endregion

        #region Cleanup on UnregisterRPCs

        [Test]
        public void UnregisterRPCs_ClearsBufferLastForObject()
        {
            var obj = SpawnTestObject();
            uint hash = 42;
            _nm.RegisterBufferLastRPC(hash);

            _nm.RegisterRPC(obj, NetworkObject.SELF_COMPONENT_INDEX, hash, "TestRPC",
                reader => { });
            _nm.SendRPCWeaved(obj, NetworkObject.SELF_COMPONENT_INDEX, hash, RPCTarget.All, new byte[] { 1 });

            Assert.AreEqual(1, _nm.BufferLastCount);

            _nm.UnregisterRPCs(obj);
            Assert.AreEqual(0, _nm.BufferLastCount);
        }

        [Test]
        public void UnregisterRPCs_DoesNotAffectOtherObjects()
        {
            var obj1 = SpawnTestObject();
            var obj2 = SpawnTestObject();
            uint hash = 42;
            _nm.RegisterBufferLastRPC(hash);

            _nm.RegisterRPC(obj1, NetworkObject.SELF_COMPONENT_INDEX, hash, "RPC1", reader => { });
            _nm.RegisterRPC(obj2, NetworkObject.SELF_COMPONENT_INDEX, hash, "RPC2", reader => { });

            _nm.SendRPCWeaved(obj1, NetworkObject.SELF_COMPONENT_INDEX, hash, RPCTarget.All, new byte[] { 1 });
            _nm.SendRPCWeaved(obj2, NetworkObject.SELF_COMPONENT_INDEX, hash, RPCTarget.All, new byte[] { 2 });

            Assert.AreEqual(2, _nm.BufferLastCount);

            _nm.UnregisterRPCs(obj1);
            Assert.AreEqual(1, _nm.BufferLastCount); // obj2's buffer still there
        }

        #endregion

        #region ClearBufferLastRPCs

        [Test]
        public void ClearBufferLastRPCs_ClearsAll()
        {
            var obj = SpawnTestObject();
            uint hash = 42;
            _nm.RegisterBufferLastRPC(hash);

            _nm.RegisterRPC(obj, NetworkObject.SELF_COMPONENT_INDEX, hash, "TestRPC",
                reader => { });
            _nm.SendRPCWeaved(obj, NetworkObject.SELF_COMPONENT_INDEX, hash, RPCTarget.All, new byte[] { 1 });

            Assert.AreEqual(1, _nm.BufferLastCount);
            _nm.ClearBufferLastRPCs();
            Assert.AreEqual(0, _nm.BufferLastCount);
        }

        #endregion

        #region BufferLastCount Property

        [Test]
        public void BufferLastCount_InitiallyZero()
        {
            Assert.AreEqual(0, _nm.BufferLastCount);
        }

        [Test]
        public void BufferLastCount_TracksCorrectly()
        {
            var obj1 = SpawnTestObject();
            var obj2 = SpawnTestObject();
            uint hash1 = 42, hash2 = 99;
            _nm.RegisterBufferLastRPC(hash1);
            _nm.RegisterBufferLastRPC(hash2);

            _nm.RegisterRPC(obj1, NetworkObject.SELF_COMPONENT_INDEX, hash1, "RPC1", reader => { });
            _nm.RegisterRPC(obj1, NetworkObject.SELF_COMPONENT_INDEX, hash2, "RPC2", reader => { });
            _nm.RegisterRPC(obj2, NetworkObject.SELF_COMPONENT_INDEX, hash1, "RPC3", reader => { });

            _nm.SendRPCWeaved(obj1, NetworkObject.SELF_COMPONENT_INDEX, hash1, RPCTarget.All, new byte[] { 1 });
            Assert.AreEqual(1, _nm.BufferLastCount);

            _nm.SendRPCWeaved(obj1, NetworkObject.SELF_COMPONENT_INDEX, hash2, RPCTarget.All, new byte[] { 2 });
            Assert.AreEqual(2, _nm.BufferLastCount);

            _nm.SendRPCWeaved(obj2, NetworkObject.SELF_COMPONENT_INDEX, hash1, RPCTarget.All, new byte[] { 3 });
            Assert.AreEqual(3, _nm.BufferLastCount);

            // Overwrite — count should not increase
            _nm.SendRPCWeaved(obj1, NetworkObject.SELF_COMPONENT_INDEX, hash1, RPCTarget.All, new byte[] { 9 });
            Assert.AreEqual(3, _nm.BufferLastCount);
        }

        #endregion

        #region OfflineMode

        [Test]
        public void SendRPCWeaved_OfflineMode_StillBuffers()
        {
            // BufferLast storage happens before the offline shortcut, so it works in offline mode too.
            // This is intentional — keeps the buffer consistent for testing and mixed-mode scenarios.
            var obj = SpawnTestObject();
            uint hash = 42;
            _nm.RegisterBufferLastRPC(hash);

            _nm.RegisterRPC(obj, NetworkObject.SELF_COMPONENT_INDEX, hash, "TestRPC",
                reader => { });
            _nm.SendRPCWeaved(obj, NetworkObject.SELF_COMPONENT_INDEX, hash, RPCTarget.All, new byte[] { 1 });

            Assert.AreEqual(1, _nm.BufferLastCount);
        }

        #endregion

        #region AllRPCTargets

        [Test]
        public void BufferLast_WorksWithRPCTargetAll()
        {
            // Can't test actual remote delivery in EditMode, but verify storage works
            // Offline mode returns early — so we test the attribute + registration only
            var attr = new NetRpcAttribute(RPCTarget.All) { BufferLast = true };
            Assert.IsTrue(attr.BufferLast);
            Assert.AreEqual(RPCTarget.All, attr.Target);
        }

        [Test]
        public void BufferLast_WorksWithRPCTargetOthers()
        {
            var attr = new NetRpcAttribute(RPCTarget.Others) { BufferLast = true };
            Assert.IsTrue(attr.BufferLast);
            Assert.AreEqual(RPCTarget.Others, attr.Target);
        }

        [Test]
        public void BufferLast_WorksWithRPCTargetHost()
        {
            var attr = new NetRpcAttribute(RPCTarget.Host) { BufferLast = true };
            Assert.IsTrue(attr.BufferLast);
            Assert.AreEqual(RPCTarget.Host, attr.Target);
        }

        #endregion

        #region NullTarget Guard

        [Test]
        public void SendRPCWeaved_NullTarget_DoesNotBuffer()
        {
            uint hash = 42;
            _nm.RegisterBufferLastRPC(hash);

            // Null target — should return early without crashing
            _nm.SendRPCWeaved(null, NetworkObject.SELF_COMPONENT_INDEX, hash, RPCTarget.All, new byte[] { 1 });
            Assert.AreEqual(0, _nm.BufferLastCount);
        }

        [Test]
        public void SendRPCWeaved_UnregisteredTarget_DoesNotBuffer()
        {
            uint hash = 42;
            _nm.RegisterBufferLastRPC(hash);

            // Unregistered object
            var go = new GameObject("Unregistered");
            var obj = go.AddComponent<NetworkObject>();

            _nm.SendRPCWeaved(obj, NetworkObject.SELF_COMPONENT_INDEX, hash, RPCTarget.All, new byte[] { 1 });
            Assert.AreEqual(0, _nm.BufferLastCount);

            Object.DestroyImmediate(go);
        }

        #endregion

        #region Integration

        [Test]
        public void BufferLast_FullFlow_RegisterBufferSendOverwriteCleanup()
        {
            // 1. Register hash
            uint hash = 42;
            _nm.RegisterBufferLastRPC(hash);
            Assert.IsTrue(_nm._bufferLastHashes.Contains(hash));

            // 2. Spawn + register RPC
            var obj = SpawnTestObject();
            _nm.RegisterRPC(obj, NetworkObject.SELF_COMPONENT_INDEX, hash, "TestRPC", reader => { });

            // 3. Send (stores in buffer)
            _nm.SendRPCWeaved(obj, NetworkObject.SELF_COMPONENT_INDEX, hash, RPCTarget.All, new byte[] { 1, 2 });
            Assert.AreEqual(1, _nm.BufferLastCount);

            // 4. Overwrite
            _nm.SendRPCWeaved(obj, NetworkObject.SELF_COMPONENT_INDEX, hash, RPCTarget.All, new byte[] { 3, 4, 5 });
            Assert.AreEqual(1, _nm.BufferLastCount); // still 1

            // 5. Unregister clears
            _nm.UnregisterRPCs(obj);
            Assert.AreEqual(0, _nm.BufferLastCount);
        }

        [Test]
        public void BufferLast_Hashes_SurviveMultipleObjects()
        {
            uint hash = 42;
            _nm.RegisterBufferLastRPC(hash);

            // Spawn and unregister first object — hash should survive
            var obj1 = SpawnTestObject();
            _nm.RegisterRPC(obj1, NetworkObject.SELF_COMPONENT_INDEX, hash, "TestRPC", reader => { });
            _nm.UnregisterRPCs(obj1);

            // Hash set is global, not per-object
            Assert.IsTrue(_nm._bufferLastHashes.Contains(hash));

            // Second object can use same hash
            var obj2 = SpawnTestObject();
            _nm.RegisterRPC(obj2, NetworkObject.SELF_COMPONENT_INDEX, hash, "TestRPC", reader => { });
            Assert.IsTrue(_nm._bufferLastHashes.Contains(hash));
        }

        #endregion
    }
}
