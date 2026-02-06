using System.Collections.Generic;
using System.Threading.Tasks;
using Epic.OnlineServices;
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
        private Rect _windowRect = new Rect(20, 20, 460, 560);

        // Styles (lazy init)
        private GUIStyle _windowStyle;
        private GUIStyle _headerStyle;
        private GUIStyle _greenLabel;
        private GUIStyle _redLabel;
        private GUIStyle _yellowLabel;
        private GUIStyle _grayLabel;
        private GUIStyle _tabButtonActive;
        private GUIStyle _tabButtonNormal;
        private GUIStyle _sectionBox;
        private GUIStyle _monoLabel;
        private bool _stylesInited;

        // Lobby tab state
        private string _lobbyName = "Test Lobby";
        private int _maxPlayers = 4;
        private bool _lobbyPublic = true;
        private bool _lobbyVoice = true;
        private string _joinCode = "";
        private string _lobbyStatus = "";
        private List<LobbyData> _searchResults;
        private bool _searching;

        // Tab names
        private static readonly string[] TabNames = { "Status", "Lobbies", "Voice", "Social" };

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
        }

        private void InitStyles()
        {
            if (_stylesInited) return;
            _stylesInited = true;

            _windowStyle = new GUIStyle(GUI.skin.window);

            _headerStyle = new GUIStyle(GUI.skin.label)
            {
                fontSize = 14,
                fontStyle = FontStyle.Bold,
                alignment = TextAnchor.MiddleLeft
            };

            _greenLabel = new GUIStyle(GUI.skin.label);
            _greenLabel.normal.textColor = new Color(0.3f, 1f, 0.3f);

            _redLabel = new GUIStyle(GUI.skin.label);
            _redLabel.normal.textColor = new Color(1f, 0.3f, 0.3f);

            _yellowLabel = new GUIStyle(GUI.skin.label);
            _yellowLabel.normal.textColor = new Color(1f, 1f, 0.3f);

            _grayLabel = new GUIStyle(GUI.skin.label);
            _grayLabel.normal.textColor = Color.gray;

            _tabButtonActive = new GUIStyle(GUI.skin.button)
            {
                fontStyle = FontStyle.Bold
            };
            _tabButtonActive.normal.textColor = new Color(0.3f, 1f, 0.5f);

            _tabButtonNormal = new GUIStyle(GUI.skin.button);

            _sectionBox = new GUIStyle(GUI.skin.box)
            {
                padding = new RectOffset(8, 8, 6, 6),
                margin = new RectOffset(0, 0, 4, 4)
            };

            _monoLabel = new GUIStyle(GUI.skin.label)
            {
                fontSize = 11
            };
            _monoLabel.normal.textColor = new Color(0.8f, 0.9f, 1f);
        }

        private void OnGUI()
        {
            if (!_visible) return;

            InitStyles();

            _windowRect = GUILayout.Window(94201, _windowRect, DrawWindow, "EOS Native Status (F1)", _windowStyle);
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

            GUILayout.Space(4);

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

        #region Status Tab

        private void DrawStatusTab()
        {
            var mgr = EOSManager.Instance;

            // SDK Status
            GUILayout.BeginVertical(_sectionBox);
            GUILayout.Label("SDK Status", _headerStyle);

            DrawStatusRow("EOS SDK", mgr != null && mgr.IsInitialized, "Initialized", "Not Initialized");
            DrawStatusRow("Login", mgr != null && mgr.IsLoggedIn, "Logged In", "Not Logged In");
            DrawStatusRow("Epic Account", mgr != null && mgr.IsEpicAccountLoggedIn, "Connected", "Not Connected");

            if (mgr != null && mgr.IsLoggedIn && mgr.LocalProductUserId != null)
            {
                GUILayout.BeginHorizontal();
                GUILayout.Label("PUID:", GUILayout.Width(44));
                string puid = mgr.LocalProductUserId.ToString();
                GUILayout.Label(puid, _monoLabel);
                if (GUILayout.Button("Copy", GUILayout.Width(45)))
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
            GUILayout.BeginVertical(_sectionBox);
            GUILayout.Label("Platform", _headerStyle);
            GUILayout.Label($"Platform: {EOSPlatformHelper.CurrentPlatform} ({EOSPlatformHelper.PlatformId})", _grayLabel);
            GUILayout.Label($"Device: {SystemInfo.deviceModel}", _grayLabel);
            GUILayout.Label($"Overlay: {(EOSPlatformHelper.SupportsOverlay ? "Yes" : "No")}  |  Voice: {(EOSPlatformHelper.SupportsVoice ? "Yes" : "No")}", _grayLabel);
            GUILayout.EndVertical();

            // Interfaces
            if (mgr != null && mgr.IsInitialized)
            {
                GUILayout.BeginVertical(_sectionBox);
                GUILayout.Label("Interfaces", _headerStyle);

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

            // Actions
            GUILayout.BeginVertical(_sectionBox);
            GUILayout.Label("Actions", _headerStyle);

            GUILayout.BeginHorizontal();

            bool canInit = mgr != null && !mgr.IsInitialized;
            bool canLogin = mgr != null && mgr.IsInitialized && !mgr.IsLoggedIn;
            bool canLogout = mgr != null && mgr.IsLoggedIn;

            GUI.enabled = canInit;
            if (GUILayout.Button("Initialize"))
            {
                InitializeFromResources();
            }

            GUI.enabled = canLogin;
            if (GUILayout.Button("Login DeviceID"))
            {
                _ = mgr.LoginWithDeviceTokenAsync("Player");
            }

            GUI.enabled = canLogin;
            if (GUILayout.Button("Login Smart"))
            {
                _ = mgr.LoginSmartAsync("Player");
            }

            GUI.enabled = canLogout;
            if (GUILayout.Button("Logout"))
            {
                _ = mgr.LogoutAsync();
            }

            GUI.enabled = true;
            GUILayout.EndHorizontal();

            GUILayout.EndVertical();
        }

        private void DrawStatusRow(string label, bool ok, string trueText, string falseText)
        {
            GUILayout.BeginHorizontal();
            GUILayout.Label(label + ":", GUILayout.Width(110));
            GUILayout.Label(ok ? trueText : falseText, ok ? _greenLabel : _redLabel);
            GUILayout.EndHorizontal();
        }

        private void DrawInterfaceBadge(string name, bool available)
        {
            var style = available ? _greenLabel : _grayLabel;
            string prefix = available ? "[OK]" : "[--]";
            GUILayout.Label($"{prefix} {name}", style, GUILayout.Width(105));
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
                GUILayout.BeginVertical(_sectionBox);
                GUILayout.Label("Current Lobby", _headerStyle);

                var lobby = lobbyMgr.CurrentLobby;

                GUILayout.BeginHorizontal();
                GUILayout.Label("Join Code:", GUILayout.Width(80));
                GUILayout.Label(lobby.JoinCode ?? "????", _greenLabel);
                if (GUILayout.Button("Copy", GUILayout.Width(45)))
                {
                    GUIUtility.systemCopyBuffer = lobby.JoinCode ?? "";
                }
                GUILayout.EndHorizontal();

                GUILayout.Label($"Role: {(lobbyMgr.IsOwner ? "HOST" : "CLIENT")}", lobbyMgr.IsOwner ? _greenLabel : _yellowLabel);
                GUILayout.Label($"Members: {lobby.MemberCount} / {lobby.MaxMembers}");
                GUILayout.Label($"Public: {lobby.IsPublic}  |  Voice: {(EOSVoiceManager.Instance?.IsVoiceEnabled == true ? "Yes" : "No")}");

                string ownerShort = lobby.OwnerPuid != null && lobby.OwnerPuid.Length > 16
                    ? lobby.OwnerPuid.Substring(0, 12) + "..."
                    : lobby.OwnerPuid;
                GUILayout.Label($"Owner: {ownerShort}", _grayLabel);

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

                GUILayout.Space(4);
                if (GUILayout.Button("Leave Lobby"))
                {
                    _ = lobbyMgr.LeaveLobbyAsync();
                    _lobbyStatus = "Left lobby.";
                }

                GUILayout.EndVertical();
            }

            // Create Lobby
            if (lobbyMgr == null || !lobbyMgr.IsInLobby)
            {
                GUILayout.BeginVertical(_sectionBox);
                GUILayout.Label("Create Lobby", _headerStyle);

                GUILayout.BeginHorizontal();
                GUILayout.Label("Name:", GUILayout.Width(60));
                _lobbyName = GUILayout.TextField(_lobbyName);
                GUILayout.EndHorizontal();

                GUILayout.BeginHorizontal();
                GUILayout.Label("Max:", GUILayout.Width(60));
                int.TryParse(GUILayout.TextField(_maxPlayers.ToString(), GUILayout.Width(40)), out _maxPlayers);
                _maxPlayers = Mathf.Clamp(_maxPlayers, 2, 64);
                GUILayout.Label("Public:");
                _lobbyPublic = GUILayout.Toggle(_lobbyPublic, "");
                GUILayout.Label("Voice:");
                _lobbyVoice = GUILayout.Toggle(_lobbyVoice, "");
                GUILayout.EndHorizontal();

                if (GUILayout.Button("Create Lobby"))
                {
                    CreateLobby(lobbyMgr);
                }

                GUILayout.EndVertical();
            }

            // Join by Code
            if (lobbyMgr == null || !lobbyMgr.IsInLobby)
            {
                GUILayout.BeginVertical(_sectionBox);
                GUILayout.Label("Join by Code", _headerStyle);

                GUILayout.BeginHorizontal();
                GUILayout.Label("Code:", GUILayout.Width(44));
                _joinCode = GUILayout.TextField(_joinCode, 4, GUILayout.Width(60));
                if (GUILayout.Button("Join"))
                {
                    JoinByCode(lobbyMgr);
                }
                if (GUILayout.Button("Quick Match"))
                {
                    QuickMatch(lobbyMgr);
                }
                GUILayout.EndHorizontal();

                GUILayout.EndVertical();
            }

            // Search
            GUILayout.BeginVertical(_sectionBox);
            GUILayout.Label("Search Lobbies", _headerStyle);

            GUILayout.BeginHorizontal();
            GUI.enabled = !_searching;
            if (GUILayout.Button(_searching ? "Searching..." : "Search All"))
            {
                SearchLobbies(lobbyMgr);
            }
            GUI.enabled = true;
            GUILayout.EndHorizontal();

            if (_searchResults != null)
            {
                GUILayout.Label($"Found: {_searchResults.Count} lobbies", _searchResults.Count > 0 ? _greenLabel : _grayLabel);

                foreach (var l in _searchResults)
                {
                    GUILayout.BeginHorizontal();
                    string name = l.LobbyName ?? l.JoinCode ?? "???";
                    GUILayout.Label($"  [{l.JoinCode}] {name} ({l.MemberCount}/{l.MaxMembers})", GUILayout.Width(320));

                    if (lobbyMgr != null && !lobbyMgr.IsInLobby && GUILayout.Button("Join", GUILayout.Width(50)))
                    {
                        _ = JoinLobbyById(lobbyMgr, l.LobbyId);
                    }
                    GUILayout.EndHorizontal();
                }
            }

            GUILayout.EndVertical();

            // Status message
            if (!string.IsNullOrEmpty(_lobbyStatus))
            {
                GUILayout.Label(_lobbyStatus, _yellowLabel);
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
                EnableVoice = _lobbyVoice
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

            GUILayout.BeginVertical(_sectionBox);
            GUILayout.Label("Voice Status", _headerStyle);

            if (voice == null)
            {
                GUILayout.Label("EOSVoiceManager not found in scene.", _grayLabel);
                GUILayout.Label("Voice is lobby-based. Join a lobby with voice enabled.", _grayLabel);
            }
            else
            {
                DrawStatusRow("Connected", voice.IsConnected, "Connected", "Disconnected");
                DrawStatusRow("Mic", !voice.IsMuted, "Active", "Muted");
                DrawStatusRow("Voice Enabled", voice.IsVoiceEnabled, "Yes", "No");
                GUILayout.Label($"Room: {voice.CurrentRoomName ?? "(none)"}", _grayLabel);
                GUILayout.Label($"Participants: {voice.ParticipantCount}", _grayLabel);

                // Controls
                if (voice.IsConnected)
                {
                    GUILayout.Space(4);
                    GUILayout.BeginHorizontal();
                    if (GUILayout.Button(voice.IsMuted ? "Unmute Mic" : "Mute Mic"))
                    {
                        voice.ToggleMute();
                    }
                    GUILayout.EndHorizontal();

                    // Participants
                    var participants = voice.GetAllParticipants();
                    if (participants.Count > 0)
                    {
                        GUILayout.Space(4);
                        GUILayout.Label("Participants:", _headerStyle);

                        foreach (var puid in participants)
                        {
                            GUILayout.BeginHorizontal();

                            bool speaking = voice.IsSpeaking(puid);
                            var audioStatus = voice.GetParticipantAudioStatus(puid);
                            string shortPuid = puid.Length > 16 ? puid.Substring(0, 12) + "..." : puid;

                            string icon = speaking ? "[SPEAKING]" : "[       ]";
                            var style = speaking ? _greenLabel : _grayLabel;

                            GUILayout.Label(icon, style, GUILayout.Width(80));
                            GUILayout.Label(shortPuid, _monoLabel);
                            GUILayout.Label(audioStatus.ToString(), _grayLabel, GUILayout.Width(70));

                            GUILayout.EndHorizontal();
                        }
                    }
                }
            }

            GUILayout.EndVertical();

            // Help text
            GUILayout.BeginVertical(_sectionBox);
            GUILayout.Label("Voice Info", _headerStyle);
            GUILayout.Label("Voice chat is tied to EOS lobbies.", _grayLabel);
            GUILayout.Label("Create a lobby with Voice enabled, then", _grayLabel);
            GUILayout.Label("voice auto-connects for all members.", _grayLabel);
            GUILayout.Label("Voice persists through host migration.", _grayLabel);
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

            var registry = EOSPlayerRegistry.Instance;
            if (registry != null)
            {
                GUILayout.Label($"Cached Players: {registry.CachedPlayerCount}");
                GUILayout.Label($"Friends: {registry.FriendCount}  |  Blocked: {registry.BlockedCount}");

                // Recent players
                var recent = registry.GetRecentPlayers(7);
                if (recent.Count > 0)
                {
                    GUILayout.Space(2);
                    GUILayout.Label($"Recent ({recent.Count}):", _grayLabel);
                    int shown = 0;
                    foreach (var (puid, name, lastSeen) in recent)
                    {
                        if (shown++ >= 8) { GUILayout.Label($"  ... +{recent.Count - 8} more", _grayLabel); break; }
                        string shortPuid = puid.Length > 8 ? puid.Substring(0, 8) + ".." : puid;
                        GUILayout.Label($"  {name} ({shortPuid}) - {lastSeen:MM/dd HH:mm}", _monoLabel);
                    }
                }

                // Friends list
                var friends = registry.GetFriends();
                if (friends.Count > 0)
                {
                    GUILayout.Space(2);
                    GUILayout.Label($"Local Friends ({friends.Count}):", _headerStyle);
                    foreach (var (puid, name) in friends)
                    {
                        GUILayout.BeginHorizontal();
                        var status = registry.GetFriendStatus(puid);
                        var statusStyle = status switch
                        {
                            FriendStatus.InLobby => _greenLabel,
                            FriendStatus.InGame => _yellowLabel,
                            _ => _grayLabel
                        };
                        string statusText = status switch
                        {
                            FriendStatus.InLobby => "[IN LOBBY]",
                            FriendStatus.InGame => "[IN GAME]",
                            FriendStatus.Offline => "[OFFLINE]",
                            _ => "[???]"
                        };
                        GUILayout.Label(statusText, statusStyle, GUILayout.Width(80));
                        GUILayout.Label(name, _monoLabel);
                        GUILayout.EndHorizontal();
                    }
                }
            }
            else
            {
                GUILayout.Label("EOSPlayerRegistry not found.", _grayLabel);
            }

            GUILayout.EndVertical();

            // Epic Account
            GUILayout.BeginVertical(_sectionBox);
            GUILayout.Label("Epic Account", _headerStyle);

            if (mgr.IsEpicAccountLoggedIn)
            {
                GUILayout.Label("Connected to Epic Account", _greenLabel);
                GUILayout.Label($"EpicAccountId: {mgr.LocalEpicAccountId}", _monoLabel);

                if (GUILayout.Button("Logout Epic Account"))
                {
                    _ = mgr.LogoutEpicAccountAsync();
                }
            }
            else
            {
                GUILayout.Label("Not connected to Epic Account.", _grayLabel);
                GUILayout.Label("Epic Account enables: Friends, Presence, Achievements", _grayLabel);

                GUILayout.BeginHorizontal();
                if (GUILayout.Button("Login with Epic"))
                {
                    _ = mgr.LoginWithEpicAccountAsync();
                }
                GUILayout.EndHorizontal();
            }

            GUILayout.EndVertical();
        }

        #endregion
    }
}
