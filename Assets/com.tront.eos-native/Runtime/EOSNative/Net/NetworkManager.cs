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

        #endregion

        #region Singleton

        private static NetworkManager _instance;
        public static NetworkManager Instance
        {
            get
            {
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
                }

                NetWriterPool.Return(writer);
            }
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

        #endregion

        #region Private Fields

        private readonly Dictionary<uint, NetworkObject> _objects = new();
        private readonly List<NetworkObject> _dirtyObjects = new();
        private readonly Dictionary<RPCKey, Action<NetReader>> _rpcHandlers = new();
        private readonly Dictionary<RPCKey, string> _rpcMethodNames = new();
        private readonly List<RPCKey> _rpcKeysToRemove = new();

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
            public string MethodName;
            public RPCTarget Targets;
            public byte[] ArgData;
        }

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

            var p2p = EOSP2PManager.Instance;
            p2p.OnPacketReceived += Router.ProcessIncoming;

            Router.Register(MSG_STATE_UPDATE, HandleStateUpdate);
            Router.Register(MSG_SPAWN, HandleSpawn);
            Router.Register(MSG_DESPAWN, HandleDespawn);
            Router.Register(MSG_AUTHORITY, HandleAuthority);
            Router.Register(MSG_SNAPSHOT, HandleSnapshot);
            Router.Register(MSG_SNAPSHOT_REQUEST, HandleSnapshotRequest);
            Router.Register(MSG_RPC, HandleRPC);
            Router.Register(MSG_AUTHORITY_REQUEST, HandleAuthorityRequest);

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

            // Don't re-spawn if we already have it (e.g. we're the owner)
            if (_objects.ContainsKey(networkId)) return;

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

            EOSDebugLogger.Log(DebugCategory.EOSManager, "NetworkManager",
                $"Sending snapshot to {sender} ({_objects.Count} objects)");

            var writer = NetWriterPool.Get();
            writer.WritePackedUInt32((uint)_objects.Count);

            foreach (var obj in _objects.Values)
                WriteSpawnData(writer, obj);

            Router.SendToPeer(MSG_SNAPSHOT, writer, sender, PacketReliability.ReliableOrdered, 1);
            NetWriterPool.Return(writer);
        }

        private void HandleSnapshot(ProductUserId sender, NetReader reader)
        {
            uint count = reader.ReadPackedUInt32();

            EOSDebugLogger.Log(DebugCategory.EOSManager, "NetworkManager",
                $"Received snapshot with {count} objects from {sender}");

            for (uint i = 0; i < count; i++)
            {
                ushort prefabId = reader.ReadUInt16();
                uint networkId = reader.ReadUInt32();
                ProductUserId ownerId = reader.ReadProductUserId();
                Vector3 position = reader.ReadVector3();
                Quaternion rotation = reader.ReadQuaternion();
                bool destroyWithOwner = reader.ReadBool();
                byte syncVarCount = reader.ReadByte();

                if (_objects.ContainsKey(networkId))
                {
                    // Already have this object — update state
                    var existing = _objects[networkId];
                    if (syncVarCount > 0 && existing.SyncVarCount > 0)
                        existing.DeserializeAll(reader);
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

        #endregion

        #region Host Election

        private void RecomputeHost()
        {
            var localPuid = EOSManager.Instance?.LocalProductUserId;
            if (localPuid == null) return;

            string localStr = localPuid.ToString();
            string lowestStr = localStr;

            var peers = EOSP2PManager.Instance?.Peers;
            if (peers != null)
            {
                foreach (var peer in peers)
                {
                    string peerStr = peer.ToString();
                    if (string.Compare(peerStr, lowestStr, StringComparison.Ordinal) < 0)
                        lowestStr = peerStr;
                }
            }

            bool wasHost = IsHost;
            IsHost = (lowestStr == localStr);

            if (IsHost != wasHost)
            {
                EOSDebugLogger.Log(DebugCategory.EOSManager, "NetworkManager",
                    IsHost ? "Became host" : "No longer host");

                // When we become host, claim any ownerless scene objects
                if (IsHost)
                    RegisterSceneObjects();
            }
        }

        private ProductUserId GetHostPuid()
        {
            var localPuid = EOSManager.Instance?.LocalProductUserId;
            if (localPuid == null) return null;

            string lowestStr = localPuid.ToString();
            ProductUserId lowestPuid = localPuid;

            var peers = EOSP2PManager.Instance?.Peers;
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

            return lowestPuid;
        }

        #endregion

        #region Peer Events

        private void OnPeerConnected(ProductUserId peer)
        {
            RecomputeHost();

            // Initialize local ID prefix if not yet set
            InitLocalIdPrefix();

            // If we're the host, a new peer will send us a SNAPSHOT_REQUEST
            // If we're not the host but just connected, request a snapshot
            if (!IsHost && _objects.Count == 0)
                RequestSnapshot();
        }

        private void OnPeerDisconnected(ProductUserId peer)
        {
            // Enter migration window — buffer host/owner-targeted RPCs
            _migrationInProgress = true;

            RecomputeHost();

            // If we became the host, claim orphaned objects
            if (IsHost)
                ClaimOrphanedObjects(peer);

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

                uint nameHash = FnvHash(buffered.MethodName);

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

        #region Utilities

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

        /// <summary>Register a NetworkObject that was created outside of Spawn() (e.g. scene objects).</summary>
        public void RegisterExisting(NetworkObject obj, uint networkId)
        {
            obj.NetworkId = networkId;
            obj.IsRegistered = true;
            _objects[networkId] = obj;
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
    }
}
