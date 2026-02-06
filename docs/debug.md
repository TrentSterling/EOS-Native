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
