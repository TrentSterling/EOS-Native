using System;
using Epic.OnlineServices;
using EOSNative.P2P;
using UnityEngine;

namespace EOSNative.Net
{
    /// <summary>
    /// Abstract MonoBehaviour base class for singleton game managers that need tick callbacks
    /// and network events but don't live on a NetworkObject.
    /// Examples: match manager, wave spawner, score tracker.
    /// Override only the callbacks you need — all are no-ops by default.
    /// </summary>
    public abstract class SimulationBehaviour : MonoBehaviour
    {
        // --- Convenience accessors (mirrors NetworkBehaviour) ---

        protected NetworkManager Manager => NetworkManager.Instance;
        protected bool IsHost => NetworkManager.Instance?.IsHost ?? false;
        protected bool IsOnline => NetworkManager.Instance?.IsOnline ?? false;
        protected uint CurrentTick => TickSimulation.Instance?.CurrentTick ?? 0;
        protected float FixedTickTime => TickSimulation.Instance?.FixedTickTime ?? 0f;

        // --- Virtual callbacks (override what you need) ---

        /// <summary>Called every network tick with tick number and delta time.</summary>
        protected virtual void OnTick(uint tick, float deltaTime) { }

        /// <summary>Called after all tick processing is complete (state updates sent).</summary>
        protected virtual void OnPostTick() { }

        /// <summary>Called when a new peer connects.</summary>
        protected virtual void OnPeerConnected(ProductUserId peer) { }

        /// <summary>Called when a peer disconnects.</summary>
        protected virtual void OnPeerDisconnected(ProductUserId peer) { }

        /// <summary>Called when this peer becomes the host.</summary>
        protected virtual void OnBecameHost() { }

        /// <summary>Called when this peer is no longer the host.</summary>
        protected virtual void OnLostHost() { }

        // --- Auto-subscribe/unsubscribe ---

        private bool _wasHost;

        protected virtual void OnEnable()
        {
            var tick = TickSimulation.Instance;
            if (tick != null)
            {
                tick.OnTick += OnTick;
                tick.OnPostTick += OnPostTick;
            }

            var p2p = EOSP2PManager.Instance;
            if (p2p != null)
            {
                p2p.OnPeerConnected += OnPeerConnected;
                p2p.OnPeerDisconnected += OnPeerDisconnected;
            }

            var nm = NetworkManager.Instance;
            if (nm != null)
                nm.OnHostChanged += HandleHostChanged;

            _wasHost = NetworkManager.Instance?.IsHost ?? false;
        }

        protected virtual void OnDisable()
        {
            var tick = TickSimulation.Instance;
            if (tick != null)
            {
                tick.OnTick -= OnTick;
                tick.OnPostTick -= OnPostTick;
            }

            var p2p = EOSP2PManager.Instance;
            if (p2p != null)
            {
                p2p.OnPeerConnected -= OnPeerConnected;
                p2p.OnPeerDisconnected -= OnPeerDisconnected;
            }

            var nm = NetworkManager.Instance;
            if (nm != null)
                nm.OnHostChanged -= HandleHostChanged;
        }

        private void HandleHostChanged(bool isHost)
        {
            if (isHost && !_wasHost) OnBecameHost();
            else if (!isHost && _wasHost) OnLostHost();
            _wasHost = isHost;
        }
    }
}
