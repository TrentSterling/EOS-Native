# Leaderboards & Stats

Player statistics and global rankings.

## Stats

```csharp
var stats = EOSStats.Instance;

// Ingest a stat
await stats.IngestStatAsync("kills", 5);
await stats.IngestStatAsync("wins", 1);
```

## Leaderboards

Leaderboards must be defined in the EOS Developer Portal first.

### Loading Definitions

```csharp
var boards = EOSLeaderboards.Instance;

await boards.LoadDefinitionsAsync();
var definitions = boards.Definitions;
```

### Querying Rankings

```csharp
var ranks = await boards.QueryRanksAsync("global_kills");

foreach (var entry in ranks)
{
    Debug.Log($"#{entry.Rank}: {entry.DisplayName} - {entry.Score}");
}
```

### Player Rank

```csharp
var myRank = await boards.QueryPlayerRankAsync("global_kills", localPuid);
Debug.Log($"My rank: #{myRank.Rank}");
```

## Events

```csharp
boards.OnDefinitionsLoaded += () => { };
boards.OnRanksLoaded += (leaderboardId, entries) => { };
```
