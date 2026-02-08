# Canvas UI

Runtime Canvas-based UI for Android/iOS where OnGUI may not render. Uses `UnityEngine.UI` (no TextMeshPro dependency). All UI elements are created at runtime in code -- no prefabs, no scene objects required. Full feature parity with the [F1 OnGUI overlay](debug.md#f1-runtime-overlay).

**File:** `Runtime/EOSNative/UI/EOSNativeCanvasUI.cs` (~3500 lines)

## Toggle

- **Desktop/Editor:** Bottom-right corner "EOS" button (80x80 pixels)
- **Mobile:** 3-finger tap anywhere on screen

The toggle button is always visible on all platforms. On mobile, the Canvas UI opens by default. On desktop/editor, the OnGUI overlay is the default (Canvas toggle button still accessible).

## Canvas Setup

| Property | Value |
|----------|-------|
| Render Mode | `ScreenSpaceOverlay` |
| Sorting Order | `9999` |
| CanvasScaler Mode | `ScaleWithScreenSize` |
| Reference Resolution | 540 x 960 |
| Screen Match Mode | `MatchWidthOrHeight` (0.5) |

The high sorting order ensures the UI renders above game content but below the [Runtime Console](debug.md#runtime-console) (which uses 10000).

## Tabs

The Canvas UI mirrors the F1 overlay with six tabs:

| Tab | Contents |
|-----|----------|
| **Status** | SDK status, auth state, PUID, platform info, interfaces, login/logout actions |
| **Lobbies** | Current lobby info, create/join/search, members with profile buttons, chat |
| **Voice** | Voice status, mic level bar, mute toggle, audio device picker, participants |
| **Social** | Player registry, recently played, friends, blocked players, invites, Epic Account |
| **Stats** | Network stats, stats/leaderboards, achievements, ranked matchmaking |
| **Tools** | Cloud storage, anti-cheat, replays, session metrics, LFG |

### Status Tab

- SDK initialization state and auth status
- Product User ID (with copy button)
- Platform info and available EOS interfaces
- Login/logout action buttons (Connect login, Epic Account login)

### Lobbies Tab

- Current lobby info (code, role, member count, attributes)
- Create lobby controls: name, max players, public/private, voice toggle, host migration toggle
- Join by code input field
- Quick Match button (searches or creates)
- Search all available lobbies
- Member list with per-player info button (opens [Player Profile popup](#player-profile-popup))
- Lobby chat with text input (Enter to send)

### Voice Tab

- RTC connection status
- Local mic level bar (animated in `Update()` for smooth display)
- Mute/unmute toggle
- Audio device selection: input and output device buttons (green = selected), Refresh button
- Participant list with speaking indicators
- Voice diagnostics (interfaces, LocalAudioStatus, UpdateSending result, device counts)

### Social Tab

- **Player Registry** -- all known players with PUIDs
- **Recently Played** -- players from the last 7 days, with block/invite buttons
- **Local Friends** -- status indicators, editable notes, join/invite buttons, cloud sync
- **Blocked Players** -- list with unblock per player, clear all button
- **Invites** -- send/receive/accept/reject, quick-send to friends
- **Epic Account** -- login/logout for Epic Account Link
- **Epic Friends** -- query Epic friends, accept/reject friend requests

### Stats Tab

- **Network Stats** -- NAT type, RTT, packet loss, bandwidth in/out, per-peer table
- **Stats & Leaderboards** -- query stats, ingest test values, view leaderboard rankings
- **Achievements** -- progress bars, unlock status
- **Ranked Matchmaking** -- rating, rank tier, win/loss record, find/host/queue controls

### Tools Tab

- **Cloud Storage** -- file list with usage, write test data, delete files
- **Anti-Cheat** -- EAC status, session controls, auto-start toggle
- **Replays** -- local replay list, playback controls, timeline, export/import, favorites
- **Session Metrics** -- begin/end session, duration, session ID
- **LFG** -- create posts, browse/search, join requests

## Popups

### Player Profile Popup

Triggered by pressing the info button next to any lobby member. Displays a dark overlay with a centered panel containing:

- Display name, platform, Product User ID
- Last seen timestamp
- Badges (if any)
- Editable notes field (per-player, stored locally)
- Action buttons: Friend, Block, Report, Invite, Kick (host only)

Tap the dark overlay background to dismiss.

### Report Popup

Triggered from the Player Profile popup. Provides:

- Category selection from `EOSReports.GetAllCategories()` (Cheating, Exploiting, Offensive, Verbal Abuse, Spamming, etc.)
- Send button with status feedback text
- Back/cancel to return to the profile popup

## Overlay UI Mode

`EOSManager` exposes an `OverlayUIMode` enum to control which runtime UI is active. Set it via the Inspector on the EOSManager component.

| Mode | OnGUI (F1) | Canvas UI | Console |
|------|-----------|-----------|---------|
| **Auto** (default) | Desktop only | Mobile only | If `_showConsole` enabled |
| **OnGUI** | Yes | No | If enabled |
| **Canvas** | No | Yes | If enabled |
| **Both** | Yes | Yes | If enabled |
| **None** | No | No | If enabled |

**Default visibility by platform:**

| Platform | Default UI |
|----------|-----------|
| Android / iOS | Canvas UI on, OnGUI off |
| Editor / Windows / macOS / Linux | OnGUI on, Canvas toggle button visible |

The `_showConsole` bool independently controls the [Runtime Console](debug.md#runtime-console), regardless of overlay mode.

## Customization

### Disabling the Canvas UI

Set `OverlayUIMode` to `None` on the EOSManager component to disable all runtime UI. Alternatively, set it to `OnGUI` to use only the F1 overlay without the Canvas UI.

### Coexistence with OnGUI

Both `EOSNativeStatusUI` (OnGUI) and `EOSNativeCanvasUI` (Canvas) can run simultaneously when `OverlayUIMode` is set to `Both`. Neither depends on the other. They share the same underlying manager singletons (EOSLobbyManager, EOSVoiceManager, etc.) so state is always consistent.

### Singleton Pattern

`EOSNativeCanvasUI` follows the same auto-create singleton pattern as other managers:

```csharp
// Auto-created by EOSManager based on OverlayUIMode
// Can also be accessed directly:
var canvasUI = EOSNativeCanvasUI.Instance;
```

The singleton is created via `FindAnyObjectByType<EOSNativeCanvasUI>()` first, then `new GameObject` with `AddComponent` if not found. It parents itself under `EOSManager.Instance.transform` to keep the hierarchy clean.

### Refresh Behavior

- Tab content rebuilds via `InvokeRepeating` at 1-second intervals
- Only the active tab is refreshed (inactive tabs skip rebuild)
- Mic level bar updates in `Update()` for smooth animation
- UI elements are cleared and recreated each refresh cycle (`ClearChildren()` + rebuild)

## Assembly Dependency

The Canvas UI requires `UnityEngine.UI` (built-in Unity module). The `EOSNative.asmdef` already references this. No additional package imports are needed.
