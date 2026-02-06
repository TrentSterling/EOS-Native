# Discord Rich Presence

Zero-dependency Discord Rich Presence integration via named pipes. No Discord SDK required.

## Setup

```csharp
var discord = EOSDiscordPresence.Instance;

// Connect to Discord
discord.Connect();
```

## Auto-Updates

When configured, presence auto-updates based on lobby state:
- In lobby → Shows party info, player count
- In game → Shows game mode, map
- Idle → Shows idle status

## Manual Updates

```csharp
discord.UpdatePresence();
```

## Events

```csharp
discord.OnConnected += () => { };
discord.OnDisconnected += () => { };
discord.OnError += (error) => { };
```

## How It Works

Communicates with Discord via named pipes (`discord-ipc-0`) - no external DLLs or SDKs needed. Works on Windows, Mac, and Linux.
