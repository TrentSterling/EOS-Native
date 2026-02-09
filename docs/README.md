# EOS Native

> Epic Online Services (EOS) C# SDK packaged for Unity. No wrappers. No middleware. Just the SDK + batteries included.

## Features

- **Raw EOS SDK** - Official C# SDK v1.18.1.2, unmodified
- **All Platforms** - Windows, Mac, Linux, Android, iOS
- **Auto-Setup** - Drop EOSManager in scene, hit play
- **Lobbies** - 4-digit join codes, quick match, host migration
- **Voice Chat** - RTC lobby-based, auto-connects, mic/speaker device selection
- **Text Chat** - Lobby chat + global chat channels
- **Party System** - Persistent cross-game groups with follow-the-leader
- **Friends** - Epic Account friends, requests, block/unblock
- **Ranked** - ELO/Glicko-2 skill-based matchmaking
- **Achievements** - Unlock and track progress
- **Leaderboards** - Rankings and score submission
- **Cloud Storage** - 400MB per player saves
- **Anti-Cheat** - Easy Anti-Cheat integration
- **Discord Presence** - Zero-dependency Rich Presence
- **Replay Highlights** - Auto-detect multi-kill, clutch, comeback moments
- **Session Metrics** - Player session telemetry for Developer Portal
- **F1 Debug Overlay** - Dark theme with 6 tabs: status, lobbies, voice, social, stats, tools

## Requirements

- **Unity 2021.3+** (tested with Unity 6)
- **EOS Developer Portal** account for credentials

> **Note:** This package uses the raw EOS C# SDK directly - no PlayEveryWare plugin needed.

## Quick Example

```csharp
// Everything auto-initializes. Just use the managers:
var lobby = EOSLobbyManager.Instance;

// Create a lobby with voice
var result = await lobby.CreateLobbyAsync(new CreateLobbyOptions
{
    LobbyName = "My Lobby",
    MaxPlayers = 4,
    EnableVoice = true
});

// Join by code
await lobby.JoinLobbyByCodeAsync("1234");

// Toggle voice mute
EOSVoiceManager.Instance.ToggleMute();

// Send chat
await EOSLobbyChatManager.Instance.SendChatMessageAsync("Hello!");
```

## Getting Started

1. [Quick Start Guide](quickstart.md) - Get up and running in minutes
2. [Setup Guide](setup.md) - Configure EOS credentials
3. [Lobbies](lobbies.md) - Learn the lobby system

## Documentation Sections

| Section | Description |
|---------|-------------|
| **Core Features** | Lobbies, Voice, Chat, Authentication |
| **Social** | Friends, Parties, Invites, Clans, Discord |
| **Competitive** | Ranked Matchmaking, Leaderboards, Achievements |
| **Replay System** | Recording, Playback, Highlights, Voice Recording |
| **Advanced** | Anti-Cheat, Cloud Storage, Architecture |

## Links

- [Blog Post](https://blog.tront.xyz/posts/eos-native/)
- [TrontCloud](https://tront.xyz/trontcloud/) - Optional persistent backend for stats, leaderboards, and achievements

## Support

- [Troubleshooting](troubleshooting.md)
- [GitHub Issues](https://github.com/TrentSterling/EOS-Native/issues)
- [EOS Developer Portal](https://dev.epicgames.com/portal)
