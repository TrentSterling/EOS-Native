using System;
using System.Collections;
using System.Collections.Generic;
using EOSNative.P2P;

namespace EOSNative.Net
{
    /// <summary>
    /// Operation types for SyncHashSet change tracking.
    /// </summary>
    public enum SyncHashSetOp : byte
    {
        Add = 0,
        Remove = 1,
        Clear = 2
    }

    /// <summary>
    /// Synchronized hash set with operation-based delta sync.
    /// Only changed operations are sent over the network — not the entire set.
    ///
    /// Owner can modify freely. Remote peers receive operations and apply them in order.
    /// Full state is serialized for spawn/snapshot (late join).
    ///
    /// Usage:
    /// <code>
    /// SyncHashSet&lt;string&gt; StatusEffects;
    ///
    /// void Awake() {
    ///     StatusEffects = SyncHashSet&lt;string&gt;(new HashSet&lt;string&gt;());
    ///     StatusEffects.OnChanged += (op, item) =&gt;
    ///         Debug.Log($"{op}: {item}");
    /// }
    ///
    /// void ApplyPoison() {
    ///     if (!IsOwner) return;
    ///     StatusEffects.Add("Poison"); // Synced automatically
    /// }
    /// </code>
    /// </summary>
    public class SyncHashSet<T> : ISyncVar, IReadOnlyCollection<T>
    {
        private readonly HashSet<T> _set;
        private readonly List<SetChange> _pendingOps = new();
        private readonly NetworkObject _owner;
        private readonly Action _notifyDirty;
        private readonly byte _index;
        private bool _dirty;
        private readonly SyncVarWriteAccess _writeAccess;

        /// <summary>
        /// Fires when the set changes. Args: (operation, item).
        /// On owner: fires immediately. On remotes: fires when the delta arrives.
        /// </summary>
        public event Action<SyncHashSetOp, T> OnChanged;

        /// <summary>Who can write to this SyncHashSet.</summary>
        public SyncVarWriteAccess WriteAccess => _writeAccess;

        #region Constructor

        internal SyncHashSet(NetworkObject owner, HashSet<T> initial, byte index, SyncVarWriteAccess writeAccess = SyncVarWriteAccess.Owner)
            : this(owner, initial, index, writeAccess, null) { }

        internal SyncHashSet(NetworkObject owner, HashSet<T> initial, byte index, SyncVarWriteAccess writeAccess, Action notifyDirty)
        {
            _owner = owner;
            _set = initial ?? new HashSet<T>();
            _index = index;
            _writeAccess = writeAccess;
            _notifyDirty = notifyDirty;
        }

        #endregion

        #region Set Operations (write-access guarded)

        private bool CanWrite()
        {
            switch (_writeAccess)
            {
                case SyncVarWriteAccess.Owner: return _owner.IsOwner;
                case SyncVarWriteAccess.Host: return _owner.IsHost;
                case SyncVarWriteAccess.All: return true;
                default: return _owner.IsOwner;
            }
        }

        /// <summary>Add an item to the set. Returns true if the item was added (not already present).</summary>
        public bool Add(T item)
        {
            if (_owner != null && _owner.IsRegistered && !CanWrite()) return false;

            if (!_set.Add(item)) return false;
            RecordOp(SyncHashSetOp.Add, item);
            return true;
        }

        /// <summary>Remove an item from the set. Returns true if the item was found and removed.</summary>
        public bool Remove(T item)
        {
            if (_owner != null && _owner.IsRegistered && !CanWrite()) return false;

            if (!_set.Remove(item)) return false;
            RecordOp(SyncHashSetOp.Remove, item);
            return true;
        }

        /// <summary>Remove all items from the set.</summary>
        public void Clear()
        {
            if (_owner != null && _owner.IsRegistered && !CanWrite()) return;
            if (_set.Count == 0) return;

            _set.Clear();
            RecordOp(SyncHashSetOp.Clear, default);
        }

        #endregion

        #region Read-only accessors

        public int Count => _set.Count;
        public bool Contains(T item) => _set.Contains(item);

        public IEnumerator<T> GetEnumerator() => _set.GetEnumerator();
        IEnumerator IEnumerable.GetEnumerator() => GetEnumerator();

        #endregion

        #region ISyncVar Implementation

        public byte Index => _index;
        public bool IsDirty => _dirty;

        public void ClearDirty()
        {
            _dirty = false;
            _pendingOps.Clear();
        }

        /// <summary>Write pending operations (delta). Format: [opCount:byte] [op:byte item?]...</summary>
        public void WriteTo(NetWriter writer)
        {
            writer.WriteByte((byte)_pendingOps.Count);
            for (int i = 0; i < _pendingOps.Count; i++)
            {
                var op = _pendingOps[i];
                writer.WriteByte((byte)op.Operation);

                switch (op.Operation)
                {
                    case SyncHashSetOp.Add:
                    case SyncHashSetOp.Remove:
                        NetSerializers.Write(writer, op.Item);
                        break;
                    // Clear doesn't need a value
                }
            }
        }

        /// <summary>Read and apply operations from a delta update.</summary>
        public void ReadFrom(NetReader reader)
        {
            byte opCount = reader.ReadByte();
            for (int i = 0; i < opCount; i++)
            {
                var op = (SyncHashSetOp)reader.ReadByte();

                switch (op)
                {
                    case SyncHashSetOp.Add:
                    {
                        T item = NetSerializers.Read<T>(reader);
                        _set.Add(item);
                        OnChanged?.Invoke(SyncHashSetOp.Add, item);
                        break;
                    }
                    case SyncHashSetOp.Remove:
                    {
                        T item = NetSerializers.Read<T>(reader);
                        _set.Remove(item);
                        OnChanged?.Invoke(SyncHashSetOp.Remove, item);
                        break;
                    }
                    case SyncHashSetOp.Clear:
                    {
                        _set.Clear();
                        OnChanged?.Invoke(SyncHashSetOp.Clear, default);
                        break;
                    }
                }
            }
        }

        #endregion

        #region Full State (spawn/snapshot)

        /// <summary>Write full set state. Format: [count:ushort] [item0] [item1] ...</summary>
        public void WriteFullState(NetWriter writer)
        {
            writer.WriteUInt16((ushort)_set.Count);
            foreach (var item in _set)
                NetSerializers.Write(writer, item);
        }

        /// <summary>Read full set state (replaces current contents).</summary>
        public void ReadFullState(NetReader reader)
        {
            _set.Clear();
            ushort count = reader.ReadUInt16();
            for (int i = 0; i < count; i++)
                _set.Add(NetSerializers.Read<T>(reader));
        }

        #endregion

        #region Internal

        private void RecordOp(SyncHashSetOp op, T item)
        {
            _pendingOps.Add(new SetChange
            {
                Operation = op,
                Item = item
            });
            _dirty = true;
            if (_notifyDirty != null) _notifyDirty(); else _owner?.MarkDirty();
            OnChanged?.Invoke(op, item);
        }

        private struct SetChange
        {
            public SyncHashSetOp Operation;
            public T Item;
        }

        #endregion
    }
}
