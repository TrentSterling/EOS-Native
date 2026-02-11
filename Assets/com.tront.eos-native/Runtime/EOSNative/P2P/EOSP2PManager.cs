using System;
using System.Collections.Generic;
using Epic.OnlineServices;
using Epic.OnlineServices.P2P;
using EOSNative.Lobbies;
using EOSNative.Logging;
using EOSNative.Net;
using UnityEngine;

namespace EOSNative.P2P
{
    /// <summary>
    /// Reusable singleton P2P mesh manager.
    /// Discovers peers from the lobby member list and establishes direct P2P connections.
    /// Polls for incoming packets each Update and fires OnPacketReceived.
    /// </summary>
    public class EOSP2PManager : MonoBehaviour
    {
        #region Singleton

        private static EOSP2PManager _instance;
        private static bool _shuttingDown;
        public static EOSP2PManager Instance
        {
            get
            {
                if (_shuttingDown) return _instance;
                if (_instance == null)
                {
                    _instance = FindAnyObjectByType<EOSP2PManager>();
                    if (_instance == null)
                    {
                        var go = new GameObject("EOSP2PManager");
                        if (EOSManager.Instance != null)
                            go.transform.SetParent(EOSManager.Instance.transform);
                        else
                            DontDestroyOnLoad(go);
                        _instance = go.AddComponent<EOSP2PManager>();
                    }
                }
                return _instance;
            }
        }

        #endregion

        #region Events

        /// <summary>Fired when a P2P connection is established with a peer.</summary>
        public event Action<ProductUserId> OnPeerConnected;

        /// <summary>Fired when a P2P connection with a peer is closed.</summary>
        public event Action<ProductUserId> OnPeerDisconnected;

        /// <summary>Fired when a packet is received. Args: sender, channel, data.</summary>
        public event Action<ProductUserId, byte, byte[]> OnPacketReceived;

        #endregion

        #region Properties

        /// <summary>Currently connected peers.</summary>
        public IReadOnlyCollection<ProductUserId> Peers => _peers;

        /// <summary>Whether the P2P mesh is active (listening for connections).</summary>
        public bool IsActive => _connectionRequestNotifyId != 0;

        /// <summary>
        /// Typed message router with batching, fragmentation, and dispatch.
        /// Lazy-created on first access. Auto-subscribes to OnPacketReceived.
        /// </summary>
        public MessageRouter Router
        {
            get
            {
                if (_router == null)
                {
                    _router = new MessageRouter(this);
                    // Auto-subscribe router to raw packet events
                    OnPacketReceived += _router.ProcessIncoming;
                }
                return _router;
            }
        }

        public const string SOCKET_NAME = "EOSP2P";

        /// <summary>Number of SendPacket calls that returned non-Success.</summary>
        public int SendFailures { get; private set; }

        /// <summary>Total SendPacket calls attempted.</summary>
        public int SendAttempts { get; private set; }

        #endregion

        #region Private Fields

        private readonly HashSet<ProductUserId> _peers = new();
        private ulong _connectionRequestNotifyId;
        private ulong _connectionEstablishedNotifyId;
        private ulong _connectionClosedNotifyId;
        private SocketId _socketId = new() { SocketName = SOCKET_NAME };
        private byte[] _receiveBuffer = new byte[P2PInterface.MAX_PACKET_SIZE];
        private ProductUserId _cachedPeerId;
        private SocketId _cachedSocketId = new();
        private MessageRouter _router;

        // Handshake retry: periodically re-send handshakes if in a lobby but no peers connected
        private float _handshakeRetryTimer;
        private int _handshakeRetryCount;
        private const float HANDSHAKE_RETRY_INTERVAL = 2f;
        private const int MAX_HANDSHAKE_RETRIES = 5;

        private P2PInterface P2P => EOSManager.Instance?.P2PInterface;
        private ProductUserId LocalUserId => EOSManager.Instance?.LocalProductUserId;

        #endregion

        private void Awake()
        {
            _shuttingDown = false;
            if (_instance != null && _instance != this)
            {
                Destroy(gameObject);
                return;
            }
            _instance = this;
            if (transform.parent == null)
                DontDestroyOnLoad(gameObject);
        }

        private void OnApplicationQuit() => _shuttingDown = true;

        private void OnEnable()
        {
            var lobby = EOSLobbyManager.Instance;
            if (lobby != null)
            {
                lobby.OnLobbyJoined += OnLobbyJoined;
                lobby.OnLobbyLeft += OnLobbyLeft;
                lobby.OnMemberJoined += OnMemberJoined;
                lobby.OnMemberLeft += OnMemberLeftHandler;
            }

            // Re-subscribe router if it was previously created (handles OnDisable/OnEnable cycles)
            if (_router != null)
            {
                OnPacketReceived -= _router.ProcessIncoming;
                OnPacketReceived += _router.ProcessIncoming;
            }
        }

        private void OnDisable()
        {
            var lobby = EOSLobbyManager.Instance;
            if (lobby != null)
            {
                lobby.OnLobbyJoined -= OnLobbyJoined;
                lobby.OnLobbyLeft -= OnLobbyLeft;
                lobby.OnMemberJoined -= OnMemberJoined;
                lobby.OnMemberLeft -= OnMemberLeftHandler;
            }

            if (_router != null)
                OnPacketReceived -= _router.ProcessIncoming;

            Shutdown();
        }

        private void Update()
        {
            if (P2P == null || LocalUserId == null) return;
            PollPackets();
            CheckHandshakeRetry();
        }

        private void LateUpdate()
        {
            _router?.Flush();
        }

        #region Public API

        /// <summary>Start listening for P2P connections.</summary>
        public void Initialize()
        {
            if (P2P == null || LocalUserId == null)
            {
                EOSDebugLogger.LogWarning(DebugCategory.EOSManager, "EOSP2PManager",
                    $"Initialize skipped: P2P={(P2P != null ? "ok" : "NULL")}, LocalUserId={(LocalUserId != null ? LocalUserId.ToString() : "NULL")}");
                return;
            }
            if (_connectionRequestNotifyId != 0) return;

            var requestOptions = new AddNotifyPeerConnectionRequestOptions
            {
                LocalUserId = LocalUserId,
                SocketId = _socketId
            };
            _connectionRequestNotifyId = P2P.AddNotifyPeerConnectionRequest(ref requestOptions, null, OnConnectionRequest);

            var establishedOptions = new AddNotifyPeerConnectionEstablishedOptions
            {
                LocalUserId = LocalUserId,
                SocketId = _socketId
            };
            _connectionEstablishedNotifyId = P2P.AddNotifyPeerConnectionEstablished(ref establishedOptions, null, OnConnectionEstablished);

            var closedOptions = new AddNotifyPeerConnectionClosedOptions
            {
                LocalUserId = LocalUserId,
                SocketId = _socketId
            };
            _connectionClosedNotifyId = P2P.AddNotifyPeerConnectionClosed(ref closedOptions, null, OnConnectionClosed);

            // Increase packet queue to handle high-throughput streaming (4 MB in/out)
            var queueOptions = new SetPacketQueueSizeOptions
            {
                IncomingPacketQueueMaxSizeBytes = 4 * 1024 * 1024,
                OutgoingPacketQueueMaxSizeBytes = 4 * 1024 * 1024
            };
            P2P.SetPacketQueueSize(ref queueOptions);

            EOSDebugLogger.Log(DebugCategory.EOSManager, "EOSP2PManager",
                $"P2P mesh initialized (LocalUserId={LocalUserId})");
        }

        /// <summary>Stop listening and close all connections.</summary>
        public void Shutdown()
        {
            _handshakeRetryCount = 0;
            _handshakeRetryTimer = 0f;

            if (P2P == null) return;

            if (_connectionRequestNotifyId != 0)
            {
                P2P.RemoveNotifyPeerConnectionRequest(_connectionRequestNotifyId);
                _connectionRequestNotifyId = 0;
            }
            if (_connectionEstablishedNotifyId != 0)
            {
                P2P.RemoveNotifyPeerConnectionEstablished(_connectionEstablishedNotifyId);
                _connectionEstablishedNotifyId = 0;
            }
            if (_connectionClosedNotifyId != 0)
            {
                P2P.RemoveNotifyPeerConnectionClosed(_connectionClosedNotifyId);
                _connectionClosedNotifyId = 0;
            }

            foreach (var peer in _peers)
            {
                var closeOpts = new CloseConnectionOptions
                {
                    LocalUserId = LocalUserId,
                    RemoteUserId = peer,
                    SocketId = _socketId
                };
                P2P.CloseConnection(ref closeOpts);
            }
            _peers.Clear();
            _router?.ClearAll();

            EOSDebugLogger.Log(DebugCategory.EOSManager, "EOSP2PManager", "P2P mesh shut down");
        }

        /// <summary>Send data to all connected peers.</summary>
        public void SendToAll(byte channel, byte[] data, PacketReliability reliability = PacketReliability.UnreliableUnordered)
        {
            foreach (var peer in _peers)
                SendToPeer(peer, channel, data, reliability);
        }

        /// <summary>Send data to a specific peer.</summary>
        public void SendToPeer(ProductUserId peer, byte channel, byte[] data, PacketReliability reliability = PacketReliability.UnreliableUnordered)
        {
            if (P2P == null || LocalUserId == null) return;

            var options = new SendPacketOptions
            {
                LocalUserId = LocalUserId,
                RemoteUserId = peer,
                SocketId = _socketId,
                Channel = channel,
                Data = new ArraySegment<byte>(data),
                Reliability = reliability,
                AllowDelayedDelivery = true
            };
            SendAttempts++;
            var result = P2P.SendPacket(ref options);
            if (result != Result.Success)
            {
                SendFailures++;
                if (SendFailures <= 10 || SendFailures % 100 == 0)
                    EOSDebugLogger.LogWarning(DebugCategory.EOSManager, "EOSP2PManager", $"SendPacket to {peer} ch={channel} failed: {result} (failures: {SendFailures}/{SendAttempts})");
            }
            NetworkStats._instance?.RecordBytesSent(peer, data.Length);
        }

        /// <summary>Returns the fraction of outgoing packet queue used (0.0 to 1.0).</summary>
        public float GetOutgoingQueueFillRatio()
        {
            if (P2P == null) return 0f;
            var options = new GetPacketQueueInfoOptions();
            P2P.GetPacketQueueInfo(ref options, out var info);
            if (info.OutgoingPacketQueueMaxSizeBytes == 0) return 0f;
            return (float)info.OutgoingPacketQueueCurrentSizeBytes / info.OutgoingPacketQueueMaxSizeBytes;
        }

        /// <summary>Accept a connection from a specific peer and add them.</summary>
        public void AcceptPeer(ProductUserId remotePeer)
        {
            if (P2P == null || LocalUserId == null)
            {
                EOSDebugLogger.LogWarning(DebugCategory.EOSManager, "EOSP2PManager",
                    $"AcceptPeer skipped for {remotePeer}: P2P={(P2P != null ? "ok" : "NULL")}, LocalUserId={(LocalUserId != null ? "ok" : "NULL")}");
                return;
            }

            var acceptOpts = new AcceptConnectionOptions
            {
                LocalUserId = LocalUserId,
                RemoteUserId = remotePeer,
                SocketId = _socketId
            };
            var result = P2P.AcceptConnection(ref acceptOpts);
            if (result != Result.Success)
                EOSDebugLogger.Log(DebugCategory.EOSManager, "EOSP2PManager", $"AcceptConnection({remotePeer}): {result}");
        }

        #endregion

        #region Lobby Integration

        private void OnLobbyJoined(LobbyData lobby)
        {
            // Reset retry state for fresh lobby join
            _handshakeRetryCount = 0;
            _handshakeRetryTimer = 0f;

            Initialize();

            // Pre-accept all existing lobby members and send a handshake to kick-start connections.
            // OnMemberJoined only fires for NEW members joining after us, so existing members
            // must be handled here — otherwise the joiner never calls AcceptPeer and no
            // P2P connection is established (the host-order join bug).
            SendHandshakeToLobbyMembers("OnLobbyJoined");
        }

        private void OnLobbyLeft()
        {
            Shutdown();
        }

        private void OnMemberJoined(LobbyMemberData member)
        {
            if (LocalUserId == null) return;
            var puid = ProductUserId.FromString(member.Puid);
            if (puid == null || puid == LocalUserId) return;

            // Pre-accept connection from this lobby member
            AcceptPeer(puid);

            // Send a handshake to kick-start the P2P connection from our side too.
            // Without this, the host only accepts but never sends, so the connection
            // may not establish if the joiner's handshake is delayed.
            SendHandshakeToPeer(puid, "OnMemberJoined");
        }

        private void OnMemberLeftHandler(string puid)
        {
            var remotePuid = ProductUserId.FromString(puid);
            if (remotePuid == null) return;

            if (_peers.Remove(remotePuid))
            {
                var closeOpts = new CloseConnectionOptions
                {
                    LocalUserId = LocalUserId,
                    RemoteUserId = remotePuid,
                    SocketId = _socketId
                };
                P2P.CloseConnection(ref closeOpts);
                _router?.OnPeerDisconnected(remotePuid);
                OnPeerDisconnected?.Invoke(remotePuid);
                EOSDebugLogger.Log(DebugCategory.EOSManager, "EOSP2PManager", $"Peer left: {puid}");
            }
        }

        /// <summary>
        /// Send handshake packets to all non-connected lobby members.
        /// Used by OnLobbyJoined and the retry mechanism.
        /// </summary>
        private int SendHandshakeToLobbyMembers(string context)
        {
            var lobbyMgr = EOSLobbyManager.Instance;
            if (lobbyMgr == null || !lobbyMgr.IsInLobby)
            {
                EOSDebugLogger.LogWarning(DebugCategory.EOSManager, "EOSP2PManager",
                    $"[{context}] Cannot send handshakes: lobby={(lobbyMgr != null ? "exists" : "NULL")}, inLobby={(lobbyMgr?.IsInLobby ?? false)}");
                return 0;
            }

            var members = lobbyMgr.GetMemberPuids();
            int handshakesSent = 0;

            foreach (var puid in members)
            {
                if (puid == null || puid == LocalUserId) continue;
                if (_peers.Contains(puid)) continue; // already connected

                AcceptPeer(puid);
                SendHandshakeToPeer(puid, context);
                handshakesSent++;
            }

            if (handshakesSent > 0)
                EOSDebugLogger.Log(DebugCategory.EOSManager, "EOSP2PManager",
                    $"[{context}] Sent {handshakesSent} handshakes (lobby has {members.Count} total members, {_peers.Count} already connected)");
            else if (members.Count <= 1)
                EOSDebugLogger.Log(DebugCategory.EOSManager, "EOSP2PManager",
                    $"[{context}] No remote members in lobby (only self)");

            return handshakesSent;
        }

        /// <summary>Send a handshake packet (msgId 0xFE) to trigger EOS P2P connection establishment.</summary>
        private void SendHandshakeToPeer(ProductUserId puid, string context)
        {
            var writer = NetWriterPool.Get();
            writer.Reset();
            Router.SendToPeerImmediate(0xFE, writer, puid, PacketReliability.ReliableOrdered, 0);
            NetWriterPool.Return(writer);
        }

        /// <summary>
        /// Periodically retries handshakes if we're in a lobby but have no P2P peers connected.
        /// Handles timing issues where the initial handshake may fail, get lost, or the
        /// remote side wasn't ready yet.
        /// </summary>
        private void CheckHandshakeRetry()
        {
            // Only retry if: mesh active, still have retries left, not all connected
            if (!IsActive || _handshakeRetryCount >= MAX_HANDSHAKE_RETRIES) return;
            if (_peers.Count > 0) return; // at least one peer connected, mesh is working

            var lobbyMgr = EOSLobbyManager.Instance;
            if (lobbyMgr == null || !lobbyMgr.IsInLobby) return;

            _handshakeRetryTimer += Time.deltaTime;
            if (_handshakeRetryTimer < HANDSHAKE_RETRY_INTERVAL) return;
            _handshakeRetryTimer = 0f;
            _handshakeRetryCount++;

            SendHandshakeToLobbyMembers($"Retry {_handshakeRetryCount}/{MAX_HANDSHAKE_RETRIES}");
        }

        #endregion

        #region EOS Callbacks

        private void OnConnectionRequest(ref OnIncomingConnectionRequestInfo data)
        {
            // Auto-accept connections on our socket
            AcceptPeer(data.RemoteUserId);
            EOSDebugLogger.Log(DebugCategory.EOSManager, "EOSP2PManager",
                $"Incoming connection request from {data.RemoteUserId} — auto-accepted");
        }

        private void OnConnectionEstablished(ref OnPeerConnectionEstablishedInfo data)
        {
            if (_peers.Add(data.RemoteUserId))
            {
                NetworkStats._instance?.RecordConnectionType(data.RemoteUserId, data.NetworkType, data.ConnectionType);
                OnPeerConnected?.Invoke(data.RemoteUserId);
                EOSDebugLogger.Log(DebugCategory.EOSManager, "EOSP2PManager",
                    $"Peer connected: {data.RemoteUserId} (type={data.ConnectionType}, net={data.NetworkType})");
            }
        }

        private void OnConnectionClosed(ref OnRemoteConnectionClosedInfo data)
        {
            if (_peers.Remove(data.RemoteUserId))
            {
                // Allow retries again if all peers disconnected
                if (_peers.Count == 0)
                {
                    _handshakeRetryCount = 0;
                    _handshakeRetryTimer = 0f;
                }

                _router?.OnPeerDisconnected(data.RemoteUserId);
                OnPeerDisconnected?.Invoke(data.RemoteUserId);
                EOSDebugLogger.Log(DebugCategory.EOSManager, "EOSP2PManager",
                    $"Peer disconnected: {data.RemoteUserId} (reason={data.Reason})");
            }
        }

        #endregion

        #region Packet Polling

        private void PollPackets()
        {
            var sizeOptions = new GetNextReceivedPacketSizeOptions { LocalUserId = LocalUserId };

            while (P2P.GetNextReceivedPacketSize(ref sizeOptions, out uint packetSize) == Result.Success)
            {
                if (packetSize > _receiveBuffer.Length)
                    _receiveBuffer = new byte[packetSize];

                var receiveOptions = new ReceivePacketOptions
                {
                    LocalUserId = LocalUserId,
                    MaxDataSizeBytes = packetSize
                };

                var result = P2P.ReceivePacket(
                    ref receiveOptions,
                    ref _cachedPeerId,
                    ref _cachedSocketId,
                    out byte channel,
                    new ArraySegment<byte>(_receiveBuffer, 0, (int)packetSize),
                    out uint bytesWritten
                );

                if (result == Result.Success && bytesWritten > 0)
                {
                    var data = new byte[bytesWritten];
                    Buffer.BlockCopy(_receiveBuffer, 0, data, 0, (int)bytesWritten);
                    NetworkStats._instance?.RecordBytesReceived(_cachedPeerId, (int)bytesWritten);
                    OnPacketReceived?.Invoke(_cachedPeerId, channel, data);
                }
            }
        }

        #endregion
    }
}
