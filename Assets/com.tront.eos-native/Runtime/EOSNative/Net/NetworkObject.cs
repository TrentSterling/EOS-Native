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

        #endregion

        #region Ownership

        /// <summary>The ProductUserId of the peer that owns this object.</summary>
        public ProductUserId OwnerId { get; internal set; }

        /// <summary>
        /// If true, this object is destroyed when the owner disconnects instead of being
        /// transferred to the new host. Useful for player avatars and per-player objects.
        /// Default is false (objects persist and transfer to host on owner disconnect).
        /// </summary>
        [SerializeField]
        public bool DestroyWithOwner { get; set; } = false;

        /// <summary>True if the local peer owns this object.</summary>
        public bool IsOwner => OwnerId != null && OwnerId == EOSManager.Instance?.LocalProductUserId;

        /// <summary>True if the local peer is the current host (lowest PUID).</summary>
        public bool IsHost => NetworkManager.Instance != null && NetworkManager.Instance.IsHost;

        /// <summary>
        /// If true, this object is always replicated to all peers regardless of spatial interest.
        /// Use for important game objects that should always be visible (objectives, world anchors, etc.).
        /// NetworkRoomState and NetworkPlayerState are automatically always-visible.
        /// </summary>
        public bool AlwaysVisible { get; set; }

        #endregion

        #region SyncVar Registry

        private readonly List<ISyncVar> _syncVars = new();
        private bool _isDirty;

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

        /// <summary>Number of registered SyncVars.</summary>
        public int SyncVarCount => _syncVars.Count;

        /// <summary>
        /// Create and register a SyncVar. Call in Awake() to define synchronized state.
        /// SyncVars are ordered — the order of Sync() calls determines their index.
        /// </summary>
        /// <typeparam name="T">Type to sync. Must be registered in <see cref="NetSerializers"/>.</typeparam>
        /// <param name="defaultValue">Initial value before any sync.</param>
        /// <param name="writeAccess">Who can write: Owner (default), Host, or All.</param>
        /// <returns>The SyncVar instance. Store as a field to get/set the value.</returns>
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
        /// Create and register a SyncList. Call in Awake() to define synchronized collections.
        /// SyncLists share the SyncVar index space — order of Sync()/SyncList() calls matters.
        /// </summary>
        /// <param name="writeAccess">Who can write: Owner (default), Host, or All.</param>
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
        /// Create and register a SyncDictionary. Call in Awake() to define synchronized key-value state.
        /// SyncDictionaries share the SyncVar index space — order of Sync()/SyncList()/SyncDictionary() calls matters.
        /// </summary>
        /// <param name="writeAccess">Who can write: Owner (default), Host, or All.</param>
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
        /// Returns the most permissive WriteAccess among all SyncVars on this object.
        /// Used by HandleStateUpdate to determine if a non-owner sender is valid.
        /// </summary>
        internal SyncVarWriteAccess MaxWriteAccess
        {
            get
            {
                var max = SyncVarWriteAccess.Owner;
                for (int i = 0; i < _syncVars.Count; i++)
                {
                    if (_syncVars[i].WriteAccess > max)
                        max = _syncVars[i].WriteAccess;
                    if (max == SyncVarWriteAccess.All)
                        return max; // early out — can't be more permissive
                }
                return max;
            }
        }

        /// <summary>Cached SyncVarLOD component (null if none). Set on first MarkDirty call.</summary>
        private SyncVarLOD _lod;
        private bool _lodChecked;

        /// <summary>Called by SyncVar setters when a value changes.</summary>
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
        /// Write only dirty SyncVars. Format: [dirtyMask: 1-4 bytes] + [values for set bits].
        /// When SyncVarLOD is present, the mask is ANDed with the tier's SyncVarMask to filter
        /// which SyncVars are sent at each distance tier.
        /// </summary>
        internal void SerializeDirty(NetWriter writer)
        {
            uint mask = BuildDirtyMask();

            // Apply LOD SyncVar mask — filter out SyncVars not needed at this tier
            if (_lod != null)
                mask &= _lod.CurrentSyncVarMask;

            WriteMask(writer, mask);

            for (int i = 0; i < _syncVars.Count; i++)
            {
                if ((mask & (1u << i)) != 0)
                    _syncVars[i].WriteTo(writer);
            }
        }

        /// <summary>
        /// Read dirty SyncVars from a delta update.
        /// </summary>
        internal void DeserializeDirty(NetReader reader)
        {
            uint mask = ReadMask(reader);

            for (int i = 0; i < _syncVars.Count; i++)
            {
                if ((mask & (1u << i)) != 0)
                    _syncVars[i].ReadFrom(reader);
            }
        }

        /// <summary>
        /// Write ALL SyncVar/SyncList full state (for spawn/snapshot). Format: [value0][value1]...
        /// Uses WriteFullState which handles SyncLists correctly (writes full list, not pending ops).
        /// </summary>
        internal void SerializeAll(NetWriter writer)
        {
            for (int i = 0; i < _syncVars.Count; i++)
                _syncVars[i].WriteFullState(writer);
        }

        /// <summary>
        /// Read ALL SyncVar/SyncList full state (from spawn/snapshot).
        /// Uses ReadFullState which handles SyncLists correctly (reads full list, not ops).
        /// </summary>
        internal void DeserializeAll(NetReader reader)
        {
            for (int i = 0; i < _syncVars.Count; i++)
                _syncVars[i].ReadFullState(reader);
        }

        /// <summary>Clear dirty flags on all SyncVars.</summary>
        internal void ClearDirty()
        {
            _isDirty = false;
            for (int i = 0; i < _syncVars.Count; i++)
                _syncVars[i].ClearDirty();
        }

        #endregion

        #region Dirty Mask

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

        /// <summary>Fired when this object is spawned on the network.</summary>
        public event Action OnNetworkSpawn;

        /// <summary>Fired when this object is despawned from the network.</summary>
        public event Action OnNetworkDespawn;

        /// <summary>Invoke the OnOwnerChanged event.</summary>
        internal void NotifyOwnerChanged(ProductUserId oldOwner, ProductUserId newOwner)
        {
            OnOwnerChanged?.Invoke(oldOwner, newOwner);
        }

        /// <summary>Invoke the OnNetworkSpawn event and NetworkBehaviour lifecycle hooks.</summary>
        internal void NotifyNetworkSpawn()
        {
            // Register weaver-generated RPCs and fire lifecycle on all behaviours
            var behaviours = GetComponents<NetworkBehaviour>();
            for (int i = 0; i < behaviours.Length; i++)
            {
                behaviours[i].__RegisterNetRPCs();
                behaviours[i].OnNetworkSpawn();
            }
            OnNetworkSpawn?.Invoke();
        }

        /// <summary>Invoke the OnNetworkDespawn event and NetworkBehaviour lifecycle hooks.</summary>
        internal void NotifyNetworkDespawn()
        {
            var behaviours = GetComponents<NetworkBehaviour>();
            for (int i = 0; i < behaviours.Length; i++)
                behaviours[i].OnNetworkDespawn();
            OnNetworkDespawn?.Invoke();
        }

        #endregion

        private void OnDestroy()
        {
            if (IsRegistered)
                NetworkManager.Instance?.OnObjectDestroyed(this);
        }
    }
}
