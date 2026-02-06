# Anti-Cheat (EAC)

Easy Anti-Cheat integration with peer validation.

## Overview

EOSAntiCheatManager wraps EOS Easy Anti-Cheat for client-side protection and peer-to-peer validation.

## Setup

1. Enable EAC in [EOS Developer Portal](https://dev.epicgames.com/portal)
2. Install EAC service on development machine
3. Configure in EOSManager Inspector

## Usage

```csharp
var ac = EOSAntiCheatManager.Instance;

// Start anti-cheat session
await ac.StartSessionAsync();

// End session
await ac.EndSessionAsync();
```

## Peer Registration

```csharp
// Register peer for validation
await ac.RegisterPeerAsync(peerHandle);

// Unregister
await ac.UnregisterPeerAsync(peerHandle);
```

## Game Round Tracking

```csharp
await ac.LogGameRoundStartAsync();
await ac.LogGameRoundEndAsync();
```

## Events

```csharp
ac.OnSessionStarted += () => { };
ac.OnSessionEnded += () => { };
ac.OnIntegrityViolation += (details) => { };
ac.OnPeerActionRequired += (peerHandle, action) => { };
ac.OnPeerAuthStatusChanged += (peerHandle, status) => { };
```

## Auto-Start

Enable `AutoStartSession` in the Inspector to auto-start EAC when joining a lobby.

## Platform Support

| Platform | EAC Support |
|----------|-------------|
| Windows | Yes |
| Mac | Yes |
| Linux | Yes |
| Android | No |
| iOS | No |
