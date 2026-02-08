using System.Runtime.InteropServices;
using UnityEngine;

namespace EOSNative.P2P
{
    /// <summary>
    /// Half-precision Vector3 for bandwidth-efficient position sync (6 bytes vs 12).
    /// </summary>
    [StructLayout(LayoutKind.Sequential)]
    public struct Vector3Half
    {
        public ushort x, y, z;

        public Vector3Half(Vector3 v)
        {
            x = Mathf.FloatToHalf(v.x);
            y = Mathf.FloatToHalf(v.y);
            z = Mathf.FloatToHalf(v.z);
        }

        public Vector3 ToVector3()
        {
            return new Vector3(
                Mathf.HalfToFloat(x),
                Mathf.HalfToFloat(y),
                Mathf.HalfToFloat(z)
            );
        }

        public byte[] ToBytes()
        {
            var bytes = new byte[6];
            bytes[0] = (byte)(x & 0xFF);
            bytes[1] = (byte)(x >> 8);
            bytes[2] = (byte)(y & 0xFF);
            bytes[3] = (byte)(y >> 8);
            bytes[4] = (byte)(z & 0xFF);
            bytes[5] = (byte)(z >> 8);
            return bytes;
        }

        public static Vector3Half FromBytes(byte[] bytes, int offset = 0)
        {
            return new Vector3Half
            {
                x = (ushort)(bytes[offset] | (bytes[offset + 1] << 8)),
                y = (ushort)(bytes[offset + 2] | (bytes[offset + 3] << 8)),
                z = (ushort)(bytes[offset + 4] | (bytes[offset + 5] << 8))
            };
        }
    }

    /// <summary>
    /// Reusable spring physics sync component.
    /// Ported from PhysicsNetworkTransform.cs (credit: DrewMileham, Skylar/CometDev).
    ///
    /// Local instances run physics normally and export compressed state.
    /// Remote instances receive target positions and are guided via damped spring forces.
    /// </summary>
    public class P2PSpringSync : MonoBehaviour
    {
        [Header("Spring Settings")]
        public float positionSpringFrequency = 4f;
        public float positionDampingRatio = 0.65f;
        public float rotationSpringFrequency = 5.5f;
        public float rotationDampingRatio = 0.55f;

        /// <summary>Set by the demo manager. True = this is the local player's ball.</summary>
        public bool IsLocal { get; set; }

        private Rigidbody _rb;
        private Vector3 _targetPosition;
        private Quaternion _targetRotation = Quaternion.identity;

        private void Awake()
        {
            _rb = GetComponent<Rigidbody>();
            _targetPosition = transform.position;
            _targetRotation = transform.rotation;
        }

        private void FixedUpdate()
        {
            if (IsLocal || _rb == null) return;
            ApplyPhysicsSpring();
        }

        /// <summary>Called on remote balls when a position packet arrives.</summary>
        public void SetTarget(Vector3 pos, Quaternion rot)
        {
            _targetPosition = pos;
            _targetRotation = rot;
        }

        /// <summary>Get compressed state for outgoing packets.</summary>
        public void GetCompressedState(out Vector3Half pos, out uint rot)
        {
            pos = new Vector3Half(_rb != null ? _rb.position : transform.position);
            rot = CompressRotation(_rb != null ? _rb.rotation : transform.rotation);
        }

        #region Spring Physics

        private void ApplyPhysicsSpring()
        {
            _rb.isKinematic = false;

            // Position spring (mass-aware)
            float mass = _rb.mass;
            float omega = 2f * Mathf.PI * positionSpringFrequency;
            float positionSpring = mass * omega * omega;
            float positionDamper = 2f * mass * positionDampingRatio * omega;

            Vector3 positionError = _targetPosition - _rb.position;
            Vector3 velocityError = -_rb.linearVelocity;
            Vector3 springForce = positionError * positionSpring + velocityError * positionDamper;

            if (!IsValidVector(springForce)) return;
            _rb.AddForce(springForce, ForceMode.Force);

            // Rotation spring (inertia-aware)
            float inertia = _rb.inertiaTensor.magnitude;
            float rotOmega = 2f * Mathf.PI * rotationSpringFrequency;
            float rotationSpring = inertia * rotOmega * rotOmega;
            float rotationDamper = 2f * inertia * rotationDampingRatio * rotOmega;

            Quaternion rotationDelta = _targetRotation * Quaternion.Inverse(_rb.rotation);
            if (rotationDelta.w < 0f)
                rotationDelta = new Quaternion(-rotationDelta.x, -rotationDelta.y, -rotationDelta.z, -rotationDelta.w);

            rotationDelta.ToAngleAxis(out float angle, out Vector3 axis);

            Vector3 angularError;
            if (angle < 0.001f || float.IsNaN(axis.x))
                angularError = Vector3.zero;
            else
                angularError = axis.normalized * (angle * Mathf.Deg2Rad);

            Vector3 angularVelocityError = -_rb.angularVelocity;
            Vector3 springTorque = angularError * rotationSpring + angularVelocityError * rotationDamper;

            if (!IsValidVector(springTorque)) return;
            _rb.AddTorque(springTorque, ForceMode.Force);
        }

        private static bool IsValidVector(Vector3 v)
        {
            return !float.IsNaN(v.x) && !float.IsNaN(v.y) && !float.IsNaN(v.z) &&
                   !float.IsInfinity(v.x) && !float.IsInfinity(v.y) && !float.IsInfinity(v.z);
        }

        #endregion

        #region Compression

        /// <summary>Smallest-three quaternion compression to 32 bits.</summary>
        public static uint CompressRotation(Quaternion rot)
        {
            float absX = Mathf.Abs(rot.x);
            float absY = Mathf.Abs(rot.y);
            float absZ = Mathf.Abs(rot.z);
            float absW = Mathf.Abs(rot.w);

            int largestIndex = 0;
            float largestValue = absX;
            if (absY > largestValue) { largestIndex = 1; largestValue = absY; }
            if (absZ > largestValue) { largestIndex = 2; largestValue = absZ; }
            if (absW > largestValue) { largestIndex = 3; }

            float a, b, c;
            switch (largestIndex)
            {
                case 0: a = rot.y; b = rot.z; c = rot.w; if (rot.x < 0) { a = -a; b = -b; c = -c; } break;
                case 1: a = rot.x; b = rot.z; c = rot.w; if (rot.y < 0) { a = -a; b = -b; c = -c; } break;
                case 2: a = rot.x; b = rot.y; c = rot.w; if (rot.z < 0) { a = -a; b = -b; c = -c; } break;
                default: a = rot.x; b = rot.y; c = rot.z; if (rot.w < 0) { a = -a; b = -b; c = -c; } break;
            }

            const float scale = 1023f / 1.41421356f;
            uint ua = (uint)Mathf.Clamp((int)((a + 0.707106781f) * scale), 0, 1023);
            uint ub = (uint)Mathf.Clamp((int)((b + 0.707106781f) * scale), 0, 1023);
            uint uc = (uint)Mathf.Clamp((int)((c + 0.707106781f) * scale), 0, 1023);

            return ((uint)largestIndex << 30) | (ua << 20) | (ub << 10) | uc;
        }

        /// <summary>Decompress smallest-three 32-bit quaternion.</summary>
        public static Quaternion DecompressRotation(uint compressed)
        {
            int largestIndex = (int)(compressed >> 30);
            const float scale = 1.41421356f / 1023f;

            float a = ((compressed >> 20) & 1023) * scale - 0.707106781f;
            float b = ((compressed >> 10) & 1023) * scale - 0.707106781f;
            float c = (compressed & 1023) * scale - 0.707106781f;

            float largest = Mathf.Sqrt(Mathf.Max(0f, 1f - a * a - b * b - c * c));

            switch (largestIndex)
            {
                case 0: return new Quaternion(largest, a, b, c);
                case 1: return new Quaternion(a, largest, b, c);
                case 2: return new Quaternion(a, b, largest, c);
                default: return new Quaternion(a, b, c, largest);
            }
        }

        #endregion
    }
}
