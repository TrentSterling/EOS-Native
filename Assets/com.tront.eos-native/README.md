# EOS Native

Epic Online Services (EOS) C# SDK packaged for Unity.

**No wrappers. No middleware. Just the SDK + batteries included.**

## Why This Exists

- Epic has **no public CDN** for the EOS SDK (requires developer portal login)
- OpenEOS depends on a third-party maintainer
- PlayEveryWare is bloated with middleware
- This gives you **full control** over SDK distribution and versioning

## Installation

### Unity Package Manager (Recommended)

1. Open Unity
2. Window > Package Manager
3. Click "+" > "Add package from git URL"
4. Enter:
```
https://github.com/TrentSterling/EOS-Native.git?path=Assets/com.tront.eos-native
```

### Manual

Copy the `com.tront.eos-native` folder into your project's `Packages/` directory.

## Quick Start

1. **Tools > EOS Native > Setup Wizard** - Enter your Developer Portal credentials
2. Drop an **EOSManager** into your scene (or use **Tools > EOS Native > Setup Scene**)
3. Enable **Auto Initialize** and **Auto Login** on the EOSManager inspector
4. Hit Play - you're connected

Everything auto-creates. No manual component wiring needed.

## Features

### Core
- **EOSManager** - SDK lifecycle, initialization, shutdown, tick management
- **EOSConfig** - ScriptableObject for Developer Portal credentials with validation
- **Auto Bootstrap** - Optional auto-init and auto-login on play
- **Smart Login** - Tries persistent auth first, falls back to device token

### Authentication
| Method | Description |
|--------|-------------|
| Device Token | Anonymous login, auto-creates device ID |
| Epic Account | Login via Epic overlay |
| Persistent Auth | Silent re-login across sessions |
| Smart Login | Persistent -> device token fallback chain |

### Lobbies & Multiplayer
- **EOSLobbyManager** - Create, search, join, leave lobbies with **4-digit join codes**
- **Quick Match** - Find and join a random public lobby
- **Host Migration** - Automatic ownership transfer
- **Lobby Attributes** - Custom key/value data on lobbies and members
- **EOSLobbyChatManager** - Text chat via lobby attributes (persists through host migration)

### Parties
- **EOSPartyManager** - Persistent cross-game parties
- Party follow (leader joins game, party follows)
- Ready checks with configurable modes
- Party chat, invites, kick/promote
- Follow modes: Automatic, Confirm, ReadyCheck, Manual

### Voice Chat
- **EOSVoiceManager** - RTC lobby-based voice (not P2P)
- Auto-connects when joining voice-enabled lobbies
- Per-participant mute, volume, speaking indicators
- Raw audio frame access for custom playback
- Persists through host migration
- XAudio2 auto-resolution on Windows

### Social
- **EOSFriends** - Friends list, requests, block/unblock (requires Epic Account)
- **EOSCustomInvites** - Cross-platform invitations with custom payloads
- **EOSPresence** - Online status and rich presence
- **EOSUserInfo** - Player display names and metadata
- **EOSPlayerRegistry** - PUID/name cache for fast lookups
- **EOSDiscordPresence** - Discord Rich Presence via named pipes (zero dependencies)

### Matchmaking & Social Discovery
- **EOSLFGManager** - Looking For Group post system
- **EOSRankedMatchmaking** - Ranked queue with skill-based matching

### Progression
- **EOSAchievements** - Unlock and track achievement progress
- **EOSStats** - Player statistics
- **EOSLeaderboards** - Rankings and score submission
- **EOSSeasonManager** - Seasonal progression
- **EOSTournamentManager** - Tournament brackets and matches

### Clans
- **EOSClanManager** - Persistent clans with roles (Owner, Officer, Member)
- Clan chat, settings, kick/ban, disband

### Storage
- **EOSPlayerDataStorage** - Cloud saves (400MB per player)
- **EOSTitleStorage** - Read-only title data from Developer Portal

### Replays
- **EOSReplayStorage** - Local + cloud replay storage
- **EOSReplayPlayer** - Playback with seek, pause, speed control
- **EOSReplayViewer** - Browse and manage replays

### Moderation & Safety
- **EOSAntiCheatManager** - Easy Anti-Cheat integration with peer validation
- **EOSReports** - Player behavior reporting (to Developer Portal moderation queue)
- **EOSSanctions** - Query player bans/restrictions
- **EOSReputationManager** - Player reputation/rating

### UI
- **EOSNativeStatusUI** - Runtime overlay (toggle with **F1**) with tabs:
  - **Status** - SDK init, login state, interface availability, quick actions
  - **Lobbies** - Create/join/search, member list, chat
  - **Voice** - Connection status, mute toggle, participant list with speaking indicators
  - **Social** - Friends, invites
- **EOSToastManager** - Non-intrusive toast notifications

### Editor Tools
- **Setup Wizard** - Guided credential entry (Tools > EOS Native > Setup Wizard)
- **Setup Scene** - One-click EOSManager creation (Tools > EOS Native > Setup Scene)
- **Validate Setup** - Check for common configuration issues
- **Debug Settings** - Per-category logging control
- **Custom Inspector** - EOSManager inspector with live status, quick actions, config validation

## Supported Platforms

| Platform | Library | Architecture |
|----------|---------|-------------|
| Windows | EOSSDK-Win64-Shipping.dll | x86_64 |
| Windows | EOSSDK-Win32-Shipping.dll | x86 |
| macOS | libEOSSDK-Mac-Shipping.dylib | Universal |
| Linux | libEOSSDK-Linux-Shipping.so | x86_64 |
| Linux | libEOSSDK-LinuxArm64-Shipping.so | ARM64 |
| iOS | EOSSDK.framework | ARM64 |
| Android | eossdk-StaticSTDC-release.aar | ARM64/ARMv7 |

## Usage

```csharp
using Epic.OnlineServices;
using Epic.OnlineServices.Platform;
using Epic.OnlineServices.Connect;
using Epic.OnlineServices.P2P;
using Epic.OnlineServices.Lobby;
using Epic.OnlineServices.RTC;
// ... all EOS namespaces available
```

Most managers are auto-creating singletons. Access them via `Instance`:

```csharp
// Create a lobby
var result = await EOSLobbyManager.Instance.CreateLobbyAsync(new CreateLobbyOptions
{
    LobbyName = "My Lobby",
    MaxPlayers = 4,
    IsPublic = true,
    EnableVoice = true
});

// Join by code
await EOSLobbyManager.Instance.JoinLobbyByCodeAsync("1234");

// Voice auto-connects. Toggle mute:
EOSVoiceManager.Instance.ToggleMute();

// Send a chat message
await EOSLobbyChatManager.Instance.SendChatMessageAsync("Hello!");
```

## Conditional Compilation

Add `EOS_DISABLE` to Scripting Define Symbols to strip EOS from compilation. Useful for builds without EOS or when the SDK isn't installed yet.

## Framework Compatibility

Works with **any** networking solution - FishNet, Mirror, Netcode for GameObjects, or custom. The companion **FishNet EOS Native Transport** (`com.tront.fishnet-eos-native`) uses this package for FishNet-specific transport.

## SDK Version

**EOS C# SDK v1.18.1.2** - unmodified from Epic's official distribution.

## Updating the SDK

1. Download the latest EOS C# SDK from the [Epic Developer Portal](https://dev.epicgames.com)
2. Replace `Runtime/EOSSDK/Source/` with the new SDK's `Source/` folder
3. Replace native libraries in `Runtime/EOSSDK/Plugins/` with the new SDK's binaries
4. Update the version in `package.json`

## Credits

- EOS SDK by Epic Games: https://dev.epicgames.com
- Packaged by Trent Sterling: https://tront.xyz
