using System.Collections.Generic;
using System.Reflection;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.TestTools;
using EOSNative.P2P;
using EOSNative.Net;
using Epic.OnlineServices;

namespace EOSNative.Tests
{
    /// <summary>
    /// Tests for packet compression, decompression bomb defense, batch limits,
    /// and per-peer RPC rate limiting.
    /// </summary>
    [TestFixture]
    public class CompressionSecurityTests
    {
        #region Helpers

        /// <summary>Wrap data in a single-fragment header (7 bytes).</summary>
        private byte[] WrapInFragment(byte[] data)
        {
            var packet = new byte[7 + data.Length];
            packet[0] = 1; // packetId
            packet[6] = 1; // isLast = true
            System.Buffer.BlockCopy(data, 0, packet, 7, data.Length);
            return packet;
        }

        /// <summary>Build a single uncompressed message packet.</summary>
        private byte[] BuildSinglePacket(byte msgId, byte[] payload)
        {
            int totalInner = 1 + 1 + (payload?.Length ?? 0);
            var inner = new byte[totalInner];
            inner[0] = 0x00; // FLAG_SINGLE
            inner[1] = msgId;
            if (payload != null && payload.Length > 0)
                System.Buffer.BlockCopy(payload, 0, inner, 2, payload.Length);
            return WrapInFragment(inner);
        }

        /// <summary>Build a single compressed message packet.</summary>
        private byte[] BuildCompressedSinglePacket(byte msgId, byte[] payload)
        {
            // Content to compress: [msgId][payload...]
            var content = new byte[1 + (payload?.Length ?? 0)];
            content[0] = msgId;
            if (payload != null && payload.Length > 0)
                System.Buffer.BlockCopy(payload, 0, content, 1, payload.Length);

            var compressed = MessageRouter.CompressDeflate(content, 0, content.Length);

            // [FLAG_SINGLE_COMPRESSED][compressed data]
            var inner = new byte[1 + compressed.Length];
            inner[0] = 0x02; // FLAG_SINGLE_COMPRESSED
            System.Buffer.BlockCopy(compressed, 0, inner, 1, compressed.Length);

            return WrapInFragment(inner);
        }

        /// <summary>Build a batch packet.</summary>
        private byte[] BuildBatchPacket(List<(byte msgId, byte[] payload)> messages)
        {
            int bodySize = 1 + 2;
            foreach (var msg in messages)
                bodySize += 2 + 1 + (msg.payload?.Length ?? 0);

            var inner = new byte[bodySize];
            inner[0] = 0x01; // FLAG_BATCH
            inner[1] = (byte)(messages.Count & 0xFF);
            inner[2] = (byte)((messages.Count >> 8) & 0xFF);

            int offset = 3;
            foreach (var msg in messages)
            {
                int msgLen = 1 + (msg.payload?.Length ?? 0);
                inner[offset] = (byte)(msgLen & 0xFF);
                inner[offset + 1] = (byte)((msgLen >> 8) & 0xFF);
                inner[offset + 2] = msg.msgId;
                offset += 3;
                if (msg.payload != null && msg.payload.Length > 0)
                {
                    System.Buffer.BlockCopy(msg.payload, 0, inner, offset, msg.payload.Length);
                    offset += msg.payload.Length;
                }
            }

            return WrapInFragment(inner);
        }

        /// <summary>Build a compressed batch packet.</summary>
        private byte[] BuildCompressedBatchPacket(List<(byte msgId, byte[] payload)> messages)
        {
            // Build batch content: [count:u16][len:u16][msgId:u8][payload]...
            int contentSize = 2;
            foreach (var msg in messages)
                contentSize += 2 + 1 + (msg.payload?.Length ?? 0);

            var content = new byte[contentSize];
            content[0] = (byte)(messages.Count & 0xFF);
            content[1] = (byte)((messages.Count >> 8) & 0xFF);

            int offset = 2;
            foreach (var msg in messages)
            {
                int msgLen = 1 + (msg.payload?.Length ?? 0);
                content[offset] = (byte)(msgLen & 0xFF);
                content[offset + 1] = (byte)((msgLen >> 8) & 0xFF);
                content[offset + 2] = msg.msgId;
                offset += 3;
                if (msg.payload != null && msg.payload.Length > 0)
                {
                    System.Buffer.BlockCopy(msg.payload, 0, content, offset, msg.payload.Length);
                    offset += msg.payload.Length;
                }
            }

            var compressed = MessageRouter.CompressDeflate(content, 0, content.Length);

            var inner = new byte[1 + compressed.Length];
            inner[0] = 0x03; // FLAG_BATCH_COMPRESSED
            System.Buffer.BlockCopy(compressed, 0, inner, 1, compressed.Length);

            return WrapInFragment(inner);
        }

        #endregion

        #region Compression Round-Trip (CompressDeflate / DecompressDeflate)

        [Test]
        public void CompressDeflate_RoundTrip_SmallData()
        {
            var original = new byte[] { 1, 2, 3, 4, 5, 6, 7, 8 };
            var compressed = MessageRouter.CompressDeflate(original, 0, original.Length);
            var decompressed = MessageRouter.DecompressDeflate(compressed, 0, compressed.Length);

            Assert.AreEqual(original, decompressed);
        }

        [Test]
        public void CompressDeflate_RoundTrip_LargeRepetitiveData()
        {
            // Repetitive data should compress well
            var original = new byte[1024];
            for (int i = 0; i < original.Length; i++)
                original[i] = (byte)(i % 4);

            var compressed = MessageRouter.CompressDeflate(original, 0, original.Length);
            Assert.Less(compressed.Length, original.Length, "Repetitive data should compress smaller");

            var decompressed = MessageRouter.DecompressDeflate(compressed, 0, compressed.Length);
            Assert.AreEqual(original, decompressed);
        }

        [Test]
        public void CompressDeflate_RoundTrip_EmptyData()
        {
            var original = System.Array.Empty<byte>();
            var compressed = MessageRouter.CompressDeflate(original, 0, 0);
            var decompressed = MessageRouter.DecompressDeflate(compressed, 0, compressed.Length);

            Assert.AreEqual(0, decompressed.Length);
        }

        [Test]
        public void CompressDeflate_WithOffset_CompressesSubset()
        {
            var data = new byte[] { 0xFF, 0xFF, 1, 2, 3, 4, 5, 0xFF, 0xFF };
            // Compress only bytes [2..6] (indices 2,3,4,5,6)
            var compressed = MessageRouter.CompressDeflate(data, 2, 5);
            var decompressed = MessageRouter.DecompressDeflate(compressed, 0, compressed.Length);

            Assert.AreEqual(new byte[] { 1, 2, 3, 4, 5 }, decompressed);
        }

        [Test]
        public void CompressDeflate_RoundTrip_RandomData()
        {
            // Random data may not compress but should round-trip correctly
            var rng = new System.Random(42);
            var original = new byte[256];
            rng.NextBytes(original);

            var compressed = MessageRouter.CompressDeflate(original, 0, original.Length);
            var decompressed = MessageRouter.DecompressDeflate(compressed, 0, compressed.Length);

            Assert.AreEqual(original, decompressed);
        }

        #endregion

        #region Decompression Bomb Defense

        [Test]
        public void DecompressDeflate_MaxDecompressedSize_Is1MB()
        {
            Assert.AreEqual(1048576, MessageRouter.MaxDecompressedSize);
        }

        [Test]
        public void DecompressDeflate_LargeValidData_Works()
        {
            // Just under the limit — should succeed
            var original = new byte[500000]; // 500 KB
            for (int i = 0; i < original.Length; i++)
                original[i] = (byte)(i % 256);

            var compressed = MessageRouter.CompressDeflate(original, 0, original.Length);
            var decompressed = MessageRouter.DecompressDeflate(compressed, 0, compressed.Length);

            Assert.AreEqual(original.Length, decompressed.Length);
        }

        [Test]
        public void DecompressDeflate_BombExceedingLimit_Throws()
        {
            // Create data that exceeds MaxDecompressedSize
            // Use highly repetitive data (zeros) that compresses very small but decompresses huge
            var hugeData = new byte[MessageRouter.MaxDecompressedSize + 1];
            var compressed = CompressWithoutLimit(hugeData);

            Assert.Throws<System.InvalidOperationException>(() =>
                MessageRouter.DecompressDeflate(compressed, 0, compressed.Length));
        }

        /// <summary>
        /// Compress data without size checking (for creating bomb test data).
        /// Uses the same Deflate algorithm as MessageRouter.
        /// </summary>
        private byte[] CompressWithoutLimit(byte[] data)
        {
            using var ms = new System.IO.MemoryStream();
            using (var deflate = new System.IO.Compression.DeflateStream(ms,
                System.IO.Compression.CompressionLevel.Fastest, true))
                deflate.Write(data, 0, data.Length);
            return ms.ToArray();
        }

        #endregion

        #region Compressed Single Message Dispatch

        [Test]
        public void CompressedSingle_Dispatches()
        {
            var router = new MessageRouter(null);
            byte[] receivedPayload = null;

            router.Register(0x42, (sender, reader) =>
            {
                receivedPayload = new byte[reader.Remaining];
                for (int i = 0; i < receivedPayload.Length; i++)
                    receivedPayload[i] = reader.ReadByte();
            });

            var packet = BuildCompressedSinglePacket(0x42, new byte[] { 10, 20, 30 });
            router.ProcessIncoming(null, 0, packet);

            Assert.IsNotNull(receivedPayload);
            Assert.AreEqual(new byte[] { 10, 20, 30 }, receivedPayload);
        }

        [Test]
        public void CompressedSingle_EmptyPayload()
        {
            var router = new MessageRouter(null);
            int remaining = -1;

            router.Register(0x42, (sender, reader) =>
            {
                remaining = reader.Remaining;
            });

            var packet = BuildCompressedSinglePacket(0x42, System.Array.Empty<byte>());
            router.ProcessIncoming(null, 0, packet);

            Assert.AreEqual(0, remaining);
        }

        [Test]
        public void CompressedSingle_LargePayload()
        {
            var router = new MessageRouter(null);
            byte[] receivedPayload = null;

            router.Register(0x50, (sender, reader) =>
            {
                receivedPayload = new byte[reader.Remaining];
                for (int i = 0; i < receivedPayload.Length; i++)
                    receivedPayload[i] = reader.ReadByte();
            });

            var payload = new byte[500];
            for (int i = 0; i < payload.Length; i++)
                payload[i] = (byte)(i % 256);

            var packet = BuildCompressedSinglePacket(0x50, payload);
            router.ProcessIncoming(null, 0, packet);

            Assert.IsNotNull(receivedPayload);
            Assert.AreEqual(payload, receivedPayload);
        }

        #endregion

        #region Compressed Batch Message Dispatch

        [Test]
        public void CompressedBatch_AllHandlersCalled()
        {
            var router = new MessageRouter(null);
            var received = new List<byte>();

            router.Register(0x10, (sender, reader) => received.Add(0x10));
            router.Register(0x20, (sender, reader) => received.Add(0x20));

            var messages = new List<(byte, byte[])>
            {
                (0x10, new byte[] { 0xAA }),
                (0x20, new byte[] { 0xBB, 0xCC }),
            };

            var packet = BuildCompressedBatchPacket(messages);
            router.ProcessIncoming(null, 0, packet);

            Assert.AreEqual(2, received.Count);
            Assert.AreEqual(0x10, received[0]);
            Assert.AreEqual(0x20, received[1]);
        }

        [Test]
        public void CompressedBatch_PayloadsCorrect()
        {
            var router = new MessageRouter(null);
            var payloads = new Dictionary<byte, byte[]>();

            router.Register(0x10, (sender, reader) =>
            {
                var data = new byte[reader.Remaining];
                for (int i = 0; i < data.Length; i++) data[i] = reader.ReadByte();
                payloads[0x10] = data;
            });
            router.Register(0x20, (sender, reader) =>
            {
                var data = new byte[reader.Remaining];
                for (int i = 0; i < data.Length; i++) data[i] = reader.ReadByte();
                payloads[0x20] = data;
            });

            var messages = new List<(byte, byte[])>
            {
                (0x10, new byte[] { 1, 2, 3 }),
                (0x20, new byte[] { 4, 5 }),
            };

            var packet = BuildCompressedBatchPacket(messages);
            router.ProcessIncoming(null, 0, packet);

            Assert.AreEqual(2, payloads.Count);
            Assert.AreEqual(new byte[] { 1, 2, 3 }, payloads[0x10]);
            Assert.AreEqual(new byte[] { 4, 5 }, payloads[0x20]);
        }

        #endregion

        #region Batch Limits

        [Test]
        public void MaxBatchCount_Is256()
        {
            Assert.AreEqual(256, MessageRouter.MaxBatchCount);
        }

        [Test]
        public void BatchCount_AtLimit_Dispatches()
        {
            // 256 messages should be accepted
            var router = new MessageRouter(null);
            int callCount = 0;
            router.Register(0x10, (sender, reader) => callCount++);

            var messages = new List<(byte, byte[])>();
            for (int i = 0; i < 256; i++)
                messages.Add((0x10, new byte[] { (byte)i }));

            var packet = BuildBatchPacket(messages);
            router.ProcessIncoming(null, 0, packet);

            Assert.AreEqual(256, callCount, "Exactly 256 messages should be dispatched");
        }

        [Test]
        public void BatchCount_OverLimit_Rejected()
        {
            // Build a raw batch with count > 256 (craft manually since BuildBatchPacket builds valid ones)
            var router = new MessageRouter(null);
            int callCount = 0;
            router.Register(0x10, (sender, reader) => callCount++);

            // Craft inner: [FLAG_BATCH][count=257:u16][padding...]
            var inner = new byte[4];
            inner[0] = 0x01; // FLAG_BATCH
            inner[1] = 0x01; // count low byte = 1
            inner[2] = 0x01; // count high byte = 1 → total = 257
            inner[3] = 0x00; // padding (incomplete but doesn't matter — rejected at count check)

            var packet = WrapInFragment(inner);
            router.ProcessIncoming(null, 0, packet);

            Assert.AreEqual(0, callCount, "Oversized batch (count > 256) should be rejected");
        }

        #endregion

        #region Malformed Batch Data

        [Test]
        public void Batch_TruncatedMessageLength_DoesNotCrash()
        {
            var router = new MessageRouter(null);
            int callCount = 0;
            router.Register(0x10, (sender, reader) => callCount++);

            // Craft batch with count=1 but truncated data (not enough for msgLen)
            var inner = new byte[4]; // FLAG_BATCH + count + only 1 byte (need 3 for first message)
            inner[0] = 0x01; // FLAG_BATCH
            inner[1] = 0x01; // count = 1
            inner[2] = 0x00;
            inner[3] = 0x10; // partial data — not enough for len:u16 + msgId

            var packet = WrapInFragment(inner);
            Assert.DoesNotThrow(() => router.ProcessIncoming(null, 0, packet));
            // Truncated batch entries should be silently skipped
        }

        [Test]
        public void Batch_MessageLengthExceedsBounds_DoesNotCrash()
        {
            var router = new MessageRouter(null);
            int callCount = 0;
            router.Register(0x10, (sender, reader) => callCount++);

            // Craft batch with count=1, message length larger than remaining data
            var inner = new byte[6];
            inner[0] = 0x01; // FLAG_BATCH
            inner[1] = 0x01; inner[2] = 0x00; // count = 1
            inner[3] = 0xFF; inner[4] = 0x00; // msgLen = 255 (way more than available)
            inner[5] = 0x10; // msgId

            var packet = WrapInFragment(inner);
            Assert.DoesNotThrow(() => router.ProcessIncoming(null, 0, packet));
        }

        [Test]
        public void InvalidFlag_Ignored()
        {
            var router = new MessageRouter(null);
            int callCount = 0;
            router.Register(0x10, (sender, reader) => callCount++);

            // Flag byte 0xFF is not a known flag
            var inner = new byte[] { 0xFF, 0x10, 0xAA };
            var packet = WrapInFragment(inner);
            router.ProcessIncoming(null, 0, packet);

            Assert.AreEqual(0, callCount, "Unknown flag byte should result in no dispatch");
        }

        #endregion

        #region Rate Limiting

        [Test]
        public void MaxMessagesPerPeerPerSecond_Default0_Unlimited()
        {
            var go = new GameObject("NM");
            go.SetActive(false);
            var nm = go.AddComponent<NetworkManager>();
            Assert.AreEqual(0, nm.MaxMessagesPerPeerPerSecond);
            Object.DestroyImmediate(go);
        }

        [Test]
        public void MaxMessagesPerPeerPerSecond_IsConfigurable()
        {
            var go = new GameObject("NM");
            go.SetActive(false);
            var nm = go.AddComponent<NetworkManager>();
            nm.MaxMessagesPerPeerPerSecond = 100;
            Assert.AreEqual(100, nm.MaxMessagesPerPeerPerSecond);
            Object.DestroyImmediate(go);
        }

        [Test]
        public void IsRateLimited_Disabled_ReturnsFalse()
        {
            var go = new GameObject("NM");
            go.SetActive(false);
            var nm = go.AddComponent<NetworkManager>();
            nm.MaxMessagesPerPeerPerSecond = 0; // disabled

            var method = typeof(NetworkManager)
                .GetMethod("IsRateLimited", BindingFlags.NonPublic | BindingFlags.Instance);
            Assert.IsNotNull(method, "IsRateLimited method should exist");

            var result = (bool)method.Invoke(nm, new object[] { new ProductUserId(new System.IntPtr(1)) });
            Assert.IsFalse(result, "With rate limit disabled, should never be rate limited");

            Object.DestroyImmediate(go);
        }

        [Test]
        public void IsRateLimited_ExceedsLimit_ReturnsTrue()
        {
            var go = new GameObject("NM");
            go.SetActive(false);
            var nm = go.AddComponent<NetworkManager>();
            nm.MaxMessagesPerPeerPerSecond = 3;

            var method = typeof(NetworkManager)
                .GetMethod("IsRateLimited", BindingFlags.NonPublic | BindingFlags.Instance);
            Assert.IsNotNull(method);

            var peer = new ProductUserId(new System.IntPtr(42));

            // First 3 calls should not be limited
            Assert.IsFalse((bool)method.Invoke(nm, new object[] { peer }));
            Assert.IsFalse((bool)method.Invoke(nm, new object[] { peer }));
            Assert.IsFalse((bool)method.Invoke(nm, new object[] { peer }));

            // 4th call should be rate limited
            Assert.IsTrue((bool)method.Invoke(nm, new object[] { peer }));

            Object.DestroyImmediate(go);
        }

        [Test]
        public void IsRateLimited_DifferentPeers_IndependentCounters()
        {
            var go = new GameObject("NM");
            go.SetActive(false);
            var nm = go.AddComponent<NetworkManager>();
            nm.MaxMessagesPerPeerPerSecond = 2;

            var method = typeof(NetworkManager)
                .GetMethod("IsRateLimited", BindingFlags.NonPublic | BindingFlags.Instance);

            var peer1 = new ProductUserId(new System.IntPtr(1));
            var peer2 = new ProductUserId(new System.IntPtr(2));

            // Exhaust peer1's budget
            Assert.IsFalse((bool)method.Invoke(nm, new object[] { peer1 }));
            Assert.IsFalse((bool)method.Invoke(nm, new object[] { peer1 }));
            Assert.IsTrue((bool)method.Invoke(nm, new object[] { peer1 }), "peer1 should be rate limited");

            // peer2 should still have budget
            Assert.IsFalse((bool)method.Invoke(nm, new object[] { peer2 }), "peer2 should NOT be rate limited");

            Object.DestroyImmediate(go);
        }

        #endregion

        #region Compression Properties

        [Test]
        public void CompressionEnabled_CanBeToggled()
        {
            var router = new MessageRouter(null);
            Assert.IsFalse(router.CompressionEnabled);
            router.CompressionEnabled = true;
            Assert.IsTrue(router.CompressionEnabled);
        }

        [Test]
        public void CompressionThreshold_IsConfigurable()
        {
            var router = new MessageRouter(null);
            Assert.AreEqual(64, router.CompressionThreshold);
            router.CompressionThreshold = 128;
            Assert.AreEqual(128, router.CompressionThreshold);
        }

        #endregion
    }
}
