using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Threading.Tasks;
using EOSNative.Demo;
using EOSNative.Lobbies;
using EOSNative.Net;
using EOSNative.P2P;
using EOSNative.Voice;
using UnityEngine;
using Debug = UnityEngine.Debug;

namespace EOSNative.Diagnostics
{
    public enum CheckStatus { Pending, Running, Pass, Fail, Skipped }

    [Serializable]
    public class DiagCheck
    {
        public string Name;
        public string Category;
        public CheckStatus Status;
        public string Message;
        public float Duration; // seconds
    }

    /// <summary>
    /// Runtime diagnostic system for EOS Native. Runs passive state checks instantly
    /// or triggers full lobby lifecycle sequences with one click.
    /// Singleton auto-creates under EOSManager.
    /// </summary>
    public class EOSHealthCheck : MonoBehaviour
    {
        #region Singleton

        private static EOSHealthCheck _instance;
        private static bool _shuttingDown;

        public static EOSHealthCheck Instance
        {
            get
            {
                if (_shuttingDown) return _instance;
                if (_instance != null) return _instance;
                _instance = FindAnyObjectByType<EOSHealthCheck>();
                if (_instance != null) return _instance;
                var go = new GameObject("[EOSHealthCheck]");
                if (EOSManager.Instance != null)
                    go.transform.SetParent(EOSManager.Instance.transform);
                else
                    DontDestroyOnLoad(go);
                _instance = go.AddComponent<EOSHealthCheck>();
                return _instance;
            }
        }

        #endregion

        #region State

        private readonly List<DiagCheck> _checks = new();
        private bool _registered;
        private bool _isRunning;
        private bool _createdLobbyInSequence;

        public IReadOnlyList<DiagCheck> Checks => _checks;
        public bool IsRunning => _isRunning;

        public int PassCount { get { int c = 0; foreach (var ch in _checks) if (ch.Status == CheckStatus.Pass) c++; return c; } }
        public int FailCount { get { int c = 0; foreach (var ch in _checks) if (ch.Status == CheckStatus.Fail) c++; return c; } }
        public int TotalCount => _checks.Count;
        public float TotalDuration { get { float t = 0; foreach (var ch in _checks) t += ch.Duration; return t; } }

        public event Action OnChecksUpdated;

        // Category names
        public const string CAT_SDK = "SDK & Auth";
        public const string CAT_LOBBY = "Lobby";
        public const string CAT_P2P = "P2P";
        public const string CAT_NET = "Networking";
        public const string CAT_SYNC = "Object Sync";

        // Auto-run
        private float _autoRunTimer;
        private const float AUTO_RUN_INTERVAL = 2f;

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
            if (transform.parent == null)
                DontDestroyOnLoad(gameObject);
        }

        private void OnDestroy()
        {
            if (_instance == this)
            {
                _instance = null;
                _shuttingDown = true;
            }
        }

        private void OnApplicationQuit()
        {
            _shuttingDown = true;
        }

        private void Start()
        {
            EnsureChecksRegistered();
            RunAllPassive();
        }

        private void Update()
        {
            _autoRunTimer += Time.unscaledDeltaTime;
            if (_autoRunTimer >= AUTO_RUN_INTERVAL)
            {
                _autoRunTimer = 0f;
                if (!_isRunning)
                    RunAllPassive();
            }
        }

        #endregion

        #region Check Registration

        private void EnsureChecksRegistered()
        {
            if (_registered) return;
            _registered = true;

            // SDK & Auth (8)
            Register("SDK Initialized", CAT_SDK);
            Register("Logged In", CAT_SDK);
            Register("Product User ID", CAT_SDK);
            Register("Connect Interface", CAT_SDK);
            Register("P2P Interface", CAT_SDK);
            Register("Lobby Interface", CAT_SDK);
            Register("RTC Interface", CAT_SDK);
            Register("Auth Interface", CAT_SDK);

            // Lobby (5)
            Register("Create Lobby", CAT_LOBBY);
            Register("Join Code", CAT_LOBBY);
            Register("Voice Room", CAT_LOBBY);
            Register("Search Lobbies", CAT_LOBBY);
            Register("Leave Lobby", CAT_LOBBY);

            // P2P (3)
            Register("P2P Manager Active", CAT_P2P);
            Register("NAT Type", CAT_P2P);
            Register("Peer Connected", CAT_P2P);

            // Networking (4)
            Register("Network Manager", CAT_NET);
            Register("Is Online", CAT_NET);
            Register("Room State", CAT_NET);
            Register("Player State", CAT_NET);

            // Object Sync (6)
            Register("Registered Objects", CAT_SYNC);
            Register("Spring Syncs", CAT_SYNC);
            Register("Scene Objects", CAT_SYNC);
            Register("Player Balls", CAT_SYNC);
            Register("Physics Objects", CAT_SYNC);
            Register("Held Objects", CAT_SYNC);
        }

        private void Register(string name, string category)
        {
            _checks.Add(new DiagCheck { Name = name, Category = category, Status = CheckStatus.Pending });
        }

        private DiagCheck Get(string name)
        {
            foreach (var c in _checks)
                if (c.Name == name) return c;
            return null;
        }

        #endregion

        #region Public API

        public void RunAllPassive()
        {
            EnsureChecksRegistered();
            RunSDKChecks();
            RunP2PChecks();
            RunNetworkingChecks();
            RunSyncChecks();
            OnChecksUpdated?.Invoke();
        }

        public async void RunFullSequence()
        {
            if (_isRunning) return;
            _isRunning = true;
            _createdLobbyInSequence = false;
            EnsureChecksRegistered();

            try
            {
                // 1. SDK & Auth (instant)
                RunSDKChecks();
                OnChecksUpdated?.Invoke();

                // 2-4. Lobby sequence
                await RunLobbyChecksAsync();
                OnChecksUpdated?.Invoke();

                // 5. P2P checks
                RunP2PChecks();
                OnChecksUpdated?.Invoke();

                // 6. Networking checks
                RunNetworkingChecks();
                OnChecksUpdated?.Invoke();

                // 7. Sync checks
                RunSyncChecks();
                OnChecksUpdated?.Invoke();

                // 7. Leave lobby if we created one
                if (_createdLobbyInSequence)
                {
                    await RunLeaveCheckAsync();
                    OnChecksUpdated?.Invoke();
                }

                Debug.Log($"[HealthCheck] Complete: {PassCount}/{TotalCount} passed ({TotalDuration:F2}s)");
            }
            catch (Exception e)
            {
                Debug.LogError($"[HealthCheck] Sequence error: {e.Message}");
            }
            finally
            {
                _isRunning = false;
                OnChecksUpdated?.Invoke();
            }
        }

        public async void RunLobbySequence()
        {
            if (_isRunning) return;
            _isRunning = true;
            _createdLobbyInSequence = false;
            EnsureChecksRegistered();

            try
            {
                await RunLobbyChecksAsync();
                OnChecksUpdated?.Invoke();

                if (_createdLobbyInSequence)
                {
                    await RunLeaveCheckAsync();
                    OnChecksUpdated?.Invoke();
                }

                Debug.Log($"[HealthCheck] Lobby sequence complete: {PassCount}/{TotalCount} passed ({TotalDuration:F2}s)");
            }
            catch (Exception e)
            {
                Debug.LogError($"[HealthCheck] Lobby sequence error: {e.Message}");
            }
            finally
            {
                _isRunning = false;
                OnChecksUpdated?.Invoke();
            }
        }

        public void RunCategory(string category)
        {
            EnsureChecksRegistered();

            switch (category)
            {
                case CAT_SDK: RunSDKChecks(); break;
                case CAT_P2P: RunP2PChecks(); break;
                case CAT_NET: RunNetworkingChecks(); break;
                case CAT_SYNC: RunSyncChecks(); break;
                case CAT_LOBBY:
                    RunLobbySequence();
                    return; // async — will invoke OnChecksUpdated internally
            }

            OnChecksUpdated?.Invoke();
        }

        public void ResetAll()
        {
            EnsureChecksRegistered();
            foreach (var c in _checks)
            {
                c.Status = CheckStatus.Pending;
                c.Message = null;
                c.Duration = 0;
            }
            OnChecksUpdated?.Invoke();
        }

        #endregion

        #region SDK & Auth Checks

        private void RunSDKChecks()
        {
            var mgr = EOSManager.Instance;
            bool hasManager = mgr != null;

            SetCheck("SDK Initialized",
                hasManager && mgr.IsInitialized ? CheckStatus.Pass : CheckStatus.Fail,
                hasManager && mgr.IsInitialized ? "EOS SDK initialized" : "SDK not initialized");

            SetCheck("Logged In",
                hasManager && mgr.IsLoggedIn ? CheckStatus.Pass : CheckStatus.Fail,
                hasManager && mgr.IsLoggedIn ? "Connect login active" : "Not logged in");

            if (hasManager && mgr.LocalProductUserId != null)
            {
                string puid = mgr.LocalProductUserId.ToString();
                string short_ = puid.Length > 8 ? puid.Substring(0, 8) + "..." : puid;
                SetCheck("Product User ID", CheckStatus.Pass, short_);
            }
            else
            {
                SetCheck("Product User ID", CheckStatus.Fail, "No PUID");
            }

            SetCheck("Connect Interface",
                hasManager && mgr.ConnectInterface != null ? CheckStatus.Pass : CheckStatus.Fail,
                hasManager && mgr.ConnectInterface != null ? "Available" : "Unavailable");

            SetCheck("P2P Interface",
                hasManager && mgr.P2PInterface != null ? CheckStatus.Pass : CheckStatus.Fail,
                hasManager && mgr.P2PInterface != null ? "Available" : "Unavailable");

            SetCheck("Lobby Interface",
                hasManager && mgr.LobbyInterface != null ? CheckStatus.Pass : CheckStatus.Fail,
                hasManager && mgr.LobbyInterface != null ? "Available" : "Unavailable");

            // RTC may not be available on all platforms
            if (hasManager && mgr.RTCInterface != null)
                SetCheck("RTC Interface", CheckStatus.Pass, "Available");
            else if (hasManager && mgr.IsInitialized)
                SetCheck("RTC Interface", CheckStatus.Skipped, "Not available on this platform");
            else
                SetCheck("RTC Interface", CheckStatus.Fail, "SDK not initialized");

            SetCheck("Auth Interface",
                hasManager && mgr.AuthInterface != null ? CheckStatus.Pass : CheckStatus.Fail,
                hasManager && mgr.AuthInterface != null ? "Available" : "Unavailable");
        }

        #endregion

        #region Lobby Checks

        private async Task RunLobbyChecksAsync()
        {
            var lobby = EOSLobbyManager.Instance;
            if (lobby == null)
            {
                SetCheck("Create Lobby", CheckStatus.Fail, "LobbyManager not available");
                SetCheck("Join Code", CheckStatus.Fail, "No lobby");
                SetCheck("Voice Room", CheckStatus.Fail, "No lobby");
                SetCheck("Search Lobbies", CheckStatus.Fail, "No lobby manager");
                SetCheck("Leave Lobby", CheckStatus.Skipped, "Nothing to leave");
                return;
            }

            // If already in a lobby, skip creation
            if (lobby.IsInLobby)
            {
                SetCheck("Create Lobby", CheckStatus.Skipped, "Already in lobby");
                _createdLobbyInSequence = false;
            }
            else
            {
                // Create Lobby
                var createCheck = Get("Create Lobby");
                createCheck.Status = CheckStatus.Running;
                OnChecksUpdated?.Invoke();

                var sw = Stopwatch.StartNew();
                var (result, lobbyData) = await lobby.CreateLobbyAsync();
                sw.Stop();

                if (result == Epic.OnlineServices.Result.Success)
                {
                    string lobbyId = lobbyData.LobbyId;
                    string short_ = lobbyId != null && lobbyId.Length > 8 ? lobbyId.Substring(0, 8) + "..." : lobbyId;
                    SetCheck("Create Lobby", CheckStatus.Pass, $"Created: {short_}", sw);
                    _createdLobbyInSequence = true;
                }
                else
                {
                    SetCheck("Create Lobby", CheckStatus.Fail, $"Failed: {result}", sw);
                    // Can't continue lobby checks
                    SetCheck("Join Code", CheckStatus.Skipped, "No lobby created");
                    SetCheck("Voice Room", CheckStatus.Skipped, "No lobby created");
                    SetCheck("Search Lobbies", CheckStatus.Skipped, "No lobby created");
                    SetCheck("Leave Lobby", CheckStatus.Skipped, "Nothing to leave");
                    return;
                }
            }

            // Join Code
            var currentLobby = lobby.CurrentLobby;
            if (!string.IsNullOrEmpty(currentLobby.JoinCode))
                SetCheck("Join Code", CheckStatus.Pass, $"Code: {currentLobby.JoinCode}");
            else
                SetCheck("Join Code", CheckStatus.Fail, "No join code assigned");

            // Voice Room — use FindAnyObjectByType to avoid auto-create
            var voice = FindAnyObjectByType<EOSVoiceManager>();
            if (voice != null && voice.IsVoiceEnabled)
                SetCheck("Voice Room", CheckStatus.Pass, "RTC connected");
            else if (EOSManager.Instance?.RTCInterface == null)
                SetCheck("Voice Room", CheckStatus.Skipped, "RTC not available");
            else
                SetCheck("Voice Room", CheckStatus.Fail, "RTC not connected");

            // Search Lobbies
            var searchCheck = Get("Search Lobbies");
            searchCheck.Status = CheckStatus.Running;
            OnChecksUpdated?.Invoke();

            var searchSw = Stopwatch.StartNew();
            var (searchResult, lobbies) = await lobby.SearchLobbiesAsync();
            searchSw.Stop();

            if (searchResult == Epic.OnlineServices.Result.Success)
                SetCheck("Search Lobbies", CheckStatus.Pass, $"Found {lobbies.Count} lobb{(lobbies.Count == 1 ? "y" : "ies")}", searchSw);
            else
                SetCheck("Search Lobbies", CheckStatus.Fail, $"Search failed: {searchResult}", searchSw);

            // Leave check is handled separately if we created
            if (!_createdLobbyInSequence)
                SetCheck("Leave Lobby", CheckStatus.Skipped, "Didn't create lobby");
        }

        private async Task RunLeaveCheckAsync()
        {
            var lobby = EOSLobbyManager.Instance;
            if (lobby == null || !lobby.IsInLobby)
            {
                SetCheck("Leave Lobby", CheckStatus.Skipped, "Not in lobby");
                return;
            }

            var leaveCheck = Get("Leave Lobby");
            leaveCheck.Status = CheckStatus.Running;
            OnChecksUpdated?.Invoke();

            var sw = Stopwatch.StartNew();
            var result = await lobby.LeaveLobbyAsync();
            sw.Stop();

            if (result == Epic.OnlineServices.Result.Success)
                SetCheck("Leave Lobby", CheckStatus.Pass, "Clean exit", sw);
            else
                SetCheck("Leave Lobby", CheckStatus.Fail, $"Leave failed: {result}", sw);
        }

        #endregion

        #region P2P Checks

        private void RunP2PChecks()
        {
            var p2p = EOSP2PManager._instance;

            if (p2p != null && p2p.IsActive)
                SetCheck("P2P Manager Active", CheckStatus.Pass, "Initialized");
            else if (p2p != null)
                SetCheck("P2P Manager Active", CheckStatus.Fail, "Not active");
            else
                SetCheck("P2P Manager Active", CheckStatus.Fail, "No P2P manager");

            // NAT Type
            var stats = NetworkStats._instance;
            if (stats != null)
            {
                var nat = stats.LocalNATType;
                if (nat != Epic.OnlineServices.P2P.NATType.Unknown)
                    SetCheck("NAT Type", CheckStatus.Pass, nat.ToString());
                else
                    SetCheck("NAT Type", CheckStatus.Fail, "Unknown");
            }
            else
            {
                SetCheck("NAT Type", CheckStatus.Skipped, "NetworkStats not active");
            }

            // Peer Connected
            if (p2p != null && p2p.Peers.Count > 0)
                SetCheck("Peer Connected", CheckStatus.Pass, $"{p2p.Peers.Count} peer(s)");
            else
                SetCheck("Peer Connected", CheckStatus.Skipped, "No peers");
        }

        #endregion

        #region Networking Checks

        private void RunNetworkingChecks()
        {
            var nm = NetworkManager._instance;

            SetCheck("Network Manager",
                nm != null ? CheckStatus.Pass : CheckStatus.Fail,
                nm != null ? "Active" : "Not found");

            if (nm != null)
            {
                SetCheck("Is Online",
                    nm.IsOnline ? CheckStatus.Pass : CheckStatus.Fail,
                    nm.IsOnline ? "Online" : "Offline");

                SetCheck("Room State",
                    nm.RoomState != null ? CheckStatus.Pass : CheckStatus.Fail,
                    nm.RoomState != null ? "Present" : "Not created");

                SetCheck("Player State",
                    nm.LocalPlayerState != null ? CheckStatus.Pass : CheckStatus.Fail,
                    nm.LocalPlayerState != null ? "Present" : "Not created");
            }
            else
            {
                SetCheck("Is Online", CheckStatus.Fail, "No NetworkManager");
                SetCheck("Room State", CheckStatus.Fail, "No NetworkManager");
                SetCheck("Player State", CheckStatus.Fail, "No NetworkManager");
            }
        }

        #endregion

        #region Object Sync Checks

        private void RunSyncChecks()
        {
            var nm = NetworkManager._instance;

            if (nm == null)
            {
                SetCheck("Registered Objects", CheckStatus.Fail, "No NetworkManager");
                SetCheck("Spring Syncs", CheckStatus.Fail, "No NetworkManager");
                SetCheck("Scene Objects", CheckStatus.Fail, "No NetworkManager");
                SetCheck("Player Balls", CheckStatus.Fail, "No NetworkManager");
                SetCheck("Physics Objects", CheckStatus.Fail, "No NetworkManager");
                SetCheck("Held Objects", CheckStatus.Skipped, "No NetworkManager");
                return;
            }

            int totalObjs = nm.Objects.Count;
            int springSyncs = 0;
            int sceneObjs = 0;
            int playerBalls = 0;
            int physicsObjs = 0;
            int heldObjs = 0;
            int springEnabled = 0;
            int springDisabled = 0;

            foreach (var kvp in nm.Objects)
            {
                var obj = kvp.Value;
                if (obj == null) continue;

                if (obj.PrefabId == NetworkObject.SCENE_OBJECT)
                    sceneObjs++;

                if (obj.GetComponent<P2PPlayerBall>() != null)
                    playerBalls++;

                if (obj.GetComponent<Rigidbody>() != null)
                    physicsObjs++;

                if (obj.ParentNetworkId != 0)
                    heldObjs++;

                var sync = obj.GetComponent<P2PSpringSync>();
                if (sync != null)
                {
                    springSyncs++;
                    if (sync.enabled)
                        springEnabled++;
                    else
                        springDisabled++;
                }
            }

            // Registered Objects
            if (totalObjs > 0)
                SetCheck("Registered Objects", CheckStatus.Pass, $"{totalObjs} objects");
            else
                SetCheck("Registered Objects", CheckStatus.Skipped, "None registered");

            // Spring Syncs
            if (springSyncs > 0)
                SetCheck("Spring Syncs", CheckStatus.Pass,
                    $"{springSyncs} total ({springEnabled} active, {springDisabled} held)");
            else if (totalObjs > 0)
                SetCheck("Spring Syncs", CheckStatus.Fail, "No P2PSpringSync on any object");
            else
                SetCheck("Spring Syncs", CheckStatus.Skipped, "No objects");

            // Scene Objects
            if (sceneObjs > 0)
                SetCheck("Scene Objects", CheckStatus.Pass, $"{sceneObjs} scene objects");
            else
                SetCheck("Scene Objects", CheckStatus.Skipped, "None");

            // Player Balls
            if (playerBalls > 0)
                SetCheck("Player Balls", CheckStatus.Pass, $"{playerBalls} balls");
            else
                SetCheck("Player Balls", CheckStatus.Skipped, "No balls spawned");

            // Physics Objects
            if (physicsObjs > 0)
                SetCheck("Physics Objects", CheckStatus.Pass, $"{physicsObjs} with Rigidbody");
            else
                SetCheck("Physics Objects", CheckStatus.Skipped, "None");

            // Held Objects
            if (heldObjs > 0)
                SetCheck("Held Objects", CheckStatus.Pass, $"{heldObjs} reparented (held)");
            else
                SetCheck("Held Objects", CheckStatus.Skipped, "None held");
        }

        #endregion

        #region Helpers

        private void SetCheck(string name, CheckStatus status, string message, Stopwatch sw = null)
        {
            var check = Get(name);
            if (check == null) return;
            check.Status = status;
            check.Message = message;
            if (sw != null)
                check.Duration = (float)sw.Elapsed.TotalSeconds;
        }

        /// <summary>
        /// Get all unique category names in order.
        /// </summary>
        public List<string> GetCategories()
        {
            EnsureChecksRegistered();
            var cats = new List<string>();
            foreach (var c in _checks)
                if (!cats.Contains(c.Category))
                    cats.Add(c.Category);
            return cats;
        }

        /// <summary>
        /// Get checks for a specific category.
        /// </summary>
        public List<DiagCheck> GetChecksForCategory(string category)
        {
            var list = new List<DiagCheck>();
            foreach (var c in _checks)
                if (c.Category == category)
                    list.Add(c);
            return list;
        }

        /// <summary>
        /// Get pass/total counts for a category.
        /// </summary>
        public (int pass, int total) GetCategoryCounts(string category)
        {
            int pass = 0, total = 0;
            foreach (var c in _checks)
            {
                if (c.Category != category) continue;
                total++;
                if (c.Status == CheckStatus.Pass) pass++;
            }
            return (pass, total);
        }

        #endregion
    }
}
