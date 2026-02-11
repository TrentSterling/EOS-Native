using System;
using System.Collections.Generic;
using Epic.OnlineServices;
using Epic.OnlineServices.P2P;
using EOSNative.Lobbies;
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

        // Runtime reparenting
        private const byte MSG_REPARENT = 0xAF;

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

        /// <summary>True if this peer is the host (lowest PUID among all connected peers + self). Always true in offline mode.</summary>
        public bool IsHost { get; private set; }

        /// <summary>
        /// When true, the networking layer runs entirely locally without EOS, P2P, or lobby connections.
        /// All RPCs execute locally, SyncVars work but aren't sent, spawns are local-only.
        /// You are always the host in offline mode. Set before OnEnable or call StartOfflineMode().
        /// </summary>
        public bool OfflineMode { get; private set; }

        /// <summary>Set of NetworkIds owned by the local player in offline mode (no ProductUserId available).</summary>
        private readonly HashSet<uint> _offlineOwnedNetworkIds = new();

        /// <summary>All active NetworkObjects, keyed by NetworkId.</summary>
        public IReadOnlyDictionary<uint, NetworkObject> Objects => _objects;

        /// <summary>
        /// Optional RPC validation callback. If set, called before executing any incoming remote RPC.
        /// Return true to allow, false to reject. Null = all RPCs allowed (default, zero overhead).
        /// Parameters: (sender ProductUserId, target NetworkObject, methodHash uint) → bool.
        /// </summary>
        public Func<ProductUserId, NetworkObject, uint, bool> OnRPCValidation;

        /// <summary>
        /// Optional SyncVar write validation callback. If set, called before applying any incoming
        /// state update to a NetworkObject. Return true to allow, false to reject.
        /// Null = default validation (checks SyncVarWriteAccess rules automatically).
        /// Parameters: (sender ProductUserId, target NetworkObject) → bool.
        /// </summary>
        public Func<ProductUserId, NetworkObject, bool> OnSyncVarWrite;

        /// <summary>
        /// Fired when this peer's host status changes (true = became host, false = lost host).
        /// Used by SimulationBehaviour for OnBecameHost/OnLostHost callbacks.
        /// </summary>
        public event Action<bool> OnHostChanged;

        /// <summary>
        /// Maximum messages accepted per peer per second. 0 = unlimited (default).
        /// Excess messages from a peer are silently dropped for that second.
        /// </summary>
        public int MaxMessagesPerPeerPerSecond { get; set; }

        /// <summary>
        /// When true, broadcast paths (state updates, spawn, despawn, authority, RPCs) are
        /// filtered through InterestManager so each peer only receives data about nearby objects.
        /// Objects marked AlwaysVisible, NetworkRoomState, NetworkPlayerState, and owner's own
        /// objects bypass the filter. Requires an InterestManager component in the scene.
        /// Default: false (all objects broadcast to all peers).
        /// </summary>
        public bool InterestManagementEnabled { get; set; }

        /// <summary>
        /// True when tick-based simulation is active. When enabled, state updates are sent
        /// at the tick rate instead of every frame. Read-only — configure via TickSimulation.Instance.TickRate.
        /// </summary>
        public bool IsTickBased => TickSimulation.Instance != null && TickSimulation.Instance.IsEnabled;

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

        /// <summary>
        /// Start offline mode. The networking layer runs entirely locally — no EOS login,
        /// no P2P connections, no lobby required. You are always the host. RPCs execute locally,
        /// SyncVars work but aren't transmitted. Call this instead of joining a lobby for single-player
        /// or testing scenarios.
        /// </summary>
        public void StartOfflineMode()
        {
            if (OfflineMode) return;
            OfflineMode = true;
            IsHost = true;
            _localIdPrefix = 0xFFFF;
            _localIdCounter = 1;

            // Create RoomState and PlayerState (host responsibility)
            EnsureRoomState();
            EnsureLocalPlayerState();

            EOSDebugLogger.Log(DebugCategory.EOSManager, "NetworkManager", "Offline mode started");
        }

        /// <summary>
        /// Stop offline mode and clean up offline-only state. Call before going online
        /// (joining a lobby). Does NOT despawn objects — call DespawnAll() first if needed.
        /// </summary>
        public void StopOfflineMode()
        {
            if (!OfflineMode) return;
            OfflineMode = false;
            _offlineOwnedNetworkIds.Clear();
            _localIdPrefix = 0;
            _localIdCounter = 0;

            EOSDebugLogger.Log(DebugCategory.EOSManager, "NetworkManager", "Offline mode stopped");
        }

        /// <summary>
        /// Check if a NetworkObject is owned locally in offline mode.
        /// Used by NetworkObject.IsOwner as a fallback when OwnerId is null.
        /// </summary>
        internal bool IsLocallyOwnedOffline(uint networkId)
        {
            return OfflineMode && _offlineOwnedNetworkIds.Contains(networkId);
        }

        #endregion

        #region Prefab Registry

        [SerializeField] private NetworkPrefabTable _prefabTable;

        /// <summary>
        /// Optional ScriptableObject prefab table. Assign in Inspector to auto-register prefabs on startup.
        /// Index in the table = PrefabId. Table entries merge with runtime RegisterPrefab() calls.
        /// </summary>
        public NetworkPrefabTable PrefabTable { get => _prefabTable; set => _prefabTable = value; }

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

        /// <summary>
        /// Look up the PrefabId for a registered prefab. Returns -1 if not found.
        /// </summary>
        public int GetPrefabId(GameObject prefab)
        {
            for (int i = 0; i < _prefabs.Count; i++)
            {
                if (_prefabs[i] == prefab) return i;
            }
            return -1;
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

            ProductUserId localPuid = null;
            if (!OfflineMode)
            {
                localPuid = EOSManager.Instance?.LocalProductUserId;
                if (localPuid == null)
                {
                    Debug.LogError("[NetworkManager] Cannot spawn — not logged in");
                    return null;
                }
            }

            uint networkId = GenerateNetworkId();

            var go = GetFromPool(prefabId, prefab, position, rotation);

            // Discover all NetworkObjects in the hierarchy (root at index 0)
            var allNetObjs = go.GetComponentsInChildren<NetworkObject>(true);
            var netObj = allNetObjs.Length > 0 ? allNetObjs[0] : null;
            if (netObj == null)
                netObj = go.AddComponent<NetworkObject>();

            // Register root
            netObj.NetworkId = networkId;
            netObj.PrefabId = prefabId;
            netObj.OwnerId = localPuid; // null in offline mode
            netObj.ParentNetworkId = 0;
            netObj.IsRegistered = true;
            _objects[networkId] = netObj;

            if (OfflineMode)
                _offlineOwnedNetworkIds.Add(networkId);

            netObj.NotifyNetworkSpawn();

            // Register child NetworkObjects (index 1+)
            var origChildren = new List<(uint, byte)>();
            for (int i = 1; i < allNetObjs.Length; i++)
            {
                var child = allNetObjs[i];
                uint childId = GenerateNetworkId();
                child.NetworkId = childId;
                child.PrefabId = prefabId;
                child.OwnerId = localPuid;
                child.ParentNetworkId = networkId;
                child.OriginalParentNetworkId = networkId;
                child.IsRegistered = true;
                _objects[childId] = child;
                origChildren.Add((childId, (byte)i));

                if (OfflineMode)
                    _offlineOwnedNetworkIds.Add(childId);

                child.NotifyNetworkSpawn();
            }
            if (origChildren.Count > 0)
                _originalChildren[networkId] = origChildren;

            // Broadcast spawn to all peers (skip in offline mode)
            if (!OfflineMode)
                BroadcastSpawn(netObj);

            int childCount = allNetObjs.Length - 1;
            EOSDebugLogger.Log(DebugCategory.EOSManager, "NetworkManager",
                $"Spawned object {networkId} (prefab {prefabId}, {childCount} children){(OfflineMode ? " [offline]" : "")}");

            return netObj;
        }

        /// <summary>
        /// Spawn a networked object by prefab reference. Auto-registers the prefab if not already registered.
        /// </summary>
        public NetworkObject Spawn(GameObject prefab, Vector3 position, Quaternion rotation)
        {
            int id = GetPrefabId(prefab);
            if (id < 0)
            {
                id = _prefabs.Count;
                RegisterPrefab(prefab, (ushort)id);
            }
            return Spawn((ushort)id, position, rotation);
        }

        /// <summary>
        /// Spawn a networked object by prefab name. The prefab must already be registered.
        /// </summary>
        public NetworkObject Spawn(string prefabName, Vector3 position, Quaternion rotation)
        {
            for (int i = 0; i < _prefabs.Count; i++)
            {
                if (_prefabs[i] != null && _prefabs[i].name == prefabName)
                    return Spawn((ushort)i, position, rotation);
            }
            Debug.LogError($"[NetworkManager] No prefab registered with name '{prefabName}'");
            return null;
        }

        /// <summary>
        /// Despawn a networked object. Deactivates it locally and broadcasts DESPAWN.
        /// The GameObject is deactivated, not destroyed (pooling-ready).
        /// </summary>
        public void Despawn(NetworkObject obj)
        {
            if (obj == null) return;
            if (!obj.IsRegistered) return;

            // Block direct child despawn — unless it was detached (ParentNetworkId == 0)
            // Original children that are still attached must be despawned via the root.
            if (obj.IsChildNetworkObject)
            {
                Debug.LogWarning($"[NetworkManager] Cannot despawn child NetworkObject {obj.NetworkId} directly — despawn the root instead");
                return;
            }

            // Only owner or host can despawn
            if (!obj.IsOwner && !IsHost) return;

            // Despawn children first (before removing root from _objects)
            var children = GetRegisteredChildren(obj.NetworkId);
            for (int i = 0; i < children.Count; i++)
            {
                var child = children[i];
                _objects.Remove(child.NetworkId);
                _dirtyObjects.Remove(child);
                _offlineOwnedNetworkIds.Remove(child.NetworkId);
                child.IsRegistered = false;
                child.NotifyNetworkDespawn();
                UnregisterRPCs(child);
                if (InterestManagementEnabled)
                    InterestManager.Instance?.OnObjectRemoved(child.NetworkId);
            }

            uint networkId = obj.NetworkId;
            ushort prefabId = obj.PrefabId;
            _objects.Remove(networkId);
            _dirtyObjects.Remove(obj);
            _offlineOwnedNetworkIds.Remove(networkId);
            obj.IsRegistered = false;
            obj.NotifyNetworkDespawn();
            UnregisterRPCs(obj);
            ReturnToPool(prefabId, obj.gameObject);

            // Clean up _originalChildren for this root
            _originalChildren.Remove(networkId);

            // If this was a detached child, remove from its original root's list
            if (obj.OriginalParentNetworkId != 0)
                RemoveFromOriginalChildren(obj.OriginalParentNetworkId, networkId);

            if (!OfflineMode)
            {
                // Broadcast despawn for root only — receiver cascades children
                var writer = NetWriterPool.Get();
                writer.WriteUInt32(networkId);
                SendToInterestedPeers(MSG_DESPAWN, writer, obj, PacketReliability.ReliableOrdered, 1);
                NetWriterPool.Return(writer);
            }

            // Clean up from interest manager
            if (InterestManagementEnabled)
                InterestManager.Instance?.OnObjectRemoved(networkId);

            EOSDebugLogger.Log(DebugCategory.EOSManager, "NetworkManager",
                $"Despawned object {networkId} (+ {children.Count} children)");
        }

        /// <summary>
        /// Despawn all registered NetworkObjects owned by this peer. Call before StopOfflineMode()
        /// to clean up objects, or use for a full session reset.
        /// </summary>
        public void DespawnAll()
        {
            var toRemove = new List<NetworkObject>();
            foreach (var obj in _objects.Values)
            {
                // Only collect roots — Despawn() cascades to children
                if (obj != null && obj.IsRegistered && obj.IsRootNetworkObject && (obj.IsOwner || IsHost))
                    toRemove.Add(obj);
            }
            for (int i = 0; i < toRemove.Count; i++)
                Despawn(toRemove[i]);
            _originalChildren.Clear();
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
                    ComponentIndex = NetworkObject.SELF_COMPONENT_INDEX,
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

            // Offline mode: always execute locally, never send remote
            if (OfflineMode)
            {
                // In offline mode, treat all targets as local execution
                ExecuteRPCLocal(target.NetworkId, nameHash, argData, argData.Length);
                return;
            }

            if (executeLocal)
                ExecuteRPCLocal(target.NetworkId, nameHash, argData, argData.Length);

            if (sendRemote)
            {
                var writer = NetWriterPool.Get();
                writer.WriteUInt32(target.NetworkId);
                writer.WriteByte(NetworkObject.SELF_COMPONENT_INDEX);
                writer.WriteUInt32(nameHash);
                writer.WriteBytesRaw(argData, 0, argData.Length);

                switch (targets)
                {
                    case RPCTarget.All:
                    case RPCTarget.Others:
                        SendToInterestedPeers(MSG_RPC, writer, target, PacketReliability.ReliableOrdered, 1);
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
                        SendToInterestedNonSpectators(MSG_RPC, writer, target);
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

            // Offline mode: execute locally (no peers exist)
            if (OfflineMode)
            {
                var offHash = FnvHash(methodName);
                var offArgs = SerializeRPCArgs(args);
                if (offArgs != null)
                    ExecuteRPCLocal(target.NetworkId, offHash, offArgs, offArgs.Length);
                return;
            }

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
            writer.WriteByte(NetworkObject.SELF_COMPONENT_INDEX);
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

            // Offline mode: execute locally (no peers exist)
            if (OfflineMode)
            {
                var offHash = FnvHash(methodName);
                var offArgs = SerializeRPCArgs(args);
                if (offArgs != null)
                    ExecuteRPCLocal(target.NetworkId, offHash, offArgs, offArgs.Length);
                return;
            }

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
                writer.WriteByte(NetworkObject.SELF_COMPONENT_INDEX);
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
        /// Uses SELF_COMPONENT_INDEX (0xFF) — for runtime string-based RPCs without component scoping.
        /// The handler receives a NetReader positioned after the method hash — read args from it.
        /// </summary>
        public void RegisterRPC(NetworkObject target, string methodName, Action<NetReader> handler)
        {
            uint nameHash = FnvHash(methodName);
            var key = new RPCKey { NetworkId = target.NetworkId, ComponentIndex = NetworkObject.SELF_COMPONENT_INDEX, MethodHash = nameHash };

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
        /// Register an RPC handler by pre-computed hash with component scoping.
        /// Called by weaver-generated __RegisterNetRPCs() on NetworkBehaviour subclasses.
        /// </summary>
        public void RegisterRPC(NetworkObject target, byte componentIndex, uint methodHash, string methodName, Action<NetReader> handler)
        {
            var key = new RPCKey { NetworkId = target.NetworkId, ComponentIndex = componentIndex, MethodHash = methodHash };

            if (_rpcHandlers.ContainsKey(key))
            {
                if (_rpcMethodNames.TryGetValue(key, out string existing) && existing != methodName)
                    throw new InvalidOperationException(
                        $"RPC hash collision: '{methodName}' collides with '{existing}' on object {target.NetworkId} component {componentIndex}");
            }

            _rpcHandlers[key] = handler;
            _rpcMethodNames[key] = methodName;
        }

        /// <summary>
        /// Legacy overload for backward compatibility. Uses SELF_COMPONENT_INDEX.
        /// Called by weaver-generated __RegisterNetRPCs() on NetworkObject subclasses.
        /// </summary>
        public void RegisterRPC(NetworkObject target, uint methodHash, string methodName, Action<NetReader> handler)
        {
            RegisterRPC(target, NetworkObject.SELF_COMPONENT_INDEX, methodHash, methodName, handler);
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
        /// Send a validated RPC through the host with component scoping.
        /// Called by weaver for [NetRpc(Validated = true)].
        /// Client sends to host only. Host validates, then rebroadcasts to all.
        /// </summary>
        public void SendRPCValidated(NetworkObject target, byte componentIndex, uint methodHash, RPCTarget originalTarget, byte[] argData)
        {
            if (target == null || !target.IsRegistered) return;

            // Offline mode: skip validation overhead, execute locally
            if (OfflineMode)
            {
                ExecuteRPCLocal(target.NetworkId, componentIndex, methodHash, argData, argData.Length);
                return;
            }

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
                    ExecuteRPCLocal(target.NetworkId, componentIndex, methodHash, argData, argData.Length);

                // Rebroadcast to interested peers using MSG_RPC_REBROADCAST
                var writer = NetWriterPool.Get();
                writer.WriteUInt32(target.NetworkId);
                writer.WriteByte(componentIndex);
                writer.WriteUInt32(methodHash);
                writer.WriteByte((byte)originalTarget);
                writer.WriteBytesRaw(argData, 0, argData.Length);
                SendToInterestedPeers(MSG_RPC_REBROADCAST, writer, target, PacketReliability.ReliableOrdered, 1);
                NetWriterPool.Return(writer);
            }
            else
            {
                // Send to host for validation via MSG_RPC_VALIDATED
                var hostPuid = GetHostPuid();
                if (hostPuid == null) return;

                var writer = NetWriterPool.Get();
                writer.WriteUInt32(target.NetworkId);
                writer.WriteByte(componentIndex);
                writer.WriteUInt32(methodHash);
                writer.WriteByte((byte)originalTarget);
                writer.WriteBytesRaw(argData, 0, argData.Length);
                Router.SendToPeer(MSG_RPC_VALIDATED, writer, hostPuid, PacketReliability.ReliableOrdered, 1);
                NetWriterPool.Return(writer);
            }
        }

        /// <summary>
        /// Legacy overload without component index. Uses SELF_COMPONENT_INDEX.
        /// </summary>
        public void SendRPCValidated(NetworkObject target, uint methodHash, RPCTarget originalTarget, byte[] argData)
        {
            SendRPCValidated(target, NetworkObject.SELF_COMPONENT_INDEX, methodHash, originalTarget, argData);
        }

        /// <summary>
        /// Send a pre-serialized RPC with component scoping. Called by weaver-generated dispatch stubs.
        /// Args are already packed into argData by the generated serialization code.
        /// </summary>
        public void SendRPCWeaved(NetworkObject target, byte componentIndex, uint methodHash, RPCTarget targets, byte[] argData)
        {
            if (target == null || !target.IsRegistered) return;

            // Offline mode: always execute locally, never send remote
            if (OfflineMode)
            {
                ExecuteRPCLocal(target.NetworkId, componentIndex, methodHash, argData, argData.Length);
                return;
            }

            // Buffer host/owner-targeted RPCs during migration window
            if (_migrationInProgress && (targets == RPCTarget.Host || targets == RPCTarget.Owner))
            {
                _migrationBuffer.Add(new BufferedRPC
                {
                    Target = target,
                    MethodName = null,
                    ComponentIndex = componentIndex,
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
                ExecuteRPCLocal(target.NetworkId, componentIndex, methodHash, argData, argData.Length);

            if (sendRemote)
            {
                var writer = NetWriterPool.Get();
                writer.WriteUInt32(target.NetworkId);
                writer.WriteByte(componentIndex);
                writer.WriteUInt32(methodHash);
                writer.WriteBytesRaw(argData, 0, argData.Length);

                switch (targets)
                {
                    case RPCTarget.All:
                    case RPCTarget.Others:
                        SendToInterestedPeers(MSG_RPC, writer, target, PacketReliability.ReliableOrdered, 1);
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
                        SendToInterestedNonSpectators(MSG_RPC, writer, target);
                        break;
                }

                NetWriterPool.Return(writer);
            }
        }

        /// <summary>
        /// Legacy overload without component index. Uses SELF_COMPONENT_INDEX.
        /// </summary>
        public void SendRPCWeaved(NetworkObject target, uint methodHash, RPCTarget targets, byte[] argData)
        {
            SendRPCWeaved(target, NetworkObject.SELF_COMPONENT_INDEX, methodHash, targets, argData);
        }

        /// <summary>
        /// Send a pre-serialized RPC to a specific peer. Called by weaver-generated code.
        /// </summary>
        public void SendRPCWeavedToPeer(NetworkObject target, uint methodHash, ProductUserId peer, byte[] argData)
        {
            if (target == null || !target.IsRegistered) return;

            // Offline mode: execute locally (no peers exist)
            if (OfflineMode)
            {
                ExecuteRPCLocal(target.NetworkId, NetworkObject.SELF_COMPONENT_INDEX, methodHash, argData, argData.Length);
                return;
            }

            if (peer == null) return;

            var localPuid = EOSManager.Instance?.LocalProductUserId;
            if (localPuid != null && peer == localPuid)
            {
                ExecuteRPCLocal(target.NetworkId, NetworkObject.SELF_COMPONENT_INDEX, methodHash, argData, argData.Length);
                return;
            }

            var writer = NetWriterPool.Get();
            writer.WriteUInt32(target.NetworkId);
            writer.WriteByte(NetworkObject.SELF_COMPONENT_INDEX);
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

        /// <summary>
        /// Convenience: sets OnSyncVarWrite to only allow state updates from the object's owner.
        /// Ignores SyncVarWriteAccess levels — all SyncVars become strictly owner-only.
        /// </summary>
        public void EnableOwnerOnlySyncVarValidation()
        {
            OnSyncVarWrite = (sender, target) => target != null && target.OwnerId == sender;
        }

        /// <summary>
        /// Convenience: sets OnSyncVarWrite to allow the owner OR the host to write state.
        /// Useful for host-authoritative games where the host may override any object's state.
        /// </summary>
        public void EnableOwnerOrHostSyncVarValidation()
        {
            OnSyncVarWrite = (sender, target) =>
            {
                if (target == null) return false;
                if (target.OwnerId == sender) return true;
                var hostPuid = GetHostPuid();
                return hostPuid != null && sender.Equals(hostPuid);
            };
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

        // Reconnect hibernation: objects whose owner disconnected survive for a grace period
        [Tooltip("Seconds to keep PersistOnDisconnect objects alive after owner disconnect. 0 = disabled.")]
        [SerializeField] private float _reconnectGracePeriod = 30f;
        public float ReconnectGracePeriod { get => _reconnectGracePeriod; set => _reconnectGracePeriod = value; }

        internal readonly Dictionary<string, HibernatedPeer> _hibernatedPeers = new();

        internal struct HibernatedPeer
        {
            public string Puid;
            public float ExpireTime;
            public List<uint> OwnedObjectIds;
        }

        private struct BufferedRPC
        {
            public NetworkObject Target;
            public string MethodName;   // null for weaved RPCs
            public byte ComponentIndex;
            public uint MethodHash;     // pre-computed for weaved RPCs
            public RPCTarget Targets;
            public byte[] ArgData;
        }

        // Per-peer message rate limiting
        private readonly Dictionary<ProductUserId, int> _peerMessageCounts = new();
        private float _rateLimitResetTime;

        // Host-validated RPCs: per-RPC validator methods registered by weaver
        // Key: methodHash, Value: validator func (sender, target, argData) → bool
        private readonly Dictionary<uint, Func<ProductUserId, NetworkObject, byte[], bool>> _rpcValidators = new();
        // Track which method hashes require host validation
        private readonly HashSet<uint> _validatedRpcHashes = new();

        // Interest management: reusable buffer for filtered peer lists
        private readonly List<ProductUserId> _interestedPeersBuffer = new();

        // Nested NetworkObject: reusable buffer for child discovery
        private readonly List<NetworkObject> _childrenBuffer = new();

        // Original spawn-time children per root: rootNetId → [(childNetId, localIndex)]
        // Used for reparenting: tracks original prefab hierarchy regardless of runtime Transform changes.
        private readonly Dictionary<uint, List<(uint childNetId, byte localIndex)>> _originalChildren = new();

        // Tick simulation subscription tracking
        private bool _tickSubscribed;

        private MessageRouter Router => EOSP2PManager.Instance.Router;

        private struct RPCKey : IEquatable<RPCKey>
        {
            public uint NetworkId;
            public byte ComponentIndex;
            public uint MethodHash;

            public bool Equals(RPCKey other) => NetworkId == other.NetworkId && ComponentIndex == other.ComponentIndex && MethodHash == other.MethodHash;
            public override bool Equals(object obj) => obj is RPCKey other && Equals(other);
            public override int GetHashCode() => (int)(NetworkId * 397 ^ (uint)(ComponentIndex << 24) ^ MethodHash);
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
            // Auto-register prefabs from table (if assigned)
            if (_prefabTable != null)
            {
                for (int i = 0; i < _prefabTable.Count; i++)
                {
                    var prefab = _prefabTable.GetPrefab(i);
                    if (prefab != null)
                        RegisterPrefab(prefab, (ushort)i);
                }
            }

            if (!OfflineMode)
            {
                SubscribeRouter();

                var p2p = EOSP2PManager.Instance;
                p2p.OnPeerConnected += OnPeerConnected;
                p2p.OnPeerDisconnected += OnPeerDisconnected;

                // Recompute host when lobby is joined (covers solo-in-lobby case where
                // no peers connect and OnPeerConnected never fires)
                var lobby = EOSLobbyManager.Instance;
                if (lobby != null)
                    lobby.OnLobbyJoined += OnLobbyJoinedRecomputeHost;

                // Subscribe to interest enter/exit for dynamic spawn/despawn
                var im = InterestManager.Instance;
                if (im != null)
                {
                    im.OnInterestEnter += OnInterestEnter;
                    im.OnInterestExit += OnInterestExit;
                }
            }

            // Subscribe to tick simulation if active
            SubscribeTickSimulation();
        }

        private void OnDisable()
        {
            var p2p = EOSP2PManager.Instance;
            if (p2p != null)
            {
                p2p.OnPeerConnected -= OnPeerConnected;
                p2p.OnPeerDisconnected -= OnPeerDisconnected;
            }

            var lobby = EOSLobbyManager.Instance;
            if (lobby != null)
                lobby.OnLobbyJoined -= OnLobbyJoinedRecomputeHost;

            var im = InterestManager.Instance;
            if (im != null)
            {
                im.OnInterestEnter -= OnInterestEnter;
                im.OnInterestExit -= OnInterestExit;
            }

            UnsubscribeTickSimulation();
        }

        private void SubscribeTickSimulation()
        {
            var tick = TickSimulation.Instance;
            if (tick != null && tick.IsEnabled)
            {
                tick.OnPostTick -= OnSimulationPostTick;
                tick.OnPostTick += OnSimulationPostTick;
                _tickSubscribed = true;
            }
        }

        private void UnsubscribeTickSimulation()
        {
            if (_tickSubscribed)
            {
                var tick = TickSimulation.Instance;
                if (tick != null)
                    tick.OnPostTick -= OnSimulationPostTick;
                _tickSubscribed = false;
            }
        }

        private void LateUpdate()
        {
            // When tick simulation is active, state updates are driven by OnPostTick instead
            if (!_tickSubscribed)
            {
                // Frame-based path (tick system disabled or not yet active)
                if (_dirtyObjects.Count > 0)
                    SendStateUpdates();

                CheckReliableFallback();
            }

            // Rate limit reset always runs frame-based (doesn't need tick precision)
            if (MaxMessagesPerPeerPerSecond > 0 && Time.unscaledTime >= _rateLimitResetTime)
            {
                _peerMessageCounts.Clear();
                _rateLimitResetTime = Time.unscaledTime + 1f;
            }

            // Check if any hibernated peers' grace periods have expired
            if (IsHost)
                CheckHibernationExpiry();
        }

        /// <summary>Called by TickSimulation.OnPostTick — sends state updates on tick boundary.</summary>
        private void OnSimulationPostTick()
        {
            if (_dirtyObjects.Count > 0)
                SendStateUpdates();

            CheckReliableFallback();
        }

        /// <summary>
        /// Check if a peer has exceeded their message rate limit.
        /// Returns true if the message should be dropped.
        /// </summary>
        private bool IsRateLimited(ProductUserId sender)
        {
            if (MaxMessagesPerPeerPerSecond <= 0) return false;

            _peerMessageCounts.TryGetValue(sender, out int count);
            if (count >= MaxMessagesPerPeerPerSecond) return true;
            _peerMessageCounts[sender] = count + 1;
            return false;
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
            Router.Register(MSG_REPARENT, HandleReparent);

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
            // If root is destroyed, also clean up children
            if (obj.IsRootNetworkObject)
            {
                var children = GetRegisteredChildren(obj.NetworkId);
                for (int i = 0; i < children.Count; i++)
                {
                    var child = children[i];
                    _objects.Remove(child.NetworkId);
                    _dirtyObjects.Remove(child);
                    UnregisterRPCs(child);
                    if (InterestManagementEnabled)
                        InterestManager.Instance?.OnObjectRemoved(child.NetworkId);
                }
                _originalChildren.Remove(obj.NetworkId);
            }

            // If this was an original child, remove from its root's _originalChildren
            if (obj.OriginalParentNetworkId != 0)
                RemoveFromOriginalChildren(obj.OriginalParentNetworkId, obj.NetworkId);

            _objects.Remove(obj.NetworkId);
            _dirtyObjects.Remove(obj);
            UnregisterRPCs(obj);

            // Clean up from interest manager
            if (InterestManagementEnabled)
                InterestManager.Instance?.OnObjectRemoved(obj.NetworkId);

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

        /// <summary>
        /// Called by InterestManager when an object enters a peer's interest zone.
        /// Sends a spawn message so the peer knows about this object.
        /// </summary>
        private void OnInterestEnter(ProductUserId peer, uint networkId)
        {
            if (!InterestManagementEnabled) return;
            if (!_objects.TryGetValue(networkId, out var obj)) return;
            if (obj == null || !obj.IsRegistered) return;

            // Skip children — they're included in root's WriteSpawnData
            if (obj.IsChildNetworkObject) return;

            // Only the owner sends interest-based spawns (prevents duplicates)
            if (!obj.IsOwner) return;

            var writer = NetWriterPool.Get();
            WriteSpawnData(writer, obj);
            Router.SendToPeer(MSG_SPAWN, writer, peer, PacketReliability.ReliableOrdered, 1);
            NetWriterPool.Return(writer);
        }

        /// <summary>
        /// Called by InterestManager when an object leaves a peer's interest zone.
        /// Sends a despawn message so the peer cleans it up locally.
        /// </summary>
        private void OnInterestExit(ProductUserId peer, uint networkId)
        {
            if (!InterestManagementEnabled) return;
            if (!_objects.TryGetValue(networkId, out var obj)) return;
            if (obj == null || !obj.IsRegistered) return;

            // Skip children — they're despawned with root
            if (obj.IsChildNetworkObject) return;

            // Only the owner sends interest-based despawns
            if (!obj.IsOwner) return;

            var writer = NetWriterPool.Get();
            writer.WriteUInt32(networkId);
            Router.SendToPeer(MSG_DESPAWN, writer, peer, PacketReliability.ReliableOrdered, 1);
            NetWriterPool.Return(writer);
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

            // Offline mode: just clear dirty flags, no network send needed
            if (OfflineMode)
            {
                for (int i = 0; i < _dirtyObjects.Count; i++)
                    _dirtyObjects[i].ClearDirty();
                _dirtyObjects.Clear();
                return;
            }

            // Pre-serialize each dirty object's delta data + increment sequences
            // (must happen once regardless of per-peer filtering)
            for (int i = 0; i < _dirtyObjects.Count; i++)
            {
                var obj = _dirtyObjects[i];
                obj.SyncSequence++;
                obj.LastUnreliableSendTime = Time.time;
                obj.ReliableFallbackPending = true;
                if (!_reliableFallbackObjects.Contains(obj))
                    _reliableFallbackObjects.Add(obj);
            }

            var im = InterestManagementEnabled ? InterestManager.Instance : null;
            if (im == null)
            {
                // Fast path: no interest management — single packet to all
                var writer = NetWriterPool.Get();
                writer.WritePackedUInt32((uint)validCount);

                for (int i = 0; i < _dirtyObjects.Count; i++)
                {
                    var obj = _dirtyObjects[i];
                    writer.WriteUInt32(obj.NetworkId);
                    writer.WriteUInt16(obj.SyncSequence);

                    var dataWriter = NetWriterPool.Get();
                    obj.SerializeDirty(dataWriter);
                    var data = dataWriter.ToArraySegment();
                    writer.WriteUInt16((ushort)data.Count);
                    writer.WriteBytesRaw(data);
                    NetWriterPool.Return(dataWriter);

                    obj.ClearDirty();
                }

                Router.SendToAll(MSG_STATE_UPDATE, writer, PacketReliability.UnreliableUnordered, 0);
                NetWriterPool.Return(writer);
            }
            else
            {
                // Interest-filtered path: pre-serialize dirty data, then build per-peer packets
                // Pre-serialize each object's dirty data (shared across peers)
                var dirtyData = new ArraySegment<byte>[_dirtyObjects.Count];
                var dataWriters = new NetWriter[_dirtyObjects.Count];
                for (int i = 0; i < _dirtyObjects.Count; i++)
                {
                    var obj = _dirtyObjects[i];
                    var dw = NetWriterPool.Get();
                    obj.SerializeDirty(dw);
                    dirtyData[i] = dw.ToArraySegment();
                    dataWriters[i] = dw;
                    obj.ClearDirty();
                }

                // Build per-peer filtered packets
                var peers = EOSP2PManager.Instance?.Peers;
                if (peers != null)
                {
                    foreach (var peer in peers)
                    {
                        // Count objects this peer is interested in
                        int peerCount = 0;
                        for (int i = 0; i < _dirtyObjects.Count; i++)
                        {
                            if (im.IsInterested(peer, _dirtyObjects[i]))
                                peerCount++;
                        }
                        if (peerCount == 0) continue;

                        var writer = NetWriterPool.Get();
                        writer.WritePackedUInt32((uint)peerCount);

                        for (int i = 0; i < _dirtyObjects.Count; i++)
                        {
                            if (!im.IsInterested(peer, _dirtyObjects[i])) continue;

                            var obj = _dirtyObjects[i];
                            writer.WriteUInt32(obj.NetworkId);
                            writer.WriteUInt16(obj.SyncSequence);
                            writer.WriteUInt16((ushort)dirtyData[i].Count);
                            writer.WriteBytesRaw(dirtyData[i]);
                        }

                        Router.SendToPeer(MSG_STATE_UPDATE, writer, peer,
                            PacketReliability.UnreliableUnordered, 0);
                        NetWriterPool.Return(writer);
                    }
                }

                // Return pooled writers
                for (int i = 0; i < dataWriters.Length; i++)
                    NetWriterPool.Return(dataWriters[i]);
            }

            _dirtyObjects.Clear();
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
                    // Sender validation: check write access rules
                    if (!ValidateSyncVarSender(sender, obj))
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
        /// Validates whether a sender is allowed to write state to a NetworkObject.
        /// Checks OnSyncVarWrite callback first (custom rules), then falls back to
        /// SyncVarWriteAccess-based validation.
        /// </summary>
        private bool ValidateSyncVarSender(ProductUserId sender, NetworkObject obj)
        {
            // Custom callback takes priority — if set, it's the sole authority
            if (OnSyncVarWrite != null)
                return OnSyncVarWrite(sender, obj);

            // Default validation based on SyncVarWriteAccess
            var maxAccess = obj.MaxWriteAccess;
            switch (maxAccess)
            {
                case SyncVarWriteAccess.All:
                    return true;
                case SyncVarWriteAccess.Host:
                    // Allow owner OR host
                    if (obj.OwnerId != null && sender.Equals(obj.OwnerId)) return true;
                    var hostPuid = GetHostPuid();
                    return hostPuid != null && sender.Equals(hostPuid);
                case SyncVarWriteAccess.Owner:
                default:
                    // Only owner (original behavior)
                    if (obj.OwnerId == null) return true; // no owner assigned yet
                    return sender.Equals(obj.OwnerId);
            }
        }

        /// <summary>
        /// If an object was sent unreliable and hasn't been re-dirtied within 200ms,
        /// resend its full state via reliable SNAPSHOT. Guarantees eventual consistency.
        /// Uses SNAPSHOT format (WriteFullState) which handles SyncLists correctly.
        /// </summary>
        private void CheckReliableFallback()
        {
            if (OfflineMode) { _reliableFallbackObjects.Clear(); return; }

            float now = Time.time;
            for (int i = _reliableFallbackObjects.Count - 1; i >= 0; i--)
            {
                var obj = _reliableFallbackObjects[i];

                // Object was re-dirtied or destroyed — no fallback needed
                // Skip children — their root's reliable fallback will include them
                if (obj == null || !obj.IsRegistered || !obj.IsOwner || obj.IsDirty || !obj.ReliableFallbackPending
                    || obj.IsChildNetworkObject)
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

                SendToInterestedPeers(MSG_SNAPSHOT, writer, obj, PacketReliability.ReliableOrdered, 1);
                NetWriterPool.Return(writer);
            }
        }

        #endregion

        #region Spawn / Despawn Messages

        private void BroadcastSpawn(NetworkObject obj)
        {
            var writer = NetWriterPool.Get();
            WriteSpawnData(writer, obj);
            SendToInterestedPeers(MSG_SPAWN, writer, obj, PacketReliability.ReliableOrdered, 1);
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

            // Write children using _originalChildren registry (tracks original prefab hierarchy)
            if (obj.IsRootNetworkObject && _originalChildren.TryGetValue(obj.NetworkId, out var origList))
            {
                // Count how many children are still alive
                int aliveCount = 0;
                for (int c = 0; c < origList.Count; c++)
                {
                    if (_objects.ContainsKey(origList[c].childNetId))
                        aliveCount++;
                }
                writer.WriteByte((byte)aliveCount);

                for (int c = 0; c < origList.Count; c++)
                {
                    var (childNetId, localIndex) = origList[c];
                    if (!_objects.TryGetValue(childNetId, out var child))
                        continue; // child was despawned, skip

                    writer.WriteUInt32(child.NetworkId);
                    writer.WriteByte(localIndex);

                    // Flags: 0x00 = attached (in parent hierarchy), 0x01 = detached (reparented away)
                    bool isDetached = child.ParentNetworkId != obj.NetworkId;
                    writer.WriteByte(isDetached ? (byte)0x01 : (byte)0x00);

                    // Write child data with length prefix for safe skipping
                    // Position is NOT included — that's NetworkTransform's job
                    var childDataWriter = NetWriterPool.Get();
                    childDataWriter.WriteByte((byte)child.SyncVarCount);
                    child.SerializeAll(childDataWriter);
                    var childData = childDataWriter.ToArraySegment();
                    writer.WriteUInt16((ushort)childData.Count);
                    writer.WriteBytesRaw(childData);
                    NetWriterPool.Return(childDataWriter);
                }
            }
            else
            {
                // No children (or this is a child object in snapshot — they're flat)
                writer.WriteByte(0);
            }
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
            if (_objects.ContainsKey(networkId))
            {
                // Still need to consume child data from the reader
                ReadAndDiscardChildren(reader);
                return;
            }

            // Handle reserved PrefabIds (RoomState / PlayerState)
            if (prefabId == NetworkRoomState.PREFAB_ID || prefabId == NetworkPlayerState.PREFAB_ID)
            {
                SpawnReservedObject(prefabId, networkId, ownerId, destroyWithOwner, syncVarCount, reader);
                // Reserved objects have no children — but still read the child count byte
                reader.ReadByte(); // childCount = 0
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
            netObj.ParentNetworkId = 0;
            netObj.IsRegistered = true;
            _objects[networkId] = netObj;

            // Read SyncVar state
            if (syncVarCount > 0 && netObj.SyncVarCount > 0)
                netObj.DeserializeAll(reader);

            netObj.NotifyNetworkSpawn();

            // Read and register children
            byte childCount = reader.ReadByte();
            var origChildren = new List<(uint, byte)>();
            if (childCount > 0)
            {
                var allNetObjs = go.GetComponentsInChildren<NetworkObject>(true);

                for (int c = 0; c < childCount; c++)
                {
                    uint childNetId = reader.ReadUInt32();
                    byte localIndex = reader.ReadByte();
                    byte flags = reader.ReadByte();
                    ushort childDataLen = reader.ReadUInt16();

                    // Match child by localIndex in the hierarchy
                    if (localIndex > 0 && localIndex < allNetObjs.Length)
                    {
                        var child = allNetObjs[localIndex];
                        byte childSyncVarCount = reader.ReadByte();

                        bool isDetached = (flags & 0x01) != 0;

                        child.NetworkId = childNetId;
                        child.PrefabId = prefabId;
                        child.OwnerId = ownerId;
                        child.OriginalParentNetworkId = networkId;
                        child.ParentNetworkId = isDetached ? 0u : networkId;
                        child.DestroyWithOwner = destroyWithOwner;
                        child.IsRegistered = true;
                        _objects[childNetId] = child;
                        origChildren.Add((childNetId, localIndex));

                        if (childSyncVarCount > 0 && child.SyncVarCount > 0 && childSyncVarCount == child.SyncVarCount)
                            child.DeserializeAll(reader);
                        else if (childDataLen > 1)
                            reader.Skip(childDataLen - 1); // skip SyncVar data (1 byte already read)

                        // If detached: unparent Transform. Position comes from NetworkTransform.
                        if (isDetached)
                            child.transform.SetParent(null, worldPositionStays: true);

                        child.NotifyNetworkSpawn();
                    }
                    else
                    {
                        // Prefab mismatch — skip this child's data
                        reader.Skip(childDataLen);
                        Debug.LogWarning($"[NetworkManager] Child localIndex {localIndex} out of range for prefab {prefabId}");
                    }
                }
            }
            if (origChildren.Count > 0)
                _originalChildren[networkId] = origChildren;

            EOSDebugLogger.Log(DebugCategory.EOSManager, "NetworkManager",
                $"Remote spawn: object {networkId} (prefab {prefabId}, owner {ownerId}, {childCount} children)");
        }

        /// <summary>Read and discard child data from the reader (when root already exists).</summary>
        private static void ReadAndDiscardChildren(NetReader reader)
        {
            byte childCount = reader.ReadByte();
            for (int c = 0; c < childCount; c++)
            {
                reader.ReadUInt32();  // childNetId
                reader.ReadByte();    // localIndex
                reader.ReadByte();    // flags
                ushort dataLen = reader.ReadUInt16();
                reader.Skip(dataLen); // child SyncVar data (+ world pos/rot if detached)
            }
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

                // Cascade: unregister all children first
                var children = GetRegisteredChildren(networkId);
                for (int i = 0; i < children.Count; i++)
                {
                    var child = children[i];
                    _objects.Remove(child.NetworkId);
                    _dirtyObjects.Remove(child);
                    child.IsRegistered = false;
                    child.NotifyNetworkDespawn();
                    UnregisterRPCs(child);
                }

                ushort prefabId = obj.PrefabId;
                _objects.Remove(networkId);
                _dirtyObjects.Remove(obj);
                obj.IsRegistered = false;
                obj.NotifyNetworkDespawn();
                UnregisterRPCs(obj);
                ReturnToPool(prefabId, obj.gameObject);

                // Clean up _originalChildren for this root
                _originalChildren.Remove(networkId);

                // If this was a detached child, remove from its original root's list
                if (obj.OriginalParentNetworkId != 0)
                    RemoveFromOriginalChildren(obj.OriginalParentNetworkId, networkId);

                EOSDebugLogger.Log(DebugCategory.EOSManager, "NetworkManager",
                    $"Remote despawn: object {networkId} (+ {children.Count} children)");
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

                // Cascade authority to children
                var children = GetRegisteredChildren(networkId);
                for (int i = 0; i < children.Count; i++)
                {
                    var childOld = children[i].OwnerId;
                    children[i].OwnerId = newOwnerId;
                    children[i].NotifyOwnerChanged(childOld, newOwnerId);
                }

                EOSDebugLogger.Log(DebugCategory.EOSManager, "NetworkManager",
                    $"Authority transfer: object {networkId} -> {newOwnerId} (+ {children.Count} children)");
            }
        }

        /// <summary>Transfer ownership of a NetworkObject to another peer.</summary>
        public void TransferAuthority(NetworkObject obj, ProductUserId newOwner)
        {
            if (obj == null || !obj.IsRegistered) return;

            // Block direct child transfer — must transfer the root
            if (obj.IsChildNetworkObject)
            {
                Debug.LogWarning($"[NetworkManager] Cannot transfer authority on child NetworkObject {obj.NetworkId} — transfer the root instead");
                return;
            }

            if (!obj.IsOwner && !IsHost) return;

            var oldOwner = obj.OwnerId;
            obj.OwnerId = newOwner;
            obj.NotifyOwnerChanged(oldOwner, newOwner);

            // Cascade authority to children
            var children = GetRegisteredChildren(obj.NetworkId);
            for (int i = 0; i < children.Count; i++)
            {
                var childOld = children[i].OwnerId;
                children[i].OwnerId = newOwner;
                children[i].NotifyOwnerChanged(childOld, newOwner);
            }

            // Authority changes always broadcast to all (not interest-filtered)
            // because the new owner needs to know even if they can't "see" the object yet
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

        #region Reparenting

        /// <summary>
        /// Reparent a NetworkObject at runtime. Pass null to detach (make independent root).
        /// Only callable by the owner or host. World position/rotation are preserved.
        /// </summary>
        public void ReparentObject(NetworkObject obj, NetworkObject newParent)
        {
            if (obj == null || !obj.IsRegistered) return;

            // Only owner or host can reparent; unowned scene objects can be reparented by anyone
            if (obj.OwnerId != null && !obj.IsOwner && !IsHost) return;

            // Validate: cannot attach to a child — only root objects can be parents
            if (newParent != null && newParent.IsChildNetworkObject)
            {
                Debug.LogWarning($"[NetworkManager] Cannot reparent {obj.NetworkId} under child {newParent.NetworkId} — target must be a root");
                return;
            }

            // No-op if already in the desired state
            if (newParent == null && obj.ParentNetworkId == 0) return;
            if (newParent != null && obj.ParentNetworkId == newParent.NetworkId) return;

            ApplyReparent(obj, newParent);

            if (!OfflineMode)
            {
                var writer = NetWriterPool.Get();
                writer.WriteUInt32(obj.NetworkId);
                writer.WriteUInt32(newParent?.NetworkId ?? 0);
                Router.SendToAll(MSG_REPARENT, writer, PacketReliability.ReliableOrdered, 1);
                NetWriterPool.Return(writer);
            }

            EOSDebugLogger.Log(DebugCategory.EOSManager, "NetworkManager",
                $"Reparented object {obj.NetworkId} -> {(newParent != null ? newParent.NetworkId.ToString() : "detached")}");
        }

        private void ApplyReparent(NetworkObject obj, NetworkObject newParent)
        {
            // Resolve old parent for event
            NetworkObject oldParent = null;
            if (obj.ParentNetworkId != 0)
                _objects.TryGetValue(obj.ParentNetworkId, out oldParent);

            // Update ParentNetworkId
            obj.ParentNetworkId = newParent?.NetworkId ?? 0;

            // Update Transform hierarchy (preserve world position)
            obj.transform.SetParent(newParent?.transform, worldPositionStays: true);

            // If attaching to a new parent, inherit OwnerId if different
            if (newParent != null && newParent.OwnerId != null)
            {
                var oldOwner = obj.OwnerId;
                if (oldOwner == null || !oldOwner.Equals(newParent.OwnerId))
                {
                    obj.OwnerId = newParent.OwnerId;
                    obj.NotifyOwnerChanged(oldOwner, newParent.OwnerId);
                }
            }

            obj.NotifyReparented(oldParent, newParent);

            // Update interest management
            if (InterestManagementEnabled)
                InterestManager.Instance?.OnObjectReparented(obj);
        }

        private void HandleReparent(ProductUserId sender, NetReader reader)
        {
            uint objectNetId = reader.ReadUInt32();
            uint newParentNetId = reader.ReadUInt32();

            if (!_objects.TryGetValue(objectNetId, out var obj)) return;

            // Sender validation: only owner or host can reparent
            if (obj.OwnerId != null && !sender.Equals(obj.OwnerId) && !sender.Equals(GetHostPuid()))
            {
                EOSDebugLogger.LogWarning(DebugCategory.EOSManager, "NetworkManager",
                    $"Rejected reparent: sender {sender} is not owner/host for object {objectNetId}");
                return;
            }

            NetworkObject newParent = null;
            if (newParentNetId != 0 && !_objects.TryGetValue(newParentNetId, out newParent))
            {
                EOSDebugLogger.LogWarning(DebugCategory.EOSManager, "NetworkManager",
                    $"Reparent target {newParentNetId} not found for object {objectNetId}");
                return;
            }

            ApplyReparent(obj, newParent);

            EOSDebugLogger.Log(DebugCategory.EOSManager, "NetworkManager",
                $"Remote reparent: object {objectNetId} -> {(newParent != null ? newParentNetId.ToString() : "detached")}");
        }

        /// <summary>Remove a child entry from an original root's _originalChildren list.</summary>
        private void RemoveFromOriginalChildren(uint originalRootNetId, uint childNetId)
        {
            if (_originalChildren.TryGetValue(originalRootNetId, out var list))
            {
                for (int i = list.Count - 1; i >= 0; i--)
                {
                    if (list[i].childNetId == childNetId)
                    {
                        list.RemoveAt(i);
                        break;
                    }
                }
                if (list.Count == 0)
                    _originalChildren.Remove(originalRootNetId);
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

            // Priority 3: Everything else (filtered by interest if enabled)
            // Skip original children (OriginalParentNetworkId != 0) — they're inline in their root's data.
            // Dynamically attached roots (OriginalParentNetworkId == 0, ParentNetworkId != 0) are NOT skipped —
            // they appear as top-level entries, then MSG_REPARENT sets the parent after snapshot.
            var im = InterestManagementEnabled ? InterestManager.Instance : null;
            foreach (var obj in _objects.Values)
            {
                if (obj == null || !obj.IsRegistered) continue;
                // Skip already-added RoomState and PlayerStates
                if (obj.PrefabId == NetworkRoomState.PREFAB_ID || obj.PrefabId == NetworkPlayerState.PREFAB_ID)
                    continue;
                // Skip original children — they're serialized as part of their root
                if (obj.OriginalParentNetworkId != 0) continue;
                // Interest filter: only include objects the joining peer can see
                if (im != null && !im.IsInterested(sender, obj))
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

            // Send MSG_REPARENT for dynamically attached roots (OriginalParentNetworkId == 0 but ParentNetworkId != 0)
            foreach (var obj in _objects.Values)
            {
                if (obj == null || !obj.IsRegistered) continue;
                if (obj.OriginalParentNetworkId == 0 && obj.ParentNetworkId != 0)
                {
                    var rw = NetWriterPool.Get();
                    rw.WriteUInt32(obj.NetworkId);
                    rw.WriteUInt32(obj.ParentNetworkId);
                    Router.SendToPeer(MSG_REPARENT, rw, sender, PacketReliability.ReliableOrdered, 1);
                    NetWriterPool.Return(rw);
                }
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
                    // Read and update children state
                    ReadSnapshotChildren(reader, existing, prefabId, ownerId, networkId, destroyWithOwner, true);
                    continue;
                }

                // Handle reserved PrefabIds (RoomState / PlayerState)
                if (prefabId == NetworkRoomState.PREFAB_ID || prefabId == NetworkPlayerState.PREFAB_ID)
                {
                    SpawnReservedObject(prefabId, networkId, ownerId, destroyWithOwner, syncVarCount, reader);
                    reader.ReadByte(); // childCount = 0 for reserved objects
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
                netObj.ParentNetworkId = 0;
                netObj.IsRegistered = true;
                _objects[networkId] = netObj;

                if (syncVarCount > 0 && netObj.SyncVarCount > 0)
                    netObj.DeserializeAll(reader);

                netObj.NotifyNetworkSpawn();

                // Read and register children
                ReadSnapshotChildren(reader, netObj, prefabId, ownerId, networkId, destroyWithOwner, false);
            }

            // After snapshot chunks contain our RoomState, ensure our PlayerState exists
            if (RoomState != null)
            {
                EnsureLocalPlayerState();
                NetworkSceneManager.Instance?.SyncScenesFromRoomState();
            }
        }

        /// <summary>
        /// Read child data from a snapshot/spawn message. If existingOnly is true, just updates
        /// SyncVars on existing children. Otherwise, registers new children.
        /// </summary>
        private void ReadSnapshotChildren(NetReader reader, NetworkObject root, ushort prefabId,
            ProductUserId ownerId, uint rootNetworkId, bool destroyWithOwner, bool existingOnly)
        {
            byte childCount = reader.ReadByte();
            if (childCount == 0) return;

            NetworkObject[] allNetObjs = null;
            if (childCount > 0)
                allNetObjs = root.GetComponentsInChildren<NetworkObject>(true);

            var origChildren = new List<(uint, byte)>();

            for (int c = 0; c < childCount; c++)
            {
                uint childNetId = reader.ReadUInt32();
                byte localIndex = reader.ReadByte();
                byte flags = reader.ReadByte();
                ushort childDataLen = reader.ReadUInt16();
                bool isDetached = (flags & 0x01) != 0;

                if (existingOnly && _objects.ContainsKey(childNetId))
                {
                    // Update existing child's SyncVars
                    var existing = _objects[childNetId];
                    byte childSyncVarCount = reader.ReadByte();
                    if (childSyncVarCount > 0 && existing.SyncVarCount > 0 && childSyncVarCount == existing.SyncVarCount)
                        existing.DeserializeAll(reader);
                    else if (childDataLen > 1)
                        reader.Skip(childDataLen - 1);

                    // Handle detach state change. Position comes from NetworkTransform.
                    if (isDetached && existing.ParentNetworkId != 0)
                    {
                        existing.ParentNetworkId = 0;
                        existing.transform.SetParent(null, worldPositionStays: true);
                    }
                }
                else if (!existingOnly && localIndex > 0 && allNetObjs != null && localIndex < allNetObjs.Length)
                {
                    // Register new child
                    var child = allNetObjs[localIndex];
                    byte childSyncVarCount = reader.ReadByte();

                    child.NetworkId = childNetId;
                    child.PrefabId = prefabId;
                    child.OwnerId = ownerId;
                    child.OriginalParentNetworkId = rootNetworkId;
                    child.ParentNetworkId = isDetached ? 0u : rootNetworkId;
                    child.DestroyWithOwner = destroyWithOwner;
                    child.IsRegistered = true;
                    _objects[childNetId] = child;
                    origChildren.Add((childNetId, localIndex));

                    if (childSyncVarCount > 0 && child.SyncVarCount > 0 && childSyncVarCount == child.SyncVarCount)
                        child.DeserializeAll(reader);
                    else if (childDataLen > 1)
                        reader.Skip(childDataLen - 1);

                    // If detached: unparent Transform. Position comes from NetworkTransform.
                    if (isDetached)
                        child.transform.SetParent(null, worldPositionStays: true);

                    child.NotifyNetworkSpawn();
                }
                else
                {
                    // Can't match — skip data
                    reader.Skip(childDataLen);
                }
            }

            // Populate _originalChildren if registering new children
            if (!existingOnly && origChildren.Count > 0)
                _originalChildren[rootNetworkId] = origChildren;
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
            if (IsRateLimited(sender)) return;

            uint networkId = reader.ReadUInt32();
            byte componentIndex = reader.ReadByte();
            uint methodHash = reader.ReadUInt32();
            // Args are left in the reader for the handler to consume

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

            // Try exact key first (component-scoped), then fallback to SELF_COMPONENT_INDEX
            var key = new RPCKey { NetworkId = networkId, ComponentIndex = componentIndex, MethodHash = methodHash };
            if (!_rpcHandlers.TryGetValue(key, out var handler) && componentIndex != NetworkObject.SELF_COMPONENT_INDEX)
            {
                key.ComponentIndex = NetworkObject.SELF_COMPONENT_INDEX;
                _rpcHandlers.TryGetValue(key, out handler);
            }

            if (handler != null)
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

        private void ExecuteRPCLocal(uint networkId, byte componentIndex, uint methodHash, byte[] argData, int argDataLen)
        {
            // Try exact key first (component-scoped), then fallback to SELF_COMPONENT_INDEX
            var key = new RPCKey { NetworkId = networkId, ComponentIndex = componentIndex, MethodHash = methodHash };
            if (!_rpcHandlers.TryGetValue(key, out var handler) && componentIndex != NetworkObject.SELF_COMPONENT_INDEX)
            {
                key.ComponentIndex = NetworkObject.SELF_COMPONENT_INDEX;
                _rpcHandlers.TryGetValue(key, out handler);
            }
            if (handler == null) return;

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

        /// <summary>Legacy overload without component index. Uses SELF_COMPONENT_INDEX.</summary>
        private void ExecuteRPCLocal(uint networkId, uint methodHash, byte[] argData, int argDataLen)
        {
            ExecuteRPCLocal(networkId, NetworkObject.SELF_COMPONENT_INDEX, methodHash, argData, argDataLen);
        }

        /// <summary>
        /// Host receives this when a client sends a [NetRpc(Validated = true)] RPC.
        /// The host runs the validator and, if approved, rebroadcasts to all peers.
        /// Wire format: [networkId:u32][componentIndex:u8][methodHash:u32][originalTarget:u8][argData...]
        /// </summary>
        private void HandleRPCValidated(ProductUserId sender, NetReader reader)
        {
            if (IsRateLimited(sender)) return;
            if (!IsHost) return; // Only the host handles validated RPCs

            uint networkId = reader.ReadUInt32();
            byte componentIndex = reader.ReadByte();
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

            // Approved — rebroadcast to interested peers (including back to sender)
            var writer = NetWriterPool.Get();
            writer.WriteUInt32(networkId);
            writer.WriteByte(componentIndex);
            writer.WriteUInt32(methodHash);
            writer.WriteByte(originalTarget);
            writer.WriteBytesRaw(argData, 0, argData.Length);
            if (targetObj != null)
                SendToInterestedPeers(MSG_RPC_REBROADCAST, writer, targetObj, PacketReliability.ReliableOrdered, 1);
            else
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
                ExecuteRPCLocal(networkId, componentIndex, methodHash, argData, argData.Length);
        }

        /// <summary>
        /// All peers receive this when the host rebroadcasts an approved validated RPC.
        /// Wire format: [networkId:u32][componentIndex:u8][methodHash:u32][originalTarget:u8][argData...]
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
            byte componentIndex = reader.ReadByte();
            uint methodHash = reader.ReadUInt32();
            byte originalTarget = reader.ReadByte();
            byte[] argData = reader.ReadBytesRemaining();

            // Check if we should execute based on the original target
            var targets = (RPCTarget)originalTarget;
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
                ExecuteRPCLocal(networkId, componentIndex, methodHash, argData, argData.Length);
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

        private void OnLobbyJoinedRecomputeHost(LobbyData _) => RecomputeHost();

        private void RecomputeHost()
        {
            if (OfflineMode) return; // always host in offline mode

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

                OnHostChanged?.Invoke(IsHost);
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

                // Restore hibernated objects if this peer is reconnecting
                RestoreHibernatedObjects(peer);
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

            // Clean up interest management state
            if (InterestManagementEnabled)
                InterestManager.Instance?.OnPeerDisconnected(peer);

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
                    ExecuteRPCLocal(buffered.Target.NetworkId, buffered.ComponentIndex, nameHash, buffered.ArgData, buffered.ArgData.Length);

                if (sendRemote)
                {
                    var writer = NetWriterPool.Get();
                    writer.WriteUInt32(buffered.Target.NetworkId);
                    writer.WriteByte(buffered.ComponentIndex);
                    writer.WriteUInt32(nameHash);
                    writer.WriteBytesRaw(buffered.ArgData, 0, buffered.ArgData.Length);

                    switch (buffered.Targets)
                    {
                        case RPCTarget.All:
                        case RPCTarget.Others:
                            SendToInterestedPeers(MSG_RPC, writer, buffered.Target,
                                PacketReliability.ReliableOrdered, 1);
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
                            SendToInterestedNonSpectators(MSG_RPC, writer, buffered.Target);
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

            // Collect orphaned root objects only — Despawn/TransferAuthority cascades handle children
            var orphans = new List<NetworkObject>();
            foreach (var obj in _objects.Values)
            {
                if (obj.OwnerId == disconnectedPeer && obj.IsRootNetworkObject)
                    orphans.Add(obj);
            }

            // Hibernation: collect PersistOnDisconnect objects for grace period
            if (_reconnectGracePeriod > 0f)
            {
                var persistIds = new List<uint>();
                for (int i = orphans.Count - 1; i >= 0; i--)
                {
                    if (orphans[i].PersistOnDisconnect)
                    {
                        persistIds.Add(orphans[i].NetworkId);
                        // Transfer authority to host temporarily (so objects still sync)
                        var obj = orphans[i];
                        var oldOwner = obj.OwnerId;
                        obj.OwnerId = localPuid;
                        obj.NotifyOwnerChanged(oldOwner, localPuid);

                        var children = GetRegisteredChildren(obj.NetworkId);
                        for (int c = 0; c < children.Count; c++)
                        {
                            var childOld = children[c].OwnerId;
                            children[c].OwnerId = localPuid;
                            children[c].NotifyOwnerChanged(childOld, localPuid);
                        }

                        var writer = NetWriterPool.Get();
                        writer.WriteUInt32(obj.NetworkId);
                        writer.WriteProductUserId(localPuid);
                        Router.SendToAll(MSG_AUTHORITY, writer, PacketReliability.ReliableOrdered, 1);
                        NetWriterPool.Return(writer);

                        EOSDebugLogger.Log(DebugCategory.EOSManager, "NetworkManager",
                            $"Hibernating object {obj.NetworkId} (PersistOnDisconnect) — owner {disconnectedPeer} left, grace {_reconnectGracePeriod}s");

                        orphans.RemoveAt(i);
                    }
                }
                if (persistIds.Count > 0)
                {
                    _hibernatedPeers[disconnectedPeer.ToString()] = new HibernatedPeer
                    {
                        Puid = disconnectedPeer.ToString(),
                        ExpireTime = Time.time + _reconnectGracePeriod,
                        OwnedObjectIds = persistIds
                    };
                }
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

                // Transfer ownership to new host (cascades to children)
                var oldOwner = obj.OwnerId;
                obj.OwnerId = localPuid;
                obj.NotifyOwnerChanged(oldOwner, localPuid);

                // Cascade to children
                var children = GetRegisteredChildren(obj.NetworkId);
                for (int c = 0; c < children.Count; c++)
                {
                    var childOld = children[c].OwnerId;
                    children[c].OwnerId = localPuid;
                    children[c].NotifyOwnerChanged(childOld, localPuid);
                }

                // Broadcast authority change
                var writer = NetWriterPool.Get();
                writer.WriteUInt32(obj.NetworkId);
                writer.WriteProductUserId(localPuid);
                Router.SendToAll(MSG_AUTHORITY, writer, PacketReliability.ReliableOrdered, 1);
                NetWriterPool.Return(writer);

                EOSDebugLogger.Log(DebugCategory.EOSManager, "NetworkManager",
                    $"Claimed orphaned object {obj.NetworkId} (+ {children.Count} children) from {disconnectedPeer}");
            }
        }

        private void RestoreHibernatedObjects(ProductUserId peer)
        {
            string puidStr = peer.ToString();
            if (!_hibernatedPeers.TryGetValue(puidStr, out var hibernated)) return;

            EOSDebugLogger.Log(DebugCategory.EOSManager, "NetworkManager",
                $"Peer {puidStr} reconnected — restoring {hibernated.OwnedObjectIds.Count} hibernated objects");

            foreach (var netId in hibernated.OwnedObjectIds)
            {
                if (_objects.TryGetValue(netId, out var obj))
                    TransferAuthority(obj, peer);
            }

            _hibernatedPeers.Remove(puidStr);
        }

        private void CheckHibernationExpiry()
        {
            if (_hibernatedPeers.Count == 0) return;

            List<string> expired = null;
            foreach (var kvp in _hibernatedPeers)
            {
                if (Time.time >= kvp.Value.ExpireTime)
                {
                    expired ??= new List<string>();
                    expired.Add(kvp.Key);
                }
            }

            if (expired == null) return;

            var localPuid = EOSManager.Instance?.LocalProductUserId;
            foreach (var puid in expired)
            {
                var peer = _hibernatedPeers[puid];
                EOSDebugLogger.Log(DebugCategory.EOSManager, "NetworkManager",
                    $"Hibernation grace expired for {puid} — processing {peer.OwnedObjectIds.Count} objects");

                foreach (var netId in peer.OwnedObjectIds)
                {
                    if (_objects.TryGetValue(netId, out var obj))
                    {
                        if (obj.DestroyWithOwner)
                            Despawn(obj);
                        // else: host keeps authority permanently (already transferred in ClaimOrphanedObjects)
                    }
                }
                _hibernatedPeers.Remove(puid);
            }
        }

        #endregion

        #region Network ID Generation

        private void InitLocalIdPrefix()
        {
            if (_localIdPrefix != 0) return; // already set (or set by StartOfflineMode)
            if (OfflineMode) return; // offline prefix is set by StartOfflineMode()

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
            if (localPuid == null && !OfflineMode) return;

            var go = new GameObject("NetworkRoomState");
            if (EOSManager.Instance != null)
                go.transform.SetParent(EOSManager.Instance.transform);
            else
                DontDestroyOnLoad(go);

            var roomState = go.AddComponent<NetworkRoomState>();
            var netObj = roomState.Net;
            netObj.NetworkId = NetworkRoomState.WELL_KNOWN_ID;
            netObj.PrefabId = NetworkRoomState.PREFAB_ID;
            netObj.OwnerId = localPuid; // null in offline mode
            netObj.DestroyWithOwner = false;
            netObj.IsRegistered = true;
            _objects[netObj.NetworkId] = netObj;
            RoomState = roomState;

            if (OfflineMode)
                _offlineOwnedNetworkIds.Add(netObj.NetworkId);

            netObj.NotifyNetworkSpawn();

            // Broadcast spawn to all peers (skip in offline mode)
            if (!OfflineMode)
                BroadcastSpawn(netObj);

            EOSDebugLogger.Log(DebugCategory.EOSManager, "NetworkManager",
                $"RoomState created by host{(OfflineMode ? " [offline]" : "")}");
        }

        /// <summary>
        /// Ensures the local player's PlayerState object exists. Called on peer connect.
        /// </summary>
        private void EnsureLocalPlayerState()
        {
            if (LocalPlayerState != null) return;

            var localPuid = EOSManager.Instance?.LocalProductUserId;
            if (localPuid == null && !OfflineMode) return;

            uint networkId = GenerateNetworkId();

            var go = new GameObject(OfflineMode ? "PlayerState_Offline" : $"PlayerState_{localPuid}");
            if (EOSManager.Instance != null)
                go.transform.SetParent(EOSManager.Instance.transform);
            else
                DontDestroyOnLoad(go);

            var playerState = go.AddComponent<NetworkPlayerState>();
            var netObj = playerState.Net;
            netObj.NetworkId = networkId;
            netObj.PrefabId = NetworkPlayerState.PREFAB_ID;
            netObj.OwnerId = localPuid; // null in offline mode
            netObj.DestroyWithOwner = true;
            netObj.IsRegistered = true;
            _objects[networkId] = netObj;
            if (localPuid != null)
                _playerStates[localPuid] = playerState;
            LocalPlayerState = playerState;

            if (OfflineMode)
                _offlineOwnedNetworkIds.Add(networkId);

            netObj.NotifyNetworkSpawn();

            // Initialize display name from registry
            playerState.AutoInitDisplayName();

            // Mark as spectator if local peer is spectating
            if (IsSpectator)
                playerState.SetCustom("_spectator", "1");

            // Broadcast spawn to all peers (skip in offline mode)
            if (!OfflineMode)
                BroadcastSpawn(netObj);

            EOSDebugLogger.Log(DebugCategory.EOSManager, "NetworkManager",
                $"Local PlayerState created (id={networkId}){(OfflineMode ? " [offline]" : "")}");
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

        /// <summary>
        /// Send a message to all peers interested in a specific object.
        /// Falls back to Router.SendToAll when interest management is disabled.
        /// </summary>
        private void SendToInterestedPeers(byte msgId, NetWriter writer, NetworkObject obj,
            PacketReliability reliability, byte channel)
        {
            var im = InterestManagementEnabled ? InterestManager.Instance : null;
            if (im == null)
            {
                Router.SendToAll(msgId, writer, reliability, channel);
                return;
            }

            im.GetInterestedPeers(obj, _interestedPeersBuffer);
            foreach (var peer in _interestedPeersBuffer)
                Router.SendToPeer(msgId, writer, peer, reliability, channel);
        }

        /// <summary>
        /// Send a message to all peers interested in a specific object, excluding spectators.
        /// Falls back to SendToNonSpectators when interest management is disabled.
        /// </summary>
        private void SendToInterestedNonSpectators(byte msgId, NetWriter writer, NetworkObject obj)
        {
            var im = InterestManagementEnabled ? InterestManager.Instance : null;
            if (im == null)
            {
                SendToNonSpectators(msgId, writer);
                return;
            }

            im.GetInterestedPeers(obj, _interestedPeersBuffer);
            foreach (var peer in _interestedPeersBuffer)
            {
                if (!_spectators.Contains(peer))
                    Router.SendToPeer(msgId, writer, peer, PacketReliability.ReliableOrdered, 1);
            }
        }

        /// <summary>
        /// Get all registered child NetworkObjects belonging to a root parent.
        /// Populates _childrenBuffer (cleared first). Returns the buffer for convenience.
        /// </summary>
        private List<NetworkObject> GetRegisteredChildren(uint parentNetworkId)
        {
            _childrenBuffer.Clear();
            foreach (var obj in _objects.Values)
            {
                if (obj != null && obj.ParentNetworkId == parentNetworkId)
                    _childrenBuffer.Add(obj);
            }
            return _childrenBuffer;
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
        /// Register a NetworkObject hierarchy (root + children) created outside of Spawn().
        /// Discovers all child NetworkObjects via GetComponentsInChildren and assigns NetworkIds.
        /// </summary>
        public void RegisterExistingHierarchy(NetworkObject root, uint rootNetworkId)
        {
            root.NetworkId = rootNetworkId;
            root.ParentNetworkId = 0;
            root.IsRegistered = true;
            _objects[rootNetworkId] = root;
            root.NotifyNetworkSpawn();

            var allNetObjs = root.GetComponentsInChildren<NetworkObject>(true);
            var origChildren = new List<(uint, byte)>();
            for (int i = 1; i < allNetObjs.Length; i++)
            {
                var child = allNetObjs[i];
                uint childId = GenerateNetworkId();
                child.NetworkId = childId;
                child.PrefabId = root.PrefabId;
                child.OwnerId = root.OwnerId;
                child.ParentNetworkId = rootNetworkId;
                child.OriginalParentNetworkId = rootNetworkId;
                child.IsRegistered = true;
                _objects[childId] = child;
                origChildren.Add((childId, (byte)i));
                child.NotifyNetworkSpawn();
            }
            if (origChildren.Count > 0)
                _originalChildren[rootNetworkId] = origChildren;
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

            // Two-pass: register roots first, then children
            // Pass 1: Roots (objects whose parent transform has no NetworkObject above them)
            foreach (var obj in sceneObjects)
            {
                if (obj.IsRegistered) continue;

                // Check if any ancestor has a NetworkObject (would make this a child)
                bool isChild = false;
                var parentTransform = obj.transform.parent;
                while (parentTransform != null)
                {
                    if (parentTransform.GetComponent<NetworkObject>() != null) { isChild = true; break; }
                    parentTransform = parentTransform.parent;
                }
                if (isChild) continue; // handled in pass 2

                uint sceneNetId = 0xFFFF0000u | (FnvHash(GetHierarchyPath(obj.transform)) & 0xFFFFu);
                while (_objects.ContainsKey(sceneNetId)) sceneNetId++;

                obj.NetworkId = sceneNetId;
                obj.ParentNetworkId = 0;
                obj.IsRegistered = true;
                _objects[sceneNetId] = obj;

                if (obj.OwnerId == null && IsHost)
                {
                    obj.OwnerId = localPuid;
                    var peers = EOSP2PManager.Instance?.Peers;
                    if (peers != null && peers.Count > 0)
                    {
                        var writer = NetWriterPool.Get();
                        writer.WriteUInt32(obj.NetworkId);
                        writer.WriteProductUserId(localPuid);
                        Router.SendToAll(MSG_AUTHORITY, writer, PacketReliability.ReliableOrdered, 1);
                        NetWriterPool.Return(writer);
                    }
                }

                obj.NotifyNetworkSpawn();
            }

            // Pass 2: Children (objects with a NetworkObject ancestor)
            foreach (var obj in sceneObjects)
            {
                if (obj.IsRegistered) continue;

                // Find the nearest ancestor NetworkObject (the root)
                NetworkObject rootObj = null;
                var parentTransform = obj.transform.parent;
                while (parentTransform != null)
                {
                    var parentNetObj = parentTransform.GetComponent<NetworkObject>();
                    if (parentNetObj != null && parentNetObj.IsRegistered) { rootObj = parentNetObj; break; }
                    parentTransform = parentTransform.parent;
                }

                // Walk up to the actual root (in case of multi-level nesting)
                while (rootObj != null && rootObj.ParentNetworkId != 0 && _objects.TryGetValue(rootObj.ParentNetworkId, out var grandparent))
                    rootObj = grandparent;

                uint sceneNetId = 0xFFFF0000u | (FnvHash(GetHierarchyPath(obj.transform)) & 0xFFFFu);
                while (_objects.ContainsKey(sceneNetId)) sceneNetId++;

                uint rootNetId = rootObj?.NetworkId ?? 0;
                obj.NetworkId = sceneNetId;
                obj.ParentNetworkId = rootNetId;
                obj.OriginalParentNetworkId = rootNetId;
                obj.OwnerId = rootObj?.OwnerId;
                obj.IsRegistered = true;
                _objects[sceneNetId] = obj;

                // Track in _originalChildren for the root
                if (rootNetId != 0)
                {
                    if (!_originalChildren.TryGetValue(rootNetId, out var origList))
                    {
                        origList = new List<(uint, byte)>();
                        _originalChildren[rootNetId] = origList;
                    }
                    // For scene objects, localIndex is approximate (scene hierarchy order)
                    origList.Add((sceneNetId, (byte)origList.Count));
                }

                obj.NotifyNetworkSpawn();

                EOSDebugLogger.Log(DebugCategory.EOSManager, "NetworkManager",
                    $"Scene child '{obj.name}' ({sceneNetId}) registered under root {obj.ParentNetworkId}");
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
