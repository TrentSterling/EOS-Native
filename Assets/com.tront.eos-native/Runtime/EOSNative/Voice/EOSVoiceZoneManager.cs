using System;
using System.Collections.Generic;
using EOSNative.Logging;
using EOSNative.Net;
using UnityEngine;
#if UNITY_EDITOR
using UnityEditor;
#endif

namespace EOSNative.Voice
{
    /// <summary>
    /// Voice chat zone modes.
    /// </summary>
    public enum VoiceZoneMode
    {
        /// <summary>All players can hear each other equally.</summary>
        Global,
        /// <summary>Volume scales with distance from other players.</summary>
        Proximity,
        /// <summary>Only teammates can hear each other.</summary>
        Team,
        /// <summary>Proximity within team only (team + distance).</summary>
        TeamProximity,
        /// <summary>Custom zones defined by triggers/areas.</summary>
        Custom
    }

    /// <summary>
    /// Manages voice chat zones for proximity-based, team-based, or global voice.
    /// Works alongside EOSVoiceManager to dynamically adjust per-participant volumes.
    ///
    /// Modes:
    /// - Global: everyone hears everyone at full volume
    /// - Proximity: distance-based falloff (configurable exponent, fade start/end)
    /// - Team: same team = full volume, cross-team = muted or reduced
    /// - TeamProximity: team filter + distance falloff combined
    /// - Custom: zone-name matching (via trigger volumes or API)
    /// </summary>
    public class EOSVoiceZoneManager : MonoBehaviour
    {
        #region Singleton

        private static EOSVoiceZoneManager _instance;
        private static bool _shuttingDown;

        /// <summary>
        /// The singleton instance. Auto-creates if not found.
        /// </summary>
        public static EOSVoiceZoneManager Instance
        {
            get
            {
                if (_shuttingDown) return _instance;

                if (_instance == null)
                {
                    _instance = FindAnyObjectByType<EOSVoiceZoneManager>();

                    if (_instance == null)
                    {
                        // Don't auto-create until voice is actually connected
                        if (EOSVoiceManager.Instance == null || !EOSVoiceManager.Instance.IsConnected)
                            return null;

                        var go = new GameObject("EOSVoiceZoneManager");
                        if (EOSManager.Instance != null)
                            go.transform.SetParent(EOSManager.Instance.transform);
                        else
                            DontDestroyOnLoad(go);
                        _instance = go.AddComponent<EOSVoiceZoneManager>();
                        EOSDebugLogger.Log(DebugCategory.VoiceManager, "EOSVoiceZoneManager",
                            "Auto-created singleton instance");
                    }
                }
                return _instance;
            }
        }

        #endregion

        #region Events

        /// <summary>Fired when zone mode changes.</summary>
        public event Action<VoiceZoneMode> OnZoneModeChanged;

        /// <summary>Fired when a player's effective voice volume changes significantly.</summary>
        public event Action<string, float> OnPlayerVolumeChanged;

        /// <summary>Fired when a player enters hearing range (proximity mode).</summary>
        public event Action<string> OnPlayerEnteredRange;

        /// <summary>Fired when a player exits hearing range (proximity mode).</summary>
        public event Action<string> OnPlayerExitedRange;

        #endregion

        #region Inspector Settings

        [Header("Zone Mode")]
        [Tooltip("Current voice zone mode")]
        [SerializeField] private VoiceZoneMode _zoneMode = VoiceZoneMode.Global;

        [Header("Proximity Settings")]
        [Tooltip("Maximum distance to hear other players (units)")]
        [SerializeField] private float _maxHearingDistance = 30f;

        [Tooltip("Distance at which volume starts to fade")]
        [SerializeField] private float _fadeStartDistance = 10f;

        [Tooltip("Minimum volume at max distance (0-100)")]
        [SerializeField] private float _minVolume = 0f;

        [Tooltip("Maximum volume when close (0-100)")]
        [SerializeField] private float _maxVolume = 100f;

        [Tooltip("Volume falloff curve (1 = linear, 2 = quadratic, 0.5 = sqrt)")]
        [SerializeField] private float _falloffExponent = 1f;

        [Header("Team Settings")]
        [Tooltip("Local player's team (set this or use SetTeam())")]
        [SerializeField] private int _localTeam = 0;

        [Tooltip("Allow hearing enemies in team mode (at reduced volume)")]
        [SerializeField] private bool _allowCrossTeamAudio = false;

        [Tooltip("Volume multiplier for cross-team audio (0-1)")]
        [SerializeField, Range(0f, 1f)] private float _crossTeamVolumeMultiplier = 0.25f;

        [Header("Volume Ducking")]
        [Tooltip("Auto-reduce incoming voice volume when local player is speaking")]
        [SerializeField] private bool _enableVolumeDucking = false;

        [Tooltip("Volume multiplier applied when local player is speaking (0-1)")]
        [SerializeField, Range(0f, 1f)] private float _duckingMultiplier = 0.5f;

        [Tooltip("How fast ducking fades in/out (units per second)")]
        [SerializeField] private float _duckingSpeed = 5f;

        [Header("Audio Occlusion")]
        [Tooltip("Enable raycast-based wall muting (reduce volume when walls block line of sight)")]
        [SerializeField] private bool _enableAudioOcclusion = false;

        [Tooltip("Layer mask for occlusion raycasts (what counts as a wall)")]
        [SerializeField] private LayerMask _occlusionLayerMask = ~0; // Default: everything

        [Tooltip("Volume multiplier when fully occluded (0 = silent, 1 = no reduction)")]
        [SerializeField, Range(0f, 1f)] private float _occlusionVolumeMultiplier = 0.15f;

        [Tooltip("Height offset for raycast origin/target (approximate head height)")]
        [SerializeField] private float _occlusionRayHeight = 1.5f;

        [Header("Voice Priority (Bandwidth Management)")]
        [Tooltip("Maximum simultaneous voice streams. 0 = unlimited. When exceeded, lowest-priority participants are muted.")]
        [SerializeField] private int _maxActiveVoiceStreams = 0;

        [Header("Spatial Grid (100+ Players)")]
        [Tooltip("Use a spatial hash grid for proximity lookups instead of brute-force O(N^2). Enables efficient proximity voice for 100+ players.")]
        [SerializeField] private bool _useSpatialGrid = false;

        [Tooltip("Grid cell size (units). Should be roughly maxHearingDistance / 2. Smaller = more cells but tighter queries.")]
        [SerializeField] private float _gridCellSize = 15f;

        [Header("Update Settings")]
        [Tooltip("How often to update volumes (seconds)")]
        [SerializeField] private float _updateInterval = 0.1f;

        [Tooltip("Only update if volume changed by this much")]
        [SerializeField] private float _volumeChangeThreshold = 2f;

        [Header("Position Source")]
        [Tooltip("Tag to identify player objects for position tracking")]
        [SerializeField] private string _playerTag = "Player";

        [Tooltip("Auto-discover players from NetworkManager.Instance.Objects")]
        [SerializeField] private bool _autoDiscoverNetworkObjects = true;

        #endregion

        #region Public Properties

        /// <summary>Current voice zone mode.</summary>
        public VoiceZoneMode ZoneMode
        {
            get => _zoneMode;
            set => SetZoneMode(value);
        }

        /// <summary>Maximum hearing distance for proximity mode.</summary>
        public float MaxHearingDistance
        {
            get => _maxHearingDistance;
            set => _maxHearingDistance = Mathf.Max(1f, value);
        }

        /// <summary>Distance at which volume starts fading.</summary>
        public float FadeStartDistance
        {
            get => _fadeStartDistance;
            set => _fadeStartDistance = Mathf.Clamp(value, 0f, _maxHearingDistance);
        }

        /// <summary>Local player's team number.</summary>
        public int LocalTeam
        {
            get => _localTeam;
            set => SetTeam(value);
        }

        /// <summary>Whether the manager is actively adjusting volumes.</summary>
        public bool IsActive => _zoneMode != VoiceZoneMode.Global && EOSVoiceManager.Instance?.IsConnected == true;

        /// <summary>Whether volume ducking is enabled.</summary>
        public bool EnableVolumeDucking
        {
            get => _enableVolumeDucking;
            set => _enableVolumeDucking = value;
        }

        /// <summary>Whether audio occlusion (raycast wall muting) is enabled.</summary>
        public bool EnableAudioOcclusion
        {
            get => _enableAudioOcclusion;
            set => _enableAudioOcclusion = value;
        }

        /// <summary>Layer mask for occlusion raycasts. Only layers in this mask block voice.</summary>
        public LayerMask OcclusionLayerMask
        {
            get => _occlusionLayerMask;
            set => _occlusionLayerMask = value;
        }

        /// <summary>Volume multiplier when a wall blocks line of sight (0 = silent, 1 = no effect).</summary>
        public float OcclusionVolumeMultiplier
        {
            get => _occlusionVolumeMultiplier;
            set => _occlusionVolumeMultiplier = Mathf.Clamp01(value);
        }

        /// <summary>
        /// Maximum simultaneous voice streams. 0 = unlimited (default).
        /// When exceeded, lowest-priority participants are muted.
        /// Speaking participants always have highest priority, then sorted by proximity.
        /// </summary>
        public int MaxActiveVoiceStreams
        {
            get => _maxActiveVoiceStreams;
            set => _maxActiveVoiceStreams = Mathf.Max(0, value);
        }

        /// <summary>Whether the spatial hash grid is enabled for proximity lookups.</summary>
        public bool UseSpatialGrid
        {
            get => _useSpatialGrid;
            set => _useSpatialGrid = value;
        }

        /// <summary>Grid cell size in world units. Defaults to maxHearingDistance / 2.</summary>
        public float GridCellSize
        {
            get => _gridCellSize;
            set => _gridCellSize = Mathf.Max(1f, value);
        }

        #endregion

        #region Private Fields

        private float _lastUpdateTime;
        private Transform _localPlayerTransform;
        private readonly Dictionary<string, Transform> _playerTransforms = new();
        private readonly Dictionary<string, float> _lastVolumes = new();
        private readonly Dictionary<string, int> _playerTeams = new();
        private readonly HashSet<string> _playersInRange = new();

        // Custom zone support
        private readonly Dictionary<string, string> _playerZones = new();
        private string _localZone = "default";

        // Volume ducking
        private float _currentDuckingFactor = 1f;

        // Occlusion tracking
        private readonly Dictionary<string, bool> _playerOccluded = new();

        // Spatial hash grid for O(N) proximity checks with 100+ players
        private readonly Dictionary<long, HashSet<string>> _gridCells = new();
        private readonly Dictionary<string, long> _puidCells = new();
        private readonly HashSet<string> _nearbyPuids = new();

        // Voice priority system
        private readonly List<(string puid, float priority)> _priorityList = new();

        #endregion

        #region Unity Lifecycle

        private void Awake()
        {
            _shuttingDown = false;

            if (_instance != null && _instance != this)
            {
                Destroy(gameObject);
                return;
            }

            _instance = this;
            if (transform.parent == null)
                DontDestroyOnLoad(gameObject);
        }

        private void OnEnable()
        {
            if (EOSVoiceManager.Instance != null)
            {
                EOSVoiceManager.Instance.OnVoiceConnectionChanged += OnVoiceConnectionChanged;
            }
        }

        private void OnDisable()
        {
            if (EOSVoiceManager.Instance != null)
            {
                EOSVoiceManager.Instance.OnVoiceConnectionChanged -= OnVoiceConnectionChanged;
            }
        }

        private void OnDestroy()
        {
            if (_instance == this)
                _instance = null;
        }

        private void OnApplicationQuit() => _shuttingDown = true;

        private void Update()
        {
            // Update ducking factor
            if (_enableVolumeDucking)
            {
                bool localSpeaking = EOSVoiceManager.Instance != null &&
                    EOSVoiceManager.Instance.IsConnected &&
                    !EOSVoiceManager.Instance.IsMuted &&
                    EOSVoiceManager.Instance.LocalMicLevel > 0.01f;

                float target = localSpeaking ? _duckingMultiplier : 1f;
                _currentDuckingFactor = Mathf.MoveTowards(_currentDuckingFactor, target, _duckingSpeed * Time.deltaTime);
            }
            else
            {
                _currentDuckingFactor = 1f;
            }

            if (!IsActive) return;

            if (Time.time - _lastUpdateTime >= _updateInterval)
            {
                _lastUpdateTime = Time.time;
                UpdateVoiceVolumes();
            }
        }

        #endregion

        #region Public API - Zone Mode

        /// <summary>
        /// Set the voice zone mode.
        /// </summary>
        public void SetZoneMode(VoiceZoneMode mode)
        {
            if (_zoneMode == mode) return;

            var oldMode = _zoneMode;
            _zoneMode = mode;

            EOSDebugLogger.Log(DebugCategory.VoiceManager, "EOSVoiceZoneManager",
                $"Zone mode changed: {oldMode} -> {mode}");

            // Reset all volumes when switching to/from global
            if (mode == VoiceZoneMode.Global || oldMode == VoiceZoneMode.Global)
            {
                ResetAllVolumes();
            }

            OnZoneModeChanged?.Invoke(mode);
        }

        /// <summary>
        /// Set the local player's team.
        /// </summary>
        public void SetTeam(int team)
        {
            if (_localTeam == team) return;

            _localTeam = team;
            EOSDebugLogger.Log(DebugCategory.VoiceManager, "EOSVoiceZoneManager",
                $"Local team set to: {team}");

            if (_zoneMode == VoiceZoneMode.Team || _zoneMode == VoiceZoneMode.TeamProximity)
            {
                UpdateVoiceVolumes();
            }
        }

        /// <summary>
        /// Set a player's team.
        /// </summary>
        public void SetPlayerTeam(string puid, int team)
        {
            _playerTeams[puid] = team;

            if (_zoneMode == VoiceZoneMode.Team || _zoneMode == VoiceZoneMode.TeamProximity)
            {
                UpdatePlayerVolume(puid);
            }
        }

        /// <summary>
        /// Get a player's team.
        /// </summary>
        public int GetPlayerTeam(string puid)
        {
            return _playerTeams.TryGetValue(puid, out int team) ? team : 0;
        }

        #endregion

        #region Public API - Position Tracking

        /// <summary>
        /// Register the local player's transform for position tracking.
        /// </summary>
        public void RegisterLocalPlayer(Transform playerTransform)
        {
            _localPlayerTransform = playerTransform;
            EOSDebugLogger.Log(DebugCategory.VoiceManager, "EOSVoiceZoneManager",
                "Local player registered");
        }

        /// <summary>
        /// Register a remote player's transform for position tracking.
        /// </summary>
        public void RegisterPlayer(string puid, Transform playerTransform)
        {
            _playerTransforms[puid] = playerTransform;
            EOSDebugLogger.Log(DebugCategory.VoiceManager, "EOSVoiceZoneManager",
                $"Player registered: {(puid.Length > 8 ? puid.Substring(0, 8) + "..." : puid)}");
        }

        /// <summary>
        /// Unregister a player from position tracking.
        /// </summary>
        public void UnregisterPlayer(string puid)
        {
            _playerTransforms.Remove(puid);
            _playerTeams.Remove(puid);
            _lastVolumes.Remove(puid);
            _playersInRange.Remove(puid);
            _playerZones.Remove(puid);
            _playerOccluded.Remove(puid);
            RemoveFromGrid(puid);
        }

        /// <summary>
        /// Clear all tracked players.
        /// </summary>
        public void ClearAllPlayers()
        {
            _playerTransforms.Clear();
            _playerTeams.Clear();
            _lastVolumes.Clear();
            _playersInRange.Clear();
            _playerZones.Clear();
            _playerOccluded.Clear();
            _localPlayerTransform = null;
            ClearGrid();
        }

        /// <summary>
        /// Auto-discover players from Layer 2 NetworkManager objects.
        /// Call this periodically or after spawns.
        /// </summary>
        public void AutoDiscoverPlayers()
        {
            if (!_autoDiscoverNetworkObjects) return;

            var networkManager = NetworkManager.Instance;
            if (networkManager == null) return;

            string localPuid = EOSManager.Instance?.LocalProductUserId?.ToString();
            if (string.IsNullOrEmpty(localPuid)) return;

            foreach (var kvp in networkManager.Objects)
            {
                var netObj = kvp.Value;
                if (netObj == null || netObj.OwnerId == null) continue;

                // Check if it's tagged as player
                bool isPlayer = !string.IsNullOrEmpty(_playerTag) && netObj.CompareTag(_playerTag);
                if (!isPlayer) continue;

                string puid = netObj.OwnerId.ToString();
                if (string.IsNullOrEmpty(puid)) continue;

                // Local player
                if (puid == localPuid)
                {
                    if (_localPlayerTransform == null)
                        _localPlayerTransform = netObj.transform;
                    continue;
                }

                // Register remote player if not already tracked
                if (!_playerTransforms.ContainsKey(puid))
                {
                    RegisterPlayer(puid, netObj.transform);
                }
            }
        }

        #endregion

        #region Public API - Custom Zones

        /// <summary>
        /// Set the local player's current zone (for Custom mode).
        /// </summary>
        public void SetLocalZone(string zoneName)
        {
            _localZone = zoneName ?? "default";
            if (_zoneMode == VoiceZoneMode.Custom)
            {
                UpdateVoiceVolumes();
            }
        }

        /// <summary>
        /// Set a player's current zone (for Custom mode).
        /// </summary>
        public void SetPlayerZone(string puid, string zoneName)
        {
            _playerZones[puid] = zoneName ?? "default";
            if (_zoneMode == VoiceZoneMode.Custom)
            {
                UpdatePlayerVolume(puid);
            }
        }

        /// <summary>
        /// Get a player's current zone.
        /// </summary>
        public string GetPlayerZone(string puid)
        {
            return _playerZones.TryGetValue(puid, out string zone) ? zone : "default";
        }

        /// <summary>
        /// Get the local player's current zone.
        /// </summary>
        public string LocalZone => _localZone;

        #endregion

        #region Public API - Volume Queries

        /// <summary>
        /// Get the current effective volume for a player.
        /// </summary>
        public float GetPlayerVolume(string puid)
        {
            return _lastVolumes.TryGetValue(puid, out float vol) ? vol : _maxVolume;
        }

        /// <summary>
        /// Check if a player is currently in hearing range.
        /// </summary>
        public bool IsPlayerInRange(string puid)
        {
            return _playersInRange.Contains(puid);
        }

        /// <summary>
        /// Get all players currently in hearing range.
        /// </summary>
        public List<string> GetPlayersInRange()
        {
            return new List<string>(_playersInRange);
        }

        /// <summary>
        /// Get distance to a player (or -1 if unknown).
        /// </summary>
        public float GetDistanceToPlayer(string puid)
        {
            if (_localPlayerTransform == null) return -1f;
            if (!_playerTransforms.TryGetValue(puid, out var playerTransform)) return -1f;
            if (playerTransform == null) return -1f;

            return Vector3.Distance(_localPlayerTransform.position, playerTransform.position);
        }

        #endregion

        #region Public API - Configuration

        /// <summary>
        /// Configure proximity settings.
        /// </summary>
        public void ConfigureProximity(float maxDistance, float fadeStart, float minVol = 0f, float maxVol = 100f)
        {
            _maxHearingDistance = Mathf.Max(1f, maxDistance);
            _fadeStartDistance = Mathf.Clamp(fadeStart, 0f, _maxHearingDistance);
            _minVolume = Mathf.Clamp(minVol, 0f, 100f);
            _maxVolume = Mathf.Clamp(maxVol, 0f, 100f);
        }

        /// <summary>
        /// Configure team settings.
        /// </summary>
        public void ConfigureTeam(bool allowCrossTeam, float crossTeamMultiplier = 0.25f)
        {
            _allowCrossTeamAudio = allowCrossTeam;
            _crossTeamVolumeMultiplier = Mathf.Clamp01(crossTeamMultiplier);
        }

        #endregion

        #region Volume Calculation

        private void UpdateVoiceVolumes()
        {
            var voiceManager = EOSVoiceManager.Instance;
            if (voiceManager == null || !voiceManager.IsConnected) return;

            // Auto-discover if using Layer 2 network objects
            if (_autoDiscoverNetworkObjects)
            {
                AutoDiscoverPlayers();
            }

            // With spatial grid: only process nearby players for proximity modes
            if (_useSpatialGrid && (_zoneMode == VoiceZoneMode.Proximity || _zoneMode == VoiceZoneMode.TeamProximity))
            {
                UpdateSpatialGrid();
                GetNearbyPuids(_localPlayerTransform != null ? _localPlayerTransform.position : Vector3.zero);

                // Process nearby players at calculated volume
                foreach (var puid in _nearbyPuids)
                {
                    UpdatePlayerVolume(puid);
                }

                // Mute far-away players that are NOT in nearby cells
                foreach (var puid in voiceManager.GetAllParticipants())
                {
                    if (_nearbyPuids.Contains(puid)) continue;

                    float lastVolume = _lastVolumes.TryGetValue(puid, out float lv) ? lv : -999f;
                    if (Mathf.Abs(_minVolume - lastVolume) >= _volumeChangeThreshold)
                    {
                        voiceManager.SetParticipantVolume(puid, _minVolume);
                        _lastVolumes[puid] = _minVolume;

                        if (_playersInRange.Remove(puid))
                            OnPlayerExitedRange?.Invoke(puid);
                    }
                }
            }
            else
            {
                // Brute-force: update every participant (original O(N) behavior)
                foreach (var puid in voiceManager.GetAllParticipants())
                {
                    UpdatePlayerVolume(puid);
                }
            }

            // Voice priority: mute lowest-priority participants when over limit
            if (_maxActiveVoiceStreams > 0)
            {
                EnforcePriorityLimit(voiceManager);
            }
        }

        private void UpdatePlayerVolume(string puid)
        {
            var voiceManager = EOSVoiceManager.Instance;
            if (voiceManager == null) return;

            float newVolume = CalculateVolume(puid);

            // Apply audio occlusion (wall muting)
            if (_enableAudioOcclusion && newVolume > 0f)
            {
                bool occluded = CheckOcclusion(puid);
                _playerOccluded[puid] = occluded;
                if (occluded)
                    newVolume *= _occlusionVolumeMultiplier;
            }

            // Apply ducking
            if (_enableVolumeDucking)
            {
                newVolume *= _currentDuckingFactor;
            }

            // Check if volume changed significantly
            float lastVolume = _lastVolumes.TryGetValue(puid, out float lv) ? lv : -999f;
            if (Mathf.Abs(newVolume - lastVolume) < _volumeChangeThreshold) return;

            // Update EOS volume
            voiceManager.SetParticipantVolume(puid, newVolume);
            _lastVolumes[puid] = newVolume;

            // Track in-range state for proximity modes
            bool wasInRange = _playersInRange.Contains(puid);
            bool isInRange = newVolume > _minVolume + 0.1f;

            if (isInRange && !wasInRange)
            {
                _playersInRange.Add(puid);
                OnPlayerEnteredRange?.Invoke(puid);
            }
            else if (!isInRange && wasInRange)
            {
                _playersInRange.Remove(puid);
                OnPlayerExitedRange?.Invoke(puid);
            }

            // Fire volume changed event
            if (Mathf.Abs(newVolume - lastVolume) > 5f)
            {
                OnPlayerVolumeChanged?.Invoke(puid, newVolume);
            }
        }

        private float CalculateVolume(string puid)
        {
            switch (_zoneMode)
            {
                case VoiceZoneMode.Global:
                    return _maxVolume;

                case VoiceZoneMode.Proximity:
                    return CalculateProximityVolume(puid);

                case VoiceZoneMode.Team:
                    return CalculateTeamVolume(puid);

                case VoiceZoneMode.TeamProximity:
                    float teamVol = CalculateTeamVolume(puid);
                    if (teamVol <= 0) return 0;
                    return CalculateProximityVolume(puid) * (teamVol / _maxVolume);

                case VoiceZoneMode.Custom:
                    return CalculateCustomZoneVolume(puid);

                default:
                    return _maxVolume;
            }
        }

        private float CalculateProximityVolume(string puid)
        {
            if (_localPlayerTransform == null) return _maxVolume;

            if (!_playerTransforms.TryGetValue(puid, out var playerTransform) || playerTransform == null)
            {
                return _maxVolume; // Can't determine position, use max
            }

            float distance = Vector3.Distance(_localPlayerTransform.position, playerTransform.position);

            // Beyond max distance = silent
            if (distance >= _maxHearingDistance)
            {
                return _minVolume;
            }

            // Within fade start = full volume
            if (distance <= _fadeStartDistance)
            {
                return _maxVolume;
            }

            // Calculate falloff
            float fadeRange = _maxHearingDistance - _fadeStartDistance;
            float fadeDistance = distance - _fadeStartDistance;
            float t = fadeDistance / fadeRange;

            // Apply falloff curve
            t = Mathf.Pow(t, _falloffExponent);

            // Lerp between max and min
            return Mathf.Lerp(_maxVolume, _minVolume, t);
        }

        private float CalculateTeamVolume(string puid)
        {
            int playerTeam = GetPlayerTeam(puid);

            // Same team = full volume
            if (playerTeam == _localTeam)
            {
                return _maxVolume;
            }

            // Different team
            if (_allowCrossTeamAudio)
            {
                return _maxVolume * _crossTeamVolumeMultiplier;
            }

            return 0f; // Muted
        }

        private float CalculateCustomZoneVolume(string puid)
        {
            string playerZone = GetPlayerZone(puid);

            // Same zone = full volume
            if (playerZone == _localZone)
            {
                return _maxVolume;
            }

            // Different zone = muted
            return 0f;
        }

        /// <summary>
        /// Check if a player is occluded by walls (raycast from local player to target).
        /// Returns true if a wall on the occlusion layer mask blocks line of sight.
        /// </summary>
        private bool CheckOcclusion(string puid)
        {
            if (_localPlayerTransform == null) return false;
            if (!_playerTransforms.TryGetValue(puid, out var playerTransform) || playerTransform == null)
                return false;

            Vector3 headOffset = new Vector3(0f, _occlusionRayHeight, 0f);
            Vector3 from = _localPlayerTransform.position + headOffset;
            Vector3 to = playerTransform.position + headOffset;
            Vector3 direction = to - from;
            float distance = direction.magnitude;

            if (distance < 0.5f) return false; // Too close to occlude

            return Physics.Raycast(from, direction, distance, _occlusionLayerMask, QueryTriggerInteraction.Ignore);
        }

        /// <summary>Check if a player is currently occluded (wall between local player and target).</summary>
        public bool IsPlayerOccluded(string puid)
        {
            return _playerOccluded.TryGetValue(puid, out bool occluded) && occluded;
        }

        #endregion

        #region Spatial Hash Grid

        /// <summary>
        /// Update all player positions in the spatial hash grid.
        /// </summary>
        private void UpdateSpatialGrid()
        {
            float cellSize = _gridCellSize > 0 ? _gridCellSize : _maxHearingDistance / 2f;

            foreach (var kvp in _playerTransforms)
            {
                if (kvp.Value == null) continue;

                long newCell = PositionToGridCell(kvp.Value.position, cellSize);

                if (_puidCells.TryGetValue(kvp.Key, out long oldCell))
                {
                    if (oldCell == newCell) continue;

                    // Remove from old cell
                    if (_gridCells.TryGetValue(oldCell, out var oldSet))
                    {
                        oldSet.Remove(kvp.Key);
                        if (oldSet.Count == 0) _gridCells.Remove(oldCell);
                    }
                }

                // Add to new cell
                if (!_gridCells.TryGetValue(newCell, out var set))
                {
                    set = new HashSet<string>();
                    _gridCells[newCell] = set;
                }
                set.Add(kvp.Key);
                _puidCells[kvp.Key] = newCell;
            }
        }

        /// <summary>
        /// Query the spatial grid for all PUIDs in the local player's cell + 8 neighbors.
        /// </summary>
        private void GetNearbyPuids(Vector3 localPos)
        {
            _nearbyPuids.Clear();
            float cellSize = _gridCellSize > 0 ? _gridCellSize : _maxHearingDistance / 2f;

            int cx = Mathf.FloorToInt(localPos.x / cellSize);
            int cz = Mathf.FloorToInt(localPos.z / cellSize);

            for (int dx = -1; dx <= 1; dx++)
            {
                for (int dz = -1; dz <= 1; dz++)
                {
                    long key = ((long)(cx + dx) << 32) | (uint)(cz + dz);
                    if (_gridCells.TryGetValue(key, out var set))
                    {
                        foreach (var puid in set)
                            _nearbyPuids.Add(puid);
                    }
                }
            }
        }

        private static long PositionToGridCell(Vector3 pos, float cellSize)
        {
            int cx = Mathf.FloorToInt(pos.x / cellSize);
            int cz = Mathf.FloorToInt(pos.z / cellSize);
            return ((long)cx << 32) | (uint)cz;
        }

        /// <summary>Remove a PUID from the spatial grid.</summary>
        private void RemoveFromGrid(string puid)
        {
            if (_puidCells.TryGetValue(puid, out long cell))
            {
                _puidCells.Remove(puid);
                if (_gridCells.TryGetValue(cell, out var set))
                {
                    set.Remove(puid);
                    if (set.Count == 0) _gridCells.Remove(cell);
                }
            }
        }

        /// <summary>Clear all spatial grid data.</summary>
        private void ClearGrid()
        {
            _gridCells.Clear();
            _puidCells.Clear();
            _nearbyPuids.Clear();
        }

        #endregion

        #region Voice Priority

        /// <summary>
        /// Enforce voice stream limit by muting lowest-priority participants.
        /// Priority scoring: speaking (+1000), then inverse distance (closer = higher).
        /// Participants already at min volume are excluded from counting.
        /// </summary>
        private void EnforcePriorityLimit(EOSVoiceManager voiceManager)
        {
            _priorityList.Clear();

            foreach (var puid in voiceManager.GetAllParticipants())
            {
                float vol = _lastVolumes.TryGetValue(puid, out float v) ? v : 0f;
                if (vol <= _minVolume + 0.1f) continue; // already muted — skip

                float priority = 0f;

                // Speaking = highest priority
                if (voiceManager.IsSpeaking(puid))
                    priority += 1000f;

                // Closer = higher priority (inverse distance)
                float dist = GetDistanceToPlayer(puid);
                if (dist >= 0f)
                    priority += Mathf.Max(0f, _maxHearingDistance - dist);

                // Same team = slight boost
                if ((_zoneMode == VoiceZoneMode.Team || _zoneMode == VoiceZoneMode.TeamProximity) &&
                    _playerTeams.TryGetValue(puid, out int team) && team == _localTeam)
                    priority += 100f;

                _priorityList.Add((puid, priority));
            }

            // If under limit, nothing to do
            if (_priorityList.Count <= _maxActiveVoiceStreams) return;

            // Sort descending by priority
            _priorityList.Sort((a, b) => b.priority.CompareTo(a.priority));

            // Mute everyone beyond the limit
            for (int i = _maxActiveVoiceStreams; i < _priorityList.Count; i++)
            {
                string puid = _priorityList[i].puid;
                voiceManager.SetParticipantVolume(puid, _minVolume);
                _lastVolumes[puid] = _minVolume;

                if (_playersInRange.Remove(puid))
                    OnPlayerExitedRange?.Invoke(puid);
            }
        }

        /// <summary>Get the current priority score of a participant (for debugging). Returns -1 if not scored.</summary>
        public float GetPlayerPriority(string puid)
        {
            for (int i = 0; i < _priorityList.Count; i++)
            {
                if (_priorityList[i].puid == puid)
                    return _priorityList[i].priority;
            }
            return -1f;
        }

        #endregion

        #region Volume Reset

        private void ResetAllVolumes()
        {
            var voiceManager = EOSVoiceManager.Instance;
            if (voiceManager == null) return;

            foreach (var puid in voiceManager.GetAllParticipants())
            {
                voiceManager.SetParticipantVolume(puid, _maxVolume);
            }

            _lastVolumes.Clear();
            _playersInRange.Clear();
        }

        #endregion

        #region Event Handlers

        private void OnVoiceConnectionChanged(bool connected)
        {
            if (!connected)
            {
                _lastVolumes.Clear();
                _playersInRange.Clear();
            }
        }

        #endregion
    }

#if UNITY_EDITOR
    [CustomEditor(typeof(EOSVoiceZoneManager))]
    public class EOSVoiceZoneManagerEditor : Editor
    {
        public override void OnInspectorGUI()
        {
            DrawDefaultInspector();

            var manager = (EOSVoiceZoneManager)target;

            EditorGUILayout.Space(10);
            EditorGUILayout.LabelField("Runtime Status", EditorStyles.boldLabel);

            using (new EditorGUI.DisabledGroupScope(true))
            {
                EditorGUILayout.EnumPopup("Zone Mode", manager.ZoneMode);
                EditorGUILayout.Toggle("Active", manager.IsActive);
                EditorGUILayout.IntField("Local Team", manager.LocalTeam);
            }

            if (Application.isPlaying && manager.IsActive)
            {
                EditorGUILayout.Space(5);
                if (manager.UseSpatialGrid)
                    EditorGUILayout.LabelField("Spatial Grid: ON", EditorStyles.miniBoldLabel);
                if (manager.MaxActiveVoiceStreams > 0)
                    EditorGUILayout.LabelField($"Priority Limit: {manager.MaxActiveVoiceStreams}", EditorStyles.miniBoldLabel);
                var inRange = manager.GetPlayersInRange();
                EditorGUILayout.LabelField($"Players in Range: {inRange.Count}");

                if (inRange.Count > 0)
                {
                    EditorGUI.indentLevel++;
                    foreach (var puid in inRange)
                    {
                        float vol = manager.GetPlayerVolume(puid);
                        float dist = manager.GetDistanceToPlayer(puid);
                        bool occluded = manager.IsPlayerOccluded(puid);
                        string shortPuid = puid.Length > 12 ? puid.Substring(0, 8) + "..." : puid;
                        string occStr = occluded ? " [WALL]" : "";
                        EditorGUILayout.LabelField($"{shortPuid}: Vol={vol:0}%, Dist={dist:0.0}m{occStr}");
                    }
                    EditorGUI.indentLevel--;
                }

                EditorGUILayout.Space(5);
                EditorGUILayout.LabelField("Quick Actions", EditorStyles.boldLabel);

                EditorGUILayout.BeginHorizontal();
                if (GUILayout.Button("Global")) manager.SetZoneMode(VoiceZoneMode.Global);
                if (GUILayout.Button("Proximity")) manager.SetZoneMode(VoiceZoneMode.Proximity);
                if (GUILayout.Button("Team")) manager.SetZoneMode(VoiceZoneMode.Team);
                EditorGUILayout.EndHorizontal();

                EditorUtility.SetDirty(target);
            }
        }
    }
#endif
}
