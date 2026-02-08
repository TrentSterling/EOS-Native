using Epic.OnlineServices;
using UnityEngine;

namespace EOSNative.Net
{
    /// <summary>
    /// Optional convenience base class for networked components.
    /// Provides shortcuts to the NetworkObject's identity, ownership, and SyncVar creation.
    ///
    /// Users can also skip this and reference <see cref="NetworkObject"/> directly via GetComponent.
    /// </summary>
    public abstract class NetworkBehaviour : MonoBehaviour
    {
        /// <summary>The NetworkObject on this GameObject. Auto-added if missing.</summary>
        public NetworkObject Net { get; private set; }

        /// <summary>This object's network ID (0 if not yet registered).</summary>
        public uint NetworkId => Net != null ? Net.NetworkId : 0;

        /// <summary>True if the local peer owns this object.</summary>
        public bool IsOwner => Net != null && Net.IsOwner;

        /// <summary>True if the local peer is the current host.</summary>
        public bool IsHost => Net != null && Net.IsHost;

        /// <summary>The ProductUserId of the owning peer.</summary>
        public ProductUserId OwnerId => Net?.OwnerId;

        /// <summary>True if the local peer is a spectator (read-only observer).</summary>
        public bool IsSpectator => NetworkManager.Instance?.IsSpectator ?? false;

        /// <summary>Shortcut to NetworkManager.Instance.</summary>
        protected NetworkManager Manager => NetworkManager.Instance;

        /// <summary>
        /// Create and register a SyncVar on the NetworkObject.
        /// Call in Awake() after base.Awake().
        /// </summary>
        protected SyncVar<T> Sync<T>(T defaultValue = default)
        {
            return Net.Sync(defaultValue);
        }

        /// <summary>
        /// Create and register a SyncList on the NetworkObject.
        /// Call in Awake() after base.Awake().
        /// </summary>
        protected SyncList<T> SyncList<T>(System.Collections.Generic.List<T> initial = null)
        {
            return Net.SyncList(initial);
        }

        /// <summary>
        /// Create and register a SyncDictionary on the NetworkObject.
        /// Call in Awake() after base.Awake().
        /// </summary>
        protected SyncDictionary<TKey, TValue> SyncDictionary<TKey, TValue>(
            System.Collections.Generic.Dictionary<TKey, TValue> initial = null)
        {
            return Net.SyncDictionary(initial);
        }

        /// <summary>
        /// Called by NetworkManager after NetworkId is assigned and the object is registered.
        /// Override for post-spawn initialization (e.g. subscribing to events that need NetworkId).
        /// </summary>
        public virtual void OnNetworkSpawn() { }

        /// <summary>
        /// Called when the object is about to be despawned from the network.
        /// Override for cleanup before the object is deactivated/pooled.
        /// </summary>
        public virtual void OnNetworkDespawn() { }

        /// <summary>
        /// Weaver-generated override that registers all [NetRpc] handlers on this behaviour.
        /// Called by NetworkManager after NetworkId is assigned. Do not call manually.
        /// </summary>
        internal virtual void __RegisterNetRPCs() { }

        protected virtual void Awake()
        {
            Net = GetComponent<NetworkObject>();
            if (Net == null)
                Net = gameObject.AddComponent<NetworkObject>();
        }
    }
}
