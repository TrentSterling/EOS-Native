#if UNITY_ANDROID
using System.IO;
using System.Text.RegularExpressions;
using System.Xml;
using UnityEditor;
using UnityEditor.Android;
using UnityEngine;

namespace EOSNative.Editor
{
    /// <summary>
    /// Automatically configures Android builds for the EOS SDK:
    /// 1. Enables core library desugaring (required by eossdk-StaticSTDC-release.aar)
    /// 2. Adds androidx.browser:browser dependency (required for Custom Tabs login flow)
    /// 3. Sets extractNativeLibs=true (required for native .so extraction)
    /// 4. Injects eos_login_protocol_scheme string resource (required by AAR's AndroidManifest)
    /// Runs after Unity generates the Gradle project, before Gradle builds it.
    /// </summary>
    public class EOSAndroidBuildProcessor : IPostGenerateGradleAndroidProject
    {
        private const string DesugarDep = "coreLibraryDesugaring 'com.android.tools:desugar_jdk_libs:2.1.4'";
        private const string DesugarOption = "coreLibraryDesugaringEnabled true";

        // AndroidX dependencies required by the EOS AAR. Unity doesn't resolve transitive Maven
        // dependencies from AARs, so we must add them explicitly. Versions match PlayEveryWare reference.
        private static readonly string[] AndroidXDeps = new[]
        {
            "implementation 'androidx.appcompat:appcompat:1.5.1'",
            "implementation 'androidx.constraintlayout:constraintlayout:2.1.4'",
            "implementation 'androidx.security:security-crypto:1.0.0'",
            "implementation 'androidx.browser:browser:1.4.0'",
        };

        public int callbackOrder => 99; // Run late so we don't conflict with other processors

        public void OnPostGenerateGradleAndroidProject(string path)
        {
            // path = unityLibrary module. Launcher is a sibling directory.
            string gradleRoot = Directory.GetParent(path).FullName;
            string launcherDir = Path.Combine(gradleRoot, "launcher");
            string launcherGradle = Path.Combine(launcherDir, "build.gradle");
            string unityLibGradle = Path.Combine(path, "build.gradle");

            // 1. Enable core library desugaring in both modules
            if (File.Exists(launcherGradle))
                InjectDesugaring(launcherGradle, "launcher");

            if (File.Exists(unityLibGradle))
                InjectDesugaring(unityLibGradle, "unityLibrary");

            // 2. Ensure native libs are extracted from AARs (required for EOS SDK .so loading)
            InjectExtractNativeLibs(path);

            // 3. Inject eos_login_protocol_scheme string resource
            InjectEosLoginScheme(path);

            // 4. Inject Java init helper class (fixes JNI classloader mismatch for EOS native lib)
            InjectJavaInitHelper(path);

            // 5. Inject required permissions for EOS features (voice, networking)
            InjectPermissions(path);

            // 6. Inject ProGuard keep rules for EOS SDK Java classes (prevents R8 stripping)
            InjectProguardRules(path);
        }

        private static void InjectDesugaring(string gradlePath, string moduleName)
        {
            string content = File.ReadAllText(gradlePath);
            bool modified = false;

            // Add coreLibraryDesugaringEnabled to compileOptions block
            if (!content.Contains("coreLibraryDesugaringEnabled"))
            {
                content = Regex.Replace(
                    content,
                    @"(compileOptions\s*\{)",
                    $"$1\n        {DesugarOption}");
                modified = true;
            }

            // Add desugaring dependency to dependencies block
            if (!content.Contains("desugar_jdk_libs"))
            {
                if (content.Contains("dependencies {"))
                {
                    content = Regex.Replace(
                        content,
                        @"(dependencies\s*\{)",
                        $"$1\n    {DesugarDep}");
                }
                else
                {
                    content += $"\ndependencies {{\n    {DesugarDep}\n}}\n";
                }
                modified = true;
            }

            // Add AndroidX dependencies required by EOS AAR
            foreach (string dep in AndroidXDeps)
            {
                // Extract the group:artifact portion for the contains check (e.g. "androidx.browser:browser")
                int quoteStart = dep.IndexOf('\'') + 1;
                int lastColon = dep.LastIndexOf(':');
                string artifactKey = dep.Substring(quoteStart, lastColon - quoteStart);

                if (!content.Contains(artifactKey))
                {
                    if (content.Contains("dependencies {"))
                    {
                        content = Regex.Replace(
                            content,
                            @"(dependencies\s*\{)",
                            $"$1\n    {dep}");
                    }
                    else
                    {
                        content += $"\ndependencies {{\n    {dep}\n}}\n";
                    }
                    modified = true;
                }
            }

            // Ensure google() repository is available for AndroidX dependency resolution
            if (!content.Contains("google()") && content.Contains("repositories {"))
            {
                content = Regex.Replace(
                    content,
                    @"(repositories\s*\{)",
                    "$1\n        google()");
                modified = true;
            }

            if (modified)
            {
                File.WriteAllText(gradlePath, content);
                Debug.Log($"[EOS-Native] Configured {moduleName}/build.gradle (desugaring + AndroidX dependencies)");
            }
        }

        private static void InjectExtractNativeLibs(string unityLibPath)
        {
            // Set extractNativeLibs=true in AndroidManifest.xml so native .so files
            // from AAR dependencies are extracted to the APK lib directory at install time.
            // Without this, System.loadLibrary() may fail to find libEOSSDK.so on some devices.
            string manifestPath = Path.Combine(unityLibPath, "src", "main", "AndroidManifest.xml");
            if (!File.Exists(manifestPath))
            {
                Debug.LogWarning("[EOS-Native] AndroidManifest.xml not found, skipping extractNativeLibs injection.");
                return;
            }

            string content = File.ReadAllText(manifestPath);

            // Check if extractNativeLibs is already set
            if (content.Contains("extractNativeLibs"))
                return;

            // Add extractNativeLibs="true" to the <application> tag
            content = Regex.Replace(
                content,
                @"(<application\b)",
                "$1 android:extractNativeLibs=\"true\"");

            File.WriteAllText(manifestPath, content);
            Debug.Log("[EOS-Native] Injected android:extractNativeLibs=\"true\" into AndroidManifest.xml");
        }

        private static void InjectEosLoginScheme(string unityLibPath)
        {
            // Find the EOS client ID from the EOSConfig asset
            string clientId = FindClientId();
            if (string.IsNullOrEmpty(clientId))
            {
                Debug.LogWarning("[EOS-Native] No EOSConfig asset found with a ClientId. " +
                    "Android EOS login callbacks may not work. " +
                    "Create an EOSConfig via Assets > Create > EOS Native > Config");
                // Use a placeholder so the build doesn't fail
                clientId = "placeholder";
            }

            // EOS requires the scheme to be lowercase: eos.{clientid}
            string scheme = $"eos.{clientId.ToLower()}";

            // Inject into unityLibrary's res/values/strings.xml
            string valuesDir = Path.Combine(unityLibPath, "src", "main", "res", "values");
            if (!Directory.Exists(valuesDir))
                Directory.CreateDirectory(valuesDir);

            string stringsPath = Path.Combine(valuesDir, "strings.xml");

            XmlDocument xml = new XmlDocument();
            if (File.Exists(stringsPath))
            {
                xml.Load(stringsPath);
            }
            else
            {
                xml.LoadXml("<?xml version=\"1.0\" encoding=\"utf-8\"?>\n<resources></resources>");
            }

            XmlNode resources = xml.SelectSingleNode("resources");

            // Remove existing entry if present
            XmlNode existing = resources.SelectSingleNode("string[@name='eos_login_protocol_scheme']");
            if (existing != null)
                resources.RemoveChild(existing);

            // Add the scheme string
            XmlElement element = xml.CreateElement("string");
            element.SetAttribute("name", "eos_login_protocol_scheme");
            element.InnerText = scheme;
            resources.AppendChild(element);

            xml.Save(stringsPath);
            Debug.Log($"[EOS-Native] Injected eos_login_protocol_scheme: {scheme}");
        }

        private static void InjectPermissions(string unityLibPath)
        {
            // EOS SDK requires RECORD_AUDIO for voice/RTC and ACCESS_WIFI_STATE for NAT detection.
            // These must be declared in the AndroidManifest for runtime permission requests to work.
            string manifestPath = Path.Combine(unityLibPath, "src", "main", "AndroidManifest.xml");
            if (!File.Exists(manifestPath))
                return;

            string content = File.ReadAllText(manifestPath);
            bool modified = false;

            string[] requiredPermissions = new[]
            {
                "android.permission.RECORD_AUDIO",
                "android.permission.ACCESS_WIFI_STATE",
            };

            foreach (string perm in requiredPermissions)
            {
                if (!content.Contains(perm))
                {
                    // Insert before the <application> tag
                    content = Regex.Replace(
                        content,
                        @"(\s*<application\b)",
                        $"\n    <uses-permission android:name=\"{perm}\" />$1");
                    modified = true;
                }
            }

            if (modified)
            {
                File.WriteAllText(manifestPath, content);
                Debug.Log("[EOS-Native] Injected RECORD_AUDIO and ACCESS_WIFI_STATE permissions into AndroidManifest.xml");
            }
        }

        private static void InjectJavaInitHelper(string unityLibPath)
        {
            // Generate a Java helper class that calls EOSSDK.init() from the app's own
            // classloader context. This ensures JNI_OnLoad's FindClass() resolves EOS SDK
            // Java classes correctly when registering native methods.
            //
            // IMPORTANT: Do NOT call System.loadLibrary("EOSSDK") before EOSSDK.init().
            // The AAR's init() method handles native library loading internally in the
            // correct order. Pre-loading corrupts JNI registration.
            // (PlayEveryWare's implementation also only calls EOSSDK.init, never loadLibrary.)
            string javaDir = Path.Combine(unityLibPath, "src", "main", "java", "com", "tront", "eosnative");
            if (!Directory.Exists(javaDir))
                Directory.CreateDirectory(javaDir);

            string javaFile = Path.Combine(javaDir, "EOSNativeInit.java");

            const string javaSource = @"package com.tront.eosnative;

import android.app.Activity;

/**
 * Helper class that calls EOSSDK.init() from the app's own classloader context.
 * This ensures that JNI_OnLoad can find EOS SDK Java classes via FindClass()
 * and RegisterNatives runs correctly for methods like EOSLogger.Log.
 *
 * IMPORTANT: Do NOT call System.loadLibrary before EOSSDK.init().
 * The AAR's init() handles all native library loading internally.
 */
public class EOSNativeInit {
    private static boolean sInitialized = false;

    public static boolean init(Activity activity) {
        if (sInitialized) return true;

        // Set thread context classloader to the app's classloader.
        // This helps JNI_OnLoad's FindClass resolve AAR classes on Android
        // versions where it checks the thread context classloader.
        try {
            Thread.currentThread().setContextClassLoader(
                activity.getClassLoader());
        } catch (Throwable t) {
            // Non-fatal — continue without classloader hint
            android.util.Log.w(""EOSNativeInit"", ""setContextClassLoader failed: "" + t.getMessage());
        }

        try {
            // Let the AAR handle native library loading internally.
            // EOSSDK.init() calls System.loadLibrary from the correct classloader,
            // so JNI_OnLoad's FindClass resolves AAR Java classes properly.
            com.epicgames.mobile.eossdk.EOSSDK.init(activity);
            sInitialized = true;
            return true;
        } catch (Throwable t) {
            // Catch Throwable, not Exception — UnsatisfiedLinkError extends Error, not Exception.
            // Without this, the error propagates and crashes instead of being handled.
            android.util.Log.e(""EOSNativeInit"", ""EOSSDK.init() failed: "" + t.getMessage(), t);
            // Mark as initialized anyway — the native library IS loaded (P/Invoke works),
            // and a retry won't help. Java audio pipeline may be broken though.
            sInitialized = true;
            return false;
        }
    }
}
";

            File.WriteAllText(javaFile, javaSource);
            Debug.Log("[EOS-Native] Injected EOSNativeInit.java helper class for Android SDK init.");
        }

        private static void InjectProguardRules(string unityLibPath)
        {
            // Generate ProGuard keep rules that prevent R8/ProGuard from stripping EOS SDK
            // Java classes. These classes are needed for JNI native method registration
            // (RegisterNatives during JNI_OnLoad). Without these rules, R8 may strip classes
            // like com.epicgames.mobile.eossdk.EOSLogger, causing UnsatisfiedLinkError.
            string proguardPath = Path.Combine(unityLibPath, "proguard-eos.pro");

            const string proguardRules = @"# EOS Native SDK - Keep rules for JNI native method registration
# These classes are called from native code via RegisterNatives in JNI_OnLoad.
# R8/ProGuard cannot detect these references and may strip them.
-keep class com.epicgames.mobile.eossdk.** { *; }
-keep class com.tront.eosnative.** { *; }
-dontwarn com.epicgames.mobile.eossdk.**
";

            File.WriteAllText(proguardPath, proguardRules);

            // Reference the proguard file in build.gradle
            string gradlePath = Path.Combine(unityLibPath, "build.gradle");
            if (File.Exists(gradlePath))
            {
                string content = File.ReadAllText(gradlePath);

                if (!content.Contains("proguard-eos.pro"))
                {
                    // Add proguardFiles directive to the android > defaultConfig or buildTypes > release block
                    // Safest approach: add to the existing proguardFiles line or create one in defaultConfig
                    if (content.Contains("proguardFiles"))
                    {
                        // Append our file to existing proguardFiles directive
                        content = Regex.Replace(
                            content,
                            @"(proguardFiles\s+[^\n]+)",
                            "$1, 'proguard-eos.pro'");
                    }
                    else if (content.Contains("defaultConfig {"))
                    {
                        // Add proguardFiles to defaultConfig
                        content = Regex.Replace(
                            content,
                            @"(defaultConfig\s*\{)",
                            "$1\n        proguardFiles 'proguard-eos.pro'");
                    }
                    else
                    {
                        // Fallback: add consumerProguardFiles at the android block level
                        content = Regex.Replace(
                            content,
                            @"(android\s*\{)",
                            "$1\n    consumerProguardFiles 'proguard-eos.pro'");
                    }

                    File.WriteAllText(gradlePath, content);
                }
            }

            Debug.Log("[EOS-Native] Injected ProGuard keep rules for EOS SDK classes.");
        }

        private static string FindClientId()
        {
            // Search for EOSConfig ScriptableObject assets
            string[] guids = AssetDatabase.FindAssets("t:EOSConfig");
            if (guids.Length == 0)
                return null;

            foreach (string guid in guids)
            {
                string assetPath = AssetDatabase.GUIDToAssetPath(guid);
                var config = AssetDatabase.LoadAssetAtPath<EOSConfig>(assetPath);
                if (config != null && !string.IsNullOrEmpty(config.ClientId))
                    return config.ClientId;
            }

            return null;
        }
    }
}
#endif
