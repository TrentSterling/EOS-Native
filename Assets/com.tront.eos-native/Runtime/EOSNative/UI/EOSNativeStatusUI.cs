using System.Collections.Generic;
using System.Threading.Tasks;
using Epic.OnlineServices;
using Epic.OnlineServices.RTCAudio;
using EOSNative.Lobbies;
using EOSNative.Logging;
using EOSNative.Voice;
using UnityEngine;
#if EOS_HAS_INPUT_SYSTEM
using UnityEngine.InputSystem;
#endif

namespace EOSNative.UI
{
    /// <summary>
    /// Runtime OnGUI overlay for EOS Native verification.
    /// Toggle with F1. Tabs: Status, Lobbies, Voice, Social.
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
        private bool _foldInterfaces = false;
        private bool _foldPlatform = true;
        private bool _foldCurrentLobby = true;
        private bool _foldCreateLobby = true;
        private bool _foldJoinLobby = true;
        private bool _foldSearch = false;
        private bool _foldParticipants = true;
        private bool _foldAudioDevices = true;
        private bool _foldFriends = false;
        private bool _foldRecent = false;
        private bool _foldEpicAccount = true;

        // Tab names
        private static readonly string[] TabNames = { "Status", "Lobbies", "Voice", "Social" };

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

            // Player Registry
            GUILayout.BeginVertical(_sectionBox);
            GUILayout.Label("Player Registry", _headerStyle);
            GUILayout.Space(4);

            var registry = EOSPlayerRegistry.Instance;
            if (registry != null)
            {
                DrawKeyValue("Cached", registry.CachedPlayerCount.ToString());
                DrawKeyValue("Friends", registry.FriendCount.ToString());
                DrawKeyValue("Blocked", registry.BlockedCount.ToString());

                // Recent players
                if (DrawFoldout($"Recent Players", ref _foldRecent))
                {
                    var recent = registry.GetRecentPlayers(7);
                    if (recent.Count > 0)
                    {
                        int shown = 0;
                        foreach (var (puid, name, lastSeen) in recent)
                        {
                            if (shown++ >= 8) { GUILayout.Label($"  ... +{recent.Count - 8} more", _grayLabel); break; }
                            string shortPuid = puid.Length > 8 ? puid.Substring(0, 8) + ".." : puid;
                            GUILayout.Label($"  {name} ({shortPuid}) - {lastSeen:MM/dd HH:mm}", _monoLabel);
                        }
                    }
                    else
                    {
                        GUILayout.Label("  No recent players.", _grayLabel);
                    }
                }

                // Friends list
                if (DrawFoldout($"Friends ({registry.FriendCount})", ref _foldFriends))
                {
                    var friends = registry.GetFriends();
                    if (friends.Count > 0)
                    {
                        foreach (var (puid, name) in friends)
                        {
                            GUILayout.BeginHorizontal();
                            var status = registry.GetFriendStatus(puid);
                            var (statusText, statusStyle) = status switch
                            {
                                FriendStatus.InLobby => ("[IN LOBBY]", _greenLabel),
                                FriendStatus.InGame => ("[IN GAME]", _yellowLabel),
                                FriendStatus.Offline => ("[OFFLINE]", _grayLabel),
                                _ => ("[???]", _grayLabel)
                            };
                            GUILayout.Label(statusText, statusStyle, GUILayout.Width(80));
                            GUILayout.Label(name, _whiteLabel);
                            GUILayout.EndHorizontal();
                        }
                    }
                    else
                    {
                        GUILayout.Label("  No friends found.", _grayLabel);
                    }
                }
            }
            else
            {
                GUILayout.Label("EOSPlayerRegistry not found.", _grayLabel);
            }

            GUILayout.EndVertical();

            // Epic Account
            if (DrawFoldout("Epic Account", ref _foldEpicAccount))
            {
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
        }

        #endregion
    }
}
