using System;
using System.Collections.Generic;
using Epic.OnlineServices;
using EOSNative.Logging;
using EOSNative.P2P;
using UnityEngine;

namespace EOSNative.Net
{
    /// <summary>
    /// Spatial interest management for networked objects.
    /// Controls which objects each peer receives updates for, based on proximity.
    ///
    /// Attach to any GameObject (auto-creates under EOSManager if not found).
    /// When enabled, NetworkManager uses per-peer filtered sends instead of broadcasts.
    ///
    /// Objects exempted from filtering (always visible to all peers):
    /// - NetworkRoomState and NetworkPlayerState
    /// - Objects owned by the observing peer
    /// - Objects marked with AlwaysVisible = true
    ///
    /// Uses a 2D spatial hash grid (Mirror-style) with configurable visibility range.
    /// </summary>
    public class InterestManager : MonoBehaviour
    {
        #region Singleton

        private static InterestManager _instance;
        private static bool _shuttingDown;

        public static InterestManager Instance
        {
            get
            {
                if (_shuttingDown) return _instance;
                if (_instance == null)
                {
                    _instance = FindAnyObjectByType<InterestManager>();
                }
                return _instance;
            }
        }

        #endregion

        #region Configuration

        /// <summary>Visibility range in world units. Objects beyond this distance are culled.</summary>
        [Tooltip("Visibility range in world units")]
        [SerializeField] private int _visRange = 100;

        /// <summary>
        /// Hysteresis buffer percentage (0-1). Once visible, an object stays visible
        /// until the peer exceeds visRange * (1 + hysteresis). Prevents flickering at boundaries.
        /// Default 0.1 = 10% buffer.
        /// </summary>
        [Tooltip("Hysteresis buffer (0.1 = 10% extra distance before hiding)")]
        [SerializeField] private float _hysteresis = 0.1f;

        /// <summary>Seconds between full interest rebuilds. Lower = more responsive, higher = less CPU.</summary>
        [Tooltip("Seconds between full interest rebuilds")]
        [SerializeField] private float _rebuildInterval = 0.5f;

        /// <summary>Which 2D plane to project onto.</summary>
        [Tooltip("Grid projection axes")]
        [SerializeField] private SpatialHashGrid.GridAxes _gridAxes = SpatialHashGrid.GridAxes.XZ;

        /// <summary>Current visibility range.</summary>
        public int VisRange
        {
            get => _visRange;
            set { _visRange = Math.Max(1, value); RebuildGrid(); }
        }

        #endregion

        #region State

        private SpatialHashGrid _grid;
        private float _nextRebuildTime;

        // Per-peer interest sets: which objects each peer can currently see
        private readonly Dictionary<ProductUserId, HashSet<uint>> _peerInterests = new();

        // Reusable buffers
        private readonly HashSet<uint> _nearbyBuffer = new();
        private readonly List<uint> _enterBuffer = new();
        private readonly List<uint> _exitBuffer = new();

        #endregion

        #region Events

        /// <summary>Fired when a peer gains interest in an object. Args: (peer, networkId).</summary>
        public event Action<ProductUserId, uint> OnInterestEnter;

        /// <summary>Fired when a peer loses interest in an object. Args: (peer, networkId).</summary>
        public event Action<ProductUserId, uint> OnInterestExit;

        #endregion

        #region Lifecycle

        private void Awake()
        {
            _shuttingDown = false;
            if (_instance != null && _instance != this)
            {
                Destroy(gameObject);
                return;
            }
            _instance = this;
            RebuildGrid();
        }

        private void OnApplicationQuit()
        {
            _shuttingDown = true;
        }

        private void OnDestroy()
        {
            if (_instance == this)
                _instance = null;
        }

        private void LateUpdate()
        {
            if (_grid == null) return;

            UpdateGrid();

            if (Time.unscaledTime >= _nextRebuildTime)
            {
                _nextRebuildTime = Time.unscaledTime + _rebuildInterval;
                RebuildInterests();
            }
        }

        #endregion

        #region Grid Management

        private void RebuildGrid()
        {
            _grid = new SpatialHashGrid(_visRange, _gridAxes);
        }

        /// <summary>Update all object positions in the grid (every frame).</summary>
        private void UpdateGrid()
        {
            var nm = NetworkManager.Instance;
            if (nm == null) return;

            foreach (var kvp in nm.Objects)
            {
                var obj = kvp.Value;
                if (obj != null && obj.IsRegistered)
                    _grid.UpdateObject(obj.NetworkId, obj.transform.position);
            }
        }

        /// <summary>
        /// Rebuild per-peer interest sets and fire enter/leave events.
        /// Called every _rebuildInterval seconds.
        /// </summary>
        private void RebuildInterests()
        {
            var nm = NetworkManager.Instance;
            if (nm == null) return;

            var peers = EOSP2PManager.Instance?.Peers;
            if (peers == null || peers.Count == 0) return;

            float hideRange = _visRange * (1f + _hysteresis);
            float hideRangeSqr = hideRange * hideRange;
            float showRangeSqr = (float)_visRange * _visRange;

            foreach (var peer in peers)
            {
                // Get the peer's position (from their owned objects)
                Vector3? peerPos = GetPeerPosition(peer, nm);
                if (!peerPos.HasValue) continue;

                // Get or create interest set for this peer
                if (!_peerInterests.TryGetValue(peer, out var currentInterest))
                {
                    currentInterest = new HashSet<uint>();
                    _peerInterests[peer] = currentInterest;
                }

                // Get nearby objects from grid
                _grid.GetNearbyObjects(peerPos.Value, _nearbyBuffer);

                // Add all always-visible objects
                foreach (var kvp in nm.Objects)
                {
                    var obj = kvp.Value;
                    if (obj == null || !obj.IsRegistered) continue;
                    if (IsAlwaysVisible(obj, peer))
                        _nearbyBuffer.Add(obj.NetworkId);
                }

                // Apply hysteresis: objects currently visible stay visible until hideRange
                _enterBuffer.Clear();
                _exitBuffer.Clear();

                // Check for new objects entering interest
                foreach (uint id in _nearbyBuffer)
                {
                    if (!currentInterest.Contains(id))
                    {
                        // New: check show range
                        if (nm.Objects.TryGetValue(id, out var obj) && obj != null)
                        {
                            float distSqr = (obj.transform.position - peerPos.Value).sqrMagnitude;
                            if (distSqr <= showRangeSqr || IsAlwaysVisible(obj, peer))
                                _enterBuffer.Add(id);
                        }
                    }
                }

                // Check for objects leaving interest (with hysteresis)
                foreach (uint id in currentInterest)
                {
                    if (nm.Objects.TryGetValue(id, out var obj) && obj != null && obj.IsRegistered)
                    {
                        if (IsAlwaysVisible(obj, peer)) continue; // never exits

                        float distSqr = (obj.transform.position - peerPos.Value).sqrMagnitude;
                        if (distSqr > hideRangeSqr)
                            _exitBuffer.Add(id);
                    }
                    else
                    {
                        // Object no longer exists
                        _exitBuffer.Add(id);
                    }
                }

                // Apply changes
                foreach (uint id in _enterBuffer)
                {
                    currentInterest.Add(id);
                    OnInterestEnter?.Invoke(peer, id);
                }
                foreach (uint id in _exitBuffer)
                {
                    currentInterest.Remove(id);
                    OnInterestExit?.Invoke(peer, id);
                }
            }

            // Clean up disconnected peers
            _exitBuffer.Clear();
            foreach (var kvp in _peerInterests)
            {
                bool found = false;
                foreach (var peer in peers)
                {
                    if (peer.Equals(kvp.Key)) { found = true; break; }
                }
                // Can't remove during iteration, but we can clear the set
            }
        }

        #endregion

        #region Public API

        /// <summary>
        /// Check if a peer is interested in a specific object.
        /// Returns true if no InterestManager is active (fail-safe: default to visible).
        /// </summary>
        public bool IsInterested(ProductUserId peer, NetworkObject obj)
        {
            if (obj == null || peer == null) return false;

            // Always-visible objects bypass spatial check
            if (IsAlwaysVisible(obj, peer)) return true;

            // Check interest set
            if (_peerInterests.TryGetValue(peer, out var set))
                return set.Contains(obj.NetworkId);

            // Not yet tracked — use distance check as fallback
            var nm = NetworkManager.Instance;
            Vector3? peerPos = GetPeerPosition(peer, nm);
            if (!peerPos.HasValue) return true; // fail-safe

            float distSqr = (obj.transform.position - peerPos.Value).sqrMagnitude;
            return distSqr <= (float)_visRange * _visRange;
        }

        /// <summary>
        /// Get all peers interested in a specific object.
        /// Populates the provided list (cleared first).
        /// </summary>
        public void GetInterestedPeers(NetworkObject obj, List<ProductUserId> results)
        {
            results.Clear();
            if (obj == null) return;

            var peers = EOSP2PManager.Instance?.Peers;
            if (peers == null) return;

            foreach (var peer in peers)
            {
                if (IsInterested(peer, obj))
                    results.Add(peer);
            }
        }

        /// <summary>Called when a peer disconnects. Cleans up their interest set.</summary>
        public void OnPeerDisconnected(ProductUserId peer)
        {
            _peerInterests.Remove(peer);
        }

        /// <summary>Called when an object is removed. Cleans up from all interest sets.</summary>
        public void OnObjectRemoved(uint networkId)
        {
            _grid.RemoveObject(networkId);
            foreach (var set in _peerInterests.Values)
                set.Remove(networkId);
        }

        /// <summary>Force an object into a peer's interest set (e.g., on spawn).</summary>
        public void ForceInterest(ProductUserId peer, uint networkId)
        {
            if (!_peerInterests.TryGetValue(peer, out var set))
            {
                set = new HashSet<uint>();
                _peerInterests[peer] = set;
            }
            set.Add(networkId);
        }

        /// <summary>Clear all state. Called on disconnect/cleanup.</summary>
        public void ClearAll()
        {
            _grid?.Clear();
            _peerInterests.Clear();
        }

        #endregion

        #region Helpers

        /// <summary>
        /// Objects that should always be visible to all peers, regardless of distance.
        /// </summary>
        private static bool IsAlwaysVisible(NetworkObject obj, ProductUserId peer)
        {
            // Reserved objects (room state, player state) are always visible
            if (obj.PrefabId == NetworkRoomState.PREFAB_ID ||
                obj.PrefabId == NetworkPlayerState.PREFAB_ID)
                return true;

            // Owner always sees their own objects
            if (obj.OwnerId != null && obj.OwnerId.Equals(peer))
                return true;

            // User-flagged always visible
            if (obj.AlwaysVisible)
                return true;

            return false;
        }

        /// <summary>Get a peer's world position from their first owned object.</summary>
        private static Vector3? GetPeerPosition(ProductUserId peer, NetworkManager nm)
        {
            if (nm == null || peer == null) return null;

            foreach (var kvp in nm.Objects)
            {
                var obj = kvp.Value;
                if (obj != null && obj.OwnerId != null && obj.OwnerId.Equals(peer)
                    && obj.PrefabId != NetworkRoomState.PREFAB_ID
                    && obj.PrefabId != NetworkPlayerState.PREFAB_ID)
                {
                    return obj.transform.position;
                }
            }
            return null;
        }

        #endregion
    }
}
