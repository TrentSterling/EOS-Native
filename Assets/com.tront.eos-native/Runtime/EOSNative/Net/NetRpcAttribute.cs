using System;
using Epic.OnlineServices;
using Epic.OnlineServices.P2P;

namespace EOSNative.Net
{
    /// <summary>
    /// Mark a method on a <see cref="NetworkBehaviour"/> as a networked RPC.
    /// The IL post-processor rewrites the method so that callers automatically
    /// serialize args and dispatch over the P2P mesh.
    ///
    /// <example>
    /// <code>
    /// [NetRpc(RPCTarget.All)]
    /// public void TakeDamage(float damage)
    /// {
    ///     Health.Value -= damage;
    /// }
    ///
    /// // Calling is transparent — all peers execute TakeDamage:
    /// player.TakeDamage(19f);
    /// </code>
    /// </example>
    /// </summary>
    [AttributeUsage(AttributeTargets.Method)]
    public class NetRpcAttribute : Attribute
    {
        /// <summary>Who receives this RPC when the method is called.</summary>
        public RPCTarget Target { get; }

        /// <summary>
        /// If true, this RPC is routed through the host for validation before being
        /// broadcast to all peers. The host runs the Validate_MethodName method (if present)
        /// and only rebroadcasts if it returns true. Adds ~20-40ms latency (one extra hop).
        /// The IL weaver auto-discovers validation methods by convention — no nameof needed.
        /// </summary>
        public bool Validated { get; set; }

        /// <summary>
        /// If true, the RPC method body always executes locally on the caller in addition
        /// to being sent to remote peers. Useful for RPCTarget.Others when you want the
        /// caller to also run the logic without waiting for a round-trip.
        /// <example><code>
        /// [NetRpc(RPCTarget.Others, RunLocally = true)]
        /// public void ApplyDamage(float amount) { Health.Value -= amount; }
        /// // Caller runs ApplyDamage immediately, others receive it over the network
        /// </code></example>
        /// </summary>
        public bool RunLocally { get; set; }

        /// <summary>
        /// If true, the object's owner is excluded from remote delivery.
        /// Useful for effects the owner handles locally but wants to broadcast to observers.
        /// <example><code>
        /// [NetRpc(RPCTarget.All, ExcludeOwner = true)]
        /// public void PlayHitReaction() { ... }
        /// // All peers except the owner see the hit reaction
        /// </code></example>
        /// </summary>
        public bool ExcludeOwner { get; set; }

        /// <summary>
        /// EOS P2P channel to send this RPC on. Default is 1 (reliable RPC channel).
        /// Use channel 0 for unreliable position-like updates. Channels batch independently.
        /// </summary>
        public byte Channel { get; set; } = 1;

        /// <summary>
        /// Packet reliability for this RPC. Default is ReliableOrdered (2).
        /// Use UnreliableUnordered (0) for position/rotation updates that can tolerate loss.
        /// <example><code>
        /// [NetRpc(RPCTarget.All, Channel = 0, Reliability = PacketReliability.UnreliableUnordered)]
        /// public void SyncPosition(Vector3 pos) { ... }
        /// </code></example>
        /// </summary>
        public PacketReliability Reliability { get; set; } = PacketReliability.ReliableOrdered;

        public NetRpcAttribute(RPCTarget target = RPCTarget.All)
        {
            Target = target;
        }
    }

    /// <summary>
    /// Guard attribute: only execute this RPC if the sender is the current host.
    /// Non-host RPCs are silently rejected at reception time.
    /// Can be combined with any <see cref="RPCTarget"/>.
    /// <example><code>
    /// [NetRpc(RPCTarget.All), HostOnly]
    /// public void StartRound(int roundNumber) { ... }
    /// </code></example>
    /// </summary>
    [AttributeUsage(AttributeTargets.Method)]
    public class HostOnlyAttribute : Attribute { }

    /// <summary>
    /// Guard attribute: only execute this RPC if the sender is the object's owner.
    /// Non-owner RPCs are silently rejected at reception time.
    /// Can be combined with any <see cref="RPCTarget"/>.
    /// <example><code>
    /// [NetRpc(RPCTarget.All), OwnerOnly]
    /// public void TakeDamage(float damage) { ... }
    /// </code></example>
    /// </summary>
    [AttributeUsage(AttributeTargets.Method)]
    public class OwnerOnlyAttribute : Attribute { }
}
