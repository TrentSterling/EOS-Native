using UnityEngine;

namespace EOSNative.Demo
{
    /// <summary>
    /// Top-down follow camera for the P2P demo.
    /// Finds the local P2PPlayerBall and smoothly follows with an offset.
    /// Uses FRIM (framerate-independent) exponential decay for smooth, jitter-free following.
    /// See: tront.xyz/lerp
    /// </summary>
    public class P2PDemoCamera : MonoBehaviour
    {
        public Vector3 offset = new(0f, 10f, -5f);

        [Tooltip("Seconds to reach halfway to target. Lower = snappier. 0 = instant.")]
        public float halfLife = 0.15f;

        private P2PPlayerBall _target;

        private void LateUpdate()
        {
            if (_target == null)
            {
                foreach (var ball in FindObjectsByType<P2PPlayerBall>(FindObjectsSortMode.None))
                {
                    if (ball.IsLocal)
                    {
                        _target = ball;

                        // Ensure physics interpolation is on so transform doesn't jitter between FixedUpdate steps
                        var rb = _target.GetComponent<Rigidbody>();
                        if (rb != null && rb.interpolation == RigidbodyInterpolation.None)
                            rb.interpolation = RigidbodyInterpolation.Interpolate;

                        break;
                    }
                }
                if (_target == null) return;
            }

            Vector3 desired = _target.transform.position + offset;

            if (halfLife <= 0f)
            {
                transform.position = desired;
            }
            else
            {
                // FRIM half-life lerp: framerate-independent exponential decay
                float t = 1f - Mathf.Pow(2f, -Time.deltaTime / halfLife);
                transform.position = Vector3.Lerp(transform.position, desired, t);
            }

            transform.LookAt(_target.transform.position);
        }
    }
}
