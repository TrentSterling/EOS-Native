using System;
using System.Collections.Generic;
using NUnit.Framework;
using EOSNative.P2P;

namespace EOSNative.Tests
{
    public class MessageRouterTests
    {
        // We test ProcessIncoming (dispatch) by crafting raw byte arrays
        // that match the MessageRouter wire format. This avoids needing
        // an EOSP2PManager or EOS SDK.

        #region Helpers

        // Build a single-message packet: [FragmentHeader][FLAG_SINGLE][msgId][payload...]
        // FragmentHeader for single-fragment: [seqHi][seqLo][0][1][0][0][0] = 7 bytes
        private byte[] BuildSinglePacket(byte msgId, byte[] payload)
        {
            // FLAG_SINGLE = 0x00
            int totalInner = 1 + 1 + (payload?.Length ?? 0); // flag + msgId + payload
            var inner = new byte[totalInner];
            inner[0] = 0x00; // FLAG_SINGLE
            inner[1] = msgId;
            if (payload != null && payload.Length > 0)
                Buffer.BlockCopy(payload, 0, inner, 2, payload.Length);

            return WrapInFragment(inner);
        }

        // Build a batch packet: [FragmentHeader][FLAG_BATCH][count:u16][len:u16][msgId:u8][payload]...
        private byte[] BuildBatchPacket(List<(byte msgId, byte[] payload)> messages)
        {
            // Calculate total size
            int bodySize = 1 + 2; // flag + count
            foreach (var msg in messages)
                bodySize += 2 + 1 + (msg.payload?.Length ?? 0); // len + msgId + payload

            var inner = new byte[bodySize];
            inner[0] = 0x01; // FLAG_BATCH
            inner[1] = (byte)(messages.Count & 0xFF);
            inner[2] = (byte)((messages.Count >> 8) & 0xFF);

            int offset = 3;
            foreach (var msg in messages)
            {
                int msgLen = 1 + (msg.payload?.Length ?? 0); // msgId + payload
                inner[offset] = (byte)(msgLen & 0xFF);
                inner[offset + 1] = (byte)((msgLen >> 8) & 0xFF);
                inner[offset + 2] = msg.msgId;
                offset += 3;
                if (msg.payload != null && msg.payload.Length > 0)
                {
                    Buffer.BlockCopy(msg.payload, 0, inner, offset, msg.payload.Length);
                    offset += msg.payload.Length;
                }
            }

            return WrapInFragment(inner);
        }

        // Wrap data in a single-fragment packet header
        // Fragment header: [seqHi:1][seqLo:1][fragmentIndex:1][fragmentCount:1][channelId:1][reserved:2] = 7 bytes
        private byte[] WrapInFragment(byte[] data)
        {
            var packet = new byte[7 + data.Length];
            packet[0] = 0; // seq high
            packet[1] = 1; // seq low
            packet[2] = 0; // fragment index
            packet[3] = 1; // fragment count (1 = single)
            packet[4] = 0; // channel
            packet[5] = 0; // reserved
            packet[6] = 0; // reserved
            Buffer.BlockCopy(data, 0, packet, 7, data.Length);
            return packet;
        }

        #endregion

        #region Registration

        [Test]
        public void Register_HandlerReceivesMessages()
        {
            var router = new MessageRouter(null);
            int callCount = 0;
            byte receivedMsgId = 0;

            router.Register(0x10, (sender, reader) =>
            {
                callCount++;
            });

            var packet = BuildSinglePacket(0x10, new byte[] { 0xAA, 0xBB });
            router.ProcessIncoming(null, 0, packet);

            Assert.AreEqual(1, callCount);
        }

        [Test]
        public void Unregister_HandlerNotCalled()
        {
            var router = new MessageRouter(null);
            int callCount = 0;

            router.Register(0x10, (sender, reader) => callCount++);
            router.Unregister(0x10);

            var packet = BuildSinglePacket(0x10, new byte[] { 0xAA });
            router.ProcessIncoming(null, 0, packet);

            Assert.AreEqual(0, callCount);
        }

        [Test]
        public void Register_OverwritesPrevious()
        {
            var router = new MessageRouter(null);
            int handler1Count = 0;
            int handler2Count = 0;

            router.Register(0x10, (sender, reader) => handler1Count++);
            router.Register(0x10, (sender, reader) => handler2Count++);

            var packet = BuildSinglePacket(0x10, new byte[] { 0xAA });
            router.ProcessIncoming(null, 0, packet);

            Assert.AreEqual(0, handler1Count);
            Assert.AreEqual(1, handler2Count);
        }

        [Test]
        public void UnregisteredMsgId_Ignored()
        {
            var router = new MessageRouter(null);
            int callCount = 0;

            router.Register(0x10, (sender, reader) => callCount++);

            // Send msgId 0x20 which has no handler
            var packet = BuildSinglePacket(0x20, new byte[] { 0xAA });
            router.ProcessIncoming(null, 0, packet);

            Assert.AreEqual(0, callCount);
        }

        #endregion

        #region Single Message Dispatch

        [Test]
        public void SingleMessage_PayloadDelivered()
        {
            var router = new MessageRouter(null);
            byte[] receivedPayload = null;

            router.Register(0x42, (sender, reader) =>
            {
                receivedPayload = new byte[reader.Remaining];
                for (int i = 0; i < receivedPayload.Length; i++)
                    receivedPayload[i] = reader.ReadByte();
            });

            var packet = BuildSinglePacket(0x42, new byte[] { 1, 2, 3, 4, 5 });
            router.ProcessIncoming(null, 0, packet);

            Assert.IsNotNull(receivedPayload);
            Assert.AreEqual(5, receivedPayload.Length);
            Assert.AreEqual(1, receivedPayload[0]);
            Assert.AreEqual(5, receivedPayload[4]);
        }

        [Test]
        public void SingleMessage_EmptyPayload()
        {
            var router = new MessageRouter(null);
            int remaining = -1;

            router.Register(0x42, (sender, reader) =>
            {
                remaining = reader.Remaining;
            });

            var packet = BuildSinglePacket(0x42, Array.Empty<byte>());
            router.ProcessIncoming(null, 0, packet);

            Assert.AreEqual(0, remaining);
        }

        #endregion

        #region Batch Message Dispatch

        [Test]
        public void BatchMessage_AllHandlersCalled()
        {
            var router = new MessageRouter(null);
            var received = new List<byte>();

            router.Register(0x10, (sender, reader) => received.Add(0x10));
            router.Register(0x20, (sender, reader) => received.Add(0x20));
            router.Register(0x30, (sender, reader) => received.Add(0x30));

            var messages = new List<(byte, byte[])>
            {
                (0x10, new byte[] { 0xAA }),
                (0x20, new byte[] { 0xBB, 0xCC }),
                (0x30, new byte[] { 0xDD }),
            };

            var packet = BuildBatchPacket(messages);
            router.ProcessIncoming(null, 0, packet);

            Assert.AreEqual(3, received.Count);
            Assert.AreEqual(0x10, received[0]);
            Assert.AreEqual(0x20, received[1]);
            Assert.AreEqual(0x30, received[2]);
        }

        [Test]
        public void BatchMessage_PayloadsCorrect()
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

            var packet = BuildBatchPacket(messages);
            router.ProcessIncoming(null, 0, packet);

            Assert.AreEqual(2, payloads.Count);
            Assert.AreEqual(new byte[] { 1, 2, 3 }, payloads[0x10]);
            Assert.AreEqual(new byte[] { 4, 5 }, payloads[0x20]);
        }

        [Test]
        public void BatchMessage_SingleItem()
        {
            var router = new MessageRouter(null);
            int callCount = 0;

            router.Register(0x10, (sender, reader) => callCount++);

            var messages = new List<(byte, byte[])>
            {
                (0x10, new byte[] { 0xAA }),
            };

            var packet = BuildBatchPacket(messages);
            router.ProcessIncoming(null, 0, packet);

            Assert.AreEqual(1, callCount);
        }

        [Test]
        public void BatchMessage_EmptyPayloads()
        {
            var router = new MessageRouter(null);
            var received = new List<byte>();

            router.Register(0x10, (sender, reader) => received.Add(0x10));
            router.Register(0x20, (sender, reader) => received.Add(0x20));

            var messages = new List<(byte, byte[])>
            {
                (0x10, Array.Empty<byte>()),
                (0x20, Array.Empty<byte>()),
            };

            var packet = BuildBatchPacket(messages);
            router.ProcessIncoming(null, 0, packet);

            Assert.AreEqual(2, received.Count);
        }

        #endregion

        #region Edge Cases

        [Test]
        public void NullData_Ignored()
        {
            var router = new MessageRouter(null);
            int callCount = 0;
            router.Register(0x10, (sender, reader) => callCount++);

            router.ProcessIncoming(null, 0, null);
            Assert.AreEqual(0, callCount);
        }

        [Test]
        public void EmptyData_Ignored()
        {
            var router = new MessageRouter(null);
            int callCount = 0;
            router.Register(0x10, (sender, reader) => callCount++);

            router.ProcessIncoming(null, 0, Array.Empty<byte>());
            Assert.AreEqual(0, callCount);
        }

        [Test]
        public void TooShortData_Ignored()
        {
            var router = new MessageRouter(null);
            int callCount = 0;
            router.Register(0x10, (sender, reader) => callCount++);

            // Less than FragmentHeader (7 bytes) + 1 byte for flag
            router.ProcessIncoming(null, 0, new byte[] { 0, 1, 2 });
            Assert.AreEqual(0, callCount);
        }

        [Test]
        public void HandlerException_DoesNotCrash()
        {
            var router = new MessageRouter(null);
            int handler2Count = 0;

            router.Register(0x10, (sender, reader) =>
            {
                throw new InvalidOperationException("Test exception");
            });

            // Should not throw — exceptions are caught internally
            var packet = BuildSinglePacket(0x10, new byte[] { 0xAA });
            Assert.DoesNotThrow(() => router.ProcessIncoming(null, 0, packet));
        }

        [Test]
        public void ClearAll_DoesNotThrow()
        {
            // ClearAll clears fragmentation state and batch queues
            var router = new MessageRouter(null);
            router.Register(0x10, (sender, reader) => { });
            Assert.DoesNotThrow(() => router.ClearAll());
        }

        #endregion

        #region Properties

        [Test]
        public void BatchingEnabled_DefaultTrue()
        {
            var router = new MessageRouter(null);
            Assert.IsTrue(router.BatchingEnabled);
        }

        [Test]
        public void CompressionEnabled_DefaultFalse()
        {
            var router = new MessageRouter(null);
            Assert.IsFalse(router.CompressionEnabled);
        }

        [Test]
        public void CompressionThreshold_Default64()
        {
            var router = new MessageRouter(null);
            Assert.AreEqual(64, router.CompressionThreshold);
        }

        #endregion
    }
}
