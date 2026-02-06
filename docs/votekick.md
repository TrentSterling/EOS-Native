# Vote Kick

Allow players to vote to remove disruptive players from the lobby.

## Overview

The vote kick system lets players democratically remove problematic players. Features include:

- Configurable vote thresholds (majority, 2/3, unanimous, etc.)
- Host immunity and veto power
- Cooldowns to prevent spam
- Lobby attribute broadcast for vote state
- Host evaluates votes and executes kick

## Basic Usage

### Starting a Vote

```csharp
var voteKick = EOSVoteKickManager.Instance;

// Start a vote kick
var (success, error) = voteKick.StartVoteKick(targetPuid, "Toxic behavior");

if (!success)
{
    Debug.Log($"Failed to start vote: {error}");
}
```

### Casting Votes

```csharp
// Vote YES to kick the player
voteKick.CastVote(true);

// Vote NO to keep the player
voteKick.CastVote(false);
```

### Host Actions

```csharp
// Veto the vote (keeps the player, ends vote)
voteKick.VetoVote();
```

### Canceling a Vote

```csharp
// Only the initiator or host can cancel
voteKick.CancelVote();
```

## Configuration

### Vote Thresholds

```csharp
// Built-in thresholds
voteKick.Threshold = VoteThreshold.Majority;      // >50%
voteKick.Threshold = VoteThreshold.TwoThirds;     // >=67%
voteKick.Threshold = VoteThreshold.ThreeQuarters;  // >=75%
voteKick.Threshold = VoteThreshold.Unanimous;      // 100%

// Custom threshold
voteKick.Threshold = VoteThreshold.Custom;
voteKick.CustomThresholdPercent = 60;  // 60%
```

### Timing Settings

```csharp
voteKick.VoteTimeout = 30f;    // Seconds before vote expires (15-120)
voteKick.VoteCooldown = 60f;   // Seconds before same player can start another vote (30-300)
```

### Player Requirements

```csharp
voteKick.MinPlayersForVote = 3;  // Minimum players to start a vote (2-10)
```

### Host Settings

```csharp
voteKick.HostImmunity = true;   // Host cannot be vote kicked
voteKick.HostCanVeto = true;    // Host can veto any vote
```

### Other Options

```csharp
voteKick.RequireReason = false; // Require reason when starting vote
voteKick.Enabled = true;        // Enable/disable the system
```

## Checking Status

```csharp
// Is a vote currently active?
if (voteKick.IsVoteActive)
{
    var vote = voteKick.ActiveVote;
    Debug.Log($"Vote to kick {vote.TargetName}");
    Debug.Log($"Votes: {vote.YesVotes} YES / {vote.NoVotes} NO");
    Debug.Log($"Time remaining: {vote.TimeRemaining}s");
}

// Can a player be vote kicked?
bool canKick = voteKick.CanBeVoteKicked(puid);

// Has the local player voted?
bool voted = voteKick.HasVoted();

// How many YES votes needed to pass?
int required = voteKick.GetRequiredYesVotes();

// How many players can vote (excluding target)?
int eligible = voteKick.GetEligibleVoterCount();

// Cooldown remaining before you can start a vote
float cooldown = voteKick.GetCooldownRemaining();
```

## Events

```csharp
// Vote started
voteKick.OnVoteStarted += (voteData) =>
{
    Debug.Log($"{voteData.InitiatorName} started vote to kick {voteData.TargetName}");
    Debug.Log($"Reason: {voteData.Reason}");
};

// Someone cast a vote
voteKick.OnVoteCast += (voterPuid, votedYes) =>
{
    string vote = votedYes ? "YES" : "NO";
    Debug.Log($"Player {voterPuid} voted {vote}");
};

// Vote progress updated
voteKick.OnVoteProgress += (yesVotes, noVotes, totalEligible) =>
{
    Debug.Log($"Progress: {yesVotes}/{totalEligible} YES, {noVotes}/{totalEligible} NO");
};

// Vote ended
voteKick.OnVoteEnded += (voteData, result) =>
{
    switch (result)
    {
        case VoteResult.Passed:
            Debug.Log($"{voteData.TargetName} was kicked!");
            break;
        case VoteResult.Failed:
            Debug.Log("Vote failed - not enough YES votes");
            break;
        case VoteResult.Vetoed:
            Debug.Log("Host vetoed the vote");
            break;
        case VoteResult.TimedOut:
            Debug.Log("Vote timed out");
            break;
        case VoteResult.Cancelled:
            Debug.Log("Vote was cancelled");
            break;
    }
};

// Player was kicked via vote
voteKick.OnPlayerVoteKicked += (puid, name) =>
{
    Debug.Log($"{name} was removed by vote");
};
```

## Vote Results

| Result | Description |
|--------|-------------|
| Passed | Enough YES votes reached threshold, player kicked |
| Failed | Not enough YES votes possible (too many NO or abstains) |
| Vetoed | Host used veto power to cancel the vote |
| TimedOut | Vote expired before reaching a conclusion |
| Cancelled | Initiator cancelled, or target/initiator left |

## How It Works

1. **Initiator starts vote** - Vote data broadcast via lobby member attribute
2. **Players vote** - Votes stored as member attributes
3. **Host counts** - Lobby owner tallies votes
4. **Result determined** - Based on threshold or timeout
5. **Kick executed** - If passed, host kicks member via `EOSLobbyManager.KickMemberAsync()`

## Best Practices

### Do

- Set reasonable thresholds (2/3 is common for competitive games)
- Use cooldowns to prevent vote spam
- Show vote status in your game UI
- Allow hosts to veto against false accusations

### Don't

- Set unanimous threshold in large lobbies (one abstain fails vote)
- Allow vote kicks in ranked/competitive without safeguards
- Skip cooldowns (enables harassment via repeated votes)

## Troubleshooting

### Vote not starting

- Check `voteKick.Enabled` is true
- Verify minimum player count is met
- Check if you're on cooldown
- Ensure target isn't host (if host immunity enabled)

### Votes not counting

- Ensure players are in the same lobby
- Check that member attribute updates are working
- Verify players haven't already voted

### Vote not ending

- Check lobby owner is connected (owner processes vote logic)
- Verify timeout hasn't been set too high
