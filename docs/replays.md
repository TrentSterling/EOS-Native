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
