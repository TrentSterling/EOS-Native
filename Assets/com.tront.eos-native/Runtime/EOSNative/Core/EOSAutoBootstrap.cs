using System.Threading.Tasks;
using Epic.OnlineServices;
using UnityEngine;

namespace EOSNative
{
    /// <summary>
    /// Auto-initializes EOS SDK and logs in on play. Attach to the EOSManager GameObject.
    /// </summary>
    public class EOSAutoBootstrap : MonoBehaviour
    {
        [Header("Configuration")]
        [SerializeField] private EOSConfig _config;
        [SerializeField] private string _resourceConfigName = "SampleEOSConfig";

        [Header("Auto Login")]
        [SerializeField] private bool _autoLogin = true;
        [SerializeField] private string _displayName = "Player";

        [Header("Login Method")]
        [SerializeField] private LoginMethod _loginMethod = LoginMethod.DeviceToken;

        public enum LoginMethod
        {
            DeviceToken,
            SmartLogin
        }

        private async void Start()
        {
            // Wait a frame for EOSManager to initialize
            await Task.Yield();

            var manager = EOSManager.Instance;
            if (manager == null)
            {
                Debug.LogError("[EOSAutoBootstrap] EOSManager not found in scene.");
                return;
            }

            // Load config
            var config = _config;
            if (config == null && !string.IsNullOrEmpty(_resourceConfigName))
            {
                config = Resources.Load<EOSConfig>(_resourceConfigName);
                if (config == null)
                {
                    Debug.LogError($"[EOSAutoBootstrap] EOSConfig '{_resourceConfigName}' not found in Resources. Use Tools > EOS Native > Create Sample Config.");
                    return;
                }
            }

            if (config == null)
            {
                Debug.LogError("[EOSAutoBootstrap] No EOSConfig assigned or found in Resources.");
                return;
            }

            // Initialize
            if (!manager.IsInitialized)
            {
                Debug.Log("[EOSAutoBootstrap] Initializing EOS SDK...");
                var initResult = manager.Initialize(config);
                if (initResult != Result.Success)
                {
                    Debug.LogError($"[EOSAutoBootstrap] Init failed: {initResult}");
                    return;
                }
                Debug.Log("[EOSAutoBootstrap] EOS SDK initialized.");
            }

            // Login
            if (_autoLogin && !manager.IsLoggedIn)
            {
                string name = string.IsNullOrEmpty(_displayName) ? config.DefaultDisplayName : _displayName;
                Debug.Log($"[EOSAutoBootstrap] Logging in as '{name}'...");

                Result loginResult;
                if (_loginMethod == LoginMethod.SmartLogin)
                {
                    loginResult = await manager.LoginSmartAsync(name);
                }
                else
                {
                    loginResult = await manager.LoginWithDeviceTokenAsync(name);
                }

                if (loginResult == Result.Success)
                {
                    Debug.Log($"[EOSAutoBootstrap] Logged in. PUID: {manager.LocalProductUserId}");
                }
                else
                {
                    Debug.LogError($"[EOSAutoBootstrap] Login failed: {loginResult}");
                }
            }
        }
    }
}
