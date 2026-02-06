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
    /// Map/mode voting system using lobby attributes for vote broadcast.
    /// Supports custom options, tie breakers, live results, and host controls.
    /// </summary>
    public class EOSMapVoteManager : MonoBehaviour
    {
        #region Singleton

        private static EOSMapVoteManager _instance;

        public static EOSMapVoteManager Instance
        {
            get
            {
                if (_instance == null)
                {
#if UNITY_2023_1_OR_NEWER
                    _instance = FindFirstObjectByType<EOSMapVoteManager>();
#else
                    _instance = FindObjectOfType<EOSMapVoteManager>();
#endif
                    if (_instance == null)
                    {
                        var go = new GameObject("[EOSMapVoteManager]");
                        _instance = go.AddComponent<EOSMapVoteManager>();
                        DontDestroyOnLoad(go);
                    }
                }
                return _instance;
            }
        }

        #endregion

        #region Enums

        public enum TieBreaker { Random, FirstOption, HostChoice, Revote }
        public enum VoteState { Inactive, Active, Completed }

        #endregion

        #region Data Types

        [Serializable]
        public class VoteOption
        {
            public string Id;
            public string DisplayName;
            public string Description;
            public string Category; // "map", "mode", or custom
        }

        [Serializable]
        public class MapVoteData
        {
            public string Title;
            public List<VoteOption> Options = new();
            public Dictionary<string, int> Votes = new(); // puid -> optionIndex
            public float Duration;
            public string InitiatorPuid;

            public int GetVoteCount(int optionIndex) => Votes.Count(v => v.Value == optionIndex);
            public int TotalVotes => Votes.Count;
            public int GetPlayerVote(string puid) => Votes.TryGetValue(puid, out int idx) ? idx : -1;
        }

        public class VoteResultData
        {
            public VoteOption Winner;
            public int WinnerIndex;
            public int WinnerVotes;
            public bool WasTie;
            public List<int> TiedIndices;
        }

        #endregion

        #region Settings

        [Header("Map Vote Settings")]
        [SerializeField] private bool _enabled = true;
        [SerializeField, Range(10f, 120f)] private float _voteDuration = 30f;
        [SerializeField] private TieBreaker _tieBreakerMode = TieBreaker.Random;
        [SerializeField] private bool _allowVoteChange = true;
        [SerializeField] private bool _showLiveResults = true;

        #endregion

        #region Public Properties

        public bool Enabled { get => _enabled; set => _enabled = value; }
        public float VoteDuration { get => _voteDuration; set => _voteDuration = Mathf.Clamp(value, 10f, 120f); }
        public TieBreaker TieBreakerMode { get => _tieBreakerMode; set => _tieBreakerMode = value; }
        public bool AllowVoteChange { get => _allowVoteChange; set => _allowVoteChange = value; }
        public bool ShowLiveResults { get => _showLiveResults; set => _showLiveResults = value; }
        public bool IsVoteActive => _state == VoteState.Active;
        public VoteState State => _state;
        public MapVoteData CurrentVote => _currentVote;
        public float TimeRemaining => _state == VoteState.Active ? Mathf.Max(0f, _voteEndTime - Time.time) : 0f;

        #endregion

        #region Events

        public event Action<MapVoteData> OnVoteStarted;
        public event Action<string, int> OnVoteCast;
        public event Action<int> OnTimerTick;
        public event Action<MapVoteData, VoteOption, int> OnVoteEnded;
        public event Action<List<VoteOption>> OnTieNeedsDecision;

        #endregion

        #region Private State

        private VoteState _state = VoteState.Inactive;
        private MapVoteData _currentVote;
        private float _voteEndTime;
        private float _lastTickTime;
        private List<int> _pendingTieIndices;

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
            if (_state != VoteState.Active || _currentVote == null) return;

            // Timer tick
            if (Time.time - _lastTickTime >= 1f)
            {
                _lastTickTime = Time.time;
                int remaining = Mathf.CeilToInt(TimeRemaining);
                OnTimerTick?.Invoke(remaining);
            }

            // Time expired
            if (Time.time >= _voteEndTime)
            {
                EndVote();
            }
        }

        #endregion

        #region Public API

        /// <summary>Start a vote with custom options.</summary>
        public bool StartVote(string title, List<VoteOption> options)
        {
            if (!_enabled || _state == VoteState.Active) return false;
            if (options == null || options.Count < 2) return false;

            var lobby = EOSLobbyManager.Instance;
            if (lobby == null || !lobby.IsInLobby) return false;

            string localPuid = EOSManager.Instance?.LocalProductUserId?.ToString();

            _currentVote = new MapVoteData
            {
                Title = title,
                Options = new List<VoteOption>(options),
                Duration = _voteDuration,
                InitiatorPuid = localPuid
            };

            _state = VoteState.Active;
            _voteEndTime = Time.time + _voteDuration;
            _lastTickTime = Time.time;
            _pendingTieIndices = null;

            OnVoteStarted?.Invoke(_currentVote);
            EOSDebugLogger.Log(DebugCategory.LobbyManager, "EOSMapVoteManager",
                $"Vote started: {title} ({options.Count} options)");

            return true;
        }

        /// <summary>Convenience: start a map vote with string names.</summary>
        public bool StartMapVote(string title, params string[] mapNames)
        {
            var options = mapNames.Select(n => new VoteOption { Id = n, DisplayName = n, Category = "map" }).ToList();
            return StartVote(title, options);
        }

        /// <summary>Convenience: start a mode vote with string names.</summary>
        public bool StartModeVote(string title, params string[] modeNames)
        {
            var options = modeNames.Select(n => new VoteOption { Id = n, DisplayName = n, Category = "mode" }).ToList();
            return StartVote(title, options);
        }

        /// <summary>Cast a vote for an option index.</summary>
        public bool CastVote(int optionIndex)
        {
            if (_state != VoteState.Active || _currentVote == null) return false;
            if (optionIndex < 0 || optionIndex >= _currentVote.Options.Count) return false;

            string localPuid = EOSManager.Instance?.LocalProductUserId?.ToString();
            if (string.IsNullOrEmpty(localPuid)) return false;

            if (!_allowVoteChange && _currentVote.Votes.ContainsKey(localPuid)) return false;

            _currentVote.Votes[localPuid] = optionIndex;
            OnVoteCast?.Invoke(localPuid, optionIndex);

            // Broadcast via lobby attribute
            var lobby = EOSLobbyManager.Instance;
            if (lobby != null && lobby.IsInLobby)
                _ = lobby.SetMemberAttributeAsync("MAP_VOTE", $"MV:{optionIndex}");

            return true;
        }

        /// <summary>Cast a vote by option ID.</summary>
        public bool CastVoteById(string optionId)
        {
            if (_currentVote == null) return false;
            int idx = _currentVote.Options.FindIndex(o => o.Id == optionId);
            return idx >= 0 && CastVote(idx);
        }

        /// <summary>Host resolves a tie by choosing winner index.</summary>
        public bool ResolveTie(int winnerIndex)
        {
            if (_pendingTieIndices == null || !_pendingTieIndices.Contains(winnerIndex)) return false;

            var lobby = EOSLobbyManager.Instance;
            if (lobby == null || !lobby.IsOwner) return false;

            FinalizeWinner(winnerIndex);
            return true;
        }

        /// <summary>Cancel the current vote.</summary>
        public bool CancelVote()
        {
            if (_state != VoteState.Active) return false;

            _state = VoteState.Inactive;
            _currentVote = null;
            return true;
        }

        /// <summary>Host extends the timer.</summary>
        public void ExtendTimer(float additionalSeconds)
        {
            if (_state == VoteState.Active)
                _voteEndTime += additionalSeconds;
        }

        /// <summary>Host ends vote immediately.</summary>
        public void EndVoteNow()
        {
            if (_state == VoteState.Active)
                EndVote();
        }

        /// <summary>Get the local player's current vote (-1 if not voted).</summary>
        public int GetMyVote()
        {
            string localPuid = EOSManager.Instance?.LocalProductUserId?.ToString();
            return _currentVote?.GetPlayerVote(localPuid) ?? -1;
        }

        /// <summary>Get vote counts per option.</summary>
        public int[] GetVoteCounts()
        {
            if (_currentVote == null) return Array.Empty<int>();
            int[] counts = new int[_currentVote.Options.Count];
            foreach (var vote in _currentVote.Votes.Values)
            {
                if (vote >= 0 && vote < counts.Length) counts[vote]++;
            }
            return counts;
        }

        /// <summary>Get indices of currently leading options.</summary>
        public List<int> GetCurrentLeaders()
        {
            var counts = GetVoteCounts();
            if (counts.Length == 0) return new List<int>();

            int maxVotes = counts.Max();
            var leaders = new List<int>();
            for (int i = 0; i < counts.Length; i++)
            {
                if (counts[i] == maxVotes) leaders.Add(i);
            }
            return leaders;
        }

        #endregion

        #region Private Methods

        private void EndVote()
        {
            var result = CalculateResult();

            if (result.WasTie && _tieBreakerMode == TieBreaker.HostChoice)
            {
                _pendingTieIndices = result.TiedIndices;
                var tiedOptions = result.TiedIndices.Select(i => _currentVote.Options[i]).ToList();
                OnTieNeedsDecision?.Invoke(tiedOptions);
                return;
            }

            if (result.WasTie && _tieBreakerMode == TieBreaker.Random)
            {
                int randomIdx = result.TiedIndices[UnityEngine.Random.Range(0, result.TiedIndices.Count)];
                FinalizeWinner(randomIdx);
                return;
            }

            FinalizeWinner(result.WinnerIndex);
        }

        private VoteResultData CalculateResult()
        {
            var counts = GetVoteCounts();
            int maxVotes = counts.Length > 0 ? counts.Max() : 0;

            var tiedIndices = new List<int>();
            for (int i = 0; i < counts.Length; i++)
            {
                if (counts[i] == maxVotes) tiedIndices.Add(i);
            }

            bool isTie = tiedIndices.Count > 1;

            return new VoteResultData
            {
                WinnerIndex = tiedIndices.Count > 0 ? tiedIndices[0] : 0,
                Winner = _currentVote.Options.Count > 0 ? _currentVote.Options[tiedIndices.Count > 0 ? tiedIndices[0] : 0] : null,
                WinnerVotes = maxVotes,
                WasTie = isTie,
                TiedIndices = tiedIndices
            };
        }

        private void FinalizeWinner(int winnerIndex)
        {
            _state = VoteState.Completed;
            _pendingTieIndices = null;

            var winner = _currentVote.Options[winnerIndex];
            OnVoteEnded?.Invoke(_currentVote, winner, winnerIndex);

            EOSDebugLogger.Log(DebugCategory.LobbyManager, "EOSMapVoteManager",
                $"Vote ended: {winner.DisplayName} won");
        }

        #endregion

        #region Common Presets

        public static class CommonMaps
        {
            public static VoteOption[] FPS => new[]
            {
                new VoteOption { Id = "dust2", DisplayName = "Dust II", Category = "map" },
                new VoteOption { Id = "inferno", DisplayName = "Inferno", Category = "map" },
                new VoteOption { Id = "mirage", DisplayName = "Mirage", Category = "map" },
                new VoteOption { Id = "nuke", DisplayName = "Nuke", Category = "map" }
            };

            public static VoteOption[] Arena => new[]
            {
                new VoteOption { Id = "colosseum", DisplayName = "Colosseum", Category = "map" },
                new VoteOption { Id = "pit", DisplayName = "The Pit", Category = "map" },
                new VoteOption { Id = "tower", DisplayName = "Tower", Category = "map" },
                new VoteOption { Id = "arena", DisplayName = "Arena", Category = "map" }
            };
        }

        public static class CommonModes
        {
            public static VoteOption[] Standard => new[]
            {
                new VoteOption { Id = "deathmatch", DisplayName = "Deathmatch", Category = "mode" },
                new VoteOption { Id = "tdm", DisplayName = "Team Deathmatch", Category = "mode" },
                new VoteOption { Id = "ctf", DisplayName = "Capture the Flag", Category = "mode" },
                new VoteOption { Id = "koth", DisplayName = "King of the Hill", Category = "mode" }
            };

            public static VoteOption[] Casual => new[]
            {
                new VoteOption { Id = "ffa", DisplayName = "Free for All", Category = "mode" },
                new VoteOption { Id = "infection", DisplayName = "Infection", Category = "mode" },
                new VoteOption { Id = "gungame", DisplayName = "Gun Game", Category = "mode" }
            };
        }

        #endregion
    }
}
