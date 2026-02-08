# CLAUDE-UI.md

Detailed runtime UI reference. See CLAUDE.md for project overview and rules.

## F1 Overlay Tabs (EOSNativeStatusUI)

The runtime F1 overlay (`EOSNativeStatusUI.cs`, ~3100 lines) provides 6 tabs:

| Tab | Sections |
|-----|----------|
| **Status** | SDK status, platform info, interfaces, login actions |
| **Lobbies** | Current lobby, create/join/search, lobby members (with report & profile buttons), lobby chat |
| **Voice** | Voice status, local mic level bar, audio devices, participants, voice diagnostics |
| **Social** | Player registry, recently played (friend/block/invite), local friends (notes/join/invite), blocked players, invites (send/receive/requests), Epic Account, Epic Friends |
| **Stats** | Stats query/ingest, leaderboard rankings, achievements progress, ranked matchmaking |
| **Tools** | Cloud storage (files/write/delete), anti-cheat status, replay list/playback/export/import, session metrics, LFG posts |

Also includes a modal report popup triggered from lobby member list and a player profile popup (info button per member) showing name, platform, PUID, last seen, friend/block status, editable notes, and action buttons (friend, block, report, invite, kick).

## Canvas UI (EOSNativeCanvasUI)

A Canvas-based runtime UI (`EOSNativeCanvasUI.cs`, ~3500 lines) that works on Android/iOS where OnGUI may not render. Uses `UnityEngine.UI` (no TextMeshPro). All UI elements created at runtime in code — no prefabs, no scene objects. Full feature parity with the OnGUI overlay.

**Toggle:** Bottom-right corner "EOS" button (80x80), or 3-finger tap on mobile.

**Canvas setup:** `ScreenSpaceOverlay`, `sortingOrder: 9999`, `CanvasScaler` with `ScaleWithScreenSize` (540x960 reference, match 0.5).

**6 Tabs:** Status, Lobbies, Voice, Social, Stats, Tools (mirrors OnGUI overlay).

| Tab | Contents |
|-----|----------|
| **Status** | SDK status, auth, PUID, platform info, interfaces, login/logout actions |
| **Lobbies** | Current lobby info, create (name/max/public/voice/migrate), join by code, quick match, search, members (with profile info button), chat (Enter to send) |
| **Voice** | Voice status, mic level bar, mute toggle, audio device picker (input/output), participants with speaking indicators, voice diagnostics |
| **Social** | Player registry, recently played (with block/invite), local friends (notes editing, join, invite, cloud sync), blocked players (clear all), invites (send/receive/accept/reject, quick send), Epic Account (login/logout), Epic Friends (accept/reject invites) |
| **Stats** | Network stats (NAT, RTT, loss, bandwidth, per-peer table), Stats & Leaderboards (query, ingest, rankings), Achievements (progress, unlocks), Ranked Matchmaking (rating, rank, find/host) |
| **Tools** | Cloud Storage (files, write, delete), Anti-Cheat (status, session), Replays (list, play, export, import, favorites), Session Metrics (begin/end), LFG (create/browse/join posts) |

**Popups:**
- **Player Profile** — Triggered from lobby member info button. Name, platform, PUID, last seen, badges, editable notes, action buttons (friend/block/report/invite/kick). Dark overlay + centered panel.
- **Report** — Category selection from `EOSReports.GetAllCategories()`, send with status feedback. Triggered from profile popup.

**Default visibility:** Mobile = Canvas ON, OnGUI OFF. Editor/Desktop = OnGUI ON, Canvas toggle button always visible.

**Refresh:** `InvokeRepeating` at 1s interval updates the active tab. Mic level bar uses `Update()` for smooth animation.

**Singleton:** Same auto-create pattern as other managers (`FindAnyObjectByType + AddComponent + DontDestroyOnLoad`).

**asmdef dependency:** `EOSNative.asmdef` references `UnityEngine.UI` (built-in Unity module).

**Coexistence:** Both OnGUI (`EOSNativeStatusUI`) and Canvas UI (`EOSNativeCanvasUI`) can run simultaneously. Neither depends on the other.

## Ported Managers

These managers were ported from FishNet-EOS-Native with FishNet dependencies removed:

| Manager | Location | Description |
|---------|----------|-------------|
| EOSGlobalChatManager | `Social/` | Channel-based global chat (join/leave/mute, message history) |
| EOSReplayHighlights | `Replay/` | Auto-detect gameplay highlights (multi-kill, clutch, comeback) |
| EOSReplayVoicePlayer | `Replay/` | Voice playback during replay viewing |
| EOSReplayVoiceRecorder | `Replay/` | Record voice chat for replay storage |
| EOSMetrics | `Core/` | EOS Metrics API for session telemetry |
| EOSAfkManager | `Lobbies/` | Idle detection with auto-kick, host immunity, lobby broadcast |
| EOSVoteKickManager | `Lobbies/` | Democratic vote-kick with thresholds, veto, cooldowns |
| EOSMapVoteManager | `Lobbies/` | Map/mode voting with tie breakers and preset templates |
| EOSRematchManager | `Lobbies/` | Post-match rematch voting with auto-offer and team swap |
| EOSBackfillManager | `Lobbies/` | Join-in-progress, game phases, backfill requests, team balancing |

**Not ported** (too tightly coupled to FishNet): EOSReplayRecorder, EOSVoiceZoneManager, EOSVoiceTriggerZone.

## Runtime Console (EOSNativeConsole)

A Canvas-based runtime console (`EOSNativeConsole.cs`) that captures `Application.logMessageReceived` output. Works on Android/iOS where the built-in dev console is hard to read.

- **Toggle:** Bottom-left corner button with error count badge, or 3-finger tap
- **Canvas:** `ScreenSpaceOverlay`, `sortingOrder: 10000` (above Canvas UI at 9999)
- **Features:** Log/Warning/Error filter buttons with counts, collapse duplicate messages, clear button
- **Limits:** Max 200 entries, 60 visible lines, color-coded by log type
- **Panel:** Occupies bottom half of screen when open
- **Text area:** Uses a simple `Text` component with `VerticalWrapMode.Truncate` instead of `ScrollRect`/`RectMask2D`/`ContentSizeFitter` — eliminates text flickering on window resize caused by circular layout dependencies. Newest entries at top, overflow truncated at bottom.

## Setup Wizard (Editor Window)

`EOSSetupWizard.cs` (`EOSNative.Editor/`) provides an editor window accessible via **EOS Native > Setup Wizard** menu. Three tabs:

| Tab | Contents |
|-----|----------|
| **Setup** | EOS credential configuration — select or create config ScriptableObject, 4-step guide (Product Name, Product ID, Sandbox ID, Deployment ID + Client credentials), validation with quick-check button |
| **Dependencies** | ParrelSync (install/remove/open GitHub), Input System status, uGUI status. Install/remove edits `Packages/manifest.json` directly and calls `Client.Resolve()` |
| **About** | Package version (read from package.json), SDK version, description, link buttons (docs site, GitHub, Epic dev portal, EOS SDK docs), feature list (14 items), platform table (7 platforms), credits |

**ParrelSync integration:** The Dependencies tab can install ParrelSync via its git URL (`https://github.com/VeriorPies/ParrelSync.git?path=/ParrelSync`). Uses `IsPackageInstalled()` to check manifest.json for the package ID string and changes the button between Install/Remove accordingly.
