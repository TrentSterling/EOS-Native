using System;
using System.Collections;
using System.Collections.Generic;
using Epic.OnlineServices;
using Epic.OnlineServices.P2P;
using EOSNative.Logging;
using EOSNative.P2P;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace EOSNative.Net
{
    /// <summary>
    /// Manages networked scene loading. Host calls <see cref="LoadScene"/> /
    /// <see cref="LoadSceneAdditive"/> / <see cref="UnloadScene"/> and all peers follow.
    ///
    /// Scene info is stored on <see cref="NetworkRoomState"/> properties so late joiners
    /// can load the correct scenes after receiving the snapshot.
    ///
    /// Max 8 additive scenes (matching Fusion).
    /// </summary>
    public class NetworkSceneManager : MonoBehaviour
    {
        #region Constants

        internal const byte MSG_SCENE_LOAD = 0xAA;
        internal const byte MSG_SCENE_UNLOAD = 0xAB;
        internal const byte MSG_SCENE_LOADED_ACK = 0xAC;

        private const int MAX_ADDITIVE_SCENES = 8;

        #endregion

        #region Singleton

        private static NetworkSceneManager _instance;
        public static NetworkSceneManager Instance
        {
            get
            {
                if (_instance == null)
                {
                    _instance = FindAnyObjectByType<NetworkSceneManager>();
                    if (_instance == null)
                    {
                        var go = new GameObject("NetworkSceneManager");
                        if (EOSManager.Instance != null)
                            go.transform.SetParent(EOSManager.Instance.transform);
                        else
                            DontDestroyOnLoad(go);
                        _instance = go.AddComponent<NetworkSceneManager>();
                    }
                }
                return _instance;
            }
        }

        #endregion

        #region Events

        /// <summary>Fired on all peers when a scene finishes loading locally. Arg: scene name.</summary>
        public event Action<string> OnSceneLoadCompleted;

        /// <summary>Fired on all peers when a scene is unloaded locally. Arg: scene name.</summary>
        public event Action<string> OnSceneUnloaded;

        /// <summary>
        /// Fired on the host when ALL connected peers have acknowledged loading the scene.
        /// Use this to start the game round after everyone is loaded.
        /// </summary>
        public event Action OnAllPeersLoaded;

        /// <summary>True while a scene load is in progress.</summary>
        public bool IsLoading { get; private set; }

        /// <summary>The name of the scene currently being loaded (null if not loading).</summary>
        public string LoadingSceneName { get; private set; }

        #endregion

        #region Public API

        /// <summary>
        /// Load a scene on all peers (single mode — replaces current scene).
        /// Host only. NetworkObjects in DontDestroyOnLoad (including RoomState/PlayerStates) persist.
        /// </summary>
        public void LoadScene(string sceneName)
        {
            if (!NetworkManager.Instance.IsHost)
            {
                Debug.LogWarning("[NetworkSceneManager] Only the host can load scenes");
                return;
            }

            if (IsLoading)
            {
                Debug.LogWarning("[NetworkSceneManager] Already loading a scene");
                return;
            }

            EOSDebugLogger.Log(DebugCategory.EOSManager, "NetworkSceneManager",
                $"Loading scene: {sceneName}");

            // Update room state
            var room = NetworkManager.Instance.RoomState;
            if (room != null && room.IsOwner)
            {
                room.SetProperty(NetworkRoomState.SCENE_KEY, sceneName);
                // Clear additive scenes on full load
                room.SetProperty(NetworkRoomState.ADDITIVE_SCENE_COUNT_KEY, "0");
            }

            // Clear pending ACKs
            _pendingAcks.Clear();
            var peers = EOSP2PManager.Instance?.Peers;
            if (peers != null)
            {
                foreach (var peer in peers)
                    _pendingAcks.Add(peer);
            }

            // Broadcast to all peers
            var writer = NetWriterPool.Get();
            writer.WriteString(sceneName);
            writer.WriteBool(false); // additive = false
            EOSP2PManager.Instance.Router.SendToAll(MSG_SCENE_LOAD, writer, PacketReliability.ReliableOrdered, 1);
            NetWriterPool.Return(writer);

            // Load locally
            StartCoroutine(LoadSceneCoroutine(sceneName, LoadSceneMode.Single));
        }

        /// <summary>
        /// Load a scene additively on all peers (adds to current scene).
        /// Host only. Max 8 additive scenes.
        /// </summary>
        public void LoadSceneAdditive(string sceneName)
        {
            if (!NetworkManager.Instance.IsHost)
            {
                Debug.LogWarning("[NetworkSceneManager] Only the host can load scenes");
                return;
            }

            if (IsLoading)
            {
                Debug.LogWarning("[NetworkSceneManager] Already loading a scene");
                return;
            }

            // Check additive limit
            var room = NetworkManager.Instance.RoomState;
            if (room != null)
            {
                int count = room.GetPropertyInt(NetworkRoomState.ADDITIVE_SCENE_COUNT_KEY, 0);
                if (count >= MAX_ADDITIVE_SCENES)
                {
                    Debug.LogWarning($"[NetworkSceneManager] Max additive scenes ({MAX_ADDITIVE_SCENES}) reached");
                    return;
                }

                if (room.IsOwner)
                {
                    room.SetProperty(NetworkRoomState.ADDITIVE_SCENE_PREFIX + count, sceneName);
                    room.SetProperty(NetworkRoomState.ADDITIVE_SCENE_COUNT_KEY, (count + 1).ToString());
                }
            }

            EOSDebugLogger.Log(DebugCategory.EOSManager, "NetworkSceneManager",
                $"Loading additive scene: {sceneName}");

            // Clear pending ACKs
            _pendingAcks.Clear();
            var peers = EOSP2PManager.Instance?.Peers;
            if (peers != null)
            {
                foreach (var peer in peers)
                    _pendingAcks.Add(peer);
            }

            // Broadcast
            var writer = NetWriterPool.Get();
            writer.WriteString(sceneName);
            writer.WriteBool(true); // additive = true
            EOSP2PManager.Instance.Router.SendToAll(MSG_SCENE_LOAD, writer, PacketReliability.ReliableOrdered, 1);
            NetWriterPool.Return(writer);

            // Load locally
            StartCoroutine(LoadSceneCoroutine(sceneName, LoadSceneMode.Additive));
        }

        /// <summary>
        /// Unload an additive scene on all peers. Host only.
        /// </summary>
        public void UnloadScene(string sceneName)
        {
            if (!NetworkManager.Instance.IsHost)
            {
                Debug.LogWarning("[NetworkSceneManager] Only the host can unload scenes");
                return;
            }

            EOSDebugLogger.Log(DebugCategory.EOSManager, "NetworkSceneManager",
                $"Unloading scene: {sceneName}");

            // Remove from room state additive scenes
            var room = NetworkManager.Instance.RoomState;
            if (room != null && room.IsOwner)
            {
                int count = room.GetPropertyInt(NetworkRoomState.ADDITIVE_SCENE_COUNT_KEY, 0);
                var scenes = new List<string>();
                for (int i = 0; i < count; i++)
                {
                    var name = room.GetProperty(NetworkRoomState.ADDITIVE_SCENE_PREFIX + i);
                    if (!string.IsNullOrEmpty(name) && name != sceneName)
                        scenes.Add(name);
                }

                // Rewrite compacted list
                for (int i = 0; i < scenes.Count; i++)
                    room.SetProperty(NetworkRoomState.ADDITIVE_SCENE_PREFIX + i, scenes[i]);
                // Clear old trailing entries
                for (int i = scenes.Count; i < count; i++)
                    room.RemoveProperty(NetworkRoomState.ADDITIVE_SCENE_PREFIX + i);
                room.SetProperty(NetworkRoomState.ADDITIVE_SCENE_COUNT_KEY, scenes.Count.ToString());
            }

            // Broadcast
            var writer = NetWriterPool.Get();
            writer.WriteString(sceneName);
            EOSP2PManager.Instance.Router.SendToAll(MSG_SCENE_UNLOAD, writer, PacketReliability.ReliableOrdered, 1);
            NetWriterPool.Return(writer);

            // Unload locally
            StartCoroutine(UnloadSceneCoroutine(sceneName));
        }

        /// <summary>
        /// Called by NetworkManager after a late-joiner receives their snapshot.
        /// Reads scene info from RoomState and loads the correct scenes.
        /// </summary>
        internal void SyncScenesFromRoomState()
        {
            var room = NetworkManager.Instance.RoomState;
            if (room == null) return;

            // Load the active scene if different from current
            string activeScene = room.ActiveScene;
            if (!string.IsNullOrEmpty(activeScene))
            {
                var currentScene = SceneManager.GetActiveScene();
                if (currentScene.name != activeScene)
                {
                    EOSDebugLogger.Log(DebugCategory.EOSManager, "NetworkSceneManager",
                        $"Late join: loading active scene '{activeScene}'");
                    StartCoroutine(LoadSceneCoroutine(activeScene, LoadSceneMode.Single));
                }
            }

            // Load any additive scenes
            var additiveScenes = room.AdditiveScenes;
            foreach (var sceneName in additiveScenes)
            {
                // Check if already loaded
                bool alreadyLoaded = false;
                for (int i = 0; i < SceneManager.sceneCount; i++)
                {
                    if (SceneManager.GetSceneAt(i).name == sceneName)
                    {
                        alreadyLoaded = true;
                        break;
                    }
                }

                if (!alreadyLoaded)
                {
                    EOSDebugLogger.Log(DebugCategory.EOSManager, "NetworkSceneManager",
                        $"Late join: loading additive scene '{sceneName}'");
                    StartCoroutine(LoadSceneCoroutine(sceneName, LoadSceneMode.Additive));
                }
            }
        }

        #endregion

        #region Private Fields

        private readonly HashSet<ProductUserId> _pendingAcks = new();

        #endregion

        #region Unity Lifecycle

        private void Awake()
        {
            if (_instance != null && _instance != this)
            {
                Destroy(gameObject);
                return;
            }
            _instance = this;
            if (transform.parent == null)
                DontDestroyOnLoad(gameObject);
        }

        #endregion

        #region Message Handlers

        internal void HandleSceneLoad(ProductUserId sender, NetReader reader)
        {
            string sceneName = reader.ReadString();
            bool additive = reader.ReadBool();

            EOSDebugLogger.Log(DebugCategory.EOSManager, "NetworkSceneManager",
                $"Received scene load request: {sceneName} (additive={additive})");

            var mode = additive ? LoadSceneMode.Additive : LoadSceneMode.Single;
            StartCoroutine(LoadSceneCoroutine(sceneName, mode, sendAck: true));
        }

        internal void HandleSceneUnload(ProductUserId sender, NetReader reader)
        {
            string sceneName = reader.ReadString();

            EOSDebugLogger.Log(DebugCategory.EOSManager, "NetworkSceneManager",
                $"Received scene unload request: {sceneName}");

            StartCoroutine(UnloadSceneCoroutine(sceneName));
        }

        internal void HandleSceneLoadedAck(ProductUserId sender, NetReader reader)
        {
            string sceneName = reader.ReadString();
            _pendingAcks.Remove(sender);

            EOSDebugLogger.Log(DebugCategory.EOSManager, "NetworkSceneManager",
                $"Scene loaded ACK from {sender} for '{sceneName}' ({_pendingAcks.Count} remaining)");

            if (_pendingAcks.Count == 0)
            {
                EOSDebugLogger.Log(DebugCategory.EOSManager, "NetworkSceneManager",
                    "All peers loaded — firing OnAllPeersLoaded");
                OnAllPeersLoaded?.Invoke();
            }
        }

        #endregion

        #region Coroutines

        private IEnumerator LoadSceneCoroutine(string sceneName, LoadSceneMode mode, bool sendAck = false)
        {
            IsLoading = true;
            LoadingSceneName = sceneName;

            var op = SceneManager.LoadSceneAsync(sceneName, mode);
            if (op == null)
            {
                Debug.LogError($"[NetworkSceneManager] Failed to start loading scene: {sceneName}");
                IsLoading = false;
                LoadingSceneName = null;
                yield break;
            }

            while (!op.isDone)
                yield return null;

            IsLoading = false;
            LoadingSceneName = null;

            // Register scene objects after load
            NetworkManager.Instance?.RegisterSceneObjects();

            EOSDebugLogger.Log(DebugCategory.EOSManager, "NetworkSceneManager",
                $"Scene loaded: {sceneName}");

            OnSceneLoadCompleted?.Invoke(sceneName);

            // Send ACK to host if we're not the host
            if (sendAck && !NetworkManager.Instance.IsHost)
            {
                var writer = NetWriterPool.Get();
                writer.WriteString(sceneName);
                var hostPuid = GetHostPuid();
                if (hostPuid != null)
                    EOSP2PManager.Instance.Router.SendToPeer(MSG_SCENE_LOADED_ACK, writer, hostPuid, PacketReliability.ReliableOrdered, 1);
                NetWriterPool.Return(writer);
            }
        }

        private IEnumerator UnloadSceneCoroutine(string sceneName)
        {
            // Find the scene
            Scene scene = default;
            bool found = false;
            for (int i = 0; i < SceneManager.sceneCount; i++)
            {
                var s = SceneManager.GetSceneAt(i);
                if (s.name == sceneName)
                {
                    scene = s;
                    found = true;
                    break;
                }
            }

            if (!found)
            {
                Debug.LogWarning($"[NetworkSceneManager] Scene '{sceneName}' not found to unload");
                yield break;
            }

            var op = SceneManager.UnloadSceneAsync(scene);
            if (op == null)
            {
                Debug.LogError($"[NetworkSceneManager] Failed to start unloading scene: {sceneName}");
                yield break;
            }

            while (!op.isDone)
                yield return null;

            EOSDebugLogger.Log(DebugCategory.EOSManager, "NetworkSceneManager",
                $"Scene unloaded: {sceneName}");

            OnSceneUnloaded?.Invoke(sceneName);
        }

        #endregion

        #region Utilities

        private ProductUserId GetHostPuid()
        {
            var localPuid = EOSManager.Instance?.LocalProductUserId;
            if (localPuid == null) return null;

            string lowestStr = localPuid.ToString();
            ProductUserId lowestPuid = localPuid;

            var peers = EOSP2PManager.Instance?.Peers;
            if (peers != null)
            {
                foreach (var peer in peers)
                {
                    string peerStr = peer.ToString();
                    if (string.Compare(peerStr, lowestStr, StringComparison.Ordinal) < 0)
                    {
                        lowestStr = peerStr;
                        lowestPuid = peer;
                    }
                }
            }

            return lowestPuid;
        }

        #endregion
    }
}
