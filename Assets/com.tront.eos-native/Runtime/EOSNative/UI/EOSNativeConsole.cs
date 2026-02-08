using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;

namespace EOSNative.UI
{
    /// <summary>
    /// Canvas-based runtime console that captures Debug.Log output.
    /// Works on Android/iOS where the built-in dev console is hard to read.
    /// Toggle visibility with the corner button or 3-finger tap.
    /// </summary>
    public class EOSNativeConsole : MonoBehaviour
    {
        #region Singleton

        private static EOSNativeConsole _instance;
        public static EOSNativeConsole Instance
        {
            get
            {
                if (_instance != null) return _instance;
#if UNITY_2023_1_OR_NEWER
                _instance = FindAnyObjectByType<EOSNativeConsole>();
#else
                _instance = FindObjectOfType<EOSNativeConsole>();
#endif
                if (_instance != null) return _instance;
                var go = new GameObject("[EOSNativeConsole]");
                if (EOSManager.Instance != null)
                    go.transform.SetParent(EOSManager.Instance.transform);
                else
                    DontDestroyOnLoad(go);
                _instance = go.AddComponent<EOSNativeConsole>();
                return _instance;
            }
        }

        #endregion

        #region Settings

        private const int MaxEntries = 200;
        private const int VisibleLines = 60;

        #endregion

        #region Colors

        private static readonly Color ColBg = new Color(0.05f, 0.05f, 0.08f, 0.95f);
        private static readonly Color ColHeader = new Color(0.08f, 0.08f, 0.12f, 1f);
        private static readonly Color ColLog = new Color(0.8f, 0.8f, 0.82f, 1f);
        private static readonly Color ColWarning = new Color(1f, 0.85f, 0.2f, 1f);
        private static readonly Color ColError = new Color(1f, 0.3f, 0.3f, 1f);
        private static readonly Color ColException = new Color(1f, 0.2f, 0.5f, 1f);
        private static readonly Color ColButton = new Color(0.2f, 0.35f, 0.55f, 1f);
        private static readonly Color ColFilterActive = new Color(0.3f, 0.5f, 0.7f, 1f);
        private static readonly Color ColFilterOff = new Color(0.2f, 0.2f, 0.25f, 1f);

        #endregion

        #region State

        private struct LogEntry
        {
            public string Message;
            public string StackTrace;
            public LogType Type;
            public int Count; // Collapse count
        }

        private readonly List<LogEntry> _entries = new List<LogEntry>();
        private bool _panelVisible;
        private bool _collapse = true;
        private bool _showLog = true;
        private bool _showWarning = true;
        private bool _showError = true;
        private int _logCount;
        private int _warningCount;
        private int _errorCount;

        /// <summary>Total number of log entries captured.</summary>
        public int EntryCount => _entries.Count;

        /// <summary>Number of log-level messages.</summary>
        public int LogCount => _logCount;

        /// <summary>Number of warning messages.</summary>
        public int WarningCount => _warningCount;

        /// <summary>Number of error messages.</summary>
        public int ErrorCount => _errorCount;

        /// <summary>Whether the console panel is currently visible.</summary>
        public bool IsVisible => _panelVisible;

        // UI references
        private Canvas _canvas;
        private GameObject _toggleButton;
        private Text _toggleBadge;
        private GameObject _mainPanel;
        private Text _consoleText;
        private Image _logFilterImg;
        private Image _warnFilterImg;
        private Image _errFilterImg;
        private Text _logFilterText;
        private Text _warnFilterText;
        private Text _errFilterText;

        private Font _font;
        private bool _built;
        private bool _dirty;

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

        private void OnEnable()
        {
            Application.logMessageReceived += OnLogReceived;
        }

        private void OnDisable()
        {
            Application.logMessageReceived -= OnLogReceived;
        }

        private void Start()
        {
            BuildUI();
        }

        private void LateUpdate()
        {
            if (_dirty && _panelVisible)
            {
                _dirty = false;
                RebuildConsoleText();
            }
        }

        #endregion

        #region Log Capture

        private void OnLogReceived(string message, string stackTrace, LogType type)
        {
            // Collapse duplicate messages
            if (_collapse && _entries.Count > 0)
            {
                var last = _entries[_entries.Count - 1];
                if (last.Message == message && last.Type == type)
                {
                    last.Count++;
                    _entries[_entries.Count - 1] = last;
                    IncrementCounter(type);
                    _dirty = true;
                    return;
                }
            }

            _entries.Add(new LogEntry
            {
                Message = message,
                StackTrace = stackTrace,
                Type = type,
                Count = 1
            });

            // Trim old entries
            while (_entries.Count > MaxEntries)
                _entries.RemoveAt(0);

            IncrementCounter(type);
            _dirty = true;
            UpdateBadge();
        }

        private void IncrementCounter(LogType type)
        {
            switch (type)
            {
                case LogType.Log: _logCount++; break;
                case LogType.Warning: _warningCount++; break;
                default: _errorCount++; break;
            }
        }

        #endregion

        #region UI Building

        private void BuildUI()
        {
            if (_built) return;
            _built = true;

            _font = Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");
            if (_font == null) _font = Resources.GetBuiltinResource<Font>("Arial.ttf");

            // Canvas
            var canvasGo = new GameObject("ConsoleCanvas");
            canvasGo.transform.SetParent(transform);
            _canvas = canvasGo.AddComponent<Canvas>();
            _canvas.renderMode = RenderMode.ScreenSpaceOverlay;
            _canvas.sortingOrder = 10000; // Above everything

            var scaler = canvasGo.AddComponent<CanvasScaler>();
            scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
            scaler.referenceResolution = new Vector2(1080, 1920);
            scaler.matchWidthOrHeight = 0.5f;

            canvasGo.AddComponent<GraphicRaycaster>();

            // Toggle button (bottom-left)
            BuildToggleButton(canvasGo.transform);

            // Main panel
            _mainPanel = BuildMainPanel(canvasGo.transform);
            _mainPanel.SetActive(false);
        }

        private void BuildToggleButton(Transform parent)
        {
            var go = new GameObject("ConsoleToggle");
            go.transform.SetParent(parent, false);
            var img = go.AddComponent<Image>();
            img.color = new Color(0.15f, 0.15f, 0.2f, 0.9f);

            var rt = go.GetComponent<RectTransform>();
            rt.anchorMin = new Vector2(0, 0);
            rt.anchorMax = new Vector2(0, 0);
            rt.pivot = new Vector2(0, 0);
            rt.anchoredPosition = new Vector2(20, 20);
            rt.sizeDelta = new Vector2(90, 60);

            var btn = go.AddComponent<Button>();
            btn.targetGraphic = img;
            btn.navigation = new Navigation { mode = Navigation.Mode.None };
            var colors = btn.colors;
            colors.normalColor = new Color(0.15f, 0.15f, 0.2f, 0.9f);
            colors.highlightedColor = new Color(0.25f, 0.25f, 0.35f, 0.95f);
            colors.pressedColor = ColButton;
            colors.fadeDuration = 0f;
            btn.colors = colors;
            btn.onClick.AddListener(TogglePanel);

            // Icon text
            var txt = CreateText(go.transform, "LOG", 18, ColLog, TextAnchor.UpperCenter);
            txt.fontStyle = FontStyle.Bold;
            var txtRT = txt.GetComponent<RectTransform>();
            txtRT.anchorMin = Vector2.zero;
            txtRT.anchorMax = Vector2.one;
            txtRT.offsetMin = new Vector2(2, 18);
            txtRT.offsetMax = new Vector2(-2, -2);

            // Badge (error count)
            var badgeGo = new GameObject("Badge");
            badgeGo.transform.SetParent(go.transform, false);
            _toggleBadge = badgeGo.AddComponent<Text>();
            _toggleBadge.font = _font;
            _toggleBadge.fontSize = 18;
            _toggleBadge.color = ColError;
            _toggleBadge.alignment = TextAnchor.LowerCenter;
            _toggleBadge.text = "";
            _toggleBadge.raycastTarget = false;
            var badgeRT = badgeGo.GetComponent<RectTransform>();
            badgeRT.anchorMin = Vector2.zero;
            badgeRT.anchorMax = Vector2.one;
            badgeRT.offsetMin = new Vector2(2, 2);
            badgeRT.offsetMax = new Vector2(-2, -20);

            _toggleButton = go;
        }

        private GameObject BuildMainPanel(Transform parent)
        {
            var panel = new GameObject("ConsolePanel");
            panel.transform.SetParent(parent, false);
            var panelImg = panel.AddComponent<Image>();
            panelImg.color = ColBg;

            var rt = panel.GetComponent<RectTransform>();
            rt.anchorMin = new Vector2(0.02f, 0.02f);
            rt.anchorMax = new Vector2(0.98f, 0.55f); // Bottom half of screen
            rt.offsetMin = Vector2.zero;
            rt.offsetMax = Vector2.zero;

            var vlg = panel.AddComponent<VerticalLayoutGroup>();
            vlg.padding = new RectOffset(6, 6, 6, 6);
            vlg.spacing = 4;
            vlg.childForceExpandWidth = true;
            vlg.childForceExpandHeight = false;
            vlg.childControlWidth = true;
            vlg.childControlHeight = true;

            // Header bar
            BuildHeaderBar(panel.transform);

            // Text area
            BuildTextArea(panel.transform);

            return panel;
        }

        private void BuildHeaderBar(Transform parent)
        {
            var bar = new GameObject("HeaderBar");
            bar.transform.SetParent(parent, false);
            bar.AddComponent<Image>().color = ColHeader;
            var barLE = bar.AddComponent<LayoutElement>();
            barLE.preferredHeight = 56;
            barLE.flexibleHeight = 0;
            barLE.flexibleWidth = 1;

            var hlg = bar.AddComponent<HorizontalLayoutGroup>();
            hlg.padding = new RectOffset(8, 8, 4, 4);
            hlg.spacing = 6;
            hlg.childForceExpandWidth = false;
            hlg.childForceExpandHeight = false;
            hlg.childControlWidth = true;
            hlg.childControlHeight = false;
            hlg.childAlignment = TextAnchor.MiddleCenter;

            // Title
            var title = CreateText(bar.transform, "Console", 20, new Color(0.6f, 0.8f, 1f, 1f), TextAnchor.MiddleLeft);
            title.fontStyle = FontStyle.Bold;
            title.gameObject.AddComponent<LayoutElement>().preferredWidth = 100;

            // Filter buttons
            _logFilterImg = CreateFilterButton(bar.transform, "Log", ColLog, out _logFilterText, () =>
            {
                _showLog = !_showLog;
                UpdateFilterColors();
                _dirty = true;
            });

            _warnFilterImg = CreateFilterButton(bar.transform, "Warn", ColWarning, out _warnFilterText, () =>
            {
                _showWarning = !_showWarning;
                UpdateFilterColors();
                _dirty = true;
            });

            _errFilterImg = CreateFilterButton(bar.transform, "Err", ColError, out _errFilterText, () =>
            {
                _showError = !_showError;
                UpdateFilterColors();
                _dirty = true;
            });

            UpdateFilterColors();

            // Collapse toggle
            CreateHeaderButton(bar.transform, "Collapse", () =>
            {
                _collapse = !_collapse;
                _dirty = true;
            }, 80);

            // Clear button
            CreateHeaderButton(bar.transform, "Clear", () =>
            {
                _entries.Clear();
                _logCount = _warningCount = _errorCount = 0;
                _dirty = true;
                UpdateBadge();
            }, 60);

            // Close button
            CreateHeaderButton(bar.transform, "X", TogglePanel, 44, new Color(0.5f, 0.15f, 0.15f, 1f));
        }

        private void BuildTextArea(Transform parent)
        {
            // Simple container — no ScrollRect, no RectMask2D, no ContentSizeFitter.
            // The console already limits output to VisibleLines entries, so scrolling is unnecessary.
            // This avoids the circular layout dependency that caused text flickering on resize.
            var container = new GameObject("TextArea");
            container.transform.SetParent(parent, false);
            container.AddComponent<Image>().color = new Color(0, 0, 0, 0);
            var containerLE = container.AddComponent<LayoutElement>();
            containerLE.flexibleHeight = 1;
            containerLE.flexibleWidth = 1;

            // Console text — stretched to fill container with horizontal padding
            _consoleText = CreateText(container.transform, "", 16, ColLog, TextAnchor.UpperLeft);
            _consoleText.horizontalOverflow = HorizontalWrapMode.Wrap;
            _consoleText.verticalOverflow = VerticalWrapMode.Truncate;
            _consoleText.supportRichText = true;
            _consoleText.raycastTarget = false;
            var textRT = _consoleText.GetComponent<RectTransform>();
            textRT.anchorMin = Vector2.zero;
            textRT.anchorMax = Vector2.one;
            textRT.offsetMin = new Vector2(6, 0);
            textRT.offsetMax = new Vector2(-6, 0);
        }

        #endregion

        #region UI Helpers

        private Text CreateText(Transform parent, string text, int fontSize, Color color, TextAnchor alignment)
        {
            var go = new GameObject("Text");
            go.transform.SetParent(parent, false);
            var txt = go.AddComponent<Text>();
            txt.text = text;
            txt.font = _font;
            txt.fontSize = fontSize;
            txt.color = color;
            txt.alignment = alignment;
            txt.raycastTarget = false;
            return txt;
        }

        private Image CreateFilterButton(Transform parent, string label, Color textColor,
            out Text labelText, System.Action onClick)
        {
            var go = new GameObject("Filter_" + label);
            go.transform.SetParent(parent, false);
            var img = go.AddComponent<Image>();
            img.color = ColFilterActive;

            var le = go.AddComponent<LayoutElement>();
            le.preferredWidth = 75;
            le.preferredHeight = 44;

            var btn = go.AddComponent<Button>();
            btn.targetGraphic = img;
            btn.navigation = new Navigation { mode = Navigation.Mode.None };
            btn.onClick.AddListener(() => onClick?.Invoke());

            labelText = CreateText(go.transform, $"{label}: 0", 16, textColor, TextAnchor.MiddleCenter);
            var txtRT = labelText.GetComponent<RectTransform>();
            txtRT.anchorMin = Vector2.zero;
            txtRT.anchorMax = Vector2.one;
            txtRT.offsetMin = Vector2.zero;
            txtRT.offsetMax = Vector2.zero;

            return img;
        }

        private void CreateHeaderButton(Transform parent, string label, System.Action onClick,
            float width = 60, Color? bg = null)
        {
            var go = new GameObject("Btn_" + label);
            go.transform.SetParent(parent, false);
            var img = go.AddComponent<Image>();
            img.color = bg ?? ColButton;

            var le = go.AddComponent<LayoutElement>();
            le.preferredWidth = width;
            le.preferredHeight = 44;

            var btn = go.AddComponent<Button>();
            btn.targetGraphic = img;
            btn.navigation = new Navigation { mode = Navigation.Mode.None };
            btn.onClick.AddListener(() => onClick?.Invoke());

            var txt = CreateText(go.transform, label, 16, Color.white, TextAnchor.MiddleCenter);
            var txtRT = txt.GetComponent<RectTransform>();
            txtRT.anchorMin = Vector2.zero;
            txtRT.anchorMax = Vector2.one;
            txtRT.offsetMin = Vector2.zero;
            txtRT.offsetMax = Vector2.zero;
        }

        #endregion

        #region Rendering

        private void RebuildConsoleText()
        {
            if (_consoleText == null) return;

            var sb = new System.Text.StringBuilder(4096);
            int shown = 0;

            for (int i = _entries.Count - 1; i >= 0 && shown < VisibleLines; i--)
            {
                var entry = _entries[i];

                // Filter
                bool show = entry.Type switch
                {
                    LogType.Log => _showLog,
                    LogType.Warning => _showWarning,
                    _ => _showError
                };
                if (!show) continue;

                string colorHex = entry.Type switch
                {
                    LogType.Warning => ColorUtility.ToHtmlStringRGB(ColWarning),
                    LogType.Error => ColorUtility.ToHtmlStringRGB(ColError),
                    LogType.Exception => ColorUtility.ToHtmlStringRGB(ColException),
                    LogType.Assert => ColorUtility.ToHtmlStringRGB(ColError),
                    _ => ColorUtility.ToHtmlStringRGB(ColLog)
                };

                string prefix = entry.Type switch
                {
                    LogType.Warning => "W",
                    LogType.Error => "E",
                    LogType.Exception => "X",
                    LogType.Assert => "A",
                    _ => " "
                };

                string collapse = entry.Count > 1 ? $" <color=#888888>x{entry.Count}</color>" : "";

                // Truncate long messages
                string msg = entry.Message;
                if (msg.Length > 300) msg = msg.Substring(0, 300) + "...";

                // Show stack trace for all log types (file:line info in dev builds)
                string trace = "";
                if (!string.IsNullOrEmpty(entry.StackTrace))
                {
                    string st = entry.StackTrace;
                    if (st.Length > 500) st = st.Substring(0, 500) + "...";
                    trace = $"\n<color=#666666><size=12>{st}</size></color>";
                }

                sb.Insert(0, $"<color=#{colorHex}>[{prefix}]</color> {msg}{collapse}{trace}\n");
                shown++;
            }

            _consoleText.text = sb.ToString();

            // Update filter button labels
            if (_logFilterText != null) _logFilterText.text = $"Log:{_logCount}";
            if (_warnFilterText != null) _warnFilterText.text = $"W:{_warningCount}";
            if (_errFilterText != null) _errFilterText.text = $"E:{_errorCount}";
        }

        private void UpdateFilterColors()
        {
            if (_logFilterImg != null) _logFilterImg.color = _showLog ? ColFilterActive : ColFilterOff;
            if (_warnFilterImg != null) _warnFilterImg.color = _showWarning ? ColFilterActive : ColFilterOff;
            if (_errFilterImg != null) _errFilterImg.color = _showError ? ColFilterActive : ColFilterOff;
        }

        private void UpdateBadge()
        {
            if (_toggleBadge == null) return;
            int total = _warningCount + _errorCount;
            _toggleBadge.text = total > 0 ? total.ToString() : "";
            _toggleBadge.color = _errorCount > 0 ? ColError : ColWarning;
        }

        private void TogglePanel()
        {
            _panelVisible = !_panelVisible;
            if (_mainPanel != null) _mainPanel.SetActive(_panelVisible);
            if (_panelVisible)
            {
                _dirty = true; // Force refresh when opening
            }
        }

        #endregion
    }
}
