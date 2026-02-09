using UnityEngine;

namespace EOSNative.Net
{
    /// <summary>
    /// Opt-in client-side prediction and lag compensation component.
    /// Attach to any GameObject with a <see cref="NetworkObject"/> to enable:
    /// <list type="bullet">
    ///   <item><description>State history recording every tick (for lag compensation rewind)</description></item>
    ///   <item><description>Prediction correction with visual smoothing (for host-validated RPCs)</description></item>
    /// </list>
    ///
    /// <para>
    /// <b>Recording:</b> Every tick, the object's position, rotation, and velocity are
    /// saved into a <see cref="StateHistory"/> ring buffer. <see cref="LagCompensation"/>
    /// uses this history to rewind objects for hit detection.
    /// </para>
    ///
    /// <para>
    /// <b>Correction:</b> When the authoritative state arrives (e.g. host validates an RPC
    /// and sends back the result), call <see cref="ApplyCorrection"/> with the server's state.
    /// If the error exceeds the threshold, physics snaps immediately and a visual offset
    /// blends to zero over <see cref="_correctionBlendTime"/> using exponential decay.
    /// </para>
    ///
    /// <para>
    /// Coexists with <see cref="NetworkTransform"/> — they serve different purposes.
    /// NetworkTransform handles remote object smoothing (interpolation/spring/extrapolation).
    /// NetworkPrediction handles local prediction history and authoritative correction.
    /// </para>
    ///
    /// <example>
    /// <code>
    /// // Attach to a player prefab:
    /// [RequireComponent(typeof(NetworkObject))]
    /// public class MyPlayer : NetworkBehaviour
    /// {
    ///     void OnValidatedRPCResult(uint tick, Vector3 authPos, Quaternion authRot)
    ///     {
    ///         GetComponent&lt;NetworkPrediction&gt;().ApplyCorrection(
    ///             tick, authPos, authRot, Vector3.zero, Vector3.zero);
    ///     }
    /// }
    /// </code>
    /// </example>
    /// </summary>
    [RequireComponent(typeof(NetworkObject))]
    public class NetworkPrediction : MonoBehaviour
    {
        [Header("Correction Thresholds")]
        [Tooltip("Position error (meters) below which corrections are ignored.")]
        [SerializeField] private float _correctionThreshold = 0.05f;

        [Tooltip("Rotation error (degrees) below which corrections are ignored.")]
        [SerializeField] private float _rotationCorrectionThreshold = 5f;

        [Header("Visual Smoothing")]
        [Tooltip("Time in seconds to blend visual offset to zero after a correction.")]
        [SerializeField] private float _correctionBlendTime = 0.1f;

        [Header("History")]
        [Tooltip("Ring buffer capacity. 64 entries = ~2.1s at 30 ticks/sec.")]
        [SerializeField] private int _historyCapacity = 64;

        private StateHistory _history;
        private Rigidbody _rb;
        private Vector3 _visualOffset;
        private Quaternion _visualRotOffset = Quaternion.identity;
        private bool _blending;

        /// <summary>The state history ring buffer. Read by <see cref="LagCompensation"/>.</summary>
        public StateHistory History => _history;

        /// <summary>True while visual smoothing is blending after a correction.</summary>
        public bool IsBlending => _blending;

        /// <summary>Current visual offset magnitude (for debugging/UI).</summary>
        public float VisualOffsetMagnitude => _visualOffset.magnitude;

        private void Awake()
        {
            _history = new StateHistory(_historyCapacity);
            _rb = GetComponent<Rigidbody>();
        }

        private void OnEnable()
        {
            LagCompensation.Register(this);

            var tick = TickSimulation.Instance;
            if (tick != null)
                tick.OnTick += RecordTick;
        }

        private void OnDisable()
        {
            LagCompensation.Unregister(this);

            var tick = TickSimulation.Instance;
            if (tick != null)
                tick.OnTick -= RecordTick;
        }

        /// <summary>Get the cached Rigidbody (may be null).</summary>
        internal Rigidbody GetRigidbody() => _rb;

        private void RecordTick(uint tickIndex, float dt)
        {
            var snapshot = new StateSnapshot
            {
                Tick = tickIndex,
                Position = transform.position,
                Rotation = transform.rotation,
                Velocity = _rb != null ? _rb.linearVelocity : Vector3.zero,
                AngularVelocity = _rb != null ? _rb.angularVelocity : Vector3.zero
            };

            _history.Record(snapshot);
        }

        /// <summary>
        /// Apply an authoritative correction from the host/server.
        /// Compares the predicted state at the given tick with the authoritative state.
        /// If the error exceeds thresholds, snaps physics and starts visual blending.
        /// </summary>
        /// <param name="tick">The tick this correction is for.</param>
        /// <param name="authPosition">Authoritative position.</param>
        /// <param name="authRotation">Authoritative rotation.</param>
        /// <param name="authVelocity">Authoritative velocity.</param>
        /// <param name="authAngularVelocity">Authoritative angular velocity.</param>
        public void ApplyCorrection(uint tick, Vector3 authPosition, Quaternion authRotation,
            Vector3 authVelocity, Vector3 authAngularVelocity)
        {
            // Look up what we predicted at that tick
            bool hasPredicted = _history.TryGetAtTick(tick, out var predicted);

            if (!hasPredicted)
            {
                // No history for that tick — snap directly
                SnapTo(authPosition, authRotation, authVelocity, authAngularVelocity);
                return;
            }

            float posError = Vector3.Distance(predicted.Position, authPosition);
            float rotError = Quaternion.Angle(predicted.Rotation, authRotation);

            bool needsCorrection = posError > _correctionThreshold ||
                                   rotError > _rotationCorrectionThreshold;

            if (!needsCorrection) return;

            // Calculate visual offset BEFORE snapping (current visual pos - auth pos)
            _visualOffset = transform.position - authPosition;
            _visualRotOffset = Quaternion.Inverse(authRotation) * transform.rotation;

            // Snap physics immediately
            SnapTo(authPosition, authRotation, authVelocity, authAngularVelocity);

            // Start visual blending
            _blending = true;
        }

        private void SnapTo(Vector3 pos, Quaternion rot, Vector3 vel, Vector3 angVel)
        {
            transform.position = pos;
            transform.rotation = rot;

            if (_rb != null)
            {
                _rb.linearVelocity = vel;
                _rb.angularVelocity = angVel;
            }
        }

        private void LateUpdate()
        {
            if (!_blending) return;

            // Exponential decay toward zero offset
            float blendSpeed = _correctionBlendTime > 0f ? 1f / _correctionBlendTime : 100f;
            float decay = 1f - Mathf.Exp(-blendSpeed * Time.deltaTime);

            _visualOffset = Vector3.Lerp(_visualOffset, Vector3.zero, decay);
            _visualRotOffset = Quaternion.Slerp(_visualRotOffset, Quaternion.identity, decay);

            // Apply offset to visual position (render position = physics position + offset)
            // This creates the smooth blend effect without affecting physics
            transform.position += _visualOffset;
            transform.rotation = transform.rotation * _visualRotOffset;

            // Stop blending when offset is negligible
            if (_visualOffset.sqrMagnitude < 0.0001f &&
                Quaternion.Angle(_visualRotOffset, Quaternion.identity) < 0.1f)
            {
                _visualOffset = Vector3.zero;
                _visualRotOffset = Quaternion.identity;
                _blending = false;
            }
        }
    }
}
