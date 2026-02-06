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
    /// Democratic vote-kick system using lobby member attributes for vote broadcast.
    /// Only works when in a lobby. Host has optional immunity and veto power.
    /// </summary>
    public class EOSVoteKickManager : MonoBehaviour
    {
        #region Singleton

        private static EOSVoteKickManager _instance;

        public static EOSVoteKickManager Instance
        {
            get
            {
                if (_instance == null)
                {
#if UNITY_2023_1_OR_NEWER
                    _instance = FindFirstObjectByType<EOSVoteKickManager>();
#else
                    _instance = FindObjectOfType<EOSVoteKickManager>();
#endif
                    if (_instance == null)
                    {
                        var go = new GameObject("[EOSVoteKickManager]");
                        _instance = go.AddComponent<EOSVoteKickManager>();
                        DontDestroyOnLoad(go);
                    }
                }
                return _instance;
            }
        }

        #endregion

        #region Enums

        public enum VoteThreshold
        {
            Majority,       // >50%
            TwoThirds,      // >=66.7%
            ThreeQuarters,  // >=75%
            Unanimous,      // 100% (excluding target)
            Custom
        }

        public enum VoteResult
        {
            Pending,
            Passed,
            Failed,
            Vetoed,
            TimedOut,
            Cancelled
        }

        #endregion

        #region Data Types

        [Serializable]
        public class VoteKickData
        {
            public string TargetPuid;
            public string TargetName;
            public string InitiatorPuid;
            public string InitiatorName;
            public string Reason;
            public float StartTime;
            public float Timeout;
            public VoteResult Result = VoteResult.Pending;
            public Dictionary<string, bool> Votes = new();

            public int YesVotes => Votes.Count(v => v.Value);
            public int NoVotes => Votes.Count(v => !v.Value);
            public int TotalVotes => Votes.Count;
            public float TimeRemaining => Mathf.Max(0f, Timeout - (Time.time - StartTime));
            public bool IsExpired => Time.time - StartTime >= Timeout;
        }

        #endregion

        #region Settings

        [Header("Vote Kick Settings")]
        [SerializeField] private bool _enabled = true;
        [SerializeField] private VoteThreshold _threshold = VoteThreshold.Majority;
        [SerializeField, Range(1, 100)] private int _customThresholdPercent = 51;
        [SerializeField, Range(15f, 120f)] private float _voteTimeout = 30f;
        [SerializeField, Range(30f, 300f)] private float _voteCooldown = 60f;
        [SerializeField, Range(2, 10)] private int _minPlayersForVote = 3;
        [SerializeField] private bool _hostImmunity = true;
        [SerializeField] private bool _hostCanVeto = true;
        [SerializeField, Range(1, 3)] private int _hostVoteWeight = 1;
        [SerializeField] private bool _requireReason = false;

        #endregion

        #region Public Properties

        public bool Enabled { get => _enabled; set => _enabled = value; }
        public VoteThreshold Threshold { get => _threshold; set => _threshold = value; }
        public int CustomThresholdPercent { get => _customThresholdPercent; set => _customThresholdPercent = Mathf.Clamp(value, 1, 100); }
        public float VoteTimeout { get => _voteTimeout; set => _voteTimeout = Mathf.Clamp(value, 15f, 120f); }
        public float VoteCooldown { get => _voteCooldown; set => _voteCooldown = Mathf.Clamp(value, 30f, 300f); }
        public int MinPlayersForVote { get => _minPlayersForVote; set => _minPlayersForVote = Mathf.Clamp(value, 2, 10); }
        public bool HostImmunity { get => _hostImmunity; set => _hostImmunity = value; }
        public bool HostCanVeto { get => _hostCanVeto; set => _hostCanVeto = value; }
        public int HostVoteWeight { get => _hostVoteWeight; set => _hostVoteWeight = Mathf.Clamp(value, 1, 3); }
        public bool IsVoteActive => _activeVote != null && _activeVote.Result == VoteResult.Pending;
        public VoteKickData ActiveVote => _activeVote;

        #endregion

        #region Events

        public event Action<VoteKickData> OnVoteStarted;
        public event Action<string, bool> OnVoteCast;
        public event Action<int, int, int> OnVoteProgress;
        public event Action<VoteKickData, VoteResult> OnVoteEnded;
        public event Action<string, string> OnPlayerVoteKicked;

        #endregion

        #region Private State

        private VoteKickData _activeVote;
        private readonly Dictionary<string, float> _cooldowns = new();
        private string _localPuid;

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
            if (!_enabled || _activeVote == null) return;

            if (_activeVote.Result == VoteResult.Pending && _activeVote.IsExpired)
            {
                EndVote(VoteResult.TimedOut);
            }
        }

        #endregion

        #region Public API

        /// <summary>Start a vote kick against a player.</summary>
        public (bool success, string error) StartVoteKick(string targetPuid, string reason = null)
        {
            if (!_enabled) return (false, "Vote kick is disabled");

            var lobby = EOSLobbyManager.Instance;
            if (lobby == null || !lobby.IsInLobby) return (false, "Not in a lobby");

            _localPuid = EOSManager.Instance?.LocalProductUserId?.ToString();
            if (string.IsNullOrEmpty(_localPuid)) return (false, "Not logged in");

            if (_activeVote != null && _activeVote.Result == VoteResult.Pending)
                return (false, "A vote is already in progress");

            if (_requireReason && string.IsNullOrWhiteSpace(reason))
                return (false, "A reason is required");

            if (_hostImmunity && targetPuid == lobby.CurrentLobby.OwnerPuid)
                return (false, "Cannot vote kick the host");

            if (targetPuid == _localPuid)
                return (false, "Cannot vote kick yourself");

            if (_cooldowns.TryGetValue(_localPuid, out float cooldownEnd) && Time.time < cooldownEnd)
                return (false, $"Cooldown: {cooldownEnd - Time.time:0}s remaining");

            string targetName = GetPlayerName(targetPuid);

            _activeVote = new VoteKickData
            {
                TargetPuid = targetPuid,
                TargetName = targetName,
                InitiatorPuid = _localPuid,
                InitiatorName = GetPlayerName(_localPuid),
                Reason = reason ?? "",
                StartTime = Time.time,
                Timeout = _voteTimeout,
                Result = VoteResult.Pending
            };

            // Initiator auto-votes yes
            _activeVote.Votes[_localPuid] = true;
            _cooldowns[_localPuid] = Time.time + _voteCooldown;

            OnVoteStarted?.Invoke(_activeVote);
            EOSDebugLogger.Log(DebugCategory.LobbyManager, "EOSVoteKickManager",
                $"Vote kick started against {targetName} by {_activeVote.InitiatorName}");

            BroadcastVoteViaAttribute();
            CheckVoteResult();
            return (true, null);
        }

        /// <summary>Cast a vote (true = yes kick, false = no).</summary>
        public bool CastVote(bool voteYes)
        {
            if (_activeVote == null || _activeVote.Result != VoteResult.Pending) return false;

            _localPuid = EOSManager.Instance?.LocalProductUserId?.ToString();
            if (string.IsNullOrEmpty(_localPuid)) return false;

            if (_localPuid == _activeVote.TargetPuid) return false; // Target can't vote

            _activeVote.Votes[_localPuid] = voteYes;
            OnVoteCast?.Invoke(_localPuid, voteYes);
            OnVoteProgress?.Invoke(_activeVote.YesVotes, _activeVote.NoVotes, GetEligibleVoterCount());

            BroadcastVoteViaAttribute();
            CheckVoteResult();
            return true;
        }

        /// <summary>Host vetoes the vote (cancels it).</summary>
        public bool VetoVote()
        {
            if (!_hostCanVeto || _activeVote == null || _activeVote.Result != VoteResult.Pending) return false;

            var lobby = EOSLobbyManager.Instance;
            if (lobby == null || !lobby.IsOwner) return false;

            EndVote(VoteResult.Vetoed);
            return true;
        }

        /// <summary>Cancel the vote (initiator or host only).</summary>
        public bool CancelVote()
        {
            if (_activeVote == null || _activeVote.Result != VoteResult.Pending) return false;

            _localPuid = EOSManager.Instance?.LocalProductUserId?.ToString();
            var lobby = EOSLobbyManager.Instance;
            bool isHost = lobby != null && lobby.IsOwner;

            if (_localPuid != _activeVote.InitiatorPuid && !isHost) return false;

            EndVote(VoteResult.Cancelled);
            return true;
        }

        /// <summary>Check if a player can be vote-kicked.</summary>
        public bool CanBeVoteKicked(string puid)
        {
            if (_hostImmunity)
            {
                var lobby = EOSLobbyManager.Instance;
                if (lobby != null && puid == lobby.CurrentLobby.OwnerPuid) return false;
            }
            return true;
        }

        /// <summary>Has the local player voted?</summary>
        public bool HasVoted()
        {
            _localPuid = EOSManager.Instance?.LocalProductUserId?.ToString();
            return _activeVote?.Votes.ContainsKey(_localPuid) ?? false;
        }

        /// <summary>Get the required number of yes votes to pass.</summary>
        public int GetRequiredYesVotes()
        {
            int eligible = GetEligibleVoterCount();
            float percent = GetThresholdPercent();
            return Mathf.CeilToInt(eligible * percent / 100f);
        }

        /// <summary>Get the number of eligible voters (excludes target).</summary>
        public int GetEligibleVoterCount()
        {
            var lobby = EOSLobbyManager.Instance;
            if (lobby == null || !lobby.IsInLobby) return 0;
            int total = (int)lobby.CurrentLobby.MemberCount;
            return _activeVote != null ? total - 1 : total; // Exclude target
        }

        /// <summary>Get cooldown remaining for the local player.</summary>
        public float GetCooldownRemaining()
        {
            _localPuid = EOSManager.Instance?.LocalProductUserId?.ToString();
            if (_cooldowns.TryGetValue(_localPuid, out float end))
                return Mathf.Max(0f, end - Time.time);
            return 0f;
        }

        #endregion

        #region Private Methods

        private float GetThresholdPercent()
        {
            return _threshold switch
            {
                VoteThreshold.Majority => 51f,
                VoteThreshold.TwoThirds => 67f,
                VoteThreshold.ThreeQuarters => 75f,
                VoteThreshold.Unanimous => 100f,
                VoteThreshold.Custom => _customThresholdPercent,
                _ => 51f
            };
        }

        private void CheckVoteResult()
        {
            if (_activeVote == null || _activeVote.Result != VoteResult.Pending) return;

            int required = GetRequiredYesVotes();
            int eligible = GetEligibleVoterCount();

            if (_activeVote.YesVotes >= required)
            {
                EndVote(VoteResult.Passed);
            }
            else if (_activeVote.NoVotes > eligible - required)
            {
                // Impossible to reach threshold
                EndVote(VoteResult.Failed);
            }
        }

        private void EndVote(VoteResult result)
        {
            if (_activeVote == null) return;

            _activeVote.Result = result;
            OnVoteEnded?.Invoke(_activeVote, result);

            if (result == VoteResult.Passed)
            {
                ExecuteKick(_activeVote.TargetPuid, _activeVote.TargetName);
            }

            EOSDebugLogger.Log(DebugCategory.LobbyManager, "EOSVoteKickManager",
                $"Vote ended: {result} ({_activeVote.YesVotes}Y/{_activeVote.NoVotes}N)");

            _activeVote = null;
        }

        private void ExecuteKick(string targetPuid, string targetName)
        {
            var lobby = EOSLobbyManager.Instance;
            if (lobby == null || !lobby.IsOwner) return;

            OnPlayerVoteKicked?.Invoke(targetPuid, targetName);
            _ = lobby.KickMemberAsync(targetPuid);
        }

        private void BroadcastVoteViaAttribute()
        {
            // Broadcast vote state via member attribute so other clients can see
            if (_activeVote == null) return;
            var lobby = EOSLobbyManager.Instance;
            if (lobby == null || !lobby.IsInLobby) return;

            string voteData = $"VK:{_activeVote.TargetPuid}:{_activeVote.YesVotes}:{_activeVote.NoVotes}";
            _ = lobby.SetMemberAttributeAsync("VOTE_KICK", voteData);
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
