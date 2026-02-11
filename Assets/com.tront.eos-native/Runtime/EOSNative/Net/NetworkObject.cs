using System;
using System.Collections.Generic;
using Epic.OnlineServices;
using EOSNative.P2P;
using UnityEngine;

namespace EOSNative.Net
{
    /// <summary>
    /// Core networking component. Manages identity, ownership, and a registry of SyncVars.
    /// Attach to any GameObject that needs to be synchronized across the P2P mesh.
    ///
    /// SyncVars are registered via <see cref="Sync{T}"/> in Awake — no reflection needed.
    /// Only the owner can write SyncVars; changes are automatically detected and sent as deltas.
    /// </summary>
    public class NetworkObject : MonoBehaviour
    {
        #region Identity

        /// <summary>Unique network ID. Upper 16 bits = owner hash, lower 16 bits = local counter.</summary>
        public uint NetworkId { get; internal set; }

        /// <summary>Index into NetworkManager's prefab registry. Used for spawning on remote peers.</summary>
        public ushort PrefabId { get; internal set; }

        /// <summary>
        /// NetworkId of the root NetworkObject in a nested hierarchy. 0 = this is the root.
        /// Child NetworkObjects share the root's PrefabId, OwnerId, and DestroyWithOwner.
        /// Despawn/TransferAuthority must be called on the root — children cascade automatically.
        /// Changes at runtime when reparented via <see cref="SetNetworkParent"/> / <see cref="DetachFromNetworkParent"/>.
        /// </summary>
        public uint ParentNetworkId { get; internal set; }

        /// <summary>
        /// NetworkId of the parent this object was originally spawned with. Set once at spawn, never changes.
        /// Used to distinguish original prefab children from dynamically reparented objects.
        /// 0 = this object was spawned as a root (even if later attached to another object).
        /// </summary>
        internal uint OriginalParentNetworkId { get; set; }

        /// <summary>True if this is a child NetworkObject (has a parent root).</summary>
        public bool IsChildNetworkObject => ParentNetworkId != 0;

        /// <summary>True if this is a root NetworkObject (no parent).</summary>
        public bool IsRootNetworkObject => ParentNetworkId == 0;

        #endregion

        #region Ownership

        /// <summary>The ProductUserId of the peer that owns this object.</summary>
        public ProductUserId OwnerId { get; internal set; }

        /// <summary>
        /// If true, this object is destroyed when the owner disconnects instead of being
        /// transferred to the new host. Useful for player avatars and per-player objects.
        /// Default is false (objects persist and transfer to host on owner disconnect).
        /// </summary>
        [SerializeField] private bool _destroyWithOwner;
        public bool DestroyWithOwner { get => _destroyWithOwner; set => _destroyWithOwner = value; }

        /// <summary>
        /// If true, this object survives owner disconnection during the reconnect grace period.
        /// Host temporarily claims authority. If owner reconnects within the grace period,
        /// ownership is restored. If the grace period expires, DestroyWithOwner takes effect
        /// (or host keeps permanently if DestroyWithOwner is false).
        /// </summary>
        [SerializeField] private bool _persistOnDisconnect;
        public bool PersistOnDisconnect { get => _persistOnDisconnect; set => _persistOnDisconnect = value; }

        /// <summary>True if the local peer owns this object.</summary>
        public bool IsOwner
        {
            get
            {
                if (OwnerId != null)
                    return OwnerId == EOSManager.s_Instance?.LocalProductUserId;
                // Offline mode fallback: OwnerId is null but we track ownership via NetworkManager
                var nm = NetworkManager._instance;
                return nm != null && nm.IsLocallyOwnedOffline(NetworkId);
            }
        }

        /// <summary>True if the local peer is the current host (lowest PUID).</summary>
        public bool IsHost => NetworkManager._instance != null && NetworkManager._instance.IsHost;

        /// <summary>
        /// If true, this object is always replicated to all peers regardless of spatial interest.
        /// Use for important game objects that should always be visible (objectives, world anchors, etc.).
        /// NetworkRoomState and NetworkPlayerState are automatically always-visible.
        /// </summary>
        [SerializeField] private bool _alwaysVisible;
        public bool AlwaysVisible { get => _alwaysVisible; set => _alwaysVisible = value; }

        #endregion

        #region SyncVar Registry

        private readonly List<ISyncVar> _syncVars = new();
        private bool _isDirty;

        /// <summary>Cached behaviours array. Set in NotifyNetworkSpawn.</summary>
        internal NetworkBehaviour[] _behaviours;

        /// <summary>
        /// Sequence number for state updates. Incremented by owner on each dirty write.
        /// Remote peers track last received sequence and discard stale/out-of-order updates.
        /// </summary>
        internal ushort SyncSequence { get; set; }
        internal ushort LastReceivedSequence { get; set; }

        /// <summary>Time when the last unreliable state was sent. Used for reliable fallback.</summary>
        internal float LastUnreliableSendTime { get; set; }

        /// <summary>Whether a reliable fallback is pending (unreliable was sent but not re-dirtied).</summary>
        internal bool ReliableFallbackPending { get; set; }

        /// <summary>Whether this object has any dirty SyncVars pending sync.</summary>
        internal bool IsDirty => _isDirty;

        /// <summary>Whether this object has been registered with the NetworkManager.</summary>
        public bool IsRegistered { get; internal set; }

        /// <summary>Number of SyncVars on this NetworkObject (direct, not including NB SyncVars).</summary>
        public int SyncVarCount => _syncVars.Count;

        /// <summary>
        /// Total number of SyncVars across this NetworkObject and all its NetworkBehaviours.
        /// </summary>
        public int TotalSyncVarCount
        {
            get
            {
                int total = _syncVars.Count;
                if (_behaviours != null)
                    for (int i = 0; i < _behaviours.Length; i++)
                        total += _behaviours[i]._syncVars.Count;
                return total;
            }
        }

        /// <summary>
        /// Create and register a SyncVar on this NetworkObject directly.
        /// For SyncVars scoped to a NetworkBehaviour, use <see cref="NetworkBehaviour.Sync{T}"/> instead.
        /// Call in Awake() to define synchronized state.
        /// </summary>
        public SyncVar<T> Sync<T>(T defaultValue = default, SyncVarWriteAccess writeAccess = SyncVarWriteAccess.Owner)
        {
            if (_syncVars.Count >= 32)
                throw new InvalidOperationException(
                    $"NetworkObject on '{name}' has 32 SyncVars — max supported. Split into multiple components.");

            byte index = (byte)_syncVars.Count;
            var syncVar = new SyncVar<T>(this, defaultValue, index, writeAccess);
            _syncVars.Add(syncVar);
            return syncVar;
        }

        /// <summary>
        /// Create and register a SyncList on this NetworkObject directly.
        /// For SyncLists scoped to a NetworkBehaviour, use <see cref="NetworkBehaviour.SyncList{T}"/> instead.
        /// </summary>
        public SyncList<T> SyncList<T>(List<T> initial = null, SyncVarWriteAccess writeAccess = SyncVarWriteAccess.Owner)
        {
            if (_syncVars.Count >= 32)
                throw new InvalidOperationException(
                    $"NetworkObject on '{name}' has 32 SyncVars/SyncLists — max supported.");

            byte index = (byte)_syncVars.Count;
            var syncList = new SyncList<T>(this, initial ?? new List<T>(), index, writeAccess);
            _syncVars.Add(syncList);
            return syncList;
        }

        /// <summary>
        /// Create and register a SyncDictionary on this NetworkObject directly.
        /// For SyncDictionaries scoped to a NetworkBehaviour, use <see cref="NetworkBehaviour.SyncDictionary{TKey,TValue}"/> instead.
        /// </summary>
        public SyncDictionary<TKey, TValue> SyncDictionary<TKey, TValue>(Dictionary<TKey, TValue> initial = null, SyncVarWriteAccess writeAccess = SyncVarWriteAccess.Owner)
        {
            if (_syncVars.Count >= 32)
                throw new InvalidOperationException(
                    $"NetworkObject on '{name}' has 32 SyncVars/SyncLists/SyncDicts — max supported.");

            byte index = (byte)_syncVars.Count;
            var syncDict = new SyncDictionary<TKey, TValue>(this, initial ?? new Dictionary<TKey, TValue>(), index, writeAccess);
            _syncVars.Add(syncDict);
            return syncDict;
        }

        /// <summary>
        /// Create and register a SyncHashSet on this NetworkObject directly.
        /// For SyncHashSets scoped to a NetworkBehaviour, use <see cref="NetworkBehaviour.SyncHashSet{T}"/> instead.
        /// </summary>
        public SyncHashSet<T> SyncHashSet<T>(HashSet<T> initial = null, SyncVarWriteAccess writeAccess = SyncVarWriteAccess.Owner)
        {
            if (_syncVars.Count >= 32)
                throw new InvalidOperationException(
                    $"NetworkObject on '{name}' has 32 SyncVars/SyncLists/SyncDicts/SyncHashSets — max supported.");

            byte index = (byte)_syncVars.Count;
            var syncSet = new SyncHashSet<T>(this, initial ?? new HashSet<T>(), index, writeAccess);
            _syncVars.Add(syncSet);
            return syncSet;
        }

        /// <summary>
        /// Returns the most permissive WriteAccess among all SyncVars on this object and its behaviours.
        /// Used by HandleStateUpdate to determine if a non-owner sender is valid.
        /// </summary>
        internal SyncVarWriteAccess MaxWriteAccess
        {
            get
            {
                var max = SyncVarWriteAccess.Owner;
                // Check self SyncVars
                for (int i = 0; i < _syncVars.Count; i++)
                {
                    if (_syncVars[i].WriteAccess > max)
                        max = _syncVars[i].WriteAccess;
                    if (max == SyncVarWriteAccess.All)
                        return max;
                }
                // Check NB SyncVars
                if (_behaviours != null)
                {
                    for (int b = 0; b < _behaviours.Length; b++)
                    {
                        var nbMax = _behaviours[b].MaxWriteAccessNB;
                        if (nbMax > max) max = nbMax;
                        if (max == SyncVarWriteAccess.All) return max;
                    }
                }
                return max;
            }
        }

        /// <summary>Cached SyncVarLOD component (null if none). Set on first MarkDirty call.</summary>
        private SyncVarLOD _lod;
        private bool _lodChecked;

        /// <summary>Called by SyncVar setters when a value changes (from self or NB bubbling up).</summary>
        internal void MarkDirty()
        {
            if (_isDirty) return;

            // LOD throttle: if SyncVarLOD is present, it may suppress this dirty flag
            if (!_lodChecked)
            {
                _lod = GetComponent<SyncVarLOD>();
                _lodChecked = true;
            }
            if (_lod != null && !_lod.ShouldPropagateDirty())
                return;

            _isDirty = true;
            if (IsRegistered)
                NetworkManager.Instance?.OnObjectDirty(this);
        }

        #endregion

        #region Serialization

        /// <summary>
        /// Write only dirty SyncVars using the sectioned format.
        /// Format: [sectionCount: byte] [foreach: componentIndex + mask + values]
        /// componentIndex 0xFF = self (NetworkObject), 0+ = NetworkBehaviour by GetComponents order.
        /// </summary>
        internal void SerializeDirty(NetWriter writer)
        {
            // Count dirty sections
            byte sectionCount = 0;
            bool selfDirty = HasDirtySelfSyncVars();
            if (selfDirty) sectionCount++;
            if (_behaviours != null)
                for (int b = 0; b < _behaviours.Length; b++)
                    if (_behaviours[b]._isDirty && _behaviours[b]._syncVars.Count > 0) sectionCount++;

            writer.WriteByte(sectionCount);

            // Self section
            if (selfDirty)
            {
                writer.WriteByte(SELF_COMPONENT_INDEX); // 0xFF
                uint mask = BuildDirtyMask();
                if (_lod != null) mask &= _lod.CurrentSyncVarMask;
                WriteMask(writer, mask);
                for (int i = 0; i < _syncVars.Count; i++)
                {
                    if ((mask & (1u << i)) != 0)
                        _syncVars[i].WriteTo(writer);
                }
            }

            // NB sections
            if (_behaviours != null)
            {
                for (int b = 0; b < _behaviours.Length; b++)
                {
                    var nb = _behaviours[b];
                    if (!nb._isDirty || nb._syncVars.Count == 0) continue;
                    writer.WriteByte(nb.ComponentIndex);
                    nb.SerializeDirtyNB(writer);
                }
            }
        }

        /// <summary>
        /// Read dirty SyncVars from the sectioned format.
        /// </summary>
        internal void DeserializeDirty(NetReader reader)
        {
            byte sectionCount = reader.ReadByte();
            for (int s = 0; s < sectionCount; s++)
            {
                byte compIdx = reader.ReadByte();
                if (compIdx == SELF_COMPONENT_INDEX)
                {
                    // Self section
                    uint mask = ReadMask(reader);
                    for (int i = 0; i < _syncVars.Count; i++)
                    {
                        if ((mask & (1u << i)) != 0)
                            _syncVars[i].ReadFrom(reader);
                    }
                }
                else if (_behaviours != null && compIdx < _behaviours.Length)
                {
                    _behaviours[compIdx].DeserializeDirtyNB(reader);
                }
                // else: unknown component index — data is unrecoverable, break
            }
        }

        /// <summary>
        /// Write ALL SyncVar/SyncList full state (for spawn/snapshot) using sectioned format.
        /// Format: [sectionCount: byte] [foreach: componentIndex + all values]
        /// </summary>
        internal void SerializeAll(NetWriter writer)
        {
            // Count sections with SyncVars
            byte sectionCount = 0;
            if (_syncVars.Count > 0) sectionCount++;
            if (_behaviours != null)
                for (int b = 0; b < _behaviours.Length; b++)
                    if (_behaviours[b]._syncVars.Count > 0) sectionCount++;

            writer.WriteByte(sectionCount);

            // Self
            if (_syncVars.Count > 0)
            {
                writer.WriteByte(SELF_COMPONENT_INDEX);
                for (int i = 0; i < _syncVars.Count; i++)
                    _syncVars[i].WriteFullState(writer);
            }

            // NB's
            if (_behaviours != null)
            {
                for (int b = 0; b < _behaviours.Length; b++)
                {
                    var nb = _behaviours[b];
                    if (nb._syncVars.Count == 0) continue;
                    writer.WriteByte(nb.ComponentIndex);
                    nb.SerializeAllNB(writer);
                }
            }
        }

        /// <summary>
        /// Read ALL SyncVar/SyncList full state (from spawn/snapshot) using sectioned format.
        /// </summary>
        internal void DeserializeAll(NetReader reader)
        {
            byte sectionCount = reader.ReadByte();
            for (int s = 0; s < sectionCount; s++)
            {
                byte compIdx = reader.ReadByte();
                if (compIdx == SELF_COMPONENT_INDEX)
                {
                    for (int i = 0; i < _syncVars.Count; i++)
                        _syncVars[i].ReadFullState(reader);
                }
                else if (_behaviours != null && compIdx < _behaviours.Length)
                {
                    _behaviours[compIdx].DeserializeAllNB(reader);
                }
            }
        }

        /// <summary>Clear dirty flags on all SyncVars (self and all behaviours).</summary>
        internal void ClearDirty()
        {
            _isDirty = false;
            for (int i = 0; i < _syncVars.Count; i++)
                _syncVars[i].ClearDirty();
            if (_behaviours != null)
                for (int b = 0; b < _behaviours.Length; b++)
                    _behaviours[b].ClearDirtyNB();
        }

        #endregion

        #region Dirty Mask (self SyncVars only)

        private bool HasDirtySelfSyncVars()
        {
            for (int i = 0; i < _syncVars.Count; i++)
                if (_syncVars[i].IsDirty) return true;
            return false;
        }

        private uint BuildDirtyMask()
        {
            uint mask = 0;
            for (int i = 0; i < _syncVars.Count; i++)
            {
                if (_syncVars[i].IsDirty)
                    mask |= (1u << i);
            }
            return mask;
        }

        // Adaptive mask size: <=8 vars = 1 byte, <=16 = 2 bytes, else = 4 bytes
        private void WriteMask(NetWriter writer, uint mask)
        {
            if (_syncVars.Count <= 8)
                writer.WriteByte((byte)mask);
            else if (_syncVars.Count <= 16)
                writer.WriteUInt16((ushort)mask);
            else
                writer.WriteUInt32(mask);
        }

        private uint ReadMask(NetReader reader)
        {
            if (_syncVars.Count <= 8)
                return reader.ReadByte();
            if (_syncVars.Count <= 16)
                return reader.ReadUInt16();
            return reader.ReadUInt32();
        }

        #endregion

        #region Events

        /// <summary>Fired when ownership changes. Args: (oldOwner, newOwner).</summary>
        public event Action<ProductUserId, ProductUserId> OnOwnerChanged;

        /// <summary>Fired when this object is reparented. Args: (oldParent, newParent). Null = no parent (root).</summary>
        public event Action<NetworkObject, NetworkObject> OnReparented;

        /// <summary>Fired when this object is spawned on the network.</summary>
        public event Action OnNetworkSpawn;

        /// <summary>Fired when this object is despawned from the network.</summary>
        public event Action OnNetworkDespawn;

        /// <summary>Invoke the OnOwnerChanged event and NetworkBehaviour lifecycle hooks.</summary>
        internal void NotifyOwnerChanged(ProductUserId oldOwner, ProductUserId newOwner)
        {
            if (_behaviours != null)
            {
                var nm = NetworkManager._instance; // avoid auto-create
                var localPuid = EOSManager.s_Instance?.LocalProductUserId;

                // Determine if local player was/is the owner
                bool wasLocalOwner = localPuid != null && oldOwner != null && oldOwner == localPuid;
                bool isLocalOwner = localPuid != null && newOwner != null && newOwner == localPuid;

                // In offline mode, all spawned objects are owned by local player
                if (nm != null && nm.OfflineMode)
                {
                    wasLocalOwner = oldOwner != null || nm.IsLocallyOwnedOffline(NetworkId);
                    isLocalOwner = true;
                }

                for (int i = 0; i < _behaviours.Length; i++)
                {
                    _behaviours[i].OnOwnershipChanged(oldOwner);
                    if (!wasLocalOwner && isLocalOwner)
                        _behaviours[i].OnStartOwner();
                    else if (wasLocalOwner && !isLocalOwner)
                        _behaviours[i].OnStopOwner();
                }
            }

            OnOwnerChanged?.Invoke(oldOwner, newOwner);
        }

        /// <summary>Invoke the OnReparented event.</summary>
        internal void NotifyReparented(NetworkObject oldParent, NetworkObject newParent)
        {
            OnReparented?.Invoke(oldParent, newParent);
        }

        /// <summary>
        /// Detach this child from its network parent, making it an independent root object.
        /// Only callable by the owner or host. World position/rotation are preserved.
        /// </summary>
        public void DetachFromNetworkParent()
        {
            NetworkManager.Instance?.ReparentObject(this, null);
        }

        /// <summary>
        /// Set a new network parent for this object. The target must be a root NetworkObject.
        /// Only callable by the owner or host. World position/rotation are preserved.
        /// Pass null to detach (equivalent to <see cref="DetachFromNetworkParent"/>).
        /// </summary>
        public void SetNetworkParent(NetworkObject newParent)
        {
            NetworkManager.Instance?.ReparentObject(this, newParent);
        }

        /// <summary>
        /// Sentinel component index used for RPCs declared directly on a NetworkObject subclass.
        /// NetworkBehaviours use indices 0, 1, 2, ... based on GetComponents order.
        /// </summary>
        internal const byte SELF_COMPONENT_INDEX = 0xFF;

        /// <summary>
        /// Weaver-generated override that registers all [NetRpc] handlers declared directly on this NetworkObject.
        /// Called by NotifyNetworkSpawn before processing NetworkBehaviours. Do not call manually.
        /// </summary>
        internal virtual void __RegisterNetRPCs() { }

        /// <summary>Invoke the OnNetworkSpawn event and NetworkBehaviour lifecycle hooks.</summary>
        internal void NotifyNetworkSpawn()
        {
            // Register RPCs declared directly on this NetworkObject subclass (if any)
            __RegisterNetRPCs();

            // Cache behaviours and assign component indices
            _behaviours = GetComponents<NetworkBehaviour>();
            for (int i = 0; i < _behaviours.Length; i++)
            {
                _behaviours[i].Net = this; // Ensure Net is set even if Awake didn't run (inactive clones)
                _behaviours[i].ComponentIndex = (byte)i;
                _behaviours[i].__RegisterNetRPCs();
                _behaviours[i].OnNetworkSpawn();
            }
            OnNetworkSpawn?.Invoke();
        }

        /// <summary>Invoke the OnNetworkDespawn event and NetworkBehaviour lifecycle hooks.</summary>
        internal void NotifyNetworkDespawn()
        {
            if (_behaviours != null)
                for (int i = 0; i < _behaviours.Length; i++)
                    _behaviours[i].OnNetworkDespawn();
            OnNetworkDespawn?.Invoke();
        }

        /// <summary>Write per-NB spawn payload (length-prefixed sections).</summary>
        internal void WriteSpawnPayload(NetWriter writer)
        {
            if (_behaviours == null || _behaviours.Length == 0)
            {
                writer.WriteByte(0); // section count
                return;
            }
            writer.WriteByte((byte)_behaviours.Length);
            for (int i = 0; i < _behaviours.Length; i++)
            {
                var sectionWriter = NetWriterPool.Get();
                _behaviours[i].WriteSpawnData(sectionWriter);
                var data = sectionWriter.ToArraySegment();
                writer.WriteUInt16((ushort)data.Count);
                if (data.Count > 0)
                    writer.WriteBytesRaw(data);
                NetWriterPool.Return(sectionWriter);
            }
        }

        /// <summary>Read per-NB spawn payload (length-prefixed sections).</summary>
        internal void ReadSpawnPayload(NetReader reader)
        {
            byte sectionCount = reader.ReadByte();
            for (int i = 0; i < sectionCount; i++)
            {
                ushort len = reader.ReadUInt16();
                if (_behaviours != null && i < _behaviours.Length && len > 0)
                    _behaviours[i].ReadSpawnData(reader);
                else if (len > 0)
                    reader.Skip(len); // skip unknown/extra section
            }
        }

        /// <summary>Dispatch OnTick to all behaviours on this object.</summary>
        internal void NotifyTick(uint tick)
        {
            if (_behaviours == null) return;
            for (int i = 0; i < _behaviours.Length; i++)
                _behaviours[i].OnTick(tick);
        }

        /// <summary>Dispatch OnPeerConnected to all behaviours on this object.</summary>
        internal void NotifyPeerConnected(ProductUserId peer)
        {
            if (_behaviours == null) return;
            for (int i = 0; i < _behaviours.Length; i++)
                _behaviours[i].OnPeerConnected(peer);
        }

        /// <summary>Dispatch OnPeerDisconnected to all behaviours on this object.</summary>
        internal void NotifyPeerDisconnected(ProductUserId peer)
        {
            if (_behaviours == null) return;
            for (int i = 0; i < _behaviours.Length; i++)
                _behaviours[i].OnPeerDisconnected(peer);
        }

        #endregion

        private void OnDestroy()
        {
            if (IsRegistered)
                NetworkManager.Instance?.OnObjectDestroyed(this);
        }
    }
}
