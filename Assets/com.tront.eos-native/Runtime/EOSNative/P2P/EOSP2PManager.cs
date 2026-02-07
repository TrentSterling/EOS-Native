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
        public static EOSP2PManager Instance
        {
            get
            {
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
        /// Lazy-created on first access. Subscribe Router.ProcessIncoming to OnPacketReceived to enable.
        /// </summary>
        public MessageRouter Router
        {
            get
            {
                _router ??= new MessageRouter(this);
                return _router;
            }
        }

        public const string SOCKET_NAME = "EOSP2P";

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

        private P2PInterface P2P => EOSManager.Instance?.P2PInterface;
        private ProductUserId LocalUserId => EOSManager.Instance?.LocalProductUserId;

        #endregion

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
            var lobby = EOSLobbyManager.Instance;
            if (lobby != null)
            {
                lobby.OnLobbyJoined += OnLobbyJoined;
                lobby.OnLobbyLeft += OnLobbyLeft;
                lobby.OnMemberJoined += OnMemberJoined;
                lobby.OnMemberLeft += OnMemberLeftHandler;
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
            Shutdown();
        }

        private void Update()
        {
            if (P2P == null || LocalUserId == null) return;
            PollPackets();
        }

        private void LateUpdate()
        {
            _router?.Flush();
        }

        #region Public API

        /// <summary>Start listening for P2P connections.</summary>
        public void Initialize()
        {
            if (P2P == null || LocalUserId == null) return;
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

            EOSDebugLogger.Log(DebugCategory.EOSManager, "EOSP2PManager", "P2P mesh initialized");
        }

        /// <summary>Stop listening and close all connections.</summary>
        public void Shutdown()
        {
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
            P2P.SendPacket(ref options);
            NetworkStats._instance?.RecordBytesSent(peer, data.Length);
        }

        /// <summary>Accept a connection from a specific peer and add them.</summary>
        public void AcceptPeer(ProductUserId remotePeer)
        {
            if (P2P == null || LocalUserId == null) return;

            var acceptOpts = new AcceptConnectionOptions
            {
                LocalUserId = LocalUserId,
                RemoteUserId = remotePeer,
                SocketId = _socketId
            };
            P2P.AcceptConnection(ref acceptOpts);
        }

        #endregion

        #region Lobby Integration

        private void OnLobbyJoined(LobbyData lobby)
        {
            Initialize();
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

        #endregion

        #region EOS Callbacks

        private void OnConnectionRequest(ref OnIncomingConnectionRequestInfo data)
        {
            // Auto-accept connections on our socket
            AcceptPeer(data.RemoteUserId);
            EOSDebugLogger.Log(DebugCategory.EOSManager, "EOSP2PManager", $"Accepted connection from {data.RemoteUserId}");
        }

        private void OnConnectionEstablished(ref OnPeerConnectionEstablishedInfo data)
        {
            if (_peers.Add(data.RemoteUserId))
            {
                NetworkStats._instance?.RecordConnectionType(data.RemoteUserId, data.NetworkType, data.ConnectionType);
                OnPeerConnected?.Invoke(data.RemoteUserId);
                EOSDebugLogger.Log(DebugCategory.EOSManager, "EOSP2PManager", $"Peer connected: {data.RemoteUserId}");
            }
        }

        private void OnConnectionClosed(ref OnRemoteConnectionClosedInfo data)
        {
            if (_peers.Remove(data.RemoteUserId))
            {
                _router?.OnPeerDisconnected(data.RemoteUserId);
                OnPeerDisconnected?.Invoke(data.RemoteUserId);
                EOSDebugLogger.Log(DebugCategory.EOSManager, "EOSP2PManager", $"Peer disconnected: {data.RemoteUserId}");
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
