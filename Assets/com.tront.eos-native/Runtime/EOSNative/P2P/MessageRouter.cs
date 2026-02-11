using System;
using System.Collections.Generic;
using System.IO;
using System.IO.Compression;
using Epic.OnlineServices;
using Epic.OnlineServices.P2P;
using EOSNative.Logging;

namespace EOSNative.P2P
{
    /// <summary>
    /// Message registration, dispatch, and frame batching for P2P communication.
    /// Register handlers by message ID, send typed messages, and optionally batch
    /// multiple messages per frame into single P2P packets.
    /// </summary>
    public class MessageRouter
    {
        /// <summary>Wire format flag: single message.</summary>
        private const byte FLAG_SINGLE = 0x00;

        /// <summary>Wire format flag: batched messages.</summary>
        private const byte FLAG_BATCH = 0x01;

        /// <summary>Wire format flag: compressed single message.</summary>
        private const byte FLAG_SINGLE_COMPRESSED = 0x02;

        /// <summary>Wire format flag: compressed batched messages.</summary>
        private const byte FLAG_BATCH_COMPRESSED = 0x03;

        private readonly EOSP2PManager _p2p;
        private readonly PacketFragmenter _fragmenter = new();
        private readonly Dictionary<byte, Action<ProductUserId, NetReader>> _handlers = new();
        private readonly Dictionary<BatchKey, BatchQueue> _batchQueues = new();
        private readonly List<ArraySegment<byte>> _fragmentBuffer = new();
        private readonly NetReader _readerCache;

        /// <summary>Enable/disable batching. When disabled, all sends are immediate.</summary>
        public bool BatchingEnabled { get; set; } = true;

        /// <summary>Enable/disable Deflate compression for payloads above threshold. Default off.</summary>
        public bool CompressionEnabled { get; set; }

        /// <summary>Minimum payload size in bytes before compression is applied. Default 64.</summary>
        public int CompressionThreshold { get; set; } = 64;

        /// <summary>Stale timeout for incomplete fragment reassembly. Default 5s. Lower for real-time streams.</summary>
        public float FragmentStaleTimeout
        {
            get => _fragmenter.StaleTimeout;
            set => _fragmenter.StaleTimeout = value;
        }

        /// <summary>Access fragment-level diagnostics.</summary>
        public PacketFragmenter Fragmenter => _fragmenter;

        /// <summary>Number of registered message handlers.</summary>
        public int RegisteredHandlerCount => _handlers.Count;

        /// <summary>Check if a specific message ID has a handler.</summary>
        public bool HasHandler(byte msgId) => _handlers.ContainsKey(msgId);

        /// <summary>Total messages dispatched to handlers.</summary>
        public int MessagesDispatched { get; private set; }

        /// <summary>Total messages with no registered handler (silently dropped).</summary>
        public int MessagesUnhandled { get; private set; }

        public MessageRouter(EOSP2PManager p2p)
        {
            _p2p = p2p;
            _readerCache = new NetReader(Array.Empty<byte>());
        }

        #region Registration

        /// <summary>Register a handler for a message ID.</summary>
        public void Register(byte msgId, Action<ProductUserId, NetReader> handler)
        {
            _handlers[msgId] = handler;
        }

        /// <summary>Unregister a handler for a message ID.</summary>
        public void Unregister(byte msgId)
        {
            _handlers.Remove(msgId);
        }

        #endregion

        #region Sending

        /// <summary>Send a message to all peers. Queued for batching if enabled.</summary>
        public void SendToAll(byte msgId, NetWriter writer, PacketReliability reliability, byte channel = 0)
        {
            if (BatchingEnabled)
            {
                QueueMessage(msgId, writer, null, reliability, channel);
            }
            else
            {
                SendToAllImmediate(msgId, writer, reliability, channel);
            }
        }

        /// <summary>Send a message to a specific peer. Queued for batching if enabled.</summary>
        public void SendToPeer(byte msgId, NetWriter writer, ProductUserId peer, PacketReliability reliability, byte channel = 0)
        {
            if (BatchingEnabled)
            {
                QueueMessage(msgId, writer, peer, reliability, channel);
            }
            else
            {
                SendToPeerImmediate(msgId, writer, peer, reliability, channel);
            }
        }

        /// <summary>Send a message to all peers immediately (bypass batching).</summary>
        public void SendToAllImmediate(byte msgId, NetWriter writer, PacketReliability reliability, byte channel = 0)
        {
            var packet = BuildSinglePacket(msgId, writer);
            SendFragmented(packet, null, reliability, channel);
        }

        /// <summary>Send a message to a specific peer immediately (bypass batching).</summary>
        public void SendToPeerImmediate(byte msgId, NetWriter writer, ProductUserId peer, PacketReliability reliability, byte channel = 0)
        {
            var packet = BuildSinglePacket(msgId, writer);
            SendFragmented(packet, peer, reliability, channel);
        }

        #endregion

        #region Receiving

        /// <summary>
        /// Process an incoming raw P2P packet. Subscribe this to EOSP2PManager.OnPacketReceived.
        /// Handles defragmentation, unbatching, and handler dispatch.
        /// </summary>
        public void ProcessIncoming(ProductUserId sender, byte channel, byte[] data)
        {
            if (data == null || data.Length < PacketFragmenter.HeaderSize + 1) return;

            // Defragment
            var segment = new ArraySegment<byte>(data);
            byte[] reassembled = _fragmenter.ProcessIncoming(sender, segment, channel);
            if (reassembled == null) return; // waiting for more fragments

            if (reassembled.Length < 1) return;

            byte flag = reassembled[0];

            if (flag == FLAG_SINGLE)
            {
                // [FLAG_SINGLE] [msgId] [payload...]
                if (reassembled.Length < 2) return;
                byte msgId = reassembled[1];
                DispatchMessage(sender, msgId, reassembled, 2, reassembled.Length - 2);
            }
            else if (flag == FLAG_SINGLE_COMPRESSED)
            {
                // [FLAG_SINGLE_COMPRESSED] [compressed: msgId + payload]
                if (reassembled.Length < 2) return;
                var decompressed = DecompressDeflate(reassembled, 1, reassembled.Length - 1);
                if (decompressed.Length < 1) return;
                byte msgId = decompressed[0];
                DispatchMessage(sender, msgId, decompressed, 1, decompressed.Length - 1);
            }
            else if (flag == FLAG_BATCH)
            {
                // [FLAG_BATCH] [count:u16] [len:u16][msgId:u8][payload] ...
                if (reassembled.Length < 3) return;
                DispatchBatch(sender, reassembled, 1, reassembled.Length - 1);
            }
            else if (flag == FLAG_BATCH_COMPRESSED)
            {
                // [FLAG_BATCH_COMPRESSED] [compressed: count + messages]
                if (reassembled.Length < 2) return;
                var decompressed = DecompressDeflate(reassembled, 1, reassembled.Length - 1);
                if (decompressed.Length < 2) return;
                DispatchBatch(sender, decompressed, 0, decompressed.Length);
            }
        }

        private void DispatchMessage(ProductUserId sender, byte msgId, byte[] data, int offset, int count)
        {
            if (!_handlers.TryGetValue(msgId, out var handler))
            {
                MessagesUnhandled++;
                return;
            }

            MessagesDispatched++;
            _readerCache.SetBuffer(data, offset, count);
            try
            {
                handler(sender, _readerCache);
            }
            catch (Exception ex)
            {
                EOSDebugLogger.LogError("MessageRouter",
                    $"Handler for msgId 0x{msgId:X2} threw: {ex.Message}");
            }
        }

        /// <summary>Maximum messages per batch packet. Prevents flood via crafted batch.</summary>
        public const int MaxBatchCount = 256;

        private void DispatchBatch(ProductUserId sender, byte[] data, int start, int length)
        {
            if (length < 2) return;
            ushort count = (ushort)(data[start] | (data[start + 1] << 8));
            if (count > MaxBatchCount) return; // reject oversized batch
            int offset = start + 2;
            int end = start + length;

            for (int i = 0; i < count; i++)
            {
                if (offset + 3 > end) break; // malformed

                ushort msgLen = (ushort)(data[offset] | (data[offset + 1] << 8));
                byte msgId = data[offset + 2];
                offset += 3;

                int payloadLen = msgLen - 1; // msgLen includes the msgId byte
                if (payloadLen < 0 || offset + payloadLen > end) break;

                DispatchMessage(sender, msgId, data, offset, payloadLen);
                offset += payloadLen;
            }
        }

        #endregion

        #region Batching

        private struct BatchKey : IEquatable<BatchKey>
        {
            public byte Channel;
            public PacketReliability Reliability;
            public ProductUserId Target; // null = broadcast

            public bool Equals(BatchKey other)
            {
                return Channel == other.Channel && Reliability == other.Reliability && Target == other.Target;
            }

            public override bool Equals(object obj) => obj is BatchKey other && Equals(other);

            public override int GetHashCode()
            {
                unchecked
                {
                    int hash = Channel.GetHashCode();
                    hash = (hash * 397) ^ Reliability.GetHashCode();
                    if (Target != null) hash = (hash * 397) ^ Target.GetHashCode();
                    return hash;
                }
            }
        }

        private class BatchQueue
        {
            public readonly List<QueuedMessage> Messages = new();
            public int TotalPayloadSize;
        }

        private struct QueuedMessage
        {
            public byte MsgId;
            public byte[] Payload;
            public int PayloadLength;
        }

        private void QueueMessage(byte msgId, NetWriter writer, ProductUserId target, PacketReliability reliability, byte channel)
        {
            var key = new BatchKey { Channel = channel, Reliability = reliability, Target = target };

            if (!_batchQueues.TryGetValue(key, out var queue))
            {
                queue = new BatchQueue();
                _batchQueues[key] = queue;
            }

            var segment = writer.ToArraySegment();
            var payload = new byte[segment.Count];
            if (segment.Count > 0)
                Buffer.BlockCopy(segment.Array, segment.Offset, payload, 0, segment.Count);

            queue.Messages.Add(new QueuedMessage { MsgId = msgId, Payload = payload, PayloadLength = segment.Count });
            queue.TotalPayloadSize += segment.Count + 3; // +3 for len(2) + msgId(1) in batch format
        }

        /// <summary>Flush all queued messages. Called from EOSP2PManager.LateUpdate().</summary>
        public void Flush()
        {
            foreach (var kvp in _batchQueues)
            {
                var key = kvp.Key;
                var queue = kvp.Value;

                if (queue.Messages.Count == 0) continue;

                ArraySegment<byte> packet;

                if (queue.Messages.Count == 1)
                {
                    // Single message — use single format (more efficient)
                    var msg = queue.Messages[0];
                    packet = BuildSinglePacket(msg.MsgId, msg.Payload, msg.PayloadLength);
                }
                else
                {
                    // Multiple messages — use batch format
                    packet = BuildBatchPacket(queue.Messages);
                }

                SendFragmented(packet, key.Target, key.Reliability, key.Channel);

                queue.Messages.Clear();
                queue.TotalPayloadSize = 0;
            }
        }

        #endregion

        #region Packet Building

        private ArraySegment<byte> BuildSinglePacket(byte msgId, NetWriter writer)
        {
            var segment = writer.ToArraySegment();
            return BuildSinglePacket(msgId, segment.Array, segment.Count, segment.Offset);
        }

        private ArraySegment<byte> BuildSinglePacket(byte msgId, byte[] payload, int length, int sourceOffset = 0)
        {
            int contentLen = 1 + length; // msgId + payload
            if (CompressionEnabled && contentLen > CompressionThreshold)
            {
                // Build uncompressed content: [msgId][payload...]
                var content = new byte[contentLen];
                content[0] = msgId;
                if (length > 0)
                    Buffer.BlockCopy(payload, sourceOffset, content, 1, length);

                var compressed = CompressDeflate(content, 0, contentLen);
                if (compressed.Length < contentLen)
                {
                    // [FLAG_SINGLE_COMPRESSED] [compressed data...]
                    var packet = new byte[1 + compressed.Length];
                    packet[0] = FLAG_SINGLE_COMPRESSED;
                    Buffer.BlockCopy(compressed, 0, packet, 1, compressed.Length);
                    return new ArraySegment<byte>(packet);
                }
            }

            // [FLAG_SINGLE] [msgId] [payload...]
            var pkt = new byte[2 + length];
            pkt[0] = FLAG_SINGLE;
            pkt[1] = msgId;
            if (length > 0)
                Buffer.BlockCopy(payload, sourceOffset, pkt, 2, length);
            return new ArraySegment<byte>(pkt);
        }

        private ArraySegment<byte> BuildBatchPacket(List<QueuedMessage> messages)
        {
            // Calculate total size of inner content: [count(2)] + per message [len(2)][msgId(1)][payload]
            int contentSize = 2;
            for (int i = 0; i < messages.Count; i++)
                contentSize += 3 + messages[i].PayloadLength;

            var content = new byte[contentSize];
            content[0] = (byte)messages.Count;
            content[1] = (byte)(messages.Count >> 8);

            int offset = 2;
            for (int i = 0; i < messages.Count; i++)
            {
                var msg = messages[i];
                ushort msgLen = (ushort)(1 + msg.PayloadLength); // msgId + payload

                content[offset++] = (byte)msgLen;
                content[offset++] = (byte)(msgLen >> 8);
                content[offset++] = msg.MsgId;

                if (msg.PayloadLength > 0)
                {
                    Buffer.BlockCopy(msg.Payload, 0, content, offset, msg.PayloadLength);
                    offset += msg.PayloadLength;
                }
            }

            if (CompressionEnabled && contentSize > CompressionThreshold)
            {
                var compressed = CompressDeflate(content, 0, contentSize);
                if (compressed.Length < contentSize)
                {
                    // [FLAG_BATCH_COMPRESSED] [compressed content...]
                    var packet = new byte[1 + compressed.Length];
                    packet[0] = FLAG_BATCH_COMPRESSED;
                    Buffer.BlockCopy(compressed, 0, packet, 1, compressed.Length);
                    return new ArraySegment<byte>(packet);
                }
            }

            // Uncompressed: [FLAG_BATCH] [content...]
            var pkt = new byte[1 + contentSize];
            pkt[0] = FLAG_BATCH;
            Buffer.BlockCopy(content, 0, pkt, 1, contentSize);
            return new ArraySegment<byte>(pkt);
        }

        #endregion

        #region Fragmentation + Send

        private void SendFragmented(ArraySegment<byte> data, ProductUserId target, PacketReliability reliability, byte channel)
        {
            _fragmenter.Fragment(data, _fragmentBuffer);
            foreach (var frag in _fragmentBuffer)
            {
                var fragArray = ToArray(frag);
                if (target == null)
                    _p2p.SendToAll(channel, fragArray, reliability);
                else
                    _p2p.SendToPeer(target, channel, fragArray, reliability);
            }
        }

        private static byte[] ToArray(ArraySegment<byte> segment)
        {
            if (segment.Offset == 0 && segment.Count == segment.Array.Length)
                return segment.Array;

            var result = new byte[segment.Count];
            Buffer.BlockCopy(segment.Array, segment.Offset, result, 0, segment.Count);
            return result;
        }

        #endregion

        #region Lifecycle

        /// <summary>Clean up fragment state when a peer disconnects.</summary>
        public void OnPeerDisconnected(ProductUserId peer)
        {
            _fragmenter.ClearPendingForSender(peer);
        }

        /// <summary>Clear all state (pending fragments, batch queues, handlers).</summary>
        public void ClearAll()
        {
            _fragmenter.ClearAll();
            foreach (var queue in _batchQueues.Values)
            {
                queue.Messages.Clear();
                queue.TotalPayloadSize = 0;
            }
        }

        #endregion

        #region Compression

        /// <summary>Maximum decompressed output size (1 MB). Prevents compression bomb attacks.</summary>
        public const int MaxDecompressedSize = 1048576;

        internal static byte[] CompressDeflate(byte[] data, int offset, int count)
        {
            using var output = new MemoryStream();
            using (var deflate = new DeflateStream(output, CompressionLevel.Fastest, leaveOpen: true))
            {
                deflate.Write(data, offset, count);
            }
            return output.ToArray();
        }

        internal static byte[] DecompressDeflate(byte[] data, int offset, int count)
        {
            using var input = new MemoryStream(data, offset, count);
            using var deflate = new DeflateStream(input, CompressionMode.Decompress);
            using var output = new MemoryStream();

            var buffer = new byte[4096];
            int totalRead = 0;
            int bytesRead;
            while ((bytesRead = deflate.Read(buffer, 0, buffer.Length)) > 0)
            {
                totalRead += bytesRead;
                if (totalRead > MaxDecompressedSize)
                    throw new InvalidOperationException(
                        $"Decompressed data exceeds {MaxDecompressedSize} bytes — possible compression bomb");
                output.Write(buffer, 0, bytesRead);
            }
            return output.ToArray();
        }

        #endregion
    }
}
