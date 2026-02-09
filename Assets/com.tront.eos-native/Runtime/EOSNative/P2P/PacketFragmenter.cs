using System;
using System.Collections.Generic;
using Epic.OnlineServices;

namespace EOSNative.P2P
{
    /// <summary>
    /// Splits large messages into EOS P2P-sized fragments (max 1170 bytes) and reassembles them.
    /// Single-fragment messages use a fast path that avoids allocation.
    /// Stale incomplete fragment sets are cleaned up after 5 seconds.
    /// </summary>
    public class PacketFragmenter
    {
        /// <summary>Fragment header: [packetId:u32] [fragmentIndex:u16] [lastFragment:u8]</summary>
        public const int HeaderSize = 7;

        /// <summary>Max payload per fragment (1170 - 7 header bytes).</summary>
        public const int MaxPayloadSize = 1163;

        /// <summary>EOS P2P max packet size.</summary>
        public const int MaxPacketSize = 1170;

        /// <summary>Seconds before incomplete fragment sets are discarded.</summary>
        public const float StaleTimeout = 5f;

        /// <summary>Maximum pending fragment assemblies per sender. Prevents memory exhaustion.</summary>
        public const int MaxPendingPerSender = 8;

        /// <summary>Maximum fragment index. Caps reassembled packet size at ~300 KB.</summary>
        public const int MaxFragmentIndex = 256;

        private uint _nextPacketId;

        // Key for pending fragment reassembly
        private struct FragmentKey : IEquatable<FragmentKey>
        {
            public ProductUserId Sender;
            public uint PacketId;
            public byte Channel;

            public bool Equals(FragmentKey other)
            {
                return PacketId == other.PacketId && Channel == other.Channel && Sender == other.Sender;
            }

            public override bool Equals(object obj) => obj is FragmentKey other && Equals(other);

            public override int GetHashCode()
            {
                unchecked
                {
                    int hash = PacketId.GetHashCode();
                    hash = (hash * 397) ^ Channel.GetHashCode();
                    if (Sender != null) hash = (hash * 397) ^ Sender.GetHashCode();
                    return hash;
                }
            }
        }

        private class FragmentAssembly
        {
            public byte[][] Fragments;
            public int ReceivedCount;
            public int TotalFragments; // -1 until last fragment received
            public float CreatedTime;
        }

        private readonly Dictionary<FragmentKey, FragmentAssembly> _pending = new();
        private readonly List<FragmentKey> _staleKeys = new(); // reused for cleanup

        /// <summary>Returns true if the data length exceeds a single fragment's payload capacity.</summary>
        public static bool NeedsFragmentation(int dataLength)
        {
            return dataLength > MaxPayloadSize;
        }

        /// <summary>
        /// Split data into fragments. Each fragment includes the 7-byte header.
        /// For data that fits in one fragment, outFragments will contain a single entry.
        /// </summary>
        public void Fragment(ArraySegment<byte> data, List<ArraySegment<byte>> outFragments)
        {
            outFragments.Clear();
            uint packetId = _nextPacketId++;

            int remaining = data.Count;
            int sourceOffset = data.Offset;
            ushort fragmentIndex = 0;

            while (remaining > 0)
            {
                int payloadSize = Math.Min(remaining, MaxPayloadSize);
                bool isLast = (remaining - payloadSize) == 0;

                var fragment = new byte[HeaderSize + payloadSize];

                // Write header
                fragment[0] = (byte)packetId;
                fragment[1] = (byte)(packetId >> 8);
                fragment[2] = (byte)(packetId >> 16);
                fragment[3] = (byte)(packetId >> 24);
                fragment[4] = (byte)fragmentIndex;
                fragment[5] = (byte)(fragmentIndex >> 8);
                fragment[6] = isLast ? (byte)1 : (byte)0;

                // Copy payload
                Buffer.BlockCopy(data.Array, sourceOffset, fragment, HeaderSize, payloadSize);

                outFragments.Add(new ArraySegment<byte>(fragment));

                sourceOffset += payloadSize;
                remaining -= payloadSize;
                fragmentIndex++;
            }
        }

        /// <summary>
        /// Process an incoming fragment. Returns the complete reassembled payload when all
        /// fragments have been received, or null if still waiting for more fragments.
        /// </summary>
        public byte[] ProcessIncoming(ProductUserId sender, ArraySegment<byte> data, byte channel)
        {
            if (data.Count < HeaderSize) return null;

            byte[] buf = data.Array;
            int off = data.Offset;

            // Read header
            uint packetId = (uint)(buf[off] | (buf[off + 1] << 8) | (buf[off + 2] << 16) | (buf[off + 3] << 24));
            ushort fragmentIndex = (ushort)(buf[off + 4] | (buf[off + 5] << 8));
            bool isLast = buf[off + 6] != 0;

            int payloadSize = data.Count - HeaderSize;

            // Single-fragment fast path
            if (fragmentIndex == 0 && isLast)
            {
                var result = new byte[payloadSize];
                if (payloadSize > 0)
                    Buffer.BlockCopy(buf, off + HeaderSize, result, 0, payloadSize);
                return result;
            }

            // Reject fragments with index beyond safety limit
            if (fragmentIndex >= MaxFragmentIndex) return null;

            // Multi-fragment reassembly
            var key = new FragmentKey { Sender = sender, PacketId = packetId, Channel = channel };

            if (!_pending.TryGetValue(key, out var assembly))
            {
                // Check per-sender limit before creating new assembly
                if (CountPendingForSender(sender) >= MaxPendingPerSender)
                    return null; // drop — sender has too many pending assemblies

                assembly = new FragmentAssembly
                {
                    Fragments = new byte[16][], // start with capacity for 16 fragments
                    ReceivedCount = 0,
                    TotalFragments = -1,
                    CreatedTime = UnityEngine.Time.unscaledTime
                };
                _pending[key] = assembly;
            }

            // Grow array if needed
            if (fragmentIndex >= assembly.Fragments.Length)
            {
                var newArr = new byte[fragmentIndex + 16][];
                Array.Copy(assembly.Fragments, newArr, assembly.Fragments.Length);
                assembly.Fragments = newArr;
            }

            // Store fragment payload (skip header)
            if (assembly.Fragments[fragmentIndex] == null)
            {
                assembly.Fragments[fragmentIndex] = new byte[payloadSize];
                if (payloadSize > 0)
                    Buffer.BlockCopy(buf, off + HeaderSize, assembly.Fragments[fragmentIndex], 0, payloadSize);
                assembly.ReceivedCount++;
            }

            if (isLast)
                assembly.TotalFragments = fragmentIndex + 1;

            // Check if complete
            if (assembly.TotalFragments > 0 && assembly.ReceivedCount >= assembly.TotalFragments)
            {
                _pending.Remove(key);
                return Reassemble(assembly);
            }

            // Periodically clean stale fragments
            CleanStale();

            return null;
        }

        private byte[] Reassemble(FragmentAssembly assembly)
        {
            // Calculate total size
            int totalSize = 0;
            for (int i = 0; i < assembly.TotalFragments; i++)
                totalSize += assembly.Fragments[i]?.Length ?? 0;

            var result = new byte[totalSize];
            int offset = 0;
            for (int i = 0; i < assembly.TotalFragments; i++)
            {
                var frag = assembly.Fragments[i];
                if (frag != null && frag.Length > 0)
                {
                    Buffer.BlockCopy(frag, 0, result, offset, frag.Length);
                    offset += frag.Length;
                }
            }
            return result;
        }

        private void CleanStale()
        {
            float now = UnityEngine.Time.unscaledTime;
            _staleKeys.Clear();

            foreach (var kvp in _pending)
            {
                if (now - kvp.Value.CreatedTime > StaleTimeout)
                    _staleKeys.Add(kvp.Key);
            }

            for (int i = 0; i < _staleKeys.Count; i++)
                _pending.Remove(_staleKeys[i]);
        }

        private int CountPendingForSender(ProductUserId sender)
        {
            int count = 0;
            foreach (var kvp in _pending)
            {
                if (kvp.Key.Sender == sender)
                    count++;
            }
            return count;
        }

        /// <summary>Clear all pending fragments for a specific sender.</summary>
        public void ClearPendingForSender(ProductUserId sender)
        {
            _staleKeys.Clear();
            foreach (var kvp in _pending)
            {
                if (kvp.Key.Sender == sender)
                    _staleKeys.Add(kvp.Key);
            }
            for (int i = 0; i < _staleKeys.Count; i++)
                _pending.Remove(_staleKeys[i]);
        }

        /// <summary>Clear all pending fragment assemblies.</summary>
        public void ClearAll()
        {
            _pending.Clear();
        }
    }
}
