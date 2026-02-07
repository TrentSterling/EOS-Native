using UnityEngine;

namespace EOSNative.Net
{
    /// <summary>
    /// How remote objects follow networked state.
    /// </summary>
    public enum SyncMethod
    {
        /// <summary>Spring if Rigidbody present, Interpolation if kinematic.</summary>
        Auto,
        /// <summary>Damped spring physics. Best for physics objects (balls, vehicles, ragdolls).</summary>
        Spring,
        /// <summary>Buffered lerp interpolation. Best for kinematic objects (characters, platforms, UI).</summary>
        Interpolation
    }

    /// <summary>
    /// What to do when the state buffer runs out (packet loss, latency spike).
    /// </summary>
    public enum ExtrapolationMode
    {
        /// <summary>Freeze at last known position.</summary>
        None,
        /// <summary>Predict forward with velocity, capped by time and distance.</summary>
        Limited,
        /// <summary>Predict forward indefinitely.</summary>
        Unlimited
    }

    /// <summary>
    /// Hybrid NetworkTransform: spring physics, interpolation, extrapolation, and distance LOD
    /// in a single component.
    ///
    /// Non-owner sync pipeline:
    ///   SyncVar data -> State Buffer -> Target Calculation -> Application Method
    ///                                   (interp/extrap)      (spring/lerp/snap)
    ///
    /// Distance LOD tiers:
    ///   Full (near)    -> Spring or Interpolation (configured SyncMethod)
    ///   Tweened (mid)  -> Simple lerp toward target
    ///   Simple (far)   -> Snap to target
    ///
    /// Credits: Spring physics from PhysicsNetworkTransform.cs (DrewMileham, Skylar/CometDev).
    ///          Interpolation architecture inspired by SmoothSync (Jim Burrows).
    /// </summary>
    [RequireComponent(typeof(NetworkObject))]
    public class NetworkTransform : MonoBehaviour
    {
        #region Internal Types

        private enum SyncTier { Full, Tweened, Simple }

        private struct TransformState
        {
            public float receivedTime;
            public Vector3 position;
            public Quaternion rotation;
            public Vector3 velocity;
            public Vector3 angularVelocity;
        }

        #endregion

        #region Settings

        [Header("What to Sync")]
        [SerializeField] private bool _syncPosition = true;
        [SerializeField] private bool _syncRotation = true;
        [SerializeField] private bool _syncScale = false;

        [Header("Sync Method")]
        [Tooltip("Auto = Spring for Rigidbody, Interpolation for kinematic")]
        [SerializeField] private SyncMethod _syncMethod = SyncMethod.Auto;

        [Header("Interpolation (SyncMethod = Interpolation)")]
        [Tooltip("Render delay in seconds. Higher = smoother but more latent. 0.1 = good default.")]
        [SerializeField] private float _interpolationDelay = 0.1f;
        [Tooltip("Easing speed toward interpolated target (0-1). 1 = instant snap to target.")]
        [SerializeField][Range(0.1f, 1f)] private float _positionEaseSpeed = 0.85f;
        [Tooltip("Rotation easing speed (0-1).")]
        [SerializeField][Range(0.1f, 1f)] private float _rotationEaseSpeed = 0.85f;

        [Header("Extrapolation")]
        [Tooltip("What to do when no new states arrive (packet loss, latency spike).")]
        [SerializeField] private ExtrapolationMode _extrapolationMode = ExtrapolationMode.Limited;
        [Tooltip("Max seconds to extrapolate before freezing.")]
        [SerializeField] private float _extrapolationTimeLimit = 5f;
        [Tooltip("Max meters from last known position before freezing.")]
        [SerializeField] private float _extrapolationDistanceLimit = 20f;

        [Header("Spring Physics (SyncMethod = Spring)")]
        [Tooltip("Position spring frequency in Hz. Higher = stiffer/snappier.")]
        [SerializeField] private float _posSpringFreq = 8f;
        [Tooltip("Position damping. 0.5=bouncy, 1.0=critically damped, >1=overdamped.")]
        [SerializeField][Range(0.1f, 2f)] private float _posDampingRatio = 0.9f;
        [Tooltip("Rotation spring frequency in Hz.")]
        [SerializeField] private float _rotSpringFreq = 10f;
        [Tooltip("Rotation damping ratio.")]
        [SerializeField][Range(0.1f, 2f)] private float _rotDampingRatio = 0.85f;

        [Header("Snap / Teleport Thresholds")]
        [Tooltip("Snap if position error exceeds this (meters). 0 = never snap.")]
        [SerializeField] private float _posSnapDistance = 5f;
        [Tooltip("Snap if rotation error exceeds this (degrees). 0 = never snap.")]
        [SerializeField] private float _rotSnapAngle = 90f;

        [Header("Send Thresholds (Owner Only)")]
        [Tooltip("Minimum position change to trigger sync (meters).")]
        [SerializeField] private float _positionThreshold = 0.001f;
        [Tooltip("Minimum rotation change to trigger sync (degrees).")]
        [SerializeField] private float _rotationThreshold = 0.1f;

        [Header("Distance LOD")]
        [Tooltip("Auto-reduce sync quality based on camera distance to save CPU.")]
        [SerializeField] private bool _useDistanceLOD = false;
        [Tooltip("Full spring/interp sync below this distance (meters).")]
        [SerializeField] private float _fullSyncDistance = 10f;
        [Tooltip("Snap sync above this distance. Lerp between full and simple.")]
        [SerializeField] private float _simpleSyncDistance = 30f;
        [Tooltip("Hysteresis buffer to prevent tier flickering at boundaries.")]
        [SerializeField] private float _lodDeadZone = 5f;

        [Header("Rest Detection")]
        [Tooltip("Seconds without new data before assuming at-rest (stops extrapolation).")]
        [SerializeField] private float _restTimeout = 0.5f;

        #endregion

        #region Internal State

        private NetworkObject _net;
        private Rigidbody _rb;
        private bool _wasKinematic;

        // SyncVars
        private SyncVar<Vector3> _syncPos;
        private SyncVar<Quaternion> _syncRot;
        private SyncVar<Vector3> _syncScl;

        // Latest target from SyncVars
        private Vector3 _targetPos;
        private Quaternion _targetRot = Quaternion.identity;
        private Vector3 _targetScl = Vector3.one;

        // Velocity estimation
        private Vector3 _estimatedVelocity;
        private Vector3 _estimatedAngularVelocity;

        // Spring velocities (Transform mode + scale)
        private Vector3 _springVelocity;
        private Vector3 _angularSpringVelocity;
        private Vector3 _scaleSpringVelocity;

        // Interpolation state buffer
        private const int BUFFER_SIZE = 30;
        private TransformState[] _stateBuffer;
        private int _stateCount;

        // Extrapolation tracking
        private float _timeSpentExtrapolating;
        private bool _extrapolatedLastFrame;
        private Vector3 _extrapPos;
        private Quaternion _extrapRot;
        private Vector3 _extrapVel;
        private Vector3 _extrapAngVel;

        // Distance LOD
        private SyncTier _currentTier = SyncTier.Full;

        // Rest detection
        private float _lastStateReceivedTime;

        #endregion

        #region Properties

        /// <summary>Resolved sync method (Auto resolves to Spring or Interpolation).</summary>
        public SyncMethod ResolvedMethod =>
            _syncMethod == SyncMethod.Auto
                ? (_rb != null ? SyncMethod.Spring : SyncMethod.Interpolation)
                : _syncMethod;

        #endregion

        #region Lifecycle

        private void Awake()
        {
            _net = GetComponent<NetworkObject>();
            if (_net == null)
                _net = gameObject.AddComponent<NetworkObject>();

            _rb = GetComponent<Rigidbody>();
            _wasKinematic = _rb != null && _rb.isKinematic;
            _stateBuffer = new TransformState[BUFFER_SIZE];

            if (_syncPosition)
            {
                _syncPos = _net.Sync(transform.position);
                _syncPos.OnChanged += OnPositionChanged;
            }

            if (_syncRotation)
            {
                _syncRot = _net.Sync(transform.rotation);
                _syncRot.OnChanged += OnRotationChanged;
            }

            if (_syncScale)
            {
                _syncScl = _net.Sync(transform.localScale);
                _syncScl.OnChanged += OnScaleChanged;
            }

            _targetPos = transform.position;
            _targetRot = transform.rotation;
            _targetScl = transform.localScale;
            _lastStateReceivedTime = Time.time;

            if (_syncMethod == SyncMethod.Interpolation && _rb != null && !_rb.isKinematic)
            {
                Debug.LogWarning(
                    $"[NetworkTransform] '{name}': Interpolation mode with non-kinematic Rigidbody may cause jitter. " +
                    "Consider Spring mode or setting isKinematic=true.", this);
            }
        }

        private void Update()
        {
            if (_net.IsOwner)
            {
                WriteOwnerState();
                return;
            }

            _currentTier = _useDistanceLOD ? GetSyncTier() : SyncTier.Full;

            // Manage Rigidbody kinematic state for LOD tiers
            if (_rb != null)
            {
                bool needsPhysics = _currentTier == SyncTier.Full && ResolvedMethod == SyncMethod.Spring;
                _rb.isKinematic = needsPhysics ? _wasKinematic : true;
            }

            switch (_currentTier)
            {
                case SyncTier.Simple:
                    ApplySimple();
                    break;
                case SyncTier.Tweened:
                    ApplyTweened();
                    break;
                case SyncTier.Full:
                    if (ResolvedMethod == SyncMethod.Interpolation)
                        ApplyInterpolation();
                    else if (_rb == null) // Spring + no Rigidbody -> closed-form in Update
                        ApplySpringTransform();
                    // Spring + Rigidbody -> handled in FixedUpdate
                    break;
            }

            if (_syncScale && _currentTier == SyncTier.Full)
                ApplyScaleSpring();
        }

        private void FixedUpdate()
        {
            // Spring + Rigidbody only runs in Full tier on non-owners
            if (_net.IsOwner) return;
            if (_rb == null) return;
            if (_currentTier != SyncTier.Full) return;
            if (ResolvedMethod != SyncMethod.Spring) return;

            ApplySpringRigidbody();
        }

        #endregion

        #region Owner Write

        private void WriteOwnerState()
        {
            if (_syncPosition && _syncPos != null)
            {
                Vector3 pos = _rb != null ? _rb.position : transform.position;
                if (Vector3.SqrMagnitude(pos - _syncPos.Value) >
                    _positionThreshold * _positionThreshold)
                {
                    _syncPos.Value = pos;
                }
            }

            if (_syncRotation && _syncRot != null)
            {
                Quaternion rot = _rb != null ? _rb.rotation : transform.rotation;
                if (Quaternion.Angle(rot, _syncRot.Value) > _rotationThreshold)
                {
                    _syncRot.Value = rot;
                }
            }

            if (_syncScale && _syncScl != null)
            {
                if (Vector3.SqrMagnitude(transform.localScale - _syncScl.Value) >
                    _positionThreshold * _positionThreshold)
                {
                    _syncScl.Value = transform.localScale;
                }
            }
        }

        #endregion

        #region Spring Sync (Rigidbody — force-based, physics objects)

        private void ApplySpringRigidbody()
        {
            // --- Position spring (mass-aware) ---
            if (_syncPosition)
            {
                float dist = Vector3.Distance(_rb.position, _targetPos);

                if (_posSnapDistance > 0f && dist > _posSnapDistance)
                {
                    _rb.position = _targetPos;
                    _rb.linearVelocity = _estimatedVelocity;
                }
                else
                {
                    // Extrapolate target ahead using estimated velocity
                    float timeSince = Time.time - _lastStateReceivedTime;
                    Vector3 extTarget = _targetPos + _estimatedVelocity * Mathf.Min(timeSince, 0.2f);

                    float mass = _rb.mass;
                    float omega = 2f * Mathf.PI * _posSpringFreq;
                    float k = mass * omega * omega;
                    float c = 2f * mass * _posDampingRatio * omega;

                    Vector3 error = extTarget - _rb.position;
                    Vector3 velError = _estimatedVelocity - _rb.linearVelocity;
                    Vector3 force = error * k + velError * c;

                    if (IsValid(force))
                        _rb.AddForce(force, ForceMode.Force);
                }
            }

            // --- Rotation spring (inertia-aware) ---
            if (_syncRotation)
            {
                float angle = Quaternion.Angle(_rb.rotation, _targetRot);

                if (_rotSnapAngle > 0f && angle > _rotSnapAngle)
                {
                    _rb.rotation = _targetRot;
                    _rb.angularVelocity = Vector3.zero;
                }
                else
                {
                    float inertia = Mathf.Max(_rb.inertiaTensor.magnitude, 0.001f);
                    float omega = 2f * Mathf.PI * _rotSpringFreq;
                    float k = inertia * omega * omega;
                    float c = 2f * inertia * _rotDampingRatio * omega;

                    Quaternion delta = _targetRot * Quaternion.Inverse(_rb.rotation);
                    if (delta.w < 0f)
                        delta = new Quaternion(-delta.x, -delta.y, -delta.z, -delta.w);

                    delta.ToAngleAxis(out float deltaAngle, out Vector3 axis);
                    Vector3 angError = deltaAngle < 0.001f || float.IsNaN(axis.x)
                        ? Vector3.zero
                        : axis.normalized * (deltaAngle * Mathf.Deg2Rad);

                    Vector3 torque = angError * k + (-_rb.angularVelocity) * c;
                    if (IsValid(torque))
                        _rb.AddTorque(torque, ForceMode.Force);
                }
            }
        }

        #endregion

        #region Spring Sync (Transform — closed-form, kinematic/non-physics)

        private void ApplySpringTransform()
        {
            float dt = Time.deltaTime;
            if (dt <= 0f) return;

            // --- Position spring ---
            if (_syncPosition)
            {
                float dist = Vector3.Distance(transform.position, _targetPos);

                if (_posSnapDistance > 0f && dist > _posSnapDistance)
                {
                    transform.position = _targetPos;
                    _springVelocity = _estimatedVelocity;
                }
                else
                {
                    float timeSince = Time.time - _lastStateReceivedTime;
                    Vector3 extTarget = _targetPos + _estimatedVelocity * Mathf.Min(timeSince, 0.2f);

                    float omega = 2f * Mathf.PI * _posSpringFreq;
                    float k = omega * omega;
                    float c = 2f * _posDampingRatio * omega;

                    Vector3 error = transform.position - extTarget;
                    Vector3 accel = -k * error - c * _springVelocity;
                    _springVelocity += accel * dt;
                    transform.position += _springVelocity * dt;
                }
            }

            // --- Rotation spring ---
            if (_syncRotation)
            {
                float angle = Quaternion.Angle(transform.rotation, _targetRot);

                if (_rotSnapAngle > 0f && angle > _rotSnapAngle)
                {
                    transform.rotation = _targetRot;
                    _angularSpringVelocity = Vector3.zero;
                }
                else
                {
                    float omega = 2f * Mathf.PI * _rotSpringFreq;
                    float k = omega * omega;
                    float c = 2f * _rotDampingRatio * omega;

                    Quaternion delta = _targetRot * Quaternion.Inverse(transform.rotation);
                    if (delta.w < 0f)
                        delta = new Quaternion(-delta.x, -delta.y, -delta.z, -delta.w);

                    delta.ToAngleAxis(out float deltaAngle, out Vector3 axis);
                    Vector3 angError = deltaAngle < 0.001f || float.IsNaN(axis.x)
                        ? Vector3.zero
                        : axis.normalized * (deltaAngle * Mathf.Deg2Rad);

                    Vector3 angAccel = k * angError - c * _angularSpringVelocity;
                    _angularSpringVelocity += angAccel * dt;

                    float angMag = _angularSpringVelocity.magnitude;
                    if (angMag > 0.0001f)
                    {
                        Quaternion step = Quaternion.AngleAxis(
                            angMag * Mathf.Rad2Deg * dt,
                            _angularSpringVelocity / angMag);
                        transform.rotation = step * transform.rotation;
                    }
                }
            }
        }

        private void ApplyScaleSpring()
        {
            float dt = Time.deltaTime;
            if (dt <= 0f) return;

            float omega = 2f * Mathf.PI * _posSpringFreq;
            float k = omega * omega;
            float c = 2f * _posDampingRatio * omega;

            Vector3 error = transform.localScale - _targetScl;
            Vector3 accel = -k * error - c * _scaleSpringVelocity;
            _scaleSpringVelocity += accel * dt;
            transform.localScale += _scaleSpringVelocity * dt;
        }

        #endregion

        #region Interpolation (SmoothSync-style buffered lerp)

        private void ApplyInterpolation()
        {
            if (_stateCount == 0) return;

            float targetTime = Time.time - _interpolationDelay;
            bool isAtRest = Time.time - _lastStateReceivedTime > _restTimeout;

            Vector3 targetPos = _stateBuffer[0].position;
            Quaternion targetRot = _stateBuffer[0].rotation;

            // Case 1: We have states bracketing the target time -> interpolate
            if (_stateCount > 1 && _stateBuffer[0].receivedTime > targetTime)
            {
                // Find the first state older than (or equal to) target time
                int idx = 0;
                for (; idx < _stateCount; idx++)
                {
                    if (_stateBuffer[idx].receivedTime <= targetTime)
                        break;
                }
                if (idx >= _stateCount) idx = _stateCount - 1;

                TransformState start = _stateBuffer[idx];
                TransformState end = _stateBuffer[Mathf.Max(idx - 1, 0)];

                float duration = end.receivedTime - start.receivedTime;
                float t = duration > 0.0001f
                    ? Mathf.Clamp01((targetTime - start.receivedTime) / duration)
                    : 1f;

                targetPos = Vector3.Lerp(start.position, end.position, t);
                targetRot = Quaternion.Slerp(start.rotation, end.rotation, t);

                _extrapolatedLastFrame = false;
                _timeSpentExtrapolating = 0f;
            }
            // Case 2: At rest -> hold last known position
            else if (isAtRest)
            {
                targetPos = _stateBuffer[0].position;
                targetRot = _stateBuffer[0].rotation;
                _extrapolatedLastFrame = false;
            }
            // Case 3: Buffer exhausted -> extrapolate
            else if (Extrapolate(out var extPos, out var extRot))
            {
                targetPos = extPos;
                targetRot = extRot;
                _extrapolatedLastFrame = true;
            }
            else
            {
                // Extrapolation limit hit -> freeze
                _extrapolatedLastFrame = false;
            }

            // Apply with easing (second smoothing stage)
            if (_syncPosition)
            {
                float dist = Vector3.Distance(transform.position, targetPos);
                if (_posSnapDistance > 0f && dist > _posSnapDistance)
                    transform.position = targetPos;
                else
                    transform.position = Vector3.Lerp(transform.position, targetPos, _positionEaseSpeed);
            }

            if (_syncRotation)
            {
                float angle = Quaternion.Angle(transform.rotation, targetRot);
                if (_rotSnapAngle > 0f && angle > _rotSnapAngle)
                    transform.rotation = targetRot;
                else
                    transform.rotation = Quaternion.Slerp(transform.rotation, targetRot, _rotationEaseSpeed);
            }
        }

        #endregion

        #region Extrapolation (velocity-based prediction)

        private bool Extrapolate(out Vector3 pos, out Quaternion rot)
        {
            pos = _stateBuffer[0].position;
            rot = _stateBuffer[0].rotation;

            if (_extrapolationMode == ExtrapolationMode.None || _stateCount == 0)
                return false;

            var latest = _stateBuffer[0];

            // Initialize on first extrapolation frame
            if (!_extrapolatedLastFrame)
            {
                _extrapPos = latest.position;
                _extrapRot = latest.rotation;
                _extrapVel = latest.velocity;
                _extrapAngVel = latest.angularVelocity;
                _timeSpentExtrapolating = 0f;
            }

            // Nothing to predict without velocity
            if (_extrapVel.sqrMagnitude < 0.0001f && _extrapAngVel.sqrMagnitude < 0.0001f)
                return false;

            // Time step
            float dt;
            if (!_extrapolatedLastFrame)
            {
                // First frame: catch up from latest state to current target time
                dt = Mathf.Max(0f, (Time.time - _interpolationDelay) - latest.receivedTime);
            }
            else
            {
                dt = Time.deltaTime;
            }

            _timeSpentExtrapolating += dt;

            // Time limit
            if (_extrapolationMode == ExtrapolationMode.Limited &&
                _timeSpentExtrapolating > _extrapolationTimeLimit)
                return false;

            // Advance position
            if (_extrapVel.sqrMagnitude > 0.0001f)
            {
                _extrapPos += _extrapVel * dt;

                // Apply gravity if Rigidbody with useGravity
                if (_rb != null && _rb.useGravity)
                    _extrapVel += Physics.gravity * dt;

                // Apply linear drag
                if (_rb != null && _rb.linearDamping > 0f)
                    _extrapVel *= Mathf.Max(0f, 1f - dt * _rb.linearDamping);
            }

            // Advance rotation
            float angMag = _extrapAngVel.magnitude;
            if (angMag > 0.0001f)
            {
                Quaternion step = Quaternion.AngleAxis(
                    angMag * Mathf.Rad2Deg * dt,
                    _extrapAngVel / angMag);
                _extrapRot = step * _extrapRot;

                // Apply angular drag
                if (_rb != null && _rb.angularDamping > 0f)
                    _extrapAngVel *= Mathf.Max(0f, 1f - dt * _rb.angularDamping);
            }

            // Distance limit
            if (_extrapolationMode == ExtrapolationMode.Limited &&
                Vector3.Distance(latest.position, _extrapPos) > _extrapolationDistanceLimit)
                return false;

            pos = _extrapPos;
            rot = _extrapRot;
            return true;
        }

        #endregion

        #region Distance LOD

        private SyncTier GetSyncTier()
        {
            Camera cam = Camera.main;
            if (cam == null) return SyncTier.Full;

            float sqrDist = (cam.transform.position - transform.position).sqrMagnitude;

            // Hysteresis: leaving your current tier requires passing the dead zone
            switch (_currentTier)
            {
                case SyncTier.Full:
                {
                    // Need to go FURTHER to leave Full
                    float threshold = _fullSyncDistance + _lodDeadZone;
                    if (sqrDist <= threshold * threshold)
                        return SyncTier.Full;
                    float simpleSq = _simpleSyncDistance * _simpleSyncDistance;
                    return sqrDist >= simpleSq ? SyncTier.Simple : SyncTier.Tweened;
                }
                case SyncTier.Simple:
                {
                    // Need to come CLOSER to leave Simple
                    float threshold = Mathf.Max(_simpleSyncDistance - _lodDeadZone, _fullSyncDistance);
                    if (sqrDist >= threshold * threshold)
                        return SyncTier.Simple;
                    float fullSq = _fullSyncDistance * _fullSyncDistance;
                    return sqrDist <= fullSq ? SyncTier.Full : SyncTier.Tweened;
                }
                default: // Tweened
                {
                    float fullSq = _fullSyncDistance * _fullSyncDistance;
                    if (sqrDist <= fullSq)
                        return SyncTier.Full;
                    float threshold = _simpleSyncDistance + _lodDeadZone;
                    return sqrDist >= threshold * threshold ? SyncTier.Simple : SyncTier.Tweened;
                }
            }
        }

        #endregion

        #region LOD Tier Application

        private void ApplyTweened()
        {
            if (_syncPosition)
                transform.position = Vector3.Lerp(transform.position, _targetPos, _positionEaseSpeed);
            if (_syncRotation)
                transform.rotation = Quaternion.Slerp(transform.rotation, _targetRot, _rotationEaseSpeed);
            if (_syncScale)
                transform.localScale = Vector3.Lerp(transform.localScale, _targetScl, _positionEaseSpeed);
        }

        private void ApplySimple()
        {
            if (_syncPosition)
                transform.position = _targetPos;
            if (_syncRotation)
                transform.rotation = _targetRot;
            if (_syncScale)
                transform.localScale = _targetScl;
        }

        #endregion

        #region State Buffer

        private void PushState(Vector3 pos, Quaternion rot, float receivedTime)
        {
            // Teleport detection: large position jump clears buffer and snaps
            if (_stateCount > 0 && _posSnapDistance > 0f)
            {
                float dist = Vector3.Distance(pos, _stateBuffer[0].position);
                if (dist > _posSnapDistance)
                {
                    _stateCount = 0;
                    _extrapolatedLastFrame = false;
                    _timeSpentExtrapolating = 0f;
                    _springVelocity = Vector3.zero;
                    _angularSpringVelocity = Vector3.zero;

                    if (_rb != null)
                    {
                        _rb.position = pos;
                        _rb.rotation = rot;
                        _rb.linearVelocity = Vector3.zero;
                        _rb.angularVelocity = Vector3.zero;
                    }
                    else
                    {
                        transform.position = pos;
                        transform.rotation = rot;
                    }
                }
            }

            // Shift buffer right (index 0 = newest)
            int shiftCount = Mathf.Min(_stateCount, BUFFER_SIZE - 1);
            for (int i = shiftCount; i > 0; i--)
                _stateBuffer[i] = _stateBuffer[i - 1];

            _stateBuffer[0] = new TransformState
            {
                receivedTime = receivedTime,
                position = pos,
                rotation = rot,
                velocity = _estimatedVelocity,
                angularVelocity = _estimatedAngularVelocity
            };

            _stateCount = Mathf.Min(_stateCount + 1, BUFFER_SIZE);
        }

        #endregion

        #region SyncVar Callbacks

        private void OnPositionChanged(Vector3 oldPos, Vector3 newPos)
        {
            if (_net.IsOwner) return;

            float now = Time.time;

            // Estimate velocity from consecutive position deltas
            float elapsed = now - _lastStateReceivedTime;
            if (elapsed > 0.001f && elapsed < 1f)
                _estimatedVelocity = (newPos - _targetPos) / elapsed;
            else
                _estimatedVelocity = Vector3.zero;

            _targetPos = newPos;
            _lastStateReceivedTime = now;

            // Push into interpolation buffer (used by Interpolation mode)
            PushState(newPos, _targetRot, now);
        }

        private void OnRotationChanged(Quaternion oldRot, Quaternion newRot)
        {
            if (_net.IsOwner) return;

            float now = Time.time;

            // Estimate angular velocity from consecutive rotation deltas
            float elapsed = now - _lastStateReceivedTime;
            if (elapsed > 0.001f && elapsed < 1f)
            {
                Quaternion delta = newRot * Quaternion.Inverse(_targetRot);
                if (delta.w < 0f)
                    delta = new Quaternion(-delta.x, -delta.y, -delta.z, -delta.w);

                delta.ToAngleAxis(out float angle, out Vector3 axis);
                if (angle > 0.001f && !float.IsNaN(axis.x))
                    _estimatedAngularVelocity = axis.normalized * (angle * Mathf.Deg2Rad) / elapsed;
                else
                    _estimatedAngularVelocity = Vector3.zero;
            }

            _targetRot = newRot;

            // Update latest buffer entry if one was pushed this frame
            if (_stateCount > 0 && Mathf.Approximately(_stateBuffer[0].receivedTime, now))
                _stateBuffer[0].rotation = newRot;
        }

        private void OnScaleChanged(Vector3 oldScl, Vector3 newScl)
        {
            if (!_net.IsOwner)
                _targetScl = newScl;
        }

        #endregion

        #region Public API

        /// <summary>
        /// Teleport this object instantly. On owner, sets transform and forces SyncVar sync.
        /// On remotes, large jumps auto-snap via the snap threshold.
        /// For remotes to snap, posSnapDistance must be greater than 0.
        /// </summary>
        public void Teleport(Vector3 position, Quaternion rotation)
        {
            // Reset springs
            _springVelocity = Vector3.zero;
            _angularSpringVelocity = Vector3.zero;
            _estimatedVelocity = Vector3.zero;
            _estimatedAngularVelocity = Vector3.zero;

            if (_rb != null)
            {
                _rb.position = position;
                _rb.rotation = rotation;
                _rb.linearVelocity = Vector3.zero;
                _rb.angularVelocity = Vector3.zero;
            }
            else
            {
                transform.position = position;
                transform.rotation = rotation;
            }

            if (_net.IsOwner)
            {
                if (_syncPos != null) _syncPos.Value = position;
                if (_syncRot != null) _syncRot.Value = rotation;
            }
        }

        /// <summary>Teleport position only, keep current rotation.</summary>
        public void Teleport(Vector3 position) => Teleport(position, transform.rotation);

        #endregion

        #region Utility

        private static bool IsValid(Vector3 v)
        {
            return !float.IsNaN(v.x) && !float.IsNaN(v.y) && !float.IsNaN(v.z) &&
                   !float.IsInfinity(v.x) && !float.IsInfinity(v.y) && !float.IsInfinity(v.z);
        }

        #endregion
    }
}
