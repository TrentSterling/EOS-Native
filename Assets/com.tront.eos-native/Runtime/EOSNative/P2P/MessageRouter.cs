using System;
using System.Collections.Generic;
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

        private readonly EOSP2PManager _p2p;
        private readonly PacketFragmenter _fragmenter = new();
        private readonly Dictionary<byte, Action<ProductUserId, NetReader>> _handlers = new();
        private readonly Dictionary<BatchKey, BatchQueue> _batchQueues = new();
        private readonly List<ArraySegment<byte>> _fragmentBuffer = new();
        private readonly NetReader _readerCache;

        /// <summary>Enable/disable batching. When disabled, all sends are immediate.</summary>
        public bool BatchingEnabled { get; set; } = true;

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
            else if (flag == FLAG_BATCH)
            {
                // [FLAG_BATCH] [count:u16] [len:u16][msgId:u8][payload] ...
                if (reassembled.Length < 3) return;
                ushort count = (ushort)(reassembled[1] | (reassembled[2] << 8));
                int offset = 3;

                for (int i = 0; i < count; i++)
                {
                    if (offset + 3 > reassembled.Length) break; // malformed

                    ushort msgLen = (ushort)(reassembled[offset] | (reassembled[offset + 1] << 8));
                    byte msgId = reassembled[offset + 2];
                    offset += 3;

                    int payloadLen = msgLen - 1; // msgLen includes the msgId byte
                    if (payloadLen < 0 || offset + payloadLen > reassembled.Length) break;

                    DispatchMessage(sender, msgId, reassembled, offset, payloadLen);
                    offset += payloadLen;
                }
            }
        }

        private void DispatchMessage(ProductUserId sender, byte msgId, byte[] data, int offset, int count)
        {
            if (!_handlers.TryGetValue(msgId, out var handler)) return;

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
            // [FLAG_SINGLE] [msgId] [payload...]
            var packet = new byte[2 + length];
            packet[0] = FLAG_SINGLE;
            packet[1] = msgId;
            if (length > 0)
                Buffer.BlockCopy(payload, sourceOffset, packet, 2, length);
            return new ArraySegment<byte>(packet);
        }

        private ArraySegment<byte> BuildBatchPacket(List<QueuedMessage> messages)
        {
            // Calculate total size: [FLAG_BATCH(1)] [count(2)] + per message [len(2)][msgId(1)][payload]
            int totalSize = 3;
            for (int i = 0; i < messages.Count; i++)
                totalSize += 3 + messages[i].PayloadLength;

            var packet = new byte[totalSize];
            packet[0] = FLAG_BATCH;
            packet[1] = (byte)messages.Count;
            packet[2] = (byte)(messages.Count >> 8);

            int offset = 3;
            for (int i = 0; i < messages.Count; i++)
            {
                var msg = messages[i];
                ushort msgLen = (ushort)(1 + msg.PayloadLength); // msgId + payload

                packet[offset++] = (byte)msgLen;
                packet[offset++] = (byte)(msgLen >> 8);
                packet[offset++] = msg.MsgId;

                if (msg.PayloadLength > 0)
                {
                    Buffer.BlockCopy(msg.Payload, 0, packet, offset, msg.PayloadLength);
                    offset += msg.PayloadLength;
                }
            }

            return new ArraySegment<byte>(packet);
        }

        #endregion

        #region Fragmentation + Send

        private void SendFragmented(ArraySegment<byte> data, ProductUserId target, PacketReliability reliability, byte channel)
        {
            if (!PacketFragmenter.NeedsFragmentation(data.Count))
            {
                // Wrap in single fragment (add header)
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
            else
            {
                // Fragment and send each piece
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
    }
}
