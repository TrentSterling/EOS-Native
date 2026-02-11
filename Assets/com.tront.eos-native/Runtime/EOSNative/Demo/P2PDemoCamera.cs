using UnityEngine;

namespace EOSNative.Demo
{
    /// <summary>
    /// Top-down follow camera for the P2P demo.
    /// Finds the local P2PPlayerBall and snaps to its world position with an offset.
    /// </summary>
    public class P2PDemoCamera : MonoBehaviour
    {
        public Vector3 offset = new(0f, 10f, -5f);

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

            transform.position = _target.transform.position + offset;
            transform.LookAt(_target.transform.position);
        }
    }
}
