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

        public NetRpcAttribute(RPCTarget target = RPCTarget.All)
        {
            Target = target;
        }
    }
}
