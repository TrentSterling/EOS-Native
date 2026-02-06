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
    /// 2. Injects eos_login_protocol_scheme string resource (required by AAR's AndroidManifest)
    /// Runs after Unity generates the Gradle project, before Gradle builds it.
    /// </summary>
    public class EOSAndroidBuildProcessor : IPostGenerateGradleAndroidProject
    {
        private const string DesugarDep = "coreLibraryDesugaring 'com.android.tools:desugar_jdk_libs:2.1.4'";
        private const string DesugarOption = "coreLibraryDesugaringEnabled true";

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

            // 2. Inject eos_login_protocol_scheme string resource
            InjectEosLoginScheme(path);
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

            if (modified)
            {
                File.WriteAllText(gradlePath, content);
                Debug.Log($"[EOS-Native] Enabled core library desugaring in {moduleName}/build.gradle");
            }
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
