using UnityEngine;

namespace EOSNative.Demo
{
    /// <summary>
    /// Top-down follow camera for the P2P demo.
    /// Finds the local P2PPlayerBall and smoothly follows with an offset.
    /// </summary>
    public class P2PDemoCamera : MonoBehaviour
    {
        public Vector3 offset = new(0f, 10f, -5f);
        public float smoothSpeed = 8f;

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
                        break;
                    }
                }
                if (_target == null) return;
            }

            Vector3 desired = _target.transform.position + offset;
            transform.position = Vector3.Lerp(transform.position, desired, Time.deltaTime * smoothSpeed);
            transform.LookAt(_target.transform.position);
        }
    }
}
