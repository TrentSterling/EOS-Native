using System;
using System.Collections.Generic;
using System.Text;
using Epic.OnlineServices;
using EOSNative.Lobbies;
using EOSNative.Social;
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
        private static readonly string[] TabNames = { "Status", "Lobbies", "Voice", "Social" };

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
        private Image _micLevelFill;
        private Text _micLevelText;
        private int _selectedInputDevice = -1;
        private int _selectedOutputDevice = -1;

        // Status tab state
        private Transform _statusContainer;

        // Social tab state
        private Transform _socialContainer;

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
            scaler.referenceResolution = new Vector2(1080, 1920);
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
                tabTxt.fontSize = 17;
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
            scrollRect.movementType = ScrollRect.MovementType.Elastic;
            scrollRect.elasticity = 0.1f;
            scrollRect.scrollSensitivity = 40f;

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

            var helpSection = CreateSection(parent, "Voice Info");
            AddLabel(helpSection.transform, "Voice is lobby-based. Create a lobby with Voice enabled.", 13, ColDimText);
            AddLabel(helpSection.transform, "Voice auto-connects and persists through host migration.", 13, ColDimText);
        }

        #endregion

        #region Tab Building - Social

        private void BuildSocialTab(Transform parent)
        {
            _socialContainer = parent;
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
                        if (shown++ >= 10) break;
                        var row = CreateRow(recentSection.transform);
                        AddLabel(row.transform, name, 14, ColText);
                        bool isFriend = registry.IsFriend(puid);
                        string friendPuid = puid;
                        AddButton(row.transform, isFriend ? "Unfriend" : "Friend",
                            isFriend ? ColButtonDanger : ColButton, () => registry.ToggleFriend(friendPuid), -1, 80);
                    }
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
                        var row = CreateRow(friendSection.transform);
                        var status = registry.GetFriendStatus(puid);
                        Color statusColor = status switch
                        {
                            FriendStatus.InGame => ColGreen,
                            FriendStatus.InLobby => ColHeader,
                            FriendStatus.Offline => ColDimText,
                            _ => ColDimText
                        };
                        string icon = status switch
                        {
                            FriendStatus.InGame => "\u25CF",
                            FriendStatus.InLobby => "\u25CF",
                            _ => "\u25CB"
                        };
                        AddLabel(row.transform, $"{icon} {name}", 14, statusColor);
                        string removePuid = puid;
                        AddButton(row.transform, "Remove", ColButtonDanger, () => registry.RemoveFriend(removePuid), -1, 75);
                    }
                }

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
                }
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
                        Color friendColor = friend.Status == Epic.OnlineServices.Friends.FriendsStatus.Friends ? ColGreen : ColDimText;
                        AddLabel(row.transform, displayName, 14, friendColor);
                        AddLabel(row.transform, friend.Status.ToString(), 12, ColDimText, 85);
                    }
                }
                else
                {
                    AddLabel(epicSection.transform, "Epic Friends not available.", 14, ColDimText);
                }
            }
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
                // Remove old results (keep header + button = first 2 children)
                var children = new List<Transform>();
                for (int i = 0; i < _lobbySearchContainer.childCount; i++)
                    children.Add(_lobbySearchContainer.GetChild(i));
                for (int i = 2; i < children.Count; i++)
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
