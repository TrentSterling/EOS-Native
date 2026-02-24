# Map/Mode Voting

Let players vote on the next map or game mode.

## Overview

The map/mode voting system allows democratic selection of game settings. Features include:

- Support for maps, modes, or any custom options
- Configurable vote duration and tie breakers
- Live or hidden vote counts
- Allows vote changing
- Preset templates for common setups

## Basic Usage

### Simple Map Vote

```csharp
var mapVote = EOSMapVoteManager.Instance;

// Start a map vote with string names
mapVote.StartMapVote("Vote for Next Map",
    "Dust 2", "Inferno", "Mirage", "Nuke");
```

### Simple Mode Vote

```csharp
// Start a mode vote
mapVote.StartModeVote("Choose Game Mode",
    "Deathmatch", "Team DM", "Capture the Flag");
```

### Custom Options

```csharp
var options = new List<VoteOption>
{
    new VoteOption { Id = "dust2", DisplayName = "Dust 2", Category = "map" },
    new VoteOption { Id = "inferno", DisplayName = "Inferno", Category = "map" },
    new VoteOption { Id = "mirage", DisplayName = "Mirage", Category = "map" },
    new VoteOption { Id = "nuke", DisplayName = "Nuke", Category = "map" },
};

mapVote.StartVote("Vote for Next Map", options);
```

## Casting Votes

```csharp
// Vote by index (0-based)
mapVote.CastVote(0);  // Vote for first option
mapVote.CastVote(2);  // Vote for third option

// Vote by option ID
mapVote.CastVoteById("dust2");
mapVote.CastVoteById("inferno");
```

## Configuration

### Timing

```csharp
// Vote duration (10-120 seconds)
mapVote.VoteDuration = 30f;
```

### Vote Behavior

```csharp
// Allow players to change their vote
mapVote.AllowVoteChange = true;

// Show vote counts in real-time (false = reveal at end)
mapVote.ShowLiveResults = true;
```

### Tie Breaking

```csharp
// Random selection from tied options
mapVote.TieBreakerMode = TieBreaker.Random;

// First option in list wins ties
mapVote.TieBreakerMode = TieBreaker.FirstOption;

// Host must choose the winner
mapVote.TieBreakerMode = TieBreaker.HostChoice;

// Start a new vote with only tied options
mapVote.TieBreakerMode = TieBreaker.Revote;
```

## Checking Status

```csharp
// Is a vote active?
if (mapVote.IsVoteActive)
{
    var vote = mapVote.CurrentVote;

    Debug.Log($"Title: {vote.Title}");
    Debug.Log($"Total votes: {vote.TotalVotes}");
    Debug.Log($"Time left: {mapVote.TimeRemaining}s");
}

// Get my vote (-1 if not voted)
int myVote = mapVote.GetMyVote();

// Get vote counts per option
int[] counts = mapVote.GetVoteCounts();
for (int i = 0; i < counts.Length; i++)
{
    Debug.Log($"Option {i}: {counts[i]} votes");
}

// Get current leader(s)
List<int> leaders = mapVote.GetCurrentLeaders();
if (leaders.Count > 1)
{
    Debug.Log("Currently tied!");
}
```

## Host Controls

```csharp
// Extend the timer
mapVote.ExtendTimer(15f);  // Add 15 seconds

// End vote immediately (triggers result calculation)
mapVote.EndVoteNow();

// Cancel the vote entirely
mapVote.CancelVote();

// Resolve a tie (when TieBreaker is HostChoice)
mapVote.ResolveTie(winnerIndex);
```

## Events

```csharp
// Vote started
mapVote.OnVoteStarted += (voteData) =>
{
    Debug.Log($"Vote started: {voteData.Title}");
    foreach (var option in voteData.Options)
    {
        Debug.Log($"  - {option.DisplayName}");
    }
};

// Someone cast a vote
mapVote.OnVoteCast += (voterPuid, optionIndex) =>
{
    Debug.Log($"Player {voterPuid} voted for option {optionIndex}");
};

// Timer tick (every second)
mapVote.OnTimerTick += (secondsRemaining) =>
{
    Debug.Log($"{secondsRemaining} seconds left");
};

// Vote ended
mapVote.OnVoteEnded += (voteData, winningOption, winningIndex) =>
{
    Debug.Log($"Winner: {winningOption.DisplayName}");
    LoadMap(winningOption.Id);
};

// Tie needs host decision
mapVote.OnTieNeedsDecision += (tiedOptions) =>
{
    Debug.Log("Tie! Host must choose:");
    foreach (var option in tiedOptions)
    {
        Debug.Log($"  - {option.DisplayName}");
    }
};
```

## Preset Templates

The manager includes common presets for quick setup:

```csharp
// FPS maps
var fpsMaps = EOSMapVoteManager.CommonMaps.FPS;
// Contains: Dust II, Inferno, Mirage, Nuke

// Arena maps
var arenaMaps = EOSMapVoteManager.CommonMaps.Arena;
// Contains: Colosseum, The Pit, Tower, Arena

// Standard modes
var standardModes = EOSMapVoteManager.CommonModes.Standard;
// Contains: Deathmatch, Team Deathmatch, CTF, KOTH

// Casual modes
var casualModes = EOSMapVoteManager.CommonModes.Casual;
// Contains: Free For All, Infection, Gun Game

// Use presets
mapVote.StartVote("Vote for Map", fpsMaps.ToList());
```

## Implementation Example

### End-of-Match Flow

```csharp
public class MatchManager : MonoBehaviour
{
    private EOSMapVoteManager _mapVote;

    void Start()
    {
        _mapVote = EOSMapVoteManager.Instance;
        _mapVote.OnVoteEnded += HandleVoteEnded;
    }

    public void OnMatchEnded()
    {
        var maps = new[] { "Map A", "Map B", "Map C", "Random" };
        _mapVote.StartMapVote("Vote for Next Map", maps);
    }

    private void HandleVoteEnded(MapVoteData data, VoteOption winner, int index)
    {
        string nextMap = winner.Id;

        if (nextMap == "random")
        {
            nextMap = GetRandomMap();
        }

        StartCoroutine(LoadMapCoroutine(nextMap));
    }
}
```

## How It Works

1. **Host starts vote** - Options broadcast via lobby attribute
2. **Players vote** - Selections stored as member attributes
3. **Timer counts down** - Tick events fired every second
4. **Time expires** - Result calculated
5. **Winner determined** - Based on votes and tie breaker
6. **Result broadcast** - OnVoteEnded fired with winner

## Best Practices

### Do

- Keep option count reasonable (3-5 is ideal)
- Use clear, recognizable option names
- Show timer prominently in your UI
- Handle the "no votes" edge case (first option wins)

### Don't

- Use more than 8 options (can overwhelm players)
- Set very short timers (15s minimum recommended)
- Hide live results in casual games (frustrates players)
- Forget to handle the OnVoteEnded event

## Troubleshooting

### Votes not syncing

- Ensure all players are in the same lobby
- Check that member attribute updates are working
- Verify lobby attribute limit isn't exceeded

### Timer not accurate

- Timer is local to each client
- Small variations are normal

### Tie issues

- With TieBreaker.HostChoice, ensure you call ResolveTie
- Random tie breaking may feel unfair - consider Revote mode
- FirstOption can be predictable - shuffle options first
