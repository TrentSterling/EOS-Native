using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using EOSNative.Lobbies;
using EOSNative.Logging;
using UnityEngine;

namespace EOSNative.Lobbies
{
    /// <summary>
    /// Join-in-progress and backfill management for lobbies.
    /// Tracks game phases, controls joinability, and requests backfill when slots are available.
    /// Framework-agnostic: works with any networking solution via lobby attributes.
    /// </summary>
    public class EOSBackfillManager : MonoBehaviour
    {
        #region Singleton

        private static EOSBackfillManager _instance;

        public static EOSBackfillManager Instance
        {
            get
            {
                if (_instance == null)
                {
#if UNITY_2023_1_OR_NEWER
                    _instance = FindFirstObjectByType<EOSBackfillManager>();
#else
                    _instance = FindObjectOfType<EOSBackfillManager>();
#endif
                    if (_instance == null)
                    {
                        var go = new GameObject("[EOSBackfillManager]");
                        _instance = go.AddComponent<EOSBackfillManager>();
                        DontDestroyOnLoad(go);
                    }
                }
                return _instance;
            }
        }

        #endregion

        #region Enums

        public enum GamePhase
        {
            Lobby, Loading, Warmup, InProgress, Overtime, PostGame, Custom
        }

        public enum BackfillStatus
        {
            None, Requesting, Filling, Complete, Cancelled, Failed
        }

        public enum JoinInProgressResult
        {
            Allowed, Denied_Phase, Denied_Full, Denied_Timeout, Denied_Locked, Denied_Banned, Denied_Other
        }

        #endregion

        #region Data Types

        [Serializable]
        public class BackfillRequest
        {
            public string RequestId;
            public int SlotsNeeded;
            public int SlotsFilled;
            public float RequestedTime;
            public float TimeoutSeconds;
            public int PreferredTeam = -1;
            public string GameMode;
            public string Region;
            public BackfillStatus Status = BackfillStatus.None;

            public bool IsExpired => Time.time - RequestedTime >= TimeoutSeconds;
            public float TimeRemaining => Mathf.Max(0f, TimeoutSeconds - (Time.time - RequestedTime));
            public bool IsComplete => SlotsFilled >= SlotsNeeded;
        }

        [Serializable]
        public class JoinInProgressData
        {
            public string PlayerPuid;
            public string PlayerName;
            public float JoinedTime;
            public int AssignedTeam;
            public bool IsBackfill;
            public string BackfillRequestId;
            public GamePhase JoinedDuringPhase;
        }

        #endregion

        #region Settings

        [Header("Join-in-Progress")]
        [SerializeField] private bool _allowJoinInProgress = true;
        [SerializeField, Range(0f, 300f)] private float _jipTimeout = 60f;
        [SerializeField, Range(0f, 300f)] private float _jipMinTimeRemaining = 30f;
        [SerializeField] private bool _lockAfterStart = false;

        [Header("Backfill")]
        [SerializeField] private bool _autoBackfill = false;
        [SerializeField, Range(0f, 30f)] private float _backfillDelay = 5f;
        [SerializeField, Range(10f, 300f)] private float _backfillTimeout = 60f;
        [SerializeField] private bool _balanceTeams = true;

        [Header("Game")]
        [SerializeField] private int _maxPlayers = 8;

        #endregion

        #region Public Properties

        public bool AllowJoinInProgress { get => _allowJoinInProgress; set => _allowJoinInProgress = value; }
        public float JipTimeout { get => _jipTimeout; set => _jipTimeout = value; }
        public bool LockAfterStart { get => _lockAfterStart; set => _lockAfterStart = value; }
        public bool AutoBackfill { get => _autoBackfill; set => _autoBackfill = value; }
        public bool BalanceTeams { get => _balanceTeams; set => _balanceTeams = value; }
        public GamePhase CurrentPhase => _currentPhase;
        public bool IsGameLocked => _gameLocked;
        public bool IsBackfillActive => _activeBackfill != null && _activeBackfill.Status == BackfillStatus.Filling;
        public BackfillRequest ActiveBackfill => _activeBackfill;
        public IReadOnlyList<JoinInProgressData> JipHistory => _jipHistory;
        public int CurrentPlayers => _currentPlayers;
        public int MaxPlayers => _maxPlayers;
        public int AvailableSlots => Mathf.Max(0, _maxPlayers - _currentPlayers);

        #endregion

        #region Events

        public event Action<GamePhase, GamePhase> OnPhaseChanged;
        public event Action<JoinInProgressData> OnPlayerJoinedInProgress;
        public event Action<string, JoinInProgressResult> OnJoinDenied;
        public event Action<BackfillRequest> OnBackfillStarted;
        public event Action<BackfillRequest> OnBackfillComplete;
        public event Action<BackfillRequest> OnBackfillFailed;
        public event Action<BackfillRequest, string> OnBackfillPlayerJoined;
        public event Action OnGameLocked;
        public event Action OnGameUnlocked;

        #endregion

        #region Private State

        private GamePhase _currentPhase = GamePhase.Lobby;
        private float _gameStartTime;
        private bool _gameLocked;
        private BackfillRequest _activeBackfill;
        private readonly List<JoinInProgressData> _jipHistory = new();
        private readonly Dictionary<int, int> _teamPlayerCounts = new();
        private int _currentPlayers;
        private float _estimatedTimeRemaining = -1f;
        private readonly HashSet<GamePhase> _allowedPhases = new() { GamePhase.Lobby, GamePhase.Warmup, GamePhase.InProgress };

        #endregion

        #region Unity Lifecycle

        private void Awake()
        {
            if (_instance != null && _instance != this) { Destroy(gameObject); return; }
            _instance = this;
        }

        private void Start()
        {
            var lobby = EOSLobbyManager.Instance;
            if (lobby != null)
            {
                lobby.OnMemberJoined += HandleMemberJoined;
                lobby.OnMemberLeft += HandleMemberLeft;
            }
        }

        private void OnDestroy()
        {
            var lobby = EOSLobbyManager.Instance;
            if (lobby != null)
            {
                lobby.OnMemberJoined -= HandleMemberJoined;
                lobby.OnMemberLeft -= HandleMemberLeft;
            }

            if (_instance == this) _instance = null;
        }

        private void Update()
        {
            if (_activeBackfill != null && _activeBackfill.Status == BackfillStatus.Filling)
            {
                if (_activeBackfill.IsExpired)
                {
                    _activeBackfill.Status = BackfillStatus.Failed;
                    OnBackfillFailed?.Invoke(_activeBackfill);
                    _activeBackfill = null;
                    UpdateLobbyBackfillStatus(false, 0);
                }
                else if (_activeBackfill.IsComplete)
                {
                    _activeBackfill.Status = BackfillStatus.Complete;
                    OnBackfillComplete?.Invoke(_activeBackfill);
                    _activeBackfill = null;
                    UpdateLobbyBackfillStatus(false, 0);
                }
            }
        }

        #endregion

        #region Public API - Phase Management

        /// <summary>Set the current game phase.</summary>
        public void SetPhase(GamePhase phase)
        {
            if (phase == _currentPhase) return;

            var oldPhase = _currentPhase;
            _currentPhase = phase;

            if (phase == GamePhase.InProgress)
            {
                _gameStartTime = Time.time;
                if (_lockAfterStart) LockGame();
            }

            OnPhaseChanged?.Invoke(oldPhase, phase);

            // Update lobby attribute
            var lobby = EOSLobbyManager.Instance;
            if (lobby != null && lobby.IsOwner && lobby.IsInLobby)
                _ = lobby.SetLobbyAttributeAsync(lobby.CurrentLobby.LobbyId, "GAME_PHASE", phase.ToString());

            UpdateLobbyJoinability(!_gameLocked && _allowedPhases.Contains(phase));
        }

        /// <summary>Set estimated time remaining for the match.</summary>
        public void SetEstimatedTimeRemaining(float seconds) => _estimatedTimeRemaining = seconds;

        /// <summary>Lock the game (prevent new joins).</summary>
        public void LockGame()
        {
            if (_gameLocked) return;
            _gameLocked = true;
            OnGameLocked?.Invoke();
            UpdateLobbyJoinability(false);
        }

        /// <summary>Unlock the game (allow new joins).</summary>
        public void UnlockGame()
        {
            if (!_gameLocked) return;
            _gameLocked = false;
            OnGameUnlocked?.Invoke();
            UpdateLobbyJoinability(_allowedPhases.Contains(_currentPhase));
        }

        /// <summary>Set which phases allow JIP.</summary>
        public void SetAllowedPhases(params GamePhase[] phases)
        {
            _allowedPhases.Clear();
            foreach (var p in phases) _allowedPhases.Add(p);
        }

        public void AddAllowedPhase(GamePhase phase) => _allowedPhases.Add(phase);
        public void RemoveAllowedPhase(GamePhase phase) => _allowedPhases.Remove(phase);

        #endregion

        #region Public API - Join-in-Progress

        /// <summary>Check if a player can join in progress.</summary>
        public JoinInProgressResult CanJoinInProgress(string puid = null)
        {
            if (_gameLocked) return JoinInProgressResult.Denied_Locked;
            if (!_allowJoinInProgress) return JoinInProgressResult.Denied_Phase;
            if (!_allowedPhases.Contains(_currentPhase)) return JoinInProgressResult.Denied_Phase;
            if (_currentPlayers >= _maxPlayers) return JoinInProgressResult.Denied_Full;
            if (_estimatedTimeRemaining >= 0 && _estimatedTimeRemaining < _jipMinTimeRemaining) return JoinInProgressResult.Denied_Timeout;

            // Check if player is banned
            if (!string.IsNullOrEmpty(puid))
            {
                var registry = EOSPlayerRegistry.Instance;
                if (registry != null && registry.IsBlocked(puid)) return JoinInProgressResult.Denied_Banned;
            }

            return JoinInProgressResult.Allowed;
        }

        /// <summary>Process a player joining in progress.</summary>
        public JoinInProgressData ProcessJoinInProgress(string puid, string playerName, bool isBackfill = false, string backfillRequestId = null)
        {
            int team = _balanceTeams ? GetTeamForNewPlayer() : 0;

            var jipData = new JoinInProgressData
            {
                PlayerPuid = puid,
                PlayerName = playerName,
                JoinedTime = Time.time,
                AssignedTeam = team,
                IsBackfill = isBackfill,
                BackfillRequestId = backfillRequestId,
                JoinedDuringPhase = _currentPhase
            };

            _jipHistory.Add(jipData);
            _currentPlayers++;

            if (isBackfill && _activeBackfill != null)
            {
                _activeBackfill.SlotsFilled++;
                OnBackfillPlayerJoined?.Invoke(_activeBackfill, puid);
            }

            OnPlayerJoinedInProgress?.Invoke(jipData);
            return jipData;
        }

        /// <summary>Get the best team for a new player (lowest count).</summary>
        public int GetTeamForNewPlayer()
        {
            if (_teamPlayerCounts.Count == 0) return 0;

            int bestTeam = 0;
            int lowestCount = int.MaxValue;
            foreach (var kvp in _teamPlayerCounts)
            {
                if (kvp.Value < lowestCount)
                {
                    lowestCount = kvp.Value;
                    bestTeam = kvp.Key;
                }
            }
            return bestTeam;
        }

        /// <summary>Set team player counts for balancing.</summary>
        public void SetTeamCounts(Dictionary<int, int> counts) { _teamPlayerCounts.Clear(); foreach (var kvp in counts) _teamPlayerCounts[kvp.Key] = kvp.Value; }
        public void SetTeamCount(int team, int count) => _teamPlayerCounts[team] = count;

        #endregion

        #region Public API - Backfill

        /// <summary>Request backfill for a number of slots.</summary>
        public BackfillRequest RequestBackfill(int slots, int preferredTeam = -1, string gameMode = null, string region = null)
        {
            if (slots <= 0 || _gameLocked) return null;

            _activeBackfill = new BackfillRequest
            {
                RequestId = Guid.NewGuid().ToString("N").Substring(0, 8),
                SlotsNeeded = slots,
                SlotsFilled = 0,
                RequestedTime = Time.time,
                TimeoutSeconds = _backfillTimeout,
                PreferredTeam = preferredTeam,
                GameMode = gameMode,
                Region = region,
                Status = BackfillStatus.Filling
            };

            OnBackfillStarted?.Invoke(_activeBackfill);
            UpdateLobbyBackfillStatus(true, slots);

            EOSDebugLogger.Log(DebugCategory.LobbyManager, "EOSBackfillManager",
                $"Backfill requested: {slots} slots");

            return _activeBackfill;
        }

        /// <summary>Request backfill for all available slots.</summary>
        public BackfillRequest RequestBackfillForAllSlots()
        {
            return RequestBackfill(AvailableSlots);
        }

        /// <summary>Cancel active backfill request.</summary>
        public void CancelBackfill()
        {
            if (_activeBackfill == null) return;
            _activeBackfill.Status = BackfillStatus.Cancelled;
            _activeBackfill = null;
            UpdateLobbyBackfillStatus(false, 0);
        }

        /// <summary>Handle a player leaving (triggers auto-backfill if enabled).</summary>
        public void OnPlayerLeft(string puid, int team = -1)
        {
            _currentPlayers = Mathf.Max(0, _currentPlayers - 1);

            if (team >= 0 && _teamPlayerCounts.ContainsKey(team))
                _teamPlayerCounts[team] = Mathf.Max(0, _teamPlayerCounts[team] - 1);

            if (_autoBackfill && AvailableSlots > 0 && !_gameLocked && _currentPhase == GamePhase.InProgress)
            {
                _ = DelayedBackfillRequest(team);
            }
        }

        #endregion

        #region Public API - Configuration

        public void SetMaxPlayers(int max) => _maxPlayers = Mathf.Max(1, max);
        public void SetCurrentPlayers(int count) => _currentPlayers = Mathf.Max(0, count);

        /// <summary>Reset for a new game.</summary>
        public void ResetForNewGame()
        {
            _currentPhase = GamePhase.Lobby;
            _gameLocked = false;
            _activeBackfill = null;
            _jipHistory.Clear();
            _estimatedTimeRemaining = -1f;
        }

        #endregion

        #region Public Static

        public static string GetPhaseName(GamePhase phase) => phase switch
        {
            GamePhase.Lobby => "Lobby",
            GamePhase.Loading => "Loading",
            GamePhase.Warmup => "Warmup",
            GamePhase.InProgress => "In Progress",
            GamePhase.Overtime => "Overtime",
            GamePhase.PostGame => "Post-Game",
            GamePhase.Custom => "Custom",
            _ => phase.ToString()
        };

        public static string GetJipResultMessage(JoinInProgressResult result) => result switch
        {
            JoinInProgressResult.Allowed => "Join allowed",
            JoinInProgressResult.Denied_Phase => "Cannot join during this phase",
            JoinInProgressResult.Denied_Full => "Game is full",
            JoinInProgressResult.Denied_Timeout => "Not enough time remaining",
            JoinInProgressResult.Denied_Locked => "Game is locked",
            JoinInProgressResult.Denied_Banned => "You are blocked from this game",
            JoinInProgressResult.Denied_Other => "Cannot join",
            _ => result.ToString()
        };

        #endregion

        #region Private Methods

        private void HandleMemberJoined(LobbyMemberData member)
        {
            string puid = member.Puid;
            if (_currentPhase != GamePhase.Lobby)
            {
                var result = CanJoinInProgress(puid);
                if (result == JoinInProgressResult.Allowed)
                {
                    string name = EOSPlayerRegistry.Instance?.GetPlayerName(puid) ?? puid;
                    ProcessJoinInProgress(puid, name, _activeBackfill != null, _activeBackfill?.RequestId);
                }
                else
                {
                    OnJoinDenied?.Invoke(puid, result);
                }
            }
            else
            {
                _currentPlayers++;
            }
        }

        private void HandleMemberLeft(string puid)
        {
            OnPlayerLeft(puid);
        }

        private async Task DelayedBackfillRequest(int preferredTeam)
        {
            await Task.Delay((int)(_backfillDelay * 1000));

            if (_autoBackfill && AvailableSlots > 0 && !_gameLocked && _currentPhase == GamePhase.InProgress)
            {
                if (_activeBackfill == null || _activeBackfill.Status != BackfillStatus.Filling)
                {
                    RequestBackfill(AvailableSlots, preferredTeam);
                }
            }
        }

        private void UpdateLobbyJoinability(bool allowJoin)
        {
            var lobby = EOSLobbyManager.Instance;
            if (lobby != null && lobby.IsOwner && lobby.IsInLobby)
                _ = lobby.SetLobbyAttributeAsync(lobby.CurrentLobby.LobbyId, "JOINABLE", allowJoin ? "true" : "false");
        }

        private void UpdateLobbyBackfillStatus(bool needsBackfill, int slots)
        {
            var lobby = EOSLobbyManager.Instance;
            if (lobby != null && lobby.IsOwner && lobby.IsInLobby)
                _ = lobby.SetLobbyAttributeAsync(lobby.CurrentLobby.LobbyId, "BACKFILL", needsBackfill ? $"{slots}" : "0");
        }

        #endregion
    }
}
