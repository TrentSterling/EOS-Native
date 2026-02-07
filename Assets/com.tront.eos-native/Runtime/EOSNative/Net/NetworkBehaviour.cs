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

        protected virtual void Awake()
        {
            Net = GetComponent<NetworkObject>();
            if (Net == null)
                Net = gameObject.AddComponent<NetworkObject>();
        }
    }
}
