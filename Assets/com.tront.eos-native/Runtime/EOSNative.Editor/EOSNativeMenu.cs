using UnityEngine;
using UnityEditor;

namespace EOSNative.Editor
{
    /// <summary>
    /// Tools menu for EOS Native setup and utilities.
    /// </summary>
    public static class EOSNativeMenu
    {
        private const string MenuRoot = "Tools/EOS Native/";

        /// <summary>
        /// Sets up the scene with core EOS components (EOSManager).
        /// </summary>
        [MenuItem(MenuRoot + "Setup Scene", priority = 0)]
        public static void SetupScene()
        {
            // Check if EOSManager already exists
            var existingManager = Object.FindAnyObjectByType<EOSManager>();
            if (existingManager != null)
            {
                Debug.Log("[EOSNativeMenu] EOSManager already exists in scene.");
                Selection.activeGameObject = existingManager.gameObject;
                EditorGUIUtility.PingObject(existingManager.gameObject);
                return;
            }

            // Create new GameObject with EOSManager
            var go = new GameObject("EOSManager");
            Undo.RegisterCreatedObjectUndo(go, "Setup EOS Native Scene");

            go.AddComponent<EOSManager>();

            // Select the new object
            Selection.activeGameObject = go;
            EditorGUIUtility.PingObject(go);

            Debug.Log("[EOSNativeMenu] Scene setup complete! EOSManager created.");
        }

        /// <summary>
        /// Validates Setup Scene menu item - always available.
        /// </summary>
        [MenuItem(MenuRoot + "Setup Scene", true)]
        public static bool SetupSceneValidate()
        {
            return true;
        }

        /// <summary>
        /// Selects and pings the SampleEOSConfig asset in the Project window.
        /// </summary>
        [MenuItem(MenuRoot + "Select Config", priority = 1)]
        public static void SelectConfig()
        {
            var guids = AssetDatabase.FindAssets("SampleEOSConfig t:EOSConfig");
            if (guids.Length > 0)
            {
                var path = AssetDatabase.GUIDToAssetPath(guids[0]);
                var asset = AssetDatabase.LoadAssetAtPath<EOSConfig>(path);
                if (asset != null)
                {
                    Selection.activeObject = asset;
                    EditorGUIUtility.PingObject(asset);
                    Debug.Log($"[EOSNativeMenu] Selected config: {path}");
                    return;
                }
            }

            // No SampleEOSConfig found - try to find any EOSConfig
            guids = AssetDatabase.FindAssets("t:EOSConfig");
            if (guids.Length > 0)
            {
                var path = AssetDatabase.GUIDToAssetPath(guids[0]);
                var asset = AssetDatabase.LoadAssetAtPath<EOSConfig>(path);
                if (asset != null)
                {
                    Selection.activeObject = asset;
                    EditorGUIUtility.PingObject(asset);
                    Debug.Log($"[EOSNativeMenu] Selected config: {path}");
                    return;
                }
            }

            // No config found - offer to create one
            if (EditorUtility.DisplayDialog(
                "EOSConfig Not Found",
                "No EOSConfig asset found in the project.\n\nWould you like to create one?",
                "Create Config",
                "Cancel"))
            {
                CreateEOSConfig();
            }
        }

        /// <summary>
        /// Creates a new EOSConfig asset.
        /// </summary>
        [MenuItem(MenuRoot + "Create New Config", priority = 2)]
        public static void CreateEOSConfig()
        {
            var config = ScriptableObject.CreateInstance<EOSConfig>();

            // Create in Resources folder so it can be loaded at runtime
            var directory = "Assets/Resources";
            if (!AssetDatabase.IsValidFolder(directory))
            {
                AssetDatabase.CreateFolder("Assets", "Resources");
            }

            var path = AssetDatabase.GenerateUniqueAssetPath($"{directory}/NewEOSConfig.asset");
            AssetDatabase.CreateAsset(config, path);
            AssetDatabase.SaveAssets();

            Selection.activeObject = config;
            EditorGUIUtility.PingObject(config);

            Debug.Log($"[EOSNativeMenu] Created new EOSConfig at {path}. Configure your EOS credentials in the Inspector.");
        }

        [MenuItem(MenuRoot + "Create New Config", true)]
        public static bool CreateEOSConfigValidate()
        {
            return true;
        }

        /// <summary>
        /// Creates a SampleEOSConfig pre-filled with PlayEveryWare demo credentials for testing.
        /// </summary>
        [MenuItem(MenuRoot + "Create Sample Config", priority = 3)]
        public static void CreateSampleConfig()
        {
            // Check if one already exists
            var existing = AssetDatabase.FindAssets("SampleEOSConfig t:EOSConfig");
            if (existing.Length > 0)
            {
                var existingPath = AssetDatabase.GUIDToAssetPath(existing[0]);
                var existingAsset = AssetDatabase.LoadAssetAtPath<EOSConfig>(existingPath);
                if (existingAsset != null)
                {
                    Selection.activeObject = existingAsset;
                    EditorGUIUtility.PingObject(existingAsset);
                    Debug.Log($"[EOSNativeMenu] SampleEOSConfig already exists at {existingPath}");
                    return;
                }
            }

            var config = ScriptableObject.CreateInstance<EOSConfig>();

            // PlayEveryWare demo credentials (public, safe for testing)
            config.ProductName = "EOSNativeTest";
            config.ProductId = "f7102b835ed14b5fb6b3a05d87b3d101";
            config.SandboxId = "ab139ee5b644412781cf99f48b993b45";
            config.DeploymentId = "c529498f660a4a3d8a123fd04552cb47";
            config.ClientId = "xyza7891wPzGRvRf4SkjlIF8YuqlRLbQ";
            config.ClientSecret = "aXPlP1xDH0PXnp5U+i+M5pYHhaE1a8viV0l1GO422ms";
            config.EncryptionKey = "1111111111111111111111111111111111111111111111111111111111111111";
            config.DefaultDisplayName = "Player";

            // Create in Resources folder
            var directory = "Assets/Resources";
            if (!AssetDatabase.IsValidFolder(directory))
            {
                AssetDatabase.CreateFolder("Assets", "Resources");
            }

            var path = $"{directory}/SampleEOSConfig.asset";
            AssetDatabase.CreateAsset(config, path);
            AssetDatabase.SaveAssets();

            Selection.activeObject = config;
            EditorGUIUtility.PingObject(config);

            Debug.Log($"[EOSNativeMenu] Created SampleEOSConfig with PlayEveryWare demo credentials at {path}");
        }

        [MenuItem(MenuRoot + "Create Sample Config", true)]
        public static bool CreateSampleConfigValidate()
        {
            return true;
        }

        /// <summary>
        /// Logs platform info to console (useful for crossplay debugging).
        /// </summary>
        [MenuItem(MenuRoot + "Log Platform Info", priority = 51)]
        public static void LogPlatformInfo()
        {
            EOSPlatformHelper.LogPlatformInfo();
        }

        #region Validation Utilities

        /// <summary>
        /// Validates the current scene setup and reports any issues.
        /// </summary>
        [MenuItem(MenuRoot + "Validate Setup", priority = 50)]
        public static void ValidateSetup()
        {
            var issues = new System.Collections.Generic.List<string>();
            var warnings = new System.Collections.Generic.List<string>();

            // Check for EOSManager
            var eosManager = Object.FindAnyObjectByType<EOSManager>();
            if (eosManager == null)
            {
                issues.Add("EOSManager not found in scene");
            }

            // Check for config on EOSManager
            if (eosManager != null)
            {
                // EOSManager doesn't hold config directly - it's passed to Initialize()
                // Just verify EOSManager exists
            }

            // Check for lobby manager
            var lobbyManager = Object.FindAnyObjectByType<Lobbies.EOSLobbyManager>();
            if (lobbyManager == null)
            {
                warnings.Add("EOSLobbyManager not found (required for lobby features)");
            }

            // Check for voice manager
            var voiceManager = Object.FindAnyObjectByType<Voice.EOSVoiceManager>();
            if (voiceManager == null)
            {
                warnings.Add("EOSVoiceManager not found (required for voice features)");
            }

            // Report results
            if (issues.Count == 0 && warnings.Count == 0)
            {
                EditorUtility.DisplayDialog("Validation Passed",
                    "All required components are properly configured!", "OK");
                Debug.Log("[EOSNativeMenu] Validation passed - all components configured correctly.");
            }
            else
            {
                var message = "";
                if (issues.Count > 0)
                {
                    message += "ERRORS:\n";
                    foreach (var issue in issues)
                    {
                        message += $"  - {issue}\n";
                        Debug.LogError($"[EOSNativeMenu] {issue}");
                    }
                }
                if (warnings.Count > 0)
                {
                    if (issues.Count > 0) message += "\n";
                    message += "WARNINGS:\n";
                    foreach (var warning in warnings)
                    {
                        message += $"  - {warning}\n";
                        Debug.LogWarning($"[EOSNativeMenu] {warning}");
                    }
                }

                message += "\nUse 'Tools > EOS Native > Setup Scene' to fix these issues.";

                EditorUtility.DisplayDialog("Validation Issues Found", message, "OK");
            }
        }

        #endregion
    }
}
