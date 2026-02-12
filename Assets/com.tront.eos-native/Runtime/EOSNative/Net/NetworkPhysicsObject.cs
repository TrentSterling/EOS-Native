using UnityEngine;

namespace EOSNative.Net
{
    /// <summary>
    /// Makes a networked rigidbody interactive for all clients.
    /// - Forces non-kinematic so spring physics works on non-owners
    /// - Claims ownership on collision with locally-owned objects (responsive pushing)
    /// - Works with P2PSpringSync for Layer 1 position sync
    ///
    /// Ported from FishNet EOS Native Demo's NetworkPhysicsObject
    /// (credit: DrewMileham, Skylar/CometDev).
    /// </summary>
    [RequireComponent(typeof(Rigidbody))]
    [RequireComponent(typeof(NetworkObject))]
    public class NetworkPhysicsObject : NetworkBehaviour
    {
        [Header("Physics")]
        [Tooltip("Force non-kinematic every FixedUpdate so spring physics works on non-owners.")]
        [SerializeField] private bool _forceNonKinematic = true;

        [Header("Ownership Claiming")]
        [Tooltip("Request ownership when a locally-owned object collides with this.")]
        [SerializeField] private bool _claimOnCollision = true;

        [Tooltip("Minimum collision force to trigger ownership claim.")]
        [SerializeField] private float _minCollisionForce = 0.5f;

        [Tooltip("Cooldown before ownership can be claimed again (prevents rapid swapping).")]
        [SerializeField] private float _ownershipCooldown = 0.25f;

        private Rigidbody _rb;
        private float _lastClaimTime;

        protected override void Awake()
        {
            base.Awake();
            _rb = GetComponent<Rigidbody>();
        }

        private void FixedUpdate()
        {
            if (!Net.IsRegistered) return;

            // Force non-kinematic so spring physics and collision interactions work.
            // NetworkTransform sets kinematic on non-owners in some tiers — override that here.
            if (_forceNonKinematic && _rb.isKinematic)
                _rb.isKinematic = false;
        }

        private void OnCollisionEnter(Collision collision)
        {
            TryClaimFromCollision(collision);
        }

        private void OnCollisionStay(Collision collision)
        {
            if (Time.time < _lastClaimTime + _ownershipCooldown * 2f)
                return;
            TryClaimFromCollision(collision);
        }

        private void TryClaimFromCollision(Collision collision)
        {
            if (!Net.IsRegistered) return;
            if (!_claimOnCollision) return;
            if (Net.IsOwner) return;
            if (Time.time < _lastClaimTime + _ownershipCooldown) return;
            if (collision.relativeVelocity.magnitude < _minCollisionForce) return;

            // Only claim if the colliding object is locally owned (player's ball, etc.)
            var otherNet = collision.gameObject.GetComponentInParent<NetworkObject>();
            if (otherNet == null || !otherNet.IsOwner) return;

            _lastClaimTime = Time.time;
            NetworkManager.Instance?.RequestAuthority(Net);
        }
    }
}
