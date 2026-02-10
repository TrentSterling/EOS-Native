using EOSNative.Net;
using UnityEngine;

namespace EOSNative.Demo
{
    /// <summary>
    /// Weapon pickup for the P2P Ball Demo.
    /// Tests reparenting: SetNetworkParent (pickup), DetachFromNetworkParent (drop/throw).
    /// Floats above the ball when held, has physics when dropped.
    /// </summary>
    public class DemoWeapon : NetworkBehaviour
    {
        public SyncVar<string> HolderName;

        private Rigidbody _rb;
        private Renderer _renderer;

        protected override void Awake()
        {
            base.Awake();
            HolderName = Sync(string.Empty);
            _rb = GetComponent<Rigidbody>();
            _renderer = GetComponent<Renderer>();
        }

        public override void OnNetworkSpawn()
        {
            Net.OnReparented += OnReparented;
        }

        public override void OnNetworkDespawn()
        {
            Net.OnReparented -= OnReparented;
        }

        private void OnReparented(NetworkObject oldParent, NetworkObject newParent)
        {
            if (newParent != null)
            {
                // Picked up — snap above ball, disable physics
                if (_rb != null) _rb.isKinematic = true;
                transform.localPosition = new Vector3(0f, 0.7f, 0f);
                transform.localRotation = Quaternion.identity;
            }
            else
            {
                // Dropped — re-enable physics, small bounce
                if (_rb != null)
                {
                    _rb.isKinematic = false;
                    _rb.AddForce(Vector3.up * 3f, ForceMode.Impulse);
                }
            }
        }

        /// <summary>True if this weapon is currently held by a ball.</summary>
        public bool IsHeld => Net.ParentNetworkId != 0;

        /// <summary>Pick up this weapon and parent it under the given ball.</summary>
        public void Pickup(NetworkObject ball, string playerName)
        {
            if (IsHeld) return;
            Net.SetNetworkParent(ball);
            if (IsOwner)
                HolderName.Value = playerName;
        }

        /// <summary>Drop this weapon, detaching from parent.</summary>
        public void Drop()
        {
            if (!IsHeld) return;
            Net.DetachFromNetworkParent();
            if (IsOwner)
                HolderName.Value = string.Empty;
        }

        /// <summary>Throw this weapon in a direction with force.</summary>
        public void Throw(Vector3 direction, float force)
        {
            Drop();
            if (_rb != null)
                _rb.AddForce(direction * force + Vector3.up * 2f, ForceMode.Impulse);
        }
    }
}
