using EOSNative.Net;
using UnityEngine;

namespace EOSNative.Demo
{
    /// <summary>
    /// Second NetworkBehaviour on the PlayerBall prefab.
    /// Demonstrates per-NB scoped SyncVars (v2.40.0) with varied types.
    ///
    /// This component tracks gameplay stats that are separate from DemoBallBehaviour's
    /// core state (Score, DisplayName, BallColor). Each NB has its own SyncVar pool
    /// and ComponentIndex, so dirty tracking and wire serialization are independent.
    ///
    /// SyncVars: Health (float), IsGrounded (bool), Velocity (Vector3),
    ///           Kills (short), Deaths (short), PlayTime (double), Team (byte)
    /// </summary>
    public class DemoBallStats : NetworkBehaviour
    {
        #region SyncVars

        public SyncVar<float> Health;
        public SyncVar<bool> IsGrounded;
        public SyncVar<Vector3> Velocity;
        public SyncVar<short> Kills;
        public SyncVar<short> Deaths;
        public SyncVar<double> PlayTime;
        public SyncVar<byte> Team;

        #endregion

        private float _startTime;
        private Rigidbody _rb;

        protected override void Awake()
        {
            base.Awake();

            Health = Sync(100f);
            IsGrounded = Sync(false);
            Velocity = Sync(Vector3.zero);
            Kills = Sync<short>(0);
            Deaths = Sync<short>(0);
            PlayTime = Sync(0.0);
            Team = Sync<byte>(0);
        }

        public override void OnNetworkSpawn()
        {
            _rb = GetComponent<Rigidbody>();
            _startTime = Time.time;
        }

        private void FixedUpdate()
        {
            if (!IsOwner || _rb == null) return;

            // Update velocity SyncVar (syncs smoothly to remote peers)
            Velocity.Value = _rb.linearVelocity;

            // Ground check
            bool grounded = Physics.Raycast(transform.position, Vector3.down, 0.6f);
            if (grounded != IsGrounded.Value)
                IsGrounded.Value = grounded;

            // Accumulate play time
            PlayTime.Value = Time.time - _startTime;
        }

        #region RPCs

        [NetRpc(RPCTarget.All)]
        public void TakeDamage(float amount)
        {
            if (!IsOwner) return;
            Health.Value = Mathf.Max(0f, Health.Value - amount);
            if (Health.Value <= 0f)
                Die();
        }

        [NetRpc(RPCTarget.All)]
        public void Heal(float amount)
        {
            if (!IsOwner) return;
            Health.Value = Mathf.Min(100f, Health.Value + amount);
        }

        [NetRpc(RPCTarget.All)]
        public void SetTeam(byte teamId)
        {
            if (IsOwner)
                Team.Value = teamId;
        }

        [NetRpc(RPCTarget.All)]
        public void ReportKill()
        {
            if (IsOwner)
                Kills.Value++;
        }

        private void Die()
        {
            Deaths.Value++;
            Health.Value = 100f;
            // Reset position
            transform.position = new Vector3(
                Random.Range(-3f, 3f), 2f, Random.Range(-3f, 3f));
            if (_rb != null) _rb.linearVelocity = Vector3.zero;
        }

        #endregion

        #region HUD (Stats Panel)

        private void OnGUI()
        {
            if (Camera.main == null || !IsOwner) return;

            // Show stats in bottom-left corner for local player only
            float y = Screen.height - 130f;
            float x = 10f;
            float w = 200f;

            var bgStyle = new GUIStyle(GUI.skin.box);
            GUI.Box(new Rect(x - 5, y - 5, w + 10, 125), "", bgStyle);

            var labelStyle = new GUIStyle(GUI.skin.label)
            {
                fontSize = 12,
                normal = { textColor = Color.white }
            };

            GUI.Label(new Rect(x, y, w, 18), $"HP: {Health.Value:F0} / 100", labelStyle);
            y += 18;

            Color teamColor = Team.Value switch
            {
                1 => Color.red,
                2 => Color.blue,
                3 => Color.green,
                _ => Color.gray
            };
            var teamStyle = new GUIStyle(labelStyle) { normal = { textColor = teamColor } };
            GUI.Label(new Rect(x, y, w, 18), $"Team: {(Team.Value == 0 ? "None" : Team.Value.ToString())}", teamStyle);
            y += 18;

            GUI.Label(new Rect(x, y, w, 18), $"K/D: {Kills.Value} / {Deaths.Value}", labelStyle);
            y += 18;

            GUI.Label(new Rect(x, y, w, 18), $"Speed: {Velocity.Value.magnitude:F1} m/s", labelStyle);
            y += 18;

            GUI.Label(new Rect(x, y, w, 18), $"Grounded: {IsGrounded.Value}", labelStyle);
            y += 18;

            int minutes = (int)(PlayTime.Value / 60);
            int seconds = (int)(PlayTime.Value % 60);
            GUI.Label(new Rect(x, y, w, 18), $"Time: {minutes}:{seconds:D2}", labelStyle);
        }

        #endregion
    }
}
