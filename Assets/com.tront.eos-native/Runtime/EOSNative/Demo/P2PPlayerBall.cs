using UnityEngine;
#if EOS_HAS_INPUT_SYSTEM
using UnityEngine.InputSystem;
#endif

namespace EOSNative.Demo
{
    /// <summary>
    /// WASD ball controller for the P2P demo.
    /// Ported from FishNet PlayerBall.cs with FishNet dependencies removed.
    /// Only reads input when IsLocal is true.
    /// </summary>
    [RequireComponent(typeof(Rigidbody))]
    public class P2PPlayerBall : MonoBehaviour
    {
        [Header("Movement")]
        [SerializeField] private float _acceleration = 25f;
        [SerializeField] private float _maxSpeed = 12f;
        [SerializeField] [Range(0f, 1f)] private float _friction = 0.15f;
        [SerializeField] private float _brakeMultiplier = 2f;

        [Header("Jumping")]
        [SerializeField] private float _jumpForce = 8f;
        [SerializeField] private float _groundCheckDistance = 0.15f;
        [SerializeField] private LayerMask _groundLayers = ~0;
        [SerializeField] private float _jumpCooldown = 0.1f;

        /// <summary>Set by demo manager. Only local balls read input.</summary>
        public bool IsLocal { get; set; }

        private Rigidbody _rb;
        private Vector2 _input;
        private bool _wantsJump;
        private float _lastJumpTime;
        private Renderer _renderer;

        private void Awake()
        {
            _rb = GetComponent<Rigidbody>();
            _renderer = GetComponent<Renderer>();
        }

        private void Update()
        {
            if (!IsLocal) return;
            ReadInput();
        }

        private void FixedUpdate()
        {
            if (!IsLocal) return;

            bool isGrounded = Physics.SphereCast(
                transform.position, 0.4f, Vector3.down, out _,
                _groundCheckDistance, _groundLayers, QueryTriggerInteraction.Ignore
            );

            // Jump
            if (_wantsJump && isGrounded)
            {
                Vector3 vel = _rb.linearVelocity;
                vel.y = _jumpForce;
                _rb.linearVelocity = vel;
                _lastJumpTime = Time.time;
                _wantsJump = false;
            }
            else if (_wantsJump && !isGrounded)
            {
                _wantsJump = false;
            }

            // Movement
            Vector3 currentVel = _rb.linearVelocity;
            Vector3 horizontalVel = new Vector3(currentVel.x, 0f, currentVel.z);
            Vector3 inputDir = new Vector3(_input.x, 0f, _input.y);
            bool hasInput = inputDir.sqrMagnitude > 0.01f;

            if (hasInput)
            {
                Vector3 targetVel = inputDir * _maxSpeed;
                float dot = Vector3.Dot(horizontalVel.normalized, inputDir.normalized);
                bool isBraking = dot < -0.5f && horizontalVel.magnitude > 1f;

                Vector3 velDiff = targetVel - horizontalVel;
                float accel = _acceleration * (isBraking ? _brakeMultiplier : 1f);
                Vector3 velocityChange = Vector3.ClampMagnitude(velDiff, accel * Time.fixedDeltaTime);

                _rb.AddForce(velocityChange, ForceMode.VelocityChange);
            }
            else if (isGrounded)
            {
                if (horizontalVel.magnitude > 0.1f)
                    _rb.AddForce(-horizontalVel * _friction, ForceMode.VelocityChange);
                else if (horizontalVel.magnitude > 0f)
                    _rb.linearVelocity = new Vector3(0f, currentVel.y, 0f);
            }

            // Clamp max speed
            horizontalVel = new Vector3(_rb.linearVelocity.x, 0f, _rb.linearVelocity.z);
            if (horizontalVel.magnitude > _maxSpeed)
            {
                Vector3 clamped = horizontalVel.normalized * _maxSpeed;
                _rb.linearVelocity = new Vector3(clamped.x, _rb.linearVelocity.y, clamped.z);
            }
        }

        /// <summary>Set the ball's visual color.</summary>
        public void SetColor(Color color)
        {
            if (_renderer == null) _renderer = GetComponent<Renderer>();
            if (_renderer != null)
                _renderer.material.color = color;
        }

        private void ReadInput()
        {
#if EOS_HAS_INPUT_SYSTEM
            var keyboard = Keyboard.current;
            if (keyboard == null) return;

            _input = Vector2.zero;
            if (keyboard.wKey.isPressed) _input.y += 1f;
            if (keyboard.sKey.isPressed) _input.y -= 1f;
            if (keyboard.aKey.isPressed) _input.x -= 1f;
            if (keyboard.dKey.isPressed) _input.x += 1f;
            _input = Vector2.ClampMagnitude(_input, 1f);

            if (keyboard.spaceKey.wasPressedThisFrame && Time.time >= _lastJumpTime + _jumpCooldown)
                _wantsJump = true;
#else
            _input = Vector2.zero;
            if (Input.GetKey(KeyCode.W)) _input.y += 1f;
            if (Input.GetKey(KeyCode.S)) _input.y -= 1f;
            if (Input.GetKey(KeyCode.A)) _input.x -= 1f;
            if (Input.GetKey(KeyCode.D)) _input.x += 1f;
            _input = Vector2.ClampMagnitude(_input, 1f);

            if (Input.GetKeyDown(KeyCode.Space) && Time.time >= _lastJumpTime + _jumpCooldown)
                _wantsJump = true;
#endif
        }
    }
}
