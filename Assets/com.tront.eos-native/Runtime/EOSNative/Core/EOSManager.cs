using System;
using System.Reflection;
using System.Runtime.InteropServices;
using System.Threading.Tasks;
using Epic.OnlineServices;
using Epic.OnlineServices.Connect;
using Epic.OnlineServices.Lobby;
using Epic.OnlineServices.P2P;
using Epic.OnlineServices.Platform;
using Epic.OnlineServices.RTC;
using Epic.OnlineServices.RTCAudio;
using EOSNative.Lobbies;
using EOSNative.Logging;
using EOSNative.UI;
using UnityEngine;
#if UNITY_EDITOR
using UnityEditor;
#endif

namespace EOSNative
{
    /// <summary>
    /// Singleton MonoBehaviour that manages the EOS SDK lifecycle.
    /// Handles platform-specific library loading, SDK initialization, device token login, and shutdown.
    /// </summary>
    public class EOSManager : MonoBehaviour
    {
        #region Singleton

        private static EOSManager s_Instance;

        /// <summary>
        /// The singleton instance of EOSManager.
        /// </summary>
        public static EOSManager Instance
        {
            get
            {
                if (s_Instance == null)
                {
#if UNITY_2023_1_OR_NEWER
                    s_Instance = FindFirstObjectByType<EOSManager>();
#else
                    s_Instance = FindObjectOfType<EOSManager>();
#endif
                }
                return s_Instance;
            }
        }

        #endregion

        #region Public State

        /// <summary>
        /// Whether the EOS SDK has been initialized successfully.
        /// </summary>
        public bool IsInitialized { get; private set; }

        /// <summary>
        /// Whether a user is currently logged in via the Connect interface.
        /// </summary>
        public bool IsLoggedIn { get; private set; }

        /// <summary>
        /// The ProductUserId of the currently logged in user.
        /// </summary>
        public ProductUserId LocalProductUserId { get; private set; }

        #endregion

        #region Events

        /// <summary>
        /// Fired when the EOS SDK is successfully initialized.
        /// </summary>
        public event Action OnInitialized;

        /// <summary>
        /// Fired when a user successfully logs in via the Connect interface.
        /// </summary>
        public event Action<ProductUserId> OnLoginSuccess;

        /// <summary>
        /// Fired when a login attempt fails.
        /// </summary>
        public event Action<Result> OnLoginFailed;

        /// <summary>
        /// Fired when the user logs out.
        /// </summary>
        public event Action OnLogout;

        /// <summary>
        /// Fired when authentication is about to expire (approximately 10 minutes before).
        /// </summary>
        public event Action OnAuthExpiring;

        #endregion

        #region Settings

        [Header("Configuration")]
        [Tooltip("EOS credentials config. If empty, loads 'SampleEOSConfig' from Resources.")]
        [SerializeField] private EOSConfig _config;

        [Header("Auto Bootstrap")]
        [Tooltip("Automatically initialize EOS SDK on Start")]
        [SerializeField] private bool _autoInitialize = true;

        [Tooltip("Automatically login after initialization")]
        [SerializeField] private bool _autoLogin = true;

        [Tooltip("Display name for device token login (ParrelSync safe)")]
        [SerializeField] private string _displayName = "Player";

        [Header("Status Overlay")]
        [Tooltip("Show the F1 status overlay in-game (adds EOSNativeStatusUI)")]
        [SerializeField] private bool _showStatusOverlay = true;

        /// <summary>
        /// The config currently assigned or loaded from Resources.
        /// </summary>
        public EOSConfig Config => _config;

        #endregion

        #region Private Fields

        private PlatformInterface _platform;
        private ulong _authExpirationHandle;
        private ulong _loginStatusChangedHandle;

        // Tracks if SDK initialization failed in a way that requires Unity restart
        private static bool s_sdkCorrupted;

#if UNITY_EDITOR_WIN
        [DllImport("Kernel32.dll")]
        private static extern IntPtr LoadLibrary(string lpLibFileName);

        [DllImport("Kernel32.dll")]
        private static extern int FreeLibrary(IntPtr hLibModule);

        [DllImport("Kernel32.dll")]
        private static extern IntPtr GetProcAddress(IntPtr hModule, string lpProcName);

        private IntPtr _libraryPointer;
#endif

#if UNITY_EDITOR_OSX
        [DllImport("libdl.dylib")]
        private static extern IntPtr dlopen(string filename, int flags);

        [DllImport("libdl.dylib")]
        private static extern int dlclose(IntPtr handle);

        [DllImport("libdl.dylib")]
        private static extern IntPtr dlsym(IntPtr handle, string symbol);

        [DllImport("libdl.dylib")]
        private static extern IntPtr dlerror();

        private const int RTLD_NOW = 2;
        private IntPtr _libraryPointer;

        private static IntPtr LoadLibraryOSX(string path)
        {
            dlerror();
            IntPtr handle = dlopen(path, RTLD_NOW);
            if (handle == IntPtr.Zero)
            {
                IntPtr error = dlerror();
                throw new Exception("dlopen: " + Marshal.PtrToStringAnsi(error));
            }
            return handle;
        }

        private static int FreeLibraryOSX(IntPtr handle)
        {
            return dlclose(handle);
        }

        private static IntPtr GetProcAddressOSX(IntPtr handle, string procName)
        {
            dlerror();
            IntPtr res = dlsym(handle, procName);
            IntPtr errPtr = dlerror();
            if (errPtr != IntPtr.Zero)
            {
                throw new Exception("dlsym: " + Marshal.PtrToStringAnsi(errPtr));
            }
            return res;
        }
#endif

#if UNITY_EDITOR_LINUX
        [DllImport("__Internal")]
        private static extern IntPtr dlopen(string filename, int flags);

        [DllImport("__Internal")]
        private static extern int dlclose(IntPtr handle);

        [DllImport("__Internal")]
        private static extern IntPtr dlsym(IntPtr handle, string symbol);

        [DllImport("__Internal")]
        private static extern IntPtr dlerror();

        private const int RTLD_NOW = 2;
        private IntPtr _libraryPointer;

        private static IntPtr LoadLibraryLinux(string path)
        {
            dlerror();
            IntPtr handle = dlopen(path, RTLD_NOW);
            if (handle == IntPtr.Zero)
            {
                IntPtr error = dlerror();
                throw new Exception("dlopen failed: " + Marshal.PtrToStringAnsi(error));
            }
            return handle;
        }

        private static int FreeLibraryLinux(IntPtr handle)
        {
            return dlclose(handle);
        }

        private static IntPtr GetProcAddressLinux(IntPtr handle, string procName)
        {
            dlerror();
            IntPtr res = dlsym(handle, procName);
            IntPtr errPtr = dlerror();
            if (errPtr != IntPtr.Zero)
            {
                throw new Exception("dlsym: " + Marshal.PtrToStringAnsi(errPtr));
            }
            return res;
        }
#endif

        #endregion

        #region Interface Accessors

        /// <summary>
        /// Gets the Connect interface for authentication operations.
        /// </summary>
        public ConnectInterface ConnectInterface => _platform?.GetConnectInterface();

        /// <summary>
        /// Gets the P2P interface for peer-to-peer networking.
        /// </summary>
        public P2PInterface P2PInterface => _platform?.GetP2PInterface();

        /// <summary>
        /// Gets the Lobby interface for lobby operations.
        /// </summary>
        public LobbyInterface LobbyInterface => _platform?.GetLobbyInterface();

        /// <summary>
        /// Gets the RTC interface for voice/video operations.
        /// </summary>
        public RTCInterface RTCInterface => _platform?.GetRTCInterface();

        /// <summary>
        /// Gets the RTCAudio interface for voice audio operations.
        /// </summary>
        public RTCAudioInterface RTCAudioInterface => RTCInterface?.GetAudioInterface();

        /// <summary>
        /// Player Data Storage interface for cloud saves (400MB per player).
        /// </summary>
        public Epic.OnlineServices.PlayerDataStorage.PlayerDataStorageInterface PlayerDataStorageInterface => _platform?.GetPlayerDataStorageInterface();

        /// <summary>
        /// Title Storage interface for game configs (read-only).
        /// </summary>
        public Epic.OnlineServices.TitleStorage.TitleStorageInterface TitleStorageInterface => _platform?.GetTitleStorageInterface();

        /// <summary>
        /// Reports interface for player behavior reporting.
        /// </summary>
        public Epic.OnlineServices.Reports.ReportsInterface ReportsInterface => _platform?.GetReportsInterface();

        /// <summary>
        /// Auth interface for Epic Account login.
        /// </summary>
        public Epic.OnlineServices.Auth.AuthInterface AuthInterface => _platform?.GetAuthInterface();

        /// <summary>
        /// Friends interface for Epic Account friends list.
        /// </summary>
        public Epic.OnlineServices.Friends.FriendsInterface FriendsInterface => _platform?.GetFriendsInterface();

        /// <summary>
        /// Presence interface for online status.
        /// </summary>
        public Epic.OnlineServices.Presence.PresenceInterface PresenceInterface => _platform?.GetPresenceInterface();

        /// <summary>
        /// User Info interface for player profiles.
        /// </summary>
        public Epic.OnlineServices.UserInfo.UserInfoInterface UserInfoInterface => _platform?.GetUserInfoInterface();

        /// <summary>
        /// Custom Invites interface for cross-platform game invitations.
        /// </summary>
        public Epic.OnlineServices.CustomInvites.CustomInvitesInterface CustomInvitesInterface => _platform?.GetCustomInvitesInterface();

        /// <summary>
        /// Metrics interface for player session telemetry.
        /// </summary>
        public Epic.OnlineServices.Metrics.MetricsInterface MetricsInterface => _platform?.GetMetricsInterface();

        /// <summary>
        /// Achievements interface for game achievements.
        /// </summary>
        public Epic.OnlineServices.Achievements.AchievementsInterface AchievementsInterface => _platform?.GetAchievementsInterface();

        /// <summary>
        /// Stats interface for player statistics.
        /// </summary>
        public Epic.OnlineServices.Stats.StatsInterface StatsInterface => _platform?.GetStatsInterface();

        /// <summary>
        /// Leaderboards interface for ranking queries.
        /// </summary>
        public Epic.OnlineServices.Leaderboards.LeaderboardsInterface LeaderboardsInterface => _platform?.GetLeaderboardsInterface();

        /// <summary>
        /// The local EpicAccountId (if logged in via Auth Interface).
        /// </summary>
        public Epic.OnlineServices.EpicAccountId LocalEpicAccountId { get; private set; }

        /// <summary>
        /// Whether logged in via Epic Account (enables social features).
        /// </summary>
        public bool IsEpicAccountLoggedIn => LocalEpicAccountId != null && LocalEpicAccountId.IsValid();

        /// <summary>
        /// Gets the Platform interface directly.
        /// </summary>
        public PlatformInterface Platform => _platform;

        #endregion

        #region Unity Lifecycle

        private void Awake()
        {
            if (s_Instance != null && s_Instance != this)
            {
                EOSDebugLogger.LogWarning(DebugCategory.EOSManager, "EOSManager", "Duplicate instance detected, destroying this one.");
                Destroy(gameObject);
                return;
            }

            s_Instance = this;
            // Only call DontDestroyOnLoad if we're a root object (not a child of NetworkManager)
            if (transform.parent == null)
                DontDestroyOnLoad(gameObject);

#if UNITY_EDITOR
            // Subscribe to play mode changes to prevent crashes when exiting play mode
            EditorApplication.playModeStateChanged += OnPlayModeStateChanged;
#endif

            LoadNativeLibrary();
            LoadAndroidLibrary();
        }

        private async void Start()
        {
            // Auto-load config if not assigned
            if (_config == null)
            {
                // Try Resources folders first (works in builds)
                _config = Resources.Load<EOSConfig>("SampleEOSConfig");
                if (_config == null)
                    _config = Resources.Load<EOSConfig>("EOSConfig");
            }

#if UNITY_EDITOR
            // Editor fallback: search entire project for any EOSConfig asset
            if (_config == null)
            {
                var guids = AssetDatabase.FindAssets("t:EOSConfig");
                if (guids.Length > 0)
                {
                    _config = AssetDatabase.LoadAssetAtPath<EOSConfig>(AssetDatabase.GUIDToAssetPath(guids[0]));
                    if (_config != null)
                    {
                        EOSDebugLogger.Log(DebugCategory.EOSManager, "EOSManager", $"Auto-found config: {AssetDatabase.GUIDToAssetPath(guids[0])}");
                    }
                }
            }
#endif

            // Auto-initialize
            if (_autoInitialize && !IsInitialized && _config != null)
            {
                EOSDebugLogger.Log(DebugCategory.EOSManager, "EOSManager", "Auto-initializing EOS SDK...");
                var result = Initialize(_config);
                if (result != Result.Success)
                {
                    Debug.LogError($"[EOSManager] Auto-init failed: {result}");
                    return;
                }
            }

            // Auto-login with device token (ParrelSync safe)
            if (_autoLogin && IsInitialized && !IsLoggedIn)
            {
                string name = !string.IsNullOrEmpty(_displayName) ? _displayName
                    : (_config != null && !string.IsNullOrEmpty(_config.DefaultDisplayName) ? _config.DefaultDisplayName : "Player");

                EOSDebugLogger.Log(DebugCategory.EOSManager, "EOSManager", $"Auto-login as '{name}'...");
                var loginResult = await LoginWithDeviceTokenAsync(name);
                if (loginResult != Result.Success)
                {
                    Debug.LogError($"[EOSManager] Auto-login failed: {loginResult}");
                }
            }

            // Auto-add status overlay
            if (_showStatusOverlay && GetComponent<EOSNativeStatusUI>() == null)
            {
                gameObject.AddComponent<EOSNativeStatusUI>();
            }
        }

        private void FixedUpdate()
        {
            _platform?.Tick();
        }

        private void OnApplicationPause(bool pauseStatus)
        {
            if (!IsInitialized) return;

            if (pauseStatus)
            {
                // App is being suspended/backgrounded
                SetApplicationStatus(ApplicationStatus.BackgroundSuspended);
            }
            else
            {
                // App is being resumed/foregrounded
                SetApplicationStatus(ApplicationStatus.Foreground);
            }
        }

        private void OnApplicationFocus(bool hasFocus)
        {
            if (!IsInitialized) return;

            // On some platforms (especially mobile), focus changes indicate app state
            if (hasFocus)
            {
                SetApplicationStatus(ApplicationStatus.Foreground);
            }
        }

        private void OnApplicationQuit()
        {
            Shutdown();
        }

        private void OnDestroy()
        {
            if (s_Instance == this)
            {
#if UNITY_EDITOR
                EditorApplication.playModeStateChanged -= OnPlayModeStateChanged;
                // Skip shutdown if we already did it in OnPlayModeStateChanged
                if (!_isExitingPlayMode)
                {
                    Shutdown();
                }
#else
                Shutdown();
#endif
                s_Instance = null;
            }
        }

#if UNITY_EDITOR
        private bool _isExitingPlayMode;

        /// <summary>
        /// Editor safety pattern: properly shut down EOS before Unity tears things down.
        /// </summary>
        private void OnPlayModeStateChanged(PlayModeStateChange state)
        {
            if (state == PlayModeStateChange.ExitingPlayMode)
            {
                EOSDebugLogger.Log(DebugCategory.EOSManager, "EOSManager", "Exiting play mode - performing clean shutdown");
                _isExitingPlayMode = true;

                // Do a proper shutdown while we still can
                Shutdown();
            }
        }
#endif

        #endregion

        #region Initialization

        /// <summary>
        /// Initializes the EOS SDK with the provided configuration.
        /// </summary>
        /// <param name="config">The EOSConfig asset containing credentials.</param>
        /// <returns>The result of the initialization.</returns>
        public Result Initialize(EOSConfig config)
        {
            if (IsInitialized)
            {
                EOSDebugLogger.LogWarning(DebugCategory.EOSManager, "EOSManager", "Already initialized.");
                return Result.Success;
            }

            // Check if SDK is in a corrupted state from a previous crash
            if (s_sdkCorrupted)
            {
                EOSDebugLogger.LogError("EOSManager", "EOS SDK is in a corrupted state from a previous crash. Please restart Unity to reinitialize.");
                return Result.UnexpectedError;
            }

            if (config == null)
            {
                EOSDebugLogger.LogError("EOSManager", "Config is null.");
                return Result.InvalidParameters;
            }

            if (!config.Validate(out string error))
            {
                Debug.LogError($"[EOSManager] Config validation failed: {error}");
                return Result.InvalidParameters;
            }

            // Initialize the SDK
            var initOptions = new InitializeOptions
            {
                ProductName = config.ProductName,
                ProductVersion = Application.version
            };

            Result initResult = PlatformInterface.Initialize(ref initOptions);
            if (initResult != Result.Success && initResult != Result.AlreadyConfigured)
            {
                Debug.LogError($"[EOSManager] PlatformInterface.Initialize failed: {initResult}");
                return initResult;
            }

            // Create the platform interface using platform-specific options
            _platform = CreatePlatformInterface(config);
            if (_platform == null)
            {
                // Mark SDK as corrupted - this usually happens after a crash during the previous session
                s_sdkCorrupted = true;
                EOSDebugLogger.LogError("EOSManager", "PlatformInterface.Create returned null. EOS SDK may be in a bad state - please restart Unity.");
                return Result.UnexpectedError;
            }

            IsInitialized = true;
            EOSDebugLogger.Log(DebugCategory.EOSManager, "EOSManager", "EOS SDK initialized successfully.");

            // Set network status to Online by default on PC/mobile platforms
            // On consoles, the game may need to handle this differently based on platform network APIs
#if !UNITY_PS4 && !UNITY_PS5 && !UNITY_SWITCH && !UNITY_GAMECORE
            SetNetworkStatus(NetworkStatus.Online);
#else
            // On consoles, network starts as Disabled - game must call SetNetworkOnline()
            // when network connectivity is confirmed via platform APIs
            EOSDebugLogger.Log(DebugCategory.EOSManager, "EOSManager", "Console platform detected - call SetNetworkOnline() when network is available.");
#endif

            OnInitialized?.Invoke();

            return Result.Success;
        }

        private PlatformInterface CreatePlatformInterface(EOSConfig config)
        {
#if UNITY_STANDALONE_WIN || UNITY_EDITOR_WIN
            // Get XAudio2 DLL path for RTC/Voice support
            string xAudioDllPath = GetXAudio2DllPath();
            EOSDebugLogger.Log(DebugCategory.EOSManager, "EOSManager", $" XAudio2 DLL path: {xAudioDllPath}");

            // Use WindowsOptions for Windows platforms
            var platformOptions = new WindowsOptions
            {
                ProductId = config.ProductId,
                SandboxId = config.SandboxId,
                DeploymentId = config.DeploymentId,
                ClientCredentials = new ClientCredentials
                {
                    ClientId = config.ClientId,
                    ClientSecret = config.ClientSecret
                },
                EncryptionKey = config.EncryptionKey,
                CacheDirectory = Application.temporaryCachePath,
                IsServer = config.IsServer,
                TickBudgetInMilliseconds = config.TickBudgetInMilliseconds,
                Flags = GetPlatformFlags(),
                // RTC Options required for Voice/RTC functionality on Windows
                RTCOptions = new WindowsRTCOptions
                {
                    PlatformSpecificOptions = new WindowsRTCOptionsPlatformSpecificOptions
                    {
                        XAudio29DllPath = xAudioDllPath
                    }
                }
            };
            return PlatformInterface.Create(ref platformOptions);
#else
            // Use generic Options for other platforms
            var platformOptions = new Options
            {
                ProductId = config.ProductId,
                SandboxId = config.SandboxId,
                DeploymentId = config.DeploymentId,
                ClientCredentials = new ClientCredentials
                {
                    ClientId = config.ClientId,
                    ClientSecret = config.ClientSecret
                },
                EncryptionKey = config.EncryptionKey,
                CacheDirectory = Application.temporaryCachePath,
                IsServer = config.IsServer,
                TickBudgetInMilliseconds = config.TickBudgetInMilliseconds,
                Flags = GetPlatformFlags()
            };
            return PlatformInterface.Create(ref platformOptions);
#endif
        }

        /// <summary>
        /// Initializes the EOS SDK asynchronously.
        /// </summary>
        public async Task<Result> InitializeAsync(EOSConfig config)
        {
            return await Task.Run(() => Initialize(config));
        }

        private PlatformFlags GetPlatformFlags()
        {
#if UNITY_EDITOR
            return PlatformFlags.LoadingInEditor | PlatformFlags.DisableOverlay | PlatformFlags.DisableSocialOverlay;
#elif UNITY_SERVER
            return PlatformFlags.None;
#else
            return PlatformFlags.None;
#endif
        }

#if UNITY_STANDALONE_WIN || UNITY_EDITOR_WIN
        /// <summary>
        /// Gets the absolute path to the xaudio2_9redist.dll required for RTC/Voice on Windows.
        /// </summary>
        private string GetXAudio2DllPath()
        {
#if UNITY_EDITOR
            // Search candidate paths for the DLL in Editor
            // Supports both UPM package layout and legacy Assets/Plugins layout
            string[] candidatePaths = new[]
            {
                // UPM package path (com.tront.eos-native)
                "Packages/com.tront.eos-native/Runtime/EOSSDK/Plugins/Windows/x64/xaudio2_9redist.dll",
                // Embedded in Assets (UPM local/embedded package)
                "Assets/com.tront.eos-native/Runtime/EOSSDK/Plugins/Windows/x64/xaudio2_9redist.dll",
                // Legacy flat layout
                "Assets/Plugins/EOSSDK/Windows/x64/xaudio2_9redist.dll",
                // Alternative legacy layout
                "Assets/Plugins/Windows/x64/xaudio2_9redist.dll",
            };

            foreach (string candidate in candidatePaths)
            {
                string fullPath = System.IO.Path.GetFullPath(candidate);
                if (System.IO.File.Exists(fullPath))
                {
                    EOSDebugLogger.Log(DebugCategory.EOSManager, "EOSManager", $"Found XAudio2 DLL at: {fullPath}");
                    return fullPath;
                }
            }

            // Fallback: return the most likely path even if not found (SDK will report its own error)
            string fallback = System.IO.Path.GetFullPath(candidatePaths[0]);
            EOSDebugLogger.LogWarning(DebugCategory.EOSManager, "EOSManager", $"XAudio2 DLL not found at any candidate path, using fallback: {fallback}");
            return fallback;
#else
            // Build path - Unity copies DLLs to the Plugins folder
            string dataPath = Application.dataPath;
            return System.IO.Path.Combine(dataPath, "Plugins", "x86_64", "xaudio2_9redist.dll");
#endif
        }
#endif

        #endregion

        #region Device Token Login

        /// <summary>
        /// Logs in using a device token (anonymous authentication).
        /// Creates a new device ID if one doesn't exist.
        /// </summary>
        /// <param name="displayName">The display name for the user.</param>
        /// <returns>The result of the login operation.</returns>
        public async Task<Result> LoginWithDeviceTokenAsync(string displayName)
        {
            if (!IsInitialized)
            {
                EOSDebugLogger.LogError("EOSManager", "Cannot login - SDK not initialized.");
                return Result.NotConfigured;
            }

            if (IsLoggedIn)
            {
                EOSDebugLogger.LogWarning(DebugCategory.EOSManager, "EOSManager", "Already logged in.");
                return Result.Success;
            }

            if (string.IsNullOrEmpty(displayName) || displayName.Length > 32)
            {
                EOSDebugLogger.LogError("EOSManager", "Display name must be 1-32 characters.");
                return Result.InvalidParameters;
            }

            // Delete existing device ID (for clean Editor re-runs)
            await DeleteDeviceIdAsync();

            // Create device ID with ParrelSync support
            Result createResult = await CreateDeviceIdAsync();
            if (createResult != Result.Success && createResult != Result.DuplicateNotAllowed)
            {
                Debug.LogError($"[EOSManager] CreateDeviceId failed: {createResult}");
                OnLoginFailed?.Invoke(createResult);
                return createResult;
            }

            // Login with device token
            LoginCallbackInfo loginResult = await ConnectLoginAsync(displayName);

            if (loginResult.ResultCode == Result.InvalidUser)
            {
                // User doesn't exist, create one
                if (loginResult.ContinuanceToken == null)
                {
                    EOSDebugLogger.LogError("EOSManager", "ContinuanceToken is null, cannot create user.");
                    OnLoginFailed?.Invoke(Result.InvalidUser);
                    return Result.InvalidUser;
                }

                Result createUserResult = await CreateUserAsync(loginResult.ContinuanceToken);
                if (createUserResult != Result.Success)
                {
                    Debug.LogError($"[EOSManager] CreateUser failed: {createUserResult}");
                    OnLoginFailed?.Invoke(createUserResult);
                    return createUserResult;
                }
            }
            else if (loginResult.ResultCode != Result.Success)
            {
                Debug.LogError($"[EOSManager] Login failed: {loginResult.ResultCode}");
                OnLoginFailed?.Invoke(loginResult.ResultCode);
                return loginResult.ResultCode;
            }
            else
            {
                LocalProductUserId = loginResult.LocalUserId;
            }

            // Setup auth expiration notification
            SetupAuthExpirationNotification();
            SetupLoginStatusChangedNotification();

            IsLoggedIn = true;
            EOSDebugLogger.Log(DebugCategory.EOSManager, "EOSManager", $" Logged in successfully. ProductUserId: {LocalProductUserId}");
            OnLoginSuccess?.Invoke(LocalProductUserId);

            return Result.Success;
        }

        private string GetDeviceModel()
        {
            string model = SystemInfo.deviceUniqueIdentifier;

#if UNITY_EDITOR
            // ParrelSync support - make each clone unique
            if (IsParrelSyncClone())
            {
                string clonePath = GetParrelSyncProjectPath();
                if (!string.IsNullOrEmpty(clonePath))
                {
                    model += clonePath;
                }
            }
#endif

            // Truncate to max length if needed
            if (model.Length > 64)
            {
                model = model.Substring(0, 64);
            }

            return model;
        }

        private Task<Result> DeleteDeviceIdAsync()
        {
            var tcs = new TaskCompletionSource<Result>();

            var options = new DeleteDeviceIdOptions();
            ConnectInterface.DeleteDeviceId(ref options, null, (ref DeleteDeviceIdCallbackInfo data) =>
            {
                tcs.SetResult(data.ResultCode);
            });

            return tcs.Task;
        }

        private Task<Result> CreateDeviceIdAsync()
        {
            var tcs = new TaskCompletionSource<Result>();

            var options = new CreateDeviceIdOptions
            {
                DeviceModel = GetDeviceModel()
            };

            ConnectInterface.CreateDeviceId(ref options, null, (ref CreateDeviceIdCallbackInfo data) =>
            {
                tcs.SetResult(data.ResultCode);
            });

            return tcs.Task;
        }

        private Task<LoginCallbackInfo> ConnectLoginAsync(string displayName)
        {
            var tcs = new TaskCompletionSource<LoginCallbackInfo>();

            var options = new Epic.OnlineServices.Connect.LoginOptions
            {
                Credentials = new Epic.OnlineServices.Connect.Credentials
                {
                    Type = ExternalCredentialType.DeviceidAccessToken,
                    Token = null
                },
                UserLoginInfo = new UserLoginInfo
                {
                    DisplayName = displayName
                }
            };

            ConnectInterface.Login(ref options, null, (ref LoginCallbackInfo data) =>
            {
                tcs.SetResult(data);
            });

            return tcs.Task;
        }

        private Task<Result> CreateUserAsync(ContinuanceToken continuanceToken)
        {
            var tcs = new TaskCompletionSource<Result>();

            var options = new CreateUserOptions
            {
                ContinuanceToken = continuanceToken
            };

            ConnectInterface.CreateUser(ref options, null, (ref CreateUserCallbackInfo data) =>
            {
                if (data.ResultCode == Result.Success)
                {
                    LocalProductUserId = data.LocalUserId;
                }
                tcs.SetResult(data.ResultCode);
            });

            return tcs.Task;
        }

        private void SetupAuthExpirationNotification()
        {
            var options = new AddNotifyAuthExpirationOptions();
            _authExpirationHandle = ConnectInterface.AddNotifyAuthExpiration(ref options, null, OnAuthExpirationCallback);
        }

        private void OnAuthExpirationCallback(ref AuthExpirationCallbackInfo data)
        {
            EOSDebugLogger.LogWarning(DebugCategory.EOSManager, "EOSManager", "Auth token is about to expire.");
            OnAuthExpiring?.Invoke();
        }

        private void SetupLoginStatusChangedNotification()
        {
            var options = new AddNotifyLoginStatusChangedOptions();
            _loginStatusChangedHandle = ConnectInterface.AddNotifyLoginStatusChanged(ref options, null, OnLoginStatusChangedCallback);
        }

        private void OnLoginStatusChangedCallback(ref LoginStatusChangedCallbackInfo data)
        {
            EOSDebugLogger.Log(DebugCategory.EOSManager, "EOSManager", $" Login status changed: {data.PreviousStatus} -> {data.CurrentStatus}");

            if (data.CurrentStatus == LoginStatus.NotLoggedIn && IsLoggedIn)
            {
                IsLoggedIn = false;
                LocalProductUserId = null;
                OnLogout?.Invoke();
            }
        }

        #endregion

        #region Epic Account Login

        /// <summary>
        /// Logs in using Epic Account (opens Epic Games launcher overlay).
        /// This enables social features like Friends, Presence, etc.
        /// </summary>
        /// <returns>Result of the login operation.</returns>
        public async Task<Result> LoginWithEpicAccountAsync()
        {
            if (!IsInitialized)
            {
                EOSDebugLogger.LogError("EOSManager", "Cannot login - SDK not initialized.");
                return Result.NotConfigured;
            }

            if (IsEpicAccountLoggedIn)
            {
                EOSDebugLogger.LogWarning(DebugCategory.EOSManager, "EOSManager", "Already logged in with Epic Account.");
                return Result.Success;
            }

            // Auth login with Account Portal (opens Epic overlay)
            var authResult = await AuthLoginAsync();
            if (authResult.ResultCode != Result.Success)
            {
                Debug.LogWarning($"[EOSManager] Epic Account login failed: {authResult.ResultCode}");
                return authResult.ResultCode;
            }

            LocalEpicAccountId = authResult.LocalUserId;
            EOSDebugLogger.Log(DebugCategory.EOSManager, "EOSManager", $" Epic Account logged in: {LocalEpicAccountId}");

            // Now connect to game services with this Epic account
            if (!IsLoggedIn)
            {
                var connectResult = await ConnectLoginWithEpicAsync(authResult.LocalUserId);
                if (connectResult != Result.Success)
                {
                    Debug.LogWarning($"[EOSManager] Connect login failed: {connectResult}");
                    // Auth succeeded but Connect failed - partial success
                }
            }

            return Result.Success;
        }

        /// <summary>
        /// Logs in using persistent auth (previously logged in Epic Account).
        /// Silent login - no overlay shown.
        /// </summary>
        public async Task<Result> LoginWithPersistentAuthAsync()
        {
            if (!IsInitialized)
                return Result.NotConfigured;

            var result = await AuthLoginPersistentAsync();
            if (result.ResultCode != Result.Success)
            {
                EOSDebugLogger.Log(DebugCategory.EOSManager, "EOSManager", $" Persistent auth not available: {result.ResultCode}");
                return result.ResultCode;
            }

            LocalEpicAccountId = result.LocalUserId;
            EOSDebugLogger.Log(DebugCategory.EOSManager, "EOSManager", $" Persistent auth succeeded: {LocalEpicAccountId}");

            // Connect to game services
            if (!IsLoggedIn)
            {
                await ConnectLoginWithEpicAsync(result.LocalUserId);
            }

            return Result.Success;
        }

        /// <summary>
        /// Try persistent auth first, fall back to device token.
        /// </summary>
        public async Task<Result> LoginSmartAsync(string displayName = "Player")
        {
            // Try persistent Epic auth first (silent)
            var persistentResult = await LoginWithPersistentAuthAsync();
            if (persistentResult == Result.Success)
            {
                return Result.Success;
            }

            // Fall back to device token
            EOSDebugLogger.Log(DebugCategory.EOSManager, "EOSManager", "No persistent auth, using device token...");
            return await LoginWithDeviceTokenAsync(displayName);
        }

        private Task<Epic.OnlineServices.Auth.LoginCallbackInfo> AuthLoginAsync()
        {
            var tcs = new TaskCompletionSource<Epic.OnlineServices.Auth.LoginCallbackInfo>();

            var options = new Epic.OnlineServices.Auth.LoginOptions
            {
                Credentials = new Epic.OnlineServices.Auth.Credentials
                {
                    Type = Epic.OnlineServices.Auth.LoginCredentialType.AccountPortal
                },
                ScopeFlags = Epic.OnlineServices.Auth.AuthScopeFlags.BasicProfile |
                             Epic.OnlineServices.Auth.AuthScopeFlags.FriendsList |
                             Epic.OnlineServices.Auth.AuthScopeFlags.Presence
            };

            AuthInterface.Login(ref options, null, (ref Epic.OnlineServices.Auth.LoginCallbackInfo data) =>
            {
                tcs.SetResult(data);
            });

            return tcs.Task;
        }

        private Task<Epic.OnlineServices.Auth.LoginCallbackInfo> AuthLoginPersistentAsync()
        {
            var tcs = new TaskCompletionSource<Epic.OnlineServices.Auth.LoginCallbackInfo>();

            var options = new Epic.OnlineServices.Auth.LoginOptions
            {
                Credentials = new Epic.OnlineServices.Auth.Credentials
                {
                    Type = Epic.OnlineServices.Auth.LoginCredentialType.PersistentAuth
                },
                ScopeFlags = Epic.OnlineServices.Auth.AuthScopeFlags.BasicProfile |
                             Epic.OnlineServices.Auth.AuthScopeFlags.FriendsList |
                             Epic.OnlineServices.Auth.AuthScopeFlags.Presence
            };

            AuthInterface.Login(ref options, null, (ref Epic.OnlineServices.Auth.LoginCallbackInfo data) =>
            {
                tcs.SetResult(data);
            });

            return tcs.Task;
        }

        private async Task<Result> ConnectLoginWithEpicAsync(Epic.OnlineServices.EpicAccountId epicAccountId)
        {
            // Get auth token from Auth interface
            var copyOptions = new Epic.OnlineServices.Auth.CopyUserAuthTokenOptions();
            var copyResult = AuthInterface.CopyUserAuthToken(ref copyOptions, epicAccountId, out var authToken);
            if (copyResult != Result.Success || !authToken.HasValue)
            {
                Debug.LogWarning($"[EOSManager] Failed to get auth token: {copyResult}");
                return copyResult;
            }

            // Login to Connect with Epic token
            var tcs = new TaskCompletionSource<LoginCallbackInfo>();
            var loginOptions = new Epic.OnlineServices.Connect.LoginOptions
            {
                Credentials = new Epic.OnlineServices.Connect.Credentials
                {
                    Type = ExternalCredentialType.Epic,
                    Token = authToken.Value.AccessToken
                }
            };

            ConnectInterface.Login(ref loginOptions, null, (ref LoginCallbackInfo data) =>
            {
                tcs.SetResult(data);
            });

            var loginResult = await tcs.Task;

            if (loginResult.ResultCode == Result.InvalidUser)
            {
                // Create user if needed
                if (loginResult.ContinuanceToken != null)
                {
                    var createResult = await CreateUserAsync(loginResult.ContinuanceToken);
                    if (createResult != Result.Success)
                        return createResult;
                }
            }
            else if (loginResult.ResultCode != Result.Success)
            {
                return loginResult.ResultCode;
            }
            else
            {
                LocalProductUserId = loginResult.LocalUserId;
            }

            IsLoggedIn = true;
            SetupAuthExpirationNotification();
            SetupLoginStatusChangedNotification();

            EOSDebugLogger.Log(DebugCategory.EOSManager, "EOSManager", $" Connect login with Epic succeeded: {LocalProductUserId}");
            OnLoginSuccess?.Invoke(LocalProductUserId);
            return Result.Success;
        }

        /// <summary>
        /// Logout from Epic Account (keeps device token if active).
        /// </summary>
        public async Task LogoutEpicAccountAsync()
        {
            if (!IsEpicAccountLoggedIn)
                return;

            var tcs = new TaskCompletionSource<Epic.OnlineServices.Auth.LogoutCallbackInfo>();
            var options = new Epic.OnlineServices.Auth.LogoutOptions
            {
                LocalUserId = LocalEpicAccountId
            };

            AuthInterface.Logout(ref options, null, (ref Epic.OnlineServices.Auth.LogoutCallbackInfo data) =>
            {
                tcs.SetResult(data);
            });

            await tcs.Task;
            LocalEpicAccountId = null;
            EOSDebugLogger.Log(DebugCategory.EOSManager, "EOSManager", "Logged out from Epic Account");
        }

        #endregion

        #region ParrelSync Support

        private bool IsParrelSyncClone()
        {
#if UNITY_EDITOR
            try
            {
                Type clonesManagerType = Type.GetType("ParrelSync.ClonesManager, ParrelSync");
                if (clonesManagerType == null)
                    return false;

                MethodInfo isCloneMethod = clonesManagerType.GetMethod("IsClone",
                    BindingFlags.Public | BindingFlags.Static);
                if (isCloneMethod == null)
                    return false;

                return (bool)isCloneMethod.Invoke(null, null);
            }
            catch
            {
                return false;
            }
#else
            return false;
#endif
        }

        private string GetParrelSyncProjectPath()
        {
#if UNITY_EDITOR
            try
            {
                Type clonesManagerType = Type.GetType("ParrelSync.ClonesManager, ParrelSync");
                if (clonesManagerType == null)
                    return null;

                MethodInfo getPathMethod = clonesManagerType.GetMethod("GetCurrentProjectPath",
                    BindingFlags.Public | BindingFlags.Static);
                if (getPathMethod == null)
                    return null;

                return (string)getPathMethod.Invoke(null, null);
            }
            catch
            {
                return null;
            }
#else
            return null;
#endif
        }

        #endregion

        #region Native Library Loading

        private void LoadNativeLibrary()
        {
#if UNITY_EDITOR
            string libraryName = Common.LIBRARY_NAME;

#if UNITY_EDITOR_OSX
            // Remove .dylib extension for Unity asset search
            if (libraryName.EndsWith(".dylib"))
            {
                libraryName = libraryName.Substring(0, libraryName.Length - 6);
            }
#endif

            string[] libs = UnityEditor.AssetDatabase.FindAssets(libraryName);
            if (libs.Length == 0)
            {
                throw new System.IO.FileNotFoundException(
                    $"EOS SDK library '{Common.LIBRARY_NAME}' not found in project.",
                    Common.LIBRARY_NAME);
            }

            string libraryPath = System.IO.Path.GetFullPath(UnityEditor.AssetDatabase.GUIDToAssetPath(libs[0]));
            EOSDebugLogger.Log(DebugCategory.EOSManager, "EOSManager", $"Loading EOS SDK from: {libraryPath}");

#if UNITY_EDITOR_WIN
            _libraryPointer = LoadLibrary(libraryPath);
            if (_libraryPointer == IntPtr.Zero)
            {
                throw new Exception($"Failed to load EOS SDK library: {libraryPath}");
            }
            Bindings.Hook(_libraryPointer, GetProcAddress);
            WindowsBindings.Hook(_libraryPointer, GetProcAddress);
#elif UNITY_EDITOR_OSX
            _libraryPointer = LoadLibraryOSX(libraryPath);
            Bindings.Hook(_libraryPointer, GetProcAddressOSX);
#elif UNITY_EDITOR_LINUX
            _libraryPointer = LoadLibraryLinux(libraryPath);
            Bindings.Hook(_libraryPointer, GetProcAddressLinux);
#endif

            EOSDebugLogger.Log(DebugCategory.EOSManager, "EOSManager", "EOS SDK library loaded successfully.");
#endif
        }

        private void LoadAndroidLibrary()
        {
#if UNITY_ANDROID && !UNITY_EDITOR
            try
            {
                using (AndroidJavaClass sys = new AndroidJavaClass("java.lang.System"))
                {
                    sys.CallStatic("loadLibrary", "EOSSDK");
                }

#if UNITY_6000_0_OR_NEWER
                using (AndroidJavaClass eos = new AndroidJavaClass("com.epicgames.mobile.eossdk.EOSSDK"))
                {
                    eos.CallStatic("init", UnityEngine.Android.AndroidApplication.currentActivity);
                }
#else
                AndroidJavaClass player = new AndroidJavaClass("com.unity3d.player.UnityPlayer");
                AndroidJavaObject activity = player.GetStatic<AndroidJavaObject>("currentActivity");
                using (AndroidJavaClass eos = new AndroidJavaClass("com.epicgames.mobile.eossdk.EOSSDK"))
                {
                    eos.CallStatic("init", activity);
                }
#endif

                EOSDebugLogger.Log(DebugCategory.EOSManager, "EOSManager", "Android EOS SDK loaded successfully.");
            }
            catch (Exception e)
            {
                Debug.LogError($"[EOSManager] Failed to load Android EOS SDK: {e}");
                throw;
            }
#endif
        }

        private void UnloadNativeLibrary()
        {
#if UNITY_EDITOR
#if UNITY_EDITOR_WIN
            if (_libraryPointer != IntPtr.Zero)
            {
                Bindings.Unhook();
                WindowsBindings.Unhook();

                // Call FreeLibrary once - don't loop as it can hang if SDK isn't fully shut down
                FreeLibrary(_libraryPointer);
                _libraryPointer = IntPtr.Zero;
            }
#elif UNITY_EDITOR_OSX
            if (_libraryPointer != IntPtr.Zero)
            {
                Bindings.Unhook();
                FreeLibraryOSX(_libraryPointer);
                _libraryPointer = IntPtr.Zero;
            }
#elif UNITY_EDITOR_LINUX
            if (_libraryPointer != IntPtr.Zero)
            {
                Bindings.Unhook();
                FreeLibraryLinux(_libraryPointer);
                _libraryPointer = IntPtr.Zero;
            }
#endif
            EOSDebugLogger.Log(DebugCategory.EOSManager, "EOSManager", "EOS SDK library unloaded.");
#endif
        }

        #endregion

        #region Shutdown

        /// <summary>
        /// Shuts down the EOS SDK and releases all resources.
        /// </summary>
        public void Shutdown()
        {
            if (_platform != null)
            {
                // Remove notifications
                if (_authExpirationHandle != 0)
                {
                    ConnectInterface?.RemoveNotifyAuthExpiration(_authExpirationHandle);
                    _authExpirationHandle = 0;
                }

                if (_loginStatusChangedHandle != 0)
                {
                    ConnectInterface?.RemoveNotifyLoginStatusChanged(_loginStatusChangedHandle);
                    _loginStatusChangedHandle = 0;
                }

                // Release platform
                _platform.Release();
                PlatformInterface.Shutdown();
                _platform = null;

                EOSDebugLogger.Log(DebugCategory.EOSManager, "EOSManager", "EOS SDK shut down.");
            }

            UnloadNativeLibrary();

            IsInitialized = false;
            IsLoggedIn = false;
            LocalProductUserId = null;
        }

        /// <summary>
        /// Logs out the current user.
        /// </summary>
        public async Task LogoutAsync()
        {
            if (!IsLoggedIn || LocalProductUserId == null)
            {
                return;
            }

            var tcs = new TaskCompletionSource<Result>();

            var options = new LogoutOptions
            {
                LocalUserId = LocalProductUserId
            };

            ConnectInterface.Logout(ref options, null, (ref LogoutCallbackInfo data) =>
            {
                tcs.SetResult(data.ResultCode);
            });

            Result result = await tcs.Task;

            if (result == Result.Success)
            {
                IsLoggedIn = false;
                LocalProductUserId = null;
                EOSDebugLogger.Log(DebugCategory.EOSManager, "EOSManager", "Logged out successfully.");
                OnLogout?.Invoke();
            }
            else
            {
                Debug.LogError($"[EOSManager] Logout failed: {result}");
            }
        }

        #endregion

        #region Application and Network Status

        /// <summary>
        /// Sets the application status. Call this when your app is suspended/resumed.
        /// The SDK automatically handles this via OnApplicationPause, but you can call manually if needed.
        /// </summary>
        /// <param name="status">The new application status.</param>
        /// <returns>The result of the operation.</returns>
        public Result SetApplicationStatus(ApplicationStatus status)
        {
            if (_platform == null)
            {
                EOSDebugLogger.LogWarning(DebugCategory.EOSManager, "EOSManager", "Cannot set application status - platform not initialized.");
                return Result.NotConfigured;
            }

            Result result = _platform.SetApplicationStatus(status);
            if (result == Result.Success)
            {
                EOSDebugLogger.Log(DebugCategory.EOSManager, "EOSManager", $" Application status set to: {status}");
            }
            else
            {
                Debug.LogWarning($"[EOSManager] Failed to set application status: {result}");
            }

            return result;
        }

        /// <summary>
        /// Gets the current application status.
        /// </summary>
        /// <returns>The current application status.</returns>
        public ApplicationStatus GetApplicationStatus()
        {
            if (_platform == null)
            {
                return ApplicationStatus.Foreground;
            }

            return _platform.GetApplicationStatus();
        }

        /// <summary>
        /// Sets the network status. You MUST call this when network availability changes.
        /// On consoles (PS4, PS5, Switch, Xbox), the default is Disabled - you must set to Online when network is available.
        /// </summary>
        /// <param name="status">The new network status.</param>
        /// <returns>The result of the operation.</returns>
        public Result SetNetworkStatus(NetworkStatus status)
        {
            if (_platform == null)
            {
                EOSDebugLogger.LogWarning(DebugCategory.EOSManager, "EOSManager", "Cannot set network status - platform not initialized.");
                return Result.NotConfigured;
            }

            Result result = _platform.SetNetworkStatus(status);
            if (result == Result.Success)
            {
                EOSDebugLogger.Log(DebugCategory.EOSManager, "EOSManager", $" Network status set to: {status}");
            }
            else
            {
                Debug.LogWarning($"[EOSManager] Failed to set network status: {result}");
            }

            return result;
        }

        /// <summary>
        /// Gets the current network status.
        /// </summary>
        /// <returns>The current network status.</returns>
        public NetworkStatus GetNetworkStatus()
        {
            if (_platform == null)
            {
                return NetworkStatus.Disabled;
            }

            return _platform.GetNetworkStatus();
        }

        /// <summary>
        /// Convenience method to set the network status to Online.
        /// Call this after initialization when network is available.
        /// </summary>
        public void SetNetworkOnline()
        {
            SetNetworkStatus(NetworkStatus.Online);
        }

        /// <summary>
        /// Convenience method to set the network status to Offline.
        /// </summary>
        public void SetNetworkOffline()
        {
            SetNetworkStatus(NetworkStatus.Offline);
        }

        /// <summary>
        /// Convenience method to set the network status to Disabled.
        /// </summary>
        public void SetNetworkDisabled()
        {
            SetNetworkStatus(NetworkStatus.Disabled);
        }

        #endregion
    }

#if UNITY_EDITOR
    [CustomEditor(typeof(EOSManager))]
    public class EOSManagerEditor : UnityEditor.Editor
    {
        // Styles
        private GUIStyle _headerStyle;
        private GUIStyle _statusBoxStyle;
        private GUIStyle _puidStyle;
        private GUIStyle _codeStyle;

        // Foldout states
        private bool _showInterfaces = false;
        private bool _showActions = true;
        private bool _showLobby = true;
        private bool _showSettings = true;

        // Action state
        private string _actionStatus = "";
        private bool _actionInProgress;

        private void OnEnable()
        {
            // Auto-assign config if not set
            var configProp = serializedObject.FindProperty("_config");
            if (configProp.objectReferenceValue == null)
            {
                EOSConfig config = Resources.Load<EOSConfig>("SampleEOSConfig");
                if (config == null)
                    config = Resources.Load<EOSConfig>("EOSConfig");
                if (config == null)
                {
                    var guids = AssetDatabase.FindAssets("t:EOSConfig");
                    if (guids.Length > 0)
                        config = AssetDatabase.LoadAssetAtPath<EOSConfig>(AssetDatabase.GUIDToAssetPath(guids[0]));
                }

                if (config != null)
                {
                    configProp.objectReferenceValue = config;
                    serializedObject.ApplyModifiedPropertiesWithoutUndo();
                }
            }
        }

        // Lobby state
        private string _joinCode = "";
        private string _lobbyStatus = "";
        private bool _lobbyOperationInProgress;

        private void InitializeStyles()
        {
            if (_headerStyle == null)
            {
                _headerStyle = new GUIStyle(EditorStyles.boldLabel)
                {
                    fontSize = 12,
                    margin = new RectOffset(0, 0, 5, 5)
                };
            }

            if (_statusBoxStyle == null)
            {
                _statusBoxStyle = new GUIStyle(EditorStyles.helpBox)
                {
                    padding = new RectOffset(10, 10, 10, 10),
                    margin = new RectOffset(0, 0, 5, 5)
                };
            }

            if (_puidStyle == null)
            {
                _puidStyle = new GUIStyle(EditorStyles.textField)
                {
                    fontSize = 10,
                    fontStyle = FontStyle.Bold,
                    alignment = TextAnchor.MiddleLeft
                };
            }

            if (_codeStyle == null)
            {
                _codeStyle = new GUIStyle(EditorStyles.textField)
                {
                    fontSize = 16,
                    fontStyle = FontStyle.Bold,
                    alignment = TextAnchor.MiddleCenter
                };
            }
        }

        private void DrawStatusIndicator(bool isActive, string activeText, string inactiveText)
        {
            Color originalColor = GUI.backgroundColor;
            GUI.backgroundColor = isActive ? new Color(0.3f, 0.8f, 0.3f) : new Color(0.8f, 0.3f, 0.3f);
            GUILayout.Label(isActive ? activeText : inactiveText, EditorStyles.miniButton);
            GUI.backgroundColor = originalColor;
        }

        private void DrawStatusIndicatorYellow(string text)
        {
            Color originalColor = GUI.backgroundColor;
            GUI.backgroundColor = new Color(0.8f, 0.8f, 0.3f);
            GUILayout.Label(text, EditorStyles.miniButton);
            GUI.backgroundColor = originalColor;
        }

        public override void OnInspectorGUI()
        {
            InitializeStyles();
            serializedObject.Update();

            var manager = (EOSManager)target;

            // Runtime sections (play mode)
            if (Application.isPlaying)
            {
                DrawRuntimeStatus(manager);
                EditorGUILayout.Space(5);
                DrawInterfaces(manager);
                EditorGUILayout.Space(5);
                DrawQuickActions(manager);
                EditorGUILayout.Space(5);
                DrawLobbyControls(manager);
                EditorGUILayout.Space(5);
            }

            // Settings (always visible, editable outside play mode)
            DrawSettings();

            if (!Application.isPlaying)
            {
                EditorGUILayout.Space(5);
                EditorGUILayout.HelpBox(
                    "Press Play to auto-initialize and login.\n" +
                    "F1 toggles the in-game status overlay.",
                    MessageType.Info);
            }

            serializedObject.ApplyModifiedProperties();

            if (Application.isPlaying)
            {
                Repaint();
            }
        }

        private void DrawRuntimeStatus(EOSManager manager)
        {
            EditorGUILayout.BeginVertical(_statusBoxStyle);
            EditorGUILayout.LabelField("Runtime Status", _headerStyle);

            // EOS SDK
            EditorGUILayout.BeginHorizontal();
            EditorGUILayout.LabelField("EOS SDK:", GUILayout.Width(100));
            DrawStatusIndicator(manager.IsInitialized, "Initialized", "Not Initialized");
            EditorGUILayout.EndHorizontal();

            // Connect Login
            EditorGUILayout.BeginHorizontal();
            EditorGUILayout.LabelField("Connect Login:", GUILayout.Width(100));
            DrawStatusIndicator(manager.IsLoggedIn, "Logged In", "Not Logged In");
            EditorGUILayout.EndHorizontal();

            // Epic Account
            EditorGUILayout.BeginHorizontal();
            EditorGUILayout.LabelField("Epic Account:", GUILayout.Width(100));
            DrawStatusIndicator(manager.IsEpicAccountLoggedIn, "Linked", "Not Linked");
            EditorGUILayout.EndHorizontal();

            // PUID
            if (manager.IsLoggedIn && manager.LocalProductUserId != null)
            {
                EditorGUILayout.Space(5);
                EditorGUILayout.LabelField("Local ProductUserId:", EditorStyles.miniLabel);
                EditorGUILayout.BeginHorizontal();
                string puid = manager.LocalProductUserId.ToString();
                EditorGUILayout.SelectableLabel(puid, _puidStyle, GUILayout.Height(18));
                if (GUILayout.Button("Copy", GUILayout.Width(50)))
                {
                    EditorGUIUtility.systemCopyBuffer = puid;
                }
                EditorGUILayout.EndHorizontal();
            }

            // Epic Account ID
            if (manager.IsEpicAccountLoggedIn && manager.LocalEpicAccountId != null)
            {
                EditorGUILayout.LabelField("Epic Account ID:", EditorStyles.miniLabel);
                EditorGUILayout.BeginHorizontal();
                string eaid = manager.LocalEpicAccountId.ToString();
                EditorGUILayout.SelectableLabel(eaid, _puidStyle, GUILayout.Height(18));
                if (GUILayout.Button("Copy", GUILayout.Width(50)))
                {
                    EditorGUIUtility.systemCopyBuffer = eaid;
                }
                EditorGUILayout.EndHorizontal();
            }

            // Network & App Status
            if (manager.IsInitialized)
            {
                EditorGUILayout.Space(5);
                EditorGUILayout.BeginHorizontal();
                EditorGUILayout.LabelField("Network:", GUILayout.Width(100));
                var netStatus = manager.GetNetworkStatus();
                if (netStatus == NetworkStatus.Online)
                    DrawStatusIndicator(true, "Online", "");
                else if (netStatus == NetworkStatus.Offline)
                    DrawStatusIndicatorYellow("Offline");
                else
                    DrawStatusIndicator(false, "", "Disabled");
                EditorGUILayout.EndHorizontal();

                EditorGUILayout.BeginHorizontal();
                EditorGUILayout.LabelField("App Status:", GUILayout.Width(100));
                var appStatus = manager.GetApplicationStatus();
                DrawStatusIndicator(appStatus == ApplicationStatus.Foreground, appStatus.ToString(), appStatus.ToString());
                EditorGUILayout.EndHorizontal();
            }

            EditorGUILayout.EndVertical();
        }

        private void DrawInterfaces(EOSManager manager)
        {
            _showInterfaces = EditorGUILayout.Foldout(_showInterfaces, "Available Interfaces", true, EditorStyles.foldoutHeader);
            if (!_showInterfaces || !manager.IsInitialized) return;

            EditorGUILayout.BeginVertical(_statusBoxStyle);

            DrawInterfaceRow(manager, ("Connect", manager.ConnectInterface != null), ("P2P", manager.P2PInterface != null), ("Lobby", manager.LobbyInterface != null));
            DrawInterfaceRow(manager, ("RTC", manager.RTCInterface != null), ("RTC Audio", manager.RTCAudioInterface != null), ("Auth", manager.AuthInterface != null));
            DrawInterfaceRow(manager, ("Friends", manager.FriendsInterface != null), ("Presence", manager.PresenceInterface != null), ("UserInfo", manager.UserInfoInterface != null));
            DrawInterfaceRow(manager, ("Stats", manager.StatsInterface != null), ("Leaderboards", manager.LeaderboardsInterface != null), ("Achievements", manager.AchievementsInterface != null));
            DrawInterfaceRow(manager, ("PlayerData", manager.PlayerDataStorageInterface != null), ("TitleStorage", manager.TitleStorageInterface != null), ("Reports", manager.ReportsInterface != null));
            DrawInterfaceRow(manager, ("Metrics", manager.MetricsInterface != null), ("Invites", manager.CustomInvitesInterface != null));

            EditorGUILayout.EndVertical();
        }

        private void DrawInterfaceRow(EOSManager manager, params (string name, bool available)[] interfaces)
        {
            EditorGUILayout.BeginHorizontal();
            foreach (var (name, available) in interfaces)
            {
                Color originalColor = GUI.backgroundColor;
                GUI.backgroundColor = available ? new Color(0.3f, 0.7f, 0.3f) : new Color(0.4f, 0.4f, 0.4f);
                GUILayout.Label(available ? $"{name} \u2713" : name, EditorStyles.miniButton);
                GUI.backgroundColor = originalColor;
            }
            EditorGUILayout.EndHorizontal();
        }

        private void DrawQuickActions(EOSManager manager)
        {
            _showActions = EditorGUILayout.Foldout(_showActions, "Quick Actions", true, EditorStyles.foldoutHeader);
            if (!_showActions) return;

            EditorGUILayout.BeginVertical(_statusBoxStyle);

            // Init button
            if (!manager.IsInitialized)
            {
                GUI.enabled = !_actionInProgress;
                GUI.backgroundColor = new Color(0.5f, 0.9f, 0.5f);
                if (GUILayout.Button("Initialize EOS SDK", GUILayout.Height(30)))
                {
                    QuickInit(manager);
                }
                GUI.backgroundColor = Color.white;
                GUI.enabled = true;
            }

            // Login buttons
            if (manager.IsInitialized && !manager.IsLoggedIn)
            {
                EditorGUILayout.BeginHorizontal();
                GUI.enabled = !_actionInProgress;

                GUI.backgroundColor = new Color(0.5f, 0.7f, 0.9f);
                if (GUILayout.Button("Device Login", GUILayout.Height(28)))
                {
                    QuickLogin(manager);
                }

                GUI.backgroundColor = new Color(0.9f, 0.7f, 0.3f);
                if (GUILayout.Button("Smart Login", GUILayout.Height(28)))
                {
                    SmartLogin(manager);
                }

                GUI.backgroundColor = Color.white;
                GUI.enabled = true;
                EditorGUILayout.EndHorizontal();
            }

            // Epic Account link
            if (manager.IsLoggedIn && !manager.IsEpicAccountLoggedIn)
            {
                GUI.enabled = !_actionInProgress;
                GUI.backgroundColor = new Color(0.7f, 0.5f, 0.9f);
                if (GUILayout.Button("Link Epic Account", GUILayout.Height(26)))
                {
                    LinkEpicAccount(manager);
                }
                GUI.backgroundColor = Color.white;
                GUI.enabled = true;
            }

            // Logout
            if (manager.IsLoggedIn)
            {
                EditorGUILayout.Space(5);
                GUI.enabled = !_actionInProgress;
                GUI.backgroundColor = new Color(0.9f, 0.5f, 0.5f);
                if (GUILayout.Button("Logout", GUILayout.Height(24)))
                {
                    Logout(manager);
                }
                GUI.backgroundColor = Color.white;
                GUI.enabled = true;
            }

            // All good indicator
            if (manager.IsLoggedIn && manager.IsInitialized)
            {
                EditorGUILayout.Space(3);
                Color c = GUI.backgroundColor;
                GUI.backgroundColor = new Color(0.3f, 0.8f, 0.3f);
                EditorGUILayout.LabelField("Ready - use Lobby section below or F1 overlay in-game", EditorStyles.miniButton);
                GUI.backgroundColor = c;
            }

            // Status message
            if (!string.IsNullOrEmpty(_actionStatus))
            {
                EditorGUILayout.Space(3);
                EditorGUILayout.LabelField(_actionStatus, EditorStyles.miniLabel);
            }

            EditorGUILayout.EndVertical();
        }

        private void DrawLobbyControls(EOSManager manager)
        {
            _showLobby = EditorGUILayout.Foldout(_showLobby, "Lobby", true, EditorStyles.foldoutHeader);
            if (!_showLobby) return;

            EditorGUILayout.BeginVertical(_statusBoxStyle);

            if (!manager.IsLoggedIn)
            {
                EditorGUILayout.HelpBox("Login required for lobby operations.", MessageType.Info);
                EditorGUILayout.EndVertical();
                return;
            }

            var lobbyMgr = EOSLobbyManager.Instance;
            if (lobbyMgr == null)
            {
                EditorGUILayout.HelpBox("EOSLobbyManager not found.", MessageType.Warning);
                EditorGUILayout.EndVertical();
                return;
            }

            if (lobbyMgr.IsInLobby)
            {
                // In lobby - show status
                var lobby = lobbyMgr.CurrentLobby;

                EditorGUILayout.BeginHorizontal();
                EditorGUILayout.LabelField("Room:", GUILayout.Width(45));

                Color origBg = GUI.backgroundColor;
                GUI.backgroundColor = new Color(0.3f, 0.8f, 0.3f);
                EditorGUILayout.LabelField(lobby.JoinCode ?? "????", _codeStyle, GUILayout.Width(60), GUILayout.Height(24));
                GUI.backgroundColor = origBg;

                if (GUILayout.Button("Copy", GUILayout.Width(50)))
                {
                    EditorGUIUtility.systemCopyBuffer = lobby.JoinCode ?? "";
                }
                EditorGUILayout.EndHorizontal();

                string role = lobbyMgr.IsOwner ? "HOST" : "CLIENT";
                EditorGUILayout.LabelField($"{role} | {lobby.MemberCount}/{lobby.MaxMembers} players", EditorStyles.miniLabel);

                if (!string.IsNullOrEmpty(lobby.LobbyName))
                {
                    EditorGUILayout.LabelField(lobby.LobbyName, EditorStyles.miniLabel);
                }

                EditorGUILayout.Space(5);

                // Leave button
                GUI.enabled = !_lobbyOperationInProgress;
                GUI.backgroundColor = new Color(0.9f, 0.5f, 0.5f);
                if (GUILayout.Button("Leave Lobby", GUILayout.Height(28)))
                {
                    LeaveLobby(lobbyMgr);
                }
                GUI.backgroundColor = Color.white;
                GUI.enabled = true;
            }
            else
            {
                // Not in lobby - show Host/Join controls
                EditorGUILayout.BeginHorizontal();
                EditorGUILayout.LabelField("Code:", GUILayout.Width(40));
                _joinCode = EditorGUILayout.TextField(_joinCode, _codeStyle, GUILayout.Width(70), GUILayout.Height(24));
                if (_joinCode.Length > 4)
                    _joinCode = _joinCode.Substring(0, 4);
                EditorGUILayout.LabelField("(blank = new)", EditorStyles.miniLabel);
                EditorGUILayout.EndHorizontal();

                EditorGUILayout.Space(5);

                EditorGUILayout.BeginHorizontal();
                GUI.enabled = !_lobbyOperationInProgress;

                // HOST
                GUI.backgroundColor = new Color(0.5f, 0.9f, 0.5f);
                if (GUILayout.Button("Host", GUILayout.Height(32)))
                {
                    HostLobby(lobbyMgr);
                }

                // JOIN
                GUI.enabled = !_lobbyOperationInProgress && _joinCode.Length == 4;
                GUI.backgroundColor = new Color(0.5f, 0.7f, 0.9f);
                if (GUILayout.Button("Join", GUILayout.Height(32)))
                {
                    JoinLobby(lobbyMgr);
                }

                GUI.backgroundColor = Color.white;
                GUI.enabled = true;
                EditorGUILayout.EndHorizontal();

                EditorGUILayout.Space(3);

                // Quick Match
                GUI.enabled = !_lobbyOperationInProgress;
                GUI.backgroundColor = new Color(1f, 0.8f, 0.3f);
                if (GUILayout.Button("Quick Match", GUILayout.Height(28)))
                {
                    QuickMatch(lobbyMgr);
                }
                GUI.backgroundColor = Color.white;
                GUI.enabled = true;
            }

            // Lobby status message
            if (!string.IsNullOrEmpty(_lobbyStatus))
            {
                EditorGUILayout.Space(3);
                EditorGUILayout.LabelField(_lobbyStatus, EditorStyles.miniLabel);
            }

            EditorGUILayout.EndVertical();
        }

        private void DrawSettings()
        {
            _showSettings = EditorGUILayout.Foldout(_showSettings, "Settings", true, EditorStyles.foldoutHeader);
            if (!_showSettings) return;

            EditorGUILayout.BeginVertical(_statusBoxStyle);

            // Config field
            EditorGUILayout.PropertyField(serializedObject.FindProperty("_config"), new GUIContent("EOS Config", "Credentials config. Auto-loads 'SampleEOSConfig' from Resources if empty."));

            // Show config validation
            var manager = (EOSManager)target;
            var config = manager.Config;
            if (config == null)
            {
                config = Resources.Load<EOSConfig>("SampleEOSConfig");
            }

            if (config != null)
            {
                bool valid = config.Validate(out string error);
                EditorGUILayout.BeginHorizontal();
                EditorGUILayout.LabelField("Config Status:", GUILayout.Width(100));
                DrawStatusIndicator(valid, "Valid", "Incomplete");
                EditorGUILayout.EndHorizontal();

                if (!string.IsNullOrEmpty(config.ProductName))
                {
                    EditorGUILayout.LabelField($"Product: {config.ProductName}", EditorStyles.miniLabel);
                }
            }

            EditorGUILayout.Space(5);

            // Auto bootstrap settings
            EditorGUILayout.PropertyField(serializedObject.FindProperty("_autoInitialize"), new GUIContent("Auto Initialize", "Initialize EOS SDK automatically on Start"));
            EditorGUILayout.PropertyField(serializedObject.FindProperty("_autoLogin"), new GUIContent("Auto Login", "Login with device token automatically after init"));
            EditorGUILayout.PropertyField(serializedObject.FindProperty("_displayName"), new GUIContent("Display Name", "Name for device token login (ParrelSync safe)"));

            EditorGUILayout.Space(5);

            EditorGUILayout.PropertyField(serializedObject.FindProperty("_showStatusOverlay"), new GUIContent("F1 Status Overlay", "Auto-add the in-game status overlay (toggle with F1)"));

            EditorGUILayout.Space(5);

            // Utility buttons
            EditorGUILayout.BeginHorizontal();
            if (GUILayout.Button("Setup Wizard"))
            {
                EditorApplication.ExecuteMenuItem("Tools/EOS Native/Setup Wizard");
            }
            if (config != null && GUILayout.Button("Select Config"))
            {
                Selection.activeObject = config;
                EditorGUIUtility.PingObject(config);
            }
            EditorGUILayout.EndHorizontal();

            EditorGUILayout.EndVertical();
        }

        #region Actions

        private void QuickInit(EOSManager manager)
        {
            var config = manager.Config ?? Resources.Load<EOSConfig>("SampleEOSConfig");
            if (config == null)
            {
                var guids = AssetDatabase.FindAssets("t:EOSConfig");
                if (guids.Length > 0)
                    config = AssetDatabase.LoadAssetAtPath<EOSConfig>(AssetDatabase.GUIDToAssetPath(guids[0]));
            }

            if (config == null)
            {
                _actionStatus = "No EOSConfig found!";
                return;
            }

            _actionInProgress = true;
            _actionStatus = "Initializing...";

            var result = manager.Initialize(config);
            _actionStatus = $"Init: {result}";
            _actionInProgress = false;
        }

        private async void QuickLogin(EOSManager manager)
        {
            var config = manager.Config ?? Resources.Load<EOSConfig>("SampleEOSConfig");
            string displayName = config != null && !string.IsNullOrEmpty(config.DefaultDisplayName)
                ? config.DefaultDisplayName : "Player";

            _actionInProgress = true;
            _actionStatus = $"Logging in as '{displayName}'...";
            Repaint();

            var result = await manager.LoginWithDeviceTokenAsync(displayName);
            _actionStatus = $"Login: {result}";
            _actionInProgress = false;
            Repaint();
        }

        private async void SmartLogin(EOSManager manager)
        {
            _actionInProgress = true;
            _actionStatus = "Smart login...";
            Repaint();

            var result = await manager.LoginSmartAsync();
            _actionStatus = $"Smart Login: {result}";
            _actionInProgress = false;
            Repaint();
        }

        private async void LinkEpicAccount(EOSManager manager)
        {
            _actionInProgress = true;
            _actionStatus = "Opening Epic Account login...";
            Repaint();

            var result = await manager.LoginWithEpicAccountAsync();
            _actionStatus = $"Epic Account: {result}";
            _actionInProgress = false;
            Repaint();
        }

        private async void Logout(EOSManager manager)
        {
            _actionInProgress = true;
            _actionStatus = "Logging out...";
            Repaint();

            if (manager.IsEpicAccountLoggedIn)
                await manager.LogoutEpicAccountAsync();
            await manager.LogoutAsync();

            _actionStatus = "Logged out.";
            _actionInProgress = false;
            Repaint();
        }

        #endregion

        #region Lobby Actions

        private async void HostLobby(EOSLobbyManager lobbyMgr)
        {
            _lobbyOperationInProgress = true;
            _lobbyStatus = "Creating lobby...";
            Repaint();

            string code = string.IsNullOrEmpty(_joinCode) ? null : _joinCode;
            var options = new LobbyCreateOptions
            {
                MaxPlayers = 4,
                IsPublic = true,
                EnableVoice = true
            };

            var (result, lobby) = await lobbyMgr.CreateLobbyAsync(options);
            if (result == Result.Success)
            {
                _lobbyStatus = $"Hosting: {lobby.JoinCode}";
                _joinCode = lobby.JoinCode;
            }
            else
            {
                _lobbyStatus = $"Failed: {result}";
            }

            _lobbyOperationInProgress = false;
            Repaint();
        }

        private async void JoinLobby(EOSLobbyManager lobbyMgr)
        {
            _lobbyOperationInProgress = true;
            _lobbyStatus = $"Joining {_joinCode}...";
            Repaint();

            var (result, lobby) = await lobbyMgr.JoinLobbyByCodeAsync(_joinCode);
            if (result == Result.Success)
            {
                _lobbyStatus = $"Joined: {lobby.JoinCode}";
            }
            else
            {
                _lobbyStatus = $"Failed: {result}";
            }

            _lobbyOperationInProgress = false;
            Repaint();
        }

        private async void QuickMatch(EOSLobbyManager lobbyMgr)
        {
            _lobbyOperationInProgress = true;
            _lobbyStatus = "Finding match...";
            Repaint();

            var (result, lobby) = await lobbyMgr.QuickMatchAsync();
            if (result == Result.Success)
            {
                _lobbyStatus = $"Matched: {lobby.JoinCode}";
                _joinCode = lobby.JoinCode;
            }
            else
            {
                _lobbyStatus = $"No matches found.";
            }

            _lobbyOperationInProgress = false;
            Repaint();
        }

        private async void LeaveLobby(EOSLobbyManager lobbyMgr)
        {
            _lobbyOperationInProgress = true;
            _lobbyStatus = "Leaving...";
            Repaint();

            await lobbyMgr.LeaveLobbyAsync();
            _lobbyStatus = "";
            _joinCode = "";
            _lobbyOperationInProgress = false;
            Repaint();
        }

        #endregion
    }
#endif
}
