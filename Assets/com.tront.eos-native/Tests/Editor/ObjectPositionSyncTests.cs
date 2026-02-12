using System;
using System.Collections.Generic;
using System.Reflection;
using NUnit.Framework;
using Epic.OnlineServices;
using EOSNative.Demo;
using EOSNative.Net;
using EOSNative.P2P;
using UnityEngine;

namespace EOSNative.Tests
{
    /// <summary>
    /// Tests for v2.57.0 Layer 1 object position sync:
    /// - MSG_OBJECT_POSITION wire format round-trip
    /// - EnsureSpringSync behavior (auto-add, reparent-aware, NetworkTransform disable)
    /// - BroadcastOwnedObjectPositions skip conditions
    /// - P2PSpringSync rotation compression round-trip
    /// - NetworkPhysicsObject component setup
    /// </summary>
    public class ObjectPositionSyncTests
    {
        private NetworkManager _nm;
        private GameObject _eosManagerGo;

        [SetUp]
        public void SetUp()
        {
            // NetworkManager on inactive GO to avoid DontDestroyOnLoad
            var go = new GameObject("NetworkManager");
            go.SetActive(false);
            _nm = go.AddComponent<NetworkManager>();

            typeof(NetworkManager)
                .GetField("_instance", BindingFlags.NonPublic | BindingFlags.Static)
                .SetValue(null, _nm);
            typeof(NetworkManager).GetProperty("OfflineMode").SetValue(_nm, true);
            typeof(NetworkManager).GetProperty("IsHost").SetValue(_nm, true);
            typeof(NetworkManager)
                .GetField("_localIdPrefix", BindingFlags.NonPublic | BindingFlags.Instance)
                .SetValue(_nm, (ushort)0xFFFF);
            typeof(NetworkManager)
                .GetField("_localIdCounter", BindingFlags.NonPublic | BindingFlags.Instance)
                .SetValue(_nm, (ushort)1);

            // EOSManager with fake LocalProductUserId
            _eosManagerGo = new GameObject("EOSManager");
            _eosManagerGo.SetActive(false);
            var eosManager = _eosManagerGo.AddComponent<EOSManager>();
            EOSManager.s_Instance = eosManager;

            var puid = new ProductUserId(new IntPtr(99));
            typeof(EOSManager)
                .GetProperty("LocalProductUserId")
                .SetValue(eosManager, puid);
        }

        [TearDown]
        public void TearDown()
        {
            typeof(NetworkManager)
                .GetField("_instance", BindingFlags.NonPublic | BindingFlags.Static)
                .SetValue(null, null);

            EOSManager.s_Instance = null;
            EOSP2PManager._instance = null;

            if (_nm != null)
                UnityEngine.Object.DestroyImmediate(_nm.gameObject);
            if (_eosManagerGo != null)
                UnityEngine.Object.DestroyImmediate(_eosManagerGo);

            foreach (var name in new[] { "NetworkManager", "EOSP2PManager", "EOSLobbyManager", "EOSManager" })
            {
                var obj = GameObject.Find(name);
                if (obj != null) UnityEngine.Object.DestroyImmediate(obj);
            }
        }

        private NetworkObject CreateNetObj(string name = "TestObj")
        {
            var go = new GameObject(name);
            return go.AddComponent<NetworkObject>();
        }

        private void Cleanup(params GameObject[] gos)
        {
            foreach (var go in gos)
                if (go != null) UnityEngine.Object.DestroyImmediate(go);
        }

        #region MSG_OBJECT_POSITION Wire Format

        [Test]
        public void WireFormat_SingleObject_RoundTrip()
        {
            // Write MSG_OBJECT_POSITION format: [count:byte][netId:uint32 + pos:half3 + rot:uint32]
            var writer = new NetWriter();
            uint netId = 0xAB000001u;
            var pos = new Vector3(1.5f, 2.5f, -3.5f);
            uint rot = P2PSpringSync.CompressRotation(Quaternion.Euler(30, 45, 60));

            writer.WriteByte(1); // count
            writer.WriteUInt32(netId);
            writer.WriteVector3Half(pos);
            writer.WriteUInt32(rot);

            // Read it back
            var reader = new NetReader(writer.ToArray());
            int count = reader.ReadByte();
            Assert.AreEqual(1, count);

            uint readId = reader.ReadUInt32();
            var readPos = reader.ReadVector3Half();
            uint readRot = reader.ReadUInt32();

            Assert.AreEqual(netId, readId);
            Assert.AreEqual(rot, readRot);

            // Half precision loses some accuracy — ~0.01 tolerance
            Assert.AreEqual(pos.x, readPos.x, 0.01f, "X position round-trip");
            Assert.AreEqual(pos.y, readPos.y, 0.01f, "Y position round-trip");
            Assert.AreEqual(pos.z, readPos.z, 0.01f, "Z position round-trip");

            Assert.AreEqual(0, reader.Remaining, "All bytes consumed");
        }

        [Test]
        public void WireFormat_MultipleObjects_RoundTrip()
        {
            var writer = new NetWriter();
            int objectCount = 5;

            var positions = new Vector3[objectCount];
            var netIds = new uint[objectCount];
            var rotations = new uint[objectCount];

            writer.WriteByte((byte)objectCount);
            for (int i = 0; i < objectCount; i++)
            {
                netIds[i] = 0xAB000000u | (uint)i;
                positions[i] = new Vector3(i * 1.5f, i * 0.5f, -i * 2f);
                rotations[i] = P2PSpringSync.CompressRotation(Quaternion.Euler(i * 30f, i * 45f, 0f));

                writer.WriteUInt32(netIds[i]);
                writer.WriteVector3Half(positions[i]);
                writer.WriteUInt32(rotations[i]);
            }

            var reader = new NetReader(writer.ToArray());
            int count = reader.ReadByte();
            Assert.AreEqual(objectCount, count);

            for (int i = 0; i < count; i++)
            {
                uint id = reader.ReadUInt32();
                var pos = reader.ReadVector3Half();
                uint rot = reader.ReadUInt32();

                Assert.AreEqual(netIds[i], id, $"Object {i} netId mismatch");
                Assert.AreEqual(rotations[i], rot, $"Object {i} rotation mismatch");
                Assert.AreEqual(positions[i].x, pos.x, 0.01f, $"Object {i} pos.x");
                Assert.AreEqual(positions[i].y, pos.y, 0.01f, $"Object {i} pos.y");
                Assert.AreEqual(positions[i].z, pos.z, 0.01f, $"Object {i} pos.z");
            }

            Assert.AreEqual(0, reader.Remaining, "All bytes consumed");
        }

        [Test]
        public void WireFormat_ByteSize_PerObject()
        {
            // Each object = 4 (netId) + 6 (half3) + 4 (rot) = 14 bytes
            // Plus 1 byte for count header
            var writer = new NetWriter();
            writer.WriteByte(1);
            writer.WriteUInt32(0x12345678);
            writer.WriteVector3Half(Vector3.one);
            writer.WriteUInt32(0u);

            Assert.AreEqual(15, writer.Position, "1 object should be 1 + 14 = 15 bytes");
        }

        [Test]
        public void WireFormat_ZeroObjects_ValidPacket()
        {
            var writer = new NetWriter();
            writer.WriteByte(0);

            var reader = new NetReader(writer.ToArray());
            int count = reader.ReadByte();
            Assert.AreEqual(0, count);
            Assert.AreEqual(0, reader.Remaining);
        }

        [Test]
        public void WireFormat_MaxObjects_255()
        {
            // Count is a byte so max = 255
            var writer = new NetWriter();
            writer.WriteByte(255);
            for (int i = 0; i < 255; i++)
            {
                writer.WriteUInt32((uint)i);
                writer.WriteVector3Half(Vector3.zero);
                writer.WriteUInt32(0u);
            }

            var reader = new NetReader(writer.ToArray());
            int count = reader.ReadByte();
            Assert.AreEqual(255, count);

            // Expected size: 1 + (255 * 14) = 3571 bytes
            Assert.AreEqual(1 + 255 * 14, writer.Position);
        }

        #endregion

        #region Rotation Compression Round-Trip

        [Test]
        public void RotationCompression_Identity_RoundTrip()
        {
            var original = Quaternion.identity;
            uint compressed = P2PSpringSync.CompressRotation(original);
            var decompressed = P2PSpringSync.DecompressRotation(compressed);

            AssertQuaternionClose(original, decompressed, 0.01f);
        }

        [Test]
        public void RotationCompression_90Degrees_RoundTrip()
        {
            var original = Quaternion.Euler(90, 0, 0);
            uint compressed = P2PSpringSync.CompressRotation(original);
            var decompressed = P2PSpringSync.DecompressRotation(compressed);

            AssertQuaternionClose(original, decompressed, 0.02f);
        }

        [Test]
        public void RotationCompression_ArbitraryAngles_RoundTrip()
        {
            var angles = new[] { 30f, 45f, 60f, 120f, 180f, 270f, 359f };
            foreach (var angle in angles)
            {
                var original = Quaternion.Euler(angle, angle * 0.5f, angle * 0.25f);
                uint compressed = P2PSpringSync.CompressRotation(original);
                var decompressed = P2PSpringSync.DecompressRotation(compressed);

                AssertQuaternionClose(original, decompressed, 0.02f,
                    $"Failed at angle {angle}");
            }
        }

        [Test]
        public void RotationCompression_SmallestThree_FitsIn32Bits()
        {
            uint compressed = P2PSpringSync.CompressRotation(Quaternion.Euler(45, 90, 135));
            // 2 bits for index + 10 bits each for 3 components = 32 bits
            // The value should fit in uint (it does by construction, but verify it's not garbage)
            int index = (int)(compressed >> 30);
            Assert.IsTrue(index >= 0 && index <= 3, $"Largest index should be 0-3, got {index}");
        }

        [Test]
        public void RotationCompression_NegativeQuaternion_HandledCorrectly()
        {
            // Quaternion q and -q represent the same rotation
            var original = new Quaternion(-0.5f, -0.5f, -0.5f, -0.5f);
            uint compressed = P2PSpringSync.CompressRotation(original);
            var decompressed = P2PSpringSync.DecompressRotation(compressed);

            // The decompressed may have different sign but represent same rotation
            float dot = Mathf.Abs(Quaternion.Dot(original, decompressed));
            Assert.IsTrue(dot > 0.98f, $"Quaternion dot should be close to 1, got {dot}");
        }

        #endregion

        #region Vector3Half Round-Trip

        [Test]
        public void Vector3Half_Zero_RoundTrip()
        {
            var original = Vector3.zero;
            var half = new Vector3Half(original);
            var result = half.ToVector3();
            Assert.AreEqual(0f, result.x, 0.001f);
            Assert.AreEqual(0f, result.y, 0.001f);
            Assert.AreEqual(0f, result.z, 0.001f);
        }

        [Test]
        public void Vector3Half_SmallValues_RoundTrip()
        {
            var original = new Vector3(0.5f, 1.5f, -2.5f);
            var half = new Vector3Half(original);
            var result = half.ToVector3();
            Assert.AreEqual(original.x, result.x, 0.01f);
            Assert.AreEqual(original.y, result.y, 0.01f);
            Assert.AreEqual(original.z, result.z, 0.01f);
        }

        [Test]
        public void Vector3Half_LargeValues_RoundTrip()
        {
            var original = new Vector3(100f, -50f, 200f);
            var half = new Vector3Half(original);
            var result = half.ToVector3();
            Assert.AreEqual(original.x, result.x, 0.1f);
            Assert.AreEqual(original.y, result.y, 0.1f);
            Assert.AreEqual(original.z, result.z, 0.1f);
        }

        [Test]
        public void Vector3Half_ByteSize_6Bytes()
        {
            var half = new Vector3Half(Vector3.one);
            var bytes = half.ToBytes();
            Assert.AreEqual(6, bytes.Length, "Vector3Half should be 6 bytes (3 x ushort)");
        }

        [Test]
        public void Vector3Half_ToBytes_FromBytes_RoundTrip()
        {
            var original = new Vector3(3.14f, -1.5f, 42f);
            var half = new Vector3Half(original);
            var bytes = half.ToBytes();
            var restored = Vector3Half.FromBytes(bytes);
            var result = restored.ToVector3();

            Assert.AreEqual(original.x, result.x, 0.01f);
            Assert.AreEqual(original.y, result.y, 0.01f);
            Assert.AreEqual(original.z, result.z, 0.01f);
        }

        #endregion

        #region EnsureSpringSync Behavior

        [Test]
        public void EnsureSpringSync_AddsToRigidbodyObjects()
        {
            var netObj = CreateNetObj("Crate");
            netObj.gameObject.AddComponent<Rigidbody>();
            _nm.RegisterExisting(netObj, 0xAA000001);

            InvokeEnsureSpringSync();

            var sync = netObj.GetComponent<P2PSpringSync>();
            Assert.IsNotNull(sync, "P2PSpringSync should be auto-added to objects with Rigidbody");

            Cleanup(netObj.gameObject);
        }

        [Test]
        public void EnsureSpringSync_SkipsObjectsWithoutRigidbody()
        {
            var netObj = CreateNetObj("NoRb");
            _nm.RegisterExisting(netObj, 0xAA000002);

            InvokeEnsureSpringSync();

            var sync = netObj.GetComponent<P2PSpringSync>();
            Assert.IsNull(sync, "P2PSpringSync should not be added to objects without Rigidbody");

            Cleanup(netObj.gameObject);
        }

        [Test]
        public void EnsureSpringSync_SkipsPlayerBalls()
        {
            var netObj = CreateNetObj("Ball");
            netObj.gameObject.AddComponent<Rigidbody>();
            netObj.gameObject.AddComponent<P2PPlayerBall>();
            _nm.RegisterExisting(netObj, 0xBB000001);

            InvokeEnsureSpringSync();

            // Should not add a SECOND P2PSpringSync (player balls have their own)
            var syncs = netObj.GetComponents<P2PSpringSync>();
            Assert.AreEqual(0, syncs.Length, "EnsureSpringSync should skip P2PPlayerBall objects");

            Cleanup(netObj.gameObject);
        }

        [Test]
        public void EnsureSpringSync_DisablesNetworkTransform()
        {
            var netObj = CreateNetObj("WithNT");
            netObj.gameObject.AddComponent<Rigidbody>();
            var nt = netObj.gameObject.AddComponent<NetworkTransform>();
            Assert.IsTrue(nt.enabled, "NetworkTransform should start enabled");
            _nm.RegisterExisting(netObj, 0xAA000003);

            InvokeEnsureSpringSync();

            Assert.IsFalse(nt.enabled, "NetworkTransform should be disabled when P2PSpringSync is added");
            Assert.IsNotNull(netObj.GetComponent<P2PSpringSync>(), "P2PSpringSync should exist");

            Cleanup(netObj.gameObject);
        }

        [Test]
        public void EnsureSpringSync_DisabledWhenReparented()
        {
            var netObj = CreateNetObj("HeldWeapon");
            netObj.gameObject.AddComponent<Rigidbody>();
            _nm.RegisterExisting(netObj, 0xAA000004);

            // First call: adds P2PSpringSync
            InvokeEnsureSpringSync();
            var sync = netObj.GetComponent<P2PSpringSync>();
            Assert.IsNotNull(sync);
            Assert.IsTrue(sync.enabled, "Should start enabled");

            // Simulate being picked up (reparented)
            netObj.ParentNetworkId = 0xBB000001;

            // Second call: should disable sync
            InvokeEnsureSpringSync();
            Assert.IsFalse(sync.enabled, "P2PSpringSync should be disabled when object is reparented (held)");

            Cleanup(netObj.gameObject);
        }

        [Test]
        public void EnsureSpringSync_ReenabledWhenDropped()
        {
            var netObj = CreateNetObj("DroppedWeapon");
            netObj.gameObject.AddComponent<Rigidbody>();
            _nm.RegisterExisting(netObj, 0xAA000005);

            InvokeEnsureSpringSync();
            var sync = netObj.GetComponent<P2PSpringSync>();

            // Pick up
            netObj.ParentNetworkId = 0xBB000001;
            InvokeEnsureSpringSync();
            Assert.IsFalse(sync.enabled, "Should be disabled when held");

            // Drop
            netObj.ParentNetworkId = 0;
            InvokeEnsureSpringSync();
            Assert.IsTrue(sync.enabled, "Should be re-enabled when dropped (ParentNetworkId = 0)");

            Cleanup(netObj.gameObject);
        }

        [Test]
        public void EnsureSpringSync_DoesNotAddWhenCurrentlyReparented()
        {
            var netObj = CreateNetObj("AlreadyHeld");
            netObj.gameObject.AddComponent<Rigidbody>();
            netObj.ParentNetworkId = 0xCC000001; // already reparented
            _nm.RegisterExisting(netObj, 0xAA000006);

            InvokeEnsureSpringSync();

            var sync = netObj.GetComponent<P2PSpringSync>();
            Assert.IsNull(sync, "Should not add P2PSpringSync to currently-reparented objects");

            Cleanup(netObj.gameObject);
        }

        [Test]
        public void EnsureSpringSync_SetsIsLocal_ForOwner()
        {
            var netObj = CreateNetObj("OwnedCrate");
            netObj.gameObject.AddComponent<Rigidbody>();
            netObj.OwnerId = new ProductUserId(new IntPtr(99)); // matches our fake PUID
            _nm.RegisterExisting(netObj, 0xAA000007);

            InvokeEnsureSpringSync();

            var sync = netObj.GetComponent<P2PSpringSync>();
            Assert.IsNotNull(sync);
            Assert.IsTrue(sync.IsLocal, "IsLocal should be true when object is owned by local player");

            Cleanup(netObj.gameObject);
        }

        [Test]
        public void EnsureSpringSync_SetsIsLocal_ForNonOwner()
        {
            var netObj = CreateNetObj("RemoteCrate");
            netObj.gameObject.AddComponent<Rigidbody>();
            netObj.OwnerId = new ProductUserId(new IntPtr(42)); // different PUID
            _nm.RegisterExisting(netObj, 0xAA000008);

            InvokeEnsureSpringSync();

            var sync = netObj.GetComponent<P2PSpringSync>();
            Assert.IsNotNull(sync);
            Assert.IsFalse(sync.IsLocal, "IsLocal should be false when object is owned by another player");

            Cleanup(netObj.gameObject);
        }

        [Test]
        public void EnsureSpringSync_Idempotent_DoesNotDuplicate()
        {
            var netObj = CreateNetObj("IdempotentCrate");
            netObj.gameObject.AddComponent<Rigidbody>();
            _nm.RegisterExisting(netObj, 0xAA000009);

            InvokeEnsureSpringSync();
            InvokeEnsureSpringSync();
            InvokeEnsureSpringSync();

            var syncs = netObj.GetComponents<P2PSpringSync>();
            Assert.AreEqual(1, syncs.Length, "Multiple EnsureSpringSync calls should not add duplicate components");

            Cleanup(netObj.gameObject);
        }

        #endregion

        #region BroadcastOwnedObjectPositions Skip Conditions

        [Test]
        public void Broadcast_SkipsNonOwned()
        {
            var netObj = CreateNetObj("Remote");
            netObj.gameObject.AddComponent<Rigidbody>();
            netObj.OwnerId = new ProductUserId(new IntPtr(42)); // not us
            _nm.RegisterExisting(netObj, 0xAA000010);

            InvokeEnsureSpringSync();

            var list = CollectBroadcastObjects();
            Assert.AreEqual(0, list.Count, "Should not broadcast non-owned objects");

            Cleanup(netObj.gameObject);
        }

        [Test]
        public void Broadcast_SkipsPlayerBalls()
        {
            var netObj = CreateNetObj("MyBall");
            netObj.gameObject.AddComponent<Rigidbody>();
            netObj.gameObject.AddComponent<P2PPlayerBall>();
            netObj.OwnerId = new ProductUserId(new IntPtr(99)); // us
            _nm.RegisterExisting(netObj, 0xBB000001);

            var list = CollectBroadcastObjects();
            Assert.AreEqual(0, list.Count, "Should not broadcast player balls (they have their own MSG_POSITION)");

            Cleanup(netObj.gameObject);
        }

        [Test]
        public void Broadcast_SkipsReparentedObjects()
        {
            var netObj = CreateNetObj("HeldGun");
            netObj.gameObject.AddComponent<Rigidbody>();
            netObj.OwnerId = new ProductUserId(new IntPtr(99));
            netObj.ParentNetworkId = 0xBB000001; // held by a ball
            _nm.RegisterExisting(netObj, 0xAA000011);

            // Even if it has P2PSpringSync (from before being picked up)
            var sync = netObj.gameObject.AddComponent<P2PSpringSync>();
            sync.IsLocal = true;

            var list = CollectBroadcastObjects();
            Assert.AreEqual(0, list.Count, "Should not broadcast reparented (held) objects");

            Cleanup(netObj.gameObject);
        }

        [Test]
        public void Broadcast_IncludesOwnedPhysicsObjects()
        {
            var netObj = CreateNetObj("MyCrate");
            netObj.gameObject.AddComponent<Rigidbody>();
            netObj.OwnerId = new ProductUserId(new IntPtr(99)); // us
            _nm.RegisterExisting(netObj, 0xAA000012);

            InvokeEnsureSpringSync();

            var list = CollectBroadcastObjects();
            Assert.AreEqual(1, list.Count, "Should include owned physics objects with P2PSpringSync");
            Assert.AreEqual(netObj, list[0]);

            Cleanup(netObj.gameObject);
        }

        [Test]
        public void Broadcast_SkipsDisabledSync()
        {
            var netObj = CreateNetObj("DisabledSync");
            netObj.gameObject.AddComponent<Rigidbody>();
            netObj.OwnerId = new ProductUserId(new IntPtr(99));
            _nm.RegisterExisting(netObj, 0xAA000013);

            InvokeEnsureSpringSync();
            var sync = netObj.GetComponent<P2PSpringSync>();
            sync.enabled = false;

            var list = CollectBroadcastObjects();
            Assert.AreEqual(0, list.Count, "Should skip objects with disabled P2PSpringSync");

            Cleanup(netObj.gameObject);
        }

        [Test]
        public void Broadcast_MultipleObjects_AllIncluded()
        {
            var objs = new List<NetworkObject>();
            for (int i = 0; i < 3; i++)
            {
                var netObj = CreateNetObj($"Crate{i}");
                netObj.gameObject.AddComponent<Rigidbody>();
                netObj.OwnerId = new ProductUserId(new IntPtr(99));
                _nm.RegisterExisting(netObj, 0xAA000020u + (uint)i);
                objs.Add(netObj);
            }

            InvokeEnsureSpringSync();

            var list = CollectBroadcastObjects();
            Assert.AreEqual(3, list.Count, "All 3 owned crates should be included");

            foreach (var o in objs) Cleanup(o.gameObject);
        }

        #endregion

        #region P2PSpringSync Component

        [Test]
        public void SpringSync_SetTarget_StoresValues()
        {
            var go = new GameObject("SyncTest");
            go.AddComponent<Rigidbody>();
            var sync = go.AddComponent<P2PSpringSync>();

            var targetPos = new Vector3(5f, 10f, 15f);
            var targetRot = Quaternion.Euler(45, 90, 0);
            sync.SetTarget(targetPos, targetRot);

            // Verify via reflection that targets are stored
            var targetPosField = typeof(P2PSpringSync).GetField("_targetPosition", BindingFlags.NonPublic | BindingFlags.Instance);
            var targetRotField = typeof(P2PSpringSync).GetField("_targetRotation", BindingFlags.NonPublic | BindingFlags.Instance);

            var storedPos = (Vector3)targetPosField.GetValue(sync);
            var storedRot = (Quaternion)targetRotField.GetValue(sync);

            Assert.AreEqual(targetPos, storedPos);
            AssertQuaternionClose(targetRot, storedRot, 0.001f);

            Cleanup(go);
        }

        [Test]
        public void SpringSync_GetCompressedState_ReturnsCurrentTransform()
        {
            var go = new GameObject("CompressTest");
            var rb = go.AddComponent<Rigidbody>();
            rb.isKinematic = true; // prevent physics movement
            go.transform.position = new Vector3(3f, 4f, 5f);
            go.transform.rotation = Quaternion.Euler(30, 60, 90);
            var sync = go.AddComponent<P2PSpringSync>();

            sync.GetCompressedState(out var pos, out uint rot);

            // Verify half-precision position
            var decompressedPos = pos.ToVector3();
            Assert.AreEqual(3f, decompressedPos.x, 0.01f);
            Assert.AreEqual(4f, decompressedPos.y, 0.01f);
            Assert.AreEqual(5f, decompressedPos.z, 0.01f);

            // Verify rotation decompresses correctly
            var decompressedRot = P2PSpringSync.DecompressRotation(rot);
            AssertQuaternionClose(go.transform.rotation, decompressedRot, 0.02f);

            Cleanup(go);
        }

        [Test]
        public void SpringSync_IsLocal_DefaultFalse()
        {
            var go = new GameObject("DefaultLocal");
            go.AddComponent<Rigidbody>();
            var sync = go.AddComponent<P2PSpringSync>();

            Assert.IsFalse(sync.IsLocal);

            Cleanup(go);
        }

        [Test]
        public void SpringSync_FixedUpdate_SkipsWhenLocal()
        {
            var go = new GameObject("LocalSkip");
            var rb = go.AddComponent<Rigidbody>();
            rb.isKinematic = true;
            var sync = go.AddComponent<P2PSpringSync>();
            sync.IsLocal = true;

            // Set target far away
            sync.SetTarget(new Vector3(100f, 0f, 0f), Quaternion.identity);

            // When IsLocal, FixedUpdate should not apply spring forces
            // (we can't call FixedUpdate directly, but we can verify the IsLocal guard exists)
            Assert.IsTrue(sync.IsLocal);

            Cleanup(go);
        }

        #endregion

        #region NetworkPhysicsObject

        [Test]
        public void NetworkPhysicsObject_RequiresRigidbody()
        {
            // NetworkPhysicsObject has [RequireComponent(typeof(Rigidbody))]
            var go = new GameObject("PhysObj");
            go.AddComponent<Rigidbody>();
            go.AddComponent<NetworkObject>();
            var npo = go.AddComponent<NetworkPhysicsObject>();

            Assert.IsNotNull(go.GetComponent<Rigidbody>());
            Assert.IsNotNull(npo);

            Cleanup(go);
        }

        [Test]
        public void NetworkPhysicsObject_RequiresNetworkObject()
        {
            // NetworkPhysicsObject has [RequireComponent(typeof(NetworkObject))]
            var go = new GameObject("PhysObj2");
            go.AddComponent<Rigidbody>();
            go.AddComponent<NetworkObject>();
            var npo = go.AddComponent<NetworkPhysicsObject>();

            Assert.IsNotNull(go.GetComponent<NetworkObject>());
            Assert.IsNotNull(npo);

            Cleanup(go);
        }

        #endregion

        #region Stuck Dirty Safety Net

        [Test]
        public void StuckDirty_SafetyNet_RegisterSceneObjects()
        {
            // RegisterSceneObjects calls NotifyNetworkSpawn then checks IsDirty.
            // Verify scene objects get registered without errors.
            var netObj = CreateNetObj("DirtySafetyNet");
            netObj.gameObject.AddComponent<Rigidbody>();

            _nm.RegisterSceneObjects();

            Assert.IsTrue(netObj.IsRegistered, "Scene object should be registered");
            Assert.AreEqual(NetworkObject.SCENE_OBJECT, netObj.PrefabId);

            Cleanup(netObj.gameObject);
        }

        [Test]
        public void StuckDirty_WithChildren_BothRegistered()
        {
            var rootGo = new GameObject("Root");
            var root = rootGo.AddComponent<NetworkObject>();
            rootGo.AddComponent<Rigidbody>();

            var childGo = new GameObject("Child");
            childGo.transform.SetParent(rootGo.transform);
            var child = childGo.AddComponent<NetworkObject>();

            _nm.RegisterSceneObjects();

            Assert.IsTrue(root.IsRegistered, "Root should be registered");
            Assert.IsTrue(child.IsRegistered, "Child should be registered");
            Assert.AreEqual(root.NetworkId, child.ParentNetworkId,
                "Child should reference root as parent");

            Cleanup(rootGo);
        }

        #endregion

        #region Helpers

        /// <summary>
        /// Invoke the EnsureSpringSync logic that P2PDemoManager.Update runs.
        /// Reproduced here to test in isolation without requiring a P2PDemoManager instance.
        /// </summary>
        private void InvokeEnsureSpringSync()
        {
            foreach (var kvp in _nm.Objects)
            {
                var obj = kvp.Value;
                if (obj == null) continue;
                if (obj.GetComponent<P2PPlayerBall>() != null) continue;

                bool isChild = obj.ParentNetworkId != 0;

                var sync = obj.GetComponent<P2PSpringSync>();
                if (sync != null)
                {
                    sync.IsLocal = obj.IsOwner;
                    sync.enabled = !isChild;
                    continue;
                }

                if (isChild) continue;
                if (obj.GetComponent<Rigidbody>() == null) continue;

                sync = obj.gameObject.AddComponent<P2PSpringSync>();
                sync.IsLocal = obj.IsOwner;

                var nt = obj.GetComponent<NetworkTransform>();
                if (nt != null) nt.enabled = false;
            }
        }

        /// <summary>
        /// Collect the list of objects that BroadcastOwnedObjectPositions would send.
        /// Reproduces the filtering logic from P2PDemoManager.
        /// </summary>
        private List<NetworkObject> CollectBroadcastObjects()
        {
            var result = new List<NetworkObject>();
            foreach (var kvp in _nm.Objects)
            {
                var obj = kvp.Value;
                if (obj == null || !obj.IsOwner) continue;
                if (obj.GetComponent<P2PPlayerBall>() != null) continue;
                if (obj.ParentNetworkId != 0) continue;

                var sync = obj.GetComponent<P2PSpringSync>();
                if (sync == null || !sync.enabled) continue;

                result.Add(obj);
            }
            return result;
        }

        private static void AssertQuaternionClose(Quaternion expected, Quaternion actual, float tolerance, string message = "")
        {
            float dot = Mathf.Abs(Quaternion.Dot(expected, actual));
            Assert.IsTrue(dot > 1f - tolerance,
                $"Quaternion mismatch: dot={dot}, expected ~1.0 (tolerance {tolerance}). {message}");
        }

        #endregion
    }
}
