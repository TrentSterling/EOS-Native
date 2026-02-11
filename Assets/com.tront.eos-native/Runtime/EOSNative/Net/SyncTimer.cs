using System;
using EOSNative.P2P;

namespace EOSNative.Net
{
    /// <summary>
    /// A synced countdown timer. Owner sets the duration, all peers see the remaining time.
    /// Use <see cref="NetworkBehaviour.SyncTimer"/> to create one.
    /// <example><code>
    /// private SyncTimer _roundTimer;
    /// void Awake() { _roundTimer = SyncTimer(60f); _roundTimer.OnChanged += OnTimeChanged; }
    /// void OnTick(uint tick) { if (IsOwner) _roundTimer.Tick(Time.fixedDeltaTime); }
    /// </code></example>
    /// </summary>
    public class SyncTimer : ISyncVar
    {
        private readonly NetworkObject _owner;
        private readonly Action _notifyDirty;
        private readonly SyncVarWriteAccess _writeAccess;
        private byte _index;

        private float _remaining;
        private bool _running;
        private bool _dirty;

        /// <summary>Fires when time or running state changes. Args: (oldRemaining, newRemaining).</summary>
        public event Action<float, float> OnChanged;

        /// <summary>Fires when the timer reaches zero while running.</summary>
        public event Action OnExpired;

        public bool IsDirty => _dirty;
        public SyncVarWriteAccess WriteAccess => _writeAccess;

        /// <summary>Seconds remaining on the countdown.</summary>
        public float Remaining => _remaining;

        /// <summary>Whether the timer is actively counting down.</summary>
        public bool IsRunning => _running;

        /// <summary>Whether the timer has reached zero.</summary>
        public bool IsExpired => _remaining <= 0f;

        internal SyncTimer(NetworkObject owner, float initialDuration, byte index,
            SyncVarWriteAccess writeAccess, Action notifyDirty = null)
        {
            _owner = owner;
            _remaining = initialDuration;
            _index = index;
            _writeAccess = writeAccess;
            _notifyDirty = notifyDirty;
        }

        private bool CanWrite()
        {
            if (_owner == null || !_owner.IsRegistered) return true;
            switch (_writeAccess)
            {
                case SyncVarWriteAccess.Owner: return _owner.IsOwner;
                case SyncVarWriteAccess.Host: return _owner.IsHost;
                case SyncVarWriteAccess.All: return true;
                default: return _owner.IsOwner;
            }
        }

        private void MarkDirty(float oldValue, float newValue)
        {
            _dirty = true;
            if (_notifyDirty != null) _notifyDirty();
            else _owner?.MarkDirty();
            OnChanged?.Invoke(oldValue, newValue);
        }

        /// <summary>Start (or restart) the countdown with the given duration.</summary>
        public void Start(float duration)
        {
            if (!CanWrite()) return;
            float old = _remaining;
            _remaining = duration;
            _running = true;
            MarkDirty(old, _remaining);
        }

        /// <summary>Pause the countdown without resetting.</summary>
        public void Pause()
        {
            if (!CanWrite()) return;
            _running = false;
            float old = _remaining;
            MarkDirty(old, _remaining);
        }

        /// <summary>Resume a paused countdown.</summary>
        public void Resume()
        {
            if (!CanWrite()) return;
            _running = true;
            float old = _remaining;
            MarkDirty(old, _remaining);
        }

        /// <summary>Reset the timer to zero and stop it.</summary>
        public void Reset()
        {
            if (!CanWrite()) return;
            float old = _remaining;
            _remaining = 0f;
            _running = false;
            MarkDirty(old, 0f);
        }

        /// <summary>
        /// Advance the countdown by deltaTime. Call from OnTick or Update on the owner.
        /// Fires <see cref="OnExpired"/> when the timer reaches zero.
        /// </summary>
        public void Tick(float deltaTime)
        {
            if (!CanWrite() || !_running || _remaining <= 0f) return;

            float old = _remaining;
            _remaining -= deltaTime;
            if (_remaining < 0f) _remaining = 0f;
            MarkDirty(old, _remaining);

            if (_remaining <= 0f)
            {
                _running = false;
                OnExpired?.Invoke();
            }
        }

        public void ClearDirty() => _dirty = false;

        public void WriteTo(NetWriter writer)
        {
            writer.WriteFloat(_remaining);
            writer.WriteByte(_running ? (byte)1 : (byte)0);
        }

        public void ReadFrom(NetReader reader)
        {
            float old = _remaining;
            _remaining = reader.ReadFloat();
            _running = reader.ReadByte() != 0;
            if (Math.Abs(old - _remaining) > 0.0001f)
                OnChanged?.Invoke(old, _remaining);
            if (_remaining <= 0f && old > 0f)
                OnExpired?.Invoke();
        }

        public void WriteFullState(NetWriter writer) => WriteTo(writer);
        public void ReadFullState(NetReader reader) => ReadFrom(reader);
    }

    /// <summary>
    /// A synced stopwatch (elapsed time counter). Owner starts/stops, all peers see elapsed time.
    /// Use <see cref="NetworkBehaviour.SyncStopwatch"/> to create one.
    /// <example><code>
    /// private SyncStopwatch _matchTime;
    /// void Awake() { _matchTime = SyncStopwatch(); }
    /// void OnTick(uint tick) { if (IsOwner) _matchTime.Tick(Time.fixedDeltaTime); }
    /// </code></example>
    /// </summary>
    public class SyncStopwatch : ISyncVar
    {
        private readonly NetworkObject _owner;
        private readonly Action _notifyDirty;
        private readonly SyncVarWriteAccess _writeAccess;
        private byte _index;

        private float _elapsed;
        private bool _running;
        private bool _dirty;

        /// <summary>Fires when elapsed time or running state changes. Args: (oldElapsed, newElapsed).</summary>
        public event Action<float, float> OnChanged;

        public bool IsDirty => _dirty;
        public SyncVarWriteAccess WriteAccess => _writeAccess;

        /// <summary>Total elapsed seconds since the stopwatch was started (cumulative across pauses).</summary>
        public float Elapsed => _elapsed;

        /// <summary>Whether the stopwatch is actively counting up.</summary>
        public bool IsRunning => _running;

        internal SyncStopwatch(NetworkObject owner, byte index,
            SyncVarWriteAccess writeAccess, Action notifyDirty = null)
        {
            _owner = owner;
            _index = index;
            _writeAccess = writeAccess;
            _notifyDirty = notifyDirty;
        }

        private bool CanWrite()
        {
            if (_owner == null || !_owner.IsRegistered) return true;
            switch (_writeAccess)
            {
                case SyncVarWriteAccess.Owner: return _owner.IsOwner;
                case SyncVarWriteAccess.Host: return _owner.IsHost;
                case SyncVarWriteAccess.All: return true;
                default: return _owner.IsOwner;
            }
        }

        private void MarkDirty(float oldValue, float newValue)
        {
            _dirty = true;
            if (_notifyDirty != null) _notifyDirty();
            else _owner?.MarkDirty();
            OnChanged?.Invoke(oldValue, newValue);
        }

        /// <summary>Start or resume the stopwatch.</summary>
        public void Start()
        {
            if (!CanWrite()) return;
            _running = true;
            float old = _elapsed;
            MarkDirty(old, _elapsed);
        }

        /// <summary>Stop the stopwatch (elapsed time is preserved).</summary>
        public void Stop()
        {
            if (!CanWrite()) return;
            _running = false;
            float old = _elapsed;
            MarkDirty(old, _elapsed);
        }

        /// <summary>Reset elapsed time to zero and stop.</summary>
        public void Reset()
        {
            if (!CanWrite()) return;
            float old = _elapsed;
            _elapsed = 0f;
            _running = false;
            MarkDirty(old, 0f);
        }

        /// <summary>Reset and immediately start.</summary>
        public void Restart()
        {
            if (!CanWrite()) return;
            float old = _elapsed;
            _elapsed = 0f;
            _running = true;
            MarkDirty(old, 0f);
        }

        /// <summary>
        /// Advance the stopwatch by deltaTime. Call from OnTick or Update on the owner.
        /// </summary>
        public void Tick(float deltaTime)
        {
            if (!CanWrite() || !_running) return;

            float old = _elapsed;
            _elapsed += deltaTime;
            MarkDirty(old, _elapsed);
        }

        public void ClearDirty() => _dirty = false;

        public void WriteTo(NetWriter writer)
        {
            writer.WriteFloat(_elapsed);
            writer.WriteByte(_running ? (byte)1 : (byte)0);
        }

        public void ReadFrom(NetReader reader)
        {
            float old = _elapsed;
            _elapsed = reader.ReadFloat();
            _running = reader.ReadByte() != 0;
            if (Math.Abs(old - _elapsed) > 0.0001f)
                OnChanged?.Invoke(old, _elapsed);
        }

        public void WriteFullState(NetWriter writer) => WriteTo(writer);
        public void ReadFullState(NetReader reader) => ReadFrom(reader);
    }
}
