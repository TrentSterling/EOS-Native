# CLAUDE.md

Guidance for Claude Code working on the **EOS-Native** package.

## What This Is

A Unity Package Manager (UPM) package that bundles the official Epic Online Services (EOS) C# SDK with native libraries for all supported platforms. No wrappers, no middleware - just the raw SDK.

**Package name:** `com.tront.eos-native`
**Assembly name:** `Epic.OnlineServices`
**SDK version:** 1.18.1.2
**Unity minimum:** 2021.3

## Why It Exists

- Epic has no public CDN for the EOS SDK (requires developer portal login to download)
- OpenEOS exists but depends on a third-party maintainer
- PlayEveryWare is bloated with middleware we don't need
- This gives us full control over SDK distribution and versioning

## Who Uses This

The **FishNet EOS Native Transport** (`com.tront.fishnet-eos-native`) depends on this package for the `Epic.OnlineServices` assembly. Any Unity project needing EOS can use it.

## Package Structure

```
Assets/com.tront.eos-native/
+-- package.json                         (UPM manifest)
+-- README.md
+-- LICENSE.md
+-- Runtime/
    +-- Epic.OnlineServices.asmdef       (assembly definition)
    +-- EOSSDK/
        +-- Source/
        |   +-- Core/                    (13 files - marshalling, P/Invoke helpers)
        |   +-- Generated/               (1048+ files - auto-generated API bindings)
        |       +-- Achievements/
        |       +-- Auth/
        |       +-- Connect/
        |       +-- Lobby/
        |       +-- P2P/
        |       +-- RTC/, RTCAudio/, RTCData/
        |       +-- Friends/, Presence/, UserInfo/
        |       +-- Stats/, Leaderboards/
        |       +-- Sessions/, Sanctions/, Reports/
        |       +-- PlayerDataStorage/, TitleStorage/
        |       +-- AntiCheatClient/, AntiCheatServer/
        |       +-- Android/, IOS/, Windows/
        |       +-- [36 module folders total]
        +-- Plugins/
            +-- Windows/x64/             (EOSSDK-Win64-Shipping.dll + xaudio2)
            +-- Windows/x86/             (EOSSDK-Win32-Shipping.dll + xaudio2)
            +-- macOS/                   (libEOSSDK-Mac-Shipping.dylib)
            +-- Linux/                   (x64 + ARM64 .so files)
            +-- iOS/                     (EOSSDK.framework + .xcframework)
            +-- Android/                 (eossdk-StaticSTDC-release.aar)
```

## Assembly Definition

```json
{
    "name": "Epic.OnlineServices",
    "rootNamespace": "Epic.OnlineServices",
    "allowUnsafeCode": true,
    "autoReferenced": true,
    "defineConstraints": ["!EOS_DISABLE"]
}
```

- **Assembly name is `Epic.OnlineServices`** - matches what every EOS consumer expects
- **allowUnsafeCode: true** - required for P/Invoke interop with native DLLs
- **defineConstraints: `!EOS_DISABLE`** - add `EOS_DISABLE` to scripting defines to strip EOS from compilation (solves chicken-and-egg: transport code can compile without SDK present)

## Installation

**Via UPM git URL:**
```
https://github.com/TrentSterling/EOS-Native.git?path=Assets/com.tront.eos-native
```

**Manual:** Copy `Assets/com.tront.eos-native/` into target project's `Packages/` directory.

## Supported Platforms

| Platform | Library | Size |
|----------|---------|------|
| Windows x64 | EOSSDK-Win64-Shipping.dll | 19 MB |
| Windows x86 | EOSSDK-Win32-Shipping.dll | 15 MB |
| macOS Universal | libEOSSDK-Mac-Shipping.dylib | 46 MB |
| Linux x64 | libEOSSDK-Linux-Shipping.so | 26 MB |
| Linux ARM64 | libEOSSDK-LinuxArm64-Shipping.so | 23 MB |
| iOS ARM64 | EOSSDK.framework + .xcframework | ~25 MB |
| Android | eossdk-StaticSTDC-release.aar | 37 MB |

## How To Update The SDK

1. Download new EOS C# SDK from Epic Developer Portal
2. Replace `Runtime/EOSSDK/Source/` with the new SDK's `SDK/Source/`
3. Replace native libs in `Runtime/EOSSDK/Plugins/` from the new SDK's `SDK/Bin/`:
   - `Bin/EOSSDK-Win64-Shipping.dll` -> `Plugins/Windows/x64/`
   - `Bin/EOSSDK-Win32-Shipping.dll` -> `Plugins/Windows/x86/`
   - `Bin/x64/xaudio2_9redist.dll` -> `Plugins/Windows/x64/`
   - `Bin/x86/xaudio2_9redist.dll` -> `Plugins/Windows/x86/`
   - `Bin/libEOSSDK-Mac-Shipping.dylib` -> `Plugins/macOS/`
   - `Bin/libEOSSDK-Linux-Shipping.so` -> `Plugins/Linux/`
   - `Bin/libEOSSDK-LinuxArm64-Shipping.so` -> `Plugins/Linux/`
   - `Bin/Android/static-stdc++/aar/*.aar` -> `Plugins/Android/`
   - `Bin/IOS/` -> `Plugins/iOS/`
4. Open Unity, let it reimport
5. Verify platform targeting on new DLLs in Inspector
6. Bump version in `package.json`

## Origin / Provenance

- SDK source: Official Epic Online Services C# SDK v1.18.1.2
- Downloaded from: Epic Developer Portal (dev.epicgames.com)
- C# source and native libs are unmodified from Epic's distribution
- Only additions: package.json, asmdef, README, LICENSE, directory organization

## Related Projects

- **Transport:** `C:\Github\EOSTransport` (FishNet EOS Native Transport - depends on this)
- **Reference:** OpenEOS by RobProductions (similar concept, third-party maintained)
- **Reference:** eos-unity by dylanh724 (vanilla EOS integration, deprecated)

## Native DLL Loading (Editor)

`LoadNativeLibrary()` in `EOSManager.cs` (line ~1196) uses `AssetDatabase.FindAssets()` to locate the SDK DLL by name, then loads it via platform-specific calls (kernel32 `LoadLibrary` on Windows, `dlopen` on macOS/Linux).

**Critical:** `AssetDatabase.GUIDToAssetPath()` returns Unity virtual paths like `Packages/com.tront.eos-native/...` which native `LoadLibrary` cannot resolve. The path MUST be wrapped with `Path.GetFullPath()` to resolve to the actual filesystem path (e.g. `Library/PackageCache/com.tront.eos-native@<hash>/...`).

In builds, Unity copies DLLs to the output automatically so this is only an editor concern.

## XAudio2 DLL Path Resolution (Windows Voice/RTC)

The EOS SDK requires `xaudio2_9redist.dll` for RTC/Voice on Windows. The `GetXAudio2DllPath()` method in `EOSManager.cs` searches multiple candidate paths to support different installation layouts:

1. **UPM package:** `Packages/com.tront.eos-native/Runtime/EOSSDK/Plugins/Windows/x64/xaudio2_9redist.dll`
2. **Embedded in Assets:** `Assets/com.tront.eos-native/Runtime/EOSSDK/Plugins/Windows/x64/xaudio2_9redist.dll`
3. **Legacy flat:** `Assets/Plugins/EOSSDK/Windows/x64/xaudio2_9redist.dll`
4. **Alternative legacy:** `Assets/Plugins/Windows/x64/xaudio2_9redist.dll`

In builds, Unity copies DLLs to `<GameName>_Data/Plugins/x86_64/`.

If you see "Failed to load custom XAudio2.9 dll", verify the DLL exists at one of the candidate paths and that the path is resolving correctly.

## Singleton Auto-Creation Pattern

Most managers use a lazy auto-create singleton pattern: `FindAnyObjectByType<T>()` first, then `new GameObject + AddComponent<T>() + DontDestroyOnLoad()` if not found. This means consumers don't need to manually place manager components in the scene.

**Auto-create singletons:** EOSLobbyManager, EOSVoiceManager, EOSStats, EOSAchievements, EOSPartyManager, EOSAntiCheatManager, EOSPlayerRegistry, EOSReports, EOSSanctions, EOSCustomInvites, EOSFriends, EOSLeaderboards, EOSPresence, EOSUserInfo, EOSPlayerDataStorage, EOSTitleStorage, EOSMetrics, and more.

**Find-only singletons (no auto-create):** EOSTournamentManager, EOSClanManager, EOSGlobalChatManager, EOSReplayHighlights, EOSReplayVoicePlayer, EOSReplayVoiceRecorder.

**Note:** EOSLobbyChatManager was upgraded from find-only to auto-create in v2.2.0.

## Documentation Site

Full docsify documentation at `docs/` folder (39 files). Live at **https://tront.xyz/EOS-Native/** (GitHub Pages, `/docs` on `main` branch).

Covers: quickstart, setup, lobbies, voice, chat, auth, friends, party, clans, lfg, invites, discord, ranked, seasons, tournaments, leaderboards, achievements, reputation, match-history, replays, highlights, votekick, mapvote, backfill, rematch, afk, globalchat, parrelsync, android, anticheat, storage, architecture, platforms, debug, troubleshooting.

Style matches the FishNet EOS Native docs site with the same docsify theme, theme switcher, and code highlighting.

## Inspector Quick Match Behavior

The Inspector's Quick Match button calls `QuickMatchOrHostAsync()` which:
1. Searches for available lobbies
2. If found, joins a random one
3. **If none found, automatically creates and hosts a new lobby**

Both the Inspector (EOSManagerEditor) and the runtime F1 overlay (EOSNativeStatusUI) use this pattern. The Inspector also exposes:
- **Lobby Name** text field - sets `LobbyName` on creation (empty = unnamed)
- **Max Players** int field (2-64, default 4) - sets `MaxPlayers` on creation
- **Voice** toggle - controls `EnableVoice`
- **Host Migration** toggle - controls `AllowHostMigration`

All four fields apply to both Host Lobby and Quick Match lobby creation.

## Audio Device Selection (Mic/Speaker)

`EOSVoiceManager` provides runtime mic/speaker device switching via EOS RTCAudio APIs:

- `QueryAudioDevices()` - Queries input/output device lists and registers for hotplug notifications
- `GetInputDevices()` / `GetOutputDevices()` - Returns cached device info lists
- `SetInputDevice(deviceId)` - Switches active microphone by `RealDeviceId`
- `SetOutputDevice(deviceId)` - Switches active speaker by `RealDeviceId`
- `OnAudioDevicesChanged` event - Fires when devices are added/removed
- `CurrentInputDeviceId` / `CurrentOutputDeviceId` - Track selected device

The F1 overlay Voice tab exposes dropdown selectors for input/output devices with a Refresh button.

## F1 Overlay Tabs (EOSNativeStatusUI)

The runtime F1 overlay (`EOSNativeStatusUI.cs`, ~3100 lines) provides 6 tabs:

| Tab | Sections |
|-----|----------|
| **Status** | SDK status, platform info, interfaces, login actions |
| **Lobbies** | Current lobby, create/join/search, lobby members (with report & profile buttons), lobby chat |
| **Voice** | Voice status, local mic level bar, audio devices, participants |
| **Social** | Player registry, recently played (friend/block/invite), local friends (notes/join/invite), blocked players, invites (send/receive/requests), Epic Account, Epic Friends |
| **Stats** | Stats query/ingest, leaderboard rankings, achievements progress, ranked matchmaking |
| **Tools** | Cloud storage (files/write/delete), anti-cheat status, replay list/playback/export/import, session metrics, LFG posts |

Also includes a modal report popup triggered from lobby member list and a player profile popup (info button per member) showing name, platform, PUID, last seen, friend/block status, editable notes, and action buttons (friend, block, report, invite, kick).

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

## Android Build (Core Library Desugaring)

The EOS SDK AAR (`eossdk-StaticSTDC-release.aar`) requires Java 8 core library desugaring. Without it, Android builds fail with:

> `Dependency ':eossdk-StaticSTDC-release:' requires core library desugaring to be enabled for :launcher.`

`EOSAndroidBuildProcessor.cs` (in `EOSNative.Editor/`) automatically injects the required Gradle config into both `launcher/build.gradle` and `unityLibrary/build.gradle` via `IPostGenerateGradleAndroidProject`. It adds:
- `coreLibraryDesugaringEnabled true` in `compileOptions`
- `coreLibraryDesugaring 'com.android.tools:desugar_jdk_libs:2.1.4'` in `dependencies`

No manual Gradle template editing is required.

## Bug/TODO Tracking

See `BUGS.MD` and `TODO.MD` in the repo root for known issues and planned work.

## Rules

- **Do NOT modify the C# source** in Source/Core or Source/Generated. These are Epic's auto-generated files.
- **Do NOT remove platform support.** All platforms stay included.
- When updating the SDK, preserve the Plugins/ folder organization (by platform subfolder).
- **ALWAYS increment version** in `package.json` before each git push.
