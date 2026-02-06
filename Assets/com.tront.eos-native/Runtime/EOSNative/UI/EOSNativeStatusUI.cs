using System;
using System.Collections.Generic;
using System.Text;
using System.Threading.Tasks;
using Epic.OnlineServices;
using Epic.OnlineServices.Reports;
using Epic.OnlineServices.RTCAudio;
using EOSNative.AntiCheat;
using EOSNative.Lobbies;
using EOSNative.Logging;
using EOSNative.Replay;
using EOSNative.Social;
using EOSNative.Storage;
using EOSNative.Voice;
using UnityEngine;
#if EOS_HAS_INPUT_SYSTEM
using UnityEngine.InputSystem;
#endif

namespace EOSNative.UI
{
    /// <summary>
    /// Runtime OnGUI overlay for EOS Native verification.
    /// Toggle with F1. Tabs: Status, Lobbies, Voice, Social, Stats, Tools.
    /// Dark-themed professional UI with foldouts, level bars, and device selection.
    /// </summary>
    public class EOSNativeStatusUI : MonoBehaviour
    {
        [Header("Settings")]
#if EOS_HAS_INPUT_SYSTEM
        [SerializeField] private Key _toggleKey = Key.F1;
#else
        [SerializeField] private KeyCode _toggleKey = KeyCode.F1;
#endif
        [SerializeField] private bool _showOnStart = true;

        private bool _visible;
        private int _currentTab;
        private Vector2 _scrollPos;
        private Rect _windowRect = new Rect(20, 20, 480, 600);

        #region Style Fields

        // Styles (lazy init)
        private bool _stylesInited;

        // Background textures
        private Texture2D _windowBgTex;
        private Texture2D _sectionBgTex;
        private Texture2D _foldoutBgTex;
        private Texture2D _foldoutHoverTex;
        private Texture2D _tabActiveTex;
        private Texture2D _tabNormalTex;
        private Texture2D _tabHoverTex;
        private Texture2D _buttonBgTex;
        private Texture2D _buttonHoverTex;
        private Texture2D _toggleOnTex;
        private Texture2D _toggleOffTex;
        private Texture2D _levelBgTex;
        private Texture2D _levelFillTex;
        private Texture2D _levelPeakTex;

        // Styles
        private GUIStyle _windowStyle;
        private GUIStyle _headerStyle;
        private GUIStyle _subHeaderStyle;
        private GUIStyle _greenLabel;
        private GUIStyle _redLabel;
        private GUIStyle _yellowLabel;
        private GUIStyle _orangeLabel;
        private GUIStyle _cyanLabel;
        private GUIStyle _grayLabel;
        private GUIStyle _whiteLabel;
        private GUIStyle _tabButtonActive;
        private GUIStyle _tabButtonNormal;
        private GUIStyle _sectionBox;
        private GUIStyle _monoLabel;
        private GUIStyle _actionButton;
        private GUIStyle _smallButton;
        private GUIStyle _toggleStyle;
        private GUIStyle _foldoutStyle;
        private GUIStyle _dropdownStyle;
        private GUIStyle _textFieldStyle;

        #endregion

        #region Tab State

        // Lobby tab state
        private string _lobbyName = "Test Lobby";
        private int _maxPlayers = 4;
        private bool _lobbyPublic = true;
        private bool _lobbyVoice = true;
        private bool _lobbyHostMigration = true;
        private string _joinCode = "";
        private string _lobbyStatus = "";
        private List<LobbyData> _searchResults;
        private bool _searching;

        // Voice tab state
        private int _selectedInputDevice = -1;
        private int _selectedOutputDevice = -1;
        private float _peakLevel;
        private float _peakDecay;

        // Foldout states
        private bool _foldInterfaces = true;
        private bool _foldPlatform = true;
        private bool _foldCurrentLobby = true;
        private bool _foldCreateLobby = true;
        private bool _foldJoinLobby = true;
        private bool _foldSearch = true;
        private bool _foldParticipants = true;
        private bool _foldAudioDevices = true;
        private bool _foldFriends = true;
        private bool _foldRecent = true;
        private bool _foldEpicAccount = true;
        private bool _foldMembers = true;
        private bool _foldChat = true;
        private bool _foldLocalMic = true;
        private bool _foldInvites = true;
        private bool _foldBlocked = true;
        private bool _foldEpicFriends = true;
        private bool _foldStats = true;
        private bool _foldAchievements = true;
        private bool _foldRanked = true;
        private bool _foldStorage = true;
        private bool _foldAntiCheat = true;
        private bool _foldReplays = true;
        private bool _foldMetrics = true;
        private bool _foldLFG = true;

        // Chat state
        private string _chatInput = "";
        private Vector2 _chatScrollPos;
        private readonly StringBuilder _chatLogBuilder = new StringBuilder();

        // Members state
        private Vector2 _memberScrollPos;

        // Report popup state
        private bool _showReportPopup;
        private string _reportTargetPuid = "";
        private string _reportStatus = "";
        private int _reportCategoryIndex;

        // Player profile popup state
        private bool _showProfilePopup;
        private string _profilePuid = "";
        private string _profileNote = "";
        private bool _profileEditingNote;
        private string _profileStatus = "";

        // Invites state
        private string _inviteRecipientPuid = "";
        private string _inviteStatus = "";

        // Friends state
        private Vector2 _friendsScrollPos;
        private string _editingNotePuid;
        private string _editingNoteText = "";

        // Epic Friends state
        private Vector2 _epicFriendsScrollPos;

        // Blocked state
        private Vector2 _blockedScrollPos;

        // Stats & Leaderboards state
        private Vector2 _statsScrollPos;
        private Vector2 _leaderboardScrollPos;
        private string _selectedLeaderboardId = "";
        private List<LeaderboardEntry> _currentLeaderboardEntries = new List<LeaderboardEntry>();
        private string _testStatName = "test_stat";
        private int _testStatAmount = 1;

        // Achievements state
        private Vector2 _achievementsScrollPos;

        // Ranked state
        private string _rankedGameMode = "ranked";
        private string _rankedStatus = "";

        // Cloud Storage state
        private Vector2 _storageScrollPos;
        private string _testFileName = "test.txt";
        private string _testFileContent = "Hello, EOS!";

        // Replays state
        private Vector2 _replayListScroll;
        private List<ReplayHeader> _cachedReplays = new List<ReplayHeader>();
        private float _lastReplayRefresh;
        private string _importPath = "";
        private bool _showExportSuccess;
        private float _exportSuccessTime;

        // LFG state
        private string _lfgTitle = "Looking for players";
        private string _lfgGameMode = "";
        private int _lfgDesiredSize = 4;
        private string _lfgStatus = "";
        private Vector2 _lfgSearchScrollPos;

        // Tab names
        private static readonly string[] TabNames = { "Status", "Lobbies", "Voice", "Social", "Stats", "Tools" };

        #endregion

        #region Unity Lifecycle

        private void Start()
        {
            _visible = _showOnStart;
        }

        private void Update()
        {
#if EOS_HAS_INPUT_SYSTEM
            if (Keyboard.current != null && Keyboard.current[_toggleKey].wasPressedThisFrame)
#else
            if (Input.GetKeyDown(_toggleKey))
#endif
            {
                _visible = !_visible;
            }

            // Decay peak level indicator
            if (_peakDecay > 0)
            {
                _peakDecay -= Time.deltaTime * 0.5f;
                if (_peakDecay < 0) _peakDecay = 0;
            }
        }

        private void OnDestroy()
        {
            // Cleanup textures
            DestroyTex(_windowBgTex);
            DestroyTex(_sectionBgTex);
            DestroyTex(_foldoutBgTex);
            DestroyTex(_foldoutHoverTex);
            DestroyTex(_tabActiveTex);
            DestroyTex(_tabNormalTex);
            DestroyTex(_tabHoverTex);
            DestroyTex(_buttonBgTex);
            DestroyTex(_buttonHoverTex);
            DestroyTex(_toggleOnTex);
            DestroyTex(_toggleOffTex);
            DestroyTex(_levelBgTex);
            DestroyTex(_levelFillTex);
            DestroyTex(_levelPeakTex);
        }

        private void DestroyTex(Texture2D tex)
        {
            if (tex != null) Destroy(tex);
        }

        #endregion

        #region Style Initialization

        private static Texture2D MakeTexture(int width, int height, Color color)
        {
            var pix = new Color[width * height];
            for (int i = 0; i < pix.Length; i++) pix[i] = color;
            var tex = new Texture2D(width, height);
            tex.SetPixels(pix);
            tex.Apply();
            tex.hideFlags = HideFlags.DontSave;
            return tex;
        }

        private void InitStyles()
        {
            if (_stylesInited) return;
            _stylesInited = true;

            // Background textures
            _windowBgTex = MakeTexture(2, 2, new Color(0.12f, 0.12f, 0.15f, 0.97f));
            _sectionBgTex = MakeTexture(2, 2, new Color(0.18f, 0.18f, 0.22f, 1f));
            _foldoutBgTex = MakeTexture(2, 2, new Color(0.25f, 0.25f, 0.3f, 1f));
            _foldoutHoverTex = MakeTexture(2, 2, new Color(0.32f, 0.32f, 0.38f, 1f));
            _tabActiveTex = MakeTexture(2, 2, new Color(0.3f, 0.5f, 0.7f, 1f));
            _tabNormalTex = MakeTexture(2, 2, new Color(0.2f, 0.2f, 0.25f, 1f));
            _tabHoverTex = MakeTexture(2, 2, new Color(0.28f, 0.28f, 0.35f, 1f));
            _buttonBgTex = MakeTexture(2, 2, new Color(0.25f, 0.35f, 0.5f, 1f));
            _buttonHoverTex = MakeTexture(2, 2, new Color(0.3f, 0.45f, 0.6f, 1f));
            _toggleOnTex = MakeTexture(2, 2, new Color(0.2f, 0.6f, 0.3f, 1f));
            _toggleOffTex = MakeTexture(2, 2, new Color(0.35f, 0.2f, 0.2f, 1f));
            _levelBgTex = MakeTexture(2, 2, new Color(0.1f, 0.1f, 0.13f, 1f));
            _levelFillTex = MakeTexture(2, 2, new Color(0.3f, 0.8f, 0.4f, 1f));
            _levelPeakTex = MakeTexture(2, 2, new Color(1f, 0.4f, 0.2f, 1f));

            // Window style
            _windowStyle = new GUIStyle(GUI.skin.window)
            {
                padding = new RectOffset(8, 8, 22, 8)
            };
            _windowStyle.normal.background = _windowBgTex;
            _windowStyle.normal.textColor = new Color(0.4f, 0.8f, 1f);
            _windowStyle.fontSize = 13;
            _windowStyle.fontStyle = FontStyle.Bold;
            _windowStyle.onNormal.background = _windowBgTex;

            // Header - cyan 18px
            _headerStyle = new GUIStyle(GUI.skin.label)
            {
                fontSize = 16,
                fontStyle = FontStyle.Bold,
                alignment = TextAnchor.MiddleLeft
            };
            _headerStyle.normal.textColor = new Color(0.4f, 0.8f, 1f);

            // Sub header - 12px bold white
            _subHeaderStyle = new GUIStyle(GUI.skin.label)
            {
                fontSize = 12,
                fontStyle = FontStyle.Bold
            };
            _subHeaderStyle.normal.textColor = new Color(0.85f, 0.85f, 0.9f);

            // Color labels
            _greenLabel = new GUIStyle(GUI.skin.label) { fontSize = 12 };
            _greenLabel.normal.textColor = new Color(0.3f, 1f, 0.3f);

            _redLabel = new GUIStyle(GUI.skin.label) { fontSize = 12 };
            _redLabel.normal.textColor = new Color(1f, 0.3f, 0.3f);

            _yellowLabel = new GUIStyle(GUI.skin.label) { fontSize = 12 };
            _yellowLabel.normal.textColor = new Color(1f, 1f, 0.3f);

            _orangeLabel = new GUIStyle(GUI.skin.label) { fontSize = 12 };
            _orangeLabel.normal.textColor = new Color(1f, 0.7f, 0.2f);

            _cyanLabel = new GUIStyle(GUI.skin.label) { fontSize = 12 };
            _cyanLabel.normal.textColor = new Color(0.4f, 0.9f, 1f);

            _grayLabel = new GUIStyle(GUI.skin.label) { fontSize = 11 };
            _grayLabel.normal.textColor = new Color(0.6f, 0.6f, 0.65f);

            _whiteLabel = new GUIStyle(GUI.skin.label) { fontSize = 12 };
            _whiteLabel.normal.textColor = new Color(0.9f, 0.9f, 0.95f);

            // Mono label for IDs
            _monoLabel = new GUIStyle(GUI.skin.label) { fontSize = 11 };
            _monoLabel.normal.textColor = new Color(0.7f, 0.85f, 1f);

            // Tab buttons
            _tabButtonActive = new GUIStyle(GUI.skin.button)
            {
                fontSize = 13,
                fontStyle = FontStyle.Bold,
                padding = new RectOffset(8, 8, 4, 4)
            };
            _tabButtonActive.normal.background = _tabActiveTex;
            _tabButtonActive.normal.textColor = Color.white;
            _tabButtonActive.hover.background = _tabActiveTex;
            _tabButtonActive.hover.textColor = Color.white;
            _tabButtonActive.active.background = _tabActiveTex;

            _tabButtonNormal = new GUIStyle(GUI.skin.button)
            {
                fontSize = 12,
                padding = new RectOffset(8, 8, 4, 4)
            };
            _tabButtonNormal.normal.background = _tabNormalTex;
            _tabButtonNormal.normal.textColor = new Color(0.7f, 0.7f, 0.75f);
            _tabButtonNormal.hover.background = _tabHoverTex;
            _tabButtonNormal.hover.textColor = new Color(0.9f, 0.9f, 1f);
            _tabButtonNormal.active.background = _tabHoverTex;

            // Section box
            _sectionBox = new GUIStyle(GUI.skin.box)
            {
                padding = new RectOffset(10, 10, 8, 8),
                margin = new RectOffset(0, 0, 4, 4)
            };
            _sectionBox.normal.background = _sectionBgTex;

            // Action button - big bold
            _actionButton = new GUIStyle(GUI.skin.button)
            {
                fontSize = 14,
                fontStyle = FontStyle.Bold,
                padding = new RectOffset(12, 12, 6, 6)
            };
            _actionButton.normal.background = _buttonBgTex;
            _actionButton.normal.textColor = Color.white;
            _actionButton.hover.background = _buttonHoverTex;
            _actionButton.hover.textColor = Color.white;
            _actionButton.active.background = _buttonHoverTex;

            // Small button
            _smallButton = new GUIStyle(GUI.skin.button)
            {
                fontSize = 11,
                padding = new RectOffset(8, 8, 3, 3)
            };
            _smallButton.normal.background = _buttonBgTex;
            _smallButton.normal.textColor = new Color(0.85f, 0.9f, 1f);
            _smallButton.hover.background = _buttonHoverTex;
            _smallButton.hover.textColor = Color.white;
            _smallButton.active.background = _buttonHoverTex;

            // Toggle style
            _toggleStyle = new GUIStyle(GUI.skin.toggle)
            {
                fontSize = 12
            };
            _toggleStyle.normal.textColor = new Color(0.85f, 0.85f, 0.9f);
            _toggleStyle.onNormal.textColor = new Color(0.3f, 1f, 0.5f);

            // Foldout style
            _foldoutStyle = new GUIStyle(GUI.skin.button)
            {
                fontSize = 12,
                fontStyle = FontStyle.Bold,
                alignment = TextAnchor.MiddleLeft,
                padding = new RectOffset(10, 10, 5, 5)
            };
            _foldoutStyle.normal.background = _foldoutBgTex;
            _foldoutStyle.normal.textColor = new Color(0.8f, 0.85f, 0.95f);
            _foldoutStyle.hover.background = _foldoutHoverTex;
            _foldoutStyle.hover.textColor = Color.white;
            _foldoutStyle.active.background = _foldoutHoverTex;

            // Dropdown
            _dropdownStyle = new GUIStyle(GUI.skin.button)
            {
                fontSize = 11,
                alignment = TextAnchor.MiddleLeft,
                padding = new RectOffset(8, 8, 3, 3)
            };
            _dropdownStyle.normal.background = _sectionBgTex;
            _dropdownStyle.normal.textColor = new Color(0.8f, 0.9f, 1f);
            _dropdownStyle.hover.background = _foldoutHoverTex;

            // Text field
            _textFieldStyle = new GUIStyle(GUI.skin.textField)
            {
                fontSize = 12
            };
            _textFieldStyle.normal.textColor = new Color(0.9f, 0.95f, 1f);
        }

        #endregion

        #region Drawing Helpers

        private bool DrawFoldout(string label, ref bool state)
        {
            string arrow = state ? "\u25BC " : "\u25B6 ";
            if (GUILayout.Button(arrow + label, _foldoutStyle, GUILayout.Height(24)))
            {
                state = !state;
            }
            return state;
        }

        private void DrawStatusRow(string label, bool ok, string trueText, string falseText)
        {
            GUILayout.BeginHorizontal();
            GUILayout.Label(label + ":", _whiteLabel, GUILayout.Width(110));
            GUILayout.Label(ok ? trueText : falseText, ok ? _greenLabel : _redLabel);
            GUILayout.EndHorizontal();
        }

        private void DrawInterfaceBadge(string name, bool available)
        {
            var style = available ? _greenLabel : _grayLabel;
            string prefix = available ? "\u2713" : "\u2717";
            GUILayout.Label($"{prefix} {name}", style, GUILayout.Width(105));
        }

        private void DrawLevelBar(Rect rect, float level, float peak)
        {
            // Background
            GUI.DrawTexture(rect, _levelBgTex);

            // Fill
            if (level > 0)
            {
                float fillWidth = rect.width * Mathf.Clamp01(level);
                GUI.DrawTexture(new Rect(rect.x, rect.y, fillWidth, rect.height), _levelFillTex);
            }

            // Peak indicator
            if (peak > 0.01f)
            {
                float peakX = rect.x + rect.width * Mathf.Clamp01(peak);
                GUI.DrawTexture(new Rect(peakX - 1, rect.y, 2, rect.height), _levelPeakTex);
            }
        }

        private void DrawKeyValue(string key, string value)
        {
            GUILayout.BeginHorizontal();
            GUILayout.Label(key + ":", _grayLabel, GUILayout.Width(90));
            GUILayout.Label(value ?? "(none)", _whiteLabel);
            GUILayout.EndHorizontal();
        }

        #endregion

        #region OnGUI

        private void OnGUI()
        {
            if (!_visible) return;

            InitStyles();

            _windowRect = GUILayout.Window(94201, _windowRect, DrawWindow, "EOS Native (F1)", _windowStyle);

            // Popup overlays
            if (_showProfilePopup)
            {
                DrawPlayerProfilePopup();
            }
            else if (_showReportPopup)
            {
                DrawReportPopup();
            }
        }

        private void DrawWindow(int id)
        {
            // Tab bar
            GUILayout.BeginHorizontal();
            for (int i = 0; i < TabNames.Length; i++)
            {
                var style = i == _currentTab ? _tabButtonActive : _tabButtonNormal;
                if (GUILayout.Button(TabNames[i], style, GUILayout.Height(28)))
                {
                    _currentTab = i;
                }
            }
            GUILayout.EndHorizontal();

            GUILayout.Space(6);

            _scrollPos = GUILayout.BeginScrollView(_scrollPos);

            switch (_currentTab)
            {
                case 0: DrawStatusTab(); break;
                case 1: DrawLobbiesTab(); break;
                case 2: DrawVoiceTab(); break;
                case 3: DrawSocialTab(); break;
                case 4: DrawStatsTab(); break;
                case 5: DrawToolsTab(); break;
            }

            GUILayout.EndScrollView();

            GUI.DragWindow(new Rect(0, 0, 10000, 24));
        }

        #endregion

        #region Status Tab

        private void DrawStatusTab()
        {
            var mgr = EOSManager.Instance;

            // SDK Status
            GUILayout.BeginVertical(_sectionBox);
            GUILayout.Label("SDK Status", _headerStyle);
            GUILayout.Space(4);

            DrawStatusRow("EOS SDK", mgr != null && mgr.IsInitialized, "Initialized", "Not Initialized");
            DrawStatusRow("Login", mgr != null && mgr.IsLoggedIn, "Logged In", "Not Logged In");
            DrawStatusRow("Epic Account", mgr != null && mgr.IsEpicAccountLoggedIn, "Connected", "Not Connected");

            if (mgr != null && mgr.IsLoggedIn && mgr.LocalProductUserId != null)
            {
                GUILayout.Space(2);
                GUILayout.BeginHorizontal();
                GUILayout.Label("PUID:", _grayLabel, GUILayout.Width(44));
                string puid = mgr.LocalProductUserId.ToString();
                GUILayout.Label(puid, _monoLabel);
                if (GUILayout.Button("Copy", _smallButton, GUILayout.Width(45)))
                {
                    GUIUtility.systemCopyBuffer = puid;
                }
                GUILayout.EndHorizontal();
            }

            if (mgr != null && mgr.IsInitialized)
            {
                GUILayout.Space(2);
                GUILayout.Label($"Network: {mgr.GetNetworkStatus()}  |  App: {mgr.GetApplicationStatus()}", _grayLabel);
            }

            GUILayout.EndVertical();

            // Platform Info
            if (DrawFoldout("Platform", ref _foldPlatform))
            {
                GUILayout.BeginVertical(_sectionBox);
                DrawKeyValue("Platform", $"{EOSPlatformHelper.CurrentPlatform} ({EOSPlatformHelper.PlatformId})");
                DrawKeyValue("Device", SystemInfo.deviceModel);
                DrawKeyValue("Overlay", EOSPlatformHelper.SupportsOverlay ? "Yes" : "No");
                DrawKeyValue("Voice", EOSPlatformHelper.SupportsVoice ? "Yes" : "No");
                GUILayout.EndVertical();
            }

            // Interfaces
            if (mgr != null && mgr.IsInitialized)
            {
                if (DrawFoldout("Interfaces", ref _foldInterfaces))
                {
                    GUILayout.BeginVertical(_sectionBox);

                    GUILayout.BeginHorizontal();
                    DrawInterfaceBadge("Connect", mgr.ConnectInterface != null);
                    DrawInterfaceBadge("P2P", mgr.P2PInterface != null);
                    DrawInterfaceBadge("Lobby", mgr.LobbyInterface != null);
                    DrawInterfaceBadge("RTC", mgr.RTCInterface != null);
                    GUILayout.EndHorizontal();

                    GUILayout.BeginHorizontal();
                    DrawInterfaceBadge("Audio", mgr.RTCAudioInterface != null);
                    DrawInterfaceBadge("Auth", mgr.AuthInterface != null);
                    DrawInterfaceBadge("Friends", mgr.FriendsInterface != null);
                    DrawInterfaceBadge("Stats", mgr.StatsInterface != null);
                    GUILayout.EndHorizontal();

                    GUILayout.BeginHorizontal();
                    DrawInterfaceBadge("Storage", mgr.PlayerDataStorageInterface != null);
                    DrawInterfaceBadge("Achieve", mgr.AchievementsInterface != null);
                    DrawInterfaceBadge("Reports", mgr.ReportsInterface != null);
                    DrawInterfaceBadge("Metrics", mgr.MetricsInterface != null);
                    GUILayout.EndHorizontal();

                    GUILayout.EndVertical();
                }
            }

            // Actions
            GUILayout.BeginVertical(_sectionBox);
            GUILayout.Label("Actions", _subHeaderStyle);
            GUILayout.Space(2);

            GUILayout.BeginHorizontal();

            bool canInit = mgr != null && !mgr.IsInitialized;
            bool canLogin = mgr != null && mgr.IsInitialized && !mgr.IsLoggedIn;
            bool canLogout = mgr != null && mgr.IsLoggedIn;

            GUI.enabled = canInit;
            if (GUILayout.Button("Initialize", _actionButton))
            {
                InitializeFromResources();
            }

            GUI.enabled = canLogin;
            if (GUILayout.Button("Device Login", _actionButton))
            {
                _ = mgr.LoginWithDeviceTokenAsync("Player");
            }

            GUI.enabled = canLogin;
            if (GUILayout.Button("Smart Login", _actionButton))
            {
                _ = mgr.LoginSmartAsync("Player");
            }

            GUI.enabled = canLogout;
            if (GUILayout.Button("Logout", _smallButton))
            {
                _ = mgr.LogoutAsync();
            }

            GUI.enabled = true;
            GUILayout.EndHorizontal();

            GUILayout.EndVertical();
        }

        private void InitializeFromResources()
        {
            var config = Resources.Load<EOSConfig>("SampleEOSConfig");
            if (config == null)
            {
                config = Resources.Load<EOSConfig>("NewEOSConfig");
            }
            if (config == null)
            {
                Debug.LogError("[EOSNativeStatusUI] No EOSConfig found in Resources.");
                return;
            }

            var mgr = EOSManager.Instance;
            if (mgr != null)
            {
                var result = mgr.Initialize(config);
                Debug.Log($"[EOSNativeStatusUI] Initialize result: {result}");
            }
        }

        #endregion

        #region Lobbies Tab

        private void DrawLobbiesTab()
        {
            var mgr = EOSManager.Instance;
            if (mgr == null || !mgr.IsLoggedIn)
            {
                GUILayout.Label("Login required to use lobbies.", _yellowLabel);
                return;
            }

            var lobbyMgr = EOSLobbyManager.Instance;

            // Current Lobby
            if (lobbyMgr != null && lobbyMgr.IsInLobby)
            {
                if (DrawFoldout("Current Lobby", ref _foldCurrentLobby))
                {
                    GUILayout.BeginVertical(_sectionBox);
                    var lobby = lobbyMgr.CurrentLobby;

                    GUILayout.BeginHorizontal();
                    GUILayout.Label("Join Code:", _grayLabel, GUILayout.Width(80));
                    GUILayout.Label(lobby.JoinCode ?? "????", _greenLabel);
                    if (GUILayout.Button("Copy", _smallButton, GUILayout.Width(45)))
                    {
                        GUIUtility.systemCopyBuffer = lobby.JoinCode ?? "";
                    }
                    GUILayout.EndHorizontal();

                    GUILayout.Label($"Role: {(lobbyMgr.IsOwner ? "HOST" : "CLIENT")}", lobbyMgr.IsOwner ? _greenLabel : _cyanLabel);
                    DrawKeyValue("Members", $"{lobby.MemberCount} / {lobby.MaxMembers}");
                    DrawKeyValue("Public", lobby.IsPublic.ToString());
                    DrawKeyValue("Voice", EOSVoiceManager.Instance?.IsVoiceEnabled == true ? "Yes" : "No");

                    string ownerShort = lobby.OwnerPuid != null && lobby.OwnerPuid.Length > 16
                        ? lobby.OwnerPuid.Substring(0, 12) + "..."
                        : lobby.OwnerPuid;
                    DrawKeyValue("Owner", ownerShort);

                    // Lobby attributes
                    if (lobby.Attributes != null && lobby.Attributes.Count > 0)
                    {
                        GUILayout.Space(2);
                        GUILayout.Label($"Attributes ({lobby.Attributes.Count}):", _grayLabel);
                        int shown = 0;
                        foreach (var kvp in lobby.Attributes)
                        {
                            if (shown++ >= 6) { GUILayout.Label($"  ... +{lobby.Attributes.Count - 6} more", _grayLabel); break; }
                            string val = kvp.Value != null && kvp.Value.Length > 30 ? kvp.Value.Substring(0, 30) + "..." : kvp.Value;
                            GUILayout.Label($"  {kvp.Key}: {val}", _monoLabel);
                        }
                    }

                    GUILayout.Space(6);
                    if (GUILayout.Button("Leave Lobby", _actionButton))
                    {
                        _ = lobbyMgr.LeaveLobbyAsync();
                        _lobbyStatus = "Left lobby.";
                    }

                    GUILayout.EndVertical();
                }
            }

            // Create Lobby
            if (lobbyMgr == null || !lobbyMgr.IsInLobby)
            {
                if (DrawFoldout("Create Lobby", ref _foldCreateLobby))
                {
                    GUILayout.BeginVertical(_sectionBox);

                    GUILayout.BeginHorizontal();
                    GUILayout.Label("Name:", _whiteLabel, GUILayout.Width(60));
                    _lobbyName = GUILayout.TextField(_lobbyName, _textFieldStyle);
                    GUILayout.EndHorizontal();

                    GUILayout.BeginHorizontal();
                    GUILayout.Label("Max:", _whiteLabel, GUILayout.Width(60));
                    int.TryParse(GUILayout.TextField(_maxPlayers.ToString(), _textFieldStyle, GUILayout.Width(40)), out _maxPlayers);
                    _maxPlayers = Mathf.Clamp(_maxPlayers, 2, 64);
                    GUILayout.Space(8);
                    _lobbyPublic = GUILayout.Toggle(_lobbyPublic, "Public", _toggleStyle, GUILayout.Width(65));
                    _lobbyVoice = GUILayout.Toggle(_lobbyVoice, "Voice", _toggleStyle, GUILayout.Width(60));
                    _lobbyHostMigration = GUILayout.Toggle(_lobbyHostMigration, "Migrate", _toggleStyle, GUILayout.Width(70));
                    GUILayout.EndHorizontal();

                    GUILayout.Space(4);
                    if (GUILayout.Button("Create Lobby", _actionButton, GUILayout.Height(28)))
                    {
                        CreateLobby(lobbyMgr);
                    }

                    GUILayout.EndVertical();
                }
            }

            // Join by Code
            if (lobbyMgr == null || !lobbyMgr.IsInLobby)
            {
                if (DrawFoldout("Join / Quick Match", ref _foldJoinLobby))
                {
                    GUILayout.BeginVertical(_sectionBox);

                    GUILayout.BeginHorizontal();
                    GUILayout.Label("Code:", _whiteLabel, GUILayout.Width(44));
                    _joinCode = GUILayout.TextField(_joinCode, 4, _textFieldStyle, GUILayout.Width(60));
                    if (GUILayout.Button("Join", _smallButton))
                    {
                        JoinByCode(lobbyMgr);
                    }
                    if (GUILayout.Button("Quick Match", _actionButton))
                    {
                        QuickMatch(lobbyMgr);
                    }
                    GUILayout.EndHorizontal();

                    GUILayout.EndVertical();
                }
            }

            // Search
            if (DrawFoldout("Search Lobbies", ref _foldSearch))
            {
                GUILayout.BeginVertical(_sectionBox);

                GUI.enabled = !_searching;
                if (GUILayout.Button(_searching ? "Searching..." : "Search All", _smallButton))
                {
                    SearchLobbies(lobbyMgr);
                }
                GUI.enabled = true;

                if (_searchResults != null)
                {
                    GUILayout.Label($"Found: {_searchResults.Count} lobbies", _searchResults.Count > 0 ? _greenLabel : _grayLabel);

                    foreach (var l in _searchResults)
                    {
                        GUILayout.BeginHorizontal();
                        string name = l.LobbyName ?? l.JoinCode ?? "???";
                        GUILayout.Label($"  [{l.JoinCode}] {name} ({l.MemberCount}/{l.MaxMembers})", _whiteLabel, GUILayout.Width(300));

                        if (lobbyMgr != null && !lobbyMgr.IsInLobby && GUILayout.Button("Join", _smallButton, GUILayout.Width(50)))
                        {
                            _ = JoinLobbyById(lobbyMgr, l.LobbyId);
                        }
                        GUILayout.EndHorizontal();
                    }
                }

                GUILayout.EndVertical();
            }

            // Lobby Members
            DrawLobbyMembersSection();

            // Lobby Chat
            DrawLobbyChatSection();

            // Status message
            if (!string.IsNullOrEmpty(_lobbyStatus))
            {
                GUILayout.Space(4);
                GUILayout.Label(_lobbyStatus, _orangeLabel);
            }
        }

        private async void CreateLobby(EOSLobbyManager lobbyMgr)
        {
            if (lobbyMgr == null) return;
            _lobbyStatus = "Creating lobby...";

            var options = new LobbyCreateOptions
            {
                MaxPlayers = (uint)_maxPlayers,
                IsPublic = _lobbyPublic,
                EnableVoice = _lobbyVoice,
                AllowHostMigration = _lobbyHostMigration,
                LobbyName = _lobbyName
            };

            var (result, lobby) = await lobbyMgr.CreateLobbyAsync(options);
            _lobbyStatus = result == Result.Success
                ? $"Created! Code: {lobby.JoinCode}"
                : $"Failed: {result}";
        }

        private async void JoinByCode(EOSLobbyManager lobbyMgr)
        {
            if (lobbyMgr == null || string.IsNullOrEmpty(_joinCode)) return;
            _lobbyStatus = $"Joining {_joinCode}...";

            var (result, lobby) = await lobbyMgr.JoinLobbyByCodeAsync(_joinCode);
            _lobbyStatus = result == Result.Success
                ? $"Joined! Code: {lobby.JoinCode}"
                : $"Failed: {result}";
        }

        private async void QuickMatch(EOSLobbyManager lobbyMgr)
        {
            if (lobbyMgr == null) return;
            _lobbyStatus = "Quick matching...";

            var options = new LobbyCreateOptions
            {
                MaxPlayers = (uint)_maxPlayers,
                IsPublic = _lobbyPublic,
                EnableVoice = _lobbyVoice,
                AllowHostMigration = _lobbyHostMigration
            };

            var (result, lobby, didHost) = await lobbyMgr.QuickMatchOrHostAsync(options);
            _lobbyStatus = result == Result.Success
                ? (didHost ? $"Hosting! Code: {lobby.JoinCode}" : $"Joined! Code: {lobby.JoinCode}")
                : $"Quick match failed: {result}";
        }

        private async void SearchLobbies(EOSLobbyManager lobbyMgr)
        {
            if (lobbyMgr == null) return;
            _searching = true;
            _lobbyStatus = "Searching...";

            var (result, lobbies) = await lobbyMgr.SearchLobbiesAsync(new LobbySearchOptions
            {
                MaxResults = 20,
                OnlyAvailable = false
            });

            _searching = false;
            _searchResults = lobbies;
            _lobbyStatus = result == Result.Success
                ? $"Found {lobbies?.Count ?? 0} lobbies."
                : $"Search failed: {result}";
        }

        private async Task JoinLobbyById(EOSLobbyManager lobbyMgr, string lobbyId)
        {
            _lobbyStatus = "Joining...";
            var (result, lobby) = await lobbyMgr.JoinLobbyByIdAsync(lobbyId);
            _lobbyStatus = result == Result.Success
                ? $"Joined! Code: {lobby.JoinCode}"
                : $"Failed: {result}";
        }

        #endregion

        #region Voice Tab

        private void DrawVoiceTab()
        {
            var mgr = EOSManager.Instance;
            if (mgr == null || !mgr.IsLoggedIn)
            {
                GUILayout.Label("Login required for voice.", _yellowLabel);
                return;
            }

            var voice = EOSVoiceManager.Instance;

            // Voice Status
            GUILayout.BeginVertical(_sectionBox);
            GUILayout.Label("Voice Status", _headerStyle);
            GUILayout.Space(4);

            if (voice == null)
            {
                GUILayout.Label("EOSVoiceManager not found.", _grayLabel);
                GUILayout.Label("Join a lobby with voice enabled.", _grayLabel);
            }
            else
            {
                DrawStatusRow("Connected", voice.IsConnected, "Connected", "Disconnected");
                DrawStatusRow("Mic", !voice.IsMuted, "Active", "Muted");
                DrawStatusRow("Voice Enabled", voice.IsVoiceEnabled, "Yes", "No");
                DrawKeyValue("Room", voice.CurrentRoomName);
                DrawKeyValue("Participants", voice.ParticipantCount.ToString());

                // Mute/Unmute
                if (voice.IsConnected)
                {
                    GUILayout.Space(4);
                    if (GUILayout.Button(voice.IsMuted ? "Unmute Mic" : "Mute Mic", _actionButton, GUILayout.Height(26)))
                    {
                        voice.ToggleMute();
                    }
                }
            }

            GUILayout.EndVertical();

            // Local Mic Level
            DrawLocalMicLevelSection();

            // Audio Devices
            if (voice != null)
            {
                if (DrawFoldout("Audio Devices", ref _foldAudioDevices))
                {
                    GUILayout.BeginVertical(_sectionBox);

                    // Refresh button
                    if (GUILayout.Button("Refresh Devices", _smallButton))
                    {
                        voice.QueryAudioDevices();
                        _selectedInputDevice = -1;
                        _selectedOutputDevice = -1;
                    }

                    GUILayout.Space(4);

                    // Input devices (Microphones)
                    GUILayout.Label("Input Device (Mic):", _subHeaderStyle);
                    if (voice.InputDevices.Count > 0)
                    {
                        for (int i = 0; i < voice.InputDevices.Count; i++)
                        {
                            var device = voice.InputDevices[i];
                            string label = device.DeviceName?.ToString() ?? $"Device {i}";
                            if (device.DefaultDevice) label += " *";

                            bool isSelected = (_selectedInputDevice == i) ||
                                              (_selectedInputDevice == -1 && device.DefaultDevice);

                            GUIStyle btnStyle = isSelected ? _tabButtonActive : _dropdownStyle;
                            if (GUILayout.Button(label, btnStyle, GUILayout.Height(22)))
                            {
                                _selectedInputDevice = i;
                                voice.SetInputDevice(device.DeviceId?.ToString());
                            }
                        }
                    }
                    else
                    {
                        GUILayout.Label("No input devices found. Press Refresh.", _grayLabel);
                    }

                    GUILayout.Space(6);

                    // Output devices (Speakers)
                    GUILayout.Label("Output Device (Speaker):", _subHeaderStyle);
                    if (voice.OutputDevices.Count > 0)
                    {
                        for (int i = 0; i < voice.OutputDevices.Count; i++)
                        {
                            var device = voice.OutputDevices[i];
                            string label = device.DeviceName?.ToString() ?? $"Device {i}";
                            if (device.DefaultDevice) label += " *";

                            bool isSelected = (_selectedOutputDevice == i) ||
                                              (_selectedOutputDevice == -1 && device.DefaultDevice);

                            GUIStyle btnStyle = isSelected ? _tabButtonActive : _dropdownStyle;
                            if (GUILayout.Button(label, btnStyle, GUILayout.Height(22)))
                            {
                                _selectedOutputDevice = i;
                                voice.SetOutputDevice(device.DeviceId?.ToString());
                            }
                        }
                    }
                    else
                    {
                        GUILayout.Label("No output devices found. Press Refresh.", _grayLabel);
                    }

                    GUILayout.EndVertical();
                }

                // Participants
                if (voice.IsConnected)
                {
                    if (DrawFoldout($"Participants ({voice.ParticipantCount})", ref _foldParticipants))
                    {
                        GUILayout.BeginVertical(_sectionBox);

                        var participants = voice.GetAllParticipants();
                        if (participants.Count > 0)
                        {
                            foreach (var puid in participants)
                            {
                                bool speaking = voice.IsSpeaking(puid);
                                var audioStatus = voice.GetParticipantAudioStatus(puid);
                                string shortPuid = puid.Length > 16 ? puid.Substring(0, 12) + "..." : puid;

                                GUILayout.BeginHorizontal();

                                // Speaking indicator with color
                                if (speaking)
                                {
                                    GUILayout.Label("\u25CF SPEAKING", _greenLabel, GUILayout.Width(90));
                                }
                                else
                                {
                                    GUILayout.Label("\u25CB silent", _grayLabel, GUILayout.Width(90));
                                }

                                GUILayout.Label(shortPuid, _monoLabel);
                                GUILayout.Label(audioStatus.ToString(), _grayLabel, GUILayout.Width(70));

                                GUILayout.EndHorizontal();

                                // Level bar for speaking participant
                                if (speaking)
                                {
                                    Rect barRect = GUILayoutUtility.GetRect(GUIContent.none, GUI.skin.box, GUILayout.Height(6));
                                    float level = 0.7f; // Visual indicator only since we don't have dB
                                    if (level > _peakLevel) { _peakLevel = level; _peakDecay = level; }
                                    DrawLevelBar(barRect, level, _peakDecay);
                                }
                            }
                        }
                        else
                        {
                            GUILayout.Label("No participants yet.", _grayLabel);
                        }

                        GUILayout.EndVertical();
                    }
                }
            }

            // Help text
            GUILayout.BeginVertical(_sectionBox);
            GUILayout.Label("Voice Info", _subHeaderStyle);
            GUILayout.Label("Voice is lobby-based. Create a lobby with Voice enabled.", _grayLabel);
            GUILayout.Label("Voice auto-connects and persists through host migration.", _grayLabel);
            GUILayout.Label("Use Refresh Devices to detect mic/speaker changes.", _grayLabel);
            GUILayout.EndVertical();
        }

        #endregion

        #region Social Tab

        private void DrawSocialTab()
        {
            var mgr = EOSManager.Instance;
            if (mgr == null || !mgr.IsLoggedIn)
            {
                GUILayout.Label("Login required for social features.", _yellowLabel);
                return;
            }

            // Player Registry summary
            GUILayout.BeginVertical(_sectionBox);
            GUILayout.Label("Player Registry", _headerStyle);
            GUILayout.Space(4);
            var registry = EOSPlayerRegistry.Instance;
            if (registry != null)
            {
                DrawKeyValue("Cached", registry.CachedPlayerCount.ToString());
                DrawKeyValue("Friends", registry.FriendCount.ToString());
                DrawKeyValue("Blocked", registry.BlockedCount.ToString());
            }
            else
            {
                GUILayout.Label("EOSPlayerRegistry not found.", _grayLabel);
            }
            GUILayout.EndVertical();

            DrawRecentlyPlayedSection();
            GUILayout.Space(4);
            DrawLocalFriendsSection();
            GUILayout.Space(4);
            DrawBlockedPlayersSection();
            GUILayout.Space(4);
            DrawInvitesSection();
            GUILayout.Space(4);
            DrawEpicAccountSection();
            GUILayout.Space(4);
            DrawEpicFriendsSection();
        }

        #endregion

        #region Stats Tab

        private void DrawStatsTab()
        {
            var mgr = EOSManager.Instance;
            if (mgr == null || !mgr.IsLoggedIn)
            {
                GUILayout.Label("Login required for stats.", _yellowLabel);
                return;
            }

            DrawStatsAndLeaderboardsSection();
            GUILayout.Space(4);
            DrawAchievementsSection();
            GUILayout.Space(4);
            DrawRankedSection();
        }

        #endregion

        #region Tools Tab

        private void DrawToolsTab()
        {
            var mgr = EOSManager.Instance;
            if (mgr == null || !mgr.IsLoggedIn)
            {
                GUILayout.Label("Login required for tools.", _yellowLabel);
                return;
            }

            DrawCloudStorageSection();
            GUILayout.Space(4);
            DrawAntiCheatSection();
            GUILayout.Space(4);
            DrawReplaysSection();
            GUILayout.Space(4);
            DrawSessionMetricsSection();
            GUILayout.Space(4);
            DrawLFGSection();
        }

        #endregion

        #region Lobby Members Section

        private void DrawLobbyMembersSection()
        {
            var lobbyMgr = EOSLobbyManager.Instance;
            if (lobbyMgr == null || !lobbyMgr.IsInLobby) return;

            if (!DrawFoldout("Lobby Members", ref _foldMembers)) return;

            GUILayout.BeginVertical(_sectionBox);

            var lobby = lobbyMgr.CurrentLobby;
            string ownerPuid = lobby.OwnerPuid;
            string localPuid = EOSManager.Instance?.LocalProductUserId?.ToString();

            var lobbyInterface = EOSManager.Instance?.LobbyInterface;
            if (lobbyInterface != null)
            {
                var detailsOptions = new Epic.OnlineServices.Lobby.CopyLobbyDetailsHandleOptions
                {
                    LocalUserId = EOSManager.Instance.LocalProductUserId,
                    LobbyId = lobby.LobbyId
                };

                if (lobbyInterface.CopyLobbyDetailsHandle(ref detailsOptions, out var details) == Result.Success && details != null)
                {
                    var countOptions = new Epic.OnlineServices.Lobby.LobbyDetailsGetMemberCountOptions();
                    uint memberCount = details.GetMemberCount(ref countOptions);

                    _memberScrollPos = GUILayout.BeginScrollView(_memberScrollPos, GUILayout.Height(Mathf.Min(100, memberCount * 24 + 10)));

                    for (uint i = 0; i < memberCount; i++)
                    {
                        var memberOptions = new Epic.OnlineServices.Lobby.LobbyDetailsGetMemberByIndexOptions { MemberIndex = i };
                        var memberId = details.GetMemberByIndex(ref memberOptions);
                        if (memberId == null) continue;

                        string memberPuid = memberId.ToString();
                        string displayName = EOSPlayerRegistry.Instance?.GetPlayerName(memberPuid)
                            ?? (memberPuid.Length > 8 ? memberPuid.Substring(0, 8) : memberPuid);

                        bool isOwner = memberPuid == ownerPuid;
                        bool isLocal = memberPuid == localPuid;

                        GUILayout.BeginHorizontal();
                        string prefix = isOwner ? "[HOST] " : "       ";
                        GUIStyle style = isLocal ? _cyanLabel : (isOwner ? _greenLabel : _whiteLabel);
                        GUILayout.Label($"{prefix}{displayName}{(isLocal ? " (you)" : "")}", style);
                        GUILayout.FlexibleSpace();

                        // Profile & Report buttons (not for self)
                        if (!isLocal)
                        {
                            // Profile button
                            if (GUILayout.Button("\u2139", _smallButton, GUILayout.Width(22)))
                            {
                                _profilePuid = memberPuid;
                                _showProfilePopup = true;
                                _profileEditingNote = false;
                                _profileStatus = "";
                                var reg = EOSPlayerRegistry.Instance;
                                _profileNote = reg?.GetNote(memberPuid) ?? "";
                            }

                            // Report button
                            var reportsManager = EOSReports.Instance;
                            if (reportsManager != null && reportsManager.IsReady)
                            {
                                if (GUILayout.Button("\u26A0", _smallButton, GUILayout.Width(22)))
                                {
                                    _reportTargetPuid = memberPuid;
                                    _showReportPopup = true;
                                    _reportStatus = "";
                                }
                            }
                        }

                        GUILayout.EndHorizontal();
                    }

                    GUILayout.EndScrollView();
                    details.Release();
                }
            }

            GUILayout.EndVertical();
        }

        #endregion

        #region Lobby Chat Section

        private void DrawLobbyChatSection()
        {
            if (!DrawFoldout("Lobby Chat", ref _foldChat)) return;

            GUILayout.BeginVertical(_sectionBox);

            var lobbyMgr = EOSLobbyManager.Instance;
            if (lobbyMgr == null || !lobbyMgr.IsInLobby)
            {
                GUILayout.Label("Join a lobby to chat.", _grayLabel);
                GUILayout.EndVertical();
                return;
            }

            var chatMgr = EOSLobbyChatManager.Instance;
            if (chatMgr == null)
            {
                GUILayout.Label("Chat manager not found.", _grayLabel);
                GUILayout.EndVertical();
                return;
            }

            // Chat log
            _chatLogBuilder.Clear();
            var messages = chatMgr.Messages;
            int startIdx = Mathf.Max(0, messages.Count - 20);
            for (int i = startIdx; i < messages.Count; i++)
            {
                var msg = messages[i];
                if (msg.IsSystem)
                    _chatLogBuilder.AppendLine($"<color=#888888>* {msg.Message}</color>");
                else
                    _chatLogBuilder.AppendLine($"<color=#666666>[{msg.LocalTime:HH:mm}]</color> <color=#66ccff>{msg.SenderName}</color>: {msg.Message}");
            }

            var richLabel = new GUIStyle(_grayLabel) { richText = true, wordWrap = true };
            _chatScrollPos = GUILayout.BeginScrollView(_chatScrollPos, GUILayout.Height(120));
            GUILayout.Label(_chatLogBuilder.ToString(), richLabel);
            GUILayout.EndScrollView();

            // Chat input
            GUILayout.BeginHorizontal();
            GUI.SetNextControlName("ChatInput");
            _chatInput = GUILayout.TextField(_chatInput, _textFieldStyle);
            if (GUILayout.Button("Send", _smallButton, GUILayout.Width(45)) ||
                (Event.current.isKey && Event.current.keyCode == KeyCode.Return && GUI.GetNameOfFocusedControl() == "ChatInput"))
            {
                if (!string.IsNullOrWhiteSpace(_chatInput))
                {
                    chatMgr.SendChatMessage(_chatInput);
                    _chatInput = "";
                    _chatScrollPos.y = float.MaxValue;
                }
            }
            GUILayout.EndHorizontal();

            GUILayout.EndVertical();
        }

        #endregion

        #region Local Mic Level Section

        private void DrawLocalMicLevelSection()
        {
            var voice = EOSVoiceManager.Instance;
            if (voice == null || !voice.IsConnected) return;

            if (!DrawFoldout("Local Microphone", ref _foldLocalMic)) return;

            GUILayout.BeginVertical(_sectionBox);

            GUILayout.BeginHorizontal();
            GUILayout.Label("Status:", _grayLabel, GUILayout.Width(60));
            GUILayout.Label(voice.IsMuted ? "MUTED" : "Active", voice.IsMuted ? _redLabel : _greenLabel);
            GUILayout.EndHorizontal();

            // Level bar
            GUILayout.BeginHorizontal();
            GUILayout.Label("Level:", _grayLabel, GUILayout.Width(60));
            float localLevel = voice.IsMuted ? 0f : 0.3f;
            if (localLevel > _peakLevel) { _peakLevel = localLevel; _peakDecay = localLevel; }
            Rect barRect = GUILayoutUtility.GetRect(180, 12);
            DrawLevelBar(barRect, localLevel, _peakDecay);
            GUILayout.Label($"{(localLevel * 100):F0}%", _grayLabel, GUILayout.Width(40));
            GUILayout.EndHorizontal();

            GUILayout.Space(4);
            if (GUILayout.Button(voice.IsMuted ? "UNMUTE MIC" : "MUTE MIC", _actionButton, GUILayout.Height(26)))
            {
                voice.ToggleMute();
            }

            GUILayout.EndVertical();
        }

        #endregion

        #region Recently Played Section

        private void DrawRecentlyPlayedSection()
        {
            var registry = EOSPlayerRegistry.Instance;
            if (registry == null) return;

            int count = registry.CachedPlayerCount;
            if (!DrawFoldout($"Recently Played ({count})", ref _foldRecent)) return;

            GUILayout.BeginVertical(_sectionBox);

            if (count == 0)
            {
                GUILayout.Label("No players discovered yet.", _grayLabel);
                GUILayout.Label("Join lobbies to build your player list!", _grayLabel);
                GUILayout.EndVertical();
                return;
            }

            var recent = registry.GetRecentPlayers(7);
            if (recent.Count == 0)
            {
                GUILayout.Label("No recent players (last 7 days).", _grayLabel);
            }
            else
            {
                GUILayout.Label($"Last 7 days ({recent.Count} players):", _grayLabel);

                int shown = 0;
                foreach (var (puid, name, lastSeen) in recent)
                {
                    if (shown >= 10) break;
                    if (registry.IsBlocked(puid)) continue;

                    GUILayout.BeginHorizontal(_sectionBox);

                    string platform = registry.GetPlatform(puid);
                    if (!string.IsNullOrEmpty(platform))
                    {
                        GUILayout.Label(EOSPlayerRegistry.GetPlatformIcon(platform), _grayLabel, GUILayout.Width(18));
                    }

                    GUILayout.Label(name, _whiteLabel, GUILayout.Width(80));
                    GUILayout.Label(GetTimeAgo(lastSeen), _grayLabel, GUILayout.Width(50));
                    GUILayout.FlexibleSpace();

                    // Friend toggle
                    bool isFriend = registry.IsFriend(puid);
                    string starIcon = isFriend ? "\u2605" : "\u2606";
                    if (GUILayout.Button(starIcon, _smallButton, GUILayout.Width(22)))
                    {
                        registry.ToggleFriend(puid);
                    }

                    // Block button
                    if (GUILayout.Button("\u26D4", _smallButton, GUILayout.Width(22)))
                    {
                        registry.BlockPlayer(puid);
                    }

                    // Invite button
                    var invitesManager = EOSCustomInvites.Instance;
                    if (invitesManager != null && invitesManager.IsReady && !string.IsNullOrEmpty(invitesManager.CurrentPayload))
                    {
                        if (GUILayout.Button("Inv", _smallButton, GUILayout.Width(28)))
                        {
                            SendInviteToPuid(puid, name);
                        }
                    }

                    GUILayout.EndHorizontal();
                    shown++;
                }

                if (recent.Count > 10)
                {
                    GUILayout.Label($"  ... and {recent.Count - 10} more", _grayLabel);
                }
            }

            GUILayout.Space(4);
            GUILayout.BeginHorizontal();
            GUILayout.Label($"Total cached: {count}", _grayLabel);
            GUILayout.FlexibleSpace();
            if (GUILayout.Button("Clear", _smallButton, GUILayout.Width(45)))
            {
                registry.ClearCache();
            }
            GUILayout.EndHorizontal();

            GUILayout.EndVertical();
        }

        #endregion

        #region Local Friends Section

        private void DrawLocalFriendsSection()
        {
            var registry = EOSPlayerRegistry.Instance;
            if (registry == null) return;

            int friendCount = registry.FriendCount;
            if (!DrawFoldout($"Local Friends ({friendCount})", ref _foldFriends)) return;

            GUILayout.BeginVertical(_sectionBox);

            if (friendCount == 0)
            {
                GUILayout.Label("No local friends yet.", _grayLabel);
                GUILayout.Label("Mark players as friends from Recently Played!", _grayLabel);
            }
            else
            {
                var friends = registry.GetFriends();
                float listHeight = Mathf.Clamp(friends.Count * 28, 60, 150);
                _friendsScrollPos = GUILayout.BeginScrollView(_friendsScrollPos, GUILayout.Height(listHeight));

                foreach (var (puid, name) in friends)
                {
                    DrawLocalFriendRow(puid, name, registry);
                }

                GUILayout.EndScrollView();
            }

            // Footer
            GUILayout.Space(4);
            GUILayout.BeginHorizontal();

            // Refresh
            if (GUILayout.Button("\u21BB", _smallButton, GUILayout.Width(25)))
            {
                _ = registry.RefreshAllFriendStatusesAsync();
            }

            // Cloud sync
            var storage = EOSPlayerDataStorage.Instance;
            bool canSync = storage != null && storage.IsReady && !registry.IsCloudSyncInProgress;
            GUI.enabled = canSync;
            string syncLabel = registry.IsCloudSyncInProgress ? "..." : "\u2601";
            if (GUILayout.Button(syncLabel, _smallButton, GUILayout.Width(25)))
            {
                _ = registry.FullCloudSyncAsync();
            }
            GUI.enabled = true;

            if (registry.LastCloudSync != DateTime.MinValue)
            {
                GUILayout.Label(GetTimeAgo(registry.LastCloudSync), _grayLabel);
            }

            GUILayout.FlexibleSpace();

            if (GUILayout.Button("Clear", _smallButton, GUILayout.Width(45)))
            {
                registry.ClearFriends();
            }

            GUILayout.EndHorizontal();
            GUILayout.EndVertical();
        }

        private void DrawLocalFriendRow(string puid, string displayName, EOSPlayerRegistry registry)
        {
            GUILayout.BeginHorizontal();

            var (status, lobbyCode) = registry.GetFriendStatusWithLobby(puid);
            string statusIcon;
            GUIStyle statusStyle;
            switch (status)
            {
                case FriendStatus.InLobby:
                    statusIcon = "\u25CF";
                    statusStyle = _greenLabel;
                    break;
                case FriendStatus.InGame:
                    statusIcon = "\u25CF";
                    statusStyle = _cyanLabel;
                    break;
                default:
                    statusIcon = "\u25CB";
                    statusStyle = _grayLabel;
                    break;
            }
            GUILayout.Label(statusIcon, statusStyle, GUILayout.Width(14));

            string platform = registry.GetPlatform(puid);
            if (!string.IsNullOrEmpty(platform))
            {
                GUILayout.Label(EOSPlayerRegistry.GetPlatformIcon(platform), _grayLabel, GUILayout.Width(18));
            }

            GUILayout.Label(displayName, _whiteLabel, GUILayout.Width(80));

            // Note or status
            string note = registry.GetNote(puid);
            bool hasNote = !string.IsNullOrEmpty(note);
            bool isEditingThis = _editingNotePuid == puid;

            if (isEditingThis)
            {
                _editingNoteText = GUILayout.TextField(_editingNoteText, 200, GUILayout.Width(80));
                if (GUILayout.Button("\u2714", _smallButton, GUILayout.Width(20)))
                {
                    registry.SetNote(puid, _editingNoteText);
                    _editingNotePuid = null;
                    _editingNoteText = "";
                }
                if (GUILayout.Button("\u2718", _smallButton, GUILayout.Width(20)))
                {
                    _editingNotePuid = null;
                    _editingNoteText = "";
                }
            }
            else
            {
                string displayText;
                if (hasNote)
                    displayText = note.Length > 8 ? note.Substring(0, 6) + ".." : note;
                else
                    displayText = status == FriendStatus.InLobby ? "Here" :
                                  status == FriendStatus.InGame ? (lobbyCode ?? "Game") : "--";

                GUIStyle textStyle = hasNote ? _cyanLabel : (status == FriendStatus.InLobby ? _greenLabel : _grayLabel);
                GUILayout.Label(displayText, textStyle, GUILayout.Width(45));

                string noteIcon = hasNote ? "\u270E" : "\u270F";
                if (GUILayout.Button(noteIcon, _smallButton, GUILayout.Width(20)))
                {
                    _editingNotePuid = puid;
                    _editingNoteText = note ?? "";
                }
            }

            GUILayout.FlexibleSpace();

            if (!isEditingThis && status == FriendStatus.InGame && !string.IsNullOrEmpty(lobbyCode))
            {
                if (GUILayout.Button("Join", _smallButton, GUILayout.Width(35)))
                {
                    JoinFriendLobby(lobbyCode);
                }
            }
            else if (!isEditingThis)
            {
                var invitesManager = EOSCustomInvites.Instance;
                if (status != FriendStatus.InLobby && invitesManager != null && invitesManager.IsReady && !string.IsNullOrEmpty(invitesManager.CurrentPayload))
                {
                    if (GUILayout.Button("Inv", _smallButton, GUILayout.Width(35)))
                    {
                        SendInviteToPuid(puid, displayName);
                    }
                }
            }

            if (!isEditingThis)
            {
                if (GUILayout.Button("\u2718", _smallButton, GUILayout.Width(22)))
                {
                    registry.RemoveFriend(puid);
                }
            }

            GUILayout.EndHorizontal();
        }

        private async void JoinFriendLobby(string lobbyCode)
        {
            var lobbyMgr = EOSLobbyManager.Instance;
            if (lobbyMgr == null) return;

            if (lobbyMgr.IsInLobby)
            {
                await lobbyMgr.LeaveLobbyAsync();
            }

            _lobbyStatus = $"Joining {lobbyCode}...";
            var (result, lobby) = await lobbyMgr.JoinLobbyByCodeAsync(lobbyCode);
            _lobbyStatus = result == Result.Success
                ? $"Joined! Code: {lobby.JoinCode}"
                : $"Failed: {result}";
        }

        #endregion

        #region Blocked Players Section

        private void DrawBlockedPlayersSection()
        {
            var registry = EOSPlayerRegistry.Instance;
            if (registry == null) return;

            int blockedCount = registry.BlockedCount;
            if (blockedCount == 0) return;

            if (!DrawFoldout($"Blocked ({blockedCount})", ref _foldBlocked)) return;

            GUILayout.BeginVertical(_sectionBox);

            var blocked = registry.GetBlockedPlayers();
            float listHeight = Mathf.Clamp(blocked.Count * 24, 50, 100);
            _blockedScrollPos = GUILayout.BeginScrollView(_blockedScrollPos, GUILayout.Height(listHeight));

            foreach (var (puid, name) in blocked)
            {
                GUILayout.BeginHorizontal();
                GUILayout.Label("\u26D4", _redLabel, GUILayout.Width(18));
                GUILayout.Label(name, _whiteLabel, GUILayout.Width(130));
                GUILayout.FlexibleSpace();

                if (GUILayout.Button("Unblock", _smallButton, GUILayout.Width(55)))
                {
                    registry.UnblockPlayer(puid);
                }

                GUILayout.EndHorizontal();
            }

            GUILayout.EndScrollView();

            GUILayout.BeginHorizontal();
            GUILayout.FlexibleSpace();
            if (GUILayout.Button("Clear All", _smallButton, GUILayout.Width(60)))
            {
                registry.ClearBlocked();
            }
            GUILayout.EndHorizontal();

            GUILayout.EndVertical();
        }

        #endregion

        #region Invites Section

        private void DrawInvitesSection()
        {
            var invitesManager = EOSCustomInvites.Instance;
            if (invitesManager == null) return;

            int pendingCount = invitesManager.PendingInvites.Count + invitesManager.PendingRequests.Count;
            string title = pendingCount > 0 ? $"Invites ({pendingCount} pending)" : "Invites";

            if (!DrawFoldout(title, ref _foldInvites)) return;

            GUILayout.BeginVertical(_sectionBox);

            if (!invitesManager.IsReady)
            {
                GUILayout.Label("Waiting for EOS login...", _grayLabel);
                GUILayout.EndVertical();
                return;
            }

            var lobbyMgr = EOSLobbyManager.Instance;
            bool isInLobby = lobbyMgr != null && lobbyMgr.IsInLobby;

            // Current payload
            GUILayout.BeginHorizontal();
            GUILayout.Label("Payload:", _grayLabel, GUILayout.Width(55));
            string payload = invitesManager.CurrentPayload;
            GUILayout.Label(string.IsNullOrEmpty(payload) ? "(not set)" : payload, string.IsNullOrEmpty(payload) ? _grayLabel : _cyanLabel);
            GUILayout.FlexibleSpace();
            GUILayout.EndHorizontal();

            if (isInLobby)
            {
                if (GUILayout.Button("Set Lobby Code as Payload", _smallButton))
                {
                    invitesManager.SetLobbyPayload();
                    _inviteStatus = "Payload set to lobby code";
                }
            }

            GUILayout.Space(6);

            // Send invite
            GUILayout.Label("Send Invite", _subHeaderStyle);
            GUILayout.BeginHorizontal();
            GUILayout.Label("To:", _grayLabel, GUILayout.Width(25));
            _inviteRecipientPuid = GUILayout.TextField(_inviteRecipientPuid, _textFieldStyle);
            GUILayout.EndHorizontal();

            GUI.enabled = !string.IsNullOrWhiteSpace(_inviteRecipientPuid) && !string.IsNullOrEmpty(payload);
            if (GUILayout.Button("SEND INVITE", _smallButton))
            {
                SendInviteToRecipient();
            }
            GUI.enabled = true;

            if (string.IsNullOrEmpty(payload))
            {
                GUILayout.Label("Set payload first (lobby code)", _grayLabel);
            }

            // Quick Send to Friends
            var registry = EOSPlayerRegistry.Instance;
            if (registry != null && registry.FriendCount > 0 && !string.IsNullOrEmpty(payload))
            {
                GUILayout.Space(6);
                GUILayout.Label("Quick Send to Friends:", _grayLabel);
                GUILayout.BeginHorizontal();
                int shown = 0;
                foreach (var (puid, name) in registry.GetFriends())
                {
                    if (shown >= 4) break;
                    string buttonText = name.Length > 12 ? name.Substring(0, 10) + ".." : name;
                    if (GUILayout.Button(buttonText, _smallButton, GUILayout.Width(75)))
                    {
                        SendInviteToPuid(puid, name);
                    }
                    shown++;
                }
                GUILayout.FlexibleSpace();
                GUILayout.EndHorizontal();
            }

            // Pending received invites
            if (invitesManager.PendingInvites.Count > 0)
            {
                GUILayout.Space(8);
                GUILayout.Label($"Received Invites ({invitesManager.PendingInvites.Count})", _subHeaderStyle);
                foreach (var kvp in invitesManager.PendingInvites)
                {
                    var invite = kvp.Value;
                    GUILayout.BeginVertical(_sectionBox);
                    string shortSender = invite.SenderId?.ToString();
                    if (shortSender?.Length > 16) shortSender = shortSender.Substring(0, 8) + "...";
                    GUILayout.Label($"From: {shortSender}", _grayLabel);
                    if (!string.IsNullOrEmpty(invite.Payload))
                        GUILayout.Label($"Code: {invite.Payload}", _cyanLabel);
                    GUILayout.Label(GetTimeAgo(invite.ReceivedTime), _grayLabel);

                    GUILayout.BeginHorizontal();
                    if (GUILayout.Button("Accept", _smallButton))
                    {
                        AcceptInviteAndJoin(kvp.Key, invite);
                    }
                    if (GUILayout.Button("Reject", _smallButton))
                    {
                        invitesManager.RejectInvite(kvp.Key);
                    }
                    GUILayout.EndHorizontal();
                    GUILayout.EndVertical();
                }
            }

            // Pending join requests
            if (invitesManager.PendingRequests.Count > 0)
            {
                GUILayout.Space(8);
                GUILayout.Label($"Join Requests ({invitesManager.PendingRequests.Count})", _subHeaderStyle);
                foreach (var kvp in invitesManager.PendingRequests)
                {
                    var request = kvp.Value;
                    GUILayout.BeginVertical(_sectionBox);
                    string shortFrom = request.FromUserId?.ToString();
                    if (shortFrom?.Length > 16) shortFrom = shortFrom.Substring(0, 8) + "...";
                    GUILayout.Label($"From: {shortFrom}", _grayLabel);
                    GUILayout.Label(GetTimeAgo(request.ReceivedTime), _grayLabel);

                    GUILayout.BeginHorizontal();
                    if (GUILayout.Button("Accept", _smallButton))
                    {
                        _ = invitesManager.AcceptRequestToJoinAsync(request.FromUserId);
                    }
                    if (GUILayout.Button("Reject", _smallButton))
                    {
                        _ = invitesManager.RejectRequestToJoinAsync(request.FromUserId);
                    }
                    GUILayout.EndHorizontal();
                    GUILayout.EndVertical();
                }
            }

            if (!string.IsNullOrEmpty(_inviteStatus))
            {
                GUILayout.Space(4);
                GUILayout.Label(_inviteStatus, _orangeLabel);
            }

            GUILayout.EndVertical();
        }

        private async void SendInviteToRecipient()
        {
            var invitesManager = EOSCustomInvites.Instance;
            if (invitesManager == null) return;
            _inviteStatus = "Sending...";
            var result = await invitesManager.SendInviteAsync(_inviteRecipientPuid.Trim());
            _inviteStatus = result == Result.Success ? "Invite sent!" : $"Failed: {result}";
            if (result == Result.Success) _inviteRecipientPuid = "";
        }

        private async void SendInviteToPuid(string puid, string displayName)
        {
            var invitesManager = EOSCustomInvites.Instance;
            if (invitesManager == null || !invitesManager.IsReady) return;
            _inviteStatus = $"Sending to {displayName}...";
            var result = await invitesManager.SendInviteAsync(puid);
            _inviteStatus = result == Result.Success ? $"Sent to {displayName}!" : $"Failed: {result}";
        }

        private async void AcceptInviteAndJoin(string inviteId, InviteData invite)
        {
            var invitesManager = EOSCustomInvites.Instance;
            invitesManager?.AcceptInvite(inviteId);

            if (invite.TryGetLobbyCode(out string lobbyCode))
            {
                _inviteStatus = $"Joining {lobbyCode}...";
                var lobbyMgr = EOSLobbyManager.Instance;
                if (lobbyMgr != null)
                {
                    var (result, lobby) = await lobbyMgr.JoinLobbyByCodeAsync(lobbyCode);
                    _inviteStatus = result == Result.Success ? $"Joined: {lobby.JoinCode}" : $"Join failed: {result}";
                }
            }
            else
            {
                _inviteStatus = "Invite accepted (no lobby code)";
            }
        }

        #endregion

        #region Epic Account Section

        private void DrawEpicAccountSection()
        {
            var mgr = EOSManager.Instance;
            if (!DrawFoldout("Epic Account", ref _foldEpicAccount)) return;

            GUILayout.BeginVertical(_sectionBox);

            if (mgr.IsEpicAccountLoggedIn)
            {
                GUILayout.Label("Connected to Epic Account", _greenLabel);
                GUILayout.Label($"EpicAccountId: {mgr.LocalEpicAccountId}", _monoLabel);
                GUILayout.Space(4);
                if (GUILayout.Button("Logout Epic Account", _smallButton))
                {
                    _ = mgr.LogoutEpicAccountAsync();
                }
            }
            else
            {
                GUILayout.Label("Not connected to Epic Account.", _grayLabel);
                GUILayout.Label("Enables: Friends, Presence, Achievements", _grayLabel);
                GUILayout.Space(4);
                if (GUILayout.Button("Login with Epic", _actionButton))
                {
                    _ = mgr.LoginWithEpicAccountAsync();
                }
            }

            GUILayout.EndVertical();
        }

        #endregion

        #region Epic Friends Section

        private void DrawEpicFriendsSection()
        {
            var friendsManager = EOSFriends.Instance;
            if (friendsManager == null) return;

            var eosManager = EOSManager.Instance;
            if (eosManager == null || !eosManager.IsEpicAccountLoggedIn) return;

            string title = friendsManager.IsReady
                ? $"Epic Friends ({friendsManager.FriendCount})"
                : "Epic Friends";

            if (!DrawFoldout(title, ref _foldEpicFriends)) return;

            GUILayout.BeginVertical(_sectionBox);

            if (!friendsManager.IsReady)
            {
                GUILayout.Label("Initializing...", _grayLabel);
                GUILayout.EndVertical();
                return;
            }

            if (GUILayout.Button("Refresh", _smallButton))
            {
                _ = friendsManager.QueryFriendsAsync();
            }

            GUILayout.Space(4);

            if (friendsManager.FriendCount == 0)
            {
                GUILayout.Label("No Epic friends found.", _grayLabel);
            }
            else
            {
                float listHeight = Mathf.Clamp(friendsManager.FriendCount * 28, 60, 120);
                _epicFriendsScrollPos = GUILayout.BeginScrollView(_epicFriendsScrollPos, GUILayout.Height(listHeight));

                foreach (var friend in friendsManager.Friends)
                {
                    GUILayout.BeginHorizontal();

                    string statusIcon = friend.Status switch
                    {
                        Epic.OnlineServices.Friends.FriendsStatus.Friends => "\u2714",
                        Epic.OnlineServices.Friends.FriendsStatus.InviteSent => "\u27A1",
                        Epic.OnlineServices.Friends.FriendsStatus.InviteReceived => "\u2709",
                        _ => "\u2022"
                    };
                    GUIStyle statusStyle = friend.Status switch
                    {
                        Epic.OnlineServices.Friends.FriendsStatus.Friends => _greenLabel,
                        Epic.OnlineServices.Friends.FriendsStatus.InviteSent => _yellowLabel,
                        Epic.OnlineServices.Friends.FriendsStatus.InviteReceived => _cyanLabel,
                        _ => _grayLabel
                    };
                    GUILayout.Label(statusIcon, statusStyle, GUILayout.Width(18));

                    string displayText = !string.IsNullOrEmpty(friend.DisplayName) ? friend.DisplayName
                        : (friend.AccountId?.ToString()?.Length > 16 ? friend.AccountId.ToString().Substring(0, 8) + "..." : friend.AccountId?.ToString() ?? "?");
                    GUILayout.Label(displayText, _whiteLabel);
                    GUILayout.FlexibleSpace();

                    if (friend.Status == Epic.OnlineServices.Friends.FriendsStatus.InviteReceived)
                    {
                        if (GUILayout.Button("\u2714", _smallButton, GUILayout.Width(25)))
                        {
                            _ = friendsManager.AcceptInviteAsync(friend.AccountId);
                        }
                        if (GUILayout.Button("\u2718", _smallButton, GUILayout.Width(25)))
                        {
                            _ = friendsManager.RejectInviteAsync(friend.AccountId);
                        }
                    }
                    else if (friend.Status == Epic.OnlineServices.Friends.FriendsStatus.Friends)
                    {
                        GUILayout.Label("Friend", _grayLabel, GUILayout.Width(40));
                    }

                    GUILayout.EndHorizontal();
                }

                GUILayout.EndScrollView();
            }

            GUILayout.EndVertical();
        }

        #endregion

        #region Stats & Leaderboards Section

        private void DrawStatsAndLeaderboardsSection()
        {
            var statsManager = EOSStats.Instance;
            var leaderboardsManager = EOSLeaderboards.Instance;

            int statsCount = statsManager?.CachedStatsCount ?? 0;
            int leaderboardCount = leaderboardsManager?.DefinitionCount ?? 0;
            string title = $"Stats & Leaderboards ({statsCount} stats, {leaderboardCount} boards)";

            if (!DrawFoldout(title, ref _foldStats)) return;

            GUILayout.BeginVertical(_sectionBox);

            bool statsReady = statsManager != null && statsManager.IsReady;
            bool leaderboardsReady = leaderboardsManager != null && leaderboardsManager.IsReady;

            if (!statsReady && !leaderboardsReady)
            {
                GUILayout.Label("Waiting for EOS login...", _grayLabel);
                GUILayout.Label("Stats require Developer Portal config.", _grayLabel);
                GUILayout.EndVertical();
                return;
            }

            // Stats
            if (statsReady)
            {
                GUILayout.Label("My Stats", _subHeaderStyle);

                if (GUILayout.Button("Query My Stats", _smallButton))
                {
                    _ = statsManager.QueryMyStatsAsync();
                }

                if (statsManager.CachedStatsCount > 0)
                {
                    _statsScrollPos = GUILayout.BeginScrollView(_statsScrollPos, GUILayout.Height(80));
                    foreach (var kvp in statsManager.CachedStats)
                    {
                        GUILayout.BeginHorizontal();
                        GUILayout.Label(kvp.Key, _grayLabel, GUILayout.Width(100));
                        GUILayout.Label(kvp.Value.Value.ToString(), _whiteLabel);
                        GUILayout.FlexibleSpace();
                        GUILayout.EndHorizontal();
                    }
                    GUILayout.EndScrollView();
                }
                else
                {
                    GUILayout.Label("No stats cached. Click Query.", _grayLabel);
                }

                // Test stat ingest
                GUILayout.Space(4);
                GUILayout.Label("Ingest Test Stat", _grayLabel);
                GUILayout.BeginHorizontal();
                _testStatName = GUILayout.TextField(_testStatName, _textFieldStyle, GUILayout.Width(100));
                if (int.TryParse(GUILayout.TextField(_testStatAmount.ToString(), _textFieldStyle, GUILayout.Width(40)), out int amt))
                    _testStatAmount = amt;
                if (GUILayout.Button("+", _smallButton, GUILayout.Width(25)))
                {
                    _ = statsManager.IngestStatAsync(_testStatName, _testStatAmount);
                }
                GUILayout.EndHorizontal();
            }

            // Leaderboards
            if (leaderboardsReady)
            {
                GUILayout.Space(6);
                GUILayout.Label("Leaderboards", _subHeaderStyle);

                if (GUILayout.Button("Refresh Definitions", _smallButton))
                {
                    _ = leaderboardsManager.QueryDefinitionsAsync();
                }

                if (leaderboardsManager.DefinitionCount > 0)
                {
                    GUILayout.Label("Select leaderboard:", _grayLabel);
                    foreach (var def in leaderboardsManager.Definitions)
                    {
                        GUILayout.BeginHorizontal();
                        bool isSelected = _selectedLeaderboardId == def.LeaderboardId;
                        if (GUILayout.Button(def.LeaderboardId, isSelected ? _actionButton : _smallButton))
                        {
                            _selectedLeaderboardId = def.LeaderboardId;
                            QuerySelectedLeaderboard();
                        }
                        GUILayout.Label($"({def.StatName})", _grayLabel);
                        GUILayout.FlexibleSpace();
                        GUILayout.EndHorizontal();
                    }

                    if (_currentLeaderboardEntries.Count > 0)
                    {
                        GUILayout.Space(4);
                        GUILayout.Label($"Top {_currentLeaderboardEntries.Count}", _grayLabel);
                        _leaderboardScrollPos = GUILayout.BeginScrollView(_leaderboardScrollPos, GUILayout.Height(100));
                        foreach (var entry in _currentLeaderboardEntries)
                        {
                            GUILayout.BeginHorizontal();
                            GUILayout.Label($"#{entry.Rank}", _grayLabel, GUILayout.Width(30));
                            GUILayout.Label(entry.DisplayName ?? entry.ShortUserId, _whiteLabel, GUILayout.Width(100));
                            GUILayout.Label(entry.Score.ToString(), _cyanLabel);
                            GUILayout.FlexibleSpace();
                            GUILayout.EndHorizontal();
                        }
                        GUILayout.EndScrollView();
                    }
                }
                else
                {
                    GUILayout.Label("No leaderboards configured in Portal.", _grayLabel);
                }
            }

            GUILayout.EndVertical();
        }

        private async void QuerySelectedLeaderboard()
        {
            var leaderboardsManager = EOSLeaderboards.Instance;
            if (string.IsNullOrEmpty(_selectedLeaderboardId) || leaderboardsManager == null) return;
            var (result, entries) = await leaderboardsManager.QueryRanksAsync(_selectedLeaderboardId, 10);
            if (result == Result.Success && entries != null)
                _currentLeaderboardEntries = entries;
        }

        #endregion

        #region Achievements Section

        private void DrawAchievementsSection()
        {
            var achievementsManager = EOSAchievements.Instance;

            string title = achievementsManager != null && achievementsManager.IsReady
                ? $"Achievements ({achievementsManager.UnlockedCount}/{achievementsManager.TotalAchievements})"
                : "Achievements";

            if (!DrawFoldout(title, ref _foldAchievements)) return;

            GUILayout.BeginVertical(_sectionBox);

            if (achievementsManager == null || !achievementsManager.IsReady)
            {
                GUILayout.Label("Waiting for EOS login...", _grayLabel);
                GUILayout.Label("Achievements require Developer Portal config.", _grayLabel);
                GUILayout.EndVertical();
                return;
            }

            if (GUILayout.Button("Refresh Achievements", _smallButton))
            {
                _ = achievementsManager.RefreshAsync();
            }

            if (achievementsManager.TotalAchievements == 0)
            {
                GUILayout.Label("No achievements configured in Portal.", _grayLabel);
                GUILayout.EndVertical();
                return;
            }

            _achievementsScrollPos = GUILayout.BeginScrollView(_achievementsScrollPos, GUILayout.Height(120));

            foreach (var def in achievementsManager.Definitions)
            {
                var playerAch = achievementsManager.GetPlayerAchievement(def.Id);
                bool unlocked = playerAch?.IsUnlocked ?? false;
                float progress = (float)(playerAch?.Progress ?? 0);

                GUILayout.BeginHorizontal();
                string icon = unlocked ? "\u2714" : "\u25CB";
                GUILayout.Label(icon, unlocked ? _greenLabel : _grayLabel, GUILayout.Width(18));
                GUILayout.Label(def.DisplayName ?? def.Id, unlocked ? _greenLabel : _whiteLabel, GUILayout.Width(120));

                if (unlocked)
                {
                    var unlockTime = playerAch?.UnlockDateTime;
                    GUILayout.Label(unlockTime?.ToString("MM/dd/yy") ?? "Unlocked", _grayLabel);
                }
                else if (progress > 0)
                {
                    GUILayout.Label($"{progress * 100:F0}%", _yellowLabel);
                }

                GUILayout.FlexibleSpace();
                GUILayout.EndHorizontal();
            }

            GUILayout.EndScrollView();
            GUILayout.EndVertical();
        }

        #endregion

        #region Ranked Section

        private void DrawRankedSection()
        {
            var rankedManager = EOSRankedMatchmaking.Instance;

            string title = "Ranked";
            if (rankedManager != null && rankedManager.IsDataLoaded)
            {
                string rankDisplay = rankedManager.GetCurrentRankDisplayName();
                title = $"Ranked ({rankDisplay})";
            }

            if (!DrawFoldout(title, ref _foldRanked)) return;

            GUILayout.BeginVertical(_sectionBox);

            if (rankedManager == null || !rankedManager.IsDataLoaded)
            {
                GUILayout.Label("Loading ranked data...", _grayLabel);
                GUILayout.EndVertical();
                return;
            }

            var playerData = rankedManager.PlayerData;

            DrawKeyValue("Rating", $"{playerData.Rating} (Peak: {playerData.PeakRating})");

            GUILayout.BeginHorizontal();
            GUILayout.Label("Rank:", _grayLabel, GUILayout.Width(90));
            string rankName = rankedManager.GetCurrentRankDisplayName();
            GUIStyle rankStyle = rankedManager.CurrentTier switch
            {
                RankTier.Grandmaster or RankTier.Master or RankTier.Champion => _orangeLabel,
                RankTier.Diamond or RankTier.Platinum => _cyanLabel,
                RankTier.Gold => _yellowLabel,
                _ => _whiteLabel
            };
            GUILayout.Label(rankName, rankStyle);
            GUILayout.EndHorizontal();

            GUILayout.BeginHorizontal();
            GUILayout.Label("Record:", _grayLabel, GUILayout.Width(90));
            GUILayout.Label($"{playerData.Wins}W", _greenLabel);
            GUILayout.Label(" - ", _grayLabel);
            GUILayout.Label($"{playerData.Losses}L", _redLabel);
            if (playerData.GamesPlayed > 0)
            {
                float winRate = playerData.WinRate;
                GUILayout.Label($" ({winRate:F0}%)", winRate >= 50 ? _greenLabel : _redLabel);
            }
            GUILayout.FlexibleSpace();
            GUILayout.EndHorizontal();

            if (playerData.WinStreak >= 2 || playerData.LossStreak >= 2)
            {
                GUILayout.BeginHorizontal();
                GUILayout.Label("Streak:", _grayLabel, GUILayout.Width(90));
                if (playerData.WinStreak >= 2)
                    GUILayout.Label($"{playerData.WinStreak} wins", _greenLabel);
                else if (playerData.LossStreak >= 2)
                    GUILayout.Label($"{playerData.LossStreak} losses", _redLabel);
                GUILayout.FlexibleSpace();
                GUILayout.EndHorizontal();
            }

            GUILayout.Space(6);

            // Matchmaking controls
            var lobbyMgr = EOSLobbyManager.Instance;
            bool isInLobby = lobbyMgr != null && lobbyMgr.IsInLobby;
            bool isInQueue = rankedManager.IsInQueue;

            if (!isInLobby && !isInQueue)
            {
                GUILayout.BeginHorizontal();
                GUILayout.Label("Mode:", _grayLabel, GUILayout.Width(40));
                _rankedGameMode = GUILayout.TextField(_rankedGameMode, _textFieldStyle, GUILayout.Width(80));
                GUILayout.FlexibleSpace();
                GUILayout.EndHorizontal();

                GUILayout.BeginHorizontal();
                if (GUILayout.Button("Find Match", _actionButton))
                {
                    FindRankedMatchAsync(rankedManager);
                }
                if (GUILayout.Button("Host Ranked", _actionButton))
                {
                    HostRankedLobbyAsync(rankedManager);
                }
                GUILayout.EndHorizontal();

                if (GUILayout.Button("Find or Host", _actionButton))
                {
                    FindOrHostRankedAsync(rankedManager);
                }
            }
            else if (isInQueue)
            {
                GUILayout.BeginHorizontal();
                GUILayout.Label("Queue:", _grayLabel, GUILayout.Width(50));
                GUILayout.Label($"Searching... ({rankedManager.QueueTime:F0}s)", _yellowLabel);
                GUILayout.FlexibleSpace();
                GUILayout.EndHorizontal();

                if (GUILayout.Button("Leave Queue", _actionButton))
                {
                    rankedManager.LeaveQueue();
                    _rankedStatus = "Left queue";
                }
            }
            else
            {
                GUILayout.Label("In lobby - leave to find new match", _grayLabel);
            }

            if (!string.IsNullOrEmpty(_rankedStatus))
            {
                GUILayout.Label(_rankedStatus, _grayLabel);
            }

            GUILayout.EndVertical();
        }

        private async void FindRankedMatchAsync(EOSRankedMatchmaking mgr)
        {
            _rankedStatus = "Searching...";
            var (result, lobby) = await mgr.FindRankedMatchAsync(_rankedGameMode);
            _rankedStatus = result == Result.Success && lobby.HasValue ? $"Joined: {lobby.Value.JoinCode}" : $"No match ({result})";
        }

        private async void HostRankedLobbyAsync(EOSRankedMatchmaking mgr)
        {
            _rankedStatus = "Hosting...";
            var (result, lobby) = await mgr.HostRankedLobbyAsync(_rankedGameMode);
            _rankedStatus = result == Result.Success && lobby.HasValue ? $"Hosted: {lobby.Value.JoinCode}" : $"Failed ({result})";
        }

        private async void FindOrHostRankedAsync(EOSRankedMatchmaking mgr)
        {
            _rankedStatus = "Finding or hosting...";
            var (result, lobby, didHost) = await mgr.FindOrHostRankedMatchAsync(_rankedGameMode);
            _rankedStatus = result == Result.Success && lobby.HasValue
                ? (didHost ? $"Hosted: {lobby.Value.JoinCode}" : $"Joined: {lobby.Value.JoinCode}")
                : $"Failed ({result})";
        }

        #endregion

        #region Cloud Storage Section

        private void DrawCloudStorageSection()
        {
            var storageManager = EOSPlayerDataStorage.Instance;

            int fileCount = storageManager?.Files?.Count ?? 0;
            string title = $"Cloud Storage ({fileCount} files)";

            if (!DrawFoldout(title, ref _foldStorage)) return;

            GUILayout.BeginVertical(_sectionBox);

            if (storageManager == null || !storageManager.IsReady)
            {
                GUILayout.Label("Waiting for EOS login...", _grayLabel);
                GUILayout.EndVertical();
                return;
            }

            long used = storageManager.GetTotalStorageUsed();
            GUILayout.Label($"Usage: {EOSPlayerDataStorage.FormatBytes(used)} / 400 MB", _grayLabel);

            if (GUILayout.Button("Refresh File List", _smallButton))
            {
                _ = storageManager.QueryFileListAsync();
            }

            if (storageManager.Files.Count > 0)
            {
                _storageScrollPos = GUILayout.BeginScrollView(_storageScrollPos, GUILayout.Height(80));
                foreach (var file in storageManager.Files)
                {
                    GUILayout.BeginHorizontal();
                    GUILayout.Label(file.Filename, _whiteLabel, GUILayout.Width(120));
                    GUILayout.Label(EOSPlayerDataStorage.FormatBytes((long)file.FileSizeBytes), _grayLabel);
                    if (GUILayout.Button("\u2715", _smallButton, GUILayout.Width(22)))
                    {
                        _ = storageManager.DeleteFileAsync(file.Filename);
                    }
                    GUILayout.FlexibleSpace();
                    GUILayout.EndHorizontal();
                }
                GUILayout.EndScrollView();
            }
            else
            {
                GUILayout.Label("No cloud files.", _grayLabel);
            }

            // Test write
            GUILayout.Space(4);
            GUILayout.Label("Test Write", _grayLabel);
            GUILayout.BeginHorizontal();
            _testFileName = GUILayout.TextField(_testFileName, _textFieldStyle, GUILayout.Width(80));
            _testFileContent = GUILayout.TextField(_testFileContent, _textFieldStyle, GUILayout.Width(100));
            if (GUILayout.Button("Write", _smallButton, GUILayout.Width(45)))
            {
                _ = storageManager.WriteFileAsync(_testFileName, _testFileContent);
            }
            GUILayout.EndHorizontal();

            GUILayout.EndVertical();
        }

        #endregion

        #region Anti-Cheat Section

        private void DrawAntiCheatSection()
        {
            var antiCheatManager = EOSAntiCheatManager.Instance;

            string title = "Anti-Cheat";
            if (antiCheatManager != null)
            {
                title = antiCheatManager.Status switch
                {
                    AntiCheatStatus.Protected => "\u2713 Anti-Cheat (Protected)",
                    AntiCheatStatus.Violated => "\u26A0 Anti-Cheat (VIOLATION)",
                    AntiCheatStatus.Error => "\u2717 Anti-Cheat (Error)",
                    _ => "Anti-Cheat (N/A)"
                };
            }

            if (!DrawFoldout(title, ref _foldAntiCheat)) return;

            GUILayout.BeginVertical(_sectionBox);

            if (antiCheatManager == null || !antiCheatManager.IsReady)
            {
                GUILayout.Label("Anti-cheat not available.", _grayLabel);
                GUILayout.Label("Configure EAC in EOS Developer Portal.", _grayLabel);
                GUILayout.EndVertical();
                return;
            }

            DrawKeyValue("Status", antiCheatManager.Status.ToString());
            DrawKeyValue("Session", antiCheatManager.IsSessionActive ? "Active" : "Inactive");
            if (antiCheatManager.IsSessionActive)
            {
                DrawKeyValue("Peers", antiCheatManager.RegisteredPeerCount.ToString());
            }

            GUILayout.BeginHorizontal();
            GUILayout.Label("Auto-Start:", _grayLabel, GUILayout.Width(90));
            bool autoStart = GUILayout.Toggle(antiCheatManager.AutoStartSession,
                antiCheatManager.AutoStartSession ? "ON" : "OFF", _toggleStyle);
            if (autoStart != antiCheatManager.AutoStartSession)
                antiCheatManager.AutoStartSession = autoStart;
            GUILayout.FlexibleSpace();
            GUILayout.EndHorizontal();

            GUILayout.Space(4);
            GUILayout.BeginHorizontal();
            GUI.enabled = !antiCheatManager.IsSessionActive;
            if (GUILayout.Button("Begin Session", _smallButton))
                antiCheatManager.BeginSession();
            GUI.enabled = antiCheatManager.IsSessionActive;
            if (GUILayout.Button("End Session", _smallButton))
                antiCheatManager.EndSession();
            GUI.enabled = true;
            GUILayout.EndHorizontal();

            GUILayout.EndVertical();
        }

        #endregion

        #region Replays Section

        private void DrawReplaysSection()
        {
            var replayPlayer = EOSReplayPlayer.Instance;
            var replayStorage = EOSReplayStorage.Instance;
            var replayViewer = EOSReplayViewer.Instance;

            string title = "Replays";
            if (replayViewer != null && replayViewer.IsViewing)
                title = "\u25B6 Replays (Viewing)";
            else if (replayStorage != null)
                title = $"Replays ({replayStorage.LocalReplayCount})";

            if (!DrawFoldout(title, ref _foldReplays)) return;

            GUILayout.BeginVertical(_sectionBox);

            // Playback controls (when viewing)
            if (replayViewer != null && replayViewer.IsViewing)
            {
                GUILayout.Label("NOW PLAYING", _subHeaderStyle);
                var header = replayViewer.CurrentReplay;
                if (header.HasValue)
                {
                    GUILayout.Label($"{header.Value.GameMode} on {header.Value.MapName}", _whiteLabel);
                    GUILayout.Label($"{header.Value.Participants?.Length ?? 0} players", _grayLabel);
                }

                // Timeline
                GUILayout.BeginHorizontal();
                GUILayout.Label($"{EOSReplayStorage.FormatDuration(replayViewer.CurrentTime)}", _grayLabel, GUILayout.Width(45));
                float progress = replayViewer.Duration > 0 ? replayViewer.CurrentTime / replayViewer.Duration : 0f;
                float newProgress = GUILayout.HorizontalSlider(progress, 0f, 1f);
                if (Mathf.Abs(newProgress - progress) > 0.01f)
                    replayViewer.SeekPercent(newProgress);
                GUILayout.Label($"{EOSReplayStorage.FormatDuration(replayViewer.Duration)}", _grayLabel, GUILayout.Width(45));
                GUILayout.EndHorizontal();

                // Controls
                GUILayout.BeginHorizontal();
                if (GUILayout.Button("<<", _smallButton, GUILayout.Width(30)))
                    replayViewer.Skip(-10f);
                string playPauseIcon = replayViewer.PlaybackState == PlaybackState.Playing ? "||" : "\u25B6";
                if (GUILayout.Button(playPauseIcon, _smallButton, GUILayout.Width(30)))
                    replayViewer.TogglePlayPause();
                if (GUILayout.Button(">>", _smallButton, GUILayout.Width(30)))
                    replayViewer.Skip(10f);
                GUILayout.Space(10);
                if (GUILayout.Button($"{replayViewer.PlaybackSpeed:F1}x", _smallButton, GUILayout.Width(40)))
                    replayViewer.CycleSpeed();
                GUILayout.FlexibleSpace();
                if (GUILayout.Button("Stop", _smallButton, GUILayout.Width(40)))
                    replayViewer.StopViewing();
                GUILayout.EndHorizontal();

                // Target
                GUILayout.BeginHorizontal();
                GUILayout.Label("Viewing:", _grayLabel, GUILayout.Width(50));
                GUILayout.Label(replayViewer.GetCurrentTargetName(), _whiteLabel);
                if (GUILayout.Button("<", _smallButton, GUILayout.Width(25)))
                    replayViewer.CycleTarget(-1);
                if (GUILayout.Button(">", _smallButton, GUILayout.Width(25)))
                    replayViewer.CycleTarget(1);
                GUILayout.EndHorizontal();
            }

            // Replay list
            GUILayout.Space(6);

            if (replayStorage != null && Time.time - _lastReplayRefresh > 5f)
            {
                _cachedReplays = replayStorage.GetLocalReplays();
                _lastReplayRefresh = Time.time;
            }

            if (GUILayout.Button("Refresh List", _smallButton))
            {
                replayStorage?.RefreshLocalReplays();
                _cachedReplays = replayStorage?.GetLocalReplays() ?? new List<ReplayHeader>();
                _lastReplayRefresh = Time.time;
            }

            if (_cachedReplays.Count == 0)
            {
                GUILayout.Label("No saved replays.", _grayLabel);
            }
            else
            {
                GUILayout.Label($"Saved ({_cachedReplays.Count})", _grayLabel);
                _replayListScroll = GUILayout.BeginScrollView(_replayListScroll, GUILayout.Height(Mathf.Min(150, _cachedReplays.Count * 55 + 10)));

                foreach (var replay in _cachedReplays)
                {
                    DrawReplayEntry(replay, replayStorage, replayViewer);
                }

                GUILayout.EndScrollView();
            }

            // Export success
            if (_showExportSuccess && Time.time - _exportSuccessTime < 3f)
            {
                GUILayout.Space(4);
                GUILayout.BeginHorizontal();
                GUILayout.Label("\u2713 Exported!", _greenLabel);
                if (GUILayout.Button("Open Folder", _smallButton, GUILayout.Width(80)))
                    replayStorage?.OpenExportFolder();
                GUILayout.EndHorizontal();
            }
            else
            {
                _showExportSuccess = false;
            }

            // Import
            GUILayout.Space(4);
            GUILayout.BeginHorizontal();
            _importPath = GUILayout.TextField(_importPath, _textFieldStyle);
            if (GUILayout.Button("Import", _smallButton, GUILayout.Width(50)))
            {
                if (!string.IsNullOrWhiteSpace(_importPath))
                    _ = ImportReplayAsync(_importPath, replayStorage);
            }
            GUILayout.EndHorizontal();

            GUILayout.EndVertical();
        }

        private void DrawReplayEntry(ReplayHeader replay, EOSReplayStorage storage, EOSReplayViewer viewer)
        {
            bool isFavorite = storage?.IsFavorite(replay.ReplayId) ?? false;

            GUILayout.BeginVertical(_sectionBox);

            GUILayout.BeginHorizontal();
            string starIcon = isFavorite ? "\u2605" : "\u2606";
            if (GUILayout.Button(starIcon, _smallButton, GUILayout.Width(25)))
            {
                storage?.ToggleFavorite(replay.ReplayId);
                _cachedReplays = storage?.GetLocalReplays() ?? new List<ReplayHeader>();
            }
            GUILayout.Label($"{replay.GameMode} on {replay.MapName}", _whiteLabel);
            GUILayout.FlexibleSpace();
            GUILayout.Label(EOSReplayStorage.FormatDuration(replay.Duration), _grayLabel);
            GUILayout.EndHorizontal();

            GUILayout.BeginHorizontal();
            GUILayout.Label($"{replay.Participants?.Length ?? 0} players", _grayLabel);
            GUILayout.Label(" - ", _grayLabel);
            GUILayout.Label(FormatTimeAgo(replay.RecordedAt), _grayLabel);
            GUILayout.FlexibleSpace();

            if (GUILayout.Button("\u25B6", _smallButton, GUILayout.Width(25)))
                _ = PlayReplayAsync(replay.ReplayId, storage, viewer);
            if (GUILayout.Button("\u21E5", _smallButton, GUILayout.Width(25)))
                _ = ExportReplayAsync(replay.ReplayId, storage);
            if (GUILayout.Button("\u2715", _smallButton, GUILayout.Width(25)))
            {
                storage?.DeleteReplay(replay.ReplayId);
                _cachedReplays = storage?.GetLocalReplays() ?? new List<ReplayHeader>();
            }

            GUILayout.EndHorizontal();
            GUILayout.EndVertical();
        }

        private async Task PlayReplayAsync(string replayId, EOSReplayStorage storage, EOSReplayViewer viewer)
        {
            if (viewer == null || storage == null) return;
            var replay = await storage.LoadLocalAsync(replayId);
            if (replay.HasValue)
                viewer.StartViewing(replay.Value);
        }

        private async Task ExportReplayAsync(string replayId, EOSReplayStorage storage)
        {
            if (storage == null) return;
            string path = await storage.ExportReplayAsync(replayId);
            if (!string.IsNullOrEmpty(path))
            {
                _showExportSuccess = true;
                _exportSuccessTime = Time.time;
            }
        }

        private async Task ImportReplayAsync(string path, EOSReplayStorage storage)
        {
            if (storage == null) return;
            bool success = await storage.ImportReplayAsync(path);
            if (success)
            {
                _importPath = "";
                _cachedReplays = storage.GetLocalReplays();
            }
        }

        #endregion

        #region Session Metrics Section

        private void DrawSessionMetricsSection()
        {
            var metricsManager = EOSMetrics.Instance;

            if (!DrawFoldout("Session Metrics", ref _foldMetrics)) return;

            GUILayout.BeginVertical(_sectionBox);

            if (metricsManager == null || !metricsManager.IsReady)
            {
                GUILayout.Label("Waiting for EOS login...", _grayLabel);
                GUILayout.EndVertical();
                return;
            }

            bool sessionActive = metricsManager.IsSessionActive;

            DrawKeyValue("Session", sessionActive ? "Active" : "Inactive");

            if (sessionActive)
            {
                DrawKeyValue("Duration", metricsManager.SessionDuration.ToString(@"hh\:mm\:ss"));
                if (!string.IsNullOrEmpty(metricsManager.CurrentSessionId))
                {
                    string shortId = metricsManager.CurrentSessionId.Length > 12
                        ? metricsManager.CurrentSessionId.Substring(0, 12) + "..."
                        : metricsManager.CurrentSessionId;
                    DrawKeyValue("ID", shortId);
                }
            }

            GUILayout.Space(4);
            GUILayout.BeginHorizontal();
            GUI.enabled = !sessionActive;
            if (GUILayout.Button("Begin", _smallButton))
                metricsManager.BeginSession();
            GUI.enabled = sessionActive;
            if (GUILayout.Button("End", _smallButton))
                metricsManager.EndSession();
            GUI.enabled = true;
            GUILayout.EndHorizontal();

            GUILayout.EndVertical();
        }

        #endregion

        #region LFG Section

        private void DrawLFGSection()
        {
            var lfgManager = EOSLFGManager.Instance;

            string title = "LFG (Looking for Group)";
            if (lfgManager != null && lfgManager.HasActivePost)
                title = $"LFG (Active: {lfgManager.ActivePost.CurrentSize}/{lfgManager.ActivePost.DesiredSize})";

            if (!DrawFoldout(title, ref _foldLFG)) return;

            GUILayout.BeginVertical(_sectionBox);

            if (lfgManager == null)
            {
                GUILayout.Label("LFG Manager not available.", _grayLabel);
                GUILayout.EndVertical();
                return;
            }

            if (lfgManager.HasActivePost)
            {
                DrawActiveLFGPost(lfgManager);
            }
            else
            {
                DrawCreateLFGPost(lfgManager);
            }

            GUILayout.Space(6);
            DrawLFGSearch(lfgManager);

            if (!string.IsNullOrEmpty(_lfgStatus))
            {
                GUILayout.Label(_lfgStatus, _grayLabel);
            }

            GUILayout.EndVertical();
        }

        private void DrawActiveLFGPost(EOSLFGManager lfgManager)
        {
            var post = lfgManager.ActivePost;
            GUILayout.Label("YOUR ACTIVE POST", _subHeaderStyle);

            DrawKeyValue("Title", post.Title);
            DrawKeyValue("Size", $"{post.CurrentSize}/{post.DesiredSize}");
            if (!string.IsNullOrEmpty(post.GameMode))
                DrawKeyValue("Mode", post.GameMode);

            var timeLeft = post.TimeRemaining;
            GUILayout.BeginHorizontal();
            GUILayout.Label("Expires:", _grayLabel, GUILayout.Width(90));
            GUILayout.Label($"{timeLeft.Minutes}m {timeLeft.Seconds}s", timeLeft.TotalMinutes < 5 ? _orangeLabel : _whiteLabel);
            GUILayout.EndHorizontal();

            if (lfgManager.PendingRequests.Count > 0)
            {
                GUILayout.Space(4);
                GUILayout.Label($"Pending Requests: {lfgManager.PendingRequests.Count}", _yellowLabel);
                foreach (var request in lfgManager.PendingRequests)
                {
                    GUILayout.BeginHorizontal();
                    GUILayout.Label(request.RequesterName, _grayLabel, GUILayout.Width(100));
                    if (GUILayout.Button("Accept", _smallButton, GUILayout.Width(55)))
                        _ = lfgManager.AcceptJoinRequestAsync(request);
                    if (GUILayout.Button("Reject", _smallButton, GUILayout.Width(55)))
                        _ = lfgManager.RejectJoinRequestAsync(request);
                    GUILayout.EndHorizontal();
                }
            }

            GUILayout.Space(4);
            if (GUILayout.Button("Close Post", _actionButton))
            {
                CloseLFGPostAsync(lfgManager);
            }
        }

        private void DrawCreateLFGPost(EOSLFGManager lfgManager)
        {
            GUILayout.Label("CREATE POST", _subHeaderStyle);

            GUILayout.BeginHorizontal();
            GUILayout.Label("Title:", _grayLabel, GUILayout.Width(45));
            _lfgTitle = GUILayout.TextField(_lfgTitle, _textFieldStyle, GUILayout.Width(200));
            GUILayout.EndHorizontal();

            GUILayout.BeginHorizontal();
            GUILayout.Label("Mode:", _grayLabel, GUILayout.Width(45));
            _lfgGameMode = GUILayout.TextField(_lfgGameMode, _textFieldStyle, GUILayout.Width(100));
            GUILayout.EndHorizontal();

            GUILayout.BeginHorizontal();
            GUILayout.Label("Size:", _grayLabel, GUILayout.Width(45));
            if (GUILayout.Button("-", _smallButton, GUILayout.Width(25)))
                _lfgDesiredSize = Mathf.Max(2, _lfgDesiredSize - 1);
            GUILayout.Label(_lfgDesiredSize.ToString(), _cyanLabel, GUILayout.Width(25));
            if (GUILayout.Button("+", _smallButton, GUILayout.Width(25)))
                _lfgDesiredSize = Mathf.Min(64, _lfgDesiredSize + 1);
            GUILayout.FlexibleSpace();
            GUILayout.EndHorizontal();

            GUILayout.Space(4);
            if (GUILayout.Button("Create LFG Post", _actionButton))
            {
                CreateLFGPostAsync(lfgManager);
            }
        }

        private void DrawLFGSearch(EOSLFGManager lfgManager)
        {
            GUILayout.Label("BROWSE POSTS", _subHeaderStyle);

            GUILayout.BeginHorizontal();
            if (GUILayout.Button("Search", _smallButton, GUILayout.Width(60)))
                SearchLFGPostsAsync(lfgManager);
            if (GUILayout.Button("Refresh", _smallButton, GUILayout.Width(60)))
                _ = lfgManager.RefreshSearchAsync();
            GUILayout.Label($"{lfgManager.SearchResults.Count} posts", _grayLabel);
            GUILayout.EndHorizontal();

            if (lfgManager.SearchResults.Count > 0)
            {
                _lfgSearchScrollPos = GUILayout.BeginScrollView(_lfgSearchScrollPos, GUILayout.Height(120));

                foreach (var post in lfgManager.SearchResults)
                {
                    if (post.IsExpired) continue;

                    GUILayout.BeginVertical(_sectionBox);

                    GUILayout.BeginHorizontal();
                    GUILayout.Label(post.Title, _grayLabel, GUILayout.Width(150));
                    GUILayout.Label($"by {post.OwnerName}", _grayLabel);
                    GUILayout.EndHorizontal();

                    GUILayout.BeginHorizontal();
                    GUILayout.Label($"{post.CurrentSize}/{post.DesiredSize}", _cyanLabel, GUILayout.Width(40));
                    if (!string.IsNullOrEmpty(post.GameMode))
                        GUILayout.Label(post.GameMode, _grayLabel, GUILayout.Width(60));
                    if (post.VoiceRequired)
                        GUILayout.Label("[Voice]", _yellowLabel, GUILayout.Width(50));
                    GUILayout.FlexibleSpace();

                    bool alreadySent = lfgManager.SentRequests.Contains(post.PostId);
                    GUI.enabled = post.IsJoinable && !alreadySent;
                    if (GUILayout.Button(alreadySent ? "Sent" : "Join", _smallButton, GUILayout.Width(50)))
                        _ = lfgManager.SendJoinRequestAsync(post.PostId);
                    GUI.enabled = true;
                    GUILayout.EndHorizontal();

                    GUILayout.EndVertical();
                }

                GUILayout.EndScrollView();
            }
        }

        private async void CreateLFGPostAsync(EOSLFGManager lfgManager)
        {
            _lfgStatus = "Creating post...";
            var options = new LFGPostOptions()
                .WithTitle(_lfgTitle)
                .WithGameMode(_lfgGameMode)
                .WithDesiredSize(_lfgDesiredSize);
            var (result, post) = await lfgManager.CreatePostAsync(options);
            _lfgStatus = result == Result.Success ? "Post created!" : $"Failed: {result}";
        }

        private async void CloseLFGPostAsync(EOSLFGManager lfgManager)
        {
            _lfgStatus = "Closing post...";
            var result = await lfgManager.ClosePostAsync();
            _lfgStatus = result == Result.Success ? "Post closed" : $"Failed: {result}";
        }

        private async void SearchLFGPostsAsync(EOSLFGManager lfgManager)
        {
            _lfgStatus = "Searching...";
            var options = new LFGSearchOptions();
            if (!string.IsNullOrEmpty(_lfgGameMode))
                options.WithGameMode(_lfgGameMode);
            var (result, posts) = await lfgManager.SearchPostsAsync(options);
            _lfgStatus = result == Result.Success ? $"Found {posts.Count} posts" : $"Search failed: {result}";
        }

        #endregion

        #region Player Profile Popup

        private void DrawPlayerProfilePopup()
        {
            if (string.IsNullOrEmpty(_profilePuid)) { _showProfilePopup = false; return; }

            // Darken background
            GUI.color = new Color(0, 0, 0, 0.5f);
            GUI.DrawTexture(new Rect(0, 0, Screen.width, Screen.height), Texture2D.whiteTexture);
            GUI.color = Color.white;

            float popupWidth = 340;
            float popupHeight = 380;
            Rect popupRect = new Rect(
                (Screen.width - popupWidth) / 2,
                (Screen.height - popupHeight) / 2,
                popupWidth, popupHeight);

            GUI.Box(popupRect, "", _sectionBox);
            GUILayout.BeginArea(new Rect(popupRect.x + 10, popupRect.y + 10, popupWidth - 20, popupHeight - 20));

            GUILayout.Label("PLAYER PROFILE", _headerStyle);
            GUILayout.Space(4);

            var registry = EOSPlayerRegistry.Instance;
            string displayName = registry?.GetPlayerName(_profilePuid) ?? _profilePuid;
            string platformId = registry?.GetPlatform(_profilePuid);
            string platformIcon = !string.IsNullOrEmpty(platformId) ? EOSPlayerRegistry.GetPlatformIcon(platformId) : "";
            string platformName = !string.IsNullOrEmpty(platformId) ? EOSPlayerRegistry.GetPlatformName(platformId) : "Unknown";
            bool isFriend = registry?.IsFriend(_profilePuid) ?? false;
            bool isBlocked = registry?.IsBlocked(_profilePuid) ?? false;
            DateTime? lastSeen = registry?.GetLastSeen(_profilePuid);

            // Name + platform
            GUILayout.Label($"{platformIcon} {displayName}", _subHeaderStyle);
            GUILayout.Label($"Platform: {platformName}", _grayLabel);
            GUILayout.Label($"PUID: {(_profilePuid.Length > 20 ? _profilePuid.Substring(0, 20) + "..." : _profilePuid)}", _grayLabel);

            if (lastSeen.HasValue)
                GUILayout.Label($"Last seen: {GetTimeAgo(lastSeen.Value)}", _grayLabel);

            GUILayout.Space(6);

            // Status badges
            GUILayout.BeginHorizontal();
            if (isFriend)
                GUILayout.Label("[Friend]", _greenLabel, GUILayout.Width(60));
            if (isBlocked)
                GUILayout.Label("[Blocked]", _redLabel, GUILayout.Width(60));

            // Check if in current lobby
            var lobbyMgr = EOSLobbyManager.Instance;
            bool isOwner = lobbyMgr != null && lobbyMgr.IsInLobby && lobbyMgr.CurrentLobby.OwnerPuid == _profilePuid;
            if (isOwner)
                GUILayout.Label("[Host]", _yellowLabel, GUILayout.Width(50));

            GUILayout.EndHorizontal();
            GUILayout.Space(6);

            // Notes
            GUILayout.Label("Personal Note:", _grayLabel);
            if (_profileEditingNote)
            {
                _profileNote = GUILayout.TextField(_profileNote, _textFieldStyle, GUILayout.Height(20));
                GUILayout.BeginHorizontal();
                if (GUILayout.Button("Save", _smallButton, GUILayout.Width(50)))
                {
                    registry?.SetNote(_profilePuid, _profileNote);
                    _profileEditingNote = false;
                    _profileStatus = "Note saved";
                }
                if (GUILayout.Button("Cancel", _smallButton, GUILayout.Width(55)))
                {
                    _profileNote = registry?.GetNote(_profilePuid) ?? "";
                    _profileEditingNote = false;
                }
                GUILayout.EndHorizontal();
            }
            else
            {
                string noteDisplay = string.IsNullOrEmpty(_profileNote) ? "(no note)" : _profileNote;
                GUILayout.BeginHorizontal();
                GUILayout.Label(noteDisplay, _whiteLabel);
                if (GUILayout.Button("Edit", _smallButton, GUILayout.Width(40)))
                    _profileEditingNote = true;
                GUILayout.EndHorizontal();
            }

            GUILayout.Space(8);

            // Action buttons
            GUILayout.BeginHorizontal();
            if (GUILayout.Button(isFriend ? "Unfriend" : "Add Friend", _actionButton))
            {
                registry?.ToggleFriend(_profilePuid);
                _profileStatus = isFriend ? "Removed from friends" : "Added as friend";
            }
            if (GUILayout.Button(isBlocked ? "Unblock" : "Block", _smallButton))
            {
                if (isBlocked)
                    registry?.UnblockPlayer(_profilePuid);
                else
                    registry?.BlockPlayer(_profilePuid);
                _profileStatus = isBlocked ? "Unblocked" : "Blocked";
            }
            GUILayout.EndHorizontal();

            GUILayout.BeginHorizontal();
            // Report button
            var reportsManager = EOSReports.Instance;
            if (reportsManager != null && reportsManager.IsReady)
            {
                if (GUILayout.Button("Report", _smallButton))
                {
                    _showProfilePopup = false;
                    _reportTargetPuid = _profilePuid;
                    _showReportPopup = true;
                    _reportStatus = "";
                }
            }

            // Invite button
            var inviteMgr = EOSCustomInvites.Instance;
            if (inviteMgr != null)
            {
                if (GUILayout.Button("Invite", _smallButton))
                {
                    _ = inviteMgr.SendInviteAsync(_profilePuid);
                    _profileStatus = "Invite sent!";
                }
            }

            // Kick button (host only, when in lobby)
            if (lobbyMgr != null && lobbyMgr.IsInLobby && lobbyMgr.IsOwner && !isOwner)
            {
                if (GUILayout.Button("Kick", _smallButton))
                {
                    _ = lobbyMgr.KickMemberAsync(_profilePuid);
                    _profileStatus = "Kicked from lobby";
                }
            }
            GUILayout.EndHorizontal();

            if (!string.IsNullOrEmpty(_profileStatus))
            {
                GUILayout.Space(4);
                GUILayout.Label(_profileStatus, _cyanLabel);
            }

            GUILayout.FlexibleSpace();

            if (GUILayout.Button("Close", _actionButton))
            {
                _showProfilePopup = false;
                _profilePuid = "";
                _profileStatus = "";
            }

            GUILayout.EndArea();
        }

        #endregion

        #region Report Popup

        private void DrawReportPopup()
        {
            // Darken background
            GUI.color = new Color(0, 0, 0, 0.5f);
            GUI.DrawTexture(new Rect(0, 0, Screen.width, Screen.height), Texture2D.whiteTexture);
            GUI.color = Color.white;

            float popupWidth = 300;
            float popupHeight = 200;
            Rect popupRect = new Rect(
                (Screen.width - popupWidth) / 2,
                (Screen.height - popupHeight) / 2,
                popupWidth, popupHeight);

            GUI.Box(popupRect, "", _sectionBox);
            GUILayout.BeginArea(new Rect(popupRect.x + 10, popupRect.y + 10, popupWidth - 20, popupHeight - 20));

            GUILayout.Label("REPORT PLAYER", _headerStyle);

            string targetDisplay = _reportTargetPuid.Length > 16
                ? _reportTargetPuid.Substring(0, 8) + "..."
                : _reportTargetPuid;

            var chatMgr = EOSLobbyChatManager.Instance;
            if (chatMgr != null)
            {
                string displayName = chatMgr.GetDisplayName(_reportTargetPuid);
                if (!string.IsNullOrEmpty(displayName))
                    targetDisplay = displayName;
            }

            GUILayout.Label($"Target: {targetDisplay}", _whiteLabel);
            GUILayout.Space(10);

            // Category selection
            GUILayout.Label("Category:", _grayLabel);
            var categories = EOSReports.GetAllCategories();
            string[] categoryNames = new string[categories.Length];
            for (int i = 0; i < categories.Length; i++)
                categoryNames[i] = EOSReports.GetCategoryDisplayName(categories[i]);

            GUILayout.BeginHorizontal();
            for (int i = 0; i < Mathf.Min(4, categories.Length); i++)
            {
                bool isSelected = _reportCategoryIndex == i;
                GUIStyle btnStyle = isSelected ? _actionButton : _smallButton;
                if (GUILayout.Button(categoryNames[i], btnStyle))
                    _reportCategoryIndex = i;
            }
            GUILayout.EndHorizontal();

            if (categories.Length > 4)
            {
                GUILayout.BeginHorizontal();
                for (int i = 4; i < categories.Length; i++)
                {
                    bool isSelected = _reportCategoryIndex == i;
                    GUIStyle btnStyle = isSelected ? _actionButton : _smallButton;
                    if (GUILayout.Button(categoryNames[i], btnStyle))
                        _reportCategoryIndex = i;
                }
                GUILayout.EndHorizontal();
            }

            GUILayout.Space(10);

            if (!string.IsNullOrEmpty(_reportStatus))
                GUILayout.Label(_reportStatus, _grayLabel);

            GUILayout.FlexibleSpace();

            GUILayout.BeginHorizontal();
            if (GUILayout.Button("Send Report", _actionButton))
            {
                SendReport(categories[_reportCategoryIndex]);
            }
            if (GUILayout.Button("Cancel", _smallButton))
            {
                _showReportPopup = false;
                _reportTargetPuid = "";
                _reportStatus = "";
            }
            GUILayout.EndHorizontal();

            GUILayout.EndArea();
        }

        private async void SendReport(PlayerReportsCategory category)
        {
            var reportsManager = EOSReports.Instance;
            if (string.IsNullOrEmpty(_reportTargetPuid) || reportsManager == null) return;
            _reportStatus = "Sending...";
            var result = await reportsManager.ReportPlayerAsync(_reportTargetPuid, category);
            if (result == Result.Success)
            {
                _reportStatus = "Report sent!";
                await Task.Delay(1000);
                _showReportPopup = false;
                _reportTargetPuid = "";
                _reportStatus = "";
            }
            else
            {
                _reportStatus = $"Failed: {result}";
            }
        }

        #endregion

        #region Helpers

        private string GetTimeAgo(DateTime dt)
        {
            var span = DateTime.Now - dt;
            if (span.TotalMinutes < 1) return "just now";
            if (span.TotalMinutes < 60) return $"{(int)span.TotalMinutes}m ago";
            if (span.TotalHours < 24) return $"{(int)span.TotalHours}h ago";
            if (span.TotalDays < 7) return $"{(int)span.TotalDays}d ago";
            return dt.ToString("MM/dd");
        }

        private static string FormatTimeAgo(long unixTimeMs)
        {
            var recordedTime = DateTimeOffset.FromUnixTimeMilliseconds(unixTimeMs);
            var elapsed = DateTimeOffset.UtcNow - recordedTime;
            if (elapsed.TotalMinutes < 1) return "just now";
            if (elapsed.TotalMinutes < 60) return $"{(int)elapsed.TotalMinutes}m ago";
            if (elapsed.TotalHours < 24) return $"{(int)elapsed.TotalHours}h ago";
            if (elapsed.TotalDays < 7) return $"{(int)elapsed.TotalDays}d ago";
            return recordedTime.LocalDateTime.ToString("MMM d");
        }

        #endregion
    }
}
