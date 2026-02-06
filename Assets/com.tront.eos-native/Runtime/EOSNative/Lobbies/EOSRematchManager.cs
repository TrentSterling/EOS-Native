using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using EOSNative.Lobbies;
using EOSNative.Logging;
using UnityEngine;

namespace EOSNative.Lobbies
{
    /// <summary>
    /// Post-match rematch voting system using lobby attributes for coordination.
    /// Supports vote-based, host-forced, and auto-offer rematch modes.
    /// </summary>
    public class EOSRematchManager : MonoBehaviour
    {
        #region Singleton

        private static EOSRematchManager _instance;

        public static EOSRematchManager Instance
        {
            get
            {
                if (_instance == null)
                {
#if UNITY_2023_1_OR_NEWER
                    _instance = FindFirstObjectByType<EOSRematchManager>();
#else
                    _instance = FindObjectOfType<EOSRematchManager>();
#endif
                    if (_instance == null)
                    {
                        var go = new GameObject("[EOSRematchManager]");
                        _instance = go.AddComponent<EOSRematchManager>();
                        DontDestroyOnLoad(go);
                    }
                }
                return _instance;
            }
        }

        #endregion

        #region Enums

        public enum RematchState { None, Proposed, Accepted, Declined, Starting, Cancelled }
        public enum RematchInitiator { Vote, Host, Auto }

        #endregion

        #region Data Types

        [Serializable]
        public class RematchData
        {
            public string MatchId;
            public string ProposedBy;
            public RematchInitiator Initiator;
            public RematchState State = RematchState.None;
            public float ProposedTime;
            public float TimeoutSeconds;
            public bool SwapTeams;
            public bool KeepSettings = true;
            public HashSet<string> VotesYes = new();
            public HashSet<string> VotesNo = new();
            public List<string> EligiblePlayers = new();
            public int RequiredVotes;
            public string NewLobbyCode;

            public int TotalVotes => VotesYes.Count + VotesNo.Count;
            public int YesCount => VotesYes.Count;
            public int NoCount => VotesNo.Count;
            public bool IsExpired => Time.time - ProposedTime >= TimeoutSeconds;
            public float TimeRemaining => Mathf.Max(0f, TimeoutSeconds - (Time.time - ProposedTime));
            public bool HasEnoughYes => VotesYes.Count >= RequiredVotes;
            public bool HasEnoughNo => VotesNo.Count > EligiblePlayers.Count - RequiredVotes;
            public float YesPercent => EligiblePlayers.Count > 0 ? VotesYes.Count * 100f / EligiblePlayers.Count : 0f;
        }

        #endregion

        #region Settings

        [Header("Rematch Settings")]
        [SerializeField] private bool _allowRematch = true;
        [SerializeField, Range(15f, 120f)] private float _rematchTimeout = 30f;
        [SerializeField, Range(0.3f, 1f)] private float _rematchVoteThreshold = 0.5f;
        [SerializeField] private bool _hostCanForceRematch = true;
        [SerializeField] private bool _autoOfferRematch = false;
        [SerializeField, Range(1f, 10f)] private float _autoOfferDelay = 3f;
        [SerializeField] private bool _defaultSwapTeams = false;
        [SerializeField] private bool _defaultKeepSettings = true;
        [SerializeField, Range(1, 20)] private int _maxConsecutiveRematches = 5;

        #endregion

        #region Public Properties

        public bool AllowRematch { get => _allowRematch; set => _allowRematch = value; }
        public float RematchTimeout { get => _rematchTimeout; set => _rematchTimeout = Mathf.Clamp(value, 15f, 120f); }
        public float RematchVoteThreshold { get => _rematchVoteThreshold; set => _rematchVoteThreshold = Mathf.Clamp01(value); }
        public bool HostCanForceRematch { get => _hostCanForceRematch; set => _hostCanForceRematch = value; }
        public bool AutoOfferRematch { get => _autoOfferRematch; set => _autoOfferRematch = value; }
        public bool IsRematchActive => _currentRematch != null && _currentRematch.State == RematchState.Proposed;
        public RematchData CurrentRematch => _currentRematch;
        public RematchState CurrentState => _currentRematch?.State ?? RematchState.None;
        public int ConsecutiveRematches => _consecutiveRematches;
        public bool IsMatchEnded => _isMatchEnded;

        #endregion

        #region Events

        public event Action<RematchData> OnRematchProposed;
        public event Action<RematchData, string, bool> OnVoteCast;
        public event Action<RematchData> OnRematchAccepted;
        public event Action<RematchData> OnRematchDeclined;
        public event Action<RematchData> OnRematchCancelled;
        public event Action<RematchData> OnRematchStarting;
        public event Action<float> OnRematchTimerTick;
        public event Action<string> OnRematchLobbyCreated;

        #endregion

        #region Private State

        private RematchData _currentRematch;
        private int _consecutiveRematches;
        private string _lastMatchId;
        private bool _isMatchEnded;

        #endregion

        #region Unity Lifecycle

        private void Awake()
        {
            if (_instance != null && _instance != this) { Destroy(gameObject); return; }
            _instance = this;
        }

        private void OnDestroy()
        {
            if (_instance == this) _instance = null;
        }

        private void Update()
        {
            if (_currentRematch == null || _currentRematch.State != RematchState.Proposed) return;

            // Timer tick
            float remaining = _currentRematch.TimeRemaining;
            OnRematchTimerTick?.Invoke(remaining);

            if (_currentRematch.IsExpired)
            {
                // Time expired - check if enough yes votes
                if (_currentRematch.HasEnoughYes)
                    AcceptRematch();
                else
                    DeclineRematch();
            }
        }

        #endregion

        #region Public API - Match Lifecycle

        /// <summary>Signal that the current match has ended.</summary>
        public void OnMatchEnded(string matchId = null)
        {
            _isMatchEnded = true;
            _lastMatchId = matchId ?? Guid.NewGuid().ToString("N").Substring(0, 8);

            if (_autoOfferRematch && _allowRematch && CanProposeRematch())
            {
                _ = AutoOfferRematchDelayed();
            }
        }

        /// <summary>Signal that a rematch has started (resets state).</summary>
        public void OnRematchStarted()
        {
            _isMatchEnded = false;
            _consecutiveRematches++;
        }

        /// <summary>Reset all rematch state.</summary>
        public void Reset()
        {
            _currentRematch = null;
            _consecutiveRematches = 0;
            _isMatchEnded = false;
            _lastMatchId = null;
        }

        #endregion

        #region Public API - Proposing

        /// <summary>Propose a rematch to all players in lobby.</summary>
        public bool ProposeRematch(string proposerPuid = null, RematchInitiator initiator = RematchInitiator.Vote)
        {
            if (!CanProposeRematch()) return false;

            string localPuid = proposerPuid ?? EOSManager.Instance?.LocalProductUserId?.ToString();
            if (string.IsNullOrEmpty(localPuid)) return false;

            var eligible = GetEligiblePlayers();
            int required = Mathf.CeilToInt(eligible.Count * _rematchVoteThreshold);

            _currentRematch = new RematchData
            {
                MatchId = _lastMatchId,
                ProposedBy = localPuid,
                Initiator = initiator,
                State = RematchState.Proposed,
                ProposedTime = Time.time,
                TimeoutSeconds = _rematchTimeout,
                SwapTeams = _defaultSwapTeams,
                KeepSettings = _defaultKeepSettings,
                EligiblePlayers = eligible,
                RequiredVotes = Mathf.Max(1, required)
            };

            // Proposer auto-votes yes
            _currentRematch.VotesYes.Add(localPuid);

            OnRematchProposed?.Invoke(_currentRematch);

            // Broadcast via lobby attribute
            var lobby = EOSLobbyManager.Instance;
            if (lobby != null && lobby.IsInLobby)
                _ = lobby.SetMemberAttributeAsync("REMATCH", $"PROPOSE:{localPuid}");

            CheckVoteResult();
            return true;
        }

        /// <summary>Host force-starts a rematch (skips voting).</summary>
        public bool ForceRematch()
        {
            if (!_hostCanForceRematch) return false;

            var lobby = EOSLobbyManager.Instance;
            if (lobby == null || !lobby.IsOwner) return false;

            string localPuid = EOSManager.Instance?.LocalProductUserId?.ToString();
            ProposeRematch(localPuid, RematchInitiator.Host);

            if (_currentRematch != null)
            {
                AcceptRematch();
                return true;
            }
            return false;
        }

        /// <summary>Check if a rematch can be proposed.</summary>
        public bool CanProposeRematch()
        {
            if (!_allowRematch || !_isMatchEnded) return false;
            if (_consecutiveRematches >= _maxConsecutiveRematches) return false;
            if (_currentRematch != null && _currentRematch.State == RematchState.Proposed) return false;

            var lobby = EOSLobbyManager.Instance;
            return lobby != null && lobby.IsInLobby;
        }

        #endregion

        #region Public API - Voting

        /// <summary>Cast a rematch vote.</summary>
        public bool VoteRematch(bool voteYes, string voterPuid = null)
        {
            if (_currentRematch == null || _currentRematch.State != RematchState.Proposed) return false;

            string puid = voterPuid ?? EOSManager.Instance?.LocalProductUserId?.ToString();
            if (string.IsNullOrEmpty(puid)) return false;

            if (!_currentRematch.EligiblePlayers.Contains(puid)) return false;

            // Remove previous vote if any
            _currentRematch.VotesYes.Remove(puid);
            _currentRematch.VotesNo.Remove(puid);

            if (voteYes)
                _currentRematch.VotesYes.Add(puid);
            else
                _currentRematch.VotesNo.Add(puid);

            OnVoteCast?.Invoke(_currentRematch, puid, voteYes);

            // Broadcast via attribute
            var lobby = EOSLobbyManager.Instance;
            if (lobby != null && lobby.IsInLobby)
                _ = lobby.SetMemberAttributeAsync("REMATCH", $"VOTE:{(voteYes ? "Y" : "N")}");

            CheckVoteResult();
            return true;
        }

        /// <summary>Has the local player voted?</summary>
        public bool HasVoted()
        {
            string puid = EOSManager.Instance?.LocalProductUserId?.ToString();
            if (_currentRematch == null || string.IsNullOrEmpty(puid)) return false;
            return _currentRematch.VotesYes.Contains(puid) || _currentRematch.VotesNo.Contains(puid);
        }

        /// <summary>Get the local player's vote (null if not voted).</summary>
        public bool? GetMyVote()
        {
            string puid = EOSManager.Instance?.LocalProductUserId?.ToString();
            if (_currentRematch == null || string.IsNullOrEmpty(puid)) return null;
            if (_currentRematch.VotesYes.Contains(puid)) return true;
            if (_currentRematch.VotesNo.Contains(puid)) return false;
            return null;
        }

        #endregion

        #region Public API - Options

        public void SetSwapTeams(bool swap) { if (_currentRematch != null) _currentRematch.SwapTeams = swap; }
        public void SetKeepSettings(bool keep) { if (_currentRematch != null) _currentRematch.KeepSettings = keep; }

        /// <summary>Cancel the current rematch proposal (host or proposer only).</summary>
        public bool CancelRematch()
        {
            if (_currentRematch == null || _currentRematch.State != RematchState.Proposed) return false;

            string localPuid = EOSManager.Instance?.LocalProductUserId?.ToString();
            var lobby = EOSLobbyManager.Instance;
            bool isHost = lobby != null && lobby.IsOwner;

            if (localPuid != _currentRematch.ProposedBy && !isHost) return false;

            _currentRematch.State = RematchState.Cancelled;
            OnRematchCancelled?.Invoke(_currentRematch);
            _currentRematch = null;
            return true;
        }

        #endregion

        #region Public Static

        public static string GetStateName(RematchState state) => state switch
        {
            RematchState.None => "None",
            RematchState.Proposed => "Vote in Progress",
            RematchState.Accepted => "Accepted",
            RematchState.Declined => "Declined",
            RematchState.Starting => "Starting...",
            RematchState.Cancelled => "Cancelled",
            _ => state.ToString()
        };

        #endregion

        #region Private Methods

        private void CheckVoteResult()
        {
            if (_currentRematch == null || _currentRematch.State != RematchState.Proposed) return;

            if (_currentRematch.HasEnoughYes)
                AcceptRematch();
            else if (_currentRematch.HasEnoughNo)
                DeclineRematch();
        }

        private void AcceptRematch()
        {
            if (_currentRematch == null) return;
            _currentRematch.State = RematchState.Accepted;
            OnRematchAccepted?.Invoke(_currentRematch);
            StartRematch();
        }

        private void DeclineRematch()
        {
            if (_currentRematch == null) return;
            _currentRematch.State = RematchState.Declined;
            OnRematchDeclined?.Invoke(_currentRematch);
            _currentRematch = null;
        }

        private async void StartRematch()
        {
            if (_currentRematch == null) return;
            _currentRematch.State = RematchState.Starting;
            OnRematchStarting?.Invoke(_currentRematch);

            // The lobby host can reset lobby state for the rematch
            var lobby = EOSLobbyManager.Instance;
            if (lobby != null && lobby.IsOwner)
            {
                string lobbyCode = lobby.CurrentLobby.JoinCode;
                _currentRematch.NewLobbyCode = lobbyCode;
                OnRematchLobbyCreated?.Invoke(lobbyCode);

                EOSDebugLogger.Log(DebugCategory.LobbyManager, "EOSRematchManager",
                    $"Rematch starting in lobby {lobbyCode}");
            }

            await Task.Delay(1000); // Brief delay for UI transition
            OnRematchStarted();
        }

        private async Task AutoOfferRematchDelayed()
        {
            await Task.Delay((int)(_autoOfferDelay * 1000));
            if (_isMatchEnded && _currentRematch == null && CanProposeRematch())
            {
                ProposeRematch(null, RematchInitiator.Auto);
            }
        }

        private List<string> GetEligiblePlayers()
        {
            var result = new List<string>();
            var lobby = EOSLobbyManager.Instance;
            if (lobby == null || !lobby.IsInLobby) return result;

            // Get all lobby members via registry
            var registry = EOSPlayerRegistry.Instance;
            if (registry != null)
            {
                foreach (var (puid, _, _) in registry.GetRecentPlayers(1))
                {
                    result.Add(puid);
                }
            }

            // Ensure local player is included
            string localPuid = EOSManager.Instance?.LocalProductUserId?.ToString();
            if (!string.IsNullOrEmpty(localPuid) && !result.Contains(localPuid))
                result.Add(localPuid);

            return result;
        }

        #endregion
    }
}
