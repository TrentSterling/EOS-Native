# Debug Tools

Runtime debugging and development tools.

## Debug Settings Window

`Tools > EOS Native > Debug Settings`

Configure per-category logging to filter noise during development.

## F1 Runtime Overlay

Press **F1** to toggle the main debug overlay with four tabs:

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
- Member list
- Text chat

### Voice Tab
- RTC connection status
- Mute toggle
- Participant list with speaking indicators
- Audio status per participant

### Social Tab
- Friends list
- Custom invites

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
| VoiceManager | Voice/RTC |
| Friends | Friends system |
| Leaderboards | Rankings |
| Achievements | Achievement tracking |
| Storage | Cloud storage |
| Party | Party system |

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
