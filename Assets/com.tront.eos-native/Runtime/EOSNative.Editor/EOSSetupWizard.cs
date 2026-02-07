using UnityEngine;
using UnityEditor;
using System.IO;
using System.Text;

namespace EOSNative.Editor
{
    /// <summary>
    /// Setup wizard window for configuring EOS credentials, managing dependencies,
    /// and providing help/about info.
    /// </summary>
    public class EOSSetupWizard : EditorWindow
    {
        private EOSConfig _config;
        private Vector2 _scrollPos;
        private bool _showAdvanced = false;
        private string _validationMessage = "";
        private MessageType _validationMessageType = MessageType.None;
        private int _currentTab = 0;

        // Styles
        private GUIStyle _headerStyle;
        private GUIStyle _subHeaderStyle;
        private GUIStyle _instructionStyle;
        private GUIStyle _linkStyle;
        private GUIStyle _versionStyle;
        private bool _stylesInitialized;

        private const string PORTAL_URL = "https://dev.epicgames.com/portal";
        private const string DOCS_URL = "https://dev.epicgames.com/docs/game-services/eos-get-started/services-quick-start";
        private const string GITHUB_URL = "https://github.com/TrentSterling/EOS-Native";
        private const string DOCS_SITE_URL = "https://tront.xyz/EOS-Native/";
        private const string PARRELSYNC_URL = "https://github.com/VeriorPies/ParrelSync.git?path=/ParrelSync";
        private const string PARRELSYNC_PACKAGE = "com.veriorpies.parrelsync";

        private static readonly string[] TabNames = { "Setup", "Dependencies", "About" };

        [MenuItem("Tools/EOS Native/Setup Wizard", priority = -100)]
        public static void ShowWindow()
        {
            var window = GetWindow<EOSSetupWizard>("EOS Setup Wizard");
            window.minSize = new Vector2(500, 600);
            window.Show();
        }

        private void OnEnable()
        {
            FindOrCreateConfig();
        }

        private void FindOrCreateConfig()
        {
            var guids = AssetDatabase.FindAssets("t:EOSConfig");
            if (guids.Length > 0)
            {
                var path = AssetDatabase.GUIDToAssetPath(guids[0]);
                _config = AssetDatabase.LoadAssetAtPath<EOSConfig>(path);
            }
        }

        private void InitStyles()
        {
            if (_stylesInitialized) return;

            _headerStyle = new GUIStyle(EditorStyles.boldLabel)
            {
                fontSize = 18,
                margin = new RectOffset(0, 0, 10, 10)
            };

            _subHeaderStyle = new GUIStyle(EditorStyles.boldLabel)
            {
                fontSize = 14,
                margin = new RectOffset(0, 0, 15, 5)
            };

            _instructionStyle = new GUIStyle(EditorStyles.wordWrappedLabel)
            {
                margin = new RectOffset(0, 0, 0, 10),
                padding = new RectOffset(10, 10, 5, 5)
            };
            _instructionStyle.normal.background = MakeTex(2, 2, new Color(0.2f, 0.2f, 0.2f, 0.3f));

            _linkStyle = new GUIStyle(EditorStyles.label)
            {
                normal = { textColor = new Color(0.3f, 0.6f, 1f) },
                hover = { textColor = new Color(0.5f, 0.8f, 1f) },
                margin = new RectOffset(0, 0, 5, 5)
            };

            _versionStyle = new GUIStyle(EditorStyles.label)
            {
                fontSize = 12,
                normal = { textColor = new Color(0.6f, 0.6f, 0.6f) }
            };

            _stylesInitialized = true;
        }

        private Texture2D MakeTex(int width, int height, Color col)
        {
            Color[] pix = new Color[width * height];
            for (int i = 0; i < pix.Length; i++)
                pix[i] = col;
            Texture2D result = new Texture2D(width, height);
            result.SetPixels(pix);
            result.Apply();
            return result;
        }

        private void OnGUI()
        {
            InitStyles();

            // Tab bar
            _currentTab = GUILayout.Toolbar(_currentTab, TabNames, GUILayout.Height(30));
            EditorGUILayout.Space(5);

            _scrollPos = EditorGUILayout.BeginScrollView(_scrollPos);

            switch (_currentTab)
            {
                case 0: DrawSetupTab(); break;
                case 1: DrawDependenciesTab(); break;
                case 2: DrawAboutTab(); break;
            }

            EditorGUILayout.Space(20);
            EditorGUILayout.EndScrollView();
        }

        #region Setup Tab

        private void DrawSetupTab()
        {
            EditorGUILayout.LabelField("EOS Setup Wizard", _headerStyle);
            EditorGUILayout.Space(5);

            // Quick Links
            EditorGUILayout.BeginHorizontal();
            if (GUILayout.Button("Open Developer Portal", GUILayout.Height(30)))
                Application.OpenURL(PORTAL_URL);
            if (GUILayout.Button("View Documentation", GUILayout.Height(30)))
                Application.OpenURL(DOCS_URL);
            EditorGUILayout.EndHorizontal();

            EditorGUILayout.Space(10);

            // Config Selection
            DrawConfigSection();

            if (_config == null)
            {
                EditorGUILayout.HelpBox("Create or select an EOSConfig to continue.", MessageType.Warning);
                return;
            }

            EditorGUILayout.Space(10);

            // Step-by-step sections
            DrawStep1_ProductSettings();
            DrawStep2_ClientCredentials();
            DrawStep3_EncryptionKey();
            DrawStep4_OptionalSettings();

            EditorGUILayout.Space(10);

            // Validation
            DrawValidation();
        }

        private void DrawConfigSection()
        {
            EditorGUILayout.LabelField("Configuration Asset", _subHeaderStyle);

            EditorGUILayout.BeginVertical(EditorStyles.helpBox);

            EditorGUI.BeginChangeCheck();
            _config = (EOSConfig)EditorGUILayout.ObjectField("EOS Config", _config, typeof(EOSConfig), false);
            if (EditorGUI.EndChangeCheck() && _config != null)
            {
                EditorUtility.SetDirty(_config);
            }

            EditorGUILayout.BeginHorizontal();
            if (GUILayout.Button("Create New Config"))
            {
                CreateNewConfig();
            }
            if (_config != null && GUILayout.Button("Select in Project"))
            {
                Selection.activeObject = _config;
                EditorGUIUtility.PingObject(_config);
            }
            EditorGUILayout.EndHorizontal();

            if (_config != null)
            {
                EditorGUILayout.Space(5);
                Color oldBg = GUI.backgroundColor;
                GUI.backgroundColor = new Color(0.3f, 0.7f, 1f);
                if (GUILayout.Button("Fill Sample/Test Credentials (PlayEveryWare Demo)", GUILayout.Height(28)))
                {
                    FillSampleCredentials();
                }
                GUI.backgroundColor = oldBg;
                EditorGUILayout.HelpBox(
                    "Fills all fields with the public PlayEveryWare demo credentials. " +
                    "These are safe for testing and publicly available.",
                    MessageType.Info
                );
            }

            EditorGUILayout.EndVertical();
        }

        private void CreateNewConfig()
        {
            string path = EditorUtility.SaveFilePanelInProject(
                "Create EOS Config",
                "EOSConfig",
                "asset",
                "Choose location for the EOS configuration asset"
            );

            if (!string.IsNullOrEmpty(path))
            {
                _config = ScriptableObject.CreateInstance<EOSConfig>();
                AssetDatabase.CreateAsset(_config, path);
                AssetDatabase.SaveAssets();
                Selection.activeObject = _config;
            }
        }

        private void DrawStep1_ProductSettings()
        {
            EditorGUILayout.LabelField("Step 1: Product Settings", _subHeaderStyle);

            EditorGUILayout.BeginVertical(EditorStyles.helpBox);

            EditorGUILayout.LabelField(
                "Find these values in the EOS Developer Portal:\n" +
                "Your Product > Product Settings > SDK Credentials & Deployment",
                _instructionStyle
            );

            EditorGUI.BeginChangeCheck();

            EditorGUILayout.BeginHorizontal();
            EditorGUILayout.LabelField(new GUIContent("Product Name", "A friendly name for your game. Used in SDK logging."), GUILayout.Width(120));
            _config.ProductName = EditorGUILayout.TextField(_config.ProductName);
            EditorGUILayout.EndHorizontal();

            EditorGUILayout.BeginHorizontal();
            EditorGUILayout.LabelField(new GUIContent("Product ID *", "Found in: Product Settings > SDK Credentials\nFormat: xxxxxxxxxxxxxxxx..."), GUILayout.Width(120));
            _config.ProductId = EditorGUILayout.TextField(_config.ProductId);
            EditorGUILayout.EndHorizontal();

            EditorGUILayout.BeginHorizontal();
            EditorGUILayout.LabelField(new GUIContent("Sandbox ID *", "Found in: Product Settings > SDK Credentials\nUsually starts with your product name"), GUILayout.Width(120));
            _config.SandboxId = EditorGUILayout.TextField(_config.SandboxId);
            EditorGUILayout.EndHorizontal();

            EditorGUILayout.BeginHorizontal();
            EditorGUILayout.LabelField(new GUIContent("Deployment ID *", "Found in: Product Settings > Deployment\nCreate one if none exist (e.g., 'dev', 'live')"), GUILayout.Width(120));
            _config.DeploymentId = EditorGUILayout.TextField(_config.DeploymentId);
            EditorGUILayout.EndHorizontal();

            if (EditorGUI.EndChangeCheck())
            {
                EditorUtility.SetDirty(_config);
            }

            EditorGUILayout.EndVertical();
        }

        private void DrawStep2_ClientCredentials()
        {
            EditorGUILayout.LabelField("Step 2: Client Credentials", _subHeaderStyle);

            EditorGUILayout.BeginVertical(EditorStyles.helpBox);

            EditorGUILayout.LabelField(
                "Create a Client in the Developer Portal:\n" +
                "Your Product > Product Settings > Clients > Add New Client\n" +
                "Policy: Peer2Peer (for P2P games) or GameClient",
                _instructionStyle
            );

            EditorGUI.BeginChangeCheck();

            EditorGUILayout.BeginHorizontal();
            EditorGUILayout.LabelField(new GUIContent("Client ID *", "Found in: Product Settings > Clients\nThe ID of your client policy"), GUILayout.Width(120));
            _config.ClientId = EditorGUILayout.TextField(_config.ClientId);
            EditorGUILayout.EndHorizontal();

            EditorGUILayout.BeginHorizontal();
            EditorGUILayout.LabelField(new GUIContent("Client Secret *", "Found in: Product Settings > Clients\nKeep this secret! Don't commit to public repos."), GUILayout.Width(120));
            _config.ClientSecret = EditorGUILayout.PasswordField(_config.ClientSecret);
            EditorGUILayout.EndHorizontal();

            if (EditorGUI.EndChangeCheck())
            {
                EditorUtility.SetDirty(_config);
            }

            EditorGUILayout.HelpBox(
                "Security Note: The Client Secret will be embedded in your build. " +
                "For production, consider using a backend server for authentication.",
                MessageType.Info
            );

            EditorGUILayout.EndVertical();
        }

        private void DrawStep3_EncryptionKey()
        {
            EditorGUILayout.LabelField("Step 3: Encryption Key", _subHeaderStyle);

            EditorGUILayout.BeginVertical(EditorStyles.helpBox);

            EditorGUILayout.LabelField(
                "Required for P2P networking and Player Data Storage.\n" +
                "Must be exactly 64 hexadecimal characters (32 bytes).",
                _instructionStyle
            );

            EditorGUI.BeginChangeCheck();

            EditorGUILayout.BeginHorizontal();
            EditorGUILayout.LabelField(new GUIContent("Encryption Key *", "64 hex characters for AES-256 encryption\nExample: 1111111111111111111111111111111111111111111111111111111111111111"), GUILayout.Width(120));
            _config.EncryptionKey = EditorGUILayout.TextField(_config.EncryptionKey);
            EditorGUILayout.EndHorizontal();

            int keyLength = _config.EncryptionKey?.Length ?? 0;
            Color oldColor = GUI.color;
            GUI.color = keyLength == 64 ? Color.green : (keyLength > 0 ? Color.yellow : Color.gray);
            EditorGUILayout.LabelField($"Characters: {keyLength}/64", EditorStyles.miniLabel);
            GUI.color = oldColor;

            if (EditorGUI.EndChangeCheck())
            {
                EditorUtility.SetDirty(_config);
            }

            EditorGUILayout.Space(5);

            if (GUILayout.Button("Generate Random Key"))
            {
                _config.EncryptionKey = GenerateEncryptionKey();
                EditorUtility.SetDirty(_config);
            }

            EditorGUILayout.HelpBox(
                "Save this key somewhere safe! If you lose it, you cannot decrypt existing player data.",
                MessageType.Warning
            );

            EditorGUILayout.EndVertical();
        }

        private void DrawStep4_OptionalSettings()
        {
            _showAdvanced = EditorGUILayout.Foldout(_showAdvanced, "Step 4: Optional Settings", true);

            if (!_showAdvanced) return;

            EditorGUILayout.BeginVertical(EditorStyles.helpBox);

            EditorGUI.BeginChangeCheck();

            EditorGUILayout.BeginHorizontal();
            EditorGUILayout.LabelField(new GUIContent("Display Name", "Default name for DeviceID login (max 32 chars)\nPlayers see this before setting their own name"), GUILayout.Width(120));
            _config.DefaultDisplayName = EditorGUILayout.TextField(_config.DefaultDisplayName);
            EditorGUILayout.EndHorizontal();

            EditorGUILayout.Space(10);

            EditorGUILayout.BeginHorizontal();
            EditorGUILayout.LabelField(new GUIContent("Is Server", "Enable for dedicated server builds\nDisables overlay and some client features"), GUILayout.Width(120));
            _config.IsServer = EditorGUILayout.Toggle(_config.IsServer);
            EditorGUILayout.EndHorizontal();

            EditorGUILayout.BeginHorizontal();
            EditorGUILayout.LabelField(new GUIContent("Tick Budget (ms)", "Max time for SDK work per frame (0 = unlimited)\nIncrease if you see hitches, decrease for more responsive callbacks"), GUILayout.Width(120));
            _config.TickBudgetInMilliseconds = (uint)EditorGUILayout.IntField((int)_config.TickBudgetInMilliseconds);
            EditorGUILayout.EndHorizontal();

            if (EditorGUI.EndChangeCheck())
            {
                EditorUtility.SetDirty(_config);
            }

            EditorGUILayout.EndVertical();
        }

        private void DrawValidation()
        {
            EditorGUILayout.LabelField("Validation", _subHeaderStyle);

            EditorGUILayout.BeginVertical(EditorStyles.helpBox);

            if (GUILayout.Button("Validate Configuration", GUILayout.Height(30)))
            {
                ValidateConfig();
            }

            if (!string.IsNullOrEmpty(_validationMessage))
            {
                EditorGUILayout.HelpBox(_validationMessage, _validationMessageType);
            }

            EditorGUILayout.EndVertical();

            EditorGUILayout.Space(10);

            // Quick status
            EditorGUILayout.BeginVertical(EditorStyles.helpBox);
            EditorGUILayout.LabelField("Quick Check", EditorStyles.boldLabel);

            DrawStatusRow("Product ID", !string.IsNullOrEmpty(_config.ProductId));
            DrawStatusRow("Sandbox ID", !string.IsNullOrEmpty(_config.SandboxId));
            DrawStatusRow("Deployment ID", !string.IsNullOrEmpty(_config.DeploymentId));
            DrawStatusRow("Client ID", !string.IsNullOrEmpty(_config.ClientId));
            DrawStatusRow("Client Secret", !string.IsNullOrEmpty(_config.ClientSecret));
            DrawStatusRow("Encryption Key (64 chars)", _config.EncryptionKey?.Length == 64);

            EditorGUILayout.EndVertical();
        }

        #endregion

        #region Dependencies Tab

        private void DrawDependenciesTab()
        {
            EditorGUILayout.LabelField("Dependencies", _headerStyle);

            EditorGUILayout.LabelField(
                "Optional packages that enhance EOS Native functionality.\n" +
                "Click Install to add them via Unity Package Manager.",
                _instructionStyle
            );

            EditorGUILayout.Space(10);

            // ParrelSync
            DrawDependencyRow(
                "ParrelSync",
                PARRELSYNC_PACKAGE,
                PARRELSYNC_URL,
                "Run multiple Unity Editor instances from the same project for local multiplayer testing. " +
                "Essential for testing P2P, lobbies, and voice chat without building.",
                "https://github.com/VeriorPies/ParrelSync"
            );

            EditorGUILayout.Space(10);

            // Input System (already required but show status)
            DrawDependencyStatus(
                "Input System",
                "com.unity.inputsystem",
                "Required for WASD ball controls in the P2P demo. Already referenced by EOSNative.asmdef."
            );

            EditorGUILayout.Space(10);

            // Unity UI
            DrawDependencyStatus(
                "Unity UI (uGUI)",
                "com.unity.ugui",
                "Required for Canvas-based runtime UI (EOSNativeCanvasUI, EOSNativeConsole)."
            );
        }

        private void DrawDependencyRow(string name, string packageId, string gitUrl, string description, string infoUrl)
        {
            bool installed = IsPackageInstalled(packageId);

            EditorGUILayout.BeginVertical(EditorStyles.helpBox);

            EditorGUILayout.BeginHorizontal();
            EditorGUILayout.LabelField(name, _subHeaderStyle);
            GUILayout.FlexibleSpace();

            Color oldColor = GUI.color;
            if (installed)
            {
                GUI.color = Color.green;
                EditorGUILayout.LabelField("Installed", _versionStyle, GUILayout.Width(70));
                GUI.color = oldColor;
            }
            else
            {
                GUI.color = Color.yellow;
                EditorGUILayout.LabelField("Not Installed", _versionStyle, GUILayout.Width(90));
                GUI.color = oldColor;
            }
            EditorGUILayout.EndHorizontal();

            EditorGUILayout.LabelField(description, EditorStyles.wordWrappedMiniLabel);
            EditorGUILayout.Space(5);

            EditorGUILayout.BeginHorizontal();

            if (!installed)
            {
                Color oldBg = GUI.backgroundColor;
                GUI.backgroundColor = new Color(0.3f, 0.8f, 0.4f);
                if (GUILayout.Button("Install", GUILayout.Height(25), GUILayout.Width(100)))
                {
                    InstallPackage(packageId, gitUrl);
                }
                GUI.backgroundColor = oldBg;
            }
            else
            {
                if (GUILayout.Button("Remove", GUILayout.Height(25), GUILayout.Width(100)))
                {
                    if (EditorUtility.DisplayDialog("Remove Package",
                        $"Remove {name} from the project?", "Remove", "Cancel"))
                    {
                        RemovePackage(packageId);
                    }
                }
            }

            if (GUILayout.Button("GitHub", GUILayout.Height(25), GUILayout.Width(80)))
            {
                Application.OpenURL(infoUrl);
            }

            GUILayout.FlexibleSpace();
            EditorGUILayout.EndHorizontal();

            EditorGUILayout.EndVertical();
        }

        private void DrawDependencyStatus(string name, string packageId, string description)
        {
            bool installed = IsPackageInstalled(packageId);

            EditorGUILayout.BeginVertical(EditorStyles.helpBox);

            EditorGUILayout.BeginHorizontal();
            EditorGUILayout.LabelField(name, _subHeaderStyle);
            GUILayout.FlexibleSpace();

            Color oldColor = GUI.color;
            GUI.color = installed ? Color.green : Color.red;
            EditorGUILayout.LabelField(installed ? "Installed" : "Missing", _versionStyle, GUILayout.Width(70));
            GUI.color = oldColor;

            EditorGUILayout.EndHorizontal();

            EditorGUILayout.LabelField(description, EditorStyles.wordWrappedMiniLabel);

            EditorGUILayout.EndVertical();
        }

        private bool IsPackageInstalled(string packageId)
        {
            string manifestPath = Path.Combine(Application.dataPath, "..", "Packages", "manifest.json");
            if (!File.Exists(manifestPath)) return false;
            string manifest = File.ReadAllText(manifestPath);
            return manifest.Contains($"\"{packageId}\"");
        }

        private void InstallPackage(string packageId, string gitUrl)
        {
            string manifestPath = Path.Combine(Application.dataPath, "..", "Packages", "manifest.json");
            if (!File.Exists(manifestPath))
            {
                Debug.LogError("[EOS Setup] manifest.json not found!");
                return;
            }

            string manifest = File.ReadAllText(manifestPath);
            if (manifest.Contains($"\"{packageId}\""))
            {
                Debug.Log($"[EOS Setup] {packageId} is already installed.");
                return;
            }

            // Insert after "dependencies": {
            string insertAfter = "\"dependencies\": {";
            int idx = manifest.IndexOf(insertAfter);
            if (idx < 0)
            {
                Debug.LogError("[EOS Setup] Could not parse manifest.json");
                return;
            }

            int insertPos = idx + insertAfter.Length;
            string entry = $"\n    \"{packageId}\": \"{gitUrl}\",";
            manifest = manifest.Insert(insertPos, entry);

            File.WriteAllText(manifestPath, manifest);
            Debug.Log($"[EOS Setup] Added {packageId} to manifest.json. Unity will resolve it now...");

            AssetDatabase.Refresh();
            // Trigger UPM resolution
            UnityEditor.PackageManager.Client.Resolve();
        }

        private void RemovePackage(string packageId)
        {
            UnityEditor.PackageManager.Client.Remove(packageId);
            Debug.Log($"[EOS Setup] Removing {packageId}...");
        }

        #endregion

        #region About Tab

        private void DrawAboutTab()
        {
            EditorGUILayout.LabelField("EOS Native", _headerStyle);

            string version = GetPackageVersion();
            EditorGUILayout.LabelField($"Version {version}", _versionStyle);
            EditorGUILayout.LabelField("EOS SDK 1.18.1.2", _versionStyle);
            EditorGUILayout.Space(10);

            EditorGUILayout.BeginVertical(EditorStyles.helpBox);
            EditorGUILayout.LabelField(
                "Epic Online Services SDK + high-level wrapper for Unity.\n\n" +
                "Provides the raw Epic.OnlineServices namespace plus EOSNative managers " +
                "for lobbies, voice chat, P2P networking, social features, storage, " +
                "anti-cheat, replays, and more.\n\n" +
                "Framework-agnostic - works with any networking solution (FishNet, Mirror, Netcode, or standalone).",
                EditorStyles.wordWrappedLabel
            );
            EditorGUILayout.EndVertical();

            EditorGUILayout.Space(10);

            // Links
            EditorGUILayout.LabelField("Links", _subHeaderStyle);
            EditorGUILayout.BeginVertical(EditorStyles.helpBox);

            DrawLinkButton("Documentation Site", DOCS_SITE_URL);
            DrawLinkButton("GitHub Repository", GITHUB_URL);
            DrawLinkButton("Epic Developer Portal", PORTAL_URL);
            DrawLinkButton("EOS SDK Documentation", DOCS_URL);

            EditorGUILayout.EndVertical();

            EditorGUILayout.Space(10);

            // Features
            EditorGUILayout.LabelField("Included Features", _subHeaderStyle);
            EditorGUILayout.BeginVertical(EditorStyles.helpBox);

            string[] features = {
                "Lobbies (create, join, search, quick match, host migration)",
                "Voice Chat (RTC, mic/speaker selection, mute, level meters)",
                "P2P Networking (raw P2P mesh, spring physics sync demo)",
                "Lobby Chat (text messaging with lobby members)",
                "Friends & Social (friends list, presence, user info, invites)",
                "Stats, Leaderboards & Achievements",
                "Player Data Storage & Title Storage (cloud saves)",
                "Replays (recording, playback, ghost visualization)",
                "Anti-Cheat (client/server integration)",
                "Moderation (reports, sanctions)",
                "Party System, Clans, Tournaments, Seasons",
                "Vote Kick, Map Vote, Rematch, Backfill, AFK Detection",
                "Runtime UI (OnGUI overlay + Canvas UI for mobile)",
                "Android Build Processor (desugaring, native libs)"
            };

            foreach (var feature in features)
            {
                EditorGUILayout.LabelField($"  {feature}", EditorStyles.wordWrappedMiniLabel);
            }

            EditorGUILayout.EndVertical();

            EditorGUILayout.Space(10);

            // Platforms
            EditorGUILayout.LabelField("Supported Platforms", _subHeaderStyle);
            EditorGUILayout.BeginVertical(EditorStyles.helpBox);

            DrawPlatformRow("Windows x64", true);
            DrawPlatformRow("Windows x86", true);
            DrawPlatformRow("macOS (Universal)", true);
            DrawPlatformRow("Linux x64", true);
            DrawPlatformRow("Linux ARM64", true);
            DrawPlatformRow("iOS (ARM64)", true);
            DrawPlatformRow("Android", true);

            EditorGUILayout.EndVertical();

            EditorGUILayout.Space(10);

            // Credits
            EditorGUILayout.LabelField("Credits", _subHeaderStyle);
            EditorGUILayout.BeginVertical(EditorStyles.helpBox);

            EditorGUILayout.LabelField("Trent Sterling (tront.xyz) - EOS Native package", EditorStyles.wordWrappedMiniLabel);
            EditorGUILayout.LabelField("Epic Games - EOS SDK", EditorStyles.wordWrappedMiniLabel);
            EditorGUILayout.LabelField("DrewMileham - Spring physics sync method", EditorStyles.wordWrappedMiniLabel);
            EditorGUILayout.LabelField("Skylar/CometDev - Mirror spring sync implementation", EditorStyles.wordWrappedMiniLabel);

            EditorGUILayout.EndVertical();
        }

        private void DrawLinkButton(string label, string url)
        {
            EditorGUILayout.BeginHorizontal();
            if (GUILayout.Button(label, GUILayout.Height(24)))
            {
                Application.OpenURL(url);
            }
            EditorGUILayout.EndHorizontal();
        }

        private void DrawPlatformRow(string platform, bool supported)
        {
            EditorGUILayout.BeginHorizontal();
            Color oldColor = GUI.color;
            GUI.color = supported ? Color.green : Color.red;
            EditorGUILayout.LabelField(supported ? "  +" : "  -", GUILayout.Width(20));
            GUI.color = oldColor;
            EditorGUILayout.LabelField(platform);
            EditorGUILayout.EndHorizontal();
        }

        private string GetPackageVersion()
        {
            // Try to read from package.json
            string[] guids = AssetDatabase.FindAssets("package t:TextAsset", new[] { "Assets/com.tront.eos-native" });
            foreach (var guid in guids)
            {
                string path = AssetDatabase.GUIDToAssetPath(guid);
                if (path.EndsWith("package.json"))
                {
                    string json = File.ReadAllText(path);
                    // Simple parse for "version": "x.y.z"
                    int vIdx = json.IndexOf("\"version\"");
                    if (vIdx >= 0)
                    {
                        int colon = json.IndexOf(':', vIdx);
                        int firstQuote = json.IndexOf('"', colon + 1);
                        int secondQuote = json.IndexOf('"', firstQuote + 1);
                        if (firstQuote >= 0 && secondQuote > firstQuote)
                            return json.Substring(firstQuote + 1, secondQuote - firstQuote - 1);
                    }
                }
            }

            // Fallback: try direct path
            string directPath = Path.Combine(Application.dataPath, "com.tront.eos-native", "package.json");
            if (File.Exists(directPath))
            {
                string json = File.ReadAllText(directPath);
                int vIdx = json.IndexOf("\"version\"");
                if (vIdx >= 0)
                {
                    int colon = json.IndexOf(':', vIdx);
                    int firstQuote = json.IndexOf('"', colon + 1);
                    int secondQuote = json.IndexOf('"', firstQuote + 1);
                    if (firstQuote >= 0 && secondQuote > firstQuote)
                        return json.Substring(firstQuote + 1, secondQuote - firstQuote - 1);
                }
            }

            return "unknown";
        }

        #endregion

        #region Helpers

        private void DrawStatusRow(string label, bool isValid)
        {
            EditorGUILayout.BeginHorizontal();
            EditorGUILayout.LabelField(label, GUILayout.Width(200));

            Color oldColor = GUI.color;
            GUI.color = isValid ? Color.green : Color.red;
            EditorGUILayout.LabelField(isValid ? "+" : "-", GUILayout.Width(20));
            GUI.color = oldColor;

            EditorGUILayout.EndHorizontal();
        }

        private void ValidateConfig()
        {
            if (_config.Validate(out string error))
            {
                _validationMessage = "Configuration is valid! You're ready to use EOS.";
                _validationMessageType = MessageType.Info;
            }
            else
            {
                _validationMessage = error;
                _validationMessageType = MessageType.Error;
            }
        }

        private void FillSampleCredentials()
        {
            Undo.RecordObject(_config, "Fill Sample EOS Credentials");
            _config.ProductName = "PlayEveryWare Demo";
            _config.ProductId = "f7102b835ed14b5fb6b3a05d87b3d101";
            _config.SandboxId = "ab139ee5b644412781cf99f48b993b45";
            _config.DeploymentId = "c529498f660a4a3d8a123fd04552cb47";
            _config.ClientId = "xyza7891wPzGRvRf4SkjlIF8YuqlRLbQ";
            _config.ClientSecret = "aXPlP1xDH0PXnp5U+i+M5pYHhaE1a8viV0l1GO422ms";
            _config.EncryptionKey = "1111111111111111111111111111111111111111111111111111111111111111";
            _config.DefaultDisplayName = "TestPlayer";
            EditorUtility.SetDirty(_config);
            AssetDatabase.SaveAssets();
            Debug.Log("[EOS Setup Wizard] Sample credentials filled (PlayEveryWare Demo)");
        }

        private string GenerateEncryptionKey()
        {
            StringBuilder sb = new StringBuilder(64);
            System.Random random = new System.Random();
            const string chars = "0123456789ABCDEF";

            for (int i = 0; i < 64; i++)
            {
                sb.Append(chars[random.Next(chars.Length)]);
            }

            return sb.ToString();
        }

        #endregion
    }
}
