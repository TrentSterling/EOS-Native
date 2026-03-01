#if UNITY_EDITOR
using UnityEditor;
using UnityEditor.Build.Reporting;
using UnityEngine;

public static class BuildAndroid
{
    [MenuItem("Build/Android APK (Clean)")]
    public static void Build()
    {
        // Switch to Android platform first
        EditorUserBuildSettings.SwitchActiveBuildTarget(BuildTargetGroup.Android, BuildTarget.Android);

        string outputPath = "Builds/EOS-Native-Android.apk";

        var options = new BuildPlayerOptions
        {
            scenes = new string[0], // No scenes needed — this is a library/package test build
            locationPathName = outputPath,
            target = BuildTarget.Android,
            options = BuildOptions.None,
        };

        BuildReport report = BuildPipeline.BuildPlayer(options);

        if (report.summary.result == BuildResult.Succeeded)
        {
            Debug.Log($"[BuildAndroid] Build succeeded: {outputPath} ({report.summary.totalSize} bytes)");
            EditorApplication.Exit(0);
        }
        else
        {
            Debug.LogError($"[BuildAndroid] Build FAILED: {report.summary.result}");
            foreach (var step in report.steps)
            {
                foreach (var msg in step.messages)
                {
                    if (msg.type == LogType.Error || msg.type == LogType.Warning)
                        Debug.LogError($"  [{step.name}] {msg.content}");
                }
            }
            EditorApplication.Exit(1);
        }
    }
}
#endif
