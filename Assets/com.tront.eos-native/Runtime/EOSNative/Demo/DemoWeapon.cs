using EOSNative.Net;
using UnityEngine;

namespace EOSNative.Demo
{
    /// <summary>
    /// Weapon pickup for the P2P Ball Demo.
    /// Tests reparenting: SetNetworkParent (pickup), DetachFromNetworkParent (drop/throw).
    /// Floats above the ball when held, has physics when dropped.
    /// Supports mouse-aimed hitscan shooting when held.
    /// </summary>
    public class DemoWeapon : NetworkBehaviour
    {
        public SyncVar<string> HolderName;
        public SyncVar<float> AimAngle;

        private Rigidbody _rb;
        private Renderer _renderer;
        private Color _baseColor;
        private float _fireCooldown;
        private float _flashExpire;

        protected override void Awake()
        {
            base.Awake();
            HolderName = Sync(string.Empty);
            AimAngle = Sync(0f);
            _rb = GetComponent<Rigidbody>();
            _renderer = GetComponent<Renderer>();
            if (_renderer != null)
                _baseColor = _renderer.material.color;
        }

        public override void OnNetworkSpawn()
        {
            Net.OnReparented += OnReparented;
            AimAngle.OnChanged += (_, angle) => ApplyAimRotation(angle);
        }

        public override void OnNetworkDespawn()
        {
            Net.OnReparented -= OnReparented;
        }

        private void Update()
        {
            // Flash recovery
            if (_flashExpire > 0f && Time.time >= _flashExpire && _renderer != null)
            {
                _renderer.material.color = _baseColor;
                _flashExpire = 0f;
            }

            if (_fireCooldown > 0f)
                _fireCooldown -= Time.deltaTime;
        }

        private void ApplyAimRotation(float angle)
        {
            if (!IsHeld) return;
            transform.localRotation = Quaternion.Euler(0f, angle, 0f);
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

        /// <summary>True if weapon can fire (cooldown elapsed).</summary>
        public bool CanFire => _fireCooldown <= 0f;

        /// <summary>Set the aim direction (owner only). Updates SyncVar and local rotation.</summary>
        public void SetAimDirection(Vector3 aimDir)
        {
            if (!IsHeld) return;
            float angle = Mathf.Atan2(aimDir.x, aimDir.z) * Mathf.Rad2Deg;
            AimAngle.Value = angle;
            ApplyAimRotation(angle);
        }

        /// <summary>Owner-only: perform a hitscan shot in the given direction.</summary>
        public void ShootHitscan(Vector3 aimDir)
        {
            if (_fireCooldown > 0f) return;
            _fireCooldown = 0.2f;

            var origin = transform.position;
            var dir = aimDir.normalized;
            float hitDist = -1f;

            // Hitscan raycast (shooter-authoritative for hit detection + damage)
            if (Physics.Raycast(origin, dir, out var hit, 50f))
            {
                hitDist = hit.distance;

                // Ball-specific: score damage (shooter-authoritative)
                var victim = hit.collider.GetComponentInParent<DemoBallBehaviour>();
                if (victim != null && victim != GetComponentInParent<DemoBallBehaviour>())
                    victim.TakeHit(origin.x, origin.z);
            }

            // Fire the tracer RPC for all peers
            Fire(origin.x, origin.y, origin.z, dir.x, dir.y, dir.z, hitDist);
        }

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

        [NetRpc(RPCTarget.All)]
        public void Fire(float ox, float oy, float oz, float dx, float dy, float dz, float hitDist)
        {
            var origin = new Vector3(ox, oy, oz);
            var direction = new Vector3(dx, dy, dz).normalized;
            float length = hitDist > 0f ? hitDist : 30f;
            CreateTracer(origin, direction, length);

            // Impact spark at hit point
            if (hitDist > 0f)
                CreateImpactEffect(origin + direction * hitDist);

            // Rigidbody knockback on all peers — owner sim is authoritative,
            // non-owners get local prediction that spring sync smooths out
            if (hitDist > 0f)
            {
                if (Physics.Raycast(origin, direction, out var hit, hitDist + 1f))
                {
                    var hitRb = hit.collider.GetComponentInParent<Rigidbody>();
                    if (hitRb != null && !hitRb.isKinematic)
                        hitRb.AddForce(direction * 10f + Vector3.up * 2f, ForceMode.Impulse);
                }
            }

            // Flash weapon white briefly
            if (_renderer != null)
            {
                _renderer.material.color = Color.white;
                _flashExpire = Time.time + 0.1f;
            }
        }

        /// <summary>Creates a visual tracer line (stretched cube) that auto-destructs.</summary>
        public static void CreateTracer(Vector3 origin, Vector3 direction, float length = 30f)
        {
            var tracer = GameObject.CreatePrimitive(PrimitiveType.Cube);
            tracer.name = "Tracer";

            // Remove collider (safe if physics module is stripped)
            try
            {
                var col = tracer.GetComponent<Collider>();
                if (col != null) Destroy(col);
            }
            catch { /* physics module stripped */ }

            tracer.transform.localScale = new Vector3(0.05f, 0.05f, length);
            tracer.transform.position = origin + direction * (length * 0.5f);
            tracer.transform.rotation = Quaternion.LookRotation(direction);

            var rend = tracer.GetComponent<Renderer>();
            if (rend != null)
            {
                rend.material.color = new Color(1f, 0.9f, 0.2f);
                rend.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
                rend.receiveShadows = false;
            }

            Destroy(tracer, 0.15f);
        }

        /// <summary>Creates a bright impact spark at the hit point that auto-destructs.</summary>
        public static void CreateImpactEffect(Vector3 point)
        {
            var spark = GameObject.CreatePrimitive(PrimitiveType.Sphere);
            spark.name = "Impact";

            try
            {
                var col = spark.GetComponent<Collider>();
                if (col != null) Destroy(col);
            }
            catch { /* physics module stripped */ }

            spark.transform.position = point;
            spark.transform.localScale = Vector3.one * 0.4f;

            var rend = spark.GetComponent<Renderer>();
            if (rend != null)
            {
                rend.material.color = new Color(1f, 0.95f, 0.5f);
                rend.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
                rend.receiveShadows = false;
            }

            Destroy(spark, 0.12f);
        }
    }
}
