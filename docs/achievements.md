# Achievements

Game achievements with progress tracking.

## Loading Achievements

```csharp
var achievements = EOSAchievements.Instance;

// Load definitions from portal
await achievements.LoadDefinitionsAsync();

// Load player's unlocked achievements
await achievements.LoadPlayerAchievementsAsync();

Debug.Log($"Unlocked: {achievements.UnlockedCount}/{achievements.TotalAchievements}");
```

## Unlocking

```csharp
await achievements.UnlockAchievementAsync("first_win");
```

## Progress

```csharp
await achievements.ProgressAchievementAsync("play_100_games", 0.5f);  // 50%
```

## Events

```csharp
achievements.OnAchievementsLoaded += () => { };
achievements.OnAchievementUnlocked += (achievementId) => { };
```
