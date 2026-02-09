using System;
using UnityEngine;

namespace EOSNative.Net
{
    /// <summary>
    /// Fixed-rate tick simulation decoupled from the rendering frame rate.
    /// Drives all network state updates at a consistent rate (default 30 ticks/sec)
    /// regardless of whether the game runs at 30fps or 300fps.
    ///
    /// <para>
    /// When enabled, NetworkManager sends state updates on tick boundaries instead of
    /// every frame. SyncVarLOD throttles by tick count instead of frame count.
    /// Packet polling and message flushing remain frame-based for responsiveness.
    /// </para>
    ///
    /// <example>
    /// <code>
    /// // Enable tick-based simulation (default 30 ticks/sec):
    /// TickSimulation.Instance.TickRate = 30;
    ///
    /// // Subscribe to tick events for custom game logic:
    /// TickSimulation.Instance.OnTick += (tick, dt) => { ... };
    ///
    /// // Use Alpha for render interpolation (0..1 fraction of tick elapsed):
    /// float t = TickSimulation.Instance.Alpha;
    /// transform.position = Vector3.Lerp(prevPos, nextPos, t);
    /// </code>
    /// </example>
    /// </summary>
    public class TickSimulation : MonoBehaviour
    {
        #region Singleton

        private static TickSimulation _instance;
        private static bool _shuttingDown;

        public static TickSimulation Instance
        {
            get
            {
                if (_shuttingDown) return _instance;
                if (_instance == null)
                {
                    _instance = FindAnyObjectByType<TickSimulation>();
                    if (_instance == null)
                    {
                        var go = new GameObject("TickSimulation");
                        if (EOSManager.Instance != null)
                            go.transform.SetParent(EOSManager.Instance.transform);
                        else
                            DontDestroyOnLoad(go);
                        _instance = go.AddComponent<TickSimulation>();
                    }
                }
                return _instance;
            }
        }

        #endregion

        /// <summary>
        /// Fixed tick rate in ticks per second. Default 30.
        /// Set to 0 to disable tick simulation (falls back to frame-based updates).
        /// Common values: 20 (casual), 30 (standard), 60 (competitive).
        /// </summary>
        [Tooltip("Fixed tick rate (ticks/sec). 0 = disabled (frame-based). Common: 20, 30, 60.")]
        [SerializeField] private int _tickRate = 30;

        /// <summary>Get or set the tick rate. Recalculates FixedTickTime when changed.</summary>
        public int TickRate
        {
            get => _tickRate;
            set
            {
                _tickRate = Mathf.Max(0, value);
                FixedTickTime = _tickRate > 0 ? 1f / _tickRate : 0f;
            }
        }

        /// <summary>Time between ticks in seconds (1 / TickRate). 0 if disabled.</summary>
        public float FixedTickTime { get; private set; }

        /// <summary>Current tick index (monotonically increasing, wraps at uint.MaxValue).</summary>
        public uint CurrentTick { get; private set; }

        /// <summary>
        /// Simulation time in seconds (CurrentTick * FixedTickTime).
        /// Independent of Time.time — not affected by frame rate or timescale.
        /// </summary>
        public float SimulationTime { get; private set; }

        /// <summary>
        /// Interpolation alpha: fraction of the current tick that has elapsed (0..1).
        /// Use this for rendering smooth positions between ticks.
        /// <code>
        /// // In Update() for smooth rendering:
        /// transform.position = Vector3.Lerp(prevTickPos, nextTickPos, TickSimulation.Instance.Alpha);
        /// </code>
        /// </summary>
        public float Alpha { get; private set; }

        /// <summary>True if tick simulation is active (TickRate > 0).</summary>
        public bool IsEnabled => _tickRate > 0;

        /// <summary>
        /// Fired once per simulation tick at a fixed rate. Parameters: (tickIndex, fixedDeltaTime).
        /// Use for deterministic game logic that must run at a consistent rate.
        /// </summary>
        public event Action<uint, float> OnTick;

        /// <summary>
        /// Fired after all OnTick handlers have executed. Used by NetworkManager
        /// to send state updates after game logic has run.
        /// </summary>
        public event Action OnPostTick;

        private float _tickAccumulator;

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

            FixedTickTime = _tickRate > 0 ? 1f / _tickRate : 0f;
        }

        private void OnApplicationQuit() => _shuttingDown = true;

        private void Update()
        {
            if (_tickRate <= 0) return;

            _tickAccumulator += Time.deltaTime;

            // Cap accumulator to prevent spiral of death (max 10 ticks per frame)
            float maxAccumulator = FixedTickTime * 10f;
            if (_tickAccumulator > maxAccumulator)
                _tickAccumulator = maxAccumulator;

            while (_tickAccumulator >= FixedTickTime)
            {
                _tickAccumulator -= FixedTickTime;
                CurrentTick++;
                SimulationTime = CurrentTick * FixedTickTime;

                OnTick?.Invoke(CurrentTick, FixedTickTime);
                OnPostTick?.Invoke();
            }

            Alpha = FixedTickTime > 0f ? _tickAccumulator / FixedTickTime : 0f;
        }
    }
}
