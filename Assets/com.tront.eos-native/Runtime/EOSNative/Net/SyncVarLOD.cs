using System;
using System.Collections.Generic;
using EOSNative.P2P;
using UnityEngine;

namespace EOSNative.Net
{
    /// <summary>
    /// Attach to a NetworkObject to control sync frequency based on distance tiers.
    /// Reduces bandwidth by throttling SyncVar updates for distant objects.
    ///
    /// The component works on the OWNER side — it controls how often dirty state
    /// is propagated to the NetworkManager's send queue. Remote peers receive
    /// updates at the throttled rate automatically.
    ///
    /// When tick simulation is active, the N value refers to ticks instead of frames,
    /// giving consistent throttling regardless of frame rate.
    ///
    /// <example>
    /// <code>
    /// // Default tiers:
    /// // 0-20m:   full rate (every dirty frame/tick)
    /// // 20-50m:  every 3rd dirty frame/tick
    /// // 50-100m: every 10th dirty frame/tick
    /// // 100m+:   no sync (object is too far)
    ///
    /// // Or customize:
    /// lod.Tiers = new List&lt;SyncVarLOD.Tier&gt;
    /// {
    ///     new() { MaxDistance = 30f, SyncEveryNth = 1 },
    ///     new() { MaxDistance = 80f, SyncEveryNth = 5 },
    ///     new() { MaxDistance = 150f, SyncEveryNth = 15 },
    /// };
    /// </code>
    /// </example>
    /// </summary>
    [RequireComponent(typeof(NetworkObject))]
    public class SyncVarLOD : MonoBehaviour
    {
        /// <summary>
        /// A distance tier defining sync frequency and which SyncVars to include.
        /// Objects within MaxDistance sync every SyncEveryNth dirty events (frames or ticks).
        /// </summary>
        [Serializable]
        public struct Tier
        {
            /// <summary>Maximum distance for this tier (in world units).</summary>
            public float MaxDistance;

            /// <summary>
            /// Sync every Nth dirty event. 1 = full rate, 3 = every 3rd, etc.
            /// When tick simulation is active, N counts ticks; otherwise counts frames.
            /// Higher values = less bandwidth, more latency for updates.
            /// </summary>
            [Tooltip("Sync every Nth dirty event (frame or tick). 1 = full rate.")]
            public int SyncEveryNthFrame;

            /// <summary>
            /// Bitmask of which SyncVars to include at this tier. Bit N corresponds to SyncVar index N.
            /// Default 0xFFFFFFFF = all SyncVars sync. Set specific bits to zero to skip SyncVars
            /// at this distance tier (e.g., skip cosmetic data at far distances).
            /// </summary>
            [Tooltip("Bitmask of SyncVars to sync at this tier (0xFFFFFFFF = all). Bit 0 = SyncVar 0, Bit 1 = SyncVar 1, etc.")]
            public uint SyncVarMask;
        }

        /// <summary>
        /// Distance tiers, sorted by MaxDistance ascending.
        /// Objects beyond the last tier's MaxDistance are not synced at all.
        /// </summary>
        [SerializeField]
        public List<Tier> Tiers = new()
        {
            new Tier { MaxDistance = 20f, SyncEveryNthFrame = 1, SyncVarMask = 0xFFFFFFFF },
            new Tier { MaxDistance = 50f, SyncEveryNthFrame = 3, SyncVarMask = 0xFFFFFFFF },
            new Tier { MaxDistance = 100f, SyncEveryNthFrame = 10, SyncVarMask = 0xFFFFFFFF },
        };

        /// <summary>
        /// The reference position used for distance calculation.
        /// In Auto mode, this is the nearest peer's position.
        /// You can set this manually for custom camera/observer logic.
        /// </summary>
        public Vector3 ObserverPosition { get; set; }

        /// <summary>
        /// If true (default), automatically sets ObserverPosition to the nearest
        /// peer's NetworkObject position each frame. Set false to manage manually.
        /// </summary>
        public bool AutoObserverPosition = true;

        /// <summary>
        /// Current active tier index (-1 = beyond all tiers, object is culled).
        /// Read-only — useful for debugging or UI display.
        /// </summary>
        public int CurrentTier { get; private set; }

        /// <summary>
        /// Current effective sync rate (1 = every frame, 3 = every 3rd, etc.).
        /// 0 means the object is culled (beyond all tiers).
        /// </summary>
        public int CurrentSyncRate { get; private set; } = 1;

        /// <summary>
        /// Bitmask of which SyncVars are active at the current tier.
        /// 0xFFFFFFFF = all SyncVars. Bit N = SyncVar at index N.
        /// Used by NetworkObject.SerializeDirty() to filter which SyncVars to send.
        /// </summary>
        public uint CurrentSyncVarMask { get; private set; } = 0xFFFFFFFF;

        private NetworkObject _net;
        private int _dirtyCounter;
        private uint _lastTickUpdated;

        private void Awake()
        {
            _net = GetComponent<NetworkObject>();
        }

        private void LateUpdate()
        {
            if (_net == null || !_net.IsOwner || !_net.IsRegistered) return;

            // When tick simulation is active, observer/tier updates happen on tick
            var tick = TickSimulation.Instance;
            if (tick != null && tick.IsEnabled)
            {
                if (tick.CurrentTick == _lastTickUpdated) return;
                _lastTickUpdated = tick.CurrentTick;
            }

            if (AutoObserverPosition)
                UpdateObserverPosition();

            UpdateCurrentTier();
        }

        /// <summary>
        /// Called by NetworkObject.MarkDirty() when SyncVarLOD is present.
        /// Returns true if the dirty flag should propagate, false if throttled.
        /// Counts frames normally, or ticks when tick simulation is active.
        /// </summary>
        internal bool ShouldPropagateDirty()
        {
            if (CurrentSyncRate <= 0) return false; // Culled
            if (CurrentSyncRate <= 1) return true;  // Full rate

            _dirtyCounter++;
            if (_dirtyCounter >= CurrentSyncRate)
            {
                _dirtyCounter = 0;
                return true;
            }
            return false;
        }

        private void UpdateObserverPosition()
        {
            var peers = EOSP2PManager.Instance?.Peers;
            if (peers == null || peers.Count == 0) return;

            var nm = NetworkManager.Instance;
            if (nm == null) return;

            float nearestDist = float.MaxValue;
            Vector3 myPos = transform.position;

            foreach (var peer in peers)
            {
                // Find this peer's owned objects and use the nearest one
                foreach (var obj in nm.Objects.Values)
                {
                    if (obj.OwnerId != null && obj.OwnerId.Equals(peer))
                    {
                        float dist = Vector3.Distance(myPos, obj.transform.position);
                        if (dist < nearestDist)
                        {
                            nearestDist = dist;
                            ObserverPosition = obj.transform.position;
                        }
                    }
                }
            }
        }

        private void UpdateCurrentTier()
        {
            float dist = Vector3.Distance(transform.position, ObserverPosition);

            for (int i = 0; i < Tiers.Count; i++)
            {
                if (dist <= Tiers[i].MaxDistance)
                {
                    CurrentTier = i;
                    CurrentSyncRate = Math.Max(1, Tiers[i].SyncEveryNthFrame);
                    CurrentSyncVarMask = Tiers[i].SyncVarMask == 0 ? 0xFFFFFFFF : Tiers[i].SyncVarMask;
                    return;
                }
            }

            // Beyond all tiers — culled
            CurrentTier = -1;
            CurrentSyncRate = 0;
            CurrentSyncVarMask = 0;
        }
    }
}
