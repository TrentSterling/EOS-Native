using System;
using System.Collections;
using System.Collections.Generic;
using EOSNative.P2P;

namespace EOSNative.Net
{
    /// <summary>
    /// Operation types for SyncDictionary change tracking.
    /// </summary>
    public enum SyncDictOp : byte
    {
        Set = 0,
        Remove = 1,
        Clear = 2
    }

    /// <summary>
    /// Synchronized dictionary with operation-based delta sync.
    /// Only changed operations are sent over the network — not the entire dictionary.
    ///
    /// Owner can modify freely. Remote peers receive operations and apply them in order.
    /// Full state is serialized for spawn/snapshot (late join).
    ///
    /// Usage:
    /// <code>
    /// SyncDictionary&lt;string, int&gt; Scores;
    ///
    /// void Awake() {
    ///     Scores = SyncDictionary&lt;string, int&gt;(new Dictionary&lt;string, int&gt;());
    ///     Scores.OnChanged += (op, key, oldVal, newVal) =&gt;
    ///         Debug.Log($"{op}: {key} = {oldVal} → {newVal}");
    /// }
    ///
    /// void AddScore(string player, int score) {
    ///     if (!IsOwner) return;
    ///     Scores[player] = score; // Synced automatically
    /// }
    /// </code>
    /// </summary>
    public class SyncDictionary<TKey, TValue> : ISyncVar, IReadOnlyDictionary<TKey, TValue>
    {
        private readonly Dictionary<TKey, TValue> _dict;
        private readonly List<DictChange> _pendingOps = new();
        private readonly NetworkObject _owner;
        private readonly byte _index;
        private bool _dirty;
        private readonly SyncVarWriteAccess _writeAccess;

        /// <summary>
        /// Fires when the dictionary changes. Args: (operation, key, oldValue, newValue).
        /// On owner: fires immediately. On remotes: fires when the delta arrives.
        /// </summary>
        public event Action<SyncDictOp, TKey, TValue, TValue> OnChanged;

        /// <summary>Who can write to this SyncDictionary.</summary>
        public SyncVarWriteAccess WriteAccess => _writeAccess;

        #region Constructor

        internal SyncDictionary(NetworkObject owner, Dictionary<TKey, TValue> initial, byte index, SyncVarWriteAccess writeAccess = SyncVarWriteAccess.Owner)
        {
            _owner = owner;
            _dict = initial ?? new Dictionary<TKey, TValue>();
            _index = index;
            _writeAccess = writeAccess;
        }

        #endregion

        #region Dictionary Operations (write-access guarded)

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

        /// <summary>Set a key to a value. Adds if not present, updates if existing.</summary>
        public void Set(TKey key, TValue value)
        {
            if (_owner != null && _owner.IsRegistered && !CanWrite()) return;

            TValue oldValue = default;
            _dict.TryGetValue(key, out oldValue);

            if (_dict.ContainsKey(key) && EqualityComparer<TValue>.Default.Equals(oldValue, value))
                return;

            _dict[key] = value;
            RecordOp(SyncDictOp.Set, key, oldValue, value);
        }

        /// <summary>Indexer — same as Set.</summary>
        public TValue this[TKey key]
        {
            get => _dict[key];
            set => Set(key, value);
        }

        /// <summary>Remove a key. Returns true if the key existed.</summary>
        public bool Remove(TKey key)
        {
            if (_owner != null && _owner.IsRegistered && !CanWrite()) return false;

            if (!_dict.TryGetValue(key, out var oldValue))
                return false;

            _dict.Remove(key);
            RecordOp(SyncDictOp.Remove, key, oldValue, default);
            return true;
        }

        /// <summary>Remove all entries.</summary>
        public void Clear()
        {
            if (_owner != null && _owner.IsRegistered && !CanWrite()) return;
            if (_dict.Count == 0) return;

            _dict.Clear();
            RecordOp(SyncDictOp.Clear, default, default, default);
        }

        #endregion

        #region Read-only accessors

        public int Count => _dict.Count;
        public bool ContainsKey(TKey key) => _dict.ContainsKey(key);
        public bool TryGetValue(TKey key, out TValue value) => _dict.TryGetValue(key, out value);
        public IEnumerable<TKey> Keys => _dict.Keys;
        public IEnumerable<TValue> Values => _dict.Values;

        public IEnumerator<KeyValuePair<TKey, TValue>> GetEnumerator() => _dict.GetEnumerator();
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

        /// <summary>Write pending operations (delta). Format: [opCount:byte] [op:byte key? value?]...</summary>
        public void WriteTo(NetWriter writer)
        {
            writer.WriteByte((byte)_pendingOps.Count);
            for (int i = 0; i < _pendingOps.Count; i++)
            {
                var op = _pendingOps[i];
                writer.WriteByte((byte)op.Operation);

                switch (op.Operation)
                {
                    case SyncDictOp.Set:
                        NetSerializers.Write(writer, op.Key);
                        NetSerializers.Write(writer, op.Value);
                        break;
                    case SyncDictOp.Remove:
                        NetSerializers.Write(writer, op.Key);
                        break;
                    // Clear doesn't need key or value
                }
            }
        }

        /// <summary>Read and apply operations from a delta update.</summary>
        public void ReadFrom(NetReader reader)
        {
            byte opCount = reader.ReadByte();
            for (int i = 0; i < opCount; i++)
            {
                var op = (SyncDictOp)reader.ReadByte();

                switch (op)
                {
                    case SyncDictOp.Set:
                    {
                        TKey key = NetSerializers.Read<TKey>(reader);
                        TValue value = NetSerializers.Read<TValue>(reader);
                        _dict.TryGetValue(key, out TValue oldValue);
                        _dict[key] = value;
                        OnChanged?.Invoke(SyncDictOp.Set, key, oldValue, value);
                        break;
                    }
                    case SyncDictOp.Remove:
                    {
                        TKey key = NetSerializers.Read<TKey>(reader);
                        _dict.TryGetValue(key, out TValue oldValue);
                        _dict.Remove(key);
                        OnChanged?.Invoke(SyncDictOp.Remove, key, oldValue, default);
                        break;
                    }
                    case SyncDictOp.Clear:
                    {
                        _dict.Clear();
                        OnChanged?.Invoke(SyncDictOp.Clear, default, default, default);
                        break;
                    }
                }
            }
        }

        #endregion

        #region Full State (spawn/snapshot)

        /// <summary>Write full dictionary state. Format: [count:ushort] [key0][value0] [key1][value1] ...</summary>
        public void WriteFullState(NetWriter writer)
        {
            writer.WriteUInt16((ushort)_dict.Count);
            foreach (var kvp in _dict)
            {
                NetSerializers.Write(writer, kvp.Key);
                NetSerializers.Write(writer, kvp.Value);
            }
        }

        /// <summary>Read full dictionary state (replaces current contents).</summary>
        public void ReadFullState(NetReader reader)
        {
            _dict.Clear();
            ushort count = reader.ReadUInt16();
            for (int i = 0; i < count; i++)
            {
                TKey key = NetSerializers.Read<TKey>(reader);
                TValue value = NetSerializers.Read<TValue>(reader);
                _dict[key] = value;
            }
        }

        #endregion

        #region Internal

        private void RecordOp(SyncDictOp op, TKey key, TValue oldValue, TValue newValue)
        {
            _pendingOps.Add(new DictChange
            {
                Operation = op,
                Key = key,
                OldValue = oldValue,
                Value = newValue
            });
            _dirty = true;
            _owner?.MarkDirty();
            OnChanged?.Invoke(op, key, oldValue, newValue);
        }

        private struct DictChange
        {
            public SyncDictOp Operation;
            public TKey Key;
            public TValue OldValue;
            public TValue Value;
        }

        #endregion
    }
}
