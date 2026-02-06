# Changelog

All notable changes to the EOS-Native package will be documented in this file.

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
