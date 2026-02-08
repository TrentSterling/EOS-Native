# Debug Tools

Runtime debugging and development tools.

## Debug Settings Window

`Tools > EOS Native > Debug Settings`

Configure per-category logging to filter noise during development.

## F1 Runtime Overlay

Press **F1** to toggle the main debug overlay with six tabs:

### Status Tab
- SDK initialization state
- Login status (Connect + Epic Account)
- Product User ID (with copy button)
- Network and app status
- Available EOS interfaces
- Quick action buttons (Init, Login, Logout)

### Lobbies Tab
- Current lobby info (code, role, members, attributes)
- Create lobby controls
- Join by code
- Search all lobbies
- Lobby members with report button per player
- Lobby chat with scrollable message log and send input

### Voice Tab
- RTC connection status
- Local mic level bar with mute/unmute toggle
- Input/output audio device selection dropdowns with refresh
- Participant list with speaking indicators and level bars
- Audio status per participant

### Social Tab
- Recently played players (last 7 days) with friend/block/invite buttons
- Local friends list with status indicators, notes, join/invite buttons, cloud sync
- Blocked players with unblock/clear
- Custom invites (send, receive, accept/reject, quick-send to friends)
- Epic Account login status
- Epic friends list (query, accept/reject requests)

### Stats Tab
- Stats query and ingest with test controls
- Leaderboard definitions and top rankings
- Achievements list with progress and unlock status
- Ranked matchmaking: rating, tier, win/loss record, find/host/queue controls

### Tools Tab
- Cloud storage: file list, usage, write/delete test controls
- Anti-cheat: status, session controls, auto-start toggle
- Replays: local replay list, playback controls, timeline, export/import
- Session metrics: begin/end, duration, session ID
- LFG: create/search/browse posts, join requests

### Report Popup
- Modal overlay triggered from lobby member list
- Category selection (Cheating, Exploiting, Offensive, Verbal Abuse, Spamming, etc.)
- Send report with status feedback

## Runtime Console

A Canvas-based runtime console (`EOSNativeConsole.cs`) that captures `Application.logMessageReceived` output. Works on Android/iOS where the built-in development console is hard to read.

**File:** `Runtime/EOSNative/UI/EOSNativeConsole.cs`

### Toggle

- **Desktop/Editor:** Bottom-left corner button with error count badge
- **Mobile:** 3-finger tap

### Features

| Feature | Details |
|---------|---------|
| Filter buttons | Log, Warning, Error -- each with running count |
| Collapse | Duplicate messages collapsed with count |
| Clear | Clears all entries |
| Color-coded | White (log), yellow (warning), red (error) |
| Max entries | 200 stored, 60 visible lines |
| Sorting order | 10000 (renders above Canvas UI at 9999) |
| Panel position | Bottom half of screen |

### Early Init

The console is created in `EOSManager.Awake()` before `LoadNativeLibrary()` and `LoadAndroidLibrary()`. This ensures that any errors during SDK initialization are captured and visible in the runtime console.

### Text Rendering

Uses a simple `Text` component with `VerticalWrapMode.Truncate` instead of `ScrollRect`/`RectMask2D`/`ContentSizeFitter`. This eliminates text flickering on window resize caused by circular layout dependencies. Newest entries appear at the top; overflow is truncated at the bottom.

### Enabling

The runtime console is controlled by the `_showConsole` bool on EOSManager, independent of the `OverlayUIMode` setting. It can run alongside both the F1 overlay and the Canvas UI.

## Canvas UI

The Canvas UI (`EOSNativeCanvasUI.cs`) is a full-featured runtime UI built with `UnityEngine.UI` for platforms where OnGUI may not render (Android, iOS). It provides the same six tabs as the F1 overlay -- Status, Lobbies, Voice, Social, Stats, Tools -- plus Player Profile and Report popups.

See the full [Canvas UI documentation](canvas-ui.md) for details on tabs, popups, overlay modes, and customization.

## Logging

### Using the Logger

```csharp
using EOSNative.Logging;

// Log with category
EOSDebugLogger.Log(DebugCategory.EOSManager, "EOSManager", "SDK initialized");

// Warning
EOSDebugLogger.LogWarning(DebugCategory.LobbyManager, "Lobby", "Lobby is full");

// Error
EOSDebugLogger.LogError("Voice", "Failed to connect to RTC");
```

### Debug Categories

| Category | Description |
|----------|-------------|
| EOSManager | SDK lifecycle |
| LobbyManager | Lobby operations |
| LobbyChatManager | Lobby text chat |
| VoiceManager | Voice/RTC |
| VoicePlayer | Per-participant audio |
| Friends | Friends system |
| Presence | Online presence |
| UserInfo | User info queries |
| CustomInvites | Custom invitations |
| Stats | Player statistics |
| Leaderboards | Rankings |
| Achievements | Achievement tracking |
| PlayerDataStorage | Player cloud saves |
| TitleStorage | Title data |
| Reports | Player reporting |
| Sanctions | Ban/restriction queries |
| Metrics | Session telemetry |
| PlayerRegistry | PUID/name cache |
| Replay | Replay system (highlights, voice recording/playback) |

## Toast Notifications

```csharp
var toasts = EOSToastManager.Instance;

await toasts.ShowToastAsync("Title", "Message");
```

Configure position and duration in the Inspector.

## Validate Setup

`Tools > EOS Native > Validate Setup`

Checks for common configuration issues:
- EOSConfig asset exists and is valid
- EOSManager is in scene
- Required components present
- Platform DLLs configured

## Development Tips

### Enable Verbose Logging

In Debug Settings window, enable all categories for full output during development.

### Testing with ParrelSync

1. Install ParrelSync
2. Create a clone
3. Main editor: Create lobby
4. Clone: Join with code
5. Verify connection in F1 overlay
