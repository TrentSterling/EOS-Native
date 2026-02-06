# AFK Detection

Detect and handle idle players in lobbies.

## Overview

The AFK system provides:
- Automatic idle detection based on input activity
- Warning notifications before auto-kick
- Per-player AFK tracking via lobby attributes
- Host-only kick evaluation
- Configurable thresholds and penalties

## Basic Usage

```csharp
var afk = EOSAfkManager.Instance;

// Enable AFK detection
afk.Enabled = true;
afk.AfkThreshold = 120f;  // 2 minutes idle = AFK
afk.AutoKick = true;
afk.AutoKickDelay = 30f;  // 30s warning before kick
```

## How It Works

1. **Input monitoring** - Tracks mouse/keyboard activity locally
2. **AFK flagged** - After `AfkThreshold` seconds without input, player is marked AFK
3. **Lobby broadcast** - AFK state shared via lobby member attribute
4. **Warning** - `OnAutoKickWarning` fires with countdown
5. **Auto-kick** - Host kicks the player after `AutoKickDelay` if still AFK
6. **Return** - Any input clears AFK status immediately

## Events

```csharp
// Player went AFK
afk.OnPlayerAfk += (puid, name) =>
{
    Debug.Log($"{name} is now AFK");
};

// Player returned from AFK
afk.OnPlayerReturned += (puid, name) =>
{
    Debug.Log($"{name} is back");
};

// Auto-kick warning (fires each second during countdown)
afk.OnAutoKickWarning += (puid, name, secondsRemaining) =>
{
    Debug.Log($"{name} will be kicked in {secondsRemaining}s");
};

// Player was auto-kicked
afk.OnPlayerAutoKicked += (puid, name) =>
{
    Debug.Log($"{name} was kicked for being AFK");
};
```

## Checking AFK Status

```csharp
// Is a specific player AFK?
bool isAfk = afk.IsPlayerAfk(puid);

// Is the local player AFK?
bool localAfk = afk.IsLocalPlayerAfk;

// Get time since last input
float idleTime = afk.TimeSinceLastInput;

// Get all AFK players
var afkPlayers = afk.GetAfkPlayers();
foreach (var (puid, name) in afkPlayers)
{
    Debug.Log($"{name} is AFK");
}
```

## Configuration

### Inspector Settings

| Setting | Default | Description |
|---------|---------|-------------|
| Enabled | true | Enable AFK detection |
| AFK Threshold | 120s | Seconds idle before AFK |
| Auto Kick | false | Kick AFK players automatically |
| Auto Kick Delay | 30s | Warning time before kick |
| Host Immunity | true | Host cannot be AFK kicked |
| Broadcast Interval | 10s | How often to sync AFK state |

### Runtime Configuration

```csharp
afk.Enabled = true;
afk.AfkThreshold = 180f;     // 3 minutes
afk.AutoKick = true;
afk.AutoKickDelay = 60f;     // 1 minute warning
afk.HostImmunity = true;
```

## Integration

### With Vote Kick

```csharp
afk.OnPlayerAfk += (puid, name) =>
{
    // Suggest vote kick for AFK player
    if (EOSVoteKickManager.Instance.CanBeVoteKicked(puid))
    {
        ShowAfkVoteKickPrompt(puid, name);
    }
};
```

### With Backfill

```csharp
afk.OnPlayerAutoKicked += (puid, name) =>
{
    // Request backfill for kicked player's slot
    EOSBackfillManager.Instance.RequestBackfill(slots: 1);
};
```

## Best Practices

1. **Set appropriate thresholds** - 2-3 minutes for competitive, 5+ for casual
2. **Warn before kicking** - Always give players time to return
3. **Grant host immunity** - Hosts may be managing settings
4. **Use with backfill** - Replace kicked players automatically
5. **Show AFK indicators** - Display AFK badges in player lists
