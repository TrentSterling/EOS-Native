using System.Collections;
using EOSNative.Net;
using EOSNative.P2P;
using UnityEngine;

namespace EOSNative.Demo
{
    /// <summary>
    /// Layer 2 networking component for the ball demo.
    /// Demonstrates SyncVars and [NetRpc] typed RPCs on a prefab-based object
    /// registered via RegisterExisting() with deterministic IDs.
    ///
    /// SyncVars: Score, DisplayName, BallColor
    /// RPCs: ApplyImpulse, ChangeColor, ChatBubble, PlayEffect, RequestScorePoint
    /// </summary>
    public class DemoBallBehaviour : NetworkBehaviour
    {
        #region SyncVars

        public SyncVar<int> Score;
        public SyncVar<string> DisplayName;
        public SyncVar<Color> BallColor;

        #endregion

        #region Chat Bubble State

        private string _chatMessage;
        private float _chatExpireTime;

        #endregion

        #region Effect State

        private float _effectExpireTime;
        private byte _activeEffect;

        #endregion

        protected override void Awake()
        {
            base.Awake();

            Score = Sync(0);
            DisplayName = Sync(string.Empty);
            BallColor = Sync(Color.white);

            // Apply color when it changes (for late-joiners and remote color changes)
            BallColor.OnChanged += (_, newColor) => ApplyColorToRenderer(newColor);
        }

        public override void OnNetworkSpawn()
        {
            // Apply initial state from SyncVars
            ApplyColorToRenderer(BallColor.Value);
        }

        private void ApplyColorToRenderer(Color color)
        {
            var renderer = GetComponent<Renderer>();
            if (renderer != null)
                renderer.material.color = color;
        }

        #region RPCs

        [NetRpc(RPCTarget.All)]
        public void ApplyImpulse(float dirX, float dirY, float dirZ, float force)
        {
            var rb = GetComponent<Rigidbody>();
            if (rb != null)
            {
                var dir = new Vector3(dirX, dirY, dirZ).normalized;
                rb.AddForce(dir * force, ForceMode.Impulse);
            }
        }

        [NetRpc(RPCTarget.All)]
        public void ChangeColor(float r, float g, float b)
        {
            var color = new Color(r, g, b);
            if (IsOwner)
                BallColor.Value = color;
            ApplyColorToRenderer(color);
        }

        [NetRpc(RPCTarget.All)]
        public void ChatBubble(string message)
        {
            _chatMessage = message;
            _chatExpireTime = Time.time + 3f;
        }

        [NetRpc(RPCTarget.All)]
        public void PlayEffect(byte effectId)
        {
            _activeEffect = effectId;
            _effectExpireTime = Time.time + 1f;
        }

        [NetRpc(RPCTarget.Owner)]
        public void RequestScorePoint(int amount)
        {
            if (IsOwner)
                Score.Value += amount;
        }

        [NetRpc(RPCTarget.All)]
        public void TakeHit(float fromX, float fromZ)
        {
            // Knockback away from shooter
            var rb = GetComponent<Rigidbody>();
            if (rb != null)
            {
                var away = (transform.position - new Vector3(fromX, transform.position.y, fromZ)).normalized;
                rb.AddForce((away + Vector3.up * 0.3f) * 5f, ForceMode.Impulse);
            }

            // Red flash
            StartCoroutine(FlashRedCoroutine());

            // Score decrement (owner only — SyncVar propagates to all)
            if (IsOwner)
                Score.Value -= 1;
        }

        private IEnumerator FlashRedCoroutine()
        {
            var rend = GetComponent<Renderer>();
            if (rend == null) yield break;
            rend.material.color = Color.red;
            yield return new WaitForSeconds(0.2f);
            rend.material.color = BallColor.Value;
        }

        #endregion

        #region HUD

        private void OnGUI()
        {
            if (Camera.main == null || DisplayName == null) return;

            Vector3 worldPos = transform.position + Vector3.up * 1.8f;
            Vector3 screenPos = Camera.main.WorldToScreenPoint(worldPos);
            if (screenPos.z <= 0) return;

            float x = screenPos.x;
            float y = Screen.height - screenPos.y;

            var nameStyle = new GUIStyle(GUI.skin.label)
            {
                alignment = TextAnchor.MiddleCenter,
                fontSize = 13,
                fontStyle = FontStyle.Bold,
                normal = { textColor = Color.white }
            };

            var scoreStyle = new GUIStyle(GUI.skin.label)
            {
                alignment = TextAnchor.MiddleCenter,
                fontSize = 11,
                normal = { textColor = Color.yellow }
            };

            // Name
            string name = DisplayName.Value;
            if (string.IsNullOrEmpty(name))
            {
                if (IsOwner)
                    name = "YOU";
                else if (Net != null && Net.OwnerId != null)
                {
                    var registry = EOSNative.EOSPlayerRegistry.Instance;
                    name = registry != null
                        ? registry.GetOrGenerateName(Net.OwnerId.ToString())
                        : Net.OwnerId.ToString().Substring(0, Mathf.Min(6, Net.OwnerId.ToString().Length));
                }
                else
                    name = "???";
            }
            float nameWidth = Mathf.Max(80f, name.Length * 9f);
            GUI.Label(new Rect(x - nameWidth / 2f, y - 20f, nameWidth, 20f), name, nameStyle);

            // Score
            if (Score.Value != 0)
            {
                string scoreText = $"Score: {Score.Value}";
                GUI.Label(new Rect(x - 40f, y, 80f, 18f), scoreText, scoreStyle);
            }

            // Chat bubble
            if (!string.IsNullOrEmpty(_chatMessage) && Time.time < _chatExpireTime)
            {
                var chatStyle = new GUIStyle(GUI.skin.box)
                {
                    alignment = TextAnchor.MiddleCenter,
                    fontSize = 12,
                    normal = { textColor = Color.white }
                };
                float chatWidth = Mathf.Max(100f, _chatMessage.Length * 8f);
                GUI.Label(new Rect(x - chatWidth / 2f, y - 50f, chatWidth, 24f), _chatMessage, chatStyle);
            }

            // Armed indicator (weapon held)
            var weapon = GetComponentInChildren<DemoWeapon>();
            if (weapon != null)
            {
                var armedStyle = new GUIStyle(GUI.skin.label)
                {
                    alignment = TextAnchor.MiddleCenter,
                    fontSize = 12,
                    fontStyle = FontStyle.Bold,
                    normal = { textColor = new Color(1f, 0.6f, 0f) }
                };
                GUI.Label(new Rect(x - 30f, y + 18f, 60f, 20f), "[ARMED]", armedStyle);
            }

            // Effect indicator
            if (Time.time < _effectExpireTime)
            {
                var effectStyle = new GUIStyle(GUI.skin.label)
                {
                    alignment = TextAnchor.MiddleCenter,
                    fontSize = 16,
                    normal = { textColor = Color.cyan }
                };
                string effectText = _activeEffect switch
                {
                    0 => "*FLASH*",
                    1 => "~RING~",
                    2 => ">>TRAIL<<",
                    _ => "*FX*"
                };
                GUI.Label(new Rect(x - 40f, y + 18f, 80f, 20f), effectText, effectStyle);
            }
        }

        #endregion
    }
}
