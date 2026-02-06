# Changelog

All notable changes to the EOS-Native package will be documented in this file.

## [2.5.0] - 2026-02-06

### Added
- **Canvas-based runtime UI (`EOSNativeCanvasUI`)** - New `UnityEngine.UI` Canvas overlay that works on Android/iOS where OnGUI may not render. All UI created at runtime in code (no prefabs). Toggle via bottom-right corner button or 3-finger tap.
  - 4 tabs: Status, Lobbies, Voice, Social
  - CanvasScaler with ScaleWithScreenSize for automatic DPI handling
  - ScrollRect for proper touch scrolling on mobile
  - Dark theme matching existing OnGUI overlay
  - Coexists with OnGUI overlay (both can run simultaneously)
- **Canvas-based runtime console (`EOSNativeConsole`)** - Captures `Application.logMessageReceived` for mobile-friendly log viewing. Bottom-left toggle button with error count badge, Log/Warning/Error filter buttons with counts, collapse duplicates, clear, auto-scroll. Max 200 entries, sortingOrder 10000.
- **Overlay UI Mode toggle** - `OverlayUIMode` enum on EOSManager (Auto/OnGUI/Canvas/Both/None) controls which runtime UI is active. Auto mode defaults to Canvas on mobile, OnGUI on desktop. Separate `_showConsole` toggle for the runtime console.
- Added `UnityEngine.UI` reference to `EOSNative.asmdef`
- **Unified `LobbyOptions` class** - Fluent builder that works for both creating and searching lobbies. One options object with implicit conversion to `LobbyCreateOptions` and `LobbySearchOptions`. Includes factory presets (`QuickMatch()`, `ForGameMode()`, `ForSkillRange()`) and platform/input filtering.
- **`LobbySearchOptions` platform & input filtering** - Added `WithPlatform()`, `DesktopOnly()`, `MobileOnly()`, `SamePlatformOnly()`, `WithInputType()`, `KeyboardMouseOnly()`, `ControllerOnly()`, `SameInputOnly()`, `FairInputOnly()`, `WithMinPlayers()`, `WithMaxPlayers()`, `ExcludeFull()` fluent methods
- Added `HOST_PLATFORM` and `HOST_INPUT_TYPE` lobby attribute constants
- Added `UseEosLobbyId` field to `LobbyCreateOptions`

### Fixed
- **Android native library loading crash** - `System.loadLibrary("EOSSDK")` failure no longer crashes the app; logged as warning and `EOSSDK.init(activity)` is always called (AAR handles loading internally)
- **Android native libs not extracted** - Build processor now injects `android:extractNativeLibs="true"` into AndroidManifest.xml to ensure AAR `.so` files are extracted at install time

## [2.4.2] - 2026-02-06

### Added
- `android.md` - Comprehensive Android build guide documenting both common errors, automatic fixes, manual fallbacks, Gradle project structure, and PlayEveryWare comparison

### Fixed
- **Android build failure (desugaring)** - EOS SDK AAR requires core library desugaring; `EOSAndroidBuildProcessor` auto-injects `coreLibraryDesugaringEnabled` and `desugar_jdk_libs` into both Gradle modules
- **Android build failure (missing resource)** - EOS AAR's AndroidManifest references `eos_login_protocol_scheme` string resource; build processor now auto-injects it into `strings.xml` using the client ID from EOSConfig asset (format: `eos.{clientid_lowercase}`)

## [2.4.0] - 2026-02-06

### Added

#### Player Profile Popup (F1 Overlay)
- Info button on each lobby member opens a modal profile popup
- Shows: display name, platform icon, PUID, last seen timestamp
- Status badges: [Friend], [Blocked], [Host]
- Editable personal notes per player (save/cancel)
- Action buttons: Add Friend/Unfriend, Block/Unblock, Report, Invite, Kick (host only)

#### Inspector Improvements
- Added **Lobby Name** text field to Inspector lobby controls (was unnamed)
- Added **Max Players** int field to Inspector lobby controls (was hardcoded to 4, now 2-64)
- Both fields apply to Host Lobby and Quick Match lobby creation

#### Documentation
- `parrelsync.md` - ParrelSync multiplayer testing support (reflection-based detection, zero config, device ID uniqueness, troubleshooting)
- Added ParrelSync link to docs sidebar under Advanced section

## [2.3.0] - 2026-02-06

### Added

#### New Managers (ported from FishNet-EOS-Native)
- `EOSAfkManager` - Automatic idle detection with configurable threshold, auto-kick with warning countdown, host immunity, lobby attribute broadcast
- `EOSVoteKickManager` - Democratic vote-kick with configurable thresholds (majority/2-3/3-4/unanimous/custom), host immunity, host veto, cooldowns, lobby attribute broadcast
- `EOSMapVoteManager` - Map/mode voting with tie breakers (random/first/host-choice/revote), live results, preset templates (FPS/Arena maps, Standard/Casual modes)
- `EOSRematchManager` - Post-match rematch voting with auto-offer, host force, swap teams toggle, keep settings toggle, consecutive rematch limits
- `EOSBackfillManager` - Join-in-progress controls, game phase management (7 phases), backfill requests, team balancing, game locking, auto-backfill

#### Lobby Manager Enhancement
- Added `KickMemberAsync(string targetPuid)` to `EOSLobbyManager` using EOS SDK `LobbyInterface.KickMember`

#### Documentation (9 new pages)
- `reputation.md` - Reputation & karma system (commendations, reports, levels, badges)
- `match-history.md` - Match history tracking (recording, querying, filtering, cloud sync)
- `globalchat.md` - Global chat channels (join/leave, messaging, moderation, history)
- `highlights.md` - Replay highlights (auto-detection, multi-kill, clutch, comeback, manual bookmarks)
- `votekick.md` - Vote kick system (thresholds, host controls, cooldowns)
- `mapvote.md` - Map/mode voting (options, tie breakers, presets, live results)
- `backfill.md` - Backfill & join-in-progress (game phases, JIP eligibility, team balancing)
- `rematch.md` - Rematch system (voting, host force, swap teams, consecutive limits)
- `afk.md` - AFK detection (idle tracking, auto-kick, integration with vote kick and backfill)
- Updated `_sidebar.md` with new "Lobby Systems" section and all new pages

## [2.2.0] - 2026-02-06

### Added

#### F1 Overlay Expansion (6 tabs, 17 new sections)
- Expanded F1 overlay from 4 tabs to 6 tabs: Status, Lobbies, Voice, Social, Stats, Tools
- **Lobbies tab:** Lobby Members section with per-member report button, Lobby Chat section with scrollable log and send input
- **Voice tab:** Local Mic Level section with mute toggle and level bar
- **Social tab:** Recently Played (friend/block/invite per player), Local Friends (notes, join, status indicators, cloud sync), Blocked Players (unblock/clear), Invites (send/receive/accept/reject, quick-send to friends), Epic Account status, Epic Friends (query/accept/reject)
- **Stats tab (new):** Stats query/ingest with test controls, Leaderboard definitions and rankings, Achievements progress list, Ranked Matchmaking (rating, tier, win/loss, find/host/queue)
- **Tools tab (new):** Cloud Storage (file list, write/delete, usage), Anti-Cheat (status, session, auto-start), Replays (list, playback controls, timeline, export/import), Session Metrics (begin/end, duration), LFG (create/search/browse posts, join requests)
- Modal report popup overlay with category selection triggered from lobby member list
- All foldout sections default to expanded

#### Auto-Create Singletons
- `EOSLobbyChatManager` now auto-creates via singleton pattern (was find-only, now matches other managers)

### Fixed
- `async void` discard pattern (`_ = method()`) causing CS8209 compile errors on 7 call sites
- Ranked matchmaking `OnDestroy` crash during application quit (JsonUtility.ToJson null reference when Unity is shutting down)

## [2.1.0] - 2026-02-06

### Added

#### Dark Theme UI Overhaul
- Completely redesigned F1 runtime overlay (`EOSNativeStatusUI.cs`) with FishNet-style dark theme
- Dark background textures, cyan headers, color-coded status labels (green/yellow/red/orange/cyan)
- Collapsible foldout sections with arrow indicators
- Tab buttons with active/hover states (Status, Lobbies, Voice, Social)
- Audio level bars with background/fill/peak rendering
- 28px action buttons and 20px small buttons with proper hover states

#### Mic/Speaker Device Selection
- Added audio device enumeration to `EOSVoiceManager`:
  - `QueryAudioDevices()` - query input/output devices
  - `SetInputDevice(deviceId)` - switch microphone
  - `SetOutputDevice(deviceId)` - switch speakers
  - `InputDevices` / `OutputDevices` cached device lists
  - `CurrentInputDeviceId` / `CurrentOutputDeviceId` properties
  - `OnAudioDevicesChanged` event for hotplug detection
- Voice tab in F1 overlay now shows input/output device dropdowns with refresh button
- Speaking indicators with unicode circles and level bars

#### Host Migration Toggle
- Added host migration toggle to F1 overlay Lobbies tab
- Added host migration toggle to Inspector lobby controls
- Passed through to `LobbyCreateOptions.AllowHostMigration` on lobby creation

#### New Managers (ported from FishNet-EOS-Native)
- `EOSGlobalChatManager` - Global chat channels with join/leave/mute, message history, channel user tracking
- `EOSReplayHighlights` - Auto-detect gameplay highlights (multi-kill, clutch, comeback, headshot, objectives)
- `EOSReplayVoicePlayer` - Voice playback during replay viewing with per-speaker volume/mute
- `EOSReplayVoiceRecorder` - Record voice chat during gameplay for replay storage with GZip compression
- `EOSMetrics` - EOS Metrics API integration for player session telemetry

#### Debug System
- Added `DebugCategory.Replay` for replay-related log filtering

### Fixed
- XAudio2 DLL path resolution for UPM package layout (multi-path search)
- Native DLL loading path fix (`Path.GetFullPath()` for Unity virtual paths)
- Quick Match now auto-hosts when no lobbies found
- `CompressionLevel` ambiguity in `EOSReplayVoiceRecorder` (fully qualified `System.IO.Compression.CompressionLevel`)
- Removed CS1998 warnings in `EOSGlobalChatManager` (async methods that don't await)

## [2.0.0] - 2026-02-06

### Added
- Initial release as standalone UPM package
- Full EOS C# SDK v1.18.1.2 with native libraries for all platforms
- High-level EOSNative wrapper layer:
  - `EOSManager` - SDK initialization, auth, platform tick
  - `EOSLobbyManager` - Lobby CRUD, search, join by code
  - `EOSLobbyChatManager` - In-lobby text chat
  - `EOSVoiceManager` - RTC voice chat
  - `EOSVoicePlayer` - Per-participant audio playback
  - `EOSPlayerRegistry` - Player discovery, friends, blocked, notes
  - `EOSFriends` - EOS friends list
  - `EOSPresence` - Online presence
  - `EOSUserInfo` - User info queries
  - `EOSStats` / `EOSStatsHelper` - Stats ingestion/query
  - `EOSLeaderboards` - Leaderboard queries
  - `EOSAchievements` - Achievement unlock/query
  - `EOSReports` - Player reporting
  - `EOSSanctions` - Sanction queries
  - `EOSCustomInvites` - Custom invite payloads
  - `EOSPartyManager` - Party system
  - `EOSAntiCheatManager` - Anti-cheat integration
  - `EOSPlayerDataStorage` / `EOSTitleStorage` - Cloud storage
  - `EOSDiscordPresence` - Discord rich presence
  - `EOSRankedMatchmaking` - Ranked/MMR system
  - `EOSSeasonManager` - Seasons/battle pass
  - `EOSTournamentManager` - Tournament brackets
  - `EOSClanManager` - Clan system
  - `EOSLFGManager` - Looking-for-group
  - `EOSMatchHistory` - Match history tracking
  - `EOSReputationManager` - Player reputation
  - `EOSReplayPlayer` / `EOSReplayViewer` / `EOSReplayStorage` - Replay system
- Editor tools: Setup Wizard, Menu tools, Debug Settings Window
- Runtime F1 overlay for testing
- Full docsify documentation site
