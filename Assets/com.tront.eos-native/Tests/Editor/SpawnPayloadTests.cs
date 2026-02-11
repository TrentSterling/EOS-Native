using System.Reflection;
using Epic.OnlineServices;
using NUnit.Framework;
using EOSNative.Net;
using EOSNative.P2P;
using UnityEngine;

namespace EOSNative.Tests
{
    /// <summary>
    /// Tests for spawn payload (WriteSpawnData/ReadSpawnData) and
    /// OnTick/OnPeerConnected/OnPeerDisconnected hooks on NetworkBehaviour.
    /// Uses inactive GO + reflection pattern.
    /// </summary>
    [TestFixture]
    public class SpawnPayloadTests
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

            typeof(NetworkManager).GetProperty("OfflineMode").SetValue(_nm, true);
            typeof(NetworkManager).GetProperty("IsHost").SetValue(_nm, true);
            typeof(NetworkManager)
                .GetField("_localIdPrefix", BindingFlags.NonPublic | BindingFlags.Instance)
                .SetValue(_nm, (ushort)0xFFFF);
            typeof(NetworkManager)
                .GetField("_localIdCounter", BindingFlags.NonPublic | BindingFlags.Instance)
                .SetValue(_nm, (ushort)1);
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

        private (NetworkObject, TrackingBehaviour) CreateTrackedObject()
        {
            var go = new GameObject("TestObj");
            var netObj = go.AddComponent<NetworkObject>();
            var tb = go.AddComponent<TrackingBehaviour>();
            return (netObj, tb);
        }

        #region WriteSpawnPayload / ReadSpawnPayload

        [Test]
        public void WriteSpawnPayload_CallsWriteSpawnDataOnBehaviours()
        {
            var (netObj, tb) = CreateTrackedObject();
            // Simulate what NotifyNetworkSpawn does (cache behaviours)
            netObj.NotifyNetworkSpawn();

            var writer = new NetWriter();
            netObj.WriteSpawnPayload(writer);

            Assert.AreEqual(1, tb.SpawnDataWritten, "WriteSpawnData should be called once");
            Assert.AreEqual(42, tb.LastWrittenValue);
            Object.DestroyImmediate(netObj.gameObject);
        }

        [Test]
        public void ReadSpawnPayload_CallsReadSpawnDataOnBehaviours()
        {
            var (netObj, tb) = CreateTrackedObject();
            netObj.NotifyNetworkSpawn();

            // Write the payload first
            var writer = new NetWriter();
            netObj.WriteSpawnPayload(writer);

            // Create a second object to read
            var (netObj2, tb2) = CreateTrackedObject();
            netObj2.NotifyNetworkSpawn();

            var reader = new NetReader(writer.ToArraySegment());
            netObj2.ReadSpawnPayload(reader);

            Assert.AreEqual(1, tb2.SpawnDataRead, "ReadSpawnData should be called once");
            Assert.AreEqual(42, tb2.LastReadValue);

            Object.DestroyImmediate(netObj.gameObject);
            Object.DestroyImmediate(netObj2.gameObject);
        }

        [Test]
        public void SpawnPayload_RoundTrip_PreservesData()
        {
            var (netObj, tb) = CreateTrackedObject();
            netObj.NotifyNetworkSpawn();

            var writer = new NetWriter();
            netObj.WriteSpawnPayload(writer);
            var data = writer.ToArraySegment();

            // Read on a fresh object
            var (netObj2, tb2) = CreateTrackedObject();
            netObj2.NotifyNetworkSpawn();

            var reader = new NetReader(data);
            netObj2.ReadSpawnPayload(reader);

            Assert.AreEqual(42, tb2.LastReadValue, "Data should round-trip correctly");
            Assert.AreEqual(reader.Position, data.Count + data.Offset, "Reader should consume all bytes");

            Object.DestroyImmediate(netObj.gameObject);
            Object.DestroyImmediate(netObj2.gameObject);
        }

        [Test]
        public void WriteSpawnPayload_NoBehaviours_WritesZeroSections()
        {
            var go = new GameObject("Empty");
            var netObj = go.AddComponent<NetworkObject>();
            // No behaviours — _behaviours is null until NotifyNetworkSpawn
            // But WriteSpawnPayload handles null _behaviours

            var writer = new NetWriter();
            netObj.WriteSpawnPayload(writer);
            var data = writer.ToArraySegment();

            // Should be just 1 byte: sectionCount = 0
            Assert.AreEqual(1, data.Count, "Empty payload should be 1 byte (section count = 0)");
            Assert.AreEqual(0, data.Array[data.Offset], "Section count should be 0");

            Object.DestroyImmediate(go);
        }

        [Test]
        public void ReadSpawnPayload_ExtraSections_Skipped()
        {
            // Write payload with 2 sections (more than the target has behaviours)
            var writer = new NetWriter();
            writer.WriteByte(2); // 2 sections
            // Section 0: 4 bytes of data
            writer.WriteUInt16(4);
            writer.WriteInt32(99);
            // Section 1: 4 bytes of data
            writer.WriteUInt16(4);
            writer.WriteInt32(77);

            // Target object has 1 behaviour
            var (netObj, tb) = CreateTrackedObject();
            netObj.NotifyNetworkSpawn();

            var reader = new NetReader(writer.ToArraySegment());
            netObj.ReadSpawnPayload(reader);

            Assert.AreEqual(1, tb.SpawnDataRead, "Should read first section");
            Assert.AreEqual(99, tb.LastReadValue, "Should read first section's data");
            // Reader should have consumed everything (including skipped section)
            Assert.AreEqual(reader.Position, writer.ToArraySegment().Count + writer.ToArraySegment().Offset,
                "Reader should consume all bytes including skipped sections");

            Object.DestroyImmediate(netObj.gameObject);
        }

        [Test]
        public void ReadSpawnPayload_ZeroSections_IsNoOp()
        {
            var writer = new NetWriter();
            writer.WriteByte(0); // 0 sections

            var (netObj, tb) = CreateTrackedObject();
            netObj.NotifyNetworkSpawn();

            var reader = new NetReader(writer.ToArraySegment());
            netObj.ReadSpawnPayload(reader);

            Assert.AreEqual(0, tb.SpawnDataRead, "Should not call ReadSpawnData with 0 sections");

            Object.DestroyImmediate(netObj.gameObject);
        }

        [Test]
        public void WriteSpawnPayload_MultipleBehaviours_WritesAll()
        {
            var go = new GameObject("Multi");
            var netObj = go.AddComponent<NetworkObject>();
            go.AddComponent<TrackingBehaviour>();
            go.AddComponent<TrackingBehaviour>();
            netObj.NotifyNetworkSpawn();

            var writer = new NetWriter();
            netObj.WriteSpawnPayload(writer);

            var behaviours = go.GetComponents<TrackingBehaviour>();
            Assert.AreEqual(2, behaviours.Length);
            Assert.AreEqual(1, behaviours[0].SpawnDataWritten);
            Assert.AreEqual(1, behaviours[1].SpawnDataWritten);

            Object.DestroyImmediate(go);
        }

        #endregion

        #region NotifyTick

        [Test]
        public void NotifyTick_DispatchesToBehaviours()
        {
            var (netObj, tb) = CreateTrackedObject();
            netObj.NotifyNetworkSpawn();

            netObj.NotifyTick(100);

            Assert.AreEqual(1, tb.TickCount, "OnTick should be called once");
            Assert.AreEqual(100u, tb.LastTick, "Tick number should be passed");

            Object.DestroyImmediate(netObj.gameObject);
        }

        [Test]
        public void NotifyTick_MultipleTicks_IncrementCounter()
        {
            var (netObj, tb) = CreateTrackedObject();
            netObj.NotifyNetworkSpawn();

            for (uint t = 1; t <= 10; t++)
                netObj.NotifyTick(t);

            Assert.AreEqual(10, tb.TickCount, "OnTick should be called 10 times");
            Assert.AreEqual(10u, tb.LastTick, "Last tick should be 10");

            Object.DestroyImmediate(netObj.gameObject);
        }

        [Test]
        public void NotifyTick_NoBehaviours_NoThrow()
        {
            var go = new GameObject("Empty");
            var netObj = go.AddComponent<NetworkObject>();
            // _behaviours is null — should not throw
            Assert.DoesNotThrow(() => netObj.NotifyTick(1));

            Object.DestroyImmediate(go);
        }

        [Test]
        public void NotifyTick_MultipleBehaviours_AllReceive()
        {
            var go = new GameObject("Multi");
            var netObj = go.AddComponent<NetworkObject>();
            go.AddComponent<TrackingBehaviour>();
            go.AddComponent<TrackingBehaviour>();
            netObj.NotifyNetworkSpawn();

            netObj.NotifyTick(5);

            var behaviours = go.GetComponents<TrackingBehaviour>();
            Assert.AreEqual(1, behaviours[0].TickCount);
            Assert.AreEqual(1, behaviours[1].TickCount);
            Assert.AreEqual(5u, behaviours[0].LastTick);
            Assert.AreEqual(5u, behaviours[1].LastTick);

            Object.DestroyImmediate(go);
        }

        #endregion

        #region NotifyPeerConnected / NotifyPeerDisconnected

        [Test]
        public void NotifyPeerConnected_DispatchesToBehaviours()
        {
            var (netObj, tb) = CreateTrackedObject();
            netObj.NotifyNetworkSpawn();

            var fakePeer = new ProductUserId(new System.IntPtr(1));
            netObj.NotifyPeerConnected(fakePeer);

            Assert.AreEqual(1, tb.PeerConnectedCount, "OnPeerConnected should be called once");
            Assert.AreEqual(fakePeer, tb.LastConnectedPeer, "Should receive the correct peer");

            Object.DestroyImmediate(netObj.gameObject);
        }

        [Test]
        public void NotifyPeerDisconnected_DispatchesToBehaviours()
        {
            var (netObj, tb) = CreateTrackedObject();
            netObj.NotifyNetworkSpawn();

            var fakePeer = new ProductUserId(new System.IntPtr(2));
            netObj.NotifyPeerDisconnected(fakePeer);

            Assert.AreEqual(1, tb.PeerDisconnectedCount, "OnPeerDisconnected should be called once");
            Assert.AreEqual(fakePeer, tb.LastDisconnectedPeer, "Should receive the correct peer");

            Object.DestroyImmediate(netObj.gameObject);
        }

        [Test]
        public void NotifyPeerConnected_NoBehaviours_NoThrow()
        {
            var go = new GameObject("Empty");
            var netObj = go.AddComponent<NetworkObject>();
            Assert.DoesNotThrow(() => netObj.NotifyPeerConnected(new ProductUserId(new System.IntPtr(3))));
            Object.DestroyImmediate(go);
        }

        [Test]
        public void NotifyPeerDisconnected_NoBehaviours_NoThrow()
        {
            var go = new GameObject("Empty");
            var netObj = go.AddComponent<NetworkObject>();
            Assert.DoesNotThrow(() => netObj.NotifyPeerDisconnected(new ProductUserId(new System.IntPtr(3))));
            Object.DestroyImmediate(go);
        }

        [Test]
        public void NotifyPeerConnected_MultipleBehaviours_AllReceive()
        {
            var go = new GameObject("Multi");
            var netObj = go.AddComponent<NetworkObject>();
            go.AddComponent<TrackingBehaviour>();
            go.AddComponent<TrackingBehaviour>();
            netObj.NotifyNetworkSpawn();

            var fakePeer = new ProductUserId(new System.IntPtr(4));
            netObj.NotifyPeerConnected(fakePeer);

            var behaviours = go.GetComponents<TrackingBehaviour>();
            Assert.AreEqual(1, behaviours[0].PeerConnectedCount);
            Assert.AreEqual(1, behaviours[1].PeerConnectedCount);

            Object.DestroyImmediate(go);
        }

        [Test]
        public void NotifyPeerDisconnected_MultipleBehaviours_AllReceive()
        {
            var go = new GameObject("Multi");
            var netObj = go.AddComponent<NetworkObject>();
            go.AddComponent<TrackingBehaviour>();
            go.AddComponent<TrackingBehaviour>();
            netObj.NotifyNetworkSpawn();

            var fakePeer = new ProductUserId(new System.IntPtr(5));
            netObj.NotifyPeerDisconnected(fakePeer);

            var behaviours = go.GetComponents<TrackingBehaviour>();
            Assert.AreEqual(1, behaviours[0].PeerDisconnectedCount);
            Assert.AreEqual(1, behaviours[1].PeerDisconnectedCount);

            Object.DestroyImmediate(go);
        }

        #endregion

        #region Default Hook Implementations (no-op)

        [Test]
        public void DefaultHooks_AreNoOp()
        {
            // Ensure base class virtual methods don't throw
            var go = new GameObject("Base");
            var netObj = go.AddComponent<NetworkObject>();
            // Use a minimal NB subclass that doesn't override anything
            var nb = go.AddComponent<EmptyBehaviour>();

            Assert.DoesNotThrow(() => nb.OnTick(1));
            Assert.DoesNotThrow(() => nb.OnPeerConnected(null));
            Assert.DoesNotThrow(() => nb.OnPeerDisconnected(null));

            var writer = new NetWriter();
            Assert.DoesNotThrow(() => nb.WriteSpawnData(writer));
            Assert.AreEqual(0, writer.ToArraySegment().Count,
                "Default WriteSpawnData should write nothing");

            Object.DestroyImmediate(go);
        }

        #endregion

        #region Spawn Payload Wire Format

        [Test]
        public void SpawnPayload_EmptyWriteSpawnData_ProducesLengthPrefixedZeroSection()
        {
            // Object with EmptyBehaviour (no-op WriteSpawnData)
            var go = new GameObject("EmptyNB");
            var netObj = go.AddComponent<NetworkObject>();
            go.AddComponent<EmptyBehaviour>();
            netObj.NotifyNetworkSpawn();

            var writer = new NetWriter();
            netObj.WriteSpawnPayload(writer);
            var data = writer.ToArraySegment();

            // Format: [sectionCount:1][len:2][data:0]
            // 1 section, each with u16 length = 0
            Assert.AreEqual(3, data.Count, "1 section with 0 data = 3 bytes (1 + 2)");
            Assert.AreEqual(1, data.Array[data.Offset], "Section count should be 1");
            // Length should be 0 (2 bytes little-endian)
            Assert.AreEqual(0, data.Array[data.Offset + 1]);
            Assert.AreEqual(0, data.Array[data.Offset + 2]);

            Object.DestroyImmediate(go);
        }

        #endregion
    }
}
