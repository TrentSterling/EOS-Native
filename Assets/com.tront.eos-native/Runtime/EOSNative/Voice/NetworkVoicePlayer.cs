using EOSNative.Logging;
using EOSNative.Net;
using UnityEngine;

namespace EOSNative.Voice
{
    /// <summary>
    /// NetworkBehaviour wrapper that auto-wires EOSVoicePlayer to the correct
    /// participant based on NetworkObject ownership. Drop this on a player prefab
    /// alongside an AudioSource — it handles everything.
    ///
    /// Features:
    /// - Auto-disables playback for local player (don't hear yourself)
    /// - Sets participant PUID from NetworkObject owner
    /// - Applies 3D audio settings to AudioSource
    /// - Registers with EOSVoiceZoneManager for position tracking
    /// - Animated speaking indicator sphere above the player
    /// </summary>
    [RequireComponent(typeof(AudioSource))]
    public class NetworkVoicePlayer : NetworkBehaviour
    {
        [Header("3D Audio Settings")]
        [Tooltip("Set to 1.0 for full 3D spatial audio, 0.0 for 2D.")]
        [Range(0f, 1f)]
        [SerializeField] private float _spatialBlend = 1f;

        [Tooltip("Doppler effect intensity. 0 = off, 1 = normal, higher = exaggerated.")]
        [Range(0f, 5f)]
        [SerializeField] private float _dopplerLevel = 1f;

        [Tooltip("Minimum distance for 3D audio rolloff.")]
        [SerializeField] private float _minDistance = 1f;

        [Tooltip("Maximum distance for 3D audio rolloff.")]
        [SerializeField] private float _maxDistance = 50f;

        [Tooltip("How the volume attenuates over distance.")]
        [SerializeField] private AudioRolloffMode _rolloffMode = AudioRolloffMode.Logarithmic;

        [Header("Voice Effects")]
        [Tooltip("Enable reverb effect on voice.")]
        [SerializeField] private bool _enableReverb = true;

        [Tooltip("Reverb environment preset.")]
        [SerializeField] private AudioReverbPreset _reverbPreset = AudioReverbPreset.Cave;

        [Tooltip("Enable pitch shifting effect.")]
        [SerializeField] private bool _enablePitchShift = false;

        [Tooltip("Pitch shift factor. 0.5 = octave down, 1.0 = normal, 2.0 = octave up.")]
        [Range(0.5f, 2f)]
        [SerializeField] private float _pitchShift = 1f;

        [Header("Speaking Indicator")]
        [Tooltip("Show a floating sphere above the player when speaking.")]
        [SerializeField] private bool _showSpeakingIndicator = true;

        [Tooltip("Color of the speaking indicator sphere.")]
        [SerializeField] private Color _indicatorColor = new Color(0.2f, 0.6f, 1f, 0.8f);

        [Tooltip("Height offset above the player's position for the indicator.")]
        [SerializeField] private float _indicatorHeight = 2.2f;

        [Tooltip("Base size of the indicator sphere.")]
        [SerializeField] private float _indicatorBaseSize = 0.15f;

        [Tooltip("Max size when speaking actively.")]
        [SerializeField] private float _indicatorMaxSize = 0.35f;

        [Header("Debug")]
        [SerializeField] private bool _showDebugLogs = false;

        private EOSVoicePlayer _voicePlayer;
        private AudioSource _audioSource;
        private string _ownerPuid;

        // Speaking indicator
        private GameObject _indicatorObj;
        private MeshRenderer _indicatorRenderer;
        private float _indicatorScale;
        private float _indicatorTargetScale;
        private float _indicatorVelocity; // for SmoothDamp
        private bool _wasSpeaking;
        private float _speakingTimer;
        private static Material _indicatorMaterial;

        /// <summary>The PUID of the player this voice player is rendering audio for.</summary>
        public string OwnerPuid => _ownerPuid;

        /// <summary>The underlying EOSVoicePlayer component.</summary>
        public EOSVoicePlayer VoicePlayer => _voicePlayer;

        /// <summary>The AudioSource used for playback.</summary>
        public AudioSource AudioSource => _audioSource;

        /// <summary>Whether the speaking indicator is currently visible.</summary>
        public bool IsSpeakingIndicatorVisible => _indicatorObj != null && _indicatorObj.activeSelf;

        #region Properties

        /// <summary>Spatial blend (0 = 2D, 1 = 3D). Changes are applied immediately.</summary>
        public float SpatialBlend
        {
            get => _spatialBlend;
            set { _spatialBlend = value; if (_audioSource != null) _audioSource.spatialBlend = value; }
        }

        /// <summary>Doppler effect level.</summary>
        public float DopplerLevel
        {
            get => _dopplerLevel;
            set { _dopplerLevel = value; if (_audioSource != null) _audioSource.dopplerLevel = value; }
        }

        /// <summary>Enable/disable reverb effect.</summary>
        public bool EnableReverb
        {
            get => _enableReverb;
            set { _enableReverb = value; if (_voicePlayer != null) _voicePlayer.EnableReverb = value; }
        }

        /// <summary>Reverb environment preset.</summary>
        public AudioReverbPreset ReverbPreset
        {
            get => _reverbPreset;
            set { _reverbPreset = value; if (_voicePlayer != null) _voicePlayer.ReverbPreset = value; }
        }

        /// <summary>Enable/disable pitch shifting.</summary>
        public bool EnablePitchShift
        {
            get => _enablePitchShift;
            set { _enablePitchShift = value; if (_voicePlayer != null) _voicePlayer.EnablePitchShift = value; }
        }

        /// <summary>Pitch shift factor (0.5 = octave down, 1.0 = normal, 2.0 = octave up).</summary>
        public float PitchShift
        {
            get => _pitchShift;
            set { _pitchShift = value; if (_voicePlayer != null) _voicePlayer.PitchShift = value; }
        }

        #endregion

        protected override void Awake()
        {
            base.Awake();

            _audioSource = GetComponent<AudioSource>();
            _audioSource.spatialBlend = _spatialBlend;
            _audioSource.dopplerLevel = _dopplerLevel;
            _audioSource.minDistance = _minDistance;
            _audioSource.maxDistance = _maxDistance;
            _audioSource.rolloffMode = _rolloffMode;
            _audioSource.playOnAwake = false;
            _audioSource.loop = true;

            _voicePlayer = GetComponent<EOSVoicePlayer>();
            if (_voicePlayer == null)
                _voicePlayer = gameObject.AddComponent<EOSVoicePlayer>();

            ApplyVoiceEffectsSettings();
        }

        /// <summary>
        /// Apply voice effect settings to the underlying EOSVoicePlayer.
        /// Call this if you change settings at runtime.
        /// </summary>
        public void ApplyVoiceEffectsSettings()
        {
            if (_voicePlayer == null) return;

            _voicePlayer.EnableReverb = _enableReverb;
            _voicePlayer.ReverbPreset = _reverbPreset;
            _voicePlayer.EnablePitchShift = _enablePitchShift;
            _voicePlayer.PitchShift = _pitchShift;
        }

        public override void OnNetworkSpawn()
        {
            if (IsOwner)
            {
                if (_showDebugLogs)
                    Debug.Log("[NetworkVoicePlayer] Local player — disabling voice playback");

                _voicePlayer.enabled = false;
                _audioSource.enabled = false;

                // Register local player with zone manager
                var zoneManager = EOSVoiceZoneManager.Instance;
                if (zoneManager != null)
                    zoneManager.RegisterLocalPlayer(transform);

                return;
            }

            // Remote player — set up voice playback
            SetupVoiceForOwner();

            // Create speaking indicator
            if (_showSpeakingIndicator)
                CreateSpeakingIndicator();
        }

        public override void OnNetworkDespawn()
        {
            if (!string.IsNullOrEmpty(_ownerPuid))
            {
                var zoneManager = EOSVoiceZoneManager.Instance;
                if (zoneManager != null)
                    zoneManager.UnregisterPlayer(_ownerPuid);
            }

            DestroySpeakingIndicator();
        }

        private void SetupVoiceForOwner()
        {
            if (OwnerId == null)
            {
                if (_showDebugLogs)
                    Debug.Log("[NetworkVoicePlayer] No owner yet");
                return;
            }

            _ownerPuid = OwnerId.ToString();
            _voicePlayer.SetParticipant(_ownerPuid);

            // Register with zone manager
            var zoneManager = EOSVoiceZoneManager.Instance;
            if (zoneManager != null)
                zoneManager.RegisterPlayer(_ownerPuid, transform);

            if (_showDebugLogs)
                EOSDebugLogger.Log(DebugCategory.VoicePlayer, "NetworkVoicePlayer",
                    $"Set voice for owner: {_ownerPuid}");
        }

        /// <summary>
        /// Manually set the participant PUID (if you have it from another source).
        /// </summary>
        public void SetParticipantPuid(string puid)
        {
            _ownerPuid = puid;
            if (_voicePlayer != null)
                _voicePlayer.SetParticipant(puid);
        }

        #region Speaking Indicator

        private void CreateSpeakingIndicator()
        {
            _indicatorObj = GameObject.CreatePrimitive(PrimitiveType.Sphere);
            _indicatorObj.name = "VoiceIndicator";
            _indicatorObj.transform.SetParent(transform);
            _indicatorObj.transform.localPosition = Vector3.up * _indicatorHeight;
            _indicatorObj.transform.localScale = Vector3.zero;

            // Remove collider — purely visual
            var col = _indicatorObj.GetComponent<Collider>();
            if (col != null) Object.Destroy(col);

            // Set up material
            _indicatorRenderer = _indicatorObj.GetComponent<MeshRenderer>();
            if (_indicatorMaterial == null)
            {
                // Create a shared unlit transparent material
                var shader = Shader.Find("Unlit/Color");
                if (shader == null) shader = Shader.Find("UI/Default");
                _indicatorMaterial = new Material(shader);
            }
            _indicatorRenderer.sharedMaterial = _indicatorMaterial;
            _indicatorRenderer.material.color = _indicatorColor;

            _indicatorObj.SetActive(false);
            _indicatorScale = 0f;
            _indicatorTargetScale = 0f;
        }

        private void DestroySpeakingIndicator()
        {
            if (_indicatorObj != null)
            {
                Destroy(_indicatorObj);
                _indicatorObj = null;
            }
        }

        private void Update()
        {
            if (!_showSpeakingIndicator || _indicatorObj == null) return;
            if (string.IsNullOrEmpty(_ownerPuid)) return;

            // Check if this participant is speaking
            bool isSpeaking = false;
            var voiceManager = EOSVoiceManager.Instance;
            if (voiceManager != null && voiceManager.IsConnected)
            {
                isSpeaking = voiceManager.IsSpeaking(_ownerPuid);
            }

            // Track speaking state
            if (isSpeaking)
            {
                _speakingTimer = 0.5f; // Keep visible for 0.5s after speech stops
                _indicatorTargetScale = _indicatorMaxSize;

                if (!_wasSpeaking)
                {
                    _indicatorObj.SetActive(true);
                    _wasSpeaking = true;
                }
            }
            else
            {
                _speakingTimer -= Time.deltaTime;
                if (_speakingTimer <= 0f)
                {
                    _indicatorTargetScale = 0f;

                    if (_wasSpeaking && _indicatorScale < 0.01f)
                    {
                        _indicatorObj.SetActive(false);
                        _wasSpeaking = false;
                    }
                }
            }

            // Animate scale with bounce-ease (SmoothDamp gives a nice springy feel)
            _indicatorScale = Mathf.SmoothDamp(_indicatorScale, _indicatorTargetScale,
                ref _indicatorVelocity, 0.15f);

            // Add a subtle pulse when actively speaking
            float pulse = 1f;
            if (isSpeaking && _indicatorScale > 0.01f)
            {
                pulse = 1f + Mathf.Sin(Time.time * 8f) * 0.15f;
            }

            float finalScale = _indicatorScale * pulse;
            _indicatorObj.transform.localScale = Vector3.one * finalScale;
            _indicatorObj.transform.localPosition = Vector3.up * _indicatorHeight;

            // Fade color alpha based on scale
            if (_indicatorRenderer != null)
            {
                var c = _indicatorColor;
                c.a = Mathf.Clamp01(_indicatorScale / _indicatorBaseSize) * _indicatorColor.a;
                _indicatorRenderer.material.color = c;
            }
        }

        #endregion
    }
}
