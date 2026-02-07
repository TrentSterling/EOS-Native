using System;
using System.Collections.Generic;
using EOSNative.P2P;

namespace EOSNative.Net
{
    /// <summary>
    /// Non-generic interface for SyncVar, allowing the NetworkObject to manage
    /// a heterogeneous list of SyncVars without knowing their concrete types.
    /// </summary>
    public interface ISyncVar
    {
        /// <summary>Whether this SyncVar has been modified since the last sync.</summary>
        bool IsDirty { get; }

        /// <summary>Clear the dirty flag after syncing.</summary>
        void ClearDirty();

        /// <summary>Write dirty delta to the writer (changed value or operations).</summary>
        void WriteTo(NetWriter writer);

        /// <summary>Read a dirty delta from the reader and apply.</summary>
        void ReadFrom(NetReader reader);

        /// <summary>Write full state for spawn/snapshot. Defaults to WriteTo for simple SyncVars.</summary>
        void WriteFullState(NetWriter writer);

        /// <summary>Read full state from spawn/snapshot. Defaults to ReadFrom for simple SyncVars.</summary>
        void ReadFullState(NetReader reader);
    }

    /// <summary>
    /// Generic synchronized variable with dirty tracking, owner-write guard, and change callbacks.
    /// Created via <see cref="NetworkObject.Sync{T}"/> — do not construct directly.
    ///
    /// Only the owning peer can set the Value. Remote peers receive updates automatically
    /// and fire <see cref="OnChanged"/> when the value changes.
    /// </summary>
    /// <typeparam name="T">The type to synchronize. Must be registered in <see cref="NetSerializers"/>.</typeparam>
    public class SyncVar<T> : ISyncVar
    {
        private T _value;
        private bool _dirty;
        private readonly NetworkObject _owner;
        private byte _index;

        /// <summary>
        /// Fires on ALL peers when the value changes. Args: (oldValue, newValue).
        /// On the owner, fires immediately on set. On remote peers, fires when the update is received.
        /// </summary>
        public event Action<T, T> OnChanged;

        /// <summary>
        /// Get or set the synchronized value.
        /// Only the owning peer can set — other peers' writes are silently ignored.
        /// Setting to an equal value is a no-op.
        /// </summary>
        public T Value
        {
            get => _value;
            set
            {
                // Owner-write guard: only the owner can set SyncVars
                if (_owner != null && _owner.IsRegistered && !_owner.IsOwner) return;

                if (EqualityComparer<T>.Default.Equals(_value, value)) return;

                T old = _value;
                _value = value;
                _dirty = true;
                _owner?.MarkDirty();
                OnChanged?.Invoke(old, _value);
            }
        }

        /// <summary>The index of this SyncVar within its NetworkObject's SyncVar list.</summary>
        public byte Index => _index;

        public bool IsDirty => _dirty;

        /// <summary>
        /// Internal constructor — called by <see cref="NetworkObject.Sync{T}"/>.
        /// </summary>
        internal SyncVar(NetworkObject owner, T defaultValue, byte index)
        {
            _owner = owner;
            _value = defaultValue;
            _index = index;
            _dirty = false;
        }

        public void ClearDirty()
        {
            _dirty = false;
        }

        public void WriteTo(NetWriter writer)
        {
            NetSerializers.Write(writer, _value);
        }

        public void ReadFrom(NetReader reader)
        {
            T old = _value;
            _value = NetSerializers.Read<T>(reader);
            if (!EqualityComparer<T>.Default.Equals(old, _value))
                OnChanged?.Invoke(old, _value);
        }

        // For simple SyncVars, full state is the same as delta
        public void WriteFullState(NetWriter writer) => WriteTo(writer);
        public void ReadFullState(NetReader reader) => ReadFrom(reader);

        /// <summary>
        /// Force-set the value without owner checks. Used internally for snapshot/authority transfer.
        /// </summary>
        internal void SetInternal(T value)
        {
            T old = _value;
            _value = value;
            if (!EqualityComparer<T>.Default.Equals(old, _value))
                OnChanged?.Invoke(old, _value);
        }

        public override string ToString()
        {
            return _value?.ToString() ?? "null";
        }
    }
}
