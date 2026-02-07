using System;
using System.Collections.Generic;
using EOSNative.Logging;
using EOSNative.P2P;
using UnityEngine;

namespace EOSNative.Net
{
    /// <summary>
    /// Singleton NetworkObject representing the shared room/game state.
    /// Host auto-creates with well-known ID <see cref="WELL_KNOWN_ID"/> and reserved
    /// PrefabId <see cref="PREFAB_ID"/>. Survives host migration (DestroyWithOwner = false).
    ///
    /// Provides typed SyncVars for common game state and a <see cref="Properties"/>
    /// SyncDictionary for arbitrary key-value data. Optionally mirrors configured keys
    /// to EOS lobby attributes for search/matchmaking.
    /// </summary>
    public class NetworkRoomState : NetworkBehaviour
    {
        #region Constants

        /// <summary>Well-known NetworkId for the room state object.</summary>
        public const uint WELL_KNOWN_ID = 0xFFFF0001u;

        /// <summary>Reserved PrefabId — not in the prefab registry. Recognized by NetworkManager.</summary>
        public const ushort PREFAB_ID = 0xFFF0;

        #endregion

        #region SyncVars (index 0-7)

        /// <summary>Current game mode (e.g. "deathmatch", "ctf"). Mirrors to lobby attribute.</summary>
        public SyncVar<string> GameMode { get; private set; }

        /// <summary>Current map/level name. Mirrors to lobby attribute.</summary>
        public SyncVar<string> MapName { get; private set; }

        /// <summary>Current round number (0 = first round).</summary>
        public SyncVar<int> RoundNumber { get; private set; }

        /// <summary>Current number of players.</summary>
        public SyncVar<int> PlayerCount { get; private set; }

        /// <summary>Maximum number of players allowed.</summary>
        public SyncVar<int> MaxPlayers { get; private set; }

        /// <summary>Round timer in seconds (host-driven, counts down or up as needed).</summary>
        public SyncVar<float> RoundTimer { get; private set; }

        /// <summary>
        /// Current game phase. 0=Lobby, 1=Loading, 2=Playing, 3=PostMatch.
        /// Use <see cref="GamePhase"/> enum for readability.
        /// </summary>
        public SyncVar<byte> Phase { get; private set; }

        /// <summary>Whether a game is currently in progress. Mirrors to lobby attribute.</summary>
        public SyncVar<bool> IsInProgress { get; private set; }

        #endregion

        #region SyncDictionary (index 8)

        /// <summary>
        /// Dynamic key-value properties for custom room data.
        /// Use <see cref="SetProperty"/> / <see cref="GetProperty"/> for convenience.
        /// </summary>
        public SyncDictionary<string, string> Properties { get; private set; }

        #endregion

        #region Convenience API

        /// <summary>Set a custom room property (host only).</summary>
        public void SetProperty(string key, string value)
        {
            if (!IsOwner) return;
            Properties[key] = value;
        }

        /// <summary>Get a custom room property, or defaultValue if not set.</summary>
        public string GetProperty(string key, string defaultValue = null)
        {
            return Properties.TryGetValue(key, out var val) ? val : defaultValue;
        }

        /// <summary>Get a custom room property as int.</summary>
        public int GetPropertyInt(string key, int defaultValue = 0)
        {
            return Properties.TryGetValue(key, out var val) && int.TryParse(val, out int result) ? result : defaultValue;
        }

        /// <summary>Get a custom room property as float.</summary>
        public float GetPropertyFloat(string key, float defaultValue = 0f)
        {
            return Properties.TryGetValue(key, out var val) && float.TryParse(val, out float result) ? result : defaultValue;
        }

        /// <summary>Get a custom room property as bool ("true"/"1" = true).</summary>
        public bool GetPropertyBool(string key, bool defaultValue = false)
        {
            if (!Properties.TryGetValue(key, out var val)) return defaultValue;
            return val == "true" || val == "1" || val == "True";
        }

        /// <summary>Remove a custom room property (host only).</summary>
        public void RemoveProperty(string key)
        {
            if (!IsOwner) return;
            Properties.Remove(key);
        }

        /// <summary>Get the current game phase as the typed enum.</summary>
        public GamePhase CurrentPhase
        {
            get => (GamePhase)Phase.Value;
            set { if (IsOwner) Phase.Value = (byte)value; }
        }

        #endregion

        #region Lobby Attribute Mirroring

        /// <summary>
        /// Keys that should be mirrored to EOS lobby attributes for matchmaking/search.
        /// Add keys here and they will be pushed to lobby attributes when changed.
        /// GameMode, MapName, and IsInProgress are mirrored automatically.
        /// </summary>
        public readonly HashSet<string> SearchablePropertyKeys = new();

        private float _lastMirrorTime;
        private const float MIRROR_INTERVAL = 1f; // Rate limit: 1 push per second
        private bool _mirrorDirty;

        private void MirrorToLobby()
        {
            if (!IsOwner) return;
            if (!_mirrorDirty) return;
            if (Time.time - _lastMirrorTime < MIRROR_INTERVAL) return;

            _lastMirrorTime = Time.time;
            _mirrorDirty = false;

            var lobbyMgr = EOSNative.Lobbies.EOSLobbyManager.Instance;
            if (lobbyMgr == null || !lobbyMgr.IsInLobby) return;

            string lobbyId = lobbyMgr.CurrentLobby.LobbyId;
            if (string.IsNullOrEmpty(lobbyId)) return;

            // Mirror built-in searchable fields
            _ = lobbyMgr.SetLobbyAttributeAsync(lobbyId, "gamemode", GameMode.Value ?? string.Empty);
            _ = lobbyMgr.SetLobbyAttributeAsync(lobbyId, "map", MapName.Value ?? string.Empty);
            _ = lobbyMgr.SetLobbyAttributeAsync(lobbyId, "in_progress", IsInProgress.Value ? "1" : "0");

            // Mirror configured custom property keys
            foreach (var key in SearchablePropertyKeys)
            {
                if (Properties.TryGetValue(key, out var val))
                    _ = lobbyMgr.SetLobbyAttributeAsync(lobbyId, key, val);
            }
        }

        private void MarkMirrorDirty()
        {
            _mirrorDirty = true;
        }

        #endregion

        #region Scene Properties (used by NetworkSceneManager)

        /// <summary>Property key for the active scene name. Written by NetworkSceneManager.</summary>
        internal const string SCENE_KEY = "_scene";

        /// <summary>Property key prefix for additive scenes. Written by NetworkSceneManager.</summary>
        internal const string ADDITIVE_SCENE_PREFIX = "_addScene_";

        /// <summary>Property key for additive scene count. Written by NetworkSceneManager.</summary>
        internal const string ADDITIVE_SCENE_COUNT_KEY = "_addSceneCount";

        /// <summary>Get the active scene name stored in room state.</summary>
        public string ActiveScene => GetProperty(SCENE_KEY);

        /// <summary>Get the list of additive scenes stored in room state.</summary>
        public List<string> AdditiveScenes
        {
            get
            {
                var list = new List<string>();
                int count = GetPropertyInt(ADDITIVE_SCENE_COUNT_KEY, 0);
                for (int i = 0; i < count; i++)
                {
                    var name = GetProperty(ADDITIVE_SCENE_PREFIX + i);
                    if (!string.IsNullOrEmpty(name))
                        list.Add(name);
                }
                return list;
            }
        }

        #endregion

        #region Lifecycle

        protected override void Awake()
        {
            base.Awake();

            // Index 0-7: typed SyncVars
            GameMode = Sync(string.Empty);
            MapName = Sync(string.Empty);
            RoundNumber = Sync(0);
            PlayerCount = Sync(0);
            MaxPlayers = Sync(16);
            RoundTimer = Sync(0f);
            Phase = Sync<byte>(0);
            IsInProgress = Sync(false);

            // Index 8: dynamic properties
            Properties = SyncDictionary<string, string>();

            // Mark mirror dirty when searchable fields change
            GameMode.OnChanged += (_, __) => MarkMirrorDirty();
            MapName.OnChanged += (_, __) => MarkMirrorDirty();
            IsInProgress.OnChanged += (_, __) => MarkMirrorDirty();
            Properties.OnChanged += (_, __, ___, ____) => MarkMirrorDirty();

            Net.DestroyWithOwner = false; // Survives host migration
        }

        private void Update()
        {
            MirrorToLobby();
        }

        #endregion
    }

    /// <summary>Game phase enum for readability. Stored as byte in the Phase SyncVar.</summary>
    public enum GamePhase : byte
    {
        Lobby = 0,
        Loading = 1,
        Playing = 2,
        PostMatch = 3,
    }
}
