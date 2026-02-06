# Ranked Matchmaking

Skill-based matchmaking with ELO/Glicko-2 ranking.

## Overview

EOSRankedMatchmaking provides queue-based matchmaking with skill rating tracking.

```csharp
var ranked = EOSRankedMatchmaking.Instance;
```

## Queuing

```csharp
// Join ranked queue
await ranked.JoinQueueAsync("competitive");

// Leave queue
await ranked.LeaveQueueAsync();
```

## Rating

Player skill ratings are tracked and used for match quality:

```csharp
// Get current rating
var rating = ranked.CurrentRating;
Debug.Log($"Rating: {rating.Score}, Tier: {rating.Tier}");
```

## Events

```csharp
ranked.OnMatchFound += (matchData) => { };
ranked.OnQueueUpdated += (position, estimatedWait) => { };
ranked.OnRatingChanged += (oldRating, newRating) => { };
```
