using System;

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
