using UnityEditor;
using UnityEngine;
using EOSNative.Net;
using EOSNative.P2P;
using EOSNative.UI;

namespace EOSNative.Editor
{
    /// <summary>
    /// Custom inspectors for networking and runtime components that would otherwise
    /// show blank in the Inspector. Each shows runtime status as read-only fields.
    /// </summary>

    [CustomEditor(typeof(NetworkManager))]
    public class NetworkManagerEditor : UnityEditor.Editor
    {
        public override void OnInspectorGUI()
        {
            DrawDefaultInspector();

            var mgr = (NetworkManager)target;

            EditorGUILayout.Space(5);
            EditorGUILayout.LabelField("Network Status", EditorStyles.boldLabel);

            if (!Application.isPlaying)
            {
                EditorGUILayout.HelpBox("Enter Play Mode to see runtime status.", MessageType.Info);
                return;
            }

            using (new EditorGUI.DisabledGroupScope(true))
            {
                EditorGUILayout.Toggle("Is Online", mgr.IsOnline);
                EditorGUILayout.Toggle("Is Host", mgr.IsHost);
                EditorGUILayout.Toggle("Is Spectator", mgr.IsSpectator);
                EditorGUILayout.IntField("Objects", mgr.Objects.Count);
                EditorGUILayout.IntField("Connected Players", mgr.ConnectedPlayers.Count);
                EditorGUILayout.Toggle("Compression", mgr.CompressionEnabled);

                if (mgr.RoomState != null)
                {
                    EditorGUILayout.Space(3);
                    EditorGUILayout.LabelField("Room State", EditorStyles.boldLabel);
                    EditorGUILayout.TextField("Game Mode", mgr.RoomState.GameMode.Value);
                    EditorGUILayout.TextField("Map", mgr.RoomState.MapName.Value);
                    EditorGUILayout.TextField("Phase", mgr.RoomState.CurrentPhase.ToString());
                    EditorGUILayout.IntField("Players", mgr.RoomState.PlayerCount.Value);
                }

                if (mgr.LocalPlayerState != null)
                {
                    EditorGUILayout.Space(3);
                    EditorGUILayout.LabelField("Local Player", EditorStyles.boldLabel);
                    EditorGUILayout.TextField("Name", mgr.LocalPlayerState.DisplayName.Value);
                    EditorGUILayout.IntField("Team", mgr.LocalPlayerState.Team.Value);
                    EditorGUILayout.Toggle("Ready", mgr.LocalPlayerState.IsReady.Value);
                    EditorGUILayout.IntField("Score", mgr.LocalPlayerState.Score.Value);
                }
            }

            EditorUtility.SetDirty(target);
        }
    }

    [CustomEditor(typeof(NetworkObject))]
    public class NetworkObjectEditor : UnityEditor.Editor
    {
        private SerializedProperty _destroyWithOwner;
        private SerializedProperty _alwaysVisible;

        private void OnEnable()
        {
            _destroyWithOwner = serializedObject.FindProperty("_destroyWithOwner");
            _alwaysVisible = serializedObject.FindProperty("_alwaysVisible");
        }

        public override void OnInspectorGUI()
        {
            serializedObject.Update();

            var obj = (NetworkObject)target;

            // --- Edit-time configuration ---
            EditorGUILayout.LabelField("Configuration", EditorStyles.boldLabel);
            EditorGUILayout.PropertyField(_destroyWithOwner, new GUIContent("Destroy With Owner",
                "Destroy when owner disconnects instead of transferring to host. Use for player avatars."));
            EditorGUILayout.PropertyField(_alwaysVisible, new GUIContent("Always Visible",
                "Always replicate to all peers regardless of spatial interest. Use for objectives, world anchors."));

            serializedObject.ApplyModifiedProperties();

            // --- Hierarchy info (works in edit + play) ---
            var childNetObjs = obj.GetComponentsInChildren<NetworkObject>(true);
            var parentNetObj = obj.transform.parent != null ? obj.transform.parent.GetComponentInParent<NetworkObject>() : null;

            if (childNetObjs.Length > 1 || parentNetObj != null)
            {
                EditorGUILayout.Space(5);
                EditorGUILayout.LabelField("Hierarchy", EditorStyles.boldLabel);
                using (new EditorGUI.DisabledGroupScope(true))
                {
                    if (parentNetObj != null)
                        EditorGUILayout.ObjectField("Parent NetworkObject", parentNetObj, typeof(NetworkObject), true);
                    if (childNetObjs.Length > 1)
                        EditorGUILayout.IntField("Child NetworkObjects", childNetObjs.Length - 1);
                }
            }

            if (!Application.isPlaying) return;

            // --- Runtime status ---
            EditorGUILayout.Space(5);
            EditorGUILayout.LabelField("Runtime Status", EditorStyles.boldLabel);

            using (new EditorGUI.DisabledGroupScope(true))
            {
                EditorGUILayout.TextField("Network ID", obj.IsRegistered ? $"0x{obj.NetworkId:X8}" : "(unregistered)");
                EditorGUILayout.TextField("Prefab ID", $"0x{obj.PrefabId:X4}");
                EditorGUILayout.TextField("Owner", obj.OwnerId?.ToString() ?? "(none)");
                EditorGUILayout.Toggle("Is Owner", obj.IsOwner);
                EditorGUILayout.Toggle("Is Host", obj.IsHost);
                EditorGUILayout.Toggle("Is Registered", obj.IsRegistered);
                EditorGUILayout.IntField("SyncVar Count", obj.SyncVarCount);

                if (obj.ParentNetworkId != 0)
                    EditorGUILayout.TextField("Parent Net ID", $"0x{obj.ParentNetworkId:X8}");
                if (obj.IsChildNetworkObject)
                    EditorGUILayout.Toggle("Is Child", true);
            }

            EditorUtility.SetDirty(target);
        }
    }

    [CustomEditor(typeof(NetworkRoomState))]
    public class NetworkRoomStateEditor : UnityEditor.Editor
    {
        public override void OnInspectorGUI()
        {
            var state = (NetworkRoomState)target;

            EditorGUILayout.Space(5);
            EditorGUILayout.LabelField("Room State", EditorStyles.boldLabel);

            if (!Application.isPlaying)
            {
                EditorGUILayout.HelpBox("Enter Play Mode to see room state.", MessageType.Info);
                return;
            }

            using (new EditorGUI.DisabledGroupScope(true))
            {
                EditorGUILayout.TextField("Game Mode", state.GameMode.Value);
                EditorGUILayout.TextField("Map Name", state.MapName.Value);
                EditorGUILayout.IntField("Round", state.RoundNumber.Value);
                EditorGUILayout.IntField("Players", state.PlayerCount.Value);
                EditorGUILayout.IntField("Max Players", state.MaxPlayers.Value);
                EditorGUILayout.FloatField("Round Timer", state.RoundTimer.Value);
                EditorGUILayout.TextField("Phase", state.CurrentPhase.ToString());
                EditorGUILayout.Toggle("In Progress", state.IsInProgress.Value);

                var netObj = state.Net;
                if (netObj != null)
                {
                    EditorGUILayout.Space(3);
                    EditorGUILayout.TextField("Network ID", $"0x{netObj.NetworkId:X8}");
                    EditorGUILayout.TextField("Owner", netObj.OwnerId?.ToString() ?? "(none)");
                    EditorGUILayout.Toggle("Is Owner", netObj.IsOwner);
                }
            }

            EditorUtility.SetDirty(target);
        }
    }

    [CustomEditor(typeof(NetworkPlayerState))]
    public class NetworkPlayerStateEditor : UnityEditor.Editor
    {
        public override void OnInspectorGUI()
        {
            var state = (NetworkPlayerState)target;

            EditorGUILayout.Space(5);
            EditorGUILayout.LabelField("Player State", EditorStyles.boldLabel);

            if (!Application.isPlaying)
            {
                EditorGUILayout.HelpBox("Enter Play Mode to see player state.", MessageType.Info);
                return;
            }

            using (new EditorGUI.DisabledGroupScope(true))
            {
                EditorGUILayout.TextField("Name", state.DisplayName.Value);
                EditorGUILayout.IntField("Team", state.Team.Value);
                EditorGUILayout.Toggle("Ready", state.IsReady.Value);
                EditorGUILayout.IntField("Score", state.Score.Value);
                EditorGUILayout.IntField("Deaths", state.Deaths.Value);
                EditorGUILayout.IntField("Assists", state.Assists.Value);
                EditorGUILayout.TextField("Loadout", state.Loadout.Value);
                EditorGUILayout.IntField("Slot", state.PlayerSlot.Value);
                EditorGUILayout.Toggle("Spectating", state.IsSpectating);
                EditorGUILayout.FloatField("K/D", state.KDRatio);

                var netObj = state.Net;
                if (netObj != null)
                {
                    EditorGUILayout.Space(3);
                    EditorGUILayout.TextField("Network ID", $"0x{netObj.NetworkId:X8}");
                    EditorGUILayout.TextField("Owner", netObj.OwnerId?.ToString() ?? "(none)");
                    EditorGUILayout.Toggle("Is Owner", netObj.IsOwner);
                }
            }

            EditorUtility.SetDirty(target);
        }
    }

    [CustomEditor(typeof(EOSP2PManager))]
    public class EOSP2PManagerEditor : UnityEditor.Editor
    {
        public override void OnInspectorGUI()
        {
            DrawDefaultInspector();

            var mgr = (EOSP2PManager)target;

            EditorGUILayout.Space(5);
            EditorGUILayout.LabelField("P2P Status", EditorStyles.boldLabel);

            if (!Application.isPlaying)
            {
                EditorGUILayout.HelpBox("Enter Play Mode to see P2P status.", MessageType.Info);
                return;
            }

            using (new EditorGUI.DisabledGroupScope(true))
            {
                EditorGUILayout.Toggle("Active", mgr.IsActive);
                EditorGUILayout.IntField("Connected Peers", mgr.Peers.Count);

                if (mgr.Peers.Count > 0)
                {
                    EditorGUILayout.Space(3);
                    EditorGUILayout.LabelField("Peers", EditorStyles.miniBoldLabel);
                    foreach (var peer in mgr.Peers)
                    {
                        string peerStr = peer.ToString();
                        string display = peerStr.Length > 16 ? peerStr.Substring(0, 16) + "..." : peerStr;
                        EditorGUILayout.LabelField("  " + display, EditorStyles.miniLabel);
                    }
                }
            }

            EditorUtility.SetDirty(target);
        }
    }

    [CustomEditor(typeof(NetworkSceneManager))]
    public class NetworkSceneManagerEditor : UnityEditor.Editor
    {
        public override void OnInspectorGUI()
        {
            DrawDefaultInspector();

            EditorGUILayout.Space(5);
            EditorGUILayout.LabelField("Scene Manager", EditorStyles.boldLabel);

            if (!Application.isPlaying)
            {
                EditorGUILayout.HelpBox("Enter Play Mode to see scene status.", MessageType.Info);
                return;
            }

            var roomState = NetworkManager.Instance?.RoomState;
            if (roomState == null)
            {
                EditorGUILayout.LabelField("No RoomState available", EditorStyles.miniLabel);
                return;
            }

            using (new EditorGUI.DisabledGroupScope(true))
            {
                EditorGUILayout.TextField("Active Scene", roomState.ActiveScene ?? "(none)");
                var additive = roomState.AdditiveScenes;
                if (additive.Count > 0)
                {
                    EditorGUILayout.LabelField("Additive Scenes", EditorStyles.miniBoldLabel);
                    foreach (var scene in additive)
                        EditorGUILayout.LabelField("  " + scene, EditorStyles.miniLabel);
                }
            }

            EditorUtility.SetDirty(target);
        }
    }

    [CustomEditor(typeof(EOSNativeConsole))]
    public class EOSNativeConsoleEditor : UnityEditor.Editor
    {
        public override void OnInspectorGUI()
        {
            DrawDefaultInspector();

            var console = (EOSNativeConsole)target;

            EditorGUILayout.Space(5);
            EditorGUILayout.LabelField("Runtime Console", EditorStyles.boldLabel);

            if (!Application.isPlaying)
            {
                EditorGUILayout.HelpBox("Enter Play Mode to see console status.", MessageType.Info);
                return;
            }

            using (new EditorGUI.DisabledGroupScope(true))
            {
                EditorGUILayout.Toggle("Visible", console.IsVisible);
                EditorGUILayout.IntField("Total Entries", console.EntryCount);
                EditorGUILayout.IntField("Logs", console.LogCount);
                EditorGUILayout.IntField("Warnings", console.WarningCount);
                EditorGUILayout.IntField("Errors", console.ErrorCount);
            }

            EditorUtility.SetDirty(target);
        }
    }

    [CustomEditor(typeof(SyncVarLOD))]
    public class SyncVarLODEditor : UnityEditor.Editor
    {
        private bool[] _tierFoldouts = new bool[8];

        public override void OnInspectorGUI()
        {
            var lod = (SyncVarLOD)target;
            var netObj = lod.GetComponent<NetworkObject>();

            serializedObject.Update();

            EditorGUILayout.LabelField("SyncVar LOD", EditorStyles.boldLabel);

            // Auto observer toggle
            var autoObs = serializedObject.FindProperty("AutoObserverPosition");
            if (autoObs != null) EditorGUILayout.PropertyField(autoObs);

            // Tier list
            var tiersProp = serializedObject.FindProperty("Tiers");
            if (tiersProp != null)
            {
                EditorGUILayout.Space(5);
                EditorGUILayout.LabelField("Distance Tiers", EditorStyles.boldLabel);

                int syncVarCount = Application.isPlaying && netObj != null ? netObj.SyncVarCount : 0;

                for (int i = 0; i < tiersProp.arraySize; i++)
                {
                    if (i >= _tierFoldouts.Length) break;

                    var tier = tiersProp.GetArrayElementAtIndex(i);
                    var distProp = tier.FindPropertyRelative("MaxDistance");
                    var nthProp = tier.FindPropertyRelative("SyncEveryNthFrame");
                    var maskProp = tier.FindPropertyRelative("SyncVarMask");

                    string tierLabel = distProp != null ? $"Tier {i}: 0-{distProp.floatValue}m" : $"Tier {i}";
                    if (Application.isPlaying && lod.CurrentTier == i)
                        tierLabel += " [ACTIVE]";

                    _tierFoldouts[i] = EditorGUILayout.Foldout(_tierFoldouts[i], tierLabel, true);

                    if (_tierFoldouts[i])
                    {
                        EditorGUI.indentLevel++;

                        if (distProp != null) EditorGUILayout.PropertyField(distProp, new GUIContent("Max Distance"));
                        if (nthProp != null) EditorGUILayout.PropertyField(nthProp, new GUIContent("Sync Every Nth"));

                        // SyncVarMask: show as checkboxes in Play Mode if we have SyncVar info
                        if (maskProp != null)
                        {
                            if (Application.isPlaying && syncVarCount > 0)
                            {
                                EditorGUILayout.LabelField("SyncVar Mask", EditorStyles.miniBoldLabel);
                                uint mask = (uint)maskProp.intValue;
                                bool changed = false;

                                for (int sv = 0; sv < syncVarCount; sv++)
                                {
                                    bool enabled = (mask & (1u << sv)) != 0;
                                    bool newVal = EditorGUILayout.Toggle($"  SyncVar [{sv}]", enabled);
                                    if (newVal != enabled)
                                    {
                                        if (newVal)
                                            mask |= (1u << sv);
                                        else
                                            mask &= ~(1u << sv);
                                        changed = true;
                                    }
                                }

                                if (changed)
                                {
                                    maskProp.intValue = (int)mask;
                                    // Update runtime tier data
                                    if (i < lod.Tiers.Count)
                                    {
                                        var t = lod.Tiers[i];
                                        t.SyncVarMask = mask;
                                        lod.Tiers[i] = t;
                                    }
                                }
                            }
                            else
                            {
                                // Edit mode: show as hex
                                string hexStr = $"0x{(uint)maskProp.intValue:X8}";
                                string newHex = EditorGUILayout.TextField("SyncVar Mask", hexStr);
                                if (newHex != hexStr)
                                {
                                    if (newHex.StartsWith("0x") || newHex.StartsWith("0X"))
                                        newHex = newHex.Substring(2);
                                    if (uint.TryParse(newHex, System.Globalization.NumberStyles.HexNumber, null, out uint parsed))
                                        maskProp.intValue = (int)parsed;
                                }

                                EditorGUILayout.HelpBox("Enter Play Mode to see per-SyncVar checkboxes.", MessageType.None);
                            }
                        }

                        EditorGUI.indentLevel--;
                    }
                }

                EditorGUILayout.Space(3);
                EditorGUILayout.BeginHorizontal();
                if (GUILayout.Button("Add Tier"))
                {
                    tiersProp.InsertArrayElementAtIndex(tiersProp.arraySize);
                    var newTier = tiersProp.GetArrayElementAtIndex(tiersProp.arraySize - 1);
                    newTier.FindPropertyRelative("MaxDistance").floatValue = 200f;
                    newTier.FindPropertyRelative("SyncEveryNthFrame").intValue = 20;
                    newTier.FindPropertyRelative("SyncVarMask").intValue = unchecked((int)0xFFFFFFFF);
                }
                if (tiersProp.arraySize > 1 && GUILayout.Button("Remove Last"))
                    tiersProp.DeleteArrayElementAtIndex(tiersProp.arraySize - 1);
                EditorGUILayout.EndHorizontal();
            }

            // Runtime status
            if (Application.isPlaying)
            {
                EditorGUILayout.Space(5);
                EditorGUILayout.LabelField("Runtime Status", EditorStyles.boldLabel);

                using (new EditorGUI.DisabledGroupScope(true))
                {
                    string tierStr = lod.CurrentTier >= 0 ? $"Tier {lod.CurrentTier}" : "Culled";
                    EditorGUILayout.TextField("Active Tier", tierStr);
                    EditorGUILayout.IntField("Sync Rate", lod.CurrentSyncRate);
                    EditorGUILayout.TextField("SyncVar Mask", $"0x{lod.CurrentSyncVarMask:X8}");

                    if (netObj != null)
                    {
                        float dist = Vector3.Distance(lod.transform.position, lod.ObserverPosition);
                        EditorGUILayout.FloatField("Observer Distance", dist);
                        EditorGUILayout.IntField("SyncVar Count", netObj.SyncVarCount);
                    }
                }

                // Repaint during play mode to show live updates
                Repaint();
            }

            serializedObject.ApplyModifiedProperties();
        }
    }

    [CustomEditor(typeof(NetworkPrefabTable))]
    public class NetworkPrefabTableEditor : UnityEditor.Editor
    {
        private string _folderFilter;

        public override void OnInspectorGUI()
        {
            DrawDefaultInspector();

            var table = (NetworkPrefabTable)target;

            EditorGUILayout.Space(8);
            EditorGUILayout.LabelField("Auto-Collect", EditorStyles.boldLabel);

            EditorGUILayout.BeginHorizontal();
            _folderFilter = EditorGUILayout.TextField("Folder Filter", _folderFilter);
            if (GUILayout.Button("Browse", GUILayout.Width(60)))
            {
                var path = EditorUtility.OpenFolderPanel("Select Prefab Folder", "Assets", "");
                if (!string.IsNullOrEmpty(path))
                {
                    // Convert absolute to Assets-relative
                    if (path.Contains("Assets"))
                        _folderFilter = "Assets" + path.Substring(path.IndexOf("Assets") + 6);
                }
            }
            EditorGUILayout.EndHorizontal();

            EditorGUILayout.BeginHorizontal();
            if (GUILayout.Button("Collect All NetworkObject Prefabs"))
            {
                Undo.RecordObject(table, "Collect Prefabs");
                string filter = string.IsNullOrWhiteSpace(_folderFilter) ? null : _folderFilter;
                table.CollectAll(filter);
            }
            if (GUILayout.Button("Remove Nulls"))
            {
                Undo.RecordObject(table, "Remove Null Prefabs");
                table.RemoveNulls();
            }
            EditorGUILayout.EndHorizontal();

            EditorGUILayout.HelpBox(
                $"Table has {table.Count} entries. Index = PrefabId used by Spawn().\n" +
                "Collect scans for all prefabs with NetworkObject. Order is preserved for existing entries.",
                MessageType.Info);
        }
    }
}
