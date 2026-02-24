# Lobbies

Lobbies are the core of EOS multiplayer. They manage player presence, attributes, voice channels, and serve as the connection point for P2P networking.

## Creating a Lobby

### Simple Create

```csharp
var lobby = EOSLobbyManager.Instance;

// Auto-generates 4-digit join code, voice enabled, host migration on
var (result, data) = await lobby.CreateLobbyAsync();

Debug.Log($"Code: {data.JoinCode}");
```

### With Options (Object Initializer)

```csharp
var (result, data) = await lobby.CreateLobbyAsync(new LobbyOptions
{
    LobbyName = "Pro Players Only",
    GameMode = "competitive",
    Region = "us-east",
    MaxPlayers = 8,
    Password = "secret123"
});
```

### With Options (Fluent Style)

```csharp
var (result, data) = await lobby.CreateLobbyAsync(
    new LobbyOptions()
        .WithName("Pro Players Only")
        .WithGameMode("competitive")
        .WithRegion("us-east")
        .WithMaxPlayers(8)
        .WithPassword("secret123")
);
```

Both styles are equivalent. `LobbyOptions` implicitly converts to `LobbyCreateOptions` when passed to `CreateLobbyAsync`.

### With Voice and Host Migration

```csharp
var (result, data) = await lobby.CreateLobbyAsync(
    new LobbyOptions()
        .WithName("Voice Room")
        .WithMaxPlayers(8)
        .WithVoice()
        .WithHostMigration()
);
```

Voice and host migration are enabled by default. Use `.WithVoice(false)` or `.WithHostMigration(false)` to disable them.

### With Custom Join Code Length

By default, join codes are 6 digits. You can configure the length (4-8 digits) globally or per-lobby:

```csharp
// Global: change default for all lobbies
EOSLobbyManager.Instance.JoinCodeLength = 4;  // shorter codes, easier to share

// Per-lobby: override via LobbyOptions
var (result, data) = await lobby.CreateLobbyAsync(
    new LobbyOptions()
        .WithName("Tournament")
        .WithJoinCodeLength(8)  // longer code for this lobby only
);
```

Shorter codes (4 digits) are easier to share verbally but have higher collision probability. Longer codes (6-8 digits) are safer for large player bases.

### With EOS LobbyId as Code

Use the EOS-generated LobbyId instead of a random numeric code. Guarantees uniqueness - useful for chat history or invite links that key off the lobby code:

```csharp
var (result, data) = await lobby.CreateLobbyAsync(
    new LobbyOptions()
        .WithEosLobbyId()
        .WithName("My Room")
        .WithGameMode("casual")
);

// data.JoinCode will be something like "a1b2c3d4e5f67890abcdef12"
```

**When to use EOS LobbyId:**
- Chat history (no collisions with reused 4-digit codes)
- Invite links / deep links (not shared verbally)
- Internal systems that don't need human-readable codes

**When to use custom codes:**
- Verbal sharing ("Join lobby 1234!")
- Simple UI display
- Human-memorable codes

## Unified LobbyOptions

`LobbyOptions` is a single class that works for both creating and searching lobbies. It implicitly converts to `LobbyCreateOptions` or `LobbySearchOptions`, so the same object can be passed to any lobby API:

```csharp
var options = new LobbyOptions()
    .WithName("Pro Players")
    .WithGameMode("deathmatch")
    .WithMaxPlayers(16)
    .WithVoice()
    .WithRegion("us-east");

// Same object works for both:
await lobby.CreateLobbyAsync(options);    // -> LobbyCreateOptions
await lobby.SearchLobbiesAsync(options);  // -> LobbySearchOptions
```

### LobbyOptions Fields

| Field | Used For | Default | Description |
|-------|----------|---------|-------------|
| `LobbyName` | Both | null | Display name / name filter |
| `GameMode` | Both | null | Game mode attribute / filter |
| `Map` | Both | null | Map attribute / filter |
| `Region` | Both | null | Region attribute / filter |
| `BucketId` | Both | "v1" | Version bucket for matchmaking |
| `MaxPlayers` | Both | 4 | Max capacity / capacity filter |
| `Password` | Create | null | Password protection |
| `SkillLevel` | Both | null | Skill level for matchmaking |
| `JoinCode` | Create | null | Custom join code (null = auto-generated) |
| `JoinCodeLength` | Create | null | Override join code length (4-8 digits) |
| `UseEosLobbyId` | Create | false | Use EOS-generated ID as code |
| `IsPublic` | Create | true | Publicly searchable |
| `EnableVoice` | Create | true | Enable voice chat (RTC room) |
| `StartMuted` | Create | false | Start with mic muted |
| `AllowHostMigration` | Create | true | Auto-promote new host on leave |
| `AllowCrossplay` | Create | true | Allow cross-platform players |
| `MaxResults` | Search | 10 | Limit search results |
| `MinPlayers` | Search | null | Minimum player count filter |
| `OnlyAvailable` | Search | true | Only lobbies with open slots |
| `ExcludePasswordProtected` | Search | false | Skip password-protected lobbies |
| `ExcludeInProgress` | Search | false | Skip in-progress games |
| `MinSkill` / `MaxSkill` | Search | null | Skill range filter |
| `PlatformFilter` | Search | null | Platform filter (DESKTOP, MOBILE, SAME) |
| `InputFilter` | Search | null | Input filter (KBM, CTL, TCH, SAME, FAIR) |
| `Attributes` | Both | null | Custom key-value attributes |

Irrelevant fields are gracefully ignored during conversion (e.g., search-only fields are ignored when creating).

### Fluent Builder Methods

All fluent methods return the same `LobbyOptions` instance for chaining.

**Shared (Create + Search):**

| Method | Description |
|--------|-------------|
| `.WithName(string)` | Set lobby name |
| `.WithGameMode(string)` | Set game mode |
| `.WithMap(string)` | Set map |
| `.WithRegion(string)` | Set region |
| `.WithBucketId(string)` | Set version bucket |
| `.WithMaxPlayers(uint)` | Set max players |
| `.WithPassword(string)` | Set password |
| `.WithSkillLevel(int)` | Set skill level |
| `.WithAttribute(key, value)` | Add custom attribute |

**Create-Only:**

| Method | Description |
|--------|-------------|
| `.WithJoinCode(string)` | Set custom join code |
| `.WithJoinCodeLength(int)` | Override code length (4-8 digits) |
| `.WithEosLobbyId()` | Use EOS-generated lobby ID as code |
| `.WithVoice(bool)` | Enable/disable voice (default: true) |
| `.WithMutedMic(bool)` | Start muted (default: false) |
| `.WithHostMigration(bool)` | Enable/disable migration (default: true) |
| `.WithCrossplay(bool)` | Enable/disable crossplay (default: true) |
| `.AsPrivate()` | Make lobby private (not searchable) |
| `.AsPublic()` | Make lobby public |

**Search-Only:**

| Method | Description |
|--------|-------------|
| `.WithMaxResults(uint)` | Limit search results |
| `.WithMinPlayers(int)` | Minimum player count |
| `.ExcludeFull()` | Only lobbies with space |
| `.IncludeFull()` | Include full lobbies |
| `.ExcludePassworded()` | Skip password-protected |
| `.ExcludeGamesInProgress()` | Skip in-progress games |
| `.WithSkillRange(min, max)` | Set skill range filter |
| `.DesktopOnly()` | Desktop platform filter |
| `.MobileOnly()` | Mobile platform filter |
| `.SamePlatformOnly()` | Same platform filter |
| `.KeyboardMouseOnly()` | KBM input filter |
| `.ControllerOnly()` | Controller input filter |
| `.SameInputOnly()` | Same input type filter |
| `.FairInputOnly()` | Fair input matching filter |

### Factory Methods

Pre-configured presets for common scenarios:

```csharp
// Quick match - search for open, non-passworded, non-in-progress lobbies
var options = LobbyOptions.QuickMatch();

// Game mode search
var options = LobbyOptions.ForGameMode("deathmatch");

// Skill-based matchmaking (player skill 1500, +/- 200 range)
var options = LobbyOptions.ForSkillRange(1500, 200);
// Sets MinSkill=1300, MaxSkill=1700, ExcludePassworded, ExcludeInProgress
```

## Joining a Lobby

### By Code

```csharp
var (result, data) = await EOSLobbyManager.Instance.JoinLobbyByCodeAsync("1234");

if (result == Result.Success)
    Debug.Log($"Joined: {data.JoinCode}");
```

### By Lobby ID

```csharp
var (result, data) = await EOSLobbyManager.Instance.JoinLobbyByIdAsync(lobbyId);
```

## Quick Match

Finds an available lobby or creates one if none found:

```csharp
// Simple - uses defaults
var (result, data, didHost) = await EOSLobbyManager.Instance.QuickMatchOrHostAsync();

if (didHost)
    Debug.Log("No lobbies found, created one!");
else
    Debug.Log($"Joined existing lobby: {data.JoinCode}");
```

With custom host options if no match is found:

```csharp
var (result, data, didHost) = await EOSLobbyManager.Instance.QuickMatchOrHostAsync(
    new LobbyOptions()
        .WithName("Auto-Hosted")
        .WithGameMode("casual")
        .WithMaxPlayers(8)
);
```

### Join by Game Mode

```csharp
var (result, data) = await EOSLobbyManager.Instance.JoinByGameModeAsync("deathmatch");
```

## Searching for Lobbies

### Simple Search

```csharp
var (result, lobbies) = await EOSLobbyManager.Instance.SearchLobbiesAsync();

foreach (var lobby in lobbies)
{
    Debug.Log($"{lobby.LobbyName} ({lobby.JoinCode}) - {lobby.MemberCount}/{lobby.MaxPlayers}");
}
```

### Filtered Search

```csharp
var (result, lobbies) = await EOSLobbyManager.Instance.SearchLobbiesAsync(
    new LobbyOptions()
        .WithGameMode("ranked")
        .WithSkillRange(1000, 2000)
        .WithMaxResults(20)
        .ExcludePassworded()
        .DesktopOnly()
);
```

### Join First Match

Search and automatically join the first matching lobby:

```csharp
var (result, data) = await EOSLobbyManager.Instance.JoinFirstMatchingAsync(
    new LobbyOptions()
        .WithGameMode("casual")
        .ExcludePassworded()
);
```

## Lobby Attributes

Custom data attached to lobbies, synced to all members.

### Setting Attributes (Host Only)

```csharp
await EOSLobbyManager.Instance.UpdateAttributeAsync("map", "dust2");
await EOSLobbyManager.Instance.UpdateAttributeAsync("difficulty", "hard");
```

### Reading Attributes

```csharp
string map = await EOSLobbyManager.Instance.GetAttributeAsync("map");
```

### Via LobbyOptions

```csharp
var (result, data) = await lobby.CreateLobbyAsync(
    new LobbyOptions()
        .WithName("Custom Room")
        .WithAttribute("map", "arena")
        .WithAttribute("difficulty", "hard")
        .WithAttribute("season", "3")
);
```

## Ghost Lobby Filtering

EOS lobbies can become "ghosts" - they linger in search results after all players leave or disconnect. EOS-Native automatically filters these at every level:

```csharp
// Check if a lobby is a ghost (0 members or no owner)
if (lobbyData.IsGhost)
    Debug.Log("This lobby is dead");
```

**Built-in protection (no user action needed):**

| Layer | Method | What Happens |
|-------|--------|-------------|
| Search | `SearchLobbiesAsync` | Ghost lobbies excluded from results |
| Direct ID | `SearchByLobbyIdAsync` | Returns `NotFound` for ghost lobbies |
| Friend search | `SearchByMemberAsync` | Ghost lobbies filtered from results |
| Friend join | `FindFriendLobbiesAsync` | Ghost lobbies filtered |
| Join | `JoinLobbyByIdAsync` | Post-join detection - auto-leaves if lobby is dead |

The join-level check is the final safety net. Even if a ghost lobby slips through search results (race condition between search and join), `JoinLobbyByIdAsync` will detect it after joining, automatically leave, and return `NotFound`.

## Leaving a Lobby

```csharp
// Async (recommended) - fires BeforeLeaveLobby hook + OnLobbyLeft event
await EOSLobbyManager.Instance.LeaveLobbyAsync();

// Sync (for application quit) - fires OnLobbyLeft event
EOSLobbyManager.Instance.LeaveLobbySync();
```

Both paths notify all subscribers (FishNet transport, P2P, voice, etc.) via the `OnLobbyLeft` event. Use `LeaveLobbyAsync` when possible; use `LeaveLobbySync` only during `OnApplicationQuit` where async won't complete in time.

## State Checking

```csharp
var lobby = EOSLobbyManager.Instance;

if (lobby.IsInLobby)
    Debug.Log($"In lobby: {lobby.CurrentLobby.JoinCode}");

if (lobby.IsOwner)
    Debug.Log("I am the host");
```

## Events

```csharp
var lobby = EOSLobbyManager.Instance;

lobby.OnLobbyJoined += (lobbyData) => { };
lobby.OnLobbyLeft += () => { };
lobby.OnMemberJoined += (member) => { };
lobby.OnMemberLeft += (puid) => { };
lobby.OnOwnerChanged += (newOwnerPuid) => { };
lobby.OnLobbyUpdated += (lobbyData) => { };
lobby.OnMemberAttributeUpdated += (puid, key, value) => { };
```

## Voice Fallback

`CreateLobbyAsync` includes automatic voice fallback. If the EOS SDK returns an error when creating with `EnableVoice = true` (e.g., on platforms where RTC isn't available), it automatically retries without voice. This prevents `InvalidRequest` errors on Android and other platforms.

## Service Limits

| Limit | Value |
|-------|-------|
| Max players per lobby | 64 |
| Max lobbies per user | 16 |
| Create/Join rate | 30/min |
| Attribute updates | 100/min |
| Attribute value length | 1000 chars |
| Voice participants | 64 (SDK 1.16+) |
