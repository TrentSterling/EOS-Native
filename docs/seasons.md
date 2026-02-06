# Seasons & Resets

Seasonal progression system.

## Overview

EOSSeasonManager handles seasonal content rotation, progress tracking, and rating resets.

```csharp
var seasons = EOSSeasonManager.Instance;

// Get current season info
var current = seasons.CurrentSeason;
Debug.Log($"Season: {current.Name}, Ends: {current.EndDate}");
```

## Season Progress

```csharp
// Track player progress
var progress = seasons.GetProgress();
Debug.Log($"Level: {progress.Level}, XP: {progress.CurrentXP}/{progress.RequiredXP}");
```

## Events

```csharp
seasons.OnSeasonChanged += (newSeason) => { };
seasons.OnProgressUpdated += (progress) => { };
```
