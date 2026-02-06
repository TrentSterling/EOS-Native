# Tournament Brackets

Tournament creation and match management.

## Overview

EOSTournamentManager handles bracket generation, match scheduling, and results tracking.

```csharp
var tournaments = EOSTournamentManager.Instance;
```

## Creating a Tournament

```csharp
await tournaments.CreateTournamentAsync(new TournamentOptions
{
    Name = "Weekly Cup",
    MaxParticipants = 16,
    Format = TournamentFormat.SingleElimination
});
```

## Registering Players

```csharp
await tournaments.RegisterPlayerAsync(tournamentId);
```

## Match Management

```csharp
// Get current match
var match = tournaments.GetCurrentMatch();

// Report result
await tournaments.ReportMatchResultAsync(matchId, winnerId);
```

## Events

```csharp
tournaments.OnTournamentCreated += (tournament) => { };
tournaments.OnMatchStarted += (match) => { };
tournaments.OnMatchCompleted += (match, winner) => { };
tournaments.OnTournamentCompleted += (tournament, winner) => { };
```
