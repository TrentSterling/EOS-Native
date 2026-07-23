# Quick Start

Get EOS Native running in your Unity project.

## Installation

### Via UPM Git URL (Recommended)

1. Open Unity
2. Window > Package Manager
3. Click "+" > "Add package from git URL"
4. Enter: `https://github.com/TrentSterling/eos-native.git?path=Assets/com.tront.eos-native`

### Manual

Copy the `com.tront.eos-native` folder into your project's `Packages/` directory.

## Setup

1. Go to `Tools > EOS Native > Setup Wizard`
2. Enter your credentials from [Epic Developer Portal](https://dev.epicgames.com/portal)
3. Click **Create Config**
4. Add an **EOSManager** to your scene (or use `Tools > EOS Native > Setup Scene`)
5. Enable **Auto Initialize** and **Auto Login** in the Inspector
6. Hit Play

Everything auto-creates. No manual component wiring needed.

## Basic Usage

### Creating a Lobby

```csharp
var lobby = EOSLobbyManager.Instance;

// Simple create (generates random 4-digit code)
var result = await lobby.CreateLobbyAsync(new CreateLobbyOptions
{
    LobbyName = "My Room",
    MaxPlayers = 4,
    IsPublic = true,
    EnableVoice = true
});

Debug.Log($"Code: {lobby.CurrentLobby.JoinCode}");
```

### Joining a Lobby

```csharp
// Join by code
await EOSLobbyManager.Instance.JoinLobbyByCodeAsync("1234");
```

### Quick Match

```csharp
// Find a lobby or create one
await EOSLobbyManager.Instance.QuickMatchAsync();
```

### Leaving

```csharp
await EOSLobbyManager.Instance.LeaveLobbyAsync();
```

### Checking State

```csharp
var lobby = EOSLobbyManager.Instance;

if (lobby.IsInLobby)
    Debug.Log($"In lobby: {lobby.CurrentLobby.JoinCode}");

if (lobby.IsOwner)
    Debug.Log("I am the host");
```

## Voice Chat

Voice auto-connects when joining a voice-enabled lobby:

```csharp
var voice = EOSVoiceManager.Instance;

// Mute/unmute
voice.ToggleMute();
voice.SetMuted(true);

// Check state
bool muted = voice.IsMuted;
bool connected = voice.IsConnected;
```

## Text Chat

```csharp
var chat = EOSLobbyChatManager.Instance;

// Send a message
await chat.SendChatMessageAsync("Hello everyone!");

// Listen for messages
chat.OnChatMessageReceived += (sender, message) =>
{
    Debug.Log($"{sender}: {message}");
};
```

## Debug Overlay

Press **F1** to toggle the runtime debug overlay with tabs for:
- **Status** - SDK init, login state, quick actions
- **Lobbies** - Create/join/search, member list, chat
- **Voice** - Connection status, mute, participants
- **Social** - Friends, invites

## Next Steps

- [Connecting: Lobby to Networking](connecting.md) - How to go from lobby to spawned players
- [Setup Guide](setup.md) - Detailed credential configuration
- [Lobbies](lobbies.md) - Deep dive into lobby features
- [Networking Overview](networking.md) - SyncVars, RPCs, spawning
- [Voice Chat](voice.md) - Voice communication details
- [Party System](party.md) - Persistent cross-game groups
