#if UNITY_ANDROID
using System.IO;
using System.Text.RegularExpressions;
using UnityEditor.Android;
using UnityEngine;

namespace EOSNative.Editor
{
    /// <summary>
    /// Automatically enables core library desugaring for Android builds.
    /// The EOS SDK AAR (eossdk-StaticSTDC-release) requires this to build.
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
            string launcherGradle = Path.Combine(gradleRoot, "launcher", "build.gradle");
            string unityLibGradle = Path.Combine(path, "build.gradle");

            // Both modules need desugaring enabled
            if (File.Exists(launcherGradle))
                InjectDesugaring(launcherGradle, "launcher");

            if (File.Exists(unityLibGradle))
                InjectDesugaring(unityLibGradle, "unityLibrary");
        }

        private static void InjectDesugaring(string gradlePath, string moduleName)
        {
            string content = File.ReadAllText(gradlePath);
            bool modified = false;

            // 1. Add coreLibraryDesugaringEnabled to compileOptions block
            if (!content.Contains("coreLibraryDesugaringEnabled"))
            {
                content = Regex.Replace(
                    content,
                    @"(compileOptions\s*\{)",
                    $"$1\n        {DesugarOption}");
                modified = true;
            }

            // 2. Add desugaring dependency to dependencies block
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
                    // No dependencies block exists — add one
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
    }
}
#endif
