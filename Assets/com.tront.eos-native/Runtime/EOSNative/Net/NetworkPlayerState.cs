using System;
using System.Collections.Generic;
using Epic.OnlineServices;
using EOSNative.Logging;
using EOSNative.P2P;
using UnityEngine;

namespace EOSNative.Net
{
    /// <summary>
    /// Per-player NetworkObject syncing player-specific state across all peers.
    /// Each peer auto-creates their own on connect. DestroyWithOwner = true
    /// (destroyed when the owning player disconnects).
    ///
    /// Uses reserved PrefabId <see cref="PREFAB_ID"/> — recognized by NetworkManager,
    /// not in the prefab registry. Standard NetworkId generation (PUID partition).
    /// </summary>
    public class NetworkPlayerState : NetworkBehaviour
    {
        #region Constants

        /// <summary>Reserved PrefabId — not in the prefab registry. Recognized by NetworkManager.</summary>
        public const ushort PREFAB_ID = 0xFFF1;

        #endregion

        #region SyncVars (index 0-7)

        /// <summary>Display name of this player. Auto-populated from EOSPlayerRegistry on spawn.</summary>
        public SyncVar<string> DisplayName { get; private set; }

        /// <summary>Team index (0 = unassigned).</summary>
        public SyncVar<byte> Team { get; private set; }

        /// <summary>Whether this player is ready (e.g. in a pre-game lobby).</summary>
        public SyncVar<bool> IsReady { get; private set; }

        /// <summary>Player's score/kills.</summary>
        public SyncVar<int> Score { get; private set; }

        /// <summary>Player's death count.</summary>
        public SyncVar<int> Deaths { get; private set; }

        /// <summary>Player's assist count.</summary>
        public SyncVar<int> Assists { get; private set; }

        /// <summary>Loadout/class identifier string (game-specific).</summary>
        public SyncVar<string> Loadout { get; private set; }

        /// <summary>Player slot index (assigned by host for seat-based games).</summary>
        public SyncVar<byte> PlayerSlot { get; private set; }

        #endregion

        #region SyncDictionary (index 8)

        /// <summary>
        /// Dynamic per-player key-value data. Use <see cref="SetCustom"/> / <see cref="GetCustom"/>
        /// for convenience. Owner-writable only.
        /// </summary>
        public SyncDictionary<string, string> CustomData { get; private set; }

        #endregion

        #region Convenience API

        /// <summary>Set a custom player property (owner only).</summary>
        public void SetCustom(string key, string value)
        {
            if (!IsOwner) return;
            CustomData[key] = value;
        }

        /// <summary>Get a custom player property, or defaultValue if not set.</summary>
        public string GetCustom(string key, string defaultValue = null)
        {
            return CustomData.TryGetValue(key, out var val) ? val : defaultValue;
        }

        /// <summary>Get a custom player property as int.</summary>
        public int GetCustomInt(string key, int defaultValue = 0)
        {
            return CustomData.TryGetValue(key, out var val) && int.TryParse(val, out int result) ? result : defaultValue;
        }

        /// <summary>Get a custom player property as float.</summary>
        public float GetCustomFloat(string key, float defaultValue = 0f)
        {
            return CustomData.TryGetValue(key, out var val) && float.TryParse(val, out float result) ? result : defaultValue;
        }

        /// <summary>Get a custom player property as bool ("true"/"1" = true).</summary>
        public bool GetCustomBool(string key, bool defaultValue = false)
        {
            if (!CustomData.TryGetValue(key, out var val)) return defaultValue;
            return val == "true" || val == "1" || val == "True";
        }

        /// <summary>Remove a custom player property (owner only).</summary>
        public void RemoveCustom(string key)
        {
            if (!IsOwner) return;
            CustomData.Remove(key);
        }

        /// <summary>True if this player is a spectator (read-only observer).</summary>
        public bool IsSpectating => GetCustomBool("_spectator");

        /// <summary>K/D ratio (0 if no deaths).</summary>
        public float KDRatio => Deaths.Value > 0 ? (float)Score.Value / Deaths.Value : Score.Value;

        /// <summary>Reset score/deaths/assists to zero (owner only).</summary>
        public void ResetStats()
        {
            if (!IsOwner) return;
            Score.Value = 0;
            Deaths.Value = 0;
            Assists.Value = 0;
        }

        #endregion

        #region Lifecycle

        protected override void Awake()
        {
            base.Awake();

            // Index 0-7: typed SyncVars
            DisplayName = Sync(string.Empty);
            Team = Sync<byte>(0);
            IsReady = Sync(false);
            Score = Sync(0);
            Deaths = Sync(0);
            Assists = Sync(0);
            Loadout = Sync(string.Empty);
            PlayerSlot = Sync<byte>(0);

            // Index 8: dynamic custom data
            CustomData = SyncDictionary<string, string>();

            Net.DestroyWithOwner = true; // Destroyed when player disconnects
        }

        /// <summary>
        /// Initialize display name from EOSPlayerRegistry. Called by NetworkManager after spawn.
        /// </summary>
        internal void AutoInitDisplayName()
        {
            if (!IsOwner) return;

            var puid = EOSManager.Instance?.LocalProductUserId;
            if (puid == null) return;

            var registry = EOSPlayerRegistry.Instance;
            if (registry != null)
            {
                string name = registry.GetPlayerName(puid.ToString());
                if (!string.IsNullOrEmpty(name))
                    DisplayName.Value = name;
            }
        }

        #endregion
    }
}
