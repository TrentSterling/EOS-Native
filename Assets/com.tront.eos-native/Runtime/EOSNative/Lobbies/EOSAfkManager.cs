using System;
using System.Collections.Generic;
using System.Linq;
using EOSNative.Lobbies;
using EOSNative.Logging;
using UnityEngine;

namespace EOSNative.Lobbies
{
    /// <summary>
    /// Tracks player activity and optionally removes AFK players from the lobby.
    /// Framework-agnostic: uses Input + Time for local detection, lobby events for member tracking.
    /// Only the lobby host evaluates AFK states and can remove players.
    /// </summary>
    public class EOSAfkManager : MonoBehaviour
    {
        #region Singleton

        private static EOSAfkManager _instance;

        public static EOSAfkManager Instance
        {
            get
            {
                if (_instance == null)
                {
#if UNITY_2023_1_OR_NEWER
                    _instance = FindFirstObjectByType<EOSAfkManager>();
#else
                    _instance = FindObjectOfType<EOSAfkManager>();
#endif
                    if (_instance == null)
                    {
                        var go = new GameObject("[EOSAfkManager]");
                        _instance = go.AddComponent<EOSAfkManager>();
                        DontDestroyOnLoad(go);
                    }
                }
                return _instance;
            }
        }

        #endregion

        #region Settings

        [Header("AFK Detection")]
        [SerializeField, Tooltip("Enable AFK detection and tracking.")]
        private bool _enabled = true;

        [SerializeField, Tooltip("Time in seconds before a player is considered AFK.")]
        [Range(30f, 600f)]
        private float _afkThreshold = 120f;

        [SerializeField, Tooltip("Time in seconds after AFK before auto-kick (0 = no auto-kick).")]
        [Range(0f, 120f)]
        private float _autoKickDelay = 30f;

        [SerializeField, Tooltip("Show warning toast when player goes AFK.")]
        private bool _showAfkWarnings = true;

        [Header("Activity Detection")]
        [SerializeField, Tooltip("Track mouse/keyboard input as activity.")]
        private bool _trackInput = true;

        #endregion

        #region Public Properties

        public bool Enabled { get => _enabled; set => _enabled = value; }
        public float AfkThreshold { get => _afkThreshold; set => _afkThreshold = Mathf.Max(30f, value); }
        public float AutoKickDelay { get => _autoKickDelay; set => _autoKickDelay = Mathf.Max(0f, value); }
        public bool ShowAfkWarnings { get => _showAfkWarnings; set => _showAfkWarnings = value; }

        /// <summary>Number of currently tracked players.</summary>
        public int TrackedPlayerCount => _playerStates.Count;

        /// <summary>Number of currently AFK players.</summary>
        public int AfkPlayerCount => _playerStates.Count(kvp => kvp.Value.IsAfk);

        #endregion

        #region Events

        /// <summary>Fired when a player becomes AFK. (puid)</summary>
        public event Action<string> OnPlayerAfk;

        /// <summary>Fired when a player returns from AFK. (puid)</summary>
        public event Action<string> OnPlayerReturned;

        /// <summary>Fired when a player is about to be auto-kicked. (puid, secondsRemaining)</summary>
        public event Action<string, float> OnAutoKickWarning;

        /// <summary>Fired when a player is auto-kicked for AFK. (puid)</summary>
        public event Action<string> OnPlayerAutoKicked;

        #endregion

        #region Private State

        private class PlayerAfkState
        {
            public float LastActivityTime;
            public bool IsAfk;
            public float AfkStartTime;
            public bool WarningSent;
        }

        private readonly Dictionary<string, PlayerAfkState> _playerStates = new();
        private float _lastInputTime;

        #endregion

        #region Unity Lifecycle

        private void Awake()
        {
            if (_instance != null && _instance != this)
            {
                Destroy(gameObject);
                return;
            }
            _instance = this;
        }

        private void Start()
        {
            _lastInputTime = Time.time;

            var lobbyManager = EOSLobbyManager.Instance;
            if (lobbyManager != null)
            {
                lobbyManager.OnMemberJoined += OnMemberJoined;
                lobbyManager.OnMemberLeft += OnMemberLeft;
                lobbyManager.OnLobbyLeft += OnLobbyLeft;
            }
        }

        private void OnDestroy()
        {
            var lobbyManager = EOSLobbyManager.Instance;
            if (lobbyManager != null)
            {
                lobbyManager.OnMemberJoined -= OnMemberJoined;
                lobbyManager.OnMemberLeft -= OnMemberLeft;
                lobbyManager.OnLobbyLeft -= OnLobbyLeft;
            }

            if (_instance == this)
                _instance = null;
        }

        private void Update()
        {
            if (!_enabled) return;

            // Track local input activity
            if (_trackInput)
            {
                if (Input.anyKey || Input.GetAxis("Mouse X") != 0 || Input.GetAxis("Mouse Y") != 0)
                {
                    _lastInputTime = Time.time;
                    string localPuid = EOSManager.Instance?.LocalProductUserId?.ToString();
                    if (!string.IsNullOrEmpty(localPuid))
                        RecordActivity(localPuid);
                }
            }

            // Only the lobby host evaluates AFK states
            var lobby = EOSLobbyManager.Instance;
            if (lobby == null || !lobby.IsInLobby || !lobby.IsOwner) return;

            UpdateAfkStates();
        }

        #endregion

        #region Public API

        /// <summary>
        /// Record activity for the local player.
        /// Call this when the player performs any meaningful action.
        /// </summary>
        public void RecordLocalActivity()
        {
            _lastInputTime = Time.time;
            string localPuid = EOSManager.Instance?.LocalProductUserId?.ToString();
            if (!string.IsNullOrEmpty(localPuid))
                RecordActivity(localPuid);
        }

        /// <summary>
        /// Record activity for a specific player by PUID.
        /// </summary>
        public void RecordActivity(string puid)
        {
            if (string.IsNullOrEmpty(puid)) return;

            if (_playerStates.TryGetValue(puid, out var state))
            {
                bool wasAfk = state.IsAfk;
                state.LastActivityTime = Time.time;
                state.IsAfk = false;
                state.WarningSent = false;

                if (wasAfk)
                {
                    OnPlayerReturned?.Invoke(puid);

                    if (_showAfkWarnings)
                    {
                        string name = GetPlayerName(puid);
                        EOSDebugLogger.Log(DebugCategory.LobbyManager, "EOSAfkManager", $"{name} returned from AFK");
                    }
                }
            }
        }

        /// <summary>Check if a player is currently AFK.</summary>
        public bool IsPlayerAfk(string puid)
        {
            return _playerStates.TryGetValue(puid, out var state) && state.IsAfk;
        }

        /// <summary>Get how long a player has been AFK (0 if not AFK).</summary>
        public float GetAfkDuration(string puid)
        {
            if (_playerStates.TryGetValue(puid, out var state) && state.IsAfk)
                return Time.time - state.AfkStartTime;
            return 0f;
        }

        /// <summary>Get time until player will be auto-kicked (-1 if no auto-kick).</summary>
        public float GetTimeUntilKick(string puid)
        {
            if (_autoKickDelay <= 0) return -1f;
            if (_playerStates.TryGetValue(puid, out var state) && state.IsAfk)
            {
                float afkDuration = Time.time - state.AfkStartTime;
                return Mathf.Max(0f, _autoKickDelay - afkDuration);
            }
            return -1f;
        }

        /// <summary>Get all currently AFK player PUIDs.</summary>
        public List<string> GetAfkPlayers()
        {
            var result = new List<string>();
            foreach (var kvp in _playerStates)
            {
                if (kvp.Value.IsAfk)
                    result.Add(kvp.Key);
            }
            return result;
        }

        /// <summary>
        /// Manually remove a player from the lobby for AFK (host only).
        /// </summary>
        public void KickPlayer(string puid, string reason = "AFK")
        {
            var lobby = EOSLobbyManager.Instance;
            if (lobby == null || !lobby.IsOwner) return;

            string name = GetPlayerName(puid);
            EOSDebugLogger.LogWarning(DebugCategory.LobbyManager, "EOSAfkManager", $"Kicking {name}: {reason}");

            // Remove from lobby via EOS
            _ = lobby.KickMemberAsync(puid);
            _playerStates.Remove(puid);
        }

        #endregion

        #region Private Methods

        private void OnMemberJoined(LobbyMemberData member)
        {
            _playerStates[member.Puid] = new PlayerAfkState
            {
                LastActivityTime = Time.time,
                IsAfk = false,
                AfkStartTime = 0f,
                WarningSent = false
            };
        }

        private void OnMemberLeft(string puid)
        {
            _playerStates.Remove(puid);
        }

        private void OnLobbyLeft()
        {
            _playerStates.Clear();
        }

        private void UpdateAfkStates()
        {
            float currentTime = Time.time;
            var toKick = new List<string>();

            foreach (var kvp in _playerStates)
            {
                string puid = kvp.Key;
                var state = kvp.Value;

                // Skip self
                string localPuid = EOSManager.Instance?.LocalProductUserId?.ToString();
                if (puid == localPuid) continue;

                float idleTime = currentTime - state.LastActivityTime;

                if (!state.IsAfk && idleTime >= _afkThreshold)
                {
                    state.IsAfk = true;
                    state.AfkStartTime = currentTime;
                    state.WarningSent = false;

                    OnPlayerAfk?.Invoke(puid);

                    if (_showAfkWarnings)
                    {
                        string name = GetPlayerName(puid);
                        string msg = _autoKickDelay > 0
                            ? $"{name} is AFK (auto-kick in {_autoKickDelay:0}s)"
                            : $"{name} is AFK";
                        EOSDebugLogger.LogWarning(DebugCategory.LobbyManager, "EOSAfkManager", msg);
                    }
                }
                else if (state.IsAfk && _autoKickDelay > 0)
                {
                    float afkDuration = currentTime - state.AfkStartTime;
                    float timeUntilKick = _autoKickDelay - afkDuration;

                    if (!state.WarningSent && timeUntilKick <= 10f && timeUntilKick > 0f)
                    {
                        state.WarningSent = true;
                        OnAutoKickWarning?.Invoke(puid, timeUntilKick);
                    }

                    if (afkDuration >= _autoKickDelay)
                    {
                        toKick.Add(puid);
                    }
                }
            }

            foreach (string puid in toKick)
            {
                OnPlayerAutoKicked?.Invoke(puid);
                KickPlayer(puid, "AFK too long");
            }
        }

        private string GetPlayerName(string puid)
        {
            if (string.IsNullOrEmpty(puid)) return "Player";

            var registry = EOSPlayerRegistry.Instance;
            if (registry != null)
            {
                string name = registry.GetPlayerName(puid);
                if (!string.IsNullOrEmpty(name)) return name;
            }

            return puid.Length > 8 ? puid.Substring(0, 8) + "..." : puid;
        }

        #endregion
    }
}
