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
        public override void OnInspectorGUI()
        {
            DrawDefaultInspector();

            var obj = (NetworkObject)target;

            EditorGUILayout.Space(5);
            EditorGUILayout.LabelField("Network Identity", EditorStyles.boldLabel);

            if (!Application.isPlaying)
            {
                EditorGUILayout.HelpBox("Enter Play Mode to see network identity.", MessageType.Info);
                return;
            }

            using (new EditorGUI.DisabledGroupScope(true))
            {
                EditorGUILayout.TextField("Network ID", obj.IsRegistered ? $"0x{obj.NetworkId:X8}" : "(unregistered)");
                EditorGUILayout.TextField("Prefab ID", $"0x{obj.PrefabId:X4}");
                EditorGUILayout.TextField("Owner", obj.OwnerId?.ToString() ?? "(none)");
                EditorGUILayout.Toggle("Is Owner", obj.IsOwner);
                EditorGUILayout.Toggle("Is Host", obj.IsHost);
                EditorGUILayout.Toggle("Is Registered", obj.IsRegistered);
                EditorGUILayout.Toggle("Destroy With Owner", obj.DestroyWithOwner);
                EditorGUILayout.IntField("SyncVar Count", obj.SyncVarCount);
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
}
