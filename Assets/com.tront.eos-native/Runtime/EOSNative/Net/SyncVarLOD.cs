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
    /// <example>
    /// <code>
    /// // Default tiers:
    /// // 0-20m:   full rate (every dirty frame)
    /// // 20-50m:  every 3rd dirty frame
    /// // 50-100m: every 10th dirty frame
    /// // 100m+:   no sync (object is too far)
    ///
    /// // Or customize:
    /// lod.Tiers = new List&lt;SyncVarLOD.Tier&gt;
    /// {
    ///     new() { MaxDistance = 30f, SyncEveryNthFrame = 1 },
    ///     new() { MaxDistance = 80f, SyncEveryNthFrame = 5 },
    ///     new() { MaxDistance = 150f, SyncEveryNthFrame = 15 },
    /// };
    /// </code>
    /// </example>
    /// </summary>
    [RequireComponent(typeof(NetworkObject))]
    public class SyncVarLOD : MonoBehaviour
    {
        /// <summary>
        /// A distance tier defining sync frequency.
        /// Objects within MaxDistance sync every SyncEveryNthFrame dirty frames.
        /// </summary>
        [Serializable]
        public struct Tier
        {
            /// <summary>Maximum distance for this tier (in world units).</summary>
            public float MaxDistance;

            /// <summary>
            /// Sync every Nth dirty frame. 1 = full rate, 3 = every 3rd frame, etc.
            /// Higher values = less bandwidth, more latency for updates.
            /// </summary>
            public int SyncEveryNthFrame;
        }

        /// <summary>
        /// Distance tiers, sorted by MaxDistance ascending.
        /// Objects beyond the last tier's MaxDistance are not synced at all.
        /// </summary>
        [SerializeField]
        public List<Tier> Tiers = new()
        {
            new Tier { MaxDistance = 20f, SyncEveryNthFrame = 1 },
            new Tier { MaxDistance = 50f, SyncEveryNthFrame = 3 },
            new Tier { MaxDistance = 100f, SyncEveryNthFrame = 10 },
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

        private NetworkObject _net;
        private int _dirtyCounter;

        private void Awake()
        {
            _net = GetComponent<NetworkObject>();
        }

        private void LateUpdate()
        {
            if (_net == null || !_net.IsOwner || !_net.IsRegistered) return;

            if (AutoObserverPosition)
                UpdateObserverPosition();

            UpdateCurrentTier();
        }

        /// <summary>
        /// Called by NetworkObject.MarkDirty() when SyncVarLOD is present.
        /// Returns true if the dirty flag should propagate, false if throttled.
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
                    return;
                }
            }

            // Beyond all tiers — culled
            CurrentTier = -1;
            CurrentSyncRate = 0;
        }
    }
}
