using System.Collections.Generic;
using EOSNative.Net;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace EOSNative.Editor
{
    /// <summary>
    /// Auto-assigns stable SceneIds to NetworkObjects on scene save.
    /// Sequential ushort IDs starting at 1. Already-assigned IDs are preserved.
    /// </summary>
    [InitializeOnLoad]
    public static class SceneObjectIdAssigner
    {
        static SceneObjectIdAssigner()
        {
            EditorSceneManager.sceneSaving += OnSceneSaving;
        }

        private static void OnSceneSaving(Scene scene, string path)
        {
            var allNetObjs = Object.FindObjectsByType<NetworkObject>(FindObjectsSortMode.None);
            if (allNetObjs.Length == 0) return;

            // Collect already-assigned IDs to avoid collisions
            var usedIds = new HashSet<ushort>();
            foreach (var obj in allNetObjs)
            {
                if (obj.SceneId != 0)
                    usedIds.Add(obj.SceneId);
            }

            // Assign sequential IDs to unassigned objects
            ushort nextId = 1;
            int assigned = 0;
            foreach (var obj in allNetObjs)
            {
                if (obj.SceneId != 0) continue;

                // Find next available ID
                while (usedIds.Contains(nextId)) nextId++;
                if (nextId == 0) break; // overflow guard (65535 scene objects would be absurd)

                var so = new SerializedObject(obj);
                var prop = so.FindProperty("_sceneId");
                if (prop != null)
                {
                    prop.intValue = nextId;
                    so.ApplyModifiedPropertiesWithoutUndo();
                }

                usedIds.Add(nextId);
                nextId++;
                assigned++;
            }

            if (assigned > 0)
                Debug.Log($"[SceneObjectIdAssigner] Assigned SceneId to {assigned} NetworkObjects in '{scene.name}'");
        }
    }
}
