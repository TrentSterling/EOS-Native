using System;
using System.Collections.Generic;
using System.Text;
using System.Threading.Tasks;
using Epic.OnlineServices;
using Epic.OnlineServices.Reports;
using Epic.OnlineServices.RTCAudio;
using EOSNative.AntiCheat;
using EOSNative.Lobbies;
using EOSNative.Net;
using EOSNative.Replay;
using EOSNative.Social;
using EOSNative.Storage;
using EOSNative.Voice;
using UnityEngine;
using UnityEngine.UI;

namespace EOSNative.UI
{
    /// <summary>
    /// Canvas-based runtime UI for EOS Native.
    /// Works on Android/iOS where OnGUI may not render.
    /// Toggle with bottom-right corner button or 3-finger tap.
    /// Tabs: Status, Lobbies, Voice, Social.
    /// </summary>
    public class EOSNativeCanvasUI : MonoBehaviour
    {
        #region Singleton

        private static EOSNativeCanvasUI _instance;
        public static EOSNativeCanvasUI Instance
        {
            get
            {
                if (_instance != null) return _instance;
                _instance = FindAnyObjectByType<EOSNativeCanvasUI>();
                if (_instance != null) return _instance;
                var go = new GameObject("[EOSNativeCanvasUI]");
                if (EOSManager.Instance != null)
                    go.transform.SetParent(EOSManager.Instance.transform);
                else
                    DontDestroyOnLoad(go);
                _instance = go.AddComponent<EOSNativeCanvasUI>();
                return _instance;
            }
        }

        #endregion

        #region Colors

        // Main backgrounds
        private static readonly Color ColPanelBg = new Color(0.08f, 0.08f, 0.12f, 0.98f);
        private static readonly Color ColSectionBg = new Color(0.14f, 0.16f, 0.22f, 1f);
        private static readonly Color ColTitleBg = new Color(0.05f, 0.05f, 0.09f, 1f);

        // Text
        private static readonly Color ColHeader = new Color(0f, 0.74f, 0.83f, 1f);    // Cyan
        private static readonly Color ColText = new Color(0.88f, 0.88f, 0.92f, 1f);    // Near-white
        private static readonly Color ColDimText = new Color(0.50f, 0.52f, 0.58f, 1f); // Gray
        private static readonly Color ColGreen = new Color(0.3f, 0.9f, 0.3f, 1f);
        private static readonly Color ColRed = new Color(1f, 0.35f, 0.35f, 1f);
        private static readonly Color ColYellow = new Color(1f, 1f, 0.35f, 1f);
        private static readonly Color ColOrange = new Color(1f, 0.7f, 0.2f, 1f);

        // Interactive
        private static readonly Color ColButton = new Color(0.18f, 0.34f, 0.56f, 1f);
        private static readonly Color ColButtonHover = new Color(0.24f, 0.44f, 0.66f, 1f);
        private static readonly Color ColButtonDanger = new Color(0.55f, 0.18f, 0.18f, 1f);
        private static readonly Color ColTabActive = new Color(0.22f, 0.42f, 0.62f, 1f);
        private static readonly Color ColTabNormal = new Color(0.12f, 0.13f, 0.18f, 1f);
        private static readonly Color ColInputBg = new Color(0.10f, 0.10f, 0.16f, 1f);
        private static readonly Color ColToggleOn = new Color(0.18f, 0.55f, 0.28f, 1f);
        private static readonly Color ColToggleOff = new Color(0.32f, 0.18f, 0.18f, 1f);

        // Misc
        private static readonly Color ColLevelBg = new Color(0.08f, 0.08f, 0.12f, 1f);
        private static readonly Color ColLevelFill = new Color(0.3f, 0.8f, 0.4f, 1f);

        #endregion

        #region State

        private bool _panelVisible;
        private int _currentTab;
        private static readonly string[] TabNames = { "Status", "Lobbies", "Voice", "Social", "Stats", "Tools" };

        // Canvas hierarchy
        private Canvas _canvas;
        private GameObject _toggleButton;
        private GameObject _mainPanel;
        private GameObject[] _tabContents;
        private Button[] _tabButtons;
        private Image[] _tabButtonImages;
        private RectTransform _scrollContentRT; // For forced layout rebuilds

        // Built flag
        private bool _built;

        // Lobby tab state
        private InputField _lobbyNameInput;
        private InputField _maxPlayersInput;
        private Toggle _publicToggle;
        private Toggle _voiceToggle;
        private Toggle _hostMigrationToggle;
        private InputField _joinCodeInput;
        private Text _lobbyStatusText;
        private Transform _lobbyInfoContainer;
        private Transform _lobbyMembersContainer;
        private Transform _lobbySearchContainer;
        private Transform _lobbyChatContainer;
        private Text _lobbyChatLog;
        private InputField _chatInputField;

        // Voice tab state
        private Transform _voiceStatusContainer;
        private Transform _voiceParticipantsContainer;
        private Transform _audioDevicesContainer;
        private Transform _voiceDiagContainer;
        private Image _micLevelFill;
        private Text _micLevelText;
        private int _selectedInputDevice = -1;
        private int _selectedOutputDevice = -1;

        // Status tab state
        private Transform _statusContainer;

        // Social tab state
        private Transform _socialContainer;
        private string _editingNotePuid;
        private string _editingNoteText = "";
        private InputField _editingNoteInput;
        private string _inviteRecipientPuid = "";
        private string _inviteStatus = "";

        // Stats tab state
        private Transform _statsContainer;
        private string _selectedLeaderboardId = "";
        private List<LeaderboardEntry> _currentLeaderboardEntries = new List<LeaderboardEntry>();
        private string _testStatName = "test_stat";
        private int _testStatAmount = 1;
        private InputField _testStatNameInput;
        private InputField _testStatAmountInput;
        private string _rankedGameMode = "ranked";
        private string _rankedStatus = "";
        private InputField _rankedModeInput;

        // Tools tab state
        private Transform _toolsContainer;
        private string _testFileName = "test.txt";
        private string _testFileContent = "Hello, EOS!";
        private InputField _testFileNameInput;
        private InputField _testFileContentInput;
        private List<ReplayHeader> _cachedReplays = new List<ReplayHeader>();
        private float _lastReplayRefresh;
        private string _importPath = "";
        private InputField _importPathInput;
        private bool _showExportSuccess;
        private float _exportSuccessTime;
        private string _lfgTitle = "Looking for players";
        private string _lfgGameMode = "";
        private int _lfgDesiredSize = 4;
        private string _lfgStatus = "";
        private InputField _lfgTitleInput;
        private InputField _lfgModeInput;
        private Text _lfgSizeLabel;

        // Popup state
        private string _profilePuid = "";
        private string _profileNote = "";
        private bool _profileEditingNote;
        private string _profileStatus = "";
        private string _reportTargetPuid = "";
        private string _reportStatus = "";
        private int _reportCategoryIndex;
        private GameObject _popupOverlay;
        private GameObject _popupPanel;

        // Shared
        private Font _defaultFont;

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

        private void Start()
        {
            _panelVisible = EOSPlatformHelper.IsMobile;
            BuildUI();
        }

        private void Update()
        {
            // 3-finger tap toggle for mobile
            if (Input.touchCount == 3)
            {
                bool allBegan = true;
                for (int i = 0; i < 3; i++)
                {
                    if (Input.GetTouch(i).phase != TouchPhase.Began)
                        allBegan = false;
                }
                if (allBegan) TogglePanel();
            }

            // Update mic level bar smoothly
            if (_panelVisible && _currentTab == 2 && _micLevelFill != null)
            {
                var voice = EOSVoiceManager.Instance;
                float level = (voice != null && voice.IsConnected && !voice.IsMuted) ? voice.LocalMicLevel : 0f;
                var rt = _micLevelFill.rectTransform;
                rt.anchorMax = new Vector2(level, 1f);
                if (_micLevelText != null)
                    _micLevelText.text = $"{(level * 100):F0}%";
            }
        }

        #endregion

        #region UI Building

        private void BuildUI()
        {
            if (_built) return;
            _built = true;

            _defaultFont = Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");
            if (_defaultFont == null)
                _defaultFont = Resources.GetBuiltinResource<Font>("Arial.ttf");

            // Canvas
            var canvasGo = new GameObject("EOSCanvasUI_Canvas");
            canvasGo.transform.SetParent(transform);
            _canvas = canvasGo.AddComponent<Canvas>();
            _canvas.renderMode = RenderMode.ScreenSpaceOverlay;
            _canvas.sortingOrder = 9999;

            var scaler = canvasGo.AddComponent<CanvasScaler>();
            scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
            scaler.referenceResolution = new Vector2(540, 960);
            scaler.matchWidthOrHeight = 0.5f;

            canvasGo.AddComponent<GraphicRaycaster>();

            // EventSystem if none exists
            if (UnityEngine.EventSystems.EventSystem.current == null)
            {
                var esGo = new GameObject("EventSystem");
                esGo.transform.SetParent(transform);
                esGo.AddComponent<UnityEngine.EventSystems.EventSystem>();
#if EOS_HAS_INPUT_SYSTEM
                esGo.AddComponent<UnityEngine.InputSystem.UI.InputSystemUIInputModule>();
#else
                esGo.AddComponent<UnityEngine.EventSystems.StandaloneInputModule>();
#endif
            }

            // Toggle Button (bottom-right)
            _toggleButton = CreateToggleButton(canvasGo.transform);

            // Main Panel
            _mainPanel = CreateMainPanel(canvasGo.transform);
            _mainPanel.SetActive(_panelVisible);

            // Delay first refresh slightly so layout has a frame to settle
            Invoke(nameof(DoFirstRefresh), 0.1f);
            InvokeRepeating(nameof(RefreshActiveTab), 1.2f, 1f);
        }

        private void DoFirstRefresh()
        {
            RefreshActiveTab();
        }

        private GameObject CreateToggleButton(Transform parent)
        {
            var go = new GameObject("ToggleBtn");
            go.transform.SetParent(parent, false);
            var img = go.AddComponent<Image>();
            img.color = ColButton;

            var rt = go.GetComponent<RectTransform>();
            rt.anchorMin = new Vector2(1, 0);
            rt.anchorMax = new Vector2(1, 0);
            rt.pivot = new Vector2(1, 0);
            rt.anchoredPosition = new Vector2(-20, 20);
            rt.sizeDelta = new Vector2(90, 90);

            var btn = go.AddComponent<Button>();
            btn.targetGraphic = img;
            btn.navigation = new Navigation { mode = Navigation.Mode.None };
            var colors = btn.colors;
            colors.normalColor = ColButton;
            colors.highlightedColor = ColButtonHover;
            colors.pressedColor = ColTabActive;
            colors.fadeDuration = 0f;
            btn.colors = colors;
            btn.onClick.AddListener(TogglePanel);

            var txtGo = new GameObject("Label");
            txtGo.transform.SetParent(go.transform, false);
            var txt = txtGo.AddComponent<Text>();
            txt.text = "EOS";
            txt.font = _defaultFont;
            txt.fontSize = 22;
            txt.fontStyle = FontStyle.Bold;
            txt.color = Color.white;
            txt.alignment = TextAnchor.MiddleCenter;
            txt.raycastTarget = false;
            var txtRT = txtGo.GetComponent<RectTransform>();
            StretchFill(txtRT);

            return go;
        }

        private GameObject CreateMainPanel(Transform parent)
        {
            var panel = new GameObject("MainPanel");
            panel.transform.SetParent(parent, false);
            var panelImg = panel.AddComponent<Image>();
            panelImg.color = ColPanelBg;

            var rt = panel.GetComponent<RectTransform>();
            rt.anchorMin = new Vector2(0.02f, 0.04f);
            rt.anchorMax = new Vector2(0.98f, 0.96f);
            rt.offsetMin = Vector2.zero;
            rt.offsetMax = Vector2.zero;

            var vlg = panel.AddComponent<VerticalLayoutGroup>();
            vlg.padding = new RectOffset(8, 8, 8, 8);
            vlg.spacing = 4;
            vlg.childForceExpandWidth = true;
            vlg.childForceExpandHeight = false;
            vlg.childControlWidth = true;
            vlg.childControlHeight = true;

            // Title bar
            CreateTitleBar(panel.transform);

            // Tab bar
            CreateTabBar(panel.transform);

            // Tab content area (ScrollRect)
            CreateTabContentArea(panel.transform);

            return panel;
        }

        private void CreateTitleBar(Transform parent)
        {
            var bar = new GameObject("TitleBar");
            bar.transform.SetParent(parent, false);
            var barImg = bar.AddComponent<Image>();
            barImg.color = ColTitleBg;
            var barLE = bar.AddComponent<LayoutElement>();
            barLE.preferredHeight = 50;
            barLE.flexibleHeight = 0;
            barLE.flexibleWidth = 1;

            var hlg = bar.AddComponent<HorizontalLayoutGroup>();
            hlg.padding = new RectOffset(16, 8, 4, 4);
            hlg.spacing = 8;
            hlg.childForceExpandWidth = false;
            hlg.childForceExpandHeight = true;
            hlg.childControlWidth = true;
            hlg.childControlHeight = true;
            hlg.childAlignment = TextAnchor.MiddleLeft;

            // Title text
            var titleGo = new GameObject("Title");
            titleGo.transform.SetParent(bar.transform, false);
            var titleTxt = titleGo.AddComponent<Text>();
            titleTxt.text = "EOS Native";
            titleTxt.font = _defaultFont;
            titleTxt.fontSize = 22;
            titleTxt.fontStyle = FontStyle.Bold;
            titleTxt.color = ColHeader;
            titleTxt.alignment = TextAnchor.MiddleLeft;
            titleTxt.raycastTarget = false;
            var titleLE = titleGo.AddComponent<LayoutElement>();
            titleLE.flexibleWidth = 1;

            // Close button
            var closeGo = new GameObject("CloseBtn");
            closeGo.transform.SetParent(bar.transform, false);
            var closeImg = closeGo.AddComponent<Image>();
            closeImg.color = ColButtonDanger;
            var closeBtn = closeGo.AddComponent<Button>();
            closeBtn.targetGraphic = closeImg;
            closeBtn.navigation = new Navigation { mode = Navigation.Mode.None };
            closeBtn.onClick.AddListener(TogglePanel);
            var closeLE = closeGo.AddComponent<LayoutElement>();
            closeLE.preferredWidth = 44;
            closeLE.preferredHeight = 36;

            var closeTxtGo = new GameObject("X");
            closeTxtGo.transform.SetParent(closeGo.transform, false);
            var closeTxt = closeTxtGo.AddComponent<Text>();
            closeTxt.text = "X";
            closeTxt.font = _defaultFont;
            closeTxt.fontSize = 20;
            closeTxt.fontStyle = FontStyle.Bold;
            closeTxt.color = Color.white;
            closeTxt.alignment = TextAnchor.MiddleCenter;
            closeTxt.raycastTarget = false;
            StretchFill(closeTxtGo.GetComponent<RectTransform>());
        }

        private void CreateTabBar(Transform parent)
        {
            var bar = new GameObject("TabBar");
            bar.transform.SetParent(parent, false);
            var barImg = bar.AddComponent<Image>();
            barImg.color = new Color(0.06f, 0.06f, 0.10f, 1f);
            var tabBarLE = bar.AddComponent<LayoutElement>();
            tabBarLE.preferredHeight = 48;
            tabBarLE.flexibleHeight = 0;
            tabBarLE.flexibleWidth = 1;

            var hlg = bar.AddComponent<HorizontalLayoutGroup>();
            hlg.padding = new RectOffset(4, 4, 4, 4);
            hlg.spacing = 4;
            hlg.childForceExpandWidth = true;
            hlg.childForceExpandHeight = true;
            hlg.childControlWidth = true;
            hlg.childControlHeight = true;

            _tabButtons = new Button[TabNames.Length];
            _tabButtonImages = new Image[TabNames.Length];

            for (int i = 0; i < TabNames.Length; i++)
            {
                int tabIdx = i;
                var tabGo = new GameObject("Tab_" + TabNames[i]);
                tabGo.transform.SetParent(bar.transform, false);
                var tabImg = tabGo.AddComponent<Image>();
                tabImg.color = ColTabNormal;
                _tabButtonImages[i] = tabImg;

                var tabBtn = tabGo.AddComponent<Button>();
                tabBtn.targetGraphic = tabImg;
                tabBtn.navigation = new Navigation { mode = Navigation.Mode.None };
                tabBtn.onClick.AddListener(() => SelectTab(tabIdx));
                _tabButtons[i] = tabBtn;

                var tabTxtGo = new GameObject("Label");
                tabTxtGo.transform.SetParent(tabGo.transform, false);
                var tabTxt = tabTxtGo.AddComponent<Text>();
                tabTxt.text = TabNames[i];
                tabTxt.font = _defaultFont;
                tabTxt.fontSize = 15;
                tabTxt.fontStyle = FontStyle.Bold;
                tabTxt.color = ColText;
                tabTxt.alignment = TextAnchor.MiddleCenter;
                tabTxt.raycastTarget = false;
                StretchFill(tabTxtGo.GetComponent<RectTransform>());
            }

            UpdateTabButtonColors();
        }

        private void CreateTabContentArea(Transform parent)
        {
            // ScrollRect wrapper - takes all remaining space
            var scrollGo = new GameObject("ScrollArea", typeof(RectTransform));
            scrollGo.transform.SetParent(parent, false);
            var scrollRT = scrollGo.GetComponent<RectTransform>();
            // Fill remaining space below title+tabs
            scrollRT.anchorMin = new Vector2(0, 0);
            scrollRT.anchorMax = new Vector2(1, 1);
            // We can't use anchors properly inside VLG, so set sizeDelta
            scrollRT.sizeDelta = new Vector2(0, 0);

            // Make scroll area take all remaining space
            var scrollLE = scrollGo.AddComponent<LayoutElement>();
            scrollLE.flexibleHeight = 1;
            scrollLE.flexibleWidth = 1;

            var scrollRect = scrollGo.AddComponent<ScrollRect>();
            scrollRect.horizontal = false;
            scrollRect.vertical = true;
            scrollRect.movementType = ScrollRect.MovementType.Clamped;
            scrollRect.scrollSensitivity = 15f;

            // Viewport with Mask
            var viewport = new GameObject("Viewport");
            viewport.transform.SetParent(scrollGo.transform, false);
            var vpImg = viewport.AddComponent<Image>();
            vpImg.color = new Color(0, 0, 0, 0.01f); // Nearly invisible but needed for Mask
            vpImg.raycastTarget = true;
            var vpRT = viewport.GetComponent<RectTransform>();
            StretchFill(vpRT);
            viewport.AddComponent<RectMask2D>(); // RectMask2D is simpler and doesn't need Image alpha

            scrollRect.viewport = vpRT;

            // Content container
            var content = new GameObject("Content", typeof(RectTransform));
            content.transform.SetParent(viewport.transform, false);
            var contentRT = content.GetComponent<RectTransform>();
            contentRT.anchorMin = new Vector2(0, 1);
            contentRT.anchorMax = new Vector2(1, 1);
            contentRT.pivot = new Vector2(0.5f, 1);
            contentRT.sizeDelta = new Vector2(0, 0); // Will be sized by ContentSizeFitter

            var csf = content.AddComponent<ContentSizeFitter>();
            csf.verticalFit = ContentSizeFitter.FitMode.PreferredSize;
            csf.horizontalFit = ContentSizeFitter.FitMode.Unconstrained;

            var vlg = content.AddComponent<VerticalLayoutGroup>();
            vlg.padding = new RectOffset(6, 6, 6, 6);
            vlg.spacing = 6;
            vlg.childForceExpandWidth = true;
            vlg.childForceExpandHeight = false;
            vlg.childControlWidth = true;
            vlg.childControlHeight = true;

            scrollRect.content = contentRT;
            _scrollContentRT = contentRT;

            // Tab content panels
            _tabContents = new GameObject[TabNames.Length];
            for (int i = 0; i < TabNames.Length; i++)
            {
                var tabPanel = new GameObject($"Tab_{TabNames[i]}");
                tabPanel.transform.SetParent(content.transform, false);

                var tabVLG = tabPanel.AddComponent<VerticalLayoutGroup>();
                tabVLG.spacing = 8;
                tabVLG.childForceExpandWidth = true;
                tabVLG.childForceExpandHeight = false;
                tabVLG.childControlWidth = true;
                tabVLG.childControlHeight = true;

                _tabContents[i] = tabPanel;
                tabPanel.SetActive(i == _currentTab);
            }

            // Build static tab content
            BuildStatusTab(_tabContents[0].transform);
            BuildLobbiesTab(_tabContents[1].transform);
            BuildVoiceTab(_tabContents[2].transform);
            BuildSocialTab(_tabContents[3].transform);
            BuildStatsTab(_tabContents[4].transform);
            BuildToolsTab(_tabContents[5].transform);
        }

        #endregion

        #region Tab Building - Status

        private void BuildStatusTab(Transform parent)
        {
            _statusContainer = parent;
        }

        #endregion

        #region Tab Building - Lobbies

        private void BuildLobbiesTab(Transform parent)
        {
            // Lobby Info Section
            var infoSection = CreateSection(parent, "Current Lobby");
            _lobbyInfoContainer = infoSection.transform;

            // Create Lobby Section
            var createSection = CreateSection(parent, "Create Lobby");

            var nameRow = CreateRow(createSection.transform);
            AddLabel(nameRow.transform, "Name:", 15, ColDimText, 70);
            _lobbyNameInput = AddInputField(nameRow.transform, "Test Lobby");

            var settingsRow = CreateRow(createSection.transform, 32);
            AddLabel(settingsRow.transform, "Max:", 15, ColDimText, 50);
            _maxPlayersInput = AddInputField(settingsRow.transform, "4", 60);
            _maxPlayersInput.contentType = InputField.ContentType.IntegerNumber;
            _publicToggle = AddToggle(settingsRow.transform, "Public", true);
            _voiceToggle = AddToggle(settingsRow.transform, "Voice", true);
            _hostMigrationToggle = AddToggle(settingsRow.transform, "Migrate", true);

            AddButton(createSection.transform, "Host Lobby", ColButton, OnCreateLobby, 38);

            // Join Section
            var joinSection = CreateSection(parent, "Join / Quick Match");

            var joinRow = CreateRow(joinSection.transform);
            AddLabel(joinRow.transform, "Code:", 15, ColDimText, 55);
            _joinCodeInput = AddInputField(joinRow.transform, "ABCD", 90);
            _joinCodeInput.characterLimit = 4;
            AddButton(joinRow.transform, "Join", ColButton, OnJoinByCode, -1, 70);

            AddButton(joinSection.transform, "Quick Match", ColButton, OnQuickMatch, 38);

            // Search Section
            var searchSection = CreateSection(parent, "Search Lobbies");
            AddButton(searchSection.transform, "Search All", ColButton, OnSearchLobbies, 34);
            _lobbySearchContainer = searchSection.transform;

            // Members Section
            var membersSection = CreateSection(parent, "Lobby Members");
            _lobbyMembersContainer = membersSection.transform;

            // Chat Section
            var chatSection = CreateSection(parent, "Lobby Chat");
            _lobbyChatContainer = chatSection.transform;

            // Chat log — simple container, no ScrollRect/RectMask2D/ContentSizeFitter
            // (same fix as console: avoids circular layout dependency that causes flicker)
            var chatLogBg = CreatePanelGO(chatSection.transform, "ChatLog", new Color(0.06f, 0.06f, 0.10f, 1f));
            var chatLogLE = chatLogBg.AddComponent<LayoutElement>();
            chatLogLE.preferredHeight = 150;
            chatLogLE.flexibleWidth = 1;

            var chatLogGo = new GameObject("ChatText");
            chatLogGo.transform.SetParent(chatLogBg.transform, false);
            _lobbyChatLog = chatLogGo.AddComponent<Text>();
            _lobbyChatLog.font = _defaultFont;
            _lobbyChatLog.fontSize = 14;
            _lobbyChatLog.color = ColDimText;
            _lobbyChatLog.alignment = TextAnchor.UpperLeft;
            _lobbyChatLog.horizontalOverflow = HorizontalWrapMode.Wrap;
            _lobbyChatLog.verticalOverflow = VerticalWrapMode.Truncate;
            _lobbyChatLog.supportRichText = true;
            _lobbyChatLog.raycastTarget = false;
            var chatLogTextRT = chatLogGo.GetComponent<RectTransform>();
            chatLogTextRT.anchorMin = Vector2.zero;
            chatLogTextRT.anchorMax = Vector2.one;
            chatLogTextRT.offsetMin = new Vector2(6, 4);
            chatLogTextRT.offsetMax = new Vector2(-6, -4);

            // Chat input row
            var chatInputRow = CreateRow(chatSection.transform);
            _chatInputField = AddInputField(chatInputRow.transform, "Type message...");
            _chatInputField.onEndEdit.AddListener(text =>
            {
                if (!string.IsNullOrWhiteSpace(text))
                    OnSendChat();
            });
            AddButton(chatInputRow.transform, "Send", ColButton, OnSendChat, -1, 70);

            // Status text
            var statusGo = new GameObject("LobbyStatus");
            statusGo.transform.SetParent(parent, false);
            _lobbyStatusText = statusGo.AddComponent<Text>();
            _lobbyStatusText.font = _defaultFont;
            _lobbyStatusText.fontSize = 14;
            _lobbyStatusText.color = ColOrange;
            _lobbyStatusText.alignment = TextAnchor.MiddleLeft;
            _lobbyStatusText.raycastTarget = false;
            var statusLE = statusGo.AddComponent<LayoutElement>();
            statusLE.preferredHeight = 24;
            statusLE.flexibleWidth = 1;
        }

        #endregion

        #region Tab Building - Voice

        private void BuildVoiceTab(Transform parent)
        {
            var statusSection = CreateSection(parent, "Voice Status");
            _voiceStatusContainer = statusSection.transform;

            var micSection = CreateSection(parent, "Local Microphone");

            var micRow = CreateRow(micSection.transform, 22);
            AddLabel(micRow.transform, "Level:", 15, ColDimText, 60);

            // Level bar background
            var levelBarBg = CreatePanelGO(micRow.transform, "LevelBarBg", ColLevelBg);
            var levelBarBgLE = levelBarBg.AddComponent<LayoutElement>();
            levelBarBgLE.flexibleWidth = 1;
            levelBarBgLE.preferredHeight = 18;

            var levelFill = new GameObject("LevelFill");
            levelFill.transform.SetParent(levelBarBg.transform, false);
            _micLevelFill = levelFill.AddComponent<Image>();
            _micLevelFill.color = ColLevelFill;
            _micLevelFill.raycastTarget = false;
            var fillRT = levelFill.GetComponent<RectTransform>();
            fillRT.anchorMin = Vector2.zero;
            fillRT.anchorMax = new Vector2(0, 1); // Width controlled by anchorMax.x
            fillRT.offsetMin = Vector2.zero;
            fillRT.offsetMax = Vector2.zero;

            _micLevelText = AddLabel(micRow.transform, "0%", 14, ColDimText, 50);

            AddButton(micSection.transform, "Toggle Mute", ColButton, () =>
            {
                var voice = EOSVoiceManager.Instance;
                if (voice != null) voice.ToggleMute();
            }, 34);

            // Audio Devices section
            var deviceSection = CreateSection(parent, "Audio Devices");
            _audioDevicesContainer = deviceSection.transform;

            AddButton(deviceSection.transform, "Refresh Devices", ColButton, () =>
            {
                var voice = EOSVoiceManager.Instance;
                if (voice != null)
                {
                    voice.QueryAudioDevices();
                    _selectedInputDevice = -1;
                    _selectedOutputDevice = -1;
                }
            }, 30);

            var participantsSection = CreateSection(parent, "Participants");
            _voiceParticipantsContainer = participantsSection.transform;

            var diagSection = CreateSection(parent, "Voice Diagnostics");
            _voiceDiagContainer = diagSection.transform;

            var helpSection = CreateSection(parent, "Voice Info");
            AddLabel(helpSection.transform, "Voice is lobby-based. Create a lobby with Voice enabled.", 13, ColDimText);
            AddLabel(helpSection.transform, "Devices auto-queried on connect. Press Refresh to re-scan.", 13, ColDimText);
        }

        #endregion

        #region Tab Building - Social

        private void BuildSocialTab(Transform parent)
        {
            _socialContainer = parent;
        }

        #endregion

        #region Tab Building - Stats

        private void BuildStatsTab(Transform parent)
        {
            _statsContainer = parent;
        }

        #endregion

        #region Tab Building - Tools

        private void BuildToolsTab(Transform parent)
        {
            _toolsContainer = parent;
        }

        #endregion

        #region Tab Selection

        private void SelectTab(int index)
        {
            _currentTab = index;
            for (int i = 0; i < _tabContents.Length; i++)
                _tabContents[i].SetActive(i == index);
            UpdateTabButtonColors();
            RefreshActiveTab();
        }

        private void UpdateTabButtonColors()
        {
            for (int i = 0; i < _tabButtonImages.Length; i++)
                _tabButtonImages[i].color = (i == _currentTab) ? ColTabActive : ColTabNormal;
        }

        private void TogglePanel()
        {
            _panelVisible = !_panelVisible;
            if (_mainPanel != null) _mainPanel.SetActive(_panelVisible);
            if (_panelVisible) RefreshActiveTab();
        }

        #endregion

        #region Refresh Logic

        private void RefreshActiveTab()
        {
            if (!_panelVisible || _tabContents == null) return;

            switch (_currentTab)
            {
                case 0: RefreshStatusTab(); break;
                case 1: RefreshLobbiesTab(); break;
                case 2: RefreshVoiceTab(); break;
                case 3: RefreshSocialTab(); break;
                case 4: RefreshStatsTab(); break;
                case 5: RefreshToolsTab(); break;
            }

            // Force layout rebuild so ContentSizeFitter recalculates
            if (_scrollContentRT != null)
            {
                Canvas.ForceUpdateCanvases();
                LayoutRebuilder.ForceRebuildLayoutImmediate(_scrollContentRT);
            }
        }

        private void RefreshStatusTab()
        {
            if (_statusContainer == null) return;
            ClearChildren(_statusContainer);

            var mgr = EOSManager.Instance;

            // SDK Status
            var section = CreateSection(_statusContainer, "SDK Status");
            AddStatusRow(section.transform, "EOS SDK", mgr != null && mgr.IsInitialized, "Initialized", "Not Initialized");
            AddStatusRow(section.transform, "Login", mgr != null && mgr.IsLoggedIn, "Logged In", "Not Logged In");
            AddStatusRow(section.transform, "Epic Account", mgr != null && mgr.IsEpicAccountLoggedIn, "Connected", "Not Connected");

            if (mgr != null && mgr.IsLoggedIn && mgr.LocalProductUserId != null)
            {
                string puid = mgr.LocalProductUserId.ToString();
                var puidRow = CreateRow(section.transform);
                AddLabel(puidRow.transform, "PUID:", 14, ColDimText, 55);
                AddLabel(puidRow.transform, puid.Length > 20 ? puid.Substring(0, 20) + "..." : puid, 13, ColText);
                AddButton(puidRow.transform, "Copy", ColButton, () => GUIUtility.systemCopyBuffer = puid, -1, 60);
            }

            if (mgr != null && mgr.IsInitialized)
            {
                AddKVRow(section.transform, "Network", mgr.GetNetworkStatus().ToString());
                AddKVRow(section.transform, "App Status", mgr.GetApplicationStatus().ToString());
            }

            // Platform
            var platSection = CreateSection(_statusContainer, "Platform");
            AddKVRow(platSection.transform, "Platform", $"{EOSPlatformHelper.CurrentPlatform} ({EOSPlatformHelper.PlatformId})");
            AddKVRow(platSection.transform, "Device", SystemInfo.deviceModel);
            AddKVRow(platSection.transform, "Overlay", EOSPlatformHelper.SupportsOverlay ? "Yes" : "No");
            AddKVRow(platSection.transform, "Voice", EOSPlatformHelper.SupportsVoice ? "Yes" : "No");

            // Interfaces
            if (mgr != null && mgr.IsInitialized)
            {
                var ifSection = CreateSection(_statusContainer, "Interfaces");
                var row1 = CreateRow(ifSection.transform, 28);
                AddBadge(row1.transform, "Connect", mgr.ConnectInterface != null);
                AddBadge(row1.transform, "P2P", mgr.P2PInterface != null);
                AddBadge(row1.transform, "Lobby", mgr.LobbyInterface != null);
                AddBadge(row1.transform, "RTC", mgr.RTCInterface != null);

                var row2 = CreateRow(ifSection.transform, 28);
                AddBadge(row2.transform, "Audio", mgr.RTCAudioInterface != null);
                AddBadge(row2.transform, "Auth", mgr.AuthInterface != null);
                AddBadge(row2.transform, "Friends", mgr.FriendsInterface != null);
                AddBadge(row2.transform, "Stats", mgr.StatsInterface != null);
            }

            // Actions
            var actSection = CreateSection(_statusContainer, "Actions");

            bool canInit = mgr != null && !mgr.IsInitialized;
            bool canLogin = mgr != null && mgr.IsInitialized && !mgr.IsLoggedIn;
            bool canLogout = mgr != null && mgr.IsLoggedIn;

            var actRow1 = CreateRow(actSection.transform, 36);
            var initBtn = AddButton(actRow1.transform, "Initialize", ColButton, InitializeFromResources);
            initBtn.GetComponent<Button>().interactable = canInit;
            var loginBtn = AddButton(actRow1.transform, "Device Login", ColButton, () =>
            {
                if (mgr != null) _ = mgr.LoginWithDeviceTokenAsync("Player");
            });
            loginBtn.GetComponent<Button>().interactable = canLogin;

            var actRow2 = CreateRow(actSection.transform, 36);
            var smartBtn = AddButton(actRow2.transform, "Smart Login", ColButton, () =>
            {
                if (mgr != null) _ = mgr.LoginSmartAsync("Player");
            });
            smartBtn.GetComponent<Button>().interactable = canLogin;
            var logoutBtn = AddButton(actRow2.transform, "Logout", ColButtonDanger, () =>
            {
                if (mgr != null) _ = mgr.LogoutAsync();
            });
            logoutBtn.GetComponent<Button>().interactable = canLogout;
        }

        private void RefreshLobbiesTab()
        {
            if (_lobbyInfoContainer == null) return;

            var mgr = EOSManager.Instance;
            var lobbyMgr = EOSLobbyManager.Instance;

            ClearChildren(_lobbyInfoContainer, 1); // Keep header

            if (mgr == null || !mgr.IsLoggedIn)
            {
                AddLabel(_lobbyInfoContainer, "Login required to use lobbies.", 15, ColYellow);
                return;
            }

            if (lobbyMgr != null && lobbyMgr.IsInLobby)
            {
                var lobby = lobbyMgr.CurrentLobby;
                var codeRow = CreateRow(_lobbyInfoContainer);
                AddLabel(codeRow.transform, "Join Code:", 14, ColDimText, 85);
                AddLabel(codeRow.transform, lobby.JoinCode ?? "????", 16, ColGreen);
                AddButton(codeRow.transform, "Copy", ColButton, () => GUIUtility.systemCopyBuffer = lobby.JoinCode ?? "", -1, 60);

                AddKVRow(_lobbyInfoContainer, "Role", lobbyMgr.IsOwner ? "HOST" : "CLIENT", lobbyMgr.IsOwner ? ColGreen : ColHeader);
                AddKVRow(_lobbyInfoContainer, "Members", $"{lobby.MemberCount} / {lobby.MaxMembers}");
                AddKVRow(_lobbyInfoContainer, "Public", lobby.IsPublic.ToString());
                AddKVRow(_lobbyInfoContainer, "Voice", EOSVoiceManager.Instance?.IsVoiceEnabled == true ? "Yes" : "No");

                AddButton(_lobbyInfoContainer, "Leave Lobby", ColButtonDanger, () =>
                {
                    if (lobbyMgr != null) _ = lobbyMgr.LeaveLobbyAsync();
                    SetLobbyStatus("Left lobby.");
                }, 34);
            }
            else
            {
                AddLabel(_lobbyInfoContainer, "Not in a lobby.", 14, ColDimText);
            }

            RefreshLobbyMembers();
            RefreshLobbyChat();
        }

        private void RefreshLobbyMembers()
        {
            if (_lobbyMembersContainer == null) return;
            ClearChildren(_lobbyMembersContainer, 1);

            var lobbyMgr = EOSLobbyManager.Instance;
            if (lobbyMgr == null || !lobbyMgr.IsInLobby)
            {
                AddLabel(_lobbyMembersContainer, "Join a lobby to see members.", 14, ColDimText);
                return;
            }

            var lobby = lobbyMgr.CurrentLobby;
            string ownerPuid = lobby.OwnerPuid;
            string localPuid = EOSManager.Instance?.LocalProductUserId?.ToString();

            var lobbyInterface = EOSManager.Instance?.LobbyInterface;
            if (lobbyInterface == null) return;

            var detailsOptions = new Epic.OnlineServices.Lobby.CopyLobbyDetailsHandleOptions
            {
                LocalUserId = EOSManager.Instance.LocalProductUserId,
                LobbyId = lobby.LobbyId
            };

            if (lobbyInterface.CopyLobbyDetailsHandle(ref detailsOptions, out var details) == Result.Success && details != null)
            {
                var countOptions = new Epic.OnlineServices.Lobby.LobbyDetailsGetMemberCountOptions();
                uint memberCount = details.GetMemberCount(ref countOptions);

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

                    var row = CreateRow(_lobbyMembersContainer);
                    string prefix = isOwner ? "[HOST] " : "";
                    string suffix = isLocal ? " (you)" : "";
                    Color nameColor = isLocal ? ColHeader : (isOwner ? ColGreen : ColText);
                    AddLabel(row.transform, $"{prefix}{displayName}{suffix}", 15, nameColor);

                    // Truncated PUID
                    string shortPuid = memberPuid.Length > 12 ? memberPuid.Substring(0, 12) + "..." : memberPuid;
                    AddLabel(row.transform, shortPuid, 11, ColDimText);

                    if (!isLocal)
                    {
                        string infoPuid = memberPuid;
                        AddButton(row.transform, "i", ColButton, () => ShowProfilePopup(infoPuid), -1, 30);
                    }

                    if (!isLocal && lobbyMgr.IsOwner)
                    {
                        string kickPuid = memberPuid;
                        AddButton(row.transform, "Kick", ColButtonDanger, () =>
                        {
                            _ = lobbyMgr.KickMemberAsync(kickPuid);
                        }, -1, 60);
                    }
                }

                details.Release();
            }
        }

        private void RefreshLobbyChat()
        {
            if (_lobbyChatLog == null) return;

            var chatMgr = EOSLobbyChatManager.Instance;
            if (chatMgr == null)
            {
                _lobbyChatLog.text = "Chat manager not available.";
                return;
            }

            var messages = chatMgr.Messages;
            if (messages.Count == 0)
            {
                _lobbyChatLog.text = "No messages yet.";
                return;
            }

            var sb = new StringBuilder();
            int startIdx = Mathf.Max(0, messages.Count - 30);
            for (int i = startIdx; i < messages.Count; i++)
            {
                var msg = messages[i];
                if (msg.IsSystem)
                    sb.AppendLine($"<color=#888888>* {msg.Message}</color>");
                else
                    sb.AppendLine($"<color=#666666>[{msg.LocalTime:HH:mm}]</color> <color=#66ccff>{msg.SenderName}</color>: {msg.Message}");
            }

            _lobbyChatLog.text = sb.ToString();
        }

        private void RefreshVoiceTab()
        {
            if (_voiceStatusContainer == null) return;
            ClearChildren(_voiceStatusContainer, 1);

            var mgr = EOSManager.Instance;
            if (mgr == null || !mgr.IsLoggedIn)
            {
                AddLabel(_voiceStatusContainer, "Login required for voice.", 15, ColYellow);
                return;
            }

            var voice = EOSVoiceManager.Instance;
            if (voice == null)
            {
                AddLabel(_voiceStatusContainer, "EOSVoiceManager not found.", 14, ColDimText);
                AddLabel(_voiceStatusContainer, "Join a lobby with voice enabled.", 14, ColDimText);
                return;
            }

            AddStatusRow(_voiceStatusContainer, "Connected", voice.IsConnected, "Connected", "Disconnected");
            AddStatusRow(_voiceStatusContainer, "Mic", !voice.IsMuted, "Active", "Muted");
            AddStatusRow(_voiceStatusContainer, "Voice Enabled", voice.IsVoiceEnabled, "Yes", "No");
            AddKVRow(_voiceStatusContainer, "Room", voice.CurrentRoomName ?? "None");
            AddKVRow(_voiceStatusContainer, "Participants", voice.ParticipantCount.ToString());

            RefreshAudioDevices(voice);
            RefreshVoiceParticipants();
            RefreshVoiceDiagnostics(voice);
        }

        private void RefreshAudioDevices(EOSVoiceManager voice)
        {
            if (_audioDevicesContainer == null) return;
            // Keep the header (index 0), separator (index 1), and Refresh button (index 2)
            ClearChildren(_audioDevicesContainer, 3);

            // Input devices (Microphones)
            AddLabel(_audioDevicesContainer, "Input (Mic):", 15, ColHeader);
            if (voice.InputDevices.Count > 0)
            {
                for (int i = 0; i < voice.InputDevices.Count; i++)
                {
                    var device = voice.InputDevices[i];
                    string label = device.DeviceName?.ToString() ?? $"Device {i}";
                    if (device.DefaultDevice) label += " (default)";

                    bool isSelected = (_selectedInputDevice == i) ||
                                      (_selectedInputDevice == -1 && device.DefaultDevice);

                    int idx = i;
                    string devId = device.DeviceId?.ToString();
                    Color btnColor = isSelected ? ColGreen : ColInputBg;
                    AddButton(_audioDevicesContainer, label, btnColor, () =>
                    {
                        _selectedInputDevice = idx;
                        voice.SetInputDevice(devId);
                    }, 28);
                }
            }
            else
            {
                AddLabel(_audioDevicesContainer, "No input devices. Press Refresh.", 13, ColDimText);
            }

            // Output devices (Speakers)
            AddLabel(_audioDevicesContainer, "Output (Speaker):", 15, ColHeader);
            if (voice.OutputDevices.Count > 0)
            {
                for (int i = 0; i < voice.OutputDevices.Count; i++)
                {
                    var device = voice.OutputDevices[i];
                    string label = device.DeviceName?.ToString() ?? $"Device {i}";
                    if (device.DefaultDevice) label += " (default)";

                    bool isSelected = (_selectedOutputDevice == i) ||
                                      (_selectedOutputDevice == -1 && device.DefaultDevice);

                    int idx = i;
                    string devId = device.DeviceId?.ToString();
                    Color btnColor = isSelected ? ColGreen : ColInputBg;
                    AddButton(_audioDevicesContainer, label, btnColor, () =>
                    {
                        _selectedOutputDevice = idx;
                        voice.SetOutputDevice(devId);
                    }, 28);
                }
            }
            else
            {
                AddLabel(_audioDevicesContainer, "No output devices. Press Refresh.", 13, ColDimText);
            }
        }

        private void RefreshVoiceParticipants()
        {
            if (_voiceParticipantsContainer == null) return;
            ClearChildren(_voiceParticipantsContainer, 1);

            var voice = EOSVoiceManager.Instance;
            if (voice == null || !voice.IsConnected)
            {
                AddLabel(_voiceParticipantsContainer, "Not connected to voice.", 14, ColDimText);
                return;
            }

            var participants = voice.GetAllParticipants();
            if (participants.Count == 0)
            {
                AddLabel(_voiceParticipantsContainer, "No participants yet.", 14, ColDimText);
                return;
            }

            foreach (var puid in participants)
            {
                bool speaking = voice.IsSpeaking(puid);
                var audioStatus = voice.GetParticipantAudioStatus(puid);
                string displayName = EOSPlayerRegistry.Instance?.GetPlayerName(puid)
                    ?? (puid.Length > 16 ? puid.Substring(0, 12) + "..." : puid);

                var row = CreateRow(_voiceParticipantsContainer);
                AddLabel(row.transform, speaking ? "\u25CF SPEAK" : "\u25CB silent", 13,
                    speaking ? ColGreen : ColDimText, 80);
                AddLabel(row.transform, displayName, 14, ColText);
                AddLabel(row.transform, audioStatus.ToString(), 12, ColDimText, 75);
            }
        }

        private void RefreshVoiceDiagnostics(EOSVoiceManager voice)
        {
            if (_voiceDiagContainer == null) return;
            ClearChildren(_voiceDiagContainer, 1);

            var eosMgr = EOSManager.Instance;

            AddStatusRow(_voiceDiagContainer, "RTC Interface", eosMgr?.RTCInterface != null, "OK", "NULL");
            AddStatusRow(_voiceDiagContainer, "RTCAudio Interface", eosMgr?.RTCAudioInterface != null, "OK", "NULL");
            AddKVRow(_voiceDiagContainer, "Local AudioStatus", voice.LocalAudioStatus.ToString());
            AddKVRow(_voiceDiagContainer, "UpdateSending", voice.LastUpdateSendingResult.ToString());
            AddKVRow(_voiceDiagContainer, "Devices Queried", voice.AudioDevicesQueried ? "Yes" : "No");
            AddKVRow(_voiceDiagContainer, "Input Devices", voice.InputDevices.Count.ToString());
            AddKVRow(_voiceDiagContainer, "Output Devices", voice.OutputDevices.Count.ToString());

#if UNITY_ANDROID && !UNITY_EDITOR
            AddStatusRow(_voiceDiagContainer, "Android Java Init", eosMgr?.AndroidJavaInitSuccess ?? false, "OK", "FAILED");
            if (!(eosMgr?.AndroidJavaInitSuccess ?? true) && !string.IsNullOrEmpty(eosMgr?.AndroidJavaInitError))
            {
                AddLabel(_voiceDiagContainer, eosMgr.AndroidJavaInitError, 12, ColRed);
            }
#endif

            if (voice.LocalAudioStatus == RTCAudioStatus.Unsupported)
            {
                AddLabel(_voiceDiagContainer, "! AudioStatus=Unsupported means no audio devices.", 13, ColYellow);
                AddLabel(_voiceDiagContainer, "  Java audio pipeline may not have initialized.", 13, ColYellow);
            }
            if (voice.AudioDevicesQueried && voice.InputDevices.Count == 0 && voice.OutputDevices.Count == 0)
            {
                AddLabel(_voiceDiagContainer, "! No audio devices found by EOS SDK.", 13, ColYellow);
                AddLabel(_voiceDiagContainer, "  Platform audio API may not be available.", 13, ColYellow);
            }
        }

        private void RefreshSocialTab()
        {
            if (_socialContainer == null) return;
            ClearChildren(_socialContainer);

            var mgr = EOSManager.Instance;
            if (mgr == null || !mgr.IsLoggedIn)
            {
                AddLabel(_socialContainer, "Login required for social features.", 15, ColYellow);
                return;
            }

            var registry = EOSPlayerRegistry.Instance;

            // Player Registry
            var regSection = CreateSection(_socialContainer, "Player Registry");
            if (registry != null)
            {
                AddKVRow(regSection.transform, "Cached", registry.CachedPlayerCount.ToString());
                AddKVRow(regSection.transform, "Friends", registry.FriendCount.ToString());
                AddKVRow(regSection.transform, "Blocked", registry.BlockedCount.ToString());
            }
            else
            {
                AddLabel(regSection.transform, "EOSPlayerRegistry not found.", 14, ColDimText);
            }

            if (registry != null)
            {
                // Recently Played
                var recentSection = CreateSection(_socialContainer, "Recently Played");
                var recent = registry.GetRecentPlayers(7);
                if (recent.Count == 0)
                {
                    AddLabel(recentSection.transform, "No recent players.", 14, ColDimText);
                }
                else
                {
                    int shown = 0;
                    foreach (var (puid, name, lastSeen) in recent)
                    {
                        if (registry.IsBlocked(puid)) continue;
                        if (shown++ >= 10) break;
                        var row = CreateRow(recentSection.transform);
                        AddLabel(row.transform, name, 14, ColText);
                        bool isFriend = registry.IsFriend(puid);
                        string friendPuid = puid;
                        AddButton(row.transform, isFriend ? "Unfriend" : "Friend",
                            isFriend ? ColButtonDanger : ColButton, () => registry.ToggleFriend(friendPuid), -1, 70);
                        AddButton(row.transform, "Block", ColButtonDanger, () => registry.BlockPlayer(friendPuid), -1, 55);

                        var invitesManager = EOSCustomInvites.Instance;
                        if (invitesManager != null && invitesManager.IsReady && !string.IsNullOrEmpty(invitesManager.CurrentPayload))
                        {
                            string invPuid = puid;
                            string invName = name;
                            AddButton(row.transform, "Inv", ColButton, () => SendInviteToPuid(invPuid, invName), -1, 40);
                        }
                    }

                    var clearRow = CreateRow(recentSection.transform, 30);
                    AddButton(clearRow.transform, "Clear", ColButtonDanger, () => registry.ClearCache(), 28, 70);
                }

                // Local Friends
                var friendSection = CreateSection(_socialContainer, "Local Friends");
                var friends = registry.GetFriends();
                if (friends.Count == 0)
                {
                    AddLabel(friendSection.transform, "No friends added.", 14, ColDimText);
                }
                else
                {
                    foreach (var (puid, name) in friends)
                    {
                        var (status, lobbyCode) = registry.GetFriendStatusWithLobby(puid);
                        Color statusColor = status switch
                        {
                            FriendStatus.InGame => ColGreen,
                            FriendStatus.InLobby => ColHeader,
                            _ => ColDimText
                        };
                        string icon = status == FriendStatus.InLobby || status == FriendStatus.InGame ? "\u25CF" : "\u25CB";

                        var row = CreateRow(friendSection.transform);
                        AddLabel(row.transform, $"{icon} {name}", 14, statusColor);

                        // Note display
                        string note = registry.GetNote(puid);
                        bool isEditingThis = _editingNotePuid == puid;
                        if (!isEditingThis)
                        {
                            string noteDisplay = !string.IsNullOrEmpty(note)
                                ? (note.Length > 6 ? note.Substring(0, 5) + ".." : note)
                                : "--";
                            Color noteColor = !string.IsNullOrEmpty(note) ? ColHeader : ColDimText;
                            AddLabel(row.transform, noteDisplay, 12, noteColor, 45);
                            string editPuid = puid;
                            AddButton(row.transform, "\u270E", ColButton, () =>
                            {
                                _editingNotePuid = editPuid;
                                _editingNoteText = note ?? "";
                            }, -1, 28);
                        }

                        // Join friend lobby
                        if (!isEditingThis && status == FriendStatus.InGame && !string.IsNullOrEmpty(lobbyCode))
                        {
                            string joinCode = lobbyCode;
                            AddButton(row.transform, "Join", ColButton, () => JoinFriendLobbyAsync(joinCode), -1, 45);
                        }

                        // Invite
                        if (!isEditingThis)
                        {
                            var invMgr = EOSCustomInvites.Instance;
                            if (status != FriendStatus.InLobby && invMgr != null && invMgr.IsReady && !string.IsNullOrEmpty(invMgr.CurrentPayload))
                            {
                                string invPuid = puid;
                                string invName = name;
                                AddButton(row.transform, "Inv", ColButton, () => SendInviteToPuid(invPuid, invName), -1, 35);
                            }
                        }

                        string removePuid = puid;
                        if (!isEditingThis)
                            AddButton(row.transform, "X", ColButtonDanger, () => registry.RemoveFriend(removePuid), -1, 28);

                        // Inline note edit row
                        if (isEditingThis)
                        {
                            var editRow = CreateRow(friendSection.transform, 30);
                            _editingNoteInput = AddInputField(editRow.transform, "Note...");
                            _editingNoteInput.text = _editingNoteText;
                            string savePuid = puid;
                            AddButton(editRow.transform, "Save", ColButton, () =>
                            {
                                registry.SetNote(savePuid, _editingNoteInput?.text ?? "");
                                _editingNotePuid = null;
                                _editingNoteText = "";
                            }, -1, 50);
                            AddButton(editRow.transform, "X", ColButtonDanger, () =>
                            {
                                _editingNotePuid = null;
                                _editingNoteText = "";
                            }, -1, 28);
                        }
                    }
                }

                // Friends footer
                var friendFooter = CreateRow(friendSection.transform, 30);
                AddButton(friendFooter.transform, "Refresh", ColButton, () =>
                {
                    _ = registry.RefreshAllFriendStatusesAsync();
                }, 28, 70);
                var storageForSync = EOSPlayerDataStorage.Instance;
                bool canSync = storageForSync != null && storageForSync.IsReady && !registry.IsCloudSyncInProgress;
                if (canSync)
                {
                    AddButton(friendFooter.transform, "Cloud Sync", ColButton, () =>
                    {
                        _ = registry.FullCloudSyncAsync();
                    }, 28, 90);
                }
                AddButton(friendFooter.transform, "Clear", ColButtonDanger, () => registry.ClearFriends(), 28, 55);

                // Blocked Players
                var blockedSection = CreateSection(_socialContainer, "Blocked Players");
                var blocked = registry.GetBlockedPlayers();
                if (blocked.Count == 0)
                {
                    AddLabel(blockedSection.transform, "No blocked players.", 14, ColDimText);
                }
                else
                {
                    foreach (var (puid, name) in blocked)
                    {
                        var row = CreateRow(blockedSection.transform);
                        AddLabel(row.transform, name, 14, ColRed);
                        string unblockPuid = puid;
                        AddButton(row.transform, "Unblock", ColButton, () => registry.UnblockPlayer(unblockPuid), -1, 80);
                    }
                    AddButton(blockedSection.transform, "Clear All", ColButtonDanger, () => registry.ClearBlocked(), 28);
                }

                // Invites
                var invitesManager2 = EOSCustomInvites.Instance;
                if (invitesManager2 != null)
                {
                    var invSection = CreateSection(_socialContainer, "Invites");

                    if (!invitesManager2.IsReady)
                    {
                        AddLabel(invSection.transform, "Waiting for EOS login...", 14, ColDimText);
                    }
                    else
                    {
                        // Payload
                        string payload = invitesManager2.CurrentPayload;
                        AddKVRow(invSection.transform, "Payload", string.IsNullOrEmpty(payload) ? "(not set)" : payload);

                        var lobbyMgrInv = EOSLobbyManager.Instance;
                        if (lobbyMgrInv != null && lobbyMgrInv.IsInLobby)
                        {
                            AddButton(invSection.transform, "Set Lobby Code", ColButton, () =>
                            {
                                invitesManager2.SetLobbyPayload();
                                _inviteStatus = "Payload set";
                            }, 28);
                        }

                        // Send invite
                        var sendRow = CreateRow(invSection.transform, 32);
                        AddLabel(sendRow.transform, "To:", 14, ColDimText, 25);
                        var recipientInput = AddInputField(sendRow.transform, "PUID...");
                        recipientInput.text = _inviteRecipientPuid;
                        recipientInput.onEndEdit.AddListener(t => _inviteRecipientPuid = t);
                        bool canSend = !string.IsNullOrWhiteSpace(_inviteRecipientPuid) && !string.IsNullOrEmpty(payload);
                        var sendBtn = AddButton(sendRow.transform, "Send", ColButton, () =>
                        {
                            _inviteRecipientPuid = recipientInput.text;
                            SendInviteToRecipient();
                        }, -1, 55);
                        sendBtn.GetComponent<Button>().interactable = canSend;

                        // Quick send to friends
                        if (registry.FriendCount > 0 && !string.IsNullOrEmpty(payload))
                        {
                            var quickRow = CreateRow(invSection.transform, 28);
                            int fShown = 0;
                            foreach (var (fpuid, fname) in registry.GetFriends())
                            {
                                if (fShown++ >= 4) break;
                                string btnText = fname.Length > 10 ? fname.Substring(0, 8) + ".." : fname;
                                string iFpuid = fpuid;
                                string iFname = fname;
                                AddButton(quickRow.transform, btnText, ColButton, () => SendInviteToPuid(iFpuid, iFname), 26, 75);
                            }
                        }

                        // Received invites
                        if (invitesManager2.PendingInvites.Count > 0)
                        {
                            AddLabel(invSection.transform, $"Received ({invitesManager2.PendingInvites.Count})", 15, ColYellow);
                            foreach (var kvp in invitesManager2.PendingInvites)
                            {
                                var invite = kvp.Value;
                                string shortSender = invite.SenderId?.ToString();
                                if (shortSender?.Length > 16) shortSender = shortSender.Substring(0, 8) + "...";
                                var iRow = CreateRow(invSection.transform, 28);
                                AddLabel(iRow.transform, $"From: {shortSender}", 13, ColDimText);
                                if (!string.IsNullOrEmpty(invite.Payload))
                                    AddLabel(iRow.transform, invite.Payload, 13, ColHeader, 50);
                                string iKey = kvp.Key;
                                var iData = invite;
                                AddButton(iRow.transform, "Accept", ColButton, () => AcceptInviteAndJoin(iKey, iData), -1, 60);
                                AddButton(iRow.transform, "Reject", ColButtonDanger, () => invitesManager2.RejectInvite(iKey), -1, 60);
                            }
                        }

                        // Join requests
                        if (invitesManager2.PendingRequests.Count > 0)
                        {
                            AddLabel(invSection.transform, $"Join Requests ({invitesManager2.PendingRequests.Count})", 15, ColYellow);
                            foreach (var kvp in invitesManager2.PendingRequests)
                            {
                                var request = kvp.Value;
                                string shortFrom = request.FromUserId?.ToString();
                                if (shortFrom?.Length > 16) shortFrom = shortFrom.Substring(0, 8) + "...";
                                var rRow = CreateRow(invSection.transform, 28);
                                AddLabel(rRow.transform, $"From: {shortFrom}", 13, ColDimText);
                                AddButton(rRow.transform, "Accept", ColButton, () =>
                                {
                                    _ = invitesManager2.AcceptRequestToJoinAsync(request.FromUserId);
                                }, -1, 60);
                                AddButton(rRow.transform, "Reject", ColButtonDanger, () =>
                                {
                                    _ = invitesManager2.RejectRequestToJoinAsync(request.FromUserId);
                                }, -1, 60);
                            }
                        }

                        if (!string.IsNullOrEmpty(_inviteStatus))
                            AddLabel(invSection.transform, _inviteStatus, 13, ColOrange);
                    }
                }
            }

            // Epic Account
            var epicAcctSection = CreateSection(_socialContainer, "Epic Account");
            if (mgr.IsEpicAccountLoggedIn)
            {
                AddStatusRow(epicAcctSection.transform, "Status", true, "Connected", "Disconnected");
                AddButton(epicAcctSection.transform, "Logout Epic Account", ColButtonDanger, () =>
                {
                    _ = mgr.LogoutEpicAccountAsync();
                }, 30);
            }
            else
            {
                AddStatusRow(epicAcctSection.transform, "Status", false, "Connected", "Not Connected");
                AddLabel(epicAcctSection.transform, "Enables: Friends, Presence, Achievements", 13, ColDimText);
                AddButton(epicAcctSection.transform, "Login with Epic", ColButton, () =>
                {
                    _ = mgr.LoginWithEpicAccountAsync();
                }, 34);
            }

            // Epic Friends
            if (mgr.IsEpicAccountLoggedIn)
            {
                var epicSection = CreateSection(_socialContainer, "Epic Friends");
                var epicFriends = EOSFriends.Instance;
                if (epicFriends != null && epicFriends.IsReady && epicFriends.Friends != null)
                {
                    AddButton(epicSection.transform, "Refresh Friends", ColButton, () =>
                    {
                        _ = epicFriends.QueryFriendsAsync();
                    }, 30);

                    foreach (var friend in epicFriends.Friends)
                    {
                        var row = CreateRow(epicSection.transform);
                        string displayName = friend.DisplayName ?? friend.AccountId?.ToString() ?? "Unknown";
                        string statusIcon = friend.Status switch
                        {
                            Epic.OnlineServices.Friends.FriendsStatus.Friends => "\u2714",
                            Epic.OnlineServices.Friends.FriendsStatus.InviteSent => "\u27A1",
                            Epic.OnlineServices.Friends.FriendsStatus.InviteReceived => "\u2709",
                            _ => "\u2022"
                        };
                        Color friendColor = friend.Status == Epic.OnlineServices.Friends.FriendsStatus.Friends ? ColGreen : ColDimText;
                        AddLabel(row.transform, $"{statusIcon} {displayName}", 14, friendColor);

                        if (friend.Status == Epic.OnlineServices.Friends.FriendsStatus.InviteReceived)
                        {
                            var fAcctId = friend.AccountId;
                            AddButton(row.transform, "Accept", ColButton, () => { _ = epicFriends.AcceptInviteAsync(fAcctId); }, -1, 60);
                            AddButton(row.transform, "Reject", ColButtonDanger, () => { _ = epicFriends.RejectInviteAsync(fAcctId); }, -1, 60);
                        }
                        else
                        {
                            AddLabel(row.transform, friend.Status.ToString(), 12, ColDimText, 75);
                        }
                    }
                }
                else
                {
                    AddLabel(epicSection.transform, "Epic Friends not available.", 14, ColDimText);
                }
            }
        }

        #endregion

        #region Stats Tab Refresh

        private void RefreshStatsTab()
        {
            if (_statsContainer == null) return;
            ClearChildren(_statsContainer);

            var mgr = EOSManager.Instance;
            if (mgr == null || !mgr.IsLoggedIn)
            {
                AddLabel(_statsContainer, "Login required for stats.", 15, ColYellow);
                return;
            }

            // Network Stats
            var netStats = NetworkStats._instance;
            var netSection = CreateSection(_statsContainer, "Network Stats");
            if (netStats == null)
            {
                AddLabel(netSection.transform, "NetworkStats not active.", 14, ColDimText);
            }
            else
            {
                var global = netStats.GetGlobalStats();
                int peerCount = netStats.AllPeerStats.Count;

                AddKVRow(netSection.transform, "NAT", global.LocalNATType.ToString(),
                    NATColor(global.LocalNATType));
                AddKVRow(netSection.transform, "Peers", peerCount.ToString());
                float avgRtt = global.AverageRTT;
                AddKVRow(netSection.transform, "Avg RTT",
                    avgRtt >= 0 ? $"{avgRtt:F0}ms" : "---", RTTColor(avgRtt));
                AddKVRow(netSection.transform, "BW Out", $"{global.BandwidthOutKBps:F1} KBps");
                AddKVRow(netSection.transform, "BW In", $"{global.BandwidthInKBps:F1} KBps");

                if (global.OutgoingQueueMaxBytes > 0 || global.IncomingQueueMaxBytes > 0)
                {
                    AddKVRow(netSection.transform, "Queue Out",
                        $"{global.OutgoingQueueBytes / 1024f:F1}/{global.OutgoingQueueMaxBytes / 1024f:F0} KB");
                    AddKVRow(netSection.transform, "Queue In",
                        $"{global.IncomingQueueBytes / 1024f:F1}/{global.IncomingQueueMaxBytes / 1024f:F0} KB");
                }

                if (peerCount > 0)
                {
                    // Header
                    var hdrRow = CreateRow(netSection.transform, 20);
                    AddLabel(hdrRow.transform, "Name", 12, ColDimText, 80);
                    AddLabel(hdrRow.transform, "RTT", 12, ColDimText, 50);
                    AddLabel(hdrRow.transform, "Loss", 12, ColDimText, 45);
                    AddLabel(hdrRow.transform, "Type", 12, ColDimText, 50);

                    foreach (var kvp in netStats.AllPeerStats)
                    {
                        var ps = kvp.Value;
                        string puidStr = kvp.Key?.ToString() ?? "?";
                        string displayName = EOSPlayerRegistry.Instance?.GetPlayerName(puidStr)
                            ?? (puidStr.Length > 8 ? puidStr.Substring(0, 8) + ".." : puidStr);
                        float age = Time.unscaledTime - ps.ConnectedTime;

                        var pRow = CreateRow(netSection.transform, 22);
                        AddLabel(pRow.transform, displayName, 13, ColText, 80);
                        AddLabel(pRow.transform, ps.RTT >= 0 ? $"{ps.RTT:F0}ms" : "---", 13,
                            RTTColor(ps.RTT), 50);
                        AddLabel(pRow.transform, $"{ps.PacketLoss * 100f:F1}%", 13,
                            LossColor(ps.PacketLoss), 45);
                        string connType = ps.ConnectionType == Epic.OnlineServices.P2P.NetworkConnectionType.DirectConnection
                            ? "Direct" : "Relay";
                        Color connColor = ps.ConnectionType == Epic.OnlineServices.P2P.NetworkConnectionType.DirectConnection
                            ? ColGreen : ColYellow;
                        AddLabel(pRow.transform, connType, 13, connColor, 50);
                    }
                }

                AddButton(netSection.transform, "Reset Stats", ColButtonDanger, () => netStats.ResetStats(), 28);
            }

            // Stats & Leaderboards
            var statsManager = EOSStats.Instance;
            var leaderboardsManager = EOSLeaderboards.Instance;
            bool statsReady = statsManager != null && statsManager.IsReady;
            bool leaderboardsReady = leaderboardsManager != null && leaderboardsManager.IsReady;

            var slSection = CreateSection(_statsContainer, "Stats & Leaderboards");

            if (!statsReady && !leaderboardsReady)
            {
                AddLabel(slSection.transform, "Waiting for EOS login...", 14, ColDimText);
            }
            else
            {
                if (statsReady)
                {
                    AddButton(slSection.transform, "Query My Stats", ColButton, () =>
                    {
                        _ = statsManager.QueryMyStatsAsync();
                    }, 30);

                    if (statsManager.CachedStatsCount > 0)
                    {
                        foreach (var kvp in statsManager.CachedStats)
                        {
                            AddKVRow(slSection.transform, kvp.Key, kvp.Value.Value.ToString());
                        }
                    }
                    else
                    {
                        AddLabel(slSection.transform, "No stats cached. Click Query.", 13, ColDimText);
                    }

                    // Test ingest
                    AddLabel(slSection.transform, "Ingest Test Stat:", 14, ColDimText);
                    var ingestRow = CreateRow(slSection.transform, 32);
                    _testStatNameInput = AddInputField(ingestRow.transform, "stat_name", 100);
                    _testStatNameInput.text = _testStatName;
                    _testStatAmountInput = AddInputField(ingestRow.transform, "1", 50);
                    _testStatAmountInput.text = _testStatAmount.ToString();
                    _testStatAmountInput.contentType = InputField.ContentType.IntegerNumber;
                    AddButton(ingestRow.transform, "+", ColButton, () =>
                    {
                        _testStatName = _testStatNameInput?.text ?? _testStatName;
                        if (int.TryParse(_testStatAmountInput?.text, out int amt))
                            _testStatAmount = amt;
                        _ = statsManager.IngestStatAsync(_testStatName, _testStatAmount);
                    }, -1, 30);
                }

                if (leaderboardsReady)
                {
                    AddButton(slSection.transform, "Refresh Definitions", ColButton, () =>
                    {
                        _ = leaderboardsManager.QueryDefinitionsAsync();
                    }, 30);

                    if (leaderboardsManager.DefinitionCount > 0)
                    {
                        AddLabel(slSection.transform, "Select leaderboard:", 13, ColDimText);
                        foreach (var def in leaderboardsManager.Definitions)
                        {
                            string lbId = def.LeaderboardId;
                            bool isSelected = _selectedLeaderboardId == lbId;
                            AddButton(slSection.transform, $"{lbId} ({def.StatName})",
                                isSelected ? ColGreen : ColInputBg, () =>
                                {
                                    _selectedLeaderboardId = lbId;
                                    QuerySelectedLeaderboard();
                                }, 26);
                        }

                        if (_currentLeaderboardEntries.Count > 0)
                        {
                            AddLabel(slSection.transform, $"Top {_currentLeaderboardEntries.Count}", 14, ColHeader);
                            foreach (var entry in _currentLeaderboardEntries)
                            {
                                var eRow = CreateRow(slSection.transform, 22);
                                AddLabel(eRow.transform, $"#{entry.Rank}", 13, ColDimText, 35);
                                AddLabel(eRow.transform, entry.DisplayName ?? entry.ShortUserId, 13, ColText, 100);
                                AddLabel(eRow.transform, entry.Score.ToString(), 13, ColHeader);
                            }
                        }
                    }
                    else
                    {
                        AddLabel(slSection.transform, "No leaderboards configured.", 13, ColDimText);
                    }
                }
            }

            // Achievements
            var achMgr = EOSAchievements.Instance;
            string achTitle = achMgr != null && achMgr.IsReady
                ? $"Achievements ({achMgr.UnlockedCount}/{achMgr.TotalAchievements})"
                : "Achievements";
            var achSection = CreateSection(_statsContainer, achTitle);

            if (achMgr == null || !achMgr.IsReady)
            {
                AddLabel(achSection.transform, "Waiting for EOS login...", 14, ColDimText);
            }
            else
            {
                AddButton(achSection.transform, "Refresh", ColButton, () =>
                {
                    _ = achMgr.RefreshAsync();
                }, 28);

                if (achMgr.TotalAchievements == 0)
                {
                    AddLabel(achSection.transform, "No achievements configured.", 13, ColDimText);
                }
                else
                {
                    foreach (var def in achMgr.Definitions)
                    {
                        var playerAch = achMgr.GetPlayerAchievement(def.Id);
                        bool unlocked = playerAch?.IsUnlocked ?? false;
                        float progress = (float)(playerAch?.Progress ?? 0);

                        var aRow = CreateRow(achSection.transform, 24);
                        string achIcon = unlocked ? "\u2714" : "\u25CB";
                        AddLabel(aRow.transform, achIcon, 14, unlocked ? ColGreen : ColDimText, 22);
                        AddLabel(aRow.transform, def.DisplayName ?? def.Id, 14,
                            unlocked ? ColGreen : ColText);
                        if (unlocked)
                        {
                            var unlockTime = playerAch?.UnlockDateTime;
                            AddLabel(aRow.transform, unlockTime?.ToString("MM/dd/yy") ?? "Unlocked", 12, ColDimText, 70);
                        }
                        else if (progress > 0)
                        {
                            AddLabel(aRow.transform, $"{progress * 100:F0}%", 13, ColYellow, 45);
                        }
                    }
                }
            }

            // Ranked
            var rankedMgr = EOSRankedMatchmaking.Instance;
            string rankedTitle = "Ranked";
            if (rankedMgr != null && rankedMgr.IsDataLoaded)
                rankedTitle = $"Ranked ({rankedMgr.GetCurrentRankDisplayName()})";

            var rankedSection = CreateSection(_statsContainer, rankedTitle);

            if (rankedMgr == null || !rankedMgr.IsDataLoaded)
            {
                AddLabel(rankedSection.transform, "Loading ranked data...", 14, ColDimText);
            }
            else
            {
                var pd = rankedMgr.PlayerData;
                AddKVRow(rankedSection.transform, "Rating", $"{pd.Rating} (Peak: {pd.PeakRating})");

                Color rankColor = rankedMgr.CurrentTier switch
                {
                    RankTier.Grandmaster or RankTier.Master or RankTier.Champion => ColOrange,
                    RankTier.Diamond or RankTier.Platinum => ColHeader,
                    RankTier.Gold => ColYellow,
                    _ => ColText
                };
                AddKVRow(rankedSection.transform, "Rank", rankedMgr.GetCurrentRankDisplayName(), rankColor);

                string record = $"{pd.Wins}W - {pd.Losses}L";
                if (pd.GamesPlayed > 0)
                    record += $" ({pd.WinRate:F0}%)";
                AddKVRow(rankedSection.transform, "Record", record);

                if (pd.WinStreak >= 2)
                    AddKVRow(rankedSection.transform, "Streak", $"{pd.WinStreak} wins", ColGreen);
                else if (pd.LossStreak >= 2)
                    AddKVRow(rankedSection.transform, "Streak", $"{pd.LossStreak} losses", ColRed);

                var lobbyMgrR = EOSLobbyManager.Instance;
                bool isInLobby = lobbyMgrR != null && lobbyMgrR.IsInLobby;
                bool isInQueue = rankedMgr.IsInQueue;

                if (!isInLobby && !isInQueue)
                {
                    var modeRow = CreateRow(rankedSection.transform, 32);
                    AddLabel(modeRow.transform, "Mode:", 14, ColDimText, 45);
                    _rankedModeInput = AddInputField(modeRow.transform, "ranked");
                    _rankedModeInput.text = _rankedGameMode;

                    var btnRow = CreateRow(rankedSection.transform, 34);
                    AddButton(btnRow.transform, "Find", ColButton, () =>
                    {
                        _rankedGameMode = _rankedModeInput?.text ?? _rankedGameMode;
                        FindRankedMatchAsync(rankedMgr);
                    });
                    AddButton(btnRow.transform, "Host", ColButton, () =>
                    {
                        _rankedGameMode = _rankedModeInput?.text ?? _rankedGameMode;
                        HostRankedLobbyAsync(rankedMgr);
                    });
                    AddButton(btnRow.transform, "Find/Host", ColButton, () =>
                    {
                        _rankedGameMode = _rankedModeInput?.text ?? _rankedGameMode;
                        FindOrHostRankedAsync(rankedMgr);
                    });
                }
                else if (isInQueue)
                {
                    AddKVRow(rankedSection.transform, "Queue", $"Searching... ({rankedMgr.QueueTime:F0}s)", ColYellow);
                    AddButton(rankedSection.transform, "Leave Queue", ColButtonDanger, () =>
                    {
                        rankedMgr.LeaveQueue();
                        _rankedStatus = "Left queue";
                    }, 30);
                }
                else
                {
                    AddLabel(rankedSection.transform, "In lobby - leave to find new match", 13, ColDimText);
                }

                if (!string.IsNullOrEmpty(_rankedStatus))
                    AddLabel(rankedSection.transform, _rankedStatus, 13, ColOrange);
            }
        }

        private async void QuerySelectedLeaderboard()
        {
            var lbMgr = EOSLeaderboards.Instance;
            if (string.IsNullOrEmpty(_selectedLeaderboardId) || lbMgr == null) return;
            var (result, entries) = await lbMgr.QueryRanksAsync(_selectedLeaderboardId, 10);
            if (result == Result.Success && entries != null)
                _currentLeaderboardEntries = entries;
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

        private static Color RTTColor(float rtt)
        {
            if (rtt < 0f) return ColDimText;
            if (rtt < 50f) return ColGreen;
            if (rtt < 150f) return ColYellow;
            return ColRed;
        }

        private static Color LossColor(float loss)
        {
            if (loss < 0.01f) return ColGreen;
            if (loss < 0.05f) return ColYellow;
            return ColRed;
        }

        private static Color NATColor(Epic.OnlineServices.P2P.NATType nat)
        {
            return nat switch
            {
                Epic.OnlineServices.P2P.NATType.Open => ColGreen,
                Epic.OnlineServices.P2P.NATType.Moderate => ColYellow,
                Epic.OnlineServices.P2P.NATType.Strict => ColRed,
                _ => ColDimText
            };
        }

        #endregion

        #region Tools Tab Refresh

        private void RefreshToolsTab()
        {
            if (_toolsContainer == null) return;
            ClearChildren(_toolsContainer);

            var mgr = EOSManager.Instance;
            if (mgr == null || !mgr.IsLoggedIn)
            {
                AddLabel(_toolsContainer, "Login required for tools.", 15, ColYellow);
                return;
            }

            // Cloud Storage
            var storageMgr = EOSPlayerDataStorage.Instance;
            int fileCount = storageMgr?.Files?.Count ?? 0;
            var storageSection = CreateSection(_toolsContainer, $"Cloud Storage ({fileCount} files)");

            if (storageMgr == null || !storageMgr.IsReady)
            {
                AddLabel(storageSection.transform, "Waiting for EOS login...", 14, ColDimText);
            }
            else
            {
                long used = storageMgr.GetTotalStorageUsed();
                AddKVRow(storageSection.transform, "Usage", $"{EOSPlayerDataStorage.FormatBytes(used)} / 400 MB");

                AddButton(storageSection.transform, "Refresh File List", ColButton, () =>
                {
                    _ = storageMgr.QueryFileListAsync();
                }, 28);

                if (storageMgr.Files.Count > 0)
                {
                    foreach (var file in storageMgr.Files)
                    {
                        var fRow = CreateRow(storageSection.transform, 24);
                        AddLabel(fRow.transform, file.Filename, 13, ColText);
                        AddLabel(fRow.transform, EOSPlayerDataStorage.FormatBytes((long)file.FileSizeBytes), 12, ColDimText, 60);
                        string delName = file.Filename;
                        AddButton(fRow.transform, "X", ColButtonDanger, () =>
                        {
                            _ = storageMgr.DeleteFileAsync(delName);
                        }, -1, 28);
                    }
                }
                else
                {
                    AddLabel(storageSection.transform, "No cloud files.", 13, ColDimText);
                }

                // Test write
                AddLabel(storageSection.transform, "Test Write:", 14, ColDimText);
                var writeRow = CreateRow(storageSection.transform, 32);
                _testFileNameInput = AddInputField(writeRow.transform, "filename", 90);
                _testFileNameInput.text = _testFileName;
                _testFileContentInput = AddInputField(writeRow.transform, "content");
                _testFileContentInput.text = _testFileContent;
                AddButton(writeRow.transform, "Write", ColButton, () =>
                {
                    _testFileName = _testFileNameInput?.text ?? _testFileName;
                    _testFileContent = _testFileContentInput?.text ?? _testFileContent;
                    _ = storageMgr.WriteFileAsync(_testFileName, _testFileContent);
                }, -1, 55);
            }

            // Anti-Cheat
            var acMgr = EOSAntiCheatManager.Instance;
            string acTitle = "Anti-Cheat";
            if (acMgr != null)
            {
                acTitle = acMgr.Status switch
                {
                    AntiCheatStatus.Protected => "Anti-Cheat (Protected)",
                    AntiCheatStatus.Violated => "Anti-Cheat (VIOLATION)",
                    AntiCheatStatus.Error => "Anti-Cheat (Error)",
                    _ => "Anti-Cheat (N/A)"
                };
            }
            var acSection = CreateSection(_toolsContainer, acTitle);

            if (acMgr == null || !acMgr.IsReady)
            {
                AddLabel(acSection.transform, "Anti-cheat not available.", 14, ColDimText);
            }
            else
            {
                AddKVRow(acSection.transform, "Status", acMgr.Status.ToString());
                AddStatusRow(acSection.transform, "Session", acMgr.IsSessionActive, "Active", "Inactive");
                if (acMgr.IsSessionActive)
                    AddKVRow(acSection.transform, "Peers", acMgr.RegisteredPeerCount.ToString());

                var acAutoRow = CreateRow(acSection.transform, 30);
                AddLabel(acAutoRow.transform, "Auto-Start:", 14, ColDimText, 90);
                var acToggle = AddToggle(acAutoRow.transform, acMgr.AutoStartSession ? "ON" : "OFF", acMgr.AutoStartSession);
                acToggle.onValueChanged.AddListener(on => acMgr.AutoStartSession = on);

                var acBtnRow = CreateRow(acSection.transform, 34);
                var beginBtn = AddButton(acBtnRow.transform, "Begin Session", ColButton, () => acMgr.BeginSession());
                beginBtn.GetComponent<Button>().interactable = !acMgr.IsSessionActive;
                var endBtn = AddButton(acBtnRow.transform, "End Session", ColButtonDanger, () => acMgr.EndSession());
                endBtn.GetComponent<Button>().interactable = acMgr.IsSessionActive;
            }

            // Replays
            var replayStorage = EOSReplayStorage.Instance;
            var replayViewer = EOSReplayViewer.Instance;
            string replayTitle = "Replays";
            if (replayViewer != null && replayViewer.IsViewing)
                replayTitle = "Replays (Viewing)";
            else if (replayStorage != null)
                replayTitle = $"Replays ({replayStorage.LocalReplayCount})";

            var replaySection = CreateSection(_toolsContainer, replayTitle);

            // Playback controls
            if (replayViewer != null && replayViewer.IsViewing)
            {
                AddLabel(replaySection.transform, "NOW PLAYING", 16, ColHeader);
                var header = replayViewer.CurrentReplay;
                if (header.HasValue)
                {
                    AddKVRow(replaySection.transform, "Map", $"{header.Value.GameMode} on {header.Value.MapName}");
                    AddKVRow(replaySection.transform, "Players", $"{header.Value.Participants?.Length ?? 0}");
                }

                // Timeline
                float progress = replayViewer.Duration > 0 ? replayViewer.CurrentTime / replayViewer.Duration : 0f;
                var timeRow = CreateRow(replaySection.transform, 24);
                AddLabel(timeRow.transform, EOSReplayStorage.FormatDuration(replayViewer.CurrentTime), 12, ColDimText, 40);
                AddLabel(timeRow.transform, $"{progress * 100:F0}%", 13, ColText);
                AddLabel(timeRow.transform, EOSReplayStorage.FormatDuration(replayViewer.Duration), 12, ColDimText, 40);

                // Controls
                var ctrlRow = CreateRow(replaySection.transform, 34);
                AddButton(ctrlRow.transform, "<<", ColButton, () => replayViewer.Skip(-10f), -1, 35);
                string playIcon = replayViewer.PlaybackState == PlaybackState.Playing ? "||" : "\u25B6";
                AddButton(ctrlRow.transform, playIcon, ColButton, () => replayViewer.TogglePlayPause(), -1, 35);
                AddButton(ctrlRow.transform, ">>", ColButton, () => replayViewer.Skip(10f), -1, 35);
                AddButton(ctrlRow.transform, $"{replayViewer.PlaybackSpeed:F1}x", ColButton, () => replayViewer.CycleSpeed(), -1, 45);
                AddButton(ctrlRow.transform, "Stop", ColButtonDanger, () => replayViewer.StopViewing(), -1, 50);

                // Target
                var targetRow = CreateRow(replaySection.transform, 28);
                AddLabel(targetRow.transform, "Viewing:", 14, ColDimText, 60);
                AddLabel(targetRow.transform, replayViewer.GetCurrentTargetName(), 14, ColText);
                AddButton(targetRow.transform, "<", ColButton, () => replayViewer.CycleTarget(-1), -1, 28);
                AddButton(targetRow.transform, ">", ColButton, () => replayViewer.CycleTarget(1), -1, 28);
            }

            // Replay list
            if (replayStorage != null && Time.time - _lastReplayRefresh > 5f)
            {
                _cachedReplays = replayStorage.GetLocalReplays();
                _lastReplayRefresh = Time.time;
            }

            AddButton(replaySection.transform, "Refresh List", ColButton, () =>
            {
                replayStorage?.RefreshLocalReplays();
                _cachedReplays = replayStorage?.GetLocalReplays() ?? new List<ReplayHeader>();
                _lastReplayRefresh = Time.time;
            }, 28);

            if (_cachedReplays.Count == 0)
            {
                AddLabel(replaySection.transform, "No saved replays.", 13, ColDimText);
            }
            else
            {
                AddLabel(replaySection.transform, $"Saved ({_cachedReplays.Count})", 14, ColDimText);
                foreach (var replay in _cachedReplays)
                {
                    bool isFav = replayStorage?.IsFavorite(replay.ReplayId) ?? false;
                    var rRow = CreateRow(replaySection.transform, 26);
                    string starIcon = isFav ? "\u2605" : "\u2606";
                    string rId = replay.ReplayId;
                    AddButton(rRow.transform, starIcon, ColInputBg, () =>
                    {
                        replayStorage?.ToggleFavorite(rId);
                        _cachedReplays = replayStorage?.GetLocalReplays() ?? new List<ReplayHeader>();
                    }, -1, 28);
                    AddLabel(rRow.transform, $"{replay.GameMode} on {replay.MapName}", 13, ColText);
                    AddLabel(rRow.transform, EOSReplayStorage.FormatDuration(replay.Duration), 12, ColDimText, 40);

                    var rBtnRow = CreateRow(replaySection.transform, 26);
                    AddLabel(rBtnRow.transform, $"{replay.Participants?.Length ?? 0}p", 12, ColDimText, 25);
                    AddButton(rBtnRow.transform, "Play", ColButton, () =>
                        _ = PlayReplayAsync(rId, replayStorage, replayViewer), -1, 45);
                    AddButton(rBtnRow.transform, "Export", ColButton, () =>
                        _ = ExportReplayAsync(rId, replayStorage), -1, 55);
                    AddButton(rBtnRow.transform, "Delete", ColButtonDanger, () =>
                    {
                        replayStorage?.DeleteReplay(rId);
                        _cachedReplays = replayStorage?.GetLocalReplays() ?? new List<ReplayHeader>();
                    }, -1, 55);
                }
            }

            // Export success
            if (_showExportSuccess && Time.time - _exportSuccessTime < 3f)
            {
                var expRow = CreateRow(replaySection.transform, 28);
                AddLabel(expRow.transform, "Exported!", 14, ColGreen);
                AddButton(expRow.transform, "Open Folder", ColButton, () => replayStorage?.OpenExportFolder(), -1, 90);
            }
            else
            {
                _showExportSuccess = false;
            }

            // Import
            var importRow = CreateRow(replaySection.transform, 32);
            _importPathInput = AddInputField(importRow.transform, "path/to/replay.json");
            _importPathInput.text = _importPath;
            AddButton(importRow.transform, "Import", ColButton, () =>
            {
                _importPath = _importPathInput?.text ?? "";
                if (!string.IsNullOrWhiteSpace(_importPath))
                    _ = ImportReplayAsync(_importPath, replayStorage);
            }, -1, 60);

            // Session Metrics
            var metricsMgr = EOSMetrics.Instance;
            var metricsSection = CreateSection(_toolsContainer, "Session Metrics");

            if (metricsMgr == null || !metricsMgr.IsReady)
            {
                AddLabel(metricsSection.transform, "Waiting for EOS login...", 14, ColDimText);
            }
            else
            {
                bool sessionActive = metricsMgr.IsSessionActive;
                AddStatusRow(metricsSection.transform, "Session", sessionActive, "Active", "Inactive");
                if (sessionActive)
                {
                    AddKVRow(metricsSection.transform, "Duration", metricsMgr.SessionDuration.ToString(@"hh\:mm\:ss"));
                    if (!string.IsNullOrEmpty(metricsMgr.CurrentSessionId))
                    {
                        string shortId = metricsMgr.CurrentSessionId.Length > 12
                            ? metricsMgr.CurrentSessionId.Substring(0, 12) + "..."
                            : metricsMgr.CurrentSessionId;
                        AddKVRow(metricsSection.transform, "ID", shortId);
                    }
                }

                var mBtnRow = CreateRow(metricsSection.transform, 34);
                var mBeginBtn = AddButton(mBtnRow.transform, "Begin", ColButton, () => metricsMgr.BeginSession());
                mBeginBtn.GetComponent<Button>().interactable = !sessionActive;
                var mEndBtn = AddButton(mBtnRow.transform, "End", ColButtonDanger, () => metricsMgr.EndSession());
                mEndBtn.GetComponent<Button>().interactable = sessionActive;
            }

            // LFG
            var lfgMgr = EOSLFGManager.Instance;
            string lfgTitle = "LFG (Looking for Group)";
            if (lfgMgr != null && lfgMgr.HasActivePost)
                lfgTitle = $"LFG (Active: {lfgMgr.ActivePost.CurrentSize}/{lfgMgr.ActivePost.DesiredSize})";

            var lfgSection = CreateSection(_toolsContainer, lfgTitle);

            if (lfgMgr == null)
            {
                AddLabel(lfgSection.transform, "LFG Manager not available.", 14, ColDimText);
            }
            else if (lfgMgr.HasActivePost)
            {
                var post = lfgMgr.ActivePost;
                AddLabel(lfgSection.transform, "YOUR ACTIVE POST", 15, ColHeader);
                AddKVRow(lfgSection.transform, "Title", post.Title);
                AddKVRow(lfgSection.transform, "Size", $"{post.CurrentSize}/{post.DesiredSize}");
                if (!string.IsNullOrEmpty(post.GameMode))
                    AddKVRow(lfgSection.transform, "Mode", post.GameMode);

                var timeLeft = post.TimeRemaining;
                Color timeColor = timeLeft.TotalMinutes < 5 ? ColOrange : ColText;
                AddKVRow(lfgSection.transform, "Expires", $"{timeLeft.Minutes}m {timeLeft.Seconds}s", timeColor);

                if (lfgMgr.PendingRequests.Count > 0)
                {
                    AddLabel(lfgSection.transform, $"Pending Requests: {lfgMgr.PendingRequests.Count}", 14, ColYellow);
                    foreach (var request in lfgMgr.PendingRequests)
                    {
                        var reqRow = CreateRow(lfgSection.transform, 28);
                        AddLabel(reqRow.transform, request.RequesterName, 13, ColDimText);
                        var req = request;
                        AddButton(reqRow.transform, "Accept", ColButton, () => { _ = lfgMgr.AcceptJoinRequestAsync(req); }, -1, 60);
                        AddButton(reqRow.transform, "Reject", ColButtonDanger, () => { _ = lfgMgr.RejectJoinRequestAsync(req); }, -1, 60);
                    }
                }

                AddButton(lfgSection.transform, "Close Post", ColButtonDanger, () => CloseLFGPostAsync(lfgMgr), 32);
            }
            else
            {
                // Create post form
                AddLabel(lfgSection.transform, "CREATE POST", 15, ColHeader);

                var titleRow = CreateRow(lfgSection.transform, 32);
                AddLabel(titleRow.transform, "Title:", 14, ColDimText, 45);
                _lfgTitleInput = AddInputField(titleRow.transform, "Looking for players");
                _lfgTitleInput.text = _lfgTitle;

                var modeRow = CreateRow(lfgSection.transform, 32);
                AddLabel(modeRow.transform, "Mode:", 14, ColDimText, 45);
                _lfgModeInput = AddInputField(modeRow.transform, "game mode");
                _lfgModeInput.text = _lfgGameMode;

                // Size spinner
                var sizeRow = CreateRow(lfgSection.transform, 32);
                AddLabel(sizeRow.transform, "Size:", 14, ColDimText, 45);
                AddButton(sizeRow.transform, "-", ColButton, () =>
                {
                    _lfgDesiredSize = Mathf.Max(2, _lfgDesiredSize - 1);
                    if (_lfgSizeLabel != null) _lfgSizeLabel.text = _lfgDesiredSize.ToString();
                }, -1, 30);
                _lfgSizeLabel = AddLabel(sizeRow.transform, _lfgDesiredSize.ToString(), 16, ColHeader, TextAnchor.MiddleCenter, 30);
                AddButton(sizeRow.transform, "+", ColButton, () =>
                {
                    _lfgDesiredSize = Mathf.Min(64, _lfgDesiredSize + 1);
                    if (_lfgSizeLabel != null) _lfgSizeLabel.text = _lfgDesiredSize.ToString();
                }, -1, 30);

                AddButton(lfgSection.transform, "Create LFG Post", ColButton, () =>
                {
                    _lfgTitle = _lfgTitleInput?.text ?? _lfgTitle;
                    _lfgGameMode = _lfgModeInput?.text ?? _lfgGameMode;
                    CreateLFGPostAsync(lfgMgr);
                }, 34);
            }

            // Browse (only if LFG manager is available)
            if (lfgMgr != null)
            {
                AddLabel(lfgSection.transform, "BROWSE POSTS", 15, ColHeader);
                var searchRow = CreateRow(lfgSection.transform, 30);
                AddButton(searchRow.transform, "Search", ColButton, () => SearchLFGPostsAsync(lfgMgr), 28, 70);
                AddButton(searchRow.transform, "Refresh", ColButton, () => { _ = lfgMgr.RefreshSearchAsync(); }, 28, 70);
                AddLabel(searchRow.transform, $"{lfgMgr.SearchResults.Count} posts", 13, ColDimText);

                if (lfgMgr.SearchResults.Count > 0)
                {
                    foreach (var post in lfgMgr.SearchResults)
                    {
                        if (post.IsExpired) continue;
                        var pRow = CreateRow(lfgSection.transform, 26);
                        AddLabel(pRow.transform, post.Title, 13, ColText);
                        AddLabel(pRow.transform, $"{post.CurrentSize}/{post.DesiredSize}", 13, ColHeader, 35);
                        if (!string.IsNullOrEmpty(post.GameMode))
                            AddLabel(pRow.transform, post.GameMode, 12, ColDimText, 50);

                        bool alreadySent = lfgMgr.SentRequests.Contains(post.PostId);
                        string pId = post.PostId;
                        var joinBtn = AddButton(pRow.transform, alreadySent ? "Sent" : "Join", ColButton, () =>
                        {
                            _ = lfgMgr.SendJoinRequestAsync(pId);
                        }, -1, 50);
                        joinBtn.GetComponent<Button>().interactable = post.IsJoinable && !alreadySent;
                    }
                }

                if (!string.IsNullOrEmpty(_lfgStatus))
                    AddLabel(lfgSection.transform, _lfgStatus, 13, ColOrange);
            }
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

        private async void CreateLFGPostAsync(EOSLFGManager lfgMgr)
        {
            _lfgStatus = "Creating post...";
            var options = new LFGPostOptions()
                .WithTitle(_lfgTitle)
                .WithGameMode(_lfgGameMode)
                .WithDesiredSize(_lfgDesiredSize);
            var (result, post) = await lfgMgr.CreatePostAsync(options);
            _lfgStatus = result == Result.Success ? "Post created!" : $"Failed: {result}";
        }

        private async void CloseLFGPostAsync(EOSLFGManager lfgMgr)
        {
            _lfgStatus = "Closing post...";
            var result = await lfgMgr.ClosePostAsync();
            _lfgStatus = result == Result.Success ? "Post closed" : $"Failed: {result}";
        }

        private async void SearchLFGPostsAsync(EOSLFGManager lfgMgr)
        {
            _lfgStatus = "Searching...";
            var options = new LFGSearchOptions();
            if (!string.IsNullOrEmpty(_lfgGameMode))
                options.WithGameMode(_lfgGameMode);
            var (result, posts) = await lfgMgr.SearchPostsAsync(options);
            _lfgStatus = result == Result.Success ? $"Found {posts.Count} posts" : $"Search failed: {result}";
        }

        #endregion

        #region Popups

        private void ShowProfilePopup(string puid)
        {
            _profilePuid = puid;
            _profileNote = EOSPlayerRegistry.Instance?.GetNote(puid) ?? "";
            _profileEditingNote = false;
            _profileStatus = "";
            BuildPopupOverlay();
            BuildProfilePopupPanel();
        }

        private void ShowReportPopup(string puid)
        {
            _reportTargetPuid = puid;
            _reportCategoryIndex = 0;
            _reportStatus = "";
            BuildPopupOverlay();
            BuildReportPopupPanel();
        }

        private void ClosePopup()
        {
            _profilePuid = "";
            _reportTargetPuid = "";
            if (_popupOverlay != null) Destroy(_popupOverlay);
            _popupOverlay = null;
            _popupPanel = null;
        }

        private void BuildPopupOverlay()
        {
            if (_popupOverlay != null) Destroy(_popupOverlay);

            _popupOverlay = new GameObject("PopupOverlay");
            _popupOverlay.transform.SetParent(_canvas.transform, false);
            var overlayImg = _popupOverlay.AddComponent<Image>();
            overlayImg.color = new Color(0, 0, 0, 0.6f);
            overlayImg.raycastTarget = true;
            var overlayRT = _popupOverlay.GetComponent<RectTransform>();
            StretchFill(overlayRT);

            var overlayBtn = _popupOverlay.AddComponent<Button>();
            overlayBtn.targetGraphic = overlayImg;
            overlayBtn.onClick.AddListener(ClosePopup);
        }

        private void BuildProfilePopupPanel()
        {
            if (_popupPanel != null) Destroy(_popupPanel);
            if (_popupOverlay == null) return;

            _popupPanel = CreatePanelGO(_popupOverlay.transform, "ProfilePopup", ColPanelBg);
            var rt = _popupPanel.GetComponent<RectTransform>();
            rt.anchorMin = new Vector2(0.05f, 0.15f);
            rt.anchorMax = new Vector2(0.95f, 0.85f);
            rt.offsetMin = Vector2.zero;
            rt.offsetMax = Vector2.zero;
            _popupPanel.GetComponent<Image>().raycastTarget = true;

            var vlg = _popupPanel.AddComponent<VerticalLayoutGroup>();
            vlg.padding = new RectOffset(16, 16, 16, 16);
            vlg.spacing = 6;
            vlg.childForceExpandWidth = true;
            vlg.childForceExpandHeight = false;
            vlg.childControlWidth = true;
            vlg.childControlHeight = true;

            var registry = EOSPlayerRegistry.Instance;
            string displayName = registry?.GetPlayerName(_profilePuid) ?? _profilePuid;
            string platformId = registry?.GetPlatform(_profilePuid);
            string platformName = !string.IsNullOrEmpty(platformId) ? EOSPlayerRegistry.GetPlatformName(platformId) : "Unknown";
            bool isFriend = registry?.IsFriend(_profilePuid) ?? false;
            bool isBlocked = registry?.IsBlocked(_profilePuid) ?? false;
            DateTime? lastSeen = registry?.GetLastSeen(_profilePuid);

            AddLabel(_popupPanel.transform, "PLAYER PROFILE", 20, ColHeader);
            AddLabel(_popupPanel.transform, displayName, 18, ColText);
            AddKVRow(_popupPanel.transform, "Platform", platformName);
            AddKVRow(_popupPanel.transform, "PUID", _profilePuid.Length > 20 ? _profilePuid.Substring(0, 20) + "..." : _profilePuid);

            if (lastSeen.HasValue)
                AddKVRow(_popupPanel.transform, "Last Seen", GetTimeAgo(lastSeen.Value));

            // Badges
            var badgeRow = CreateRow(_popupPanel.transform, 26);
            if (isFriend) AddBadge(badgeRow.transform, "Friend", true);
            if (isBlocked) AddBadge(badgeRow.transform, "Blocked", false);
            var lobbyMgr = EOSLobbyManager.Instance;
            bool isLobbyOwner = lobbyMgr != null && lobbyMgr.IsInLobby && lobbyMgr.CurrentLobby.OwnerPuid == _profilePuid;
            if (isLobbyOwner) AddLabel(badgeRow.transform, "[Host]", 14, ColYellow, 50);

            // Notes
            AddLabel(_popupPanel.transform, "Personal Note:", 14, ColDimText);
            if (_profileEditingNote)
            {
                var noteInput = AddInputField(_popupPanel.transform, "Note...");
                noteInput.text = _profileNote;
                var noteActRow = CreateRow(_popupPanel.transform, 32);
                AddButton(noteActRow.transform, "Save", ColButton, () =>
                {
                    _profileNote = noteInput.text;
                    registry?.SetNote(_profilePuid, _profileNote);
                    _profileEditingNote = false;
                    _profileStatus = "Note saved";
                    BuildProfilePopupPanel(); // Rebuild
                });
                AddButton(noteActRow.transform, "Cancel", ColButtonDanger, () =>
                {
                    _profileNote = registry?.GetNote(_profilePuid) ?? "";
                    _profileEditingNote = false;
                    BuildProfilePopupPanel();
                });
            }
            else
            {
                var noteRow = CreateRow(_popupPanel.transform, 28);
                string noteDisplay = string.IsNullOrEmpty(_profileNote) ? "(no note)" : _profileNote;
                AddLabel(noteRow.transform, noteDisplay, 14, ColText);
                AddButton(noteRow.transform, "Edit", ColButton, () =>
                {
                    _profileEditingNote = true;
                    BuildProfilePopupPanel();
                }, -1, 55);
            }

            // Actions
            var actRow1 = CreateRow(_popupPanel.transform, 36);
            string pPuid = _profilePuid;
            AddButton(actRow1.transform, isFriend ? "Unfriend" : "Add Friend", ColButton, () =>
            {
                registry?.ToggleFriend(pPuid);
                _profileStatus = isFriend ? "Removed" : "Added";
                BuildProfilePopupPanel();
            });
            AddButton(actRow1.transform, isBlocked ? "Unblock" : "Block", ColButtonDanger, () =>
            {
                if (isBlocked) registry?.UnblockPlayer(pPuid);
                else registry?.BlockPlayer(pPuid);
                _profileStatus = isBlocked ? "Unblocked" : "Blocked";
                BuildProfilePopupPanel();
            });

            var actRow2 = CreateRow(_popupPanel.transform, 36);
            var reportsManager = EOSReports.Instance;
            if (reportsManager != null && reportsManager.IsReady)
            {
                AddButton(actRow2.transform, "Report", ColButtonDanger, () => ShowReportPopup(pPuid));
            }
            var invMgr = EOSCustomInvites.Instance;
            if (invMgr != null)
            {
                AddButton(actRow2.transform, "Invite", ColButton, () =>
                {
                    _ = invMgr.SendInviteAsync(pPuid);
                    _profileStatus = "Invite sent!";
                    BuildProfilePopupPanel();
                });
            }
            if (lobbyMgr != null && lobbyMgr.IsInLobby && lobbyMgr.IsOwner && !isLobbyOwner)
            {
                AddButton(actRow2.transform, "Kick", ColButtonDanger, () =>
                {
                    _ = lobbyMgr.KickMemberAsync(pPuid);
                    _profileStatus = "Kicked";
                    BuildProfilePopupPanel();
                });
            }

            if (!string.IsNullOrEmpty(_profileStatus))
                AddLabel(_popupPanel.transform, _profileStatus, 14, ColHeader);

            AddButton(_popupPanel.transform, "Close", ColButtonDanger, ClosePopup, 36);
        }

        private void BuildReportPopupPanel()
        {
            if (_popupPanel != null) Destroy(_popupPanel);
            if (_popupOverlay == null) return;

            _popupPanel = CreatePanelGO(_popupOverlay.transform, "ReportPopup", ColPanelBg);
            var rt = _popupPanel.GetComponent<RectTransform>();
            rt.anchorMin = new Vector2(0.1f, 0.25f);
            rt.anchorMax = new Vector2(0.9f, 0.75f);
            rt.offsetMin = Vector2.zero;
            rt.offsetMax = Vector2.zero;
            _popupPanel.GetComponent<Image>().raycastTarget = true;

            var vlg = _popupPanel.AddComponent<VerticalLayoutGroup>();
            vlg.padding = new RectOffset(16, 16, 16, 16);
            vlg.spacing = 6;
            vlg.childForceExpandWidth = true;
            vlg.childForceExpandHeight = false;
            vlg.childControlWidth = true;
            vlg.childControlHeight = true;

            string targetDisplay = _reportTargetPuid.Length > 16
                ? _reportTargetPuid.Substring(0, 8) + "..."
                : _reportTargetPuid;
            string displayName = EOSPlayerRegistry.Instance?.GetPlayerName(_reportTargetPuid);
            if (!string.IsNullOrEmpty(displayName))
                targetDisplay = displayName;

            AddLabel(_popupPanel.transform, "REPORT PLAYER", 20, ColRed);
            AddKVRow(_popupPanel.transform, "Target", targetDisplay);

            // Category buttons
            AddLabel(_popupPanel.transform, "Category:", 14, ColDimText);
            var categories = EOSReports.GetAllCategories();

            var catRow1 = CreateRow(_popupPanel.transform, 30);
            for (int i = 0; i < Mathf.Min(4, categories.Length); i++)
            {
                int idx = i;
                string catName = EOSReports.GetCategoryDisplayName(categories[i]);
                bool isSel = _reportCategoryIndex == i;
                AddButton(catRow1.transform, catName, isSel ? ColGreen : ColInputBg, () =>
                {
                    _reportCategoryIndex = idx;
                    BuildReportPopupPanel();
                }, 28);
            }

            if (categories.Length > 4)
            {
                var catRow2 = CreateRow(_popupPanel.transform, 30);
                for (int i = 4; i < categories.Length; i++)
                {
                    int idx = i;
                    string catName = EOSReports.GetCategoryDisplayName(categories[i]);
                    bool isSel = _reportCategoryIndex == i;
                    AddButton(catRow2.transform, catName, isSel ? ColGreen : ColInputBg, () =>
                    {
                        _reportCategoryIndex = idx;
                        BuildReportPopupPanel();
                    }, 28);
                }
            }

            if (!string.IsNullOrEmpty(_reportStatus))
                AddLabel(_popupPanel.transform, _reportStatus, 14, ColOrange);

            var btnRow = CreateRow(_popupPanel.transform, 38);
            AddButton(btnRow.transform, "Send Report", ColButtonDanger, () =>
            {
                SendReport(categories[_reportCategoryIndex]);
            });
            AddButton(btnRow.transform, "Cancel", ColButton, ClosePopup);
        }

        private async void SendReport(PlayerReportsCategory category)
        {
            var reportsManager = EOSReports.Instance;
            if (string.IsNullOrEmpty(_reportTargetPuid) || reportsManager == null) return;
            _reportStatus = "Sending...";
            BuildReportPopupPanel();
            var result = await reportsManager.ReportPlayerAsync(_reportTargetPuid, category);
            if (result == Result.Success)
            {
                _reportStatus = "Report sent!";
                BuildReportPopupPanel();
                await Task.Delay(1000);
                ClosePopup();
            }
            else
            {
                _reportStatus = $"Failed: {result}";
                BuildReportPopupPanel();
            }
        }

        #endregion

        #region Social Helpers

        private async void SendInviteToPuid(string puid, string displayName)
        {
            var invitesManager = EOSCustomInvites.Instance;
            if (invitesManager == null || !invitesManager.IsReady) return;
            _inviteStatus = $"Sending to {displayName}...";
            var result = await invitesManager.SendInviteAsync(puid);
            _inviteStatus = result == Result.Success ? $"Sent to {displayName}!" : $"Failed: {result}";
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
                _inviteStatus = "Accepted (no lobby code)";
            }
        }

        private async void JoinFriendLobbyAsync(string lobbyCode)
        {
            var lobbyMgr = EOSLobbyManager.Instance;
            if (lobbyMgr == null) return;
            if (lobbyMgr.IsInLobby)
                await lobbyMgr.LeaveLobbyAsync();
            SetLobbyStatus($"Joining {lobbyCode}...");
            var (result, lobby) = await lobbyMgr.JoinLobbyByCodeAsync(lobbyCode);
            SetLobbyStatus(result == Result.Success ? $"Joined! Code: {lobby.JoinCode}" : $"Failed: {result}");
        }

        private static string GetTimeAgo(DateTime dt)
        {
            var span = DateTime.Now - dt;
            if (span.TotalMinutes < 1) return "just now";
            if (span.TotalMinutes < 60) return $"{(int)span.TotalMinutes}m ago";
            if (span.TotalHours < 24) return $"{(int)span.TotalHours}h ago";
            if (span.TotalDays < 7) return $"{(int)span.TotalDays}d ago";
            return dt.ToString("MM/dd");
        }

        #endregion

        #region Button Callbacks

        private LobbyOptions BuildLobbyOptionsFromUI()
        {
            int maxPlayers = 4;
            if (_maxPlayersInput != null) int.TryParse(_maxPlayersInput.text, out maxPlayers);
            maxPlayers = Mathf.Clamp(maxPlayers, 2, 64);

            return new LobbyOptions()
                .WithName(_lobbyNameInput?.text)
                .WithMaxPlayers((uint)maxPlayers)
                .WithVoice(_voiceToggle != null && _voiceToggle.isOn)
                .WithHostMigration(_hostMigrationToggle != null && _hostMigrationToggle.isOn);
        }

        private void OnCreateLobby()
        {
            var lobbyMgr = EOSLobbyManager.Instance;
            if (lobbyMgr == null) return;
            SetLobbyStatus("Creating lobby...");
            var options = BuildLobbyOptionsFromUI();
            if (_publicToggle != null && !_publicToggle.isOn) options.AsPrivate();
            CreateLobbyAsync(lobbyMgr, options);
        }

        private async void CreateLobbyAsync(EOSLobbyManager lobbyMgr, LobbyOptions options)
        {
            var (result, lobby) = await lobbyMgr.CreateLobbyAsync(options);
            SetLobbyStatus(result == Result.Success ? $"Created! Code: {lobby.JoinCode}" : $"Failed: {result}");
        }

        private void OnJoinByCode()
        {
            var lobbyMgr = EOSLobbyManager.Instance;
            string code = _joinCodeInput?.text;
            if (lobbyMgr == null || string.IsNullOrEmpty(code)) return;
            SetLobbyStatus($"Joining {code}...");
            JoinByCodeAsync(lobbyMgr, code);
        }

        private async void JoinByCodeAsync(EOSLobbyManager lobbyMgr, string code)
        {
            var (result, lobby) = await lobbyMgr.JoinLobbyByCodeAsync(code);
            SetLobbyStatus(result == Result.Success ? $"Joined! Code: {lobby.JoinCode}" : $"Failed: {result}");
        }

        private void OnQuickMatch()
        {
            var lobbyMgr = EOSLobbyManager.Instance;
            if (lobbyMgr == null) return;
            SetLobbyStatus("Quick matching...");
            var options = BuildLobbyOptionsFromUI();
            QuickMatchAsync(lobbyMgr, options);
        }

        private async void QuickMatchAsync(EOSLobbyManager lobbyMgr, LobbyOptions options)
        {
            var (result, lobby, didHost) = await lobbyMgr.QuickMatchOrHostAsync(options);
            SetLobbyStatus(result == Result.Success
                ? (didHost ? $"Hosting! Code: {lobby.JoinCode}" : $"Joined! Code: {lobby.JoinCode}")
                : $"Quick match failed: {result}");
        }

        private bool _searching;
        private void OnSearchLobbies()
        {
            var lobbyMgr = EOSLobbyManager.Instance;
            if (lobbyMgr == null || _searching) return;
            SearchLobbiesAsync(lobbyMgr);
        }

        private async void SearchLobbiesAsync(EOSLobbyManager lobbyMgr)
        {
            _searching = true;
            SetLobbyStatus("Searching...");

            var (result, lobbies) = await lobbyMgr.SearchLobbiesAsync(new LobbySearchOptions
            {
                MaxResults = 20,
                OnlyAvailable = false
            });

            _searching = false;

            if (_lobbySearchContainer != null)
            {
                // Remove old results (keep header + separator + button = first 3 children)
                var children = new List<Transform>();
                for (int i = 0; i < _lobbySearchContainer.childCount; i++)
                    children.Add(_lobbySearchContainer.GetChild(i));
                for (int i = 3; i < children.Count; i++)
                    Destroy(children[i].gameObject);

                if (lobbies != null && lobbies.Count > 0)
                {
                    AddLabel(_lobbySearchContainer, $"Found {lobbies.Count} lobbies:", 14, ColGreen);
                    foreach (var l in lobbies)
                    {
                        var row = CreateRow(_lobbySearchContainer);
                        string name = l.LobbyName ?? l.JoinCode ?? "???";
                        AddLabel(row.transform, $"[{l.JoinCode}] {name} ({l.MemberCount}/{l.MaxMembers})", 14, ColText);

                        if (lobbyMgr != null && !lobbyMgr.IsInLobby)
                        {
                            string lobbyId = l.LobbyId;
                            AddButton(row.transform, "Join", ColButton, () => JoinLobbyByIdAsync(lobbyMgr, lobbyId), -1, 60);
                        }
                    }
                }
                else
                {
                    AddLabel(_lobbySearchContainer, "No lobbies found.", 14, ColDimText);
                }
            }

            SetLobbyStatus(result == Result.Success
                ? $"Found {lobbies?.Count ?? 0} lobbies."
                : $"Search failed: {result}");
        }

        private async void JoinLobbyByIdAsync(EOSLobbyManager lobbyMgr, string lobbyId)
        {
            SetLobbyStatus("Joining...");
            var (result, lobby) = await lobbyMgr.JoinLobbyByIdAsync(lobbyId);
            SetLobbyStatus(result == Result.Success ? $"Joined! Code: {lobby.JoinCode}" : $"Failed: {result}");
        }

        private void OnSendChat()
        {
            if (_chatInputField == null) return;
            string msg = _chatInputField.text;
            if (string.IsNullOrWhiteSpace(msg)) return;

            var chatMgr = EOSLobbyChatManager.Instance;
            if (chatMgr != null)
            {
                chatMgr.SendChatMessage(msg);
                _chatInputField.text = "";
                _chatInputField.ActivateInputField();
            }
        }

        private void SetLobbyStatus(string text)
        {
            if (_lobbyStatusText != null)
                _lobbyStatusText.text = text;
        }

        private void InitializeFromResources()
        {
            var config = Resources.Load<EOSConfig>("SampleEOSConfig");
            if (config == null) config = Resources.Load<EOSConfig>("NewEOSConfig");
            if (config == null)
            {
                Debug.LogError("[EOSNativeCanvasUI] No EOSConfig found in Resources.");
                return;
            }
            var mgr = EOSManager.Instance;
            if (mgr != null)
            {
                var result = mgr.Initialize(config);
                Debug.Log($"[EOSNativeCanvasUI] Initialize result: {result}");
            }
        }

        #endregion

        #region UI Builder Helpers

        /// <summary>Anchor a RectTransform to fill its parent with optional inset.</summary>
        private static void StretchFill(RectTransform rt, float inset = 0)
        {
            rt.anchorMin = Vector2.zero;
            rt.anchorMax = Vector2.one;
            rt.offsetMin = new Vector2(inset, inset);
            rt.offsetMax = new Vector2(-inset, -inset);
        }

        private GameObject CreatePanelGO(Transform parent, string name, Color bgColor)
        {
            var go = new GameObject(name);
            go.transform.SetParent(parent, false);
            var img = go.AddComponent<Image>();
            img.color = bgColor;
            img.raycastTarget = bgColor.a > 0.05f;
            return go;
        }

        private GameObject CreateSection(Transform parent, string title)
        {
            var section = CreatePanelGO(parent, "Sec_" + title, ColSectionBg);

            var vlg = section.AddComponent<VerticalLayoutGroup>();
            vlg.padding = new RectOffset(12, 12, 10, 10);
            vlg.spacing = 5;
            vlg.childForceExpandWidth = true;
            vlg.childForceExpandHeight = false;
            vlg.childControlWidth = true;
            vlg.childControlHeight = true;

            // Section header
            var headerGo = new GameObject("Header");
            headerGo.transform.SetParent(section.transform, false);
            var headerTxt = headerGo.AddComponent<Text>();
            headerTxt.text = title;
            headerTxt.font = _defaultFont;
            headerTxt.fontSize = 18;
            headerTxt.fontStyle = FontStyle.Bold;
            headerTxt.color = ColHeader;
            headerTxt.alignment = TextAnchor.MiddleLeft;
            headerTxt.raycastTarget = false;
            var headerLE = headerGo.AddComponent<LayoutElement>();
            headerLE.preferredHeight = 26;
            headerLE.flexibleWidth = 1;

            // Separator line
            var sep = CreatePanelGO(section.transform, "Sep", new Color(ColHeader.r, ColHeader.g, ColHeader.b, 0.3f));
            var sepLE = sep.AddComponent<LayoutElement>();
            sepLE.preferredHeight = 1;
            sepLE.flexibleWidth = 1;

            return section;
        }

        private GameObject CreateRow(Transform parent, float height = 28)
        {
            var row = new GameObject("Row");
            row.transform.SetParent(parent, false);
            var hlg = row.AddComponent<HorizontalLayoutGroup>();
            hlg.spacing = 6;
            hlg.childForceExpandWidth = false;
            hlg.childForceExpandHeight = true;
            hlg.childControlWidth = true;
            hlg.childControlHeight = true;
            hlg.childAlignment = TextAnchor.MiddleLeft;

            var le = row.AddComponent<LayoutElement>();
            le.preferredHeight = height;
            le.flexibleWidth = 1;

            return row;
        }

        private Text AddLabel(Transform parent, string text, int fontSize, Color color,
            float preferredWidth = -1)
        {
            return AddLabel(parent, text, fontSize, color, TextAnchor.MiddleLeft, preferredWidth);
        }

        private Text AddLabel(Transform parent, string text, int fontSize, Color color,
            TextAnchor alignment, float preferredWidth = -1)
        {
            var go = new GameObject("Lbl");
            go.transform.SetParent(parent, false);
            var txt = go.AddComponent<Text>();
            txt.text = text;
            txt.font = _defaultFont;
            txt.fontSize = fontSize;
            txt.color = color;
            txt.alignment = alignment;
            txt.horizontalOverflow = HorizontalWrapMode.Wrap;
            txt.verticalOverflow = VerticalWrapMode.Overflow;
            txt.supportRichText = true;
            txt.raycastTarget = false;

            var le = go.AddComponent<LayoutElement>();
            if (preferredWidth > 0)
            {
                le.preferredWidth = preferredWidth;
                le.minWidth = preferredWidth;
            }
            else
            {
                le.flexibleWidth = 1;
            }

            return txt;
        }

        private GameObject AddButton(Transform parent, string label, Color bgColor,
            Action onClick, int height = -1, float preferredWidth = -1)
        {
            var go = CreatePanelGO(parent, "Btn_" + label, bgColor);

            var btn = go.AddComponent<Button>();
            btn.targetGraphic = go.GetComponent<Image>();
            btn.navigation = new Navigation { mode = Navigation.Mode.None };
            var colors = btn.colors;
            colors.normalColor = bgColor;
            colors.highlightedColor = Brighten(bgColor, 0.12f);
            colors.pressedColor = Brighten(bgColor, 0.2f);
            colors.disabledColor = new Color(bgColor.r * 0.4f, bgColor.g * 0.4f, bgColor.b * 0.4f, 0.5f);
            colors.fadeDuration = 0f;
            btn.colors = colors;
            btn.onClick.AddListener(() => onClick?.Invoke());

            var txtGo = new GameObject("Lbl");
            txtGo.transform.SetParent(go.transform, false);
            var txt = txtGo.AddComponent<Text>();
            txt.text = label;
            txt.font = _defaultFont;
            txt.fontSize = 15;
            txt.fontStyle = FontStyle.Bold;
            txt.color = Color.white;
            txt.alignment = TextAnchor.MiddleCenter;
            txt.raycastTarget = false;
            StretchFill(txtGo.GetComponent<RectTransform>(), 4);

            var le = go.AddComponent<LayoutElement>();
            le.preferredHeight = height > 0 ? height : 32;
            if (preferredWidth > 0)
            {
                le.preferredWidth = preferredWidth;
                le.minWidth = preferredWidth;
            }
            else
            {
                le.flexibleWidth = 1;
            }

            return go;
        }

        private InputField AddInputField(Transform parent, string placeholder, float preferredWidth = -1)
        {
            var go = CreatePanelGO(parent, "Input", ColInputBg);
            var le = go.AddComponent<LayoutElement>();
            le.preferredHeight = 32;
            if (preferredWidth > 0)
            {
                le.preferredWidth = preferredWidth;
                le.minWidth = preferredWidth;
            }
            else
            {
                le.flexibleWidth = 1;
            }

            // Placeholder
            var phGo = new GameObject("Placeholder");
            phGo.transform.SetParent(go.transform, false);
            var phText = phGo.AddComponent<Text>();
            phText.text = placeholder;
            phText.font = _defaultFont;
            phText.fontSize = 14;
            phText.fontStyle = FontStyle.Italic;
            phText.color = new Color(0.38f, 0.38f, 0.43f, 1f);
            phText.alignment = TextAnchor.MiddleLeft;
            phText.raycastTarget = false;
            var phRT = phGo.GetComponent<RectTransform>();
            phRT.anchorMin = Vector2.zero;
            phRT.anchorMax = Vector2.one;
            phRT.offsetMin = new Vector2(8, 2);
            phRT.offsetMax = new Vector2(-8, -2);

            // Text
            var txtGo = new GameObject("Text");
            txtGo.transform.SetParent(go.transform, false);
            var inputText = txtGo.AddComponent<Text>();
            inputText.font = _defaultFont;
            inputText.fontSize = 15;
            inputText.color = ColText;
            inputText.alignment = TextAnchor.MiddleLeft;
            inputText.supportRichText = false;
            inputText.raycastTarget = false;
            inputText.verticalOverflow = VerticalWrapMode.Overflow;
            var inputTextRT = txtGo.GetComponent<RectTransform>();
            inputTextRT.anchorMin = Vector2.zero;
            inputTextRT.anchorMax = Vector2.one;
            inputTextRT.offsetMin = new Vector2(8, 2);
            inputTextRT.offsetMax = new Vector2(-8, -2);

            var inputField = go.AddComponent<InputField>();
            inputField.textComponent = inputText;
            inputField.placeholder = phText;
            inputField.text = "";
            inputField.targetGraphic = go.GetComponent<Image>();
            inputField.selectionColor = new Color(0.3f, 0.5f, 0.7f, 0.5f);
            inputField.caretColor = ColText;

            return inputField;
        }

        private Toggle AddToggle(Transform parent, string label, bool defaultValue)
        {
            var go = new GameObject("Tog_" + label);
            go.transform.SetParent(parent, false);
            var le = go.AddComponent<LayoutElement>();
            le.preferredHeight = 30;
            le.preferredWidth = 95;
            le.minWidth = 85;

            // Checkbox background
            var bgGo = CreatePanelGO(go.transform, "Bg", defaultValue ? ColToggleOn : ColToggleOff);
            var bgRT = bgGo.GetComponent<RectTransform>();
            bgRT.anchorMin = new Vector2(0, 0.5f);
            bgRT.anchorMax = new Vector2(0, 0.5f);
            bgRT.pivot = new Vector2(0, 0.5f);
            bgRT.anchoredPosition = new Vector2(4, 0);
            bgRT.sizeDelta = new Vector2(22, 22);

            // Checkmark
            var checkGo = CreatePanelGO(bgGo.transform, "Check", Color.white);
            var checkRT = checkGo.GetComponent<RectTransform>();
            checkRT.anchorMin = new Vector2(0.15f, 0.15f);
            checkRT.anchorMax = new Vector2(0.85f, 0.85f);
            checkRT.offsetMin = Vector2.zero;
            checkRT.offsetMax = Vector2.zero;

            // Label
            var lblGo = new GameObject("Lbl");
            lblGo.transform.SetParent(go.transform, false);
            var lblTxt = lblGo.AddComponent<Text>();
            lblTxt.text = label;
            lblTxt.font = _defaultFont;
            lblTxt.fontSize = 14;
            lblTxt.color = ColText;
            lblTxt.alignment = TextAnchor.MiddleLeft;
            lblTxt.raycastTarget = false;
            var lblRT = lblGo.GetComponent<RectTransform>();
            lblRT.anchorMin = Vector2.zero;
            lblRT.anchorMax = Vector2.one;
            lblRT.offsetMin = new Vector2(30, 0);
            lblRT.offsetMax = Vector2.zero;

            var toggle = go.AddComponent<Toggle>();
            toggle.targetGraphic = bgGo.GetComponent<Image>();
            toggle.graphic = checkGo.GetComponent<Image>();
            toggle.isOn = defaultValue;

            var bgImg = bgGo.GetComponent<Image>();
            toggle.onValueChanged.AddListener(on => bgImg.color = on ? ColToggleOn : ColToggleOff);

            return toggle;
        }

        private void AddStatusRow(Transform parent, string label, bool isGood, string goodText, string badText)
        {
            var row = CreateRow(parent);
            AddLabel(row.transform, label + ":", 14, ColDimText, 110);
            AddLabel(row.transform, isGood ? goodText : badText, 15, isGood ? ColGreen : ColRed);
        }

        private void AddKVRow(Transform parent, string key, string value, Color? valueColor = null)
        {
            var row = CreateRow(parent);
            AddLabel(row.transform, key + ":", 14, ColDimText, 95);
            AddLabel(row.transform, value ?? "N/A", 14, valueColor ?? ColText);
        }

        private void AddBadge(Transform parent, string name, bool available)
        {
            Color bgColor = available ? new Color(0.12f, 0.38f, 0.18f, 1f) : new Color(0.35f, 0.12f, 0.12f, 1f);
            var badge = CreatePanelGO(parent, "Badge_" + name, bgColor);
            var le = badge.AddComponent<LayoutElement>();
            le.preferredHeight = 24;
            le.flexibleWidth = 1;

            var txtGo = new GameObject("Lbl");
            txtGo.transform.SetParent(badge.transform, false);
            var txt = txtGo.AddComponent<Text>();
            txt.text = name;
            txt.font = _defaultFont;
            txt.fontSize = 13;
            txt.fontStyle = FontStyle.Bold;
            txt.color = available ? ColGreen : ColRed;
            txt.alignment = TextAnchor.MiddleCenter;
            txt.raycastTarget = false;
            StretchFill(txtGo.GetComponent<RectTransform>());

            return;
        }

        private void ClearChildren(Transform parent, int keepCount = 0)
        {
            var toDestroy = new List<GameObject>();
            for (int i = keepCount; i < parent.childCount; i++)
                toDestroy.Add(parent.GetChild(i).gameObject);
            foreach (var go in toDestroy)
                Destroy(go);
        }

        private static Color Brighten(Color c, float amount)
        {
            return new Color(
                Mathf.Min(c.r + amount, 1f),
                Mathf.Min(c.g + amount, 1f),
                Mathf.Min(c.b + amount, 1f), 1f);
        }

        #endregion
    }
}
