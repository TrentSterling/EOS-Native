# Recording & Playback

Record and replay game sessions with local + cloud storage.

## Overview

The replay system provides:
- **EOSReplayStorage** - Save/load replays locally and to cloud
- **EOSReplayPlayer** - Playback with timeline controls
- **EOSReplayViewer** - Browse and manage replays

## Saving Replays

```csharp
var storage = EOSReplayStorage.Instance;

await storage.SaveReplayAsync(replayData);
```

## Loading Replays

```csharp
var replay = await storage.LoadReplayAsync(replayId);
```

## Cloud Sync

```csharp
// Upload to EOS Player Data Storage
await storage.UploadToCloudAsync(replayId);

// Download from cloud
await storage.DownloadFromCloudAsync(replayId);
```

## Playback

```csharp
var player = EOSReplayPlayer.Instance;

// Play, pause, seek
player.Play(replayData);
player.Pause();
player.Seek(timeInSeconds);
player.SetPlaybackSpeed(2f);  // 2x speed
```

## Storage Limits

| Limit | Value |
|-------|-------|
| Local replays | 50 max |
| Cloud replays | 10 max |

## Events

```csharp
storage.OnLocalReplaysChanged += () => { };
storage.OnReplaySaved += (replayId) => { };
storage.OnReplayDeleted += (replayId) => { };
```

## Highlights

`EOSReplayHighlights` automatically detects notable gameplay moments for replay bookmarking.

### Setup

```csharp
var highlights = EOSReplayHighlights.Instance;

// Start detecting highlights during gameplay
highlights.StartDetection();

// Stop when game ends
highlights.StopDetection();
```

### Highlight Types

| Type | Description |
|------|-------------|
| MultiKill | Multiple kills in quick succession |
| Clutch | Won a round while last alive |
| Comeback | Team recovered from significant deficit |
| Headshot | Precision kill |
| Objective | Key objective completed |
| Victory | Match/round win |
| Manual | Player-triggered bookmark |
| Custom | Game-defined highlight |

### Recording Events

```csharp
// Report gameplay events for automatic detection
highlights.ReportKill();
highlights.ReportDeath();
highlights.ReportObjective("bomb_planted");
highlights.ReportHeadshot();

// Manual bookmark
highlights.AddManualHighlight("Nice play!");

// Custom highlight
highlights.AddCustomHighlight("Easter egg found", HighlightImportance.Medium);
```

### Importance Levels

| Level | Value | Description |
|-------|-------|-------------|
| Low | 0 | Minor moments |
| Medium | 1 | Notable plays |
| High | 2 | Outstanding moments |
| Epic | 3 | Once-in-a-game highlights |

### Events

```csharp
highlights.OnHighlightDetected += (highlight) =>
{
    Debug.Log($"Highlight: {highlight.Type} ({highlight.Importance})");
};
```

## Voice Recording

`EOSReplayVoiceRecorder` captures voice chat audio during gameplay for inclusion in replays.

### Recording

```csharp
var voiceRecorder = EOSReplayVoiceRecorder.Instance;

// Start recording voice for a replay session
voiceRecorder.StartRecording();

// Stop and get recorded data
voiceRecorder.StopRecording();

// Export voice data (GZip compressed)
byte[] voiceData = voiceRecorder.ExportVoiceData();
```

### Voice Playback in Replays

`EOSReplayVoicePlayer` plays back recorded voice during replay viewing.

```csharp
var voicePlayer = EOSReplayVoicePlayer.Instance;

// Load voice data
voicePlayer.LoadVoiceData(voiceData);

// Playback follows the replay player state
// Volume/mute per speaker
voicePlayer.SetSpeakerVolume(puid, 0.5f);
voicePlayer.SetSpeakerMuted(puid, true);

// Seek to match replay position
voicePlayer.Seek(timeInSeconds);
```

## Metrics

`EOSMetrics` tracks player sessions for Developer Portal analytics.

```csharp
var metrics = EOSMetrics.Instance;

// Sessions auto-start on login when AutoTrackSessions is enabled
// Manual control:
metrics.BeginSession(displayName: "Player1");
metrics.EndSession();

// Check state
bool active = metrics.IsSessionActive;
TimeSpan duration = metrics.SessionDuration;
```
