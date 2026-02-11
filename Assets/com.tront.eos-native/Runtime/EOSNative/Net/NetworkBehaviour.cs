using System;
using System.Collections.Generic;
using Epic.OnlineServices;
using EOSNative.P2P;
using UnityEngine;

namespace EOSNative.Net
{
    /// <summary>
    /// Optional convenience base class for networked components.
    /// Provides shortcuts to the NetworkObject's identity, ownership, and SyncVar creation.
    ///
    /// Each NetworkBehaviour has its own SyncVar pool (up to 32 per behaviour).
    /// SyncVars created via <see cref="Sync{T}"/> are scoped to this behaviour
    /// and serialized under its component index on the wire.
    ///
    /// Users can also skip this and reference <see cref="NetworkObject"/> directly via GetComponent.
    /// </summary>
    public abstract class NetworkBehaviour : MonoBehaviour
    {
        /// <summary>The NetworkObject on this GameObject. Auto-added if missing.</summary>
        public NetworkObject Net { get; internal set; }

        /// <summary>
        /// Index of this NetworkBehaviour within its NetworkObject's behaviour list.
        /// Assigned at spawn time based on GetComponents order. Used to scope RPCs and SyncVars
        /// to a specific behaviour — the wire format includes this index so each NB is individually addressable.
        /// </summary>
        public byte ComponentIndex { get; internal set; }

        /// <summary>This object's network ID (0 if not yet registered).</summary>
        public uint NetworkId => Net != null ? Net.NetworkId : 0;

        /// <summary>True if the local peer owns this object.</summary>
        public bool IsOwner => Net != null && Net.IsOwner;

        /// <summary>True if the local peer is the current host.</summary>
        public bool IsHost => Net != null && Net.IsHost;

        /// <summary>The ProductUserId of the owning peer.</summary>
        public ProductUserId OwnerId => Net?.OwnerId;

        /// <summary>True if the local peer is a spectator (read-only observer).</summary>
        public bool IsSpectator => NetworkManager.Instance?.IsSpectator ?? false;

        /// <summary>True if this object has been spawned and has a valid NetworkId.</summary>
        public bool IsSpawned => Net != null && Net.IsRegistered;

        /// <summary>True if this peer has authority over this object (owner, or host for unowned objects).</summary>
        public bool HasAuthority => IsOwner || (IsHost && OwnerId == null);

        /// <summary>True if connected to peers via P2P.</summary>
        public bool IsOnline => NetworkManager._instance?.IsOnline ?? false;

        /// <summary>True if running in offline mode (local-only networking).</summary>
        public bool IsOffline => NetworkManager._instance?.OfflineMode ?? false;

        /// <summary>Shortcut to NetworkManager.Instance.</summary>
        protected NetworkManager Manager => NetworkManager.Instance;

        /// <summary>Current simulation tick (0 if tick simulation is disabled).</summary>
        protected uint CurrentTick => TickSimulation.Instance?.CurrentTick ?? 0;

        /// <summary>Fixed tick delta time (0 if tick simulation is disabled).</summary>
        protected float FixedTickTime => TickSimulation.Instance?.FixedTickTime ?? 0f;

        #region Per-NB SyncVar Pool

        internal readonly List<ISyncVar> _syncVars = new();
        internal bool _isDirty;

        /// <summary>Number of SyncVars registered on this behaviour.</summary>
        public int SyncVarCount => _syncVars.Count;

        /// <summary>
        /// Create and register a SyncVar scoped to this NetworkBehaviour.
        /// Each NB has its own pool of up to 32 SyncVars.
        /// Call in Awake() after base.Awake().
        /// </summary>
        protected SyncVar<T> Sync<T>(T defaultValue = default, SyncVarWriteAccess writeAccess = SyncVarWriteAccess.Owner)
        {
            if (_syncVars.Count >= 32)
                throw new InvalidOperationException(
                    $"NetworkBehaviour '{GetType().Name}' on '{name}' has 32 SyncVars — max per behaviour.");

            byte index = (byte)_syncVars.Count;
            var syncVar = new SyncVar<T>(Net, defaultValue, index, writeAccess, MarkDirtyNB);
            _syncVars.Add(syncVar);
            return syncVar;
        }

        /// <summary>
        /// Create and register a SyncList scoped to this NetworkBehaviour.
        /// Call in Awake() after base.Awake().
        /// </summary>
        protected SyncList<T> SyncList<T>(List<T> initial = null, SyncVarWriteAccess writeAccess = SyncVarWriteAccess.Owner)
        {
            if (_syncVars.Count >= 32)
                throw new InvalidOperationException(
                    $"NetworkBehaviour '{GetType().Name}' on '{name}' has 32 SyncVars/SyncLists — max per behaviour.");

            byte index = (byte)_syncVars.Count;
            var syncList = new SyncList<T>(Net, initial ?? new List<T>(), index, writeAccess, MarkDirtyNB);
            _syncVars.Add(syncList);
            return syncList;
        }

        /// <summary>
        /// Create and register a SyncDictionary scoped to this NetworkBehaviour.
        /// Call in Awake() after base.Awake().
        /// </summary>
        protected SyncDictionary<TKey, TValue> SyncDictionary<TKey, TValue>(
            Dictionary<TKey, TValue> initial = null, SyncVarWriteAccess writeAccess = SyncVarWriteAccess.Owner)
        {
            if (_syncVars.Count >= 32)
                throw new InvalidOperationException(
                    $"NetworkBehaviour '{GetType().Name}' on '{name}' has 32 SyncVars/SyncDicts — max per behaviour.");

            byte index = (byte)_syncVars.Count;
            var syncDict = new SyncDictionary<TKey, TValue>(Net, initial ?? new Dictionary<TKey, TValue>(), index, writeAccess, MarkDirtyNB);
            _syncVars.Add(syncDict);
            return syncDict;
        }

        /// <summary>
        /// Create and register a SyncHashSet scoped to this NetworkBehaviour.
        /// Call in Awake() after base.Awake().
        /// </summary>
        protected SyncHashSet<T> SyncHashSet<T>(HashSet<T> initial = null, SyncVarWriteAccess writeAccess = SyncVarWriteAccess.Owner)
        {
            if (_syncVars.Count >= 32)
                throw new InvalidOperationException(
                    $"NetworkBehaviour '{GetType().Name}' on '{name}' has 32 SyncVars/SyncHashSets — max per behaviour.");

            byte index = (byte)_syncVars.Count;
            var syncSet = new SyncHashSet<T>(Net, initial ?? new HashSet<T>(), index, writeAccess, MarkDirtyNB);
            _syncVars.Add(syncSet);
            return syncSet;
        }

        /// <summary>Called by SyncVar setters when a value changes on this behaviour.</summary>
        private void MarkDirtyNB()
        {
            if (_isDirty) return;
            _isDirty = true;
            // Bubble up to NetworkObject so it enters the dirty list
            Net?.MarkDirty();
        }

        /// <summary>Whether this behaviour has dirty SyncVars.</summary>
        internal bool IsDirty => _isDirty;

        internal uint BuildDirtyMask()
        {
            uint mask = 0;
            for (int i = 0; i < _syncVars.Count; i++)
            {
                if (_syncVars[i].IsDirty)
                    mask |= (1u << i);
            }
            return mask;
        }

        internal void WriteMask(NetWriter writer, uint mask)
        {
            if (_syncVars.Count <= 8)
                writer.WriteByte((byte)mask);
            else if (_syncVars.Count <= 16)
                writer.WriteUInt16((ushort)mask);
            else
                writer.WriteUInt32(mask);
        }

        internal uint ReadMask(NetReader reader)
        {
            if (_syncVars.Count <= 8)
                return reader.ReadByte();
            if (_syncVars.Count <= 16)
                return reader.ReadUInt16();
            return reader.ReadUInt32();
        }

        internal void SerializeDirtyNB(NetWriter writer)
        {
            uint mask = BuildDirtyMask();
            WriteMask(writer, mask);
            for (int i = 0; i < _syncVars.Count; i++)
            {
                if ((mask & (1u << i)) != 0)
                    _syncVars[i].WriteTo(writer);
            }
        }

        internal void DeserializeDirtyNB(NetReader reader)
        {
            uint mask = ReadMask(reader);
            for (int i = 0; i < _syncVars.Count; i++)
            {
                if ((mask & (1u << i)) != 0)
                    _syncVars[i].ReadFrom(reader);
            }
        }

        internal void SerializeAllNB(NetWriter writer)
        {
            for (int i = 0; i < _syncVars.Count; i++)
                _syncVars[i].WriteFullState(writer);
        }

        internal void DeserializeAllNB(NetReader reader)
        {
            for (int i = 0; i < _syncVars.Count; i++)
                _syncVars[i].ReadFullState(reader);
        }

        internal void ClearDirtyNB()
        {
            _isDirty = false;
            for (int i = 0; i < _syncVars.Count; i++)
                _syncVars[i].ClearDirty();
        }

        /// <summary>
        /// Returns the most permissive WriteAccess among SyncVars on this behaviour.
        /// </summary>
        internal SyncVarWriteAccess MaxWriteAccessNB
        {
            get
            {
                var max = SyncVarWriteAccess.Owner;
                for (int i = 0; i < _syncVars.Count; i++)
                {
                    if (_syncVars[i].WriteAccess > max)
                        max = _syncVars[i].WriteAccess;
                    if (max == SyncVarWriteAccess.All)
                        return max;
                }
                return max;
            }
        }

        #endregion

        #region Convenience Methods

        /// <summary>Despawn this object from the network. Only callable by owner or host.</summary>
        public void Despawn()
        {
            if (Net != null) Manager?.Despawn(Net);
        }

        /// <summary>Transfer ownership of this object to another peer.</summary>
        public void GiveOwnership(ProductUserId newOwner)
        {
            if (Net != null) Manager?.TransferAuthority(Net, newOwner);
        }

        /// <summary>Release ownership — host will claim this object.</summary>
        public void RemoveOwnership()
        {
            if (Net != null) Manager?.TransferAuthority(Net, null);
        }

        #endregion

        #region Lifecycle Hooks

        /// <summary>
        /// Called by NetworkManager after NetworkId is assigned and the object is registered.
        /// Override for post-spawn initialization (e.g. subscribing to events that need NetworkId).
        /// </summary>
        public virtual void OnNetworkSpawn() { }

        /// <summary>
        /// Called when the object is about to be despawned from the network.
        /// Override for cleanup before the object is deactivated/pooled.
        /// </summary>
        public virtual void OnNetworkDespawn() { }

        /// <summary>Called when this peer becomes the host (e.g. after host migration).</summary>
        public virtual void OnStartHost() { }

        /// <summary>Called when this peer is no longer the host.</summary>
        public virtual void OnStopHost() { }

        /// <summary>Called when the local peer gains ownership of this object.</summary>
        public virtual void OnStartOwner() { }

        /// <summary>Called when the local peer loses ownership of this object (transferred away).</summary>
        public virtual void OnStopOwner() { }

        /// <summary>Called when this object's ownership changes. Args: previous owner PUID (null if unowned).</summary>
        public virtual void OnOwnershipChanged(ProductUserId previousOwner) { }

        /// <summary>
        /// Called on the spawning peer to write custom data into the spawn message.
        /// Override to inject initialization data that remote peers receive in <see cref="ReadSpawnData"/>.
        /// Called after SyncVars are serialized.
        /// </summary>
        public virtual void WriteSpawnData(NetWriter writer) { }

        /// <summary>
        /// Called on remote peers to read custom data from the spawn message.
        /// Override to consume data written by <see cref="WriteSpawnData"/>.
        /// Called after SyncVars are deserialized but before OnNetworkSpawn.
        /// </summary>
        public virtual void ReadSpawnData(NetReader reader) { }

        /// <summary>
        /// Called every tick when TickSimulation is active. Only invoked on spawned objects.
        /// Use for deterministic game logic that should run at a fixed rate.
        /// </summary>
        public virtual void OnTick(uint tick) { }

        /// <summary>Called when a remote peer connects. Only invoked on spawned objects.</summary>
        public virtual void OnPeerConnected(ProductUserId peer) { }

        /// <summary>Called when a remote peer disconnects. Only invoked on spawned objects.</summary>
        public virtual void OnPeerDisconnected(ProductUserId peer) { }

        #endregion

        /// <summary>
        /// Weaver-generated override that registers all [NetRpc] handlers on this behaviour.
        /// Called by NetworkManager after NetworkId is assigned. Do not call manually.
        /// </summary>
        internal virtual void __RegisterNetRPCs() { }

        protected virtual void Awake()
        {
            Net = GetComponent<NetworkObject>();
            if (Net == null)
                Net = gameObject.AddComponent<NetworkObject>();
        }
    }
}
