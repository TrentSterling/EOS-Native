# EOS Platform Interface Reference

> Source: [dev.epicgames.com](https://dev.epicgames.com/docs/game-services/eos-platform-interface)

## Overview

The Platform Interface is the central component of the EOS SDK. It provides access to all other interfaces and manages their lifecycle.

> "The Platform Interface sits at the heart of the EOS SDK and holds the handles you need to access every other interface and keep them all running."

## Initialization

### Step 1: EOS_Initialize

Call `EOS_Initialize` with `EOS_InitializeOptions`:

| Property | Description |
|----------|-------------|
| `ProductName` | Game name (max 64 chars, ANSI 32-127) |
| `ProductVersion` | Application version string |
| `AllocateMemoryFunction` | Custom `malloc` or `NULL` |
| `ReallocateMemoryFunction` | Custom `realloc` or `NULL` |
| `ReleaseMemoryFunction` | Custom `free` or `NULL` |
| `SystemInitializeOptions` | Platform-specific init data |
| `OverrideThreadAffinity` | Thread affinity overrides or `NULL` |

> **Security:** Product names are visible in presence information. Use codenames during development.

### Step 2: EOS_Platform_Create

Call `EOS_Platform_Create` with `EOS_Platform_Options`:

| Property | Description |
|----------|-------------|
| `ProductId` | From Developer Portal |
| `SandboxId` | From Developer Portal |
| `DeploymentId` | From Developer Portal |
| `ClientCredentials` | Client ID + Secret pair |
| `bIsServer` | `false` for client, `true` for dedicated server |
| `EncryptionKey` | 256-bit hex key (64 characters) |
| `OverrideCountryCode` | Country code override (optional) |
| `OverrideLocaleCode` | Locale code override (optional) |
| `Flags` | Platform creation flags (`EOS_PF_*`) |
| `CacheDirectory` | Absolute path for temp data cache |
| `TickBudgetInMilliseconds` | Work budget per tick (0 = all work) |
| `RTCOptions` | RTC config or `NULL` to disable voice |
| `IntegratedPlatformOptionsContainerHandle` | Native platform integration or `NULL` |
| `TaskNetworkTimeoutSeconds` | Network timeout override (default 30s) |

Returns `EOS_HPlatform` handle on success, `NULL` on failure.

> **Multi-instance:** Multiple Platform Interface handles are supported (e.g. for editors). But do NOT initialize more than one SDK instance.

## Tick Function

```csharp
platformInterface.Tick();
```

Call from your main game loop **every frame** to process async operations and fire callbacks.

`TickBudgetInMilliseconds` controls how much work is done per tick — `0` means process everything.

## Interface Access

| Interface | C# Access |
|-----------|-----------|
| Achievements | `GetAchievementsInterface()` |
| Anti-Cheat (Client) | `GetAntiCheatClientInterface()` |
| Anti-Cheat (Server) | `GetAntiCheatServerInterface()` |
| Auth | `GetAuthInterface()` |
| Connect | `GetConnectInterface()` |
| Custom Invites | `GetCustomInvitesInterface()` |
| Ecom | `GetEcomInterface()` |
| Friends | `GetFriendsInterface()` |
| Leaderboards | `GetLeaderboardsInterface()` |
| Lobby | `GetLobbyInterface()` |
| Metrics | `GetMetricsInterface()` |
| P2P | `GetP2PInterface()` |
| Player Data Storage | `GetPlayerDataStorageInterface()` |
| Presence | `GetPresenceInterface()` |
| RTC | `GetRTCInterface()` |
| RTC Admin | `GetRTCAdminInterface()` |
| Reports | `GetReportsInterface()` |
| Sanctions | `GetSanctionsInterface()` |
| Sessions | `GetSessionsInterface()` |
| Stats | `GetStatsInterface()` |
| Title Storage | `GetTitleStorageInterface()` |
| User Info | `GetUserInfoInterface()` |

## Application Status

Notify SDK of app state changes:

```csharp
platformInterface.SetApplicationStatus(ApplicationStatus.BackgroundSuspended);
platformInterface.SetApplicationStatus(ApplicationStatus.Foreground); // default
```

| Status | When |
|--------|------|
| `BackgroundSuspended` | App suspended/backgrounded by OS |
| `Foreground` | App active (default) |

## Network Status

```csharp
platformInterface.SetNetworkStatus(NetworkStatus.Online);
```

| Status | Meaning |
|--------|---------|
| `Disabled` | Network unavailable |
| `Offline` | Likely not connected to internet |
| `Online` | Connected (default on PC) |

> **Console platforms** (PlayStation, Switch, Xbox) default to `Disabled`. You must explicitly set `Online`.

### Network Timeout

Default: 30 seconds when offline. Tasks queue until network becomes available. Override via `TaskNetworkTimeoutSeconds` in platform options.

## Launcher Integration

```csharp
var result = platformInterface.CheckForLauncherAndRestart();
```

| Return Code | Action |
|-------------|--------|
| `EOS_Success` | App is restarting via launcher — terminate process |
| `EOS_NoChange` | Already launched by launcher — continue normally |
| `EOS_UnexpectedError` | LauncherCheck module failed |

Environment variable `EOS_PLATFORM_CHECKFORLAUNCHERANDRESTART_ENV_VAR` is set to `1` by the launcher.

## Shutdown

Proper cleanup order:

1. `EOS_Platform_Release(platformHandle)` — release platform instance
2. `EOS_Shutdown()` — release global SDK state

> **Warning:** After `EOS_Shutdown`, the SDK cannot be reinitialized. All further calls will fail.

> **Unity Editor:** Never call Release or Shutdown in editor — you won't be able to reinitialize without restarting the editor.

## Block List Enforcement

| Scenario | SDK Behavior |
|----------|-------------|
| Both players use Epic Account Services | Enforces EAS + platform block lists |
| Both use platform auth | Enforces platform block list |
| Different auth methods | You must implement block list management |

## Best Practices

1. **Init order:** Initialize SDK → Create Platform → Retrieve interfaces
2. **Main loop:** Call `Tick()` every frame
3. **Status updates:** Set application and network status on changes
4. **RTC:** Pass `NULL` for `RTCOptions` if voice not needed
5. **Memory:** Provide custom allocators if needed for console platforms
6. **Editor:** Never call Release/Shutdown in Unity Editor
