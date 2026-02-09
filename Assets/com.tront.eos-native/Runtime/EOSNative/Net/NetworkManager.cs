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
    /// Manages all NetworkObjects: spawning, despawning, SyncVar delta sync,
    /// late-join snapshots, authority transfer (host migration), and RPCs.
    ///
    /// Uses EOSP2PManager's MessageRouter for typed message dispatch.
    /// Host is deterministically elected as the peer with the lexicographically lowest PUID.
    /// </summary>
    public class NetworkManager : MonoBehaviour
    {
        #region Message IDs (0xA0-0xAF reserved)

        private const byte MSG_STATE_UPDATE = 0xA0;
        private const byte MSG_SPAWN = 0xA1;
        private const byte MSG_DESPAWN = 0xA2;
        private const byte MSG_AUTHORITY = 0xA3;
        private const byte MSG_SNAPSHOT = 0xA4;
        private const byte MSG_SNAPSHOT_REQUEST = 0xA5;
        private const byte MSG_RPC = 0xA6;
        private const byte MSG_AUTHORITY_REQUEST = 0xA7;
        // 0xA8-0xA9 reserved by NetworkStats (PING/PONG)

        // Scene management (0xAA-0xAC)
        private const byte MSG_SCENE_LOAD = 0xAA;
        private const byte MSG_SCENE_UNLOAD = 0xAB;
        private const byte MSG_SCENE_LOADED_ACK = 0xAC;

        // Host-validated RPCs (0xAD-0xAE)
        private const byte MSG_RPC_VALIDATED = 0xAD;   // Client→Host: validated RPC request
        private const byte MSG_RPC_REBROADCAST = 0xAE; // Host→All: approved validated RPC

        // Chunked snapshot delivery
        private const int SNAPSHOT_CHUNK_SIZE = 16;

        #endregion

        #region Singleton

        private static NetworkManager _instance;
        private static bool _shuttingDown;
        public static NetworkManager Instance
        {
            get
            {
                if (_shuttingDown) return _instance;
                if (_instance == null)
                {
                    _instance = FindAnyObjectByType<NetworkManager>();
                    if (_instance == null)
                    {
                        var go = new GameObject("NetworkManager");
                        if (EOSManager.Instance != null)
                            go.transform.SetParent(EOSManager.Instance.transform);
                        else
                            DontDestroyOnLoad(go);
                        _instance = go.AddComponent<NetworkManager>();
                    }
                }
                return _instance;
            }
        }

        #endregion

        #region Properties

        /// <summary>True if this peer is the host (lowest PUID among all connected peers + self).</summary>
        public bool IsHost { get; private set; }

        /// <summary>All active NetworkObjects, keyed by NetworkId.</summary>
        public IReadOnlyDictionary<uint, NetworkObject> Objects => _objects;

        /// <summary>
        /// Optional RPC validation callback. If set, called before executing any incoming remote RPC.
        /// Return true to allow, false to reject. Null = all RPCs allowed (default, zero overhead).
        /// Parameters: (sender ProductUserId, target NetworkObject, methodHash uint) → bool.
        /// </summary>
        public Func<ProductUserId, NetworkObject, uint, bool> OnRPCValidation;

        /// <summary>Enable/disable Deflate compression for message payloads above threshold.</summary>
        public bool CompressionEnabled
        {
            get => Router.CompressionEnabled;
            set => Router.CompressionEnabled = value;
        }

        /// <summary>
        /// Set to true before joining a lobby to join as a read-only spectator.
        /// Spectators receive all state but cannot spawn objects or become host.
        /// </summary>
        public bool IsSpectator { get; set; }

        /// <summary>Check if a specific peer is a spectator.</summary>
        public bool IsPeerSpectator(ProductUserId puid)
        {
            return puid != null && _spectators.Contains(puid);
        }

        /// <summary>The shared room state object. Null until host creates it (after first peer connects).</summary>
        public NetworkRoomState RoomState { get; private set; }

        /// <summary>The local player's state object. Null until auto-created on peer connect.</summary>
        public NetworkPlayerState LocalPlayerState { get; private set; }

        /// <summary>All player states, keyed by owner ProductUserId.</summary>
        public IReadOnlyDictionary<ProductUserId, NetworkPlayerState> PlayerStates => _playerStates;

        /// <summary>Get a specific player's state by their ProductUserId.</summary>
        public NetworkPlayerState GetPlayerState(ProductUserId puid)
        {
            return _playerStates.TryGetValue(puid, out var state) ? state : null;
        }

        /// <summary>
        /// Get a player's NetworkObject by their ProductUserId.
        /// Returns the first owned object that is not a PlayerState or RoomState.
        /// Useful for finding a player's avatar/character object.
        /// </summary>
        public NetworkObject GetPlayerObject(ProductUserId puid)
        {
            if (puid == null) return null;
            foreach (var obj in _objects.Values)
            {
                if (obj.OwnerId == puid && obj.PrefabId != NetworkPlayerState.PREFAB_ID && obj.PrefabId != NetworkRoomState.PREFAB_ID)
                    return obj;
            }
            return null;
        }

        /// <summary>
        /// All connected player ProductUserIds (includes self if connected).
        /// </summary>
        public IReadOnlyList<ProductUserId> ConnectedPlayers
        {
            get
            {
                var list = new List<ProductUserId>();
                var localPuid = EOSManager.Instance?.LocalProductUserId;
                if (localPuid != null) list.Add(localPuid);
                var peers = EOSP2PManager.Instance?.Peers;
                if (peers != null)
                {
                    foreach (var peer in peers)
                        list.Add(peer);
                }
                return list;
            }
        }

        /// <summary>
        /// True if connected to at least one peer via P2P.
        /// Quick check for "am I in a networked session".
        /// </summary>
        public bool IsOnline
        {
            get
            {
                var peers = EOSP2PManager.Instance?.Peers;
                return peers != null && peers.Count > 0;
            }
        }

        #endregion

        #region Prefab Registry

        [SerializeField] private List<GameObject> _prefabs = new();

        /// <summary>Register a prefab at runtime. The prefab must have a NetworkObject component.</summary>
        public void RegisterPrefab(GameObject prefab, ushort id)
        {
            while (_prefabs.Count <= id)
                _prefabs.Add(null);
            _prefabs[id] = prefab;
        }

        /// <summary>Get a registered prefab by ID.</summary>
        public GameObject GetPrefab(ushort id)
        {
            if (id < _prefabs.Count) return _prefabs[id];
            return null;
        }

        #endregion

        #region Spawning

        /// <summary>
        /// Spawn a networked object. The local peer becomes the owner.
        /// Instantiates from the prefab registry and broadcasts SPAWN to all peers.
        /// </summary>
        public NetworkObject Spawn(ushort prefabId, Vector3 position, Quaternion rotation)
        {
            if (IsSpectator)
            {
                Debug.LogWarning("[NetworkManager] Spectators cannot spawn objects");
                return null;
            }

            var prefab = GetPrefab(prefabId);
            if (prefab == null)
            {
                Debug.LogError($"[NetworkManager] No prefab registered for ID {prefabId}");
                return null;
            }

            var localPuid = EOSManager.Instance?.LocalProductUserId;
            if (localPuid == null)
            {
                Debug.LogError("[NetworkManager] Cannot spawn — not logged in");
                return null;
            }

            uint networkId = GenerateNetworkId();

            var go = GetFromPool(prefabId, prefab, position, rotation);
            var netObj = go.GetComponent<NetworkObject>();
            if (netObj == null)
                netObj = go.AddComponent<NetworkObject>();

            netObj.NetworkId = networkId;
            netObj.PrefabId = prefabId;
            netObj.OwnerId = localPuid;
            netObj.IsRegistered = true;
            _objects[networkId] = netObj;
            netObj.NotifyNetworkSpawn();

            // Broadcast spawn to all peers
            BroadcastSpawn(netObj);

            EOSDebugLogger.Log(DebugCategory.EOSManager, "NetworkManager",
                $"Spawned object {networkId} (prefab {prefabId})");

            return netObj;
        }

        /// <summary>
        /// Despawn a networked object. Deactivates it locally and broadcasts DESPAWN.
        /// The GameObject is deactivated, not destroyed (pooling-ready).
        /// </summary>
        public void Despawn(NetworkObject obj)
        {
            if (obj == null) return;
            if (!obj.IsRegistered) return;

            // Only owner or host can despawn
            if (!obj.IsOwner && !IsHost) return;

            uint networkId = obj.NetworkId;
            ushort prefabId = obj.PrefabId;
            _objects.Remove(networkId);
            _dirtyObjects.Remove(obj);
            obj.IsRegistered = false;
            obj.NotifyNetworkDespawn();
            UnregisterRPCs(obj);
            ReturnToPool(prefabId, obj.gameObject);

            // Broadcast despawn
            var writer = NetWriterPool.Get();
            writer.WriteUInt32(networkId);
            Router.SendToAll(MSG_DESPAWN, writer, PacketReliability.ReliableOrdered, 1);
            NetWriterPool.Return(writer);

            EOSDebugLogger.Log(DebugCategory.EOSManager, "NetworkManager",
                $"Despawned object {networkId}");
        }

        #endregion

        #region RPC

        /// <summary>
        /// Send an RPC to invoke a named method on a NetworkObject across the network.
        /// Method name is hashed for wire efficiency. Args are serialized via NetSerializers.
        /// </summary>
        /// <param name="target">The NetworkObject to invoke on.</param>
        /// <param name="methodName">The method name (must be registered via RegisterRPC).</param>
        /// <param name="targets">Who should receive this RPC.</param>
        /// <param name="args">Arguments to serialize. Must be types registered in NetSerializers.</param>
        public void SendRPC(NetworkObject target, string methodName, RPCTarget targets, params object[] args)
        {
            if (target == null || !target.IsRegistered) return;

            // Buffer host-targeted RPCs during migration window
            if (_migrationInProgress && (targets == RPCTarget.Host || targets == RPCTarget.Owner))
            {
                var bufWriter = NetWriterPool.Get();
                if (args != null)
                {
                    for (int i = 0; i < args.Length; i++)
                    {
                        var arg = args[i];
                        var type = arg?.GetType() ?? typeof(object);
                        if (_typeWriters.TryGetValue(type, out var w))
                            w(bufWriter, arg);
                    }
                }
                _migrationBuffer.Add(new BufferedRPC
                {
                    Target = target,
                    MethodName = methodName,
                    MethodHash = FnvHash(methodName),
                    Targets = targets,
                    ArgData = bufWriter.ToArray()
                });
                NetWriterPool.Return(bufWriter);
                return;
            }

            uint nameHash = FnvHash(methodName);

            // Determine who gets the RPC
            bool executeLocal = false;
            bool sendRemote = false;

            switch (targets)
            {
                case RPCTarget.All:
                    executeLocal = true;
                    sendRemote = true;
                    break;
                case RPCTarget.Others:
                    sendRemote = true;
                    break;
                case RPCTarget.Host:
                    if (IsHost) executeLocal = true;
                    else sendRemote = true;
                    break;
                case RPCTarget.Owner:
                    if (target.IsOwner) executeLocal = true;
                    else sendRemote = true;
                    break;
                case RPCTarget.Players:
                    executeLocal = !IsSpectator;
                    sendRemote = true;
                    break;
            }

            // Build the arg payload once (shared by local and remote)
            var argWriter = NetWriterPool.Get();
            if (args != null)
            {
                for (int i = 0; i < args.Length; i++)
                {
                    var arg = args[i];
                    var type = arg?.GetType() ?? typeof(object);
                    if (!_typeWriters.TryGetValue(type, out var writeAction))
                    {
                        Debug.LogError($"[NetworkManager] RPC arg type {type.Name} not registered in NetSerializers");
                        NetWriterPool.Return(argWriter);
                        return;
                    }
                    writeAction(argWriter, arg);
                }
            }
            byte[] argData = argWriter.ToArray();
            NetWriterPool.Return(argWriter);

            if (executeLocal)
                ExecuteRPCLocal(target.NetworkId, nameHash, argData, argData.Length);

            if (sendRemote)
            {
                var writer = NetWriterPool.Get();
                writer.WriteUInt32(target.NetworkId);
                writer.WriteUInt32(nameHash);
                writer.WriteBytesRaw(argData, 0, argData.Length);

                switch (targets)
                {
                    case RPCTarget.All:
                    case RPCTarget.Others:
                        Router.SendToAll(MSG_RPC, writer, PacketReliability.ReliableOrdered, 1);
                        break;
                    case RPCTarget.Host:
                        if (!IsHost)
                        {
                            var hostPuid = GetHostPuid();
                            if (hostPuid != null)
                                Router.SendToPeer(MSG_RPC, writer, hostPuid, PacketReliability.ReliableOrdered, 1);
                        }
                        break;
                    case RPCTarget.Owner:
                        if (!target.IsOwner && target.OwnerId != null)
                            Router.SendToPeer(MSG_RPC, writer, target.OwnerId, PacketReliability.ReliableOrdered, 1);
                        break;
                    case RPCTarget.Players:
                        SendToNonSpectators(MSG_RPC, writer);
                        break;
                }

                NetWriterPool.Return(writer);
            }
        }

        /// <summary>
        /// Send an RPC directly to a specific peer by their ProductUserId.
        /// Goes peer-to-peer — no host routing. This is the EOS-native way.
        /// </summary>
        public void SendRPC(NetworkObject target, string methodName, ProductUserId peer, params object[] args)
        {
            if (target == null || !target.IsRegistered) return;
            if (peer == null) return;

            uint nameHash = FnvHash(methodName);
            byte[] argData = SerializeRPCArgs(args);
            if (argData == null) return;

            // Execute locally if we're the target
            var localPuid = EOSManager.Instance?.LocalProductUserId;
            if (localPuid != null && peer == localPuid)
            {
                ExecuteRPCLocal(target.NetworkId, nameHash, argData, argData.Length);
                return;
            }

            var writer = NetWriterPool.Get();
            writer.WriteUInt32(target.NetworkId);
            writer.WriteUInt32(nameHash);
            writer.WriteBytesRaw(argData, 0, argData.Length);
            Router.SendToPeer(MSG_RPC, writer, peer, PacketReliability.ReliableOrdered, 1);
            NetWriterPool.Return(writer);
        }

        /// <summary>
        /// Send an RPC to multiple specific peers by their ProductUserIds.
        /// Each peer gets a direct P2P message — no host routing.
        /// </summary>
        public void SendRPC(NetworkObject target, string methodName, IEnumerable<ProductUserId> peers, params object[] args)
        {
            if (target == null || !target.IsRegistered) return;
            if (peers == null) return;

            uint nameHash = FnvHash(methodName);
            byte[] argData = SerializeRPCArgs(args);
            if (argData == null) return;

            var localPuid = EOSManager.Instance?.LocalProductUserId;

            foreach (var peer in peers)
            {
                if (peer == null) continue;

                if (localPuid != null && peer == localPuid)
                {
                    ExecuteRPCLocal(target.NetworkId, nameHash, argData, argData.Length);
                    continue;
                }

                var writer = NetWriterPool.Get();
                writer.WriteUInt32(target.NetworkId);
                writer.WriteUInt32(nameHash);
                writer.WriteBytesRaw(argData, 0, argData.Length);
                Router.SendToPeer(MSG_RPC, writer, peer, PacketReliability.ReliableOrdered, 1);
                NetWriterPool.Return(writer);
            }
        }

        /// <summary>Serialize RPC args into a byte array. Returns null on error.</summary>
        private byte[] SerializeRPCArgs(object[] args)
        {
            var argWriter = NetWriterPool.Get();
            if (args != null)
            {
                for (int i = 0; i < args.Length; i++)
                {
                    var arg = args[i];
                    var type = arg?.GetType() ?? typeof(object);
                    if (!_typeWriters.TryGetValue(type, out var writeAction))
                    {
                        Debug.LogError($"[NetworkManager] RPC arg type {type.Name} not registered in NetSerializers");
                        NetWriterPool.Return(argWriter);
                        return null;
                    }
                    writeAction(argWriter, arg);
                }
            }
            byte[] data = argWriter.ToArray();
            NetWriterPool.Return(argWriter);
            return data;
        }

        // Cached type-to-writer lookup for RPC args (built from NetSerializers)
        private static readonly Dictionary<Type, Action<NetWriter, object>> _typeWriters = new()
        {
            [typeof(byte)] = (w, v) => w.WriteByte((byte)v),
            [typeof(bool)] = (w, v) => w.WriteBool((bool)v),
            [typeof(short)] = (w, v) => w.WriteInt16((short)v),
            [typeof(ushort)] = (w, v) => w.WriteUInt16((ushort)v),
            [typeof(int)] = (w, v) => w.WriteInt32((int)v),
            [typeof(uint)] = (w, v) => w.WriteUInt32((uint)v),
            [typeof(long)] = (w, v) => w.WriteInt64((long)v),
            [typeof(ulong)] = (w, v) => w.WriteUInt64((ulong)v),
            [typeof(float)] = (w, v) => w.WriteFloat((float)v),
            [typeof(double)] = (w, v) => w.WriteDouble((double)v),
            [typeof(string)] = (w, v) => w.WriteString((string)v ?? string.Empty),
            [typeof(Vector2)] = (w, v) => w.WriteVector2((Vector2)v),
            [typeof(Vector3)] = (w, v) => w.WriteVector3((Vector3)v),
            [typeof(Quaternion)] = (w, v) => w.WriteQuaternion((Quaternion)v),
            [typeof(Color)] = (w, v) => w.WriteColor((Color)v),
            [typeof(Color32)] = (w, v) => w.WriteColor32((Color32)v),
            [typeof(ProductUserId)] = (w, v) => w.WriteProductUserId((ProductUserId)v),
            [typeof(byte[])] = (w, v) => w.WriteBytes((byte[])v),
            [typeof(NetworkObject)] = (w, v) => w.WriteUInt32(v != null ? ((NetworkObject)v).NetworkId : 0u),
        };

        /// <summary>
        /// Register an RPC handler for a method name on a specific NetworkObject.
        /// The handler receives a NetReader positioned after the method hash — read args from it.
        /// </summary>
        public void RegisterRPC(NetworkObject target, string methodName, Action<NetReader> handler)
        {
            uint nameHash = FnvHash(methodName);
            var key = new RPCKey { NetworkId = target.NetworkId, MethodHash = nameHash };

            if (_rpcHandlers.ContainsKey(key))
            {
                // Check for hash collision
                if (_rpcMethodNames.TryGetValue(key, out string existing) && existing != methodName)
                    throw new InvalidOperationException(
                        $"RPC hash collision: '{methodName}' collides with '{existing}' on object {target.NetworkId}");
            }

            _rpcHandlers[key] = handler;
            _rpcMethodNames[key] = methodName;
        }

        /// <summary>
        /// Register an RPC handler by pre-computed hash. Called by weaver-generated __RegisterNetRPCs().
        /// The hash is computed at compile time to avoid runtime string hashing.
        /// </summary>
        public void RegisterRPC(NetworkObject target, uint methodHash, string methodName, Action<NetReader> handler)
        {
            var key = new RPCKey { NetworkId = target.NetworkId, MethodHash = methodHash };

            if (_rpcHandlers.ContainsKey(key))
            {
                if (_rpcMethodNames.TryGetValue(key, out string existing) && existing != methodName)
                    throw new InvalidOperationException(
                        $"RPC hash collision: '{methodName}' collides with '{existing}' on object {target.NetworkId}");
            }

            _rpcHandlers[key] = handler;
            _rpcMethodNames[key] = methodName;
        }

        /// <summary>
        /// Register a validator for a host-validated RPC. Called by weaver-generated __RegisterNetRPCs().
        /// The validator runs on the host before rebroadcasting. Return true to approve, false to reject.
        /// </summary>
        public void RegisterRPCValidator(uint methodHash, Func<ProductUserId, NetworkObject, byte[], bool> validator)
        {
            _rpcValidators[methodHash] = validator;
            _validatedRpcHashes.Add(methodHash);
        }

        /// <summary>
        /// Mark a method hash as requiring host validation (even without a custom validator).
        /// If no validator is registered, the host auto-approves (relay-only mode).
        /// </summary>
        public void MarkRPCValidated(uint methodHash)
        {
            _validatedRpcHashes.Add(methodHash);
        }

        /// <summary>
        /// Send a validated RPC through the host. Called by weaver for [NetRpc(Validated = true)].
        /// Client sends to host only. Host validates, then rebroadcasts to all.
        /// </summary>
        public void SendRPCValidated(NetworkObject target, uint methodHash, RPCTarget originalTarget, byte[] argData)
        {
            if (target == null || !target.IsRegistered) return;

            if (IsHost)
            {
                // We ARE the host — validate locally and broadcast directly
                var localPuid = EOSManager.Instance?.LocalProductUserId;
                if (!RunRPCValidator(methodHash, localPuid, target, argData))
                {
                    Debug.LogWarning($"[NetworkManager] Validated RPC rejected locally: object={target.NetworkId}, hash=0x{methodHash:X8}");
                    return;
                }

                // Execute locally per original target rules
                bool executeLocal = originalTarget switch
                {
                    RPCTarget.All => true,
                    RPCTarget.Others => false,
                    RPCTarget.Host => true,
                    RPCTarget.Owner => target.IsOwner,
                    RPCTarget.Players => !IsSpectator,
                    _ => false,
                };
                if (executeLocal)
                    ExecuteRPCLocal(target.NetworkId, methodHash, argData, argData.Length);

                // Rebroadcast to all peers using MSG_RPC_REBROADCAST
                var writer = NetWriterPool.Get();
                writer.WriteUInt32(target.NetworkId);
                writer.WriteUInt32(methodHash);
                writer.WriteByte((byte)originalTarget);
                writer.WriteBytesRaw(argData, 0, argData.Length);
                Router.SendToAll(MSG_RPC_REBROADCAST, writer, PacketReliability.ReliableOrdered, 1);
                NetWriterPool.Return(writer);
            }
            else
            {
                // Send to host for validation via MSG_RPC_VALIDATED
                var hostPuid = GetHostPuid();
                if (hostPuid == null) return;

                var writer = NetWriterPool.Get();
                writer.WriteUInt32(target.NetworkId);
                writer.WriteUInt32(methodHash);
                writer.WriteByte((byte)originalTarget);
                writer.WriteBytesRaw(argData, 0, argData.Length);
                Router.SendToPeer(MSG_RPC_VALIDATED, writer, hostPuid, PacketReliability.ReliableOrdered, 1);
                NetWriterPool.Return(writer);
            }
        }

        /// <summary>
        /// Send a pre-serialized RPC. Called by weaver-generated dispatch stubs.
        /// Args are already packed into argData by the generated serialization code.
        /// </summary>
        public void SendRPCWeaved(NetworkObject target, uint methodHash, RPCTarget targets, byte[] argData)
        {
            if (target == null || !target.IsRegistered) return;

            // Buffer host/owner-targeted RPCs during migration window
            if (_migrationInProgress && (targets == RPCTarget.Host || targets == RPCTarget.Owner))
            {
                _migrationBuffer.Add(new BufferedRPC
                {
                    Target = target,
                    MethodName = null,
                    MethodHash = methodHash,
                    Targets = targets,
                    ArgData = argData
                });
                return;
            }

            bool executeLocal = false;
            bool sendRemote = false;

            switch (targets)
            {
                case RPCTarget.All:
                    executeLocal = true;
                    sendRemote = true;
                    break;
                case RPCTarget.Others:
                    sendRemote = true;
                    break;
                case RPCTarget.Host:
                    if (IsHost) executeLocal = true;
                    else sendRemote = true;
                    break;
                case RPCTarget.Owner:
                    if (target.IsOwner) executeLocal = true;
                    else sendRemote = true;
                    break;
                case RPCTarget.Players:
                    executeLocal = !IsSpectator;
                    sendRemote = true;
                    break;
            }

            if (executeLocal)
                ExecuteRPCLocal(target.NetworkId, methodHash, argData, argData.Length);

            if (sendRemote)
            {
                var writer = NetWriterPool.Get();
                writer.WriteUInt32(target.NetworkId);
                writer.WriteUInt32(methodHash);
                writer.WriteBytesRaw(argData, 0, argData.Length);

                switch (targets)
                {
                    case RPCTarget.All:
                    case RPCTarget.Others:
                        Router.SendToAll(MSG_RPC, writer, PacketReliability.ReliableOrdered, 1);
                        break;
                    case RPCTarget.Host:
                        if (!IsHost)
                        {
                            var hostPuid = GetHostPuid();
                            if (hostPuid != null)
                                Router.SendToPeer(MSG_RPC, writer, hostPuid, PacketReliability.ReliableOrdered, 1);
                        }
                        break;
                    case RPCTarget.Owner:
                        if (!target.IsOwner && target.OwnerId != null)
                            Router.SendToPeer(MSG_RPC, writer, target.OwnerId, PacketReliability.ReliableOrdered, 1);
                        break;
                    case RPCTarget.Players:
                        SendToNonSpectators(MSG_RPC, writer);
                        break;
                }

                NetWriterPool.Return(writer);
            }
        }

        /// <summary>
        /// Send a pre-serialized RPC to a specific peer. Called by weaver-generated code.
        /// </summary>
        public void SendRPCWeavedToPeer(NetworkObject target, uint methodHash, ProductUserId peer, byte[] argData)
        {
            if (target == null || !target.IsRegistered) return;
            if (peer == null) return;

            var localPuid = EOSManager.Instance?.LocalProductUserId;
            if (localPuid != null && peer == localPuid)
            {
                ExecuteRPCLocal(target.NetworkId, methodHash, argData, argData.Length);
                return;
            }

            var writer = NetWriterPool.Get();
            writer.WriteUInt32(target.NetworkId);
            writer.WriteUInt32(methodHash);
            writer.WriteBytesRaw(argData, 0, argData.Length);
            Router.SendToPeer(MSG_RPC, writer, peer, PacketReliability.ReliableOrdered, 1);
            NetWriterPool.Return(writer);
        }

        /// <summary>Unregister all RPCs for a NetworkObject.</summary>
        public void UnregisterRPCs(NetworkObject target)
        {
            _rpcKeysToRemove.Clear();
            foreach (var key in _rpcHandlers.Keys)
            {
                if (key.NetworkId == target.NetworkId)
                    _rpcKeysToRemove.Add(key);
            }
            foreach (var key in _rpcKeysToRemove)
            {
                _rpcHandlers.Remove(key);
                _rpcMethodNames.Remove(key);
            }
        }

        /// <summary>
        /// Convenience: sets OnRPCValidation to only allow RPCs from the object's owner.
        /// Rejects any RPC where the sender is not the target object's OwnerId.
        /// </summary>
        public void EnableOwnerOnlyRPCValidation()
        {
            OnRPCValidation = (sender, target, hash) => target != null && target.OwnerId == sender;
        }

        #endregion

        #region Private Fields

        private readonly Dictionary<uint, NetworkObject> _objects = new();
        private readonly List<NetworkObject> _dirtyObjects = new();
        private readonly Dictionary<RPCKey, Action<NetReader>> _rpcHandlers = new();
        private readonly Dictionary<RPCKey, string> _rpcMethodNames = new();
        private readonly List<RPCKey> _rpcKeysToRemove = new();
        private readonly Dictionary<ProductUserId, NetworkPlayerState> _playerStates = new();
        private readonly HashSet<ProductUserId> _spectators = new();

        private ushort _localIdCounter;
        private ushort _localIdPrefix;
        private bool _routerSubscribed;

        // Object pooling: per-prefab pools of deactivated GameObjects
        [Tooltip("Enable object pooling for spawn/despawn (reduces GC pressure)")]
        [SerializeField] private bool _enablePooling = true;
        private readonly Dictionary<ushort, Queue<GameObject>> _pools = new();

        // Reliable fallback tracking
        private const float RELIABLE_FALLBACK_DELAY = 0.2f; // 200ms
        private readonly List<NetworkObject> _reliableFallbackObjects = new();

        // Host migration RPC buffer
        private bool _migrationInProgress;
        private readonly List<BufferedRPC> _migrationBuffer = new();

        private struct BufferedRPC
        {
            public NetworkObject Target;
            public string MethodName;   // null for weaved RPCs
            public uint MethodHash;     // pre-computed for weaved RPCs
            public RPCTarget Targets;
            public byte[] ArgData;
        }

        // Host-validated RPCs: per-RPC validator methods registered by weaver
        // Key: methodHash, Value: validator func (sender, target, argData) → bool
        private readonly Dictionary<uint, Func<ProductUserId, NetworkObject, byte[], bool>> _rpcValidators = new();
        // Track which method hashes require host validation
        private readonly HashSet<uint> _validatedRpcHashes = new();

        private MessageRouter Router => EOSP2PManager.Instance.Router;

        private struct RPCKey : IEquatable<RPCKey>
        {
            public uint NetworkId;
            public uint MethodHash;

            public bool Equals(RPCKey other) => NetworkId == other.NetworkId && MethodHash == other.MethodHash;
            public override bool Equals(object obj) => obj is RPCKey other && Equals(other);
            public override int GetHashCode() => (int)(NetworkId * 397 ^ MethodHash);
        }

        #endregion

        #region Unity Lifecycle

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
            SubscribeRouter();

            var p2p = EOSP2PManager.Instance;
            p2p.OnPeerConnected += OnPeerConnected;
            p2p.OnPeerDisconnected += OnPeerDisconnected;
        }

        private void OnDisable()
        {
            var p2p = EOSP2PManager.Instance;
            if (p2p != null)
            {
                p2p.OnPeerConnected -= OnPeerConnected;
                p2p.OnPeerDisconnected -= OnPeerDisconnected;
            }
        }

        private void LateUpdate()
        {
            if (_dirtyObjects.Count > 0)
                SendStateUpdates();

            // Check for reliable fallback — resend state reliably if not re-dirtied
            CheckReliableFallback();
        }

        #endregion

        #region Router Setup

        private void SubscribeRouter()
        {
            if (_routerSubscribed) return;
            _routerSubscribed = true;

            // Router.ProcessIncoming is auto-subscribed by EOSP2PManager.Router getter — no manual wiring needed
            Router.Register(MSG_STATE_UPDATE, HandleStateUpdate);
            Router.Register(MSG_SPAWN, HandleSpawn);
            Router.Register(MSG_DESPAWN, HandleDespawn);
            Router.Register(MSG_AUTHORITY, HandleAuthority);
            Router.Register(MSG_SNAPSHOT, HandleSnapshot);
            Router.Register(MSG_SNAPSHOT_REQUEST, HandleSnapshotRequest);
            Router.Register(MSG_RPC, HandleRPC);
            Router.Register(MSG_RPC_VALIDATED, HandleRPCValidated);
            Router.Register(MSG_RPC_REBROADCAST, HandleRPCRebroadcast);
            Router.Register(MSG_AUTHORITY_REQUEST, HandleAuthorityRequest);

            // Scene management messages
            var sceneMgr = NetworkSceneManager.Instance;
            Router.Register(MSG_SCENE_LOAD, sceneMgr.HandleSceneLoad);
            Router.Register(MSG_SCENE_UNLOAD, sceneMgr.HandleSceneUnload);
            Router.Register(MSG_SCENE_LOADED_ACK, sceneMgr.HandleSceneLoadedAck);

            RecomputeHost();
        }

        #endregion

        #region Internal Callbacks

        /// <summary>Called by NetworkObject when it has dirty SyncVars.</summary>
        internal void OnObjectDirty(NetworkObject obj)
        {
            if (!_dirtyObjects.Contains(obj))
                _dirtyObjects.Add(obj);
        }

        /// <summary>Called by NetworkObject.OnDestroy when a registered object is destroyed.</summary>
        internal void OnObjectDestroyed(NetworkObject obj)
        {
            _objects.Remove(obj.NetworkId);
            _dirtyObjects.Remove(obj);
            UnregisterRPCs(obj);

            // Clean up RoomState/PlayerState references
            if (obj.PrefabId == NetworkRoomState.PREFAB_ID && RoomState != null && RoomState.Net == obj)
                RoomState = null;
            else if (obj.PrefabId == NetworkPlayerState.PREFAB_ID && obj.OwnerId != null)
            {
                _playerStates.Remove(obj.OwnerId);
                if (LocalPlayerState != null && LocalPlayerState.Net == obj)
                    LocalPlayerState = null;
            }
        }

        #endregion

        #region State Sync

        private void SendStateUpdates()
        {
            // First pass: collect valid dirty objects and count them
            int validCount = 0;
            for (int i = _dirtyObjects.Count - 1; i >= 0; i--)
            {
                var obj = _dirtyObjects[i];
                if (obj == null || !obj.IsRegistered || !obj.IsOwner)
                    _dirtyObjects.RemoveAt(i);
                else
                    validCount++;
            }

            if (validCount == 0)
            {
                _dirtyObjects.Clear();
                return;
            }

            var writer = NetWriterPool.Get();
            writer.WritePackedUInt32((uint)validCount);

            for (int i = 0; i < _dirtyObjects.Count; i++)
            {
                var obj = _dirtyObjects[i];

                writer.WriteUInt32(obj.NetworkId);

                // Increment sequence for stale-packet detection
                obj.SyncSequence++;
                writer.WriteUInt16(obj.SyncSequence);

                // Write data with length prefix so receivers can skip unknown objects
                var dataWriter = NetWriterPool.Get();
                obj.SerializeDirty(dataWriter);
                var data = dataWriter.ToArraySegment();
                writer.WriteUInt16((ushort)data.Count);
                writer.WriteBytesRaw(data);
                NetWriterPool.Return(dataWriter);

                obj.ClearDirty();

                // Mark for reliable fallback
                obj.LastUnreliableSendTime = Time.time;
                obj.ReliableFallbackPending = true;
                if (!_reliableFallbackObjects.Contains(obj))
                    _reliableFallbackObjects.Add(obj);
            }

            _dirtyObjects.Clear();

            Router.SendToAll(MSG_STATE_UPDATE, writer, PacketReliability.UnreliableUnordered, 0);
            NetWriterPool.Return(writer);
        }

        private void HandleStateUpdate(ProductUserId sender, NetReader reader)
        {
            uint count = reader.ReadPackedUInt32();
            for (uint i = 0; i < count; i++)
            {
                uint networkId = reader.ReadUInt32();
                ushort sequence = reader.ReadUInt16();
                ushort dataLen = reader.ReadUInt16();

                if (_objects.TryGetValue(networkId, out var obj))
                {
                    // Sender validation: only the owner can update an object's state
                    if (obj.OwnerId != null && !sender.Equals(obj.OwnerId))
                    {
                        reader.Skip(dataLen);
                        continue;
                    }

                    // BufferLast: discard stale/out-of-order packets
                    // Uses wrapping comparison: seq is "newer" if (seq - last) as signed short > 0
                    short diff = (short)(sequence - obj.LastReceivedSequence);
                    if (diff > 0)
                    {
                        obj.LastReceivedSequence = sequence;
                        obj.DeserializeDirty(reader);
                    }
                    else
                    {
                        // Stale packet — skip data
                        reader.Skip(dataLen);
                    }
                }
                else
                {
                    // Skip unknown object data
                    reader.Skip(dataLen);
                }
            }
        }

        /// <summary>
        /// If an object was sent unreliable and hasn't been re-dirtied within 200ms,
        /// resend its full state via reliable SNAPSHOT. Guarantees eventual consistency.
        /// Uses SNAPSHOT format (WriteFullState) which handles SyncLists correctly.
        /// </summary>
        private void CheckReliableFallback()
        {
            float now = Time.time;
            for (int i = _reliableFallbackObjects.Count - 1; i >= 0; i--)
            {
                var obj = _reliableFallbackObjects[i];

                // Object was re-dirtied or destroyed — no fallback needed
                if (obj == null || !obj.IsRegistered || !obj.IsOwner || obj.IsDirty || !obj.ReliableFallbackPending)
                {
                    _reliableFallbackObjects.RemoveAt(i);
                    continue;
                }

                // Wait for the delay
                if (now - obj.LastUnreliableSendTime < RELIABLE_FALLBACK_DELAY) continue;

                // Send a mini-SNAPSHOT with just this object (reliable delivery, correct format)
                obj.ReliableFallbackPending = false;
                _reliableFallbackObjects.RemoveAt(i);

                var writer = NetWriterPool.Get();
                writer.WritePackedUInt32(1); // 1 object
                WriteSpawnData(writer, obj);

                Router.SendToAll(MSG_SNAPSHOT, writer, PacketReliability.ReliableOrdered, 1);
                NetWriterPool.Return(writer);
            }
        }

        #endregion

        #region Spawn / Despawn Messages

        private void BroadcastSpawn(NetworkObject obj)
        {
            var writer = NetWriterPool.Get();
            WriteSpawnData(writer, obj);
            Router.SendToAll(MSG_SPAWN, writer, PacketReliability.ReliableOrdered, 1);
            NetWriterPool.Return(writer);
        }

        private void WriteSpawnData(NetWriter writer, NetworkObject obj)
        {
            writer.WriteUInt16(obj.PrefabId);
            writer.WriteUInt32(obj.NetworkId);
            writer.WriteProductUserId(obj.OwnerId);
            writer.WriteVector3(obj.transform.position);
            writer.WriteQuaternion(obj.transform.rotation);
            writer.WriteBool(obj.DestroyWithOwner);
            writer.WriteByte((byte)obj.SyncVarCount);
            obj.SerializeAll(writer);
        }

        private void HandleSpawn(ProductUserId sender, NetReader reader)
        {
            ushort prefabId = reader.ReadUInt16();
            uint networkId = reader.ReadUInt32();
            ProductUserId ownerId = reader.ReadProductUserId();
            Vector3 position = reader.ReadVector3();
            Quaternion rotation = reader.ReadQuaternion();
            bool destroyWithOwner = reader.ReadBool();
            byte syncVarCount = reader.ReadByte();

            // Sender validation: only accept spawns from the claimed owner
            if (ownerId != null && !sender.Equals(ownerId))
            {
                EOSDebugLogger.LogWarning(DebugCategory.EOSManager, "NetworkManager",
                    $"Rejected spawn: sender {sender} != claimed owner {ownerId} for object {networkId}");
                return;
            }

            // Don't re-spawn if we already have it (e.g. we're the owner)
            if (_objects.ContainsKey(networkId)) return;

            // Handle reserved PrefabIds (RoomState / PlayerState)
            if (prefabId == NetworkRoomState.PREFAB_ID || prefabId == NetworkPlayerState.PREFAB_ID)
            {
                SpawnReservedObject(prefabId, networkId, ownerId, destroyWithOwner, syncVarCount, reader);
                return;
            }

            var prefab = GetPrefab(prefabId);
            if (prefab == null)
            {
                Debug.LogWarning($"[NetworkManager] Received spawn for unknown prefab {prefabId}");
                return;
            }

            var go = GetFromPool(prefabId, prefab, position, rotation);
            var netObj = go.GetComponent<NetworkObject>();
            if (netObj == null)
                netObj = go.AddComponent<NetworkObject>();

            netObj.NetworkId = networkId;
            netObj.PrefabId = prefabId;
            netObj.OwnerId = ownerId;
            netObj.DestroyWithOwner = destroyWithOwner;
            netObj.IsRegistered = true;
            _objects[networkId] = netObj;

            // Read SyncVar state
            if (syncVarCount > 0 && netObj.SyncVarCount > 0)
                netObj.DeserializeAll(reader);

            netObj.NotifyNetworkSpawn();

            EOSDebugLogger.Log(DebugCategory.EOSManager, "NetworkManager",
                $"Remote spawn: object {networkId} (prefab {prefabId}, owner {ownerId})");
        }

        private void HandleDespawn(ProductUserId sender, NetReader reader)
        {
            uint networkId = reader.ReadUInt32();

            if (_objects.TryGetValue(networkId, out var obj))
            {
                // Sender validation: only the owner or host can despawn an object
                if (obj.OwnerId != null && !sender.Equals(obj.OwnerId) && !sender.Equals(GetHostPuid()))
                {
                    EOSDebugLogger.LogWarning(DebugCategory.EOSManager, "NetworkManager",
                        $"Rejected despawn: sender {sender} is not owner/host for object {networkId}");
                    return;
                }

                ushort prefabId = obj.PrefabId;
                _objects.Remove(networkId);
                _dirtyObjects.Remove(obj);
                obj.IsRegistered = false;
                obj.NotifyNetworkDespawn();
                UnregisterRPCs(obj);
                ReturnToPool(prefabId, obj.gameObject);

                EOSDebugLogger.Log(DebugCategory.EOSManager, "NetworkManager",
                    $"Remote despawn: object {networkId}");
            }
        }

        #endregion

        #region Authority Transfer

        private void HandleAuthority(ProductUserId sender, NetReader reader)
        {
            uint networkId = reader.ReadUInt32();
            ProductUserId newOwnerId = reader.ReadProductUserId();

            if (_objects.TryGetValue(networkId, out var obj))
            {
                // Sender validation: only the current owner or host can transfer authority
                if (obj.OwnerId != null && !sender.Equals(obj.OwnerId) && !sender.Equals(GetHostPuid()))
                {
                    EOSDebugLogger.LogWarning(DebugCategory.EOSManager, "NetworkManager",
                        $"Rejected authority transfer: sender {sender} is not owner/host for object {networkId}");
                    return;
                }

                var oldOwner = obj.OwnerId;
                obj.OwnerId = newOwnerId;
                obj.NotifyOwnerChanged(oldOwner, newOwnerId);

                EOSDebugLogger.Log(DebugCategory.EOSManager, "NetworkManager",
                    $"Authority transfer: object {networkId} -> {newOwnerId}");
            }
        }

        /// <summary>Transfer ownership of a NetworkObject to another peer.</summary>
        public void TransferAuthority(NetworkObject obj, ProductUserId newOwner)
        {
            if (obj == null || !obj.IsRegistered) return;
            if (!obj.IsOwner && !IsHost) return;

            var oldOwner = obj.OwnerId;
            obj.OwnerId = newOwner;
            obj.NotifyOwnerChanged(oldOwner, newOwner);

            var writer = NetWriterPool.Get();
            writer.WriteUInt32(obj.NetworkId);
            writer.WriteProductUserId(newOwner);
            Router.SendToAll(MSG_AUTHORITY, writer, PacketReliability.ReliableOrdered, 1);
            NetWriterPool.Return(writer);
        }

        /// <summary>
        /// Request ownership of a NetworkObject. Sent to the host, who auto-approves by default.
        /// Override <see cref="OnAuthorityRequested"/> to add custom validation (e.g. distance checks, cooldowns).
        /// </summary>
        public void RequestAuthority(NetworkObject obj)
        {
            if (obj == null || !obj.IsRegistered) return;
            if (obj.IsOwner) return; // Already own it

            // If we're the host, approve locally
            if (IsHost)
            {
                var localPuid = EOSManager.Instance?.LocalProductUserId;
                if (localPuid != null)
                    TransferAuthority(obj, localPuid);
                return;
            }

            // Send request to host
            var writer = NetWriterPool.Get();
            writer.WriteUInt32(obj.NetworkId);
            var hostPuid = GetHostPuid();
            if (hostPuid != null)
                Router.SendToPeer(MSG_AUTHORITY_REQUEST, writer, hostPuid, PacketReliability.ReliableOrdered, 1);
            NetWriterPool.Return(writer);
        }

        /// <summary>
        /// Called on the host when a peer requests authority.
        /// Override to add custom validation. Return false to deny.
        /// Default: auto-approve all requests.
        /// </summary>
        public Func<NetworkObject, ProductUserId, bool> OnAuthorityRequested;

        private void HandleAuthorityRequest(ProductUserId sender, NetReader reader)
        {
            if (!IsHost) return;

            uint networkId = reader.ReadUInt32();
            if (!_objects.TryGetValue(networkId, out var obj)) return;

            // Check with custom validator, default to auto-approve
            bool approved = OnAuthorityRequested?.Invoke(obj, sender) ?? true;

            if (approved)
            {
                TransferAuthority(obj, sender);
                EOSDebugLogger.Log(DebugCategory.EOSManager, "NetworkManager",
                    $"Authority request approved: object {networkId} -> {sender}");
            }
            else
            {
                EOSDebugLogger.Log(DebugCategory.EOSManager, "NetworkManager",
                    $"Authority request denied: object {networkId} from {sender}");
            }
        }

        #endregion

        #region Snapshot (Late Join)

        private void HandleSnapshotRequest(ProductUserId sender, NetReader reader)
        {
            if (!IsHost) return;

            // Priority-ordered chunked snapshot delivery:
            // 1. RoomState first (so late joiners know game state immediately)
            // 2. All PlayerStates (so late joiners know about all players)
            // 3. Remaining objects
            var ordered = new List<NetworkObject>(_objects.Count);

            // Priority 1: RoomState
            if (RoomState != null && RoomState.Net.IsRegistered)
                ordered.Add(RoomState.Net);

            // Priority 2: PlayerStates
            foreach (var ps in _playerStates.Values)
            {
                if (ps != null && ps.Net.IsRegistered)
                    ordered.Add(ps.Net);
            }

            // Priority 3: Everything else
            foreach (var obj in _objects.Values)
            {
                if (obj == null || !obj.IsRegistered) continue;
                // Skip already-added RoomState and PlayerStates
                if (obj.PrefabId == NetworkRoomState.PREFAB_ID || obj.PrefabId == NetworkPlayerState.PREFAB_ID)
                    continue;
                ordered.Add(obj);
            }

            EOSDebugLogger.Log(DebugCategory.EOSManager, "NetworkManager",
                $"Sending chunked snapshot to {sender} ({ordered.Count} objects, {(ordered.Count + SNAPSHOT_CHUNK_SIZE - 1) / SNAPSHOT_CHUNK_SIZE} chunks)");

            // Send in chunks of SNAPSHOT_CHUNK_SIZE
            int offset = 0;
            while (offset < ordered.Count)
            {
                int chunkCount = Math.Min(SNAPSHOT_CHUNK_SIZE, ordered.Count - offset);

                var writer = NetWriterPool.Get();
                writer.WritePackedUInt32((uint)chunkCount);

                for (int i = 0; i < chunkCount; i++)
                    WriteSpawnData(writer, ordered[offset + i]);

                Router.SendToPeer(MSG_SNAPSHOT, writer, sender, PacketReliability.ReliableOrdered, 1);
                NetWriterPool.Return(writer);

                offset += chunkCount;
            }

            // Send an empty sentinel chunk if there are no objects (so receiver knows snapshot is complete)
            if (ordered.Count == 0)
            {
                var writer = NetWriterPool.Get();
                writer.WritePackedUInt32(0);
                Router.SendToPeer(MSG_SNAPSHOT, writer, sender, PacketReliability.ReliableOrdered, 1);
                NetWriterPool.Return(writer);
            }
        }

        private void HandleSnapshot(ProductUserId sender, NetReader reader)
        {
            var hostPuid = GetHostPuid();
            bool senderIsHost = hostPuid != null && sender.Equals(hostPuid);

            uint count = reader.ReadPackedUInt32();

            EOSDebugLogger.Log(DebugCategory.EOSManager, "NetworkManager",
                $"Received snapshot chunk with {count} objects from {sender}");

            for (uint i = 0; i < count; i++)
            {
                ushort prefabId = reader.ReadUInt16();
                uint networkId = reader.ReadUInt32();
                ProductUserId ownerId = reader.ReadProductUserId();
                Vector3 position = reader.ReadVector3();
                Quaternion rotation = reader.ReadQuaternion();
                bool destroyWithOwner = reader.ReadBool();
                byte syncVarCount = reader.ReadByte();

                // Sender validation: accept from host (full snapshot) or owner (reliable fallback)
                if (!senderIsHost && ownerId != null && !sender.Equals(ownerId))
                {
                    EOSDebugLogger.LogWarning(DebugCategory.EOSManager, "NetworkManager",
                        $"Snapshot: sender {sender} is not owner/host for object {networkId}, skipping remainder");
                    return; // Can't safely skip variable-length SyncVar data — bail on entire snapshot
                }

                if (_objects.ContainsKey(networkId))
                {
                    // Already have this object — update state
                    var existing = _objects[networkId];
                    if (syncVarCount > 0 && existing.SyncVarCount > 0)
                        existing.DeserializeAll(reader);
                    continue;
                }

                // Handle reserved PrefabIds (RoomState / PlayerState)
                if (prefabId == NetworkRoomState.PREFAB_ID || prefabId == NetworkPlayerState.PREFAB_ID)
                {
                    SpawnReservedObject(prefabId, networkId, ownerId, destroyWithOwner, syncVarCount, reader);
                    continue;
                }

                var prefab = GetPrefab(prefabId);
                if (prefab == null)
                {
                    Debug.LogWarning($"[NetworkManager] Snapshot: unknown prefab {prefabId}");
                    continue;
                }

                var go = GetFromPool(prefabId, prefab, position, rotation);
                var netObj = go.GetComponent<NetworkObject>();
                if (netObj == null)
                    netObj = go.AddComponent<NetworkObject>();

                netObj.NetworkId = networkId;
                netObj.PrefabId = prefabId;
                netObj.OwnerId = ownerId;
                netObj.DestroyWithOwner = destroyWithOwner;
                netObj.IsRegistered = true;
                _objects[networkId] = netObj;

                if (syncVarCount > 0 && netObj.SyncVarCount > 0)
                    netObj.DeserializeAll(reader);

                netObj.NotifyNetworkSpawn();
            }

            // After snapshot chunks contain our RoomState, ensure our PlayerState exists
            if (RoomState != null)
            {
                EnsureLocalPlayerState();
                NetworkSceneManager.Instance?.SyncScenesFromRoomState();
            }
        }

        private void RequestSnapshot()
        {
            var writer = NetWriterPool.Get();
            // Empty payload — just the msgId
            var hostPuid = GetHostPuid();
            if (hostPuid != null)
                Router.SendToPeer(MSG_SNAPSHOT_REQUEST, writer, hostPuid, PacketReliability.ReliableOrdered, 1);
            NetWriterPool.Return(writer);

            EOSDebugLogger.Log(DebugCategory.EOSManager, "NetworkManager", "Requesting snapshot from host");
        }

        #endregion

        #region RPC Handling

        private void HandleRPC(ProductUserId sender, NetReader reader)
        {
            uint networkId = reader.ReadUInt32();
            uint methodHash = reader.ReadUInt32();
            // Args are left in the reader for the handler to consume
            // (argCount + typeId + value pairs)

            // Validate incoming RPC if callback is set
            if (OnRPCValidation != null)
            {
                _objects.TryGetValue(networkId, out var targetObj);
                if (!OnRPCValidation(sender, targetObj, methodHash))
                {
                    Debug.LogWarning($"[NetworkManager] RPC rejected: sender={sender}, object={networkId}, hash=0x{methodHash:X8}");
                    return;
                }
            }

            var key = new RPCKey { NetworkId = networkId, MethodHash = methodHash };
            if (_rpcHandlers.TryGetValue(key, out var handler))
            {
                try
                {
                    handler(reader);
                }
                catch (Exception ex)
                {
                    Debug.LogError($"[NetworkManager] RPC handler error on object {networkId}: {ex.Message}");
                }
            }
        }

        private void ExecuteRPCLocal(uint networkId, uint methodHash, byte[] argData, int argDataLen)
        {
            var key = new RPCKey { NetworkId = networkId, MethodHash = methodHash };
            if (!_rpcHandlers.TryGetValue(key, out var handler)) return;

            // Create a reader from the same raw arg bytes that remote peers receive
            var reader = new NetReader(argData, 0, argDataLen);
            try
            {
                handler(reader);
            }
            catch (Exception ex)
            {
                Debug.LogError($"[NetworkManager] Local RPC error on object {networkId}: {ex.Message}");
            }
        }

        /// <summary>
        /// Host receives this when a client sends a [NetRpc(Validated = true)] RPC.
        /// The host runs the validator and, if approved, rebroadcasts to all peers.
        /// Wire format: [networkId:u32][methodHash:u32][originalTarget:u8][argData...]
        /// </summary>
        private void HandleRPCValidated(ProductUserId sender, NetReader reader)
        {
            if (!IsHost) return; // Only the host handles validated RPCs

            uint networkId = reader.ReadUInt32();
            uint methodHash = reader.ReadUInt32();
            byte originalTarget = reader.ReadByte();
            byte[] argData = reader.ReadBytesRemaining();

            _objects.TryGetValue(networkId, out var targetObj);

            // Run per-method validator (if registered), otherwise auto-approve
            if (!RunRPCValidator(methodHash, sender, targetObj, argData))
            {
                Debug.LogWarning($"[NetworkManager] Validated RPC rejected: sender={sender}, object={networkId}, hash=0x{methodHash:X8}");
                return;
            }

            // Approved — rebroadcast to all peers (including back to sender)
            var writer = NetWriterPool.Get();
            writer.WriteUInt32(networkId);
            writer.WriteUInt32(methodHash);
            writer.WriteByte(originalTarget);
            writer.WriteBytesRaw(argData, 0, argData.Length);
            Router.SendToAll(MSG_RPC_REBROADCAST, writer, PacketReliability.ReliableOrdered, 1);
            NetWriterPool.Return(writer);

            // Also execute on host per target rules
            var targets = (RPCTarget)originalTarget;
            bool executeOnHost = targets switch
            {
                RPCTarget.All => true,
                RPCTarget.Others => false, // "Others" from sender's perspective — host IS an "other"
                RPCTarget.Host => true,
                RPCTarget.Owner => targetObj != null && targetObj.IsOwner,
                RPCTarget.Players => !IsSpectator,
                _ => false,
            };

            // For RPCTarget.Others, host should still execute (sender meant "all except me")
            if (targets == RPCTarget.Others)
                executeOnHost = true;

            if (executeOnHost)
                ExecuteRPCLocal(networkId, methodHash, argData, argData.Length);
        }

        /// <summary>
        /// All peers receive this when the host rebroadcasts an approved validated RPC.
        /// Wire format: [networkId:u32][methodHash:u32][originalTarget:u8][argData...]
        /// </summary>
        private void HandleRPCRebroadcast(ProductUserId sender, NetReader reader)
        {
            // Only accept rebroadcasts from the host
            var hostPuid = GetHostPuid();
            if (hostPuid == null || !sender.Equals(hostPuid))
            {
                Debug.LogWarning($"[NetworkManager] Rejected RPC rebroadcast from non-host {sender}");
                return;
            }

            uint networkId = reader.ReadUInt32();
            uint methodHash = reader.ReadUInt32();
            byte originalTarget = reader.ReadByte();
            byte[] argData = reader.ReadBytesRemaining();

            // Check if we should execute based on the original target
            var targets = (RPCTarget)originalTarget;
            var localPuid = EOSManager.Instance?.LocalProductUserId;
            _objects.TryGetValue(networkId, out var targetObj);

            bool execute = targets switch
            {
                RPCTarget.All => true,
                RPCTarget.Others => true, // We're an "other" — the sender already excluded themselves
                RPCTarget.Host => IsHost,
                RPCTarget.Owner => targetObj != null && targetObj.IsOwner,
                RPCTarget.Players => !IsSpectator,
                _ => false,
            };

            if (execute)
                ExecuteRPCLocal(networkId, methodHash, argData, argData.Length);
        }

        /// <summary>
        /// Run the per-method validator for a validated RPC. Returns true if approved.
        /// If no validator is registered for this hash, auto-approves (relay-only mode).
        /// </summary>
        private bool RunRPCValidator(uint methodHash, ProductUserId sender, NetworkObject target, byte[] argData)
        {
            if (_rpcValidators.TryGetValue(methodHash, out var validator))
            {
                try
                {
                    return validator(sender, target, argData);
                }
                catch (Exception ex)
                {
                    Debug.LogError($"[NetworkManager] RPC validator error for hash 0x{methodHash:X8}: {ex.Message}");
                    return false; // Reject on validator error
                }
            }
            return true; // No validator = auto-approve (relay-only)
        }

        #endregion

        #region Host Election

        private void RecomputeHost()
        {
            var localPuid = EOSManager.Instance?.LocalProductUserId;
            if (localPuid == null) return;

            string localStr = localPuid.ToString();
            string lowestStr = IsSpectator ? null : localStr;

            var peers = EOSP2PManager.Instance?.Peers;
            if (peers != null)
            {
                foreach (var peer in peers)
                {
                    if (_spectators.Contains(peer)) continue;
                    string peerStr = peer.ToString();
                    if (lowestStr == null || string.Compare(peerStr, lowestStr, StringComparison.Ordinal) < 0)
                        lowestStr = peerStr;
                }
            }

            // If all peers are spectators, fall back to lowest PUID anyway (with warning)
            if (lowestStr == null)
            {
                Debug.LogWarning("[NetworkManager] All peers are spectators — lowest PUID becomes host");
                lowestStr = localStr;
                if (peers != null)
                {
                    foreach (var peer in peers)
                    {
                        string peerStr = peer.ToString();
                        if (string.Compare(peerStr, lowestStr, StringComparison.Ordinal) < 0)
                            lowestStr = peerStr;
                    }
                }
            }

            bool wasHost = IsHost;
            IsHost = (lowestStr == localStr);

            if (IsHost != wasHost)
            {
                EOSDebugLogger.Log(DebugCategory.EOSManager, "NetworkManager",
                    IsHost ? "Became host" : "No longer host");

                if (IsHost)
                {
                    // When we become host, claim any ownerless scene objects
                    RegisterSceneObjects();

                    // Ensure RoomState exists (may have been created by previous host)
                    EnsureRoomState();
                }
            }
        }

        private ProductUserId GetHostPuid()
        {
            var localPuid = EOSManager.Instance?.LocalProductUserId;
            if (localPuid == null) return null;

            string lowestStr = IsSpectator ? null : localPuid.ToString();
            ProductUserId lowestPuid = IsSpectator ? null : localPuid;

            var peers = EOSP2PManager.Instance?.Peers;
            if (peers != null)
            {
                foreach (var peer in peers)
                {
                    if (_spectators.Contains(peer)) continue;
                    string peerStr = peer.ToString();
                    if (lowestStr == null || string.Compare(peerStr, lowestStr, StringComparison.Ordinal) < 0)
                    {
                        lowestStr = peerStr;
                        lowestPuid = peer;
                    }
                }
            }

            // Fallback: if all spectators, return lowest PUID anyway
            if (lowestPuid == null)
            {
                lowestStr = localPuid.ToString();
                lowestPuid = localPuid;
                if (peers != null)
                {
                    foreach (var peer in peers)
                    {
                        string peerStr = peer.ToString();
                        if (string.Compare(peerStr, lowestStr, StringComparison.Ordinal) < 0)
                        {
                            lowestStr = peerStr;
                            lowestPuid = peer;
                        }
                    }
                }
            }

            return lowestPuid;
        }

        #endregion

        #region Peer Events

        private void OnPeerConnected(ProductUserId peer)
        {
            RecomputeHost();

            // Initialize local ID prefix if not yet set
            InitLocalIdPrefix();

            // If we're the host, ensure RoomState exists and create our PlayerState
            if (IsHost)
            {
                EnsureRoomState();
                EnsureLocalPlayerState();

                // Update player count
                if (RoomState != null && RoomState.IsOwner)
                {
                    var peerList = EOSP2PManager.Instance?.Peers;
                    RoomState.PlayerCount.Value = (peerList?.Count ?? 0) + 1; // +1 for self
                }
            }

            // If we're not the host but just connected, request a snapshot
            // (our PlayerState will be created after we receive the snapshot)
            if (!IsHost && _objects.Count == 0)
            {
                RequestSnapshot();
            }
            else if (!IsHost)
            {
                // Already have objects but new peer joined — ensure our PlayerState
                EnsureLocalPlayerState();
            }
        }

        private void OnPeerDisconnected(ProductUserId peer)
        {
            // Enter migration window — buffer host/owner-targeted RPCs
            _migrationInProgress = true;

            RecomputeHost();

            // If we became the host, claim orphaned objects
            if (IsHost)
            {
                ClaimOrphanedObjects(peer);

                // Update player count on room state
                if (RoomState != null && RoomState.IsOwner)
                {
                    var peers = EOSP2PManager.Instance?.Peers;
                    RoomState.PlayerCount.Value = (peers?.Count ?? 0) + 1; // +1 for self
                }
            }

            // Clean up disconnected player's state from registry
            CleanupPlayerState(peer);

            // End migration window and flush buffered RPCs
            _migrationInProgress = false;
            FlushMigrationBuffer();
        }

        private void FlushMigrationBuffer()
        {
            if (_migrationBuffer.Count == 0) return;

            EOSDebugLogger.Log(DebugCategory.EOSManager, "NetworkManager",
                $"Flushing {_migrationBuffer.Count} buffered RPCs after migration");

            for (int i = 0; i < _migrationBuffer.Count; i++)
            {
                var buffered = _migrationBuffer[i];
                if (buffered.Target == null || !buffered.Target.IsRegistered) continue;

                // Weaved RPCs have MethodName=null and MethodHash pre-computed
                uint nameHash = buffered.MethodName != null ? FnvHash(buffered.MethodName) : buffered.MethodHash;

                // Determine if this should execute locally now
                bool executeLocal = false;
                bool sendRemote = false;

                switch (buffered.Targets)
                {
                    case RPCTarget.Host:
                        if (IsHost) executeLocal = true;
                        else sendRemote = true;
                        break;
                    case RPCTarget.Owner:
                        if (buffered.Target.IsOwner) executeLocal = true;
                        else sendRemote = true;
                        break;
                    case RPCTarget.All:
                        executeLocal = true;
                        sendRemote = true;
                        break;
                    case RPCTarget.Others:
                        sendRemote = true;
                        break;
                    case RPCTarget.Players:
                        executeLocal = !IsSpectator;
                        sendRemote = true;
                        break;
                }

                if (executeLocal)
                    ExecuteRPCLocal(buffered.Target.NetworkId, nameHash, buffered.ArgData, buffered.ArgData.Length);

                if (sendRemote)
                {
                    var writer = NetWriterPool.Get();
                    writer.WriteUInt32(buffered.Target.NetworkId);
                    writer.WriteUInt32(nameHash);
                    writer.WriteBytesRaw(buffered.ArgData, 0, buffered.ArgData.Length);

                    switch (buffered.Targets)
                    {
                        case RPCTarget.All:
                        case RPCTarget.Others:
                            Router.SendToAll(MSG_RPC, writer, PacketReliability.ReliableOrdered, 1);
                            break;
                        case RPCTarget.Host:
                            var hostPuid = GetHostPuid();
                            if (hostPuid != null)
                                Router.SendToPeer(MSG_RPC, writer, hostPuid, PacketReliability.ReliableOrdered, 1);
                            break;
                        case RPCTarget.Owner:
                            if (buffered.Target.OwnerId != null)
                                Router.SendToPeer(MSG_RPC, writer, buffered.Target.OwnerId, PacketReliability.ReliableOrdered, 1);
                            break;
                        case RPCTarget.Players:
                            SendToNonSpectators(MSG_RPC, writer);
                            break;
                    }

                    NetWriterPool.Return(writer);
                }
            }

            _migrationBuffer.Clear();
        }

        private void ClaimOrphanedObjects(ProductUserId disconnectedPeer)
        {
            var localPuid = EOSManager.Instance?.LocalProductUserId;
            if (localPuid == null) return;

            // Collect orphaned objects first (can't modify _objects during iteration)
            var orphans = new List<NetworkObject>();
            foreach (var obj in _objects.Values)
            {
                if (obj.OwnerId == disconnectedPeer)
                    orphans.Add(obj);
            }

            foreach (var obj in orphans)
            {
                // DestroyWithOwner: despawn instead of claiming (e.g. player avatars)
                if (obj.DestroyWithOwner)
                {
                    EOSDebugLogger.Log(DebugCategory.EOSManager, "NetworkManager",
                        $"Despawning object {obj.NetworkId} (DestroyWithOwner) — owner {disconnectedPeer} left");
                    Despawn(obj);
                    continue;
                }

                // Transfer ownership to new host
                var oldOwner = obj.OwnerId;
                obj.OwnerId = localPuid;
                obj.NotifyOwnerChanged(oldOwner, localPuid);

                // Broadcast authority change
                var writer = NetWriterPool.Get();
                writer.WriteUInt32(obj.NetworkId);
                writer.WriteProductUserId(localPuid);
                Router.SendToAll(MSG_AUTHORITY, writer, PacketReliability.ReliableOrdered, 1);
                NetWriterPool.Return(writer);

                EOSDebugLogger.Log(DebugCategory.EOSManager, "NetworkManager",
                    $"Claimed orphaned object {obj.NetworkId} from {disconnectedPeer}");
            }
        }

        #endregion

        #region Network ID Generation

        private void InitLocalIdPrefix()
        {
            if (_localIdPrefix != 0) return;

            var localPuid = EOSManager.Instance?.LocalProductUserId;
            if (localPuid == null) return;

            string puidStr = localPuid.ToString();
            _localIdPrefix = (ushort)(FnvHash(puidStr) & 0xFFFF);
            if (_localIdPrefix == 0) _localIdPrefix = 1; // avoid zero prefix
        }

        private uint GenerateNetworkId()
        {
            InitLocalIdPrefix();
            uint id = ((uint)_localIdPrefix << 16) | _localIdCounter;
            _localIdCounter++;
            return id;
        }

        #endregion

        #region Object Pooling

        private GameObject GetFromPool(ushort prefabId, GameObject prefab, Vector3 position, Quaternion rotation)
        {
            if (_enablePooling && _pools.TryGetValue(prefabId, out var pool) && pool.Count > 0)
            {
                var go = pool.Dequeue();
                go.transform.SetPositionAndRotation(position, rotation);
                go.SetActive(true);
                return go;
            }
            return Instantiate(prefab, position, rotation);
        }

        private void ReturnToPool(ushort prefabId, GameObject go)
        {
            if (!_enablePooling || prefabId == 0)
            {
                go.SetActive(false);
                return;
            }

            go.SetActive(false);
            if (!_pools.TryGetValue(prefabId, out var pool))
            {
                pool = new Queue<GameObject>();
                _pools[prefabId] = pool;
            }
            pool.Enqueue(go);
        }

        /// <summary>Pre-warm the pool for a prefab ID. Instantiates count objects and deactivates them.</summary>
        public void Prewarm(ushort prefabId, int count)
        {
            var prefab = GetPrefab(prefabId);
            if (prefab == null) return;

            if (!_pools.TryGetValue(prefabId, out var pool))
            {
                pool = new Queue<GameObject>();
                _pools[prefabId] = pool;
            }

            for (int i = 0; i < count; i++)
            {
                var go = Instantiate(prefab);
                go.SetActive(false);
                pool.Enqueue(go);
            }
        }

        #endregion

        #region RoomState / PlayerState

        /// <summary>
        /// Create a NetworkObject from a reserved PrefabId (RoomState or PlayerState).
        /// These are not in the prefab registry — they're created as empty GameObjects
        /// with the correct component attached.
        /// </summary>
        private void SpawnReservedObject(ushort prefabId, uint networkId, ProductUserId ownerId,
            bool destroyWithOwner, byte syncVarCount, NetReader reader)
        {
            string objName;
            GameObject go;

            if (prefabId == NetworkRoomState.PREFAB_ID)
            {
                objName = "NetworkRoomState";
                go = new GameObject(objName);
                if (EOSManager.Instance != null)
                    go.transform.SetParent(EOSManager.Instance.transform);
                else
                    DontDestroyOnLoad(go);

                var roomState = go.AddComponent<NetworkRoomState>();
                var netObj = roomState.Net;
                netObj.NetworkId = networkId;
                netObj.PrefabId = prefabId;
                netObj.OwnerId = ownerId;
                netObj.DestroyWithOwner = destroyWithOwner;
                netObj.IsRegistered = true;
                _objects[networkId] = netObj;

                if (syncVarCount > 0 && netObj.SyncVarCount > 0)
                    netObj.DeserializeAll(reader);

                RoomState = roomState;
                netObj.NotifyNetworkSpawn();

                EOSDebugLogger.Log(DebugCategory.EOSManager, "NetworkManager",
                    $"RoomState received from {ownerId} (id={networkId})");
            }
            else if (prefabId == NetworkPlayerState.PREFAB_ID)
            {
                objName = $"PlayerState_{ownerId}";
                go = new GameObject(objName);
                if (EOSManager.Instance != null)
                    go.transform.SetParent(EOSManager.Instance.transform);
                else
                    DontDestroyOnLoad(go);

                var playerState = go.AddComponent<NetworkPlayerState>();
                var netObj = playerState.Net;
                netObj.NetworkId = networkId;
                netObj.PrefabId = prefabId;
                netObj.OwnerId = ownerId;
                netObj.DestroyWithOwner = destroyWithOwner;
                netObj.IsRegistered = true;
                _objects[networkId] = netObj;

                if (syncVarCount > 0 && netObj.SyncVarCount > 0)
                    netObj.DeserializeAll(reader);

                if (ownerId != null)
                    _playerStates[ownerId] = playerState;

                // Track spectator status from custom data
                if (ownerId != null && playerState.GetCustomBool("_spectator"))
                    _spectators.Add(ownerId);

                // Check if this is our own player state
                var localPuid = EOSManager.Instance?.LocalProductUserId;
                if (localPuid != null && ownerId == localPuid)
                    LocalPlayerState = playerState;

                netObj.NotifyNetworkSpawn();

                EOSDebugLogger.Log(DebugCategory.EOSManager, "NetworkManager",
                    $"PlayerState received for {ownerId} (id={networkId})");
            }
        }

        /// <summary>
        /// Ensures the RoomState object exists. Called on the host after first peer connects.
        /// </summary>
        private void EnsureRoomState()
        {
            if (!IsHost) return;
            if (RoomState != null) return;

            var localPuid = EOSManager.Instance?.LocalProductUserId;
            if (localPuid == null) return;

            var go = new GameObject("NetworkRoomState");
            if (EOSManager.Instance != null)
                go.transform.SetParent(EOSManager.Instance.transform);
            else
                DontDestroyOnLoad(go);

            var roomState = go.AddComponent<NetworkRoomState>();
            var netObj = roomState.Net;
            netObj.NetworkId = NetworkRoomState.WELL_KNOWN_ID;
            netObj.PrefabId = NetworkRoomState.PREFAB_ID;
            netObj.OwnerId = localPuid;
            netObj.DestroyWithOwner = false;
            netObj.IsRegistered = true;
            _objects[netObj.NetworkId] = netObj;
            RoomState = roomState;
            netObj.NotifyNetworkSpawn();

            // Broadcast spawn to all peers
            BroadcastSpawn(netObj);

            EOSDebugLogger.Log(DebugCategory.EOSManager, "NetworkManager",
                "RoomState created by host");
        }

        /// <summary>
        /// Ensures the local player's PlayerState object exists. Called on peer connect.
        /// </summary>
        private void EnsureLocalPlayerState()
        {
            if (LocalPlayerState != null) return;

            var localPuid = EOSManager.Instance?.LocalProductUserId;
            if (localPuid == null) return;

            uint networkId = GenerateNetworkId();

            var go = new GameObject($"PlayerState_{localPuid}");
            if (EOSManager.Instance != null)
                go.transform.SetParent(EOSManager.Instance.transform);
            else
                DontDestroyOnLoad(go);

            var playerState = go.AddComponent<NetworkPlayerState>();
            var netObj = playerState.Net;
            netObj.NetworkId = networkId;
            netObj.PrefabId = NetworkPlayerState.PREFAB_ID;
            netObj.OwnerId = localPuid;
            netObj.DestroyWithOwner = true;
            netObj.IsRegistered = true;
            _objects[networkId] = netObj;
            _playerStates[localPuid] = playerState;
            LocalPlayerState = playerState;
            netObj.NotifyNetworkSpawn();

            // Initialize display name from registry
            playerState.AutoInitDisplayName();

            // Mark as spectator if local peer is spectating
            if (IsSpectator)
                playerState.SetCustom("_spectator", "1");

            // Broadcast spawn to all peers
            BroadcastSpawn(netObj);

            EOSDebugLogger.Log(DebugCategory.EOSManager, "NetworkManager",
                $"Local PlayerState created (id={networkId})");
        }

        /// <summary>
        /// Clean up a player state when its owner disconnects.
        /// Called from ClaimOrphanedObjects when DestroyWithOwner is true.
        /// </summary>
        private void CleanupPlayerState(ProductUserId disconnectedPeer)
        {
            if (_playerStates.TryGetValue(disconnectedPeer, out var ps))
            {
                _playerStates.Remove(disconnectedPeer);
                _spectators.Remove(disconnectedPeer);
                EOSDebugLogger.Log(DebugCategory.EOSManager, "NetworkManager",
                    $"PlayerState removed for disconnected peer {disconnectedPeer}");
            }
        }

        #endregion

        #region Utilities

        /// <summary>Send a message to all non-spectator peers via Router.</summary>
        private void SendToNonSpectators(byte msgId, NetWriter writer)
        {
            var peers = EOSP2PManager.Instance?.Peers;
            if (peers == null) return;
            foreach (var peer in peers)
            {
                if (!_spectators.Contains(peer))
                    Router.SendToPeer(msgId, writer, peer, PacketReliability.ReliableOrdered, 1);
            }
        }

        /// <summary>FNV-1a hash of a string. Used for method name hashing in RPCs.</summary>
        public static uint FnvHash(string str)
        {
            if (string.IsNullOrEmpty(str)) return 0;

            uint hash = 2166136261u;
            for (int i = 0; i < str.Length; i++)
            {
                hash ^= str[i];
                hash *= 16777619u;
            }
            return hash;
        }

        /// <summary>Register a NetworkObject that was created outside of Spawn() (e.g. scene objects, runtime-created balls).</summary>
        public void RegisterExisting(NetworkObject obj, uint networkId)
        {
            obj.NetworkId = networkId;
            obj.IsRegistered = true;
            _objects[networkId] = obj;
            obj.NotifyNetworkSpawn();
        }

        /// <summary>
        /// Find all NetworkObjects already in the scene and register them.
        /// Ownerless objects are automatically assigned to the current host.
        /// Called automatically when host status changes. Can also be called manually after scene load.
        /// </summary>
        public void RegisterSceneObjects()
        {
            var localPuid = EOSManager.Instance?.LocalProductUserId;
            if (localPuid == null) return;

            var sceneObjects = FindObjectsByType<NetworkObject>(FindObjectsSortMode.None);
            foreach (var obj in sceneObjects)
            {
                if (obj.IsRegistered) continue;

                // Generate a deterministic NetworkId from scene hierarchy path
                uint sceneNetId = 0xFFFF0000u | (FnvHash(GetHierarchyPath(obj.transform)) & 0xFFFFu);

                // Avoid collisions with existing objects
                while (_objects.ContainsKey(sceneNetId))
                    sceneNetId++;

                obj.NetworkId = sceneNetId;
                obj.IsRegistered = true;
                _objects[sceneNetId] = obj;

                // Auto-assign ownerless scene objects to host
                if (obj.OwnerId == null && IsHost)
                {
                    obj.OwnerId = localPuid;

                    // Broadcast ownership to peers
                    var peers = EOSP2PManager.Instance?.Peers;
                    if (peers != null && peers.Count > 0)
                    {
                        var writer = NetWriterPool.Get();
                        writer.WriteUInt32(obj.NetworkId);
                        writer.WriteProductUserId(localPuid);
                        Router.SendToAll(MSG_AUTHORITY, writer, PacketReliability.ReliableOrdered, 1);
                        NetWriterPool.Return(writer);
                    }

                    EOSDebugLogger.Log(DebugCategory.EOSManager, "NetworkManager",
                        $"Scene object '{obj.name}' ({sceneNetId}) auto-assigned to host");
                }

                obj.NotifyNetworkSpawn();
            }
        }

        private static string GetHierarchyPath(Transform t)
        {
            string path = t.name;
            while (t.parent != null)
            {
                t = t.parent;
                path = t.name + "/" + path;
            }
            return path;
        }

        #endregion
    }

    /// <summary>
    /// Determines who receives an RPC.
    /// </summary>
    public enum RPCTarget
    {
        /// <summary>Send to all peers including self.</summary>
        All,

        /// <summary>Send to all peers excluding self.</summary>
        Others,

        /// <summary>Send to the current host only.</summary>
        Host,

        /// <summary>Send to the object's owner only.</summary>
        Owner,

        /// <summary>Send to all non-spectator peers (including self if not spectator).</summary>
        Players = 4,
    }
}
