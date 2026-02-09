using System;
using System.Collections.Generic;
using Epic.OnlineServices;
using UnityEngine;

namespace EOSNative.Net
{
    /// <summary>
    /// Static utility for lag-compensated hit detection. Rewinds all tracked
    /// <see cref="NetworkPrediction"/> components to where they were at a past tick,
    /// executes a callback (typically Physics.Raycast), then restores all positions.
    ///
    /// <para>
    /// Uses the shooter's RTT to estimate how far in the past to rewind:
    /// rewindTicks = (rttMs / 2) / (FixedTickTime * 1000).
    /// This accounts for one-way latency — the shooter's screen showed positions
    /// that were ~half-RTT old when they fired.
    /// </para>
    ///
    /// <example>
    /// <code>
    /// // Using RTT directly:
    /// LagCompensation.Compensate(80f, () => {
    ///     if (Physics.Raycast(muzzle, dir, out var hit, 100f))
    ///         hit.collider.GetComponent&lt;Health&gt;()?.TakeDamage(25);
    /// });
    ///
    /// // Using a peer's ProductUserId (auto-fetches RTT from NetworkStats):
    /// LagCompensation.Compensate(shooterPuid, () => {
    ///     if (Physics.Raycast(muzzle, dir, out var hit, 100f))
    ///         hit.collider.GetComponent&lt;Health&gt;()?.TakeDamage(25);
    /// });
    /// </code>
    /// </example>
    /// </summary>
    public static class LagCompensation
    {
        private static readonly List<NetworkPrediction> _tracked = new List<NetworkPrediction>();

        // Saved state for restore
        private struct SavedState
        {
            public Vector3 Position;
            public Quaternion Rotation;
            public Vector3 Velocity;
            public Vector3 AngularVelocity;
            public bool HasRigidbody;
        }

        /// <summary>Register a NetworkPrediction component for lag compensation tracking.</summary>
        internal static void Register(NetworkPrediction prediction)
        {
            if (!_tracked.Contains(prediction))
                _tracked.Add(prediction);
        }

        /// <summary>Unregister a NetworkPrediction component.</summary>
        internal static void Unregister(NetworkPrediction prediction)
        {
            _tracked.Remove(prediction);
        }

        /// <summary>Number of currently tracked objects.</summary>
        public static int TrackedCount => _tracked.Count;

        /// <summary>
        /// Rewind all tracked objects to where they were rttMs/2 milliseconds ago,
        /// execute the callback, then restore all positions. Crash-safe via finally block.
        /// Physics.SyncTransforms() is called so raycasts see rewound collider positions.
        /// </summary>
        /// <param name="rttMs">Round-trip time in milliseconds to the shooter.</param>
        /// <param name="callback">Action to execute while objects are rewound (e.g. Physics.Raycast).</param>
        public static void Compensate(float rttMs, Action callback)
        {
            if (callback == null) return;
            if (_tracked.Count == 0 || rttMs <= 0f)
            {
                callback();
                return;
            }

            var tick = TickSimulation.Instance;
            if (!tick.IsEnabled || tick.FixedTickTime <= 0f)
            {
                callback();
                return;
            }

            // Calculate how many ticks in the past to rewind (half RTT = one-way latency)
            float oneWaySeconds = rttMs / 2000f;
            float rewindTicks = oneWaySeconds / tick.FixedTickTime;
            float targetTick = tick.CurrentTick - rewindTicks;

            if (targetTick < 0f) targetTick = 0f;

            // Save current state of all tracked objects
            var saved = new SavedState[_tracked.Count];
            for (int i = 0; i < _tracked.Count; i++)
            {
                var np = _tracked[i];
                if (np == null || np.transform == null) continue;

                saved[i].Position = np.transform.position;
                saved[i].Rotation = np.transform.rotation;

                var rb = np.GetRigidbody();
                if (rb != null)
                {
                    saved[i].HasRigidbody = true;
                    saved[i].Velocity = rb.linearVelocity;
                    saved[i].AngularVelocity = rb.angularVelocity;
                }
            }

            try
            {
                // Rewind all tracked objects to the target tick
                for (int i = 0; i < _tracked.Count; i++)
                {
                    var np = _tracked[i];
                    if (np == null || np.transform == null) continue;

                    var snapshot = np.History.GetInterpolated(targetTick);
                    np.transform.position = snapshot.Position;
                    np.transform.rotation = snapshot.Rotation;
                }

                // Sync physics colliders to rewound positions
                Physics.SyncTransforms();

                // Execute the user's callback (raycasts, overlap checks, etc.)
                callback();
            }
            finally
            {
                // Restore all objects to their current positions
                for (int i = 0; i < _tracked.Count; i++)
                {
                    var np = _tracked[i];
                    if (np == null || np.transform == null) continue;

                    np.transform.position = saved[i].Position;
                    np.transform.rotation = saved[i].Rotation;

                    if (saved[i].HasRigidbody)
                    {
                        var rb = np.GetRigidbody();
                        if (rb != null)
                        {
                            rb.linearVelocity = saved[i].Velocity;
                            rb.angularVelocity = saved[i].AngularVelocity;
                        }
                    }
                }

                // Re-sync colliders back to current positions
                Physics.SyncTransforms();
            }
        }

        /// <summary>
        /// Rewind using a peer's ProductUserId. Automatically fetches their RTT from NetworkStats.
        /// If RTT is unknown (-1), executes callback without rewind.
        /// </summary>
        /// <param name="shooter">The ProductUserId of the peer who fired.</param>
        /// <param name="callback">Action to execute while objects are rewound.</param>
        public static void Compensate(ProductUserId shooter, Action callback)
        {
            float rtt = -1f;
            if (NetworkStats.Instance != null)
                rtt = NetworkStats.Instance.RTT(shooter);

            Compensate(rtt > 0 ? rtt : 0f, callback);
        }
    }
}
