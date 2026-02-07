using System;
using System.Collections.Generic;
using Epic.OnlineServices;
using Epic.OnlineServices.P2P;
using EOSNative.Logging;
using EOSNative.P2P;
using UnityEngine;

namespace EOSNative.Net
{
    /// <summary>
    /// Connection statistics manager. Tracks RTT, packet loss, bandwidth, NAT type,
    /// and per-peer connection quality via a custom ping/pong protocol.
    /// Auto-creates under EOSManager like other singletons.
    /// </summary>
    public class NetworkStats : MonoBehaviour
    {
        #region Inner Types

        /// <summary>Per-peer connection statistics.</summary>
        public class PeerStats
        {
            public ProductUserId PeerId;
            public long BytesSent;
            public long BytesReceived;
            public long PacketsSent;
            public long PacketsReceived;
            public float RTT = -1f; // ms, -1 = unknown
            public float PacketLoss; // 0.0 - 1.0
            public NetworkConnectionType ConnectionType;
            public ConnectionEstablishedType EstablishedType;
            public float ConnectedTime; // Time.unscaledTime when connected

            // Internal ping tracking
            internal uint PingSequence;
            internal float LastPingSent;
            internal float[] RecentRTTs = new float[8];
            internal int RTTWriteIndex;
            internal int RTTCount;
            internal int PingsExpected;
            internal int PongsReceived;
            internal float LossWindowStart;
        }

        /// <summary>Aggregated global statistics.</summary>
        public struct GlobalStats
        {
            public long TotalBytesSent;
            public long TotalBytesReceived;
            public long TotalPacketsSent;
            public long TotalPacketsReceived;
            public float AverageRTT; // ms
            public float BandwidthOutKBps;
            public float BandwidthInKBps;
            public NATType LocalNATType;
            public ulong IncomingQueueBytes;
            public ulong IncomingQueuePackets;
            public ulong IncomingQueueMaxBytes;
            public ulong OutgoingQueueBytes;
            public ulong OutgoingQueuePackets;
            public ulong OutgoingQueueMaxBytes;
        }

        private struct BandwidthSample
        {
            public long BytesSent;
            public long BytesReceived;
            public float Timestamp;
        }

        #endregion

        #region Singleton

        internal static NetworkStats _instance;

        public static NetworkStats Instance
        {
            get
            {
                if (_instance == null)
                {
                    _instance = FindAnyObjectByType<NetworkStats>();
                    if (_instance == null)
                    {
                        var go = new GameObject("NetworkStats");
                        if (EOSManager.Instance != null)
                            go.transform.SetParent(EOSManager.Instance.transform);
                        else
                            DontDestroyOnLoad(go);
                        _instance = go.AddComponent<NetworkStats>();
                    }
                }
                return _instance;
            }
        }

        #endregion

        #region Configuration

        [Header("Timing")]
        [Tooltip("How often to send pings to each peer (seconds).")]
        [SerializeField] private float _pingInterval = 1.0f;

        [Tooltip("How often to sample bandwidth and update stats (seconds).")]
        [SerializeField] private float _sampleInterval = 0.5f;

        [Tooltip("Maximum number of bandwidth samples to keep (history length).")]
        [SerializeField] private int _maxSamples = 60;

        [Tooltip("Duration of the rolling packet loss window (seconds).")]
        [SerializeField] private float _lossWindowDuration = 10.0f;

        #endregion

        #region Message IDs

        private const byte MSG_PING = 0xA8;
        private const byte MSG_PONG = 0xA9;
        private const byte PING_CHANNEL = 2;

        #endregion

        #region Private Fields

        private readonly Dictionary<ProductUserId, PeerStats> _peerStats = new();
        private readonly List<BandwidthSample> _bandwidthSamples = new();

        private long _totalBytesSent;
        private long _totalBytesReceived;
        private long _totalPacketsSent;
        private long _totalPacketsReceived;
        private float _bandwidthOutKBps;
        private float _bandwidthInKBps;
        private NATType _localNATType = NATType.Unknown;
        private bool _natQueried;

        private float _lastPingTime;
        private float _lastSampleTime;
        private bool _routerRegistered;

        private P2PInterface P2P => EOSManager.Instance?.P2PInterface;

        #endregion

        #region Events

        /// <summary>Fires every sample interval with updated stats.</summary>
        public event Action OnStatsUpdated;

        #endregion

        #region Public API

        /// <summary>All peer stats as a read-only dictionary.</summary>
        public IReadOnlyDictionary<ProductUserId, PeerStats> AllPeerStats => _peerStats;

        /// <summary>Local NAT type (Unknown until queried).</summary>
        public NATType LocalNATType => _localNATType;

        /// <summary>Average RTT across all peers with known RTT (ms).</summary>
        public float AverageRTT
        {
            get
            {
                float sum = 0f;
                int count = 0;
                foreach (var kvp in _peerStats)
                {
                    if (kvp.Value.RTT >= 0f)
                    {
                        sum += kvp.Value.RTT;
                        count++;
                    }
                }
                return count > 0 ? sum / count : -1f;
            }
        }

        /// <summary>Total outgoing bandwidth in KBps.</summary>
        public float TotalBandwidthOutKBps => _bandwidthOutKBps;

        /// <summary>Total incoming bandwidth in KBps.</summary>
        public float TotalBandwidthInKBps => _bandwidthInKBps;

        /// <summary>Get stats for a specific peer, or null if not tracked.</summary>
        public PeerStats GetPeerStats(ProductUserId puid)
        {
            return _peerStats.TryGetValue(puid, out var stats) ? stats : null;
        }

        /// <summary>Get aggregated global stats including queue info.</summary>
        public GlobalStats GetGlobalStats()
        {
            var global = new GlobalStats
            {
                TotalBytesSent = _totalBytesSent,
                TotalBytesReceived = _totalBytesReceived,
                TotalPacketsSent = _totalPacketsSent,
                TotalPacketsReceived = _totalPacketsReceived,
                AverageRTT = AverageRTT,
                BandwidthOutKBps = _bandwidthOutKBps,
                BandwidthInKBps = _bandwidthInKBps,
                LocalNATType = _localNATType
            };

            // Query packet queue info from EOS
            if (P2P != null)
            {
                var opts = new GetPacketQueueInfoOptions();
                if (P2P.GetPacketQueueInfo(ref opts, out var queueInfo) == Result.Success)
                {
                    global.IncomingQueueBytes = queueInfo.IncomingPacketQueueCurrentSizeBytes;
                    global.IncomingQueuePackets = queueInfo.IncomingPacketQueueCurrentPacketCount;
                    global.IncomingQueueMaxBytes = queueInfo.IncomingPacketQueueMaxSizeBytes;
                    global.OutgoingQueueBytes = queueInfo.OutgoingPacketQueueCurrentSizeBytes;
                    global.OutgoingQueuePackets = queueInfo.OutgoingPacketQueueCurrentPacketCount;
                    global.OutgoingQueueMaxBytes = queueInfo.OutgoingPacketQueueMaxSizeBytes;
                }
            }

            return global;
        }

        /// <summary>Get RTT for a specific peer in milliseconds (-1 if unknown).</summary>
        public float RTT(ProductUserId puid)
        {
            return _peerStats.TryGetValue(puid, out var stats) ? stats.RTT : -1f;
        }

        /// <summary>Get packet loss for a specific peer (0.0 - 1.0).</summary>
        public float PacketLoss(ProductUserId puid)
        {
            return _peerStats.TryGetValue(puid, out var stats) ? stats.PacketLoss : 0f;
        }

        /// <summary>Get how long a peer has been connected (seconds).</summary>
        public float ConnectionAge(ProductUserId puid)
        {
            return _peerStats.TryGetValue(puid, out var stats)
                ? Time.unscaledTime - stats.ConnectedTime
                : 0f;
        }

        /// <summary>Reset all stats and clear tracking data.</summary>
        public void ResetStats()
        {
            _peerStats.Clear();
            _bandwidthSamples.Clear();
            _totalBytesSent = 0;
            _totalBytesReceived = 0;
            _totalPacketsSent = 0;
            _totalPacketsReceived = 0;
            _bandwidthOutKBps = 0f;
            _bandwidthInKBps = 0f;
        }

        #endregion

        #region Internal Recording API

        internal void RecordBytesSent(ProductUserId peer, int bytes)
        {
            _totalBytesSent += bytes;
            _totalPacketsSent++;
            var stats = GetOrCreatePeerStats(peer);
            stats.BytesSent += bytes;
            stats.PacketsSent++;
        }

        internal void RecordBytesReceived(ProductUserId peer, int bytes)
        {
            _totalBytesReceived += bytes;
            _totalPacketsReceived++;
            var stats = GetOrCreatePeerStats(peer);
            stats.BytesReceived += bytes;
            stats.PacketsReceived++;
        }

        internal void RecordConnectionType(ProductUserId peer, NetworkConnectionType networkType, ConnectionEstablishedType establishedType)
        {
            var stats = GetOrCreatePeerStats(peer);
            stats.ConnectionType = networkType;
            stats.EstablishedType = establishedType;
        }

        #endregion

        #region Unity Lifecycle

        private void Awake()
        {
            if (_instance != null && _instance != this)
            {
                Destroy(gameObject);
                return;
            }
            _instance = this;
            if (transform.parent == null)
                DontDestroyOnLoad(gameObject);
        }

        private void OnEnable()
        {
            var p2p = EOSP2PManager.Instance;
            if (p2p != null)
            {
                p2p.OnPeerConnected += OnPeerConnected;
                p2p.OnPeerDisconnected += OnPeerDisconnected;
            }
        }

        private void OnDisable()
        {
            var p2p = FindAnyObjectByType<EOSP2PManager>();
            if (p2p != null)
            {
                p2p.OnPeerConnected -= OnPeerConnected;
                p2p.OnPeerDisconnected -= OnPeerDisconnected;
            }
            UnregisterRouter();
        }

        private void Update()
        {
            EnsureRouterRegistered();

            float time = Time.unscaledTime;

            // Send pings
            if (time - _lastPingTime >= _pingInterval)
            {
                _lastPingTime = time;
                SendPings();
            }

            // Sample bandwidth and update loss
            if (time - _lastSampleTime >= _sampleInterval)
            {
                _lastSampleTime = time;
                SampleBandwidth();
                UpdatePacketLoss();
                QueryNATTypeIfNeeded();
                OnStatsUpdated?.Invoke();
            }
        }

        private void OnDestroy()
        {
            if (_instance == this)
                _instance = null;
        }

        #endregion

        #region Router Registration

        private void EnsureRouterRegistered()
        {
            if (_routerRegistered) return;
            var p2p = FindAnyObjectByType<EOSP2PManager>();
            if (p2p == null) return;

            p2p.Router.Register(MSG_PING, HandlePing);
            p2p.Router.Register(MSG_PONG, HandlePong);
            _routerRegistered = true;
            EOSDebugLogger.Log(DebugCategory.EOSManager, "NetworkStats", "Registered ping/pong handlers");
        }

        private void UnregisterRouter()
        {
            if (!_routerRegistered) return;
            var p2p = FindAnyObjectByType<EOSP2PManager>();
            if (p2p != null)
            {
                p2p.Router.Unregister(MSG_PING);
                p2p.Router.Unregister(MSG_PONG);
            }
            _routerRegistered = false;
        }

        #endregion

        #region Ping/Pong Protocol

        private void SendPings()
        {
            var p2p = FindAnyObjectByType<EOSP2PManager>();
            if (p2p == null) return;

            float time = Time.unscaledTime;

            foreach (var kvp in _peerStats)
            {
                var stats = kvp.Value;
                stats.PingSequence++;
                stats.LastPingSent = time;
                stats.PingsExpected++;

                var writer = NetWriterPool.Get();
                writer.WriteUInt32(stats.PingSequence);
                writer.WriteFloat(time);
                p2p.Router.SendToPeerImmediate(MSG_PING, writer, kvp.Key,
                    PacketReliability.UnreliableUnordered, PING_CHANNEL);
                NetWriterPool.Return(writer);
            }
        }

        private void HandlePing(ProductUserId sender, NetReader reader)
        {
            if (reader.Remaining < 8) return;

            uint sequence = reader.ReadUInt32();
            float timestamp = reader.ReadFloat();

            // Respond with pong containing the original sequence and timestamp
            var p2p = FindAnyObjectByType<EOSP2PManager>();
            if (p2p == null) return;

            var writer = NetWriterPool.Get();
            writer.WriteUInt32(sequence);
            writer.WriteFloat(timestamp);
            p2p.Router.SendToPeerImmediate(MSG_PONG, writer, sender,
                PacketReliability.UnreliableUnordered, PING_CHANNEL);
            NetWriterPool.Return(writer);
        }

        private void HandlePong(ProductUserId sender, NetReader reader)
        {
            if (reader.Remaining < 8) return;

            uint sequence = reader.ReadUInt32();
            float originalTimestamp = reader.ReadFloat();

            float rtt = (Time.unscaledTime - originalTimestamp) * 1000f; // ms
            if (rtt < 0f) rtt = 0f; // clock safety

            var stats = GetOrCreatePeerStats(sender);
            stats.PongsReceived++;

            // Store in circular buffer
            stats.RecentRTTs[stats.RTTWriteIndex] = rtt;
            stats.RTTWriteIndex = (stats.RTTWriteIndex + 1) % stats.RecentRTTs.Length;
            if (stats.RTTCount < stats.RecentRTTs.Length)
                stats.RTTCount++;

            // EMA smoothing (alpha = 0.2)
            if (stats.RTT < 0f)
                stats.RTT = rtt; // first measurement
            else
                stats.RTT = stats.RTT * 0.8f + rtt * 0.2f;
        }

        #endregion

        #region Bandwidth Sampling

        private void SampleBandwidth()
        {
            float time = Time.unscaledTime;

            var sample = new BandwidthSample
            {
                BytesSent = _totalBytesSent,
                BytesReceived = _totalBytesReceived,
                Timestamp = time
            };
            _bandwidthSamples.Add(sample);

            // Trim old samples
            while (_bandwidthSamples.Count > _maxSamples)
                _bandwidthSamples.RemoveAt(0);

            // Calculate KBps from last two samples
            if (_bandwidthSamples.Count >= 2)
            {
                var prev = _bandwidthSamples[_bandwidthSamples.Count - 2];
                var curr = _bandwidthSamples[_bandwidthSamples.Count - 1];
                float dt = curr.Timestamp - prev.Timestamp;
                if (dt > 0f)
                {
                    _bandwidthOutKBps = (curr.BytesSent - prev.BytesSent) / dt / 1024f;
                    _bandwidthInKBps = (curr.BytesReceived - prev.BytesReceived) / dt / 1024f;
                }
            }

            // Calculate per-peer bandwidth
            if (_bandwidthSamples.Count >= 2)
            {
                float dt = _bandwidthSamples[_bandwidthSamples.Count - 1].Timestamp
                         - _bandwidthSamples[_bandwidthSamples.Count - 2].Timestamp;
                if (dt > 0f)
                {
                    foreach (var kvp in _peerStats)
                    {
                        var ps = kvp.Value;
                        // Per-peer BW is tracked via delta from stored snapshots
                        // We store per-peer byte snapshots inline
                    }
                }
            }
        }

        #endregion

        #region Packet Loss

        private void UpdatePacketLoss()
        {
            float time = Time.unscaledTime;

            foreach (var kvp in _peerStats)
            {
                var stats = kvp.Value;

                // Reset window if expired
                if (time - stats.LossWindowStart >= _lossWindowDuration)
                {
                    if (stats.PingsExpected > 0)
                    {
                        stats.PacketLoss = 1.0f - Mathf.Clamp01((float)stats.PongsReceived / stats.PingsExpected);
                    }
                    stats.PingsExpected = 0;
                    stats.PongsReceived = 0;
                    stats.LossWindowStart = time;
                }
                else if (stats.PingsExpected > 0)
                {
                    // Interim calculation
                    stats.PacketLoss = 1.0f - Mathf.Clamp01((float)stats.PongsReceived / stats.PingsExpected);
                }
            }
        }

        #endregion

        #region NAT Type

        private void QueryNATTypeIfNeeded()
        {
            if (_natQueried || P2P == null) return;
            _natQueried = true;

            // Try cached first
            var getNatOpts = new GetNATTypeOptions();
            if (P2P.GetNATType(ref getNatOpts, out var cachedNat) == Result.Success)
            {
                _localNATType = cachedNat;
                EOSDebugLogger.Log(DebugCategory.EOSManager, "NetworkStats", $"NAT type (cached): {_localNATType}");
                return;
            }

            // Query fresh
            var queryOpts = new QueryNATTypeOptions();
            P2P.QueryNATType(ref queryOpts, null, (ref OnQueryNATTypeCompleteInfo info) =>
            {
                if (info.ResultCode == Result.Success)
                {
                    _localNATType = info.NATType;
                    EOSDebugLogger.Log(DebugCategory.EOSManager, "NetworkStats", $"NAT type: {_localNATType}");
                }
                else
                {
                    EOSDebugLogger.LogWarning(DebugCategory.EOSManager, "NetworkStats", $"NAT query failed: {info.ResultCode}");
                }
            });
        }

        #endregion

        #region Peer Lifecycle

        private PeerStats GetOrCreatePeerStats(ProductUserId peer)
        {
            if (!_peerStats.TryGetValue(peer, out var stats))
            {
                stats = new PeerStats
                {
                    PeerId = peer,
                    ConnectedTime = Time.unscaledTime,
                    LossWindowStart = Time.unscaledTime
                };
                _peerStats[peer] = stats;
            }
            return stats;
        }

        private void OnPeerConnected(ProductUserId peer)
        {
            GetOrCreatePeerStats(peer);
        }

        private void OnPeerDisconnected(ProductUserId peer)
        {
            _peerStats.Remove(peer);
        }

        #endregion
    }
}
