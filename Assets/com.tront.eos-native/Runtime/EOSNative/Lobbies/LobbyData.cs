using System;
using System.Collections.Generic;
using Epic.OnlineServices;
using Epic.OnlineServices.Lobby;

namespace EOSNative.Lobbies
{
    /// <summary>
    /// Lightweight lobby data container.
    /// Use this instead of holding onto LobbyDetails handles.
    /// </summary>
    [Serializable]
    public struct LobbyData
    {
        /// <summary>
        /// The EOS lobby ID (long random string).
        /// </summary>
        public string LobbyId;

        /// <summary>
        /// Our custom numeric join code.
        /// </summary>
        public string JoinCode;

        /// <summary>
        /// The lobby owner's ProductUserId.
        /// </summary>
        public string OwnerPuid;

        /// <summary>
        /// Current number of members in the lobby.
        /// </summary>
        public int MemberCount;

        /// <summary>
        /// Maximum allowed members.
        /// </summary>
        public int MaxMembers;

        /// <summary>
        /// Available slots (MaxMembers - MemberCount).
        /// </summary>
        public int AvailableSlots;

        /// <summary>
        /// Whether the lobby is publicly searchable.
        /// </summary>
        public bool IsPublic;

        /// <summary>
        /// The bucket ID for version matching.
        /// </summary>
        public string BucketId;

        /// <summary>
        /// Custom attributes on the lobby.
        /// </summary>
        public Dictionary<string, string> Attributes;

        #region Computed Properties

        /// <summary>
        /// Whether this lobby data is valid.
        /// </summary>
        public bool IsValid => !string.IsNullOrEmpty(LobbyId) && !string.IsNullOrEmpty(OwnerPuid);

        /// <summary>
        /// Whether we can join this lobby (has available slots).
        /// </summary>
        public bool CanJoin => AvailableSlots > 0;

        /// <summary>
        /// Whether this lobby is password protected.
        /// </summary>
        public bool IsPasswordProtected => HasAttribute(LobbyAttributes.PASSWORD);

        /// <summary>
        /// Whether the game is in progress.
        /// </summary>
        public bool IsInProgress => GetAttribute(LobbyAttributes.IN_PROGRESS) == "true";

        /// <summary>
        /// Whether this is a ranked/competitive lobby.
        /// </summary>
        public bool IsRanked => GameMode == "ranked" || HasAttribute(LobbyAttributes.SKILL_LEVEL);

        #endregion

        #region Typed Attribute Accessors

        /// <summary>
        /// Human-readable lobby name.
        /// </summary>
        public string LobbyName => GetAttribute(LobbyAttributes.LOBBY_NAME);

        /// <summary>
        /// Game mode (e.g., "deathmatch", "coop").
        /// </summary>
        public string GameMode => GetAttribute(LobbyAttributes.GAME_MODE);

        /// <summary>
        /// Current map name.
        /// </summary>
        public string Map => GetAttribute(LobbyAttributes.MAP);

        /// <summary>
        /// Server region.
        /// </summary>
        public string Region => GetAttribute(LobbyAttributes.REGION);

        /// <summary>
        /// Game version.
        /// </summary>
        public string Version => GetAttribute(LobbyAttributes.VERSION);

        /// <summary>
        /// Skill level as integer (0 if not set).
        /// </summary>
        public int SkillLevel => int.TryParse(GetAttribute(LobbyAttributes.SKILL_LEVEL), out int level) ? level : 0;

        #endregion

        #region Attribute Helpers

        /// <summary>
        /// Gets an attribute value, or null if not found.
        /// </summary>
        public string GetAttribute(string key)
        {
            if (Attributes == null) return null;
            return Attributes.TryGetValue(key, out string value) ? value : null;
        }

        /// <summary>
        /// Gets an attribute value with a default fallback.
        /// </summary>
        public string GetAttribute(string key, string defaultValue)
        {
            return GetAttribute(key) ?? defaultValue;
        }

        /// <summary>
        /// Checks if an attribute exists and has a non-empty value.
        /// </summary>
        public bool HasAttribute(string key)
        {
            return !string.IsNullOrEmpty(GetAttribute(key));
        }

        #endregion

        /// <summary>
        /// Resets all lobby data to default values.
        /// </summary>
        public void Clear()
        {
            LobbyId = null;
            JoinCode = null;
            OwnerPuid = null;
            MemberCount = 0;
            MaxMembers = 0;
            AvailableSlots = 0;
            IsPublic = false;
            BucketId = null;
            Attributes?.Clear();
        }

        public override string ToString()
        {
            return $"Lobby[{JoinCode}] Owner:{OwnerPuid?.Substring(0, 8)}... Members:{MemberCount}/{MaxMembers}";
        }
    }

    /// <summary>
    /// Lobby member data.
    /// </summary>
    [Serializable]
    public struct LobbyMemberData
    {
        /// <summary>
        /// The member's ProductUserId.
        /// </summary>
        public string Puid;

        /// <summary>
        /// The member's display name.
        /// </summary>
        public string DisplayName;

        /// <summary>
        /// Whether this member is the lobby owner.
        /// </summary>
        public bool IsOwner;

        /// <summary>
        /// Custom attributes on this member.
        /// </summary>
        public Dictionary<string, string> Attributes;

        public override string ToString()
        {
            return $"{DisplayName} ({Puid?.Substring(0, 8)}...){(IsOwner ? " [Owner]" : "")}";
        }
    }

    /// <summary>
    /// Options for creating a lobby.
    /// </summary>
    public class LobbyCreateOptions
    {
        /// <summary>
        /// Maximum number of players (default: 4).
        /// </summary>
        public uint MaxPlayers = 4;

        /// <summary>
        /// Whether the lobby is publicly searchable (default: true).
        /// </summary>
        public bool IsPublic = true;

        /// <summary>
        /// Version bucket for matchmaking (default: "v1").
        /// Lobbies with different buckets won't see each other.
        /// </summary>
        public string BucketId = "v1";

        /// <summary>
        /// Custom numeric join code. If null, one will be generated.
        /// </summary>
        public string JoinCode = null;

        /// <summary>
        /// Length of auto-generated join code (4-8 digits). Null = use EOSLobbyManager default (6).
        /// </summary>
        public int? JoinCodeLength = null;

        /// <summary>
        /// Use EOS-generated LobbyId as the join code instead of a random numeric code.
        /// </summary>
        public bool UseEosLobbyId = false;

        /// <summary>
        /// Whether to allow host migration (default: true).
        /// When enabled, EOS automatically promotes a new host if the owner leaves.
        /// </summary>
        public bool AllowHostMigration = true;

        /// <summary>
        /// Enable voice chat (RTC room) for this lobby (default: true).
        /// When enabled, all lobby members automatically join a voice room.
        /// Voice persists through host migration.
        /// </summary>
        public bool EnableVoice = true;

        /// <summary>
        /// Start with microphone muted (default: false).
        /// Only applies if EnableVoice is true.
        /// </summary>
        public bool StartMuted = false;

        #region Crossplay Settings

        /// <summary>
        /// Allow crossplay with other platforms (default: true).
        /// When false, only players on the same platform can join.
        /// </summary>
        public bool AllowCrossplay = true;

        /// <summary>
        /// Specific platform IDs to allow. If null, all platforms allowed.
        /// Use EOSPlatformHelper.AllPlatformIds, DesktopPlatformIds, or MobilePlatformIds.
        /// Platform IDs: "WIN" (Windows), "MAC" (macOS), "LNX" (Linux),
        ///               "AND" (Android), "IOS" (iOS), "OVR" (Quest/Meta VR)
        /// </summary>
        public string[] AllowedPlatformIds = null;

        /// <summary>
        /// If true, automatically set AllowedPlatformIds based on host platform.
        /// Desktop hosts allow desktop only, mobile hosts allow mobile only.
        /// Ignored if AllowedPlatformIds is explicitly set.
        /// </summary>
        public bool MatchPlatformType = false;

        #endregion

        #region Common Attributes (Convenience Properties)

        /// <summary>
        /// Human-readable lobby name (e.g., "Trent's Room").
        /// </summary>
        public string LobbyName = null;

        /// <summary>
        /// Game mode (e.g., "deathmatch", "coop", "ranked").
        /// </summary>
        public string GameMode = null;

        /// <summary>
        /// Current map name.
        /// </summary>
        public string Map = null;

        /// <summary>
        /// Server region (e.g., "us-east", "eu-west").
        /// </summary>
        public string Region = null;

        /// <summary>
        /// Password for the lobby. If set, lobby is password-protected.
        /// Stored as a hash/marker, not the actual password.
        /// </summary>
        public string Password = null;

        /// <summary>
        /// Skill level for matchmaking (optional).
        /// </summary>
        public int? SkillLevel = null;

        #endregion

        /// <summary>
        /// Additional custom attributes to set on the lobby.
        /// </summary>
        public Dictionary<string, string> Attributes = null;

        /// <summary>
        /// Builds the complete attributes dictionary including convenience properties.
        /// </summary>
        public Dictionary<string, string> BuildAttributes()
        {
            var attrs = Attributes != null
                ? new Dictionary<string, string>(Attributes)
                : new Dictionary<string, string>();

            // Add convenience properties
            if (!string.IsNullOrEmpty(LobbyName))
                attrs[LobbyAttributes.LOBBY_NAME] = LobbyName;
            if (!string.IsNullOrEmpty(GameMode))
                attrs[LobbyAttributes.GAME_MODE] = GameMode;
            if (!string.IsNullOrEmpty(Map))
                attrs[LobbyAttributes.MAP] = Map;
            if (!string.IsNullOrEmpty(Region))
                attrs[LobbyAttributes.REGION] = Region;
            if (!string.IsNullOrEmpty(Password))
                attrs[LobbyAttributes.PASSWORD] = Password.GetHashCode().ToString(); // Store hash, not password
            if (SkillLevel.HasValue)
                attrs[LobbyAttributes.SKILL_LEVEL] = SkillLevel.Value.ToString();

            return attrs;
        }
    }

    /// <summary>
    /// Options for searching lobbies with fluent filter builders.
    /// Supports three EOS search paths:
    /// 1. Attribute Search (SetParameter) - filter by lobby attributes
    /// 2. Direct Lobby ID Search (SetLobbyId) - fast lookup by exact ID
    /// 3. Member Search (SetTargetUserId) - find lobbies containing a specific user
    /// Note: These paths are mutually exclusive in EOS.
    /// </summary>
    public class LobbySearchOptions
    {
        /// <summary>
        /// Maximum number of results to return (default: 10).
        /// </summary>
        public uint MaxResults = 10;

        /// <summary>
        /// Search by specific join code. If set, other filters are ignored.
        /// </summary>
        public string JoinCode = null;

        /// <summary>
        /// Version bucket to filter by. Must match for lobby to appear.
        /// </summary>
        public string BucketId = null;

        /// <summary>
        /// Only return lobbies with available slots (default: true).
        /// </summary>
        public bool OnlyAvailable = true;

        /// <summary>
        /// Exclude password-protected lobbies (default: false).
        /// </summary>
        public bool ExcludePasswordProtected = false;

        /// <summary>
        /// Exclude lobbies where game is in progress (default: false).
        /// </summary>
        public bool ExcludeInProgress = false;

        /// <summary>
        /// Additional attribute filters (equality matching).
        /// Deprecated: Use AttributeFilters for more control over comparison operators.
        /// </summary>
        public Dictionary<string, string> Filters = null;

        /// <summary>
        /// Advanced attribute filters with comparison operators.
        /// Use this for numeric comparisons (skill-based matchmaking) or substring searches.
        /// </summary>
        public List<AttributeFilter> AttributeFilters { get; } = new List<AttributeFilter>();

        #region Fluent Builder Methods

        /// <summary>
        /// Filter by game mode.
        /// </summary>
        public LobbySearchOptions WithGameMode(string gameMode)
        {
            EnsureFilters();
            Filters[LobbyAttributes.GAME_MODE] = gameMode;
            return this;
        }

        /// <summary>
        /// Filter by map.
        /// </summary>
        public LobbySearchOptions WithMap(string map)
        {
            EnsureFilters();
            Filters[LobbyAttributes.MAP] = map;
            return this;
        }

        /// <summary>
        /// Filter by region.
        /// </summary>
        public LobbySearchOptions WithRegion(string region)
        {
            EnsureFilters();
            Filters[LobbyAttributes.REGION] = region;
            return this;
        }

        /// <summary>
        /// Filter by lobby name.
        /// </summary>
        public LobbySearchOptions WithLobbyName(string name)
        {
            EnsureFilters();
            Filters[LobbyAttributes.LOBBY_NAME] = name;
            return this;
        }

        /// <summary>
        /// Add a custom attribute filter (equality match).
        /// </summary>
        public LobbySearchOptions WithAttribute(string key, string value)
        {
            EnsureFilters();
            Filters[key] = value;
            return this;
        }

        /// <summary>
        /// Add a custom attribute filter with comparison operator.
        /// Use this for numeric comparisons or substring searches.
        /// </summary>
        /// <param name="key">The attribute key.</param>
        /// <param name="value">The value to compare against.</param>
        /// <param name="comparison">The comparison operator (default: Equal).</param>
        public LobbySearchOptions WithAttribute(string key, string value, SearchComparison comparison)
        {
            AttributeFilters.Add(new AttributeFilter(key, value, comparison));
            return this;
        }

        /// <summary>
        /// Filter by EOS bucket ID (standard EOS grouping mechanism).
        /// </summary>
        public LobbySearchOptions WithBucket(string bucketId)
        {
            BucketId = bucketId;
            return this;
        }

        /// <summary>
        /// Filter by minimum skill level (SKILL_LEVEL >= min).
        /// </summary>
        public LobbySearchOptions WithMinSkill(int minSkill)
        {
            AttributeFilters.Add(new AttributeFilter(
                LobbyAttributes.SKILL_LEVEL,
                minSkill.ToString(),
                SearchComparison.GreaterThanOrEqual));
            return this;
        }

        /// <summary>
        /// Filter by maximum skill level (SKILL_LEVEL <= max).
        /// </summary>
        public LobbySearchOptions WithMaxSkill(int maxSkill)
        {
            AttributeFilters.Add(new AttributeFilter(
                LobbyAttributes.SKILL_LEVEL,
                maxSkill.ToString(),
                SearchComparison.LessThanOrEqual));
            return this;
        }

        /// <summary>
        /// Filter by skill range (SKILL_LEVEL >= min AND SKILL_LEVEL <= max).
        /// </summary>
        public LobbySearchOptions WithSkillRange(int minSkill, int maxSkill)
        {
            return WithMinSkill(minSkill).WithMaxSkill(maxSkill);
        }

        /// <summary>
        /// Filter lobbies by name containing a substring (case sensitive).
        /// </summary>
        public LobbySearchOptions WithLobbyNameContaining(string substring)
        {
            AttributeFilters.Add(new AttributeFilter(
                LobbyAttributes.LOBBY_NAME,
                substring,
                SearchComparison.Contains));
            return this;
        }

        /// <summary>
        /// Set maximum results to return.
        /// </summary>
        public LobbySearchOptions WithMaxResults(uint max)
        {
            MaxResults = max;
            return this;
        }

        /// <summary>
        /// Only return lobbies that are not full.
        /// </summary>
        public LobbySearchOptions OnlyWithAvailableSlots(bool only = true)
        {
            OnlyAvailable = only;
            return this;
        }

        /// <summary>
        /// Exclude password-protected lobbies.
        /// </summary>
        public LobbySearchOptions ExcludePassworded(bool exclude = true)
        {
            ExcludePasswordProtected = exclude;
            return this;
        }

        /// <summary>
        /// Exclude lobbies where game is in progress.
        /// </summary>
        public LobbySearchOptions ExcludeGamesInProgress(bool exclude = true)
        {
            ExcludeInProgress = exclude;
            return this;
        }

        /// <summary>
        /// Filter by minimum player count in lobby.
        /// </summary>
        public LobbySearchOptions WithMinPlayers(int minPlayers)
        {
            _minPlayers = minPlayers;
            return this;
        }

        /// <summary>
        /// Filter by maximum player count (lobby max members).
        /// </summary>
        public LobbySearchOptions WithMaxPlayers(int maxPlayers)
        {
            _maxPlayers = maxPlayers;
            return this;
        }

        /// <summary>
        /// Exclude full lobbies (alias for OnlyWithAvailableSlots).
        /// </summary>
        public LobbySearchOptions ExcludeFull()
        {
            OnlyAvailable = true;
            return this;
        }

        /// <summary>
        /// Filter by host platform.
        /// Platform IDs: "WIN" (Windows), "MAC" (macOS), "LNX" (Linux),
        ///               "AND" (Android), "IOS" (iOS), "OVR" (Quest/Meta VR)
        /// </summary>
        public LobbySearchOptions WithPlatform(string platformId)
        {
            EnsureFilters();
            Filters[LobbyAttributes.HOST_PLATFORM] = platformId;
            return this;
        }

        /// <summary>
        /// Filter by host platform using EOSPlatformType.
        /// </summary>
        public LobbySearchOptions WithPlatform(EOSPlatformType platform)
        {
            string platformId = platform switch
            {
                EOSPlatformType.Windows => "WIN",
                EOSPlatformType.macOS => "MAC",
                EOSPlatformType.Linux => "LNX",
                EOSPlatformType.Android => "AND",
                EOSPlatformType.iOS => "IOS",
                EOSPlatformType.Quest => "OVR",
                _ => null
            };
            if (!string.IsNullOrEmpty(platformId))
                return WithPlatform(platformId);
            return this;
        }

        /// <summary>
        /// Filter to only desktop platforms (Windows, macOS, Linux).
        /// </summary>
        public LobbySearchOptions DesktopOnly()
        {
            AttributeFilters.Add(new AttributeFilter(
                LobbyAttributes.HOST_PLATFORM,
                "WIN,MAC,LNX",
                SearchComparison.AnyOf));
            return this;
        }

        /// <summary>
        /// Filter to only mobile platforms (Android, iOS, Quest).
        /// </summary>
        public LobbySearchOptions MobileOnly()
        {
            AttributeFilters.Add(new AttributeFilter(
                LobbyAttributes.HOST_PLATFORM,
                "AND,IOS,OVR",
                SearchComparison.AnyOf));
            return this;
        }

        /// <summary>
        /// Filter to same platform as the current device.
        /// </summary>
        public LobbySearchOptions SamePlatformOnly()
        {
            return WithPlatform(EOSPlatformHelper.PlatformId);
        }

        /// <summary>
        /// Filter by host input type.
        /// Input type IDs: "KBM" (Keyboard/Mouse), "CTL" (Controller), "TCH" (Touch), "VRC" (VR Controller)
        /// </summary>
        public LobbySearchOptions WithInputType(string inputTypeId)
        {
            EnsureFilters();
            Filters[LobbyAttributes.HOST_INPUT_TYPE] = inputTypeId;
            return this;
        }

        /// <summary>
        /// Filter by host input type using InputType enum.
        /// </summary>
        public LobbySearchOptions WithInputType(InputType inputType)
        {
            return WithInputType(EOSInputHelper.GetInputTypeId(inputType));
        }

        /// <summary>
        /// Filter to only keyboard/mouse lobbies.
        /// </summary>
        public LobbySearchOptions KeyboardMouseOnly()
        {
            return WithInputType("KBM");
        }

        /// <summary>
        /// Filter to only controller lobbies.
        /// </summary>
        public LobbySearchOptions ControllerOnly()
        {
            return WithInputType("CTL");
        }

        /// <summary>
        /// Filter to same input type as current device.
        /// </summary>
        public LobbySearchOptions SameInputOnly()
        {
            return WithInputType(EOSInputHelper.InputTypeId);
        }

        /// <summary>
        /// Filter to "fair" input types (same type or compatible).
        /// KBM only matches KBM, Controller/Touch match each other, VR only matches VR.
        /// </summary>
        public LobbySearchOptions FairInputOnly()
        {
            var current = EOSInputHelper.CurrentInputType;
            switch (current)
            {
                case InputType.KeyboardMouse:
                    return WithInputType("KBM");
                case InputType.Controller:
                case InputType.Touch:
                    AttributeFilters.Add(new AttributeFilter(
                        LobbyAttributes.HOST_INPUT_TYPE,
                        "CTL,TCH",
                        SearchComparison.AnyOf));
                    return this;
                case InputType.VRController:
                    return WithInputType("VRC");
                default:
                    return this;
            }
        }

        private void EnsureFilters()
        {
            Filters ??= new Dictionary<string, string>();
        }

        #endregion

        #region Internal State

        internal int? _minPlayers = null;
        internal int? _maxPlayers = null;

        /// <summary>Minimum players filter (if set).</summary>
        public int? MinPlayersFilter => _minPlayers;

        /// <summary>Maximum players filter (if set).</summary>
        public int? MaxPlayersFilter => _maxPlayers;

        #endregion

        #region Static Factory Methods

        /// <summary>
        /// Create options to find any available lobby (quick match).
        /// </summary>
        public static LobbySearchOptions QuickMatch()
        {
            return new LobbySearchOptions
            {
                MaxResults = 50,
                OnlyAvailable = true,
                ExcludePasswordProtected = true,
                ExcludeInProgress = true
            };
        }

        /// <summary>
        /// Create options to find lobbies by game mode.
        /// </summary>
        public static LobbySearchOptions ForGameMode(string gameMode)
        {
            return new LobbySearchOptions().WithGameMode(gameMode);
        }

        /// <summary>
        /// Create options to find lobbies by region.
        /// </summary>
        public static LobbySearchOptions ForRegion(string region)
        {
            return new LobbySearchOptions().WithRegion(region);
        }

        /// <summary>
        /// Create options for skill-based matchmaking.
        /// </summary>
        /// <param name="playerSkill">The searching player's skill level.</param>
        /// <param name="range">The acceptable skill range (+/- from player skill).</param>
        public static LobbySearchOptions ForSkillRange(int playerSkill, int range = 200)
        {
            return new LobbySearchOptions()
                .WithSkillRange(playerSkill - range, playerSkill + range)
                .ExcludePassworded()
                .ExcludeGamesInProgress();
        }

        #endregion
    }

    /// <summary>
    /// Unified lobby options that work for both creating and searching lobbies.
    /// One options class rules them all — use the same options object for
    /// HostLobbyAsync, SearchLobbiesAsync, QuickMatchOrHostAsync, etc.
    /// Fields that don't apply to a specific operation are gracefully ignored.
    /// </summary>
    public class LobbyOptions
    {
        #region Shared Fields (used by both Create and Search)

        /// <summary>
        /// Human-readable lobby name.
        /// Create: Sets the lobby name attribute.
        /// Search: Filters by exact name match.
        /// </summary>
        public string LobbyName;

        /// <summary>
        /// Game mode (e.g., "deathmatch", "coop", "ranked").
        /// </summary>
        public string GameMode;

        /// <summary>
        /// Map name.
        /// </summary>
        public string Map;

        /// <summary>
        /// Server region (e.g., "us-east", "eu-west").
        /// </summary>
        public string Region;

        /// <summary>
        /// Version bucket for matchmaking.
        /// </summary>
        public string BucketId;

        /// <summary>
        /// Maximum players.
        /// Create: Sets lobby capacity.
        /// Search: Filters by max capacity.
        /// </summary>
        public uint? MaxPlayers;

        /// <summary>
        /// Password for the lobby.
        /// Create: Makes lobby password-protected.
        /// Search: (not used for search filtering)
        /// </summary>
        public string Password;

        /// <summary>
        /// Skill level for matchmaking.
        /// </summary>
        public int? SkillLevel;

        /// <summary>
        /// Custom attributes.
        /// </summary>
        public Dictionary<string, string> Attributes;

        #endregion

        #region Create-Only Fields (ignored during search)

        /// <summary>
        /// Custom join code. If null, a random numeric code is generated.
        /// Create-only.
        /// </summary>
        public string JoinCode;

        /// <summary>
        /// Length of auto-generated join codes (4-8 digits). Null = use EOSLobbyManager default (6).
        /// Create-only.
        /// </summary>
        public int? JoinCodeLength;

        /// <summary>
        /// Use EOS-generated LobbyId as the join code.
        /// Create-only.
        /// </summary>
        public bool UseEosLobbyId;

        /// <summary>
        /// Whether the lobby is publicly searchable (default: true).
        /// Create-only.
        /// </summary>
        public bool IsPublic = true;

        /// <summary>
        /// Enable voice chat (default: true).
        /// Create-only.
        /// </summary>
        public bool EnableVoice = true;

        /// <summary>
        /// Start with microphone muted.
        /// Create-only.
        /// </summary>
        public bool StartMuted;

        /// <summary>
        /// Allow host migration (default: true).
        /// Create-only.
        /// </summary>
        public bool AllowHostMigration = true;

        /// <summary>
        /// Allow crossplay (default: true).
        /// Create-only.
        /// </summary>
        public bool AllowCrossplay = true;

        #endregion

        #region Search-Only Fields (ignored during create)

        /// <summary>
        /// Maximum search results (default: 10).
        /// Search-only.
        /// </summary>
        public uint MaxResults = 10;

        /// <summary>
        /// Minimum players in lobby filter.
        /// Search-only.
        /// </summary>
        public int? MinPlayers;

        /// <summary>
        /// Only return lobbies with available slots (default: true).
        /// Search-only.
        /// </summary>
        public bool OnlyAvailable = true;

        /// <summary>
        /// Exclude password-protected lobbies.
        /// Search-only.
        /// </summary>
        public bool ExcludePasswordProtected;

        /// <summary>
        /// Exclude lobbies where game is in progress.
        /// Search-only.
        /// </summary>
        public bool ExcludeInProgress;

        /// <summary>
        /// Minimum skill level filter.
        /// Search-only.
        /// </summary>
        public int? MinSkill;

        /// <summary>
        /// Maximum skill level filter.
        /// Search-only.
        /// </summary>
        public int? MaxSkill;

        /// <summary>
        /// Platform filter.
        /// Values: "WIN", "MAC", "LNX", "AND", "IOS", "OVR", "DESKTOP", "MOBILE", "SAME"
        /// Search-only.
        /// </summary>
        public string PlatformFilter;

        /// <summary>
        /// Input type filter for input-based matchmaking.
        /// Values: "KBM", "CTL", "TCH", "VRC", "SAME", "FAIR"
        /// Search-only.
        /// </summary>
        public string InputFilter;

        #endregion

        #region Fluent Builder Methods - Shared

        public LobbyOptions WithName(string name) { LobbyName = name; return this; }
        public LobbyOptions WithGameMode(string mode) { GameMode = mode; return this; }
        public LobbyOptions WithMap(string map) { Map = map; return this; }
        public LobbyOptions WithRegion(string region) { Region = region; return this; }
        public LobbyOptions WithBucketId(string bucket) { BucketId = bucket; return this; }
        public LobbyOptions WithMaxPlayers(uint max) { MaxPlayers = max; return this; }
        public LobbyOptions WithPassword(string pass) { Password = pass; return this; }
        public LobbyOptions WithSkillLevel(int skill) { SkillLevel = skill; return this; }
        public LobbyOptions WithAttribute(string key, string value)
        {
            Attributes ??= new Dictionary<string, string>();
            Attributes[key] = value;
            return this;
        }

        #endregion

        #region Fluent Builder Methods - Create-Only

        public LobbyOptions WithJoinCode(string code) { JoinCode = code; return this; }
        public LobbyOptions WithJoinCodeLength(int length) { JoinCodeLength = Math.Max(4, Math.Min(8, length)); return this; }
        public LobbyOptions WithEosLobbyId() { UseEosLobbyId = true; return this; }
        public LobbyOptions WithVoice(bool enabled = true) { EnableVoice = enabled; return this; }
        public LobbyOptions WithMutedMic(bool muted = true) { StartMuted = muted; return this; }
        public LobbyOptions WithHostMigration(bool enabled = true) { AllowHostMigration = enabled; return this; }
        public LobbyOptions WithCrossplay(bool enabled = true) { AllowCrossplay = enabled; return this; }
        public LobbyOptions AsPrivate() { IsPublic = false; return this; }
        public LobbyOptions AsPublic() { IsPublic = true; return this; }

        #endregion

        #region Fluent Builder Methods - Search-Only

        public LobbyOptions WithMaxResults(uint max) { MaxResults = max; return this; }
        public LobbyOptions WithMinPlayers(int min) { MinPlayers = min; return this; }
        public LobbyOptions ExcludeFull() { OnlyAvailable = true; return this; }
        public LobbyOptions IncludeFull() { OnlyAvailable = false; return this; }
        public LobbyOptions ExcludePassworded() { ExcludePasswordProtected = true; return this; }
        public LobbyOptions ExcludeGamesInProgress() { ExcludeInProgress = true; return this; }
        public LobbyOptions WithMinSkill(int min) { MinSkill = min; return this; }
        public LobbyOptions WithMaxSkill(int max) { MaxSkill = max; return this; }
        public LobbyOptions WithSkillRange(int min, int max) { MinSkill = min; MaxSkill = max; return this; }
        public LobbyOptions WithPlatformFilter(string platformId) { PlatformFilter = platformId; return this; }
        public LobbyOptions DesktopOnly() { PlatformFilter = "DESKTOP"; return this; }
        public LobbyOptions MobileOnly() { PlatformFilter = "MOBILE"; return this; }
        public LobbyOptions SamePlatformOnly() { PlatformFilter = "SAME"; return this; }
        public LobbyOptions WithInputFilter(string inputTypeId) { InputFilter = inputTypeId; return this; }
        public LobbyOptions KeyboardMouseOnly() { InputFilter = "KBM"; return this; }
        public LobbyOptions ControllerOnly() { InputFilter = "CTL"; return this; }
        public LobbyOptions SameInputOnly() { InputFilter = "SAME"; return this; }
        public LobbyOptions FairInputOnly() { InputFilter = "FAIR"; return this; }

        #endregion

        #region Conversion Methods

        /// <summary>
        /// Convert to LobbyCreateOptions for hosting.
        /// </summary>
        public LobbyCreateOptions ToCreateOptions()
        {
            var options = new LobbyCreateOptions
            {
                LobbyName = LobbyName,
                GameMode = GameMode,
                Map = Map,
                Region = Region,
                BucketId = BucketId ?? "v1",
                MaxPlayers = MaxPlayers ?? 4,
                Password = Password,
                SkillLevel = SkillLevel,
                JoinCode = JoinCode,
                JoinCodeLength = JoinCodeLength,
                UseEosLobbyId = UseEosLobbyId,
                IsPublic = IsPublic,
                EnableVoice = EnableVoice,
                StartMuted = StartMuted,
                AllowHostMigration = AllowHostMigration,
                AllowCrossplay = AllowCrossplay,
                Attributes = Attributes != null ? new Dictionary<string, string>(Attributes) : null
            };
            return options;
        }

        /// <summary>
        /// Convert to LobbySearchOptions for searching.
        /// </summary>
        public LobbySearchOptions ToSearchOptions()
        {
            var options = new LobbySearchOptions
            {
                MaxResults = MaxResults,
                BucketId = BucketId,
                OnlyAvailable = OnlyAvailable,
                ExcludePasswordProtected = ExcludePasswordProtected,
                ExcludeInProgress = ExcludeInProgress
            };

            // Apply filters
            if (!string.IsNullOrEmpty(GameMode))
                options = options.WithGameMode(GameMode);
            if (!string.IsNullOrEmpty(Map))
                options = options.WithMap(Map);
            if (!string.IsNullOrEmpty(Region))
                options = options.WithRegion(Region);
            if (!string.IsNullOrEmpty(LobbyName))
                options = options.WithLobbyName(LobbyName);
            if (MinPlayers.HasValue)
                options = options.WithMinPlayers(MinPlayers.Value);
            if (MaxPlayers.HasValue)
                options = options.WithMaxPlayers((int)MaxPlayers.Value);
            if (MinSkill.HasValue)
                options = options.WithMinSkill(MinSkill.Value);
            if (MaxSkill.HasValue)
                options = options.WithMaxSkill(MaxSkill.Value);

            // Apply platform filter
            if (!string.IsNullOrEmpty(PlatformFilter))
            {
                switch (PlatformFilter.ToUpperInvariant())
                {
                    case "DESKTOP":
                        options = options.DesktopOnly();
                        break;
                    case "MOBILE":
                        options = options.MobileOnly();
                        break;
                    case "SAME":
                        options = options.SamePlatformOnly();
                        break;
                    default:
                        options = options.WithPlatform(PlatformFilter);
                        break;
                }
            }

            // Apply input type filter
            if (!string.IsNullOrEmpty(InputFilter))
            {
                switch (InputFilter.ToUpperInvariant())
                {
                    case "SAME":
                        options = options.SameInputOnly();
                        break;
                    case "FAIR":
                        options = options.FairInputOnly();
                        break;
                    default:
                        options = options.WithInputType(InputFilter);
                        break;
                }
            }

            // Copy custom attributes
            if (Attributes != null)
            {
                foreach (var kvp in Attributes)
                    options = options.WithAttribute(kvp.Key, kvp.Value);
            }

            return options;
        }

        /// <summary>
        /// Implicit conversion to LobbyCreateOptions.
        /// </summary>
        public static implicit operator LobbyCreateOptions(LobbyOptions options)
        {
            return options?.ToCreateOptions();
        }

        /// <summary>
        /// Implicit conversion to LobbySearchOptions.
        /// </summary>
        public static implicit operator LobbySearchOptions(LobbyOptions options)
        {
            return options?.ToSearchOptions();
        }

        #endregion

        #region Factory Methods

        /// <summary>
        /// Create options preset for quick match.
        /// </summary>
        public static LobbyOptions QuickMatch()
        {
            return new LobbyOptions
            {
                MaxResults = 50,
                OnlyAvailable = true,
                ExcludePasswordProtected = true,
                ExcludeInProgress = true
            };
        }

        /// <summary>
        /// Create options for a specific game mode.
        /// </summary>
        public static LobbyOptions ForGameMode(string gameMode)
        {
            return new LobbyOptions().WithGameMode(gameMode);
        }

        /// <summary>
        /// Create options for skill-based matchmaking.
        /// </summary>
        public static LobbyOptions ForSkillRange(int playerSkill, int range = 200)
        {
            return new LobbyOptions()
                .WithSkillRange(playerSkill - range, playerSkill + range)
                .ExcludePassworded()
                .ExcludeGamesInProgress();
        }

        #endregion
    }

    /// <summary>
    /// Standard lobby attribute keys.
    /// </summary>
    public static class LobbyAttributes
    {
        /// <summary>
        /// The numeric join code (searchable).
        /// </summary>
        public const string JOIN_CODE = "JOIN_CODE";

        /// <summary>
        /// Human-readable lobby name/title.
        /// </summary>
        public const string LOBBY_NAME = "LOBBY_NAME";

        /// <summary>
        /// Game mode (e.g., "deathmatch", "coop", "ranked").
        /// </summary>
        public const string GAME_MODE = "GAME_MODE";

        /// <summary>
        /// Current map name.
        /// </summary>
        public const string MAP = "MAP";

        /// <summary>
        /// Server region (e.g., "us-east", "eu-west", "asia").
        /// </summary>
        public const string REGION = "REGION";

        /// <summary>
        /// Game version string.
        /// </summary>
        public const string VERSION = "VERSION";

        /// <summary>
        /// Password hash (presence indicates password-protected).
        /// Never store actual password - just a marker or hash.
        /// </summary>
        public const string PASSWORD = "PASSWORD";

        /// <summary>
        /// Skill level / MMR for matchmaking (as string number).
        /// </summary>
        public const string SKILL_LEVEL = "SKILL_LEVEL";

        /// <summary>
        /// Whether the game is in progress ("true"/"false").
        /// </summary>
        public const string IN_PROGRESS = "IN_PROGRESS";

        /// <summary>
        /// Whether host migration is supported.
        /// </summary>
        public const string MIGRATION_SUPPORT = "MIGRATION_SUPPORT";

        /// <summary>
        /// Custom game settings JSON or simple string.
        /// </summary>
        public const string GAME_SETTINGS = "GAME_SETTINGS";

        /// <summary>
        /// Host platform ID (e.g., "WIN", "MAC", "LNX", "AND", "IOS", "OVR").
        /// Automatically set when creating a lobby.
        /// </summary>
        public const string HOST_PLATFORM = "HOST_PLATFORM";

        /// <summary>
        /// Host input type ID (e.g., "KBM", "CTL", "TCH", "VRC").
        /// Automatically set when creating a lobby.
        /// </summary>
        public const string HOST_INPUT_TYPE = "HOST_INPUT_TYPE";
    }

    /// <summary>
    /// Standard member attribute keys.
    /// </summary>
    public static class MemberAttributes
    {
        /// <summary>
        /// Player display name.
        /// </summary>
        public const string DISPLAY_NAME = "DISPLAY_NAME";

        /// <summary>
        /// Ready status ("true"/"false").
        /// </summary>
        public const string READY = "READY";

        /// <summary>
        /// Team assignment.
        /// </summary>
        public const string TEAM = "TEAM";

        /// <summary>
        /// Chat message (timestamp:message format).
        /// </summary>
        public const string CHAT = "CHAT";

        /// <summary>
        /// Platform ID (e.g., "WIN", "AND", "IOS", "OVR").
        /// </summary>
        public const string PLATFORM = "PLATFORM";
    }

    /// <summary>
    /// Comparison operators for attribute searches.
    /// Maps to Epic.OnlineServices.ComparisonOp.
    /// </summary>
    public enum SearchComparison
    {
        /// <summary>Exact match (default).</summary>
        Equal,
        /// <summary>Value does not equal.</summary>
        NotEqual,
        /// <summary>Numeric greater than.</summary>
        GreaterThan,
        /// <summary>Numeric greater than or equal.</summary>
        GreaterThanOrEqual,
        /// <summary>Numeric less than.</summary>
        LessThan,
        /// <summary>Numeric less than or equal.</summary>
        LessThanOrEqual,
        /// <summary>Substring match (case sensitive).</summary>
        Contains,
        /// <summary>Value is any from a list.</summary>
        AnyOf,
        /// <summary>Value is NOT any from a list.</summary>
        NotAnyOf,
        /// <summary>Distance from given value (for numeric matchmaking).</summary>
        Distance
    }

    /// <summary>
    /// A single attribute filter for lobby search.
    /// </summary>
    public struct AttributeFilter
    {
        /// <summary>The attribute key to filter on.</summary>
        public string Key;

        /// <summary>The value to compare against (as string).</summary>
        public string Value;

        /// <summary>The comparison operator to use.</summary>
        public SearchComparison Comparison;

        /// <summary>
        /// Creates an attribute filter.
        /// </summary>
        public AttributeFilter(string key, string value, SearchComparison comparison = SearchComparison.Equal)
        {
            Key = key;
            Value = value;
            Comparison = comparison;
        }

        public override string ToString()
        {
            return $"{Key} {Comparison} '{Value}'";
        }
    }
}
