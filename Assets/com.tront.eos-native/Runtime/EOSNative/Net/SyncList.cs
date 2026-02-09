using System;
using System.Collections;
using System.Collections.Generic;
using EOSNative.P2P;

namespace EOSNative.Net
{
    /// <summary>
    /// Operation types for SyncList change tracking.
    /// </summary>
    public enum SyncListOp : byte
    {
        Add = 0,
        Set = 1,
        RemoveAt = 2,
        Insert = 3,
        Clear = 4
    }

    /// <summary>
    /// Synchronized list with operation-based delta sync.
    /// Only changed operations are sent over the network — not the entire list.
    ///
    /// Owner can modify freely. Remote peers receive operations and apply them in order.
    /// Full state is serialized for spawn/snapshot (late join).
    ///
    /// Usage:
    /// <code>
    /// SyncList&lt;string&gt; Inventory;
    ///
    /// void Awake() {
    ///     Inventory = SyncList(new List&lt;string&gt;());
    ///     Inventory.OnChanged += (op, index, oldItem, newItem) =&gt;
    ///         Debug.Log($"{op}: [{index}] {oldItem} → {newItem}");
    /// }
    ///
    /// void PickUp(string item) {
    ///     if (!IsOwner) return;
    ///     Inventory.Add(item); // Synced automatically
    /// }
    /// </code>
    /// </summary>
    public class SyncList<T> : ISyncVar, IReadOnlyList<T>
    {
        private readonly List<T> _items;
        private readonly List<SyncListChange<T>> _pendingOps = new();
        private NetworkObject _owner;
        private byte _index;
        private bool _dirty;
        private readonly SyncVarWriteAccess _writeAccess;

        /// <summary>
        /// Fires when the list changes. Args: (operation, index, oldItem, newItem).
        /// On owner: fires immediately. On remotes: fires when the delta arrives.
        /// </summary>
        public event Action<SyncListOp, int, T, T> OnChanged;

        /// <summary>Who can write to this SyncList.</summary>
        public SyncVarWriteAccess WriteAccess => _writeAccess;

        #region Constructor

        internal SyncList(NetworkObject owner, List<T> initial, byte index, SyncVarWriteAccess writeAccess = SyncVarWriteAccess.Owner)
        {
            _owner = owner;
            _items = initial ?? new List<T>();
            _index = index;
            _writeAccess = writeAccess;
        }

        #endregion

        #region List Operations (write-access guarded)

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

        public void Add(T item)
        {
            if (_owner != null && _owner.IsRegistered && !CanWrite()) return;

            _items.Add(item);
            RecordOp(SyncListOp.Add, _items.Count - 1, default, item);
        }

        public void Insert(int index, T item)
        {
            if (_owner != null && _owner.IsRegistered && !CanWrite()) return;

            _items.Insert(index, item);
            RecordOp(SyncListOp.Insert, index, default, item);
        }

        public void RemoveAt(int index)
        {
            if (_owner != null && _owner.IsRegistered && !CanWrite()) return;
            if (index < 0 || index >= _items.Count) return;

            T old = _items[index];
            _items.RemoveAt(index);
            RecordOp(SyncListOp.RemoveAt, index, old, default);
        }

        public bool Remove(T item)
        {
            int idx = _items.IndexOf(item);
            if (idx < 0) return false;
            RemoveAt(idx);
            return true;
        }

        public void Set(int index, T value)
        {
            if (_owner != null && _owner.IsRegistered && !CanWrite()) return;
            if (index < 0 || index >= _items.Count) return;

            T old = _items[index];
            if (EqualityComparer<T>.Default.Equals(old, value)) return;

            _items[index] = value;
            RecordOp(SyncListOp.Set, index, old, value);
        }

        public T this[int index]
        {
            get => _items[index];
            set => Set(index, value);
        }

        public void Clear()
        {
            if (_owner != null && _owner.IsRegistered && !CanWrite()) return;
            if (_items.Count == 0) return;

            _items.Clear();
            RecordOp(SyncListOp.Clear, 0, default, default);
        }

        #endregion

        #region Read-only accessors

        public int Count => _items.Count;
        public bool Contains(T item) => _items.Contains(item);
        public int IndexOf(T item) => _items.IndexOf(item);

        public IEnumerator<T> GetEnumerator() => _items.GetEnumerator();
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

        /// <summary>Write pending operations (delta). Format: [opCount:byte] [op:byte index:ushort value?]...</summary>
        public void WriteTo(NetWriter writer)
        {
            writer.WriteByte((byte)_pendingOps.Count);
            for (int i = 0; i < _pendingOps.Count; i++)
            {
                var op = _pendingOps[i];
                writer.WriteByte((byte)op.Operation);
                writer.WriteUInt16((ushort)op.Index);

                switch (op.Operation)
                {
                    case SyncListOp.Add:
                    case SyncListOp.Insert:
                    case SyncListOp.Set:
                        NetSerializers.Write(writer, op.NewItem);
                        break;
                    // RemoveAt and Clear don't need values
                }
            }
        }

        /// <summary>Read and apply operations from a delta update.</summary>
        public void ReadFrom(NetReader reader)
        {
            byte opCount = reader.ReadByte();
            for (int i = 0; i < opCount; i++)
            {
                var op = (SyncListOp)reader.ReadByte();
                int index = reader.ReadUInt16();

                switch (op)
                {
                    case SyncListOp.Add:
                    {
                        T item = NetSerializers.Read<T>(reader);
                        _items.Add(item);
                        OnChanged?.Invoke(SyncListOp.Add, _items.Count - 1, default, item);
                        break;
                    }
                    case SyncListOp.Insert:
                    {
                        T item = NetSerializers.Read<T>(reader);
                        if (index <= _items.Count)
                        {
                            _items.Insert(index, item);
                            OnChanged?.Invoke(SyncListOp.Insert, index, default, item);
                        }
                        break;
                    }
                    case SyncListOp.Set:
                    {
                        T item = NetSerializers.Read<T>(reader);
                        if (index < _items.Count)
                        {
                            T old = _items[index];
                            _items[index] = item;
                            OnChanged?.Invoke(SyncListOp.Set, index, old, item);
                        }
                        break;
                    }
                    case SyncListOp.RemoveAt:
                    {
                        if (index < _items.Count)
                        {
                            T old = _items[index];
                            _items.RemoveAt(index);
                            OnChanged?.Invoke(SyncListOp.RemoveAt, index, old, default);
                        }
                        break;
                    }
                    case SyncListOp.Clear:
                    {
                        _items.Clear();
                        OnChanged?.Invoke(SyncListOp.Clear, 0, default, default);
                        break;
                    }
                }
            }
        }

        #endregion

        #region Full State (spawn/snapshot)

        /// <summary>Write full list state. Format: [count:ushort] [item0] [item1] ...</summary>
        public void WriteFullState(NetWriter writer)
        {
            writer.WriteUInt16((ushort)_items.Count);
            for (int i = 0; i < _items.Count; i++)
                NetSerializers.Write(writer, _items[i]);
        }

        /// <summary>Read full list state (replaces current contents).</summary>
        public void ReadFullState(NetReader reader)
        {
            _items.Clear();
            ushort count = reader.ReadUInt16();
            for (int i = 0; i < count; i++)
                _items.Add(NetSerializers.Read<T>(reader));
        }

        #endregion

        #region Internal

        private void RecordOp(SyncListOp op, int index, T oldItem, T newItem)
        {
            _pendingOps.Add(new SyncListChange<T>
            {
                Operation = op,
                Index = index,
                OldItem = oldItem,
                NewItem = newItem
            });
            _dirty = true;
            _owner?.MarkDirty();
            OnChanged?.Invoke(op, index, oldItem, newItem);
        }

        #endregion
    }

    internal struct SyncListChange<T>
    {
        public SyncListOp Operation;
        public int Index;
        public T OldItem;
        public T NewItem;
    }
}
