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

## Unified LobbyOptions (Fluent Builder)

`LobbyOptions` is a single class that works for both creating and searching lobbies. It implicitly converts to `LobbyCreateOptions` or `LobbySearchOptions` so the same object can be passed to any lobby API.

```csharp
// Fluent builder — one object for everything
var options = new LobbyOptions()
    .WithName("Pro Players")
    .WithGameMode("deathmatch")
    .WithMaxPlayers(16)
    .WithVoice()
    .WithRegion("us-east");

// Works for both creating and searching (implicit conversion)
await lobbyMgr.CreateLobbyAsync(options);   // → LobbyCreateOptions
await lobbyMgr.SearchLobbiesAsync(options);  // → LobbySearchOptions

// Factory presets
var quick = LobbyOptions.QuickMatch();
var ranked = LobbyOptions.ForSkillRange(1500, 200);
var tdm = LobbyOptions.ForGameMode("tdm");
```

Fields are split into shared (name, gamemode, maxplayers), create-only (voice, crossplay, hostmigration), and search-only (maxresults, skill range, platform/input filters). Irrelevant fields are gracefully ignored during conversion.

## Singleton Auto-Creation Pattern

Most managers use a lazy auto-create singleton pattern: `FindAnyObjectByType<T>()` first, then `new GameObject` parented under `EOSManager.Instance.transform` if not found. This keeps the hierarchy clean — all auto-created managers appear as children of the EOSManager object instead of flooding the DontDestroyOnLoad root.

```csharp
// Pattern used by all auto-create singletons:
if (_instance == null)
{
    var go = new GameObject("ManagerName");
    if (EOSManager.Instance != null)
        go.transform.SetParent(EOSManager.Instance.transform);
    else
        DontDestroyOnLoad(go);  // Fallback if EOSManager not yet created
    _instance = go.AddComponent<ManagerType>();
}
```

The Awake method also guards against duplicate DontDestroyOnLoad: `if (transform.parent == null) DontDestroyOnLoad(gameObject);`

**Auto-create singletons:** EOSLobbyManager, EOSVoiceManager, EOSStats, EOSAchievements, EOSPartyManager, EOSAntiCheatManager, EOSPlayerRegistry, EOSReports, EOSSanctions, EOSCustomInvites, EOSFriends, EOSLeaderboards, EOSPresence, EOSUserInfo, EOSPlayerDataStorage, EOSTitleStorage, EOSMetrics, EOSP2PManager, P2PDemoManager, and more.

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

### Lobby Creation Voice Fallback

`CreateLobbyAsync` includes automatic voice fallback logic. If the EOS SDK returns an error when creating a lobby with `EnableRTCRoom = true` (e.g., on platforms where RTC isn't available), it automatically retries without voice. This prevents `InvalidRequest` errors on Android and other platforms where the RTC module may not be initialized. Diagnostic logging prints all `CreateLobbyOptions` parameters before the SDK call for debugging.

## Audio Device Selection (Mic/Speaker)

`EOSVoiceManager` provides runtime mic/speaker device switching via EOS RTCAudio APIs:

- `QueryAudioDevices()` - Queries input/output device lists and registers for hotplug notifications. **Auto-called on voice connect** (no manual Refresh needed)
- `GetInputDevices()` / `GetOutputDevices()` - Returns cached device info lists
- `SetInputDevice(deviceId)` - Switches active microphone by `RealDeviceId`
- `SetOutputDevice(deviceId)` - Switches active speaker by `RealDeviceId`
- `OnAudioDevicesChanged` event - Fires when devices are added/removed
- `CurrentInputDeviceId` / `CurrentOutputDeviceId` - Track selected device
- `LocalMicLevel` (float, 0-1) - Real-time mic level via Unity `Microphone` API (RMS of 256 samples, scaled 8x). Starts capture when voice is connected and unmuted, stops on disconnect/mute. Used by both OnGUI and Canvas UI level bars.
- `LocalAudioStatus` (RTCAudioStatus) - Local user's audio status from SDK. `Unsupported` (0) means no audio devices / pipeline not initialized.
- `LastUpdateSendingResult` (Result) - Result of last mute/unmute call. Useful for diagnosing SDK rejecting audio changes.
- `AudioDevicesQueried` (bool) - Whether device enumeration has completed at least once.

The F1 overlay Voice tab exposes dropdown selectors for input/output devices with a Refresh button. The Canvas UI Voice tab also has an Audio Devices section with Refresh button, input/output device selection buttons (green = selected), and real-time mic level bar.

**Note:** `AudioBeforeSend` was tested for real RMS-based mic levels but causes `StackOverflowException` in `PlatformInterface.Tick()` — the EOS C# SDK queues audio frame callbacks and overflows processing them. `IsSpeaking()` proxy was also tested but unreliable when alone in a lobby (EOS VAD may not trigger). Unity `Microphone` API is the reliable solution.

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

A Canvas-based runtime UI (`EOSNativeCanvasUI.cs`, ~800 lines) that works on Android/iOS where OnGUI may not render. Uses `UnityEngine.UI` (no TextMeshPro). All UI elements created at runtime in code — no prefabs, no scene objects.

**Toggle:** Bottom-right corner "EOS" button (80x80), or 3-finger tap on mobile.

**Canvas setup:** `ScreenSpaceOverlay`, `sortingOrder: 9999`, `CanvasScaler` with `ScaleWithScreenSize` (1080x1920 reference, match 0.5).

**4 Tabs:** Status, Lobbies, Voice, Social (mirrors OnGUI sections).

| Tab | Contents |
|-----|----------|
| **Status** | SDK status, auth, PUID, platform info, interfaces, login/logout actions |
| **Lobbies** | Current lobby info, create (name/max/public/voice/migrate), join by code, quick match, search, members, chat (Enter to send) |
| **Voice** | Voice status, mic level bar, mute toggle, audio device picker (input/output), participants with speaking indicators |
| **Social** | Player registry, recently played, local friends, blocked players, Epic friends |

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

## P2P Transport Toolkit (Layer 1)

Foundation for typed P2P messaging. Provides binary serialization, packet fragmentation, message dispatch, and frame batching. All files in `Runtime/EOSNative/P2P/`.

### NetWriter / NetReader

Binary serializer/deserializer pair with auto-growing buffer and packed integer support.

```csharp
// Write
var writer = NetWriterPool.Get();
writer.WriteVector3Half(position);
writer.WriteUInt32(compressedRot);
writer.WriteString("hello");
writer.WritePackedInt32(-42);

// Read
var reader = new NetReader(data);
Vector3 pos = reader.ReadVector3Half();
uint rot = reader.ReadUInt32();
string msg = reader.ReadString();
int val = reader.ReadPackedInt32();

NetWriterPool.Return(writer);
```

**Supported types:** byte, bool, int16/uint16, int32/uint32, int64/uint64, float, double, packed uint32/int32/uint64 (varint), string (UTF-8 ushort-prefixed), Vector2, Vector3, Quaternion, Vector3Half (6 bytes), compressed rotation (4 bytes), Color, Color32, byte[], ProductUserId.

**Pooling:** `NetWriterPool.Get()` / `NetWriterPool.Return()` for allocation-free reuse.

### PacketFragmenter

Splits messages exceeding the EOS P2P limit (1170 bytes) into fragments and reassembles them.

- **Header:** 7 bytes `[packetId:u32][fragmentIndex:u16][lastFragment:u8]`
- **Max payload per fragment:** 1163 bytes
- **Single-fragment fast path:** No dictionary lookup, direct return
- **Stale cleanup:** Incomplete fragments discarded after 5 seconds

### MessageRouter

Message registration, typed dispatch, and frame batching. The "glue" layer.

```csharp
// Register handlers
var router = EOSP2PManager.Instance.Router;
router.Register(0x01, HandlePosition);
router.Register(0x02, HandleJoin);

// Subscribe router to raw packets
EOSP2PManager.Instance.OnPacketReceived += router.ProcessIncoming;

// Send typed messages (queued for batching)
var writer = NetWriterPool.Get();
writer.WriteVector3Half(pos);
router.SendToAll(0x01, writer, PacketReliability.UnreliableUnordered);
NetWriterPool.Return(writer);
```

**Wire format:**
```
EOS P2P Packet (max 1170 bytes)
├── Fragment Header (7 bytes)
│   [packetId:u32] [fragmentIndex:u16] [lastFragment:u8]
└── Router Envelope
    [batchFlag:u8]  (0x00=single, 0x01=batched)
    ├── Single: [msgId:u8] [payload...]
    └── Batch:  [count:u16] [len:u16][msgId:u8][payload] ...
```

**Batching:** Groups messages by (channel, reliability, target) and flushes once per frame in LateUpdate. Reduces P2P send calls when multiple messages queue in the same frame.

**Backward compatibility:** `EOSP2PManager.OnPacketReceived` still fires for all raw packets. The router is opt-in — old code works unchanged.

## P2P Ball Demo

A simple multiplayer demo using the raw EOS P2P interface (no FishNet, no high-level transport). Players control a ball with WASD, can jump, and collide. Positions sync across peers using spring physics.

**Peer-authority model:** Each player owns their ball. Local physics runs normally; remote balls are guided toward received positions via damped spring forces.

**Files (5):**

| File | Location | Description |
|------|----------|-------------|
| EOSP2PManager | `P2P/` | Reusable singleton P2P mesh manager (auto-accept, send/receive, lobby integration, MessageRouter) |
| NetWriter | `P2P/` | Binary serializer with auto-growing buffer, packed ints, pooling |
| NetReader | `P2P/` | Binary deserializer with bounds checking |
| PacketFragmenter | `P2P/` | Fragment/reassemble for 1170-byte EOS limit |
| MessageRouter | `P2P/` | Typed message dispatch with frame batching |
| P2PSpringSync | `P2P/` | Spring physics sync component (Vector3Half, smallest-three rotation, damped springs) |
| P2PPlayerBall | `Demo/` | WASD ball controller (Input System + legacy fallback) |
| P2PDemoCamera | `Demo/` | Top-down follow camera |
| P2PDemoManager | `Demo/` | Scene manager (uses MessageRouter for typed dispatch, spawn/despawn) |

**Message format (via MessageRouter):**

| Type | MsgId | Channel | Reliability | Payload |
|------|-------|---------|-------------|---------|
| Position | 0x01 | 0 | Unreliable | Vector3Half(6) + compressed rot(4) = 10 bytes |
| Join | 0x02 | 1 | Reliable | R(1) + G(1) + B(1) = 3 bytes |
| Leave | 0x03 | 1 | Reliable | 0 bytes |

**Flow:** Join/create lobby via F1 overlay -> EOSP2PManager detects lobby -> P2P connections form -> exchange join packets -> spring-sync positions every FixedUpdate.

**Credits:** Spring physics ported from PhysicsNetworkTransform.cs (DrewMileham original method, Skylar/CometDev Mirror implementation).

## Android Build (Core Library Desugaring + AndroidX + Native Libs)

The EOS SDK AAR (`eossdk-StaticSTDC-release.aar`) requires Java 8 core library desugaring and several AndroidX libraries. Without them, Android builds fail at either build time or runtime.

`EOSAndroidBuildProcessor.cs` (in `EOSNative.Editor/`) automatically injects the required Gradle config into both `launcher/build.gradle` and `unityLibrary/build.gradle` via `IPostGenerateGradleAndroidProject`. It handles six things:

1. **Core library desugaring:** `coreLibraryDesugaringEnabled true` in `compileOptions` + `coreLibraryDesugaring 'com.android.tools:desugar_jdk_libs:2.1.4'` in `dependencies`
2. **AndroidX dependencies:** The EOS AAR's transitive Maven dependencies aren't resolved by Unity, so we inject them explicitly:
   - `androidx.appcompat:appcompat:1.5.1`
   - `androidx.constraintlayout:constraintlayout:2.1.4`
   - `androidx.security:security-crypto:1.0.0`
   - `androidx.browser:browser:1.4.0` — **required** for Chrome Custom Tabs (account portal login). Without this, `EOSSDK.init()` throws `NoClassDefFoundError: CustomTabsServiceConnection`
3. **Extract native libs:** Injects `android:extractNativeLibs="true"` into AndroidManifest.xml's `<application>` tag — required so native `.so` files from the EOS AAR are extracted at install time (without this, `System.loadLibrary` may fail with `UnsatisfiedLinkError`)
4. **EOS login scheme:** Injects `eos_login_protocol_scheme` string resource into `strings.xml`
5. **Java init helper:** Generates `EOSNativeInit.java` with classloader hint and `catch (Throwable)` (not `Exception`) to properly handle `UnsatisfiedLinkError` from `JNI_OnLoad`
6. **ProGuard keep rules:** Generates `proguard-eos.pro` to prevent R8/ProGuard from stripping EOS SDK Java classes needed for JNI native method registration

Also ensures `google()` repository is present for AndroidX resolution and injects RECORD_AUDIO/ACCESS_WIFI_STATE permissions. No manual Gradle template editing is required.

### Android SDK Loading (Java Classloader Fix)

**Problem:** When Unity's C# JNI bridge calls `EOSSDK.init(activity)`, the JNI environment's classloader may not include the EOS AAR's Java classes. `JNI_OnLoad` calls `FindClass("com/epicgames/mobile/eossdk/EOSLogger")` which fails, so `RegisterNatives` never runs, causing `UnsatisfiedLinkError` for `EOSLogger.Log` and broken RTC/Audio subsystems.

**Solution:** `EOSAndroidBuildProcessor` generates a Java helper class (`com.tront.eosnative.EOSNativeInit`) at build time that calls `EOSSDK.init(activity)` from the app's own classloader context. The helper sets the thread context classloader to the app's classloader before calling init (helps `FindClass` on some Android versions), and catches `Throwable` (not just `Exception`) since `UnsatisfiedLinkError extends Error`. Since this class is compiled into the APK alongside the EOS AAR, `JNI_OnLoad`'s `FindClass` resolves EOS SDK Java classes correctly.

**CRITICAL:** Do NOT call `System.loadLibrary("EOSSDK")` before `EOSSDK.init(activity)`. The AAR's `init()` handles native library loading internally in the correct order. Pre-loading corrupts JNI registration. PlayEveryWare's implementation also only calls `EOSSDK.init`, never `loadLibrary`.

**Flow:** `LoadAndroidLibrary()` in `EOSManager.cs` → tries `EOSNativeInit.init(activity)` first → falls back to direct `EOSSDK.init(activity)` if the helper class isn't found (old builds without the processor).

**Diagnostics:** `LoadAndroidLibrary()` logs API level, device model, and supported ABIs at startup. The `AndroidJavaInitSuccess` property tracks whether Java-side init succeeded.

**Console early init:** `EOSNativeConsole.Instance` is created in `Awake()` (before `LoadNativeLibrary` and `LoadAndroidLibrary`) so all startup errors are captured in the runtime console.

### Android RTC/Voice Prerequisites

For RTC/Voice to work on Android, **three things** must be in place:

1. **`RTCOptions` must be set on platform creation** — The generic `Options` struct must include `RTCOptions = new RTCOptions()`. Setting it to null (the default) tells the SDK to **skip RTC initialization entirely**. This was the primary cause of RTC/Audio showing RED on Android.

2. **`RECORD_AUDIO` permission must be in AndroidManifest** — `EOSAndroidBuildProcessor` auto-injects `android.permission.RECORD_AUDIO` and `android.permission.ACCESS_WIFI_STATE`. Without these, the OS blocks microphone access.

3. **`EOSSDK.init()` must succeed** — The Java helper class ensures the correct classloader context.

### Android SDK Initialization

On Android, `PlatformInterface.Initialize()` MUST use `AndroidInitializeOptions` (not generic `InitializeOptions`). The Android-specific struct includes a `Reserved` field set to `{1, 1}` and `SystemInitializeOptions` for Android file paths. Using the generic struct causes RTC/Audio subsystems to not initialize. The SDK generates both overloads in `Source/Generated/Android/Platform/PlatformInterface.cs`.

## Overlay UI Mode (EOSManager)

EOSManager exposes an `OverlayUIMode` enum to control which runtime UI is active:

| Mode | OnGUI (F1) | Canvas UI | Console |
|------|-----------|-----------|---------|
| **Auto** (default) | Desktop only | Mobile only | If `_showConsole` enabled |
| **OnGUI** | Yes | No | If enabled |
| **Canvas** | No | Yes | If enabled |
| **Both** | Yes | Yes | If enabled |
| **None** | No | No | If enabled |

Set via Inspector on the EOSManager component. The `_showConsole` bool independently controls the Canvas console.

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

## Layer 2: NetworkObject / SyncVar / NetworkManager

High-level networking built on the P2P Transport Toolkit (Layer 1). Provides object identity, automatic state sync, spawn/despawn, late-join snapshots, authority transfer, and RPCs. All files in `Runtime/EOSNative/Net/`.

### Architecture

- **NetworkObject** — Core component on any synced GameObject. Manages identity (NetworkId), ownership (OwnerId), and an ordered list of SyncVars.
- **SyncVar\<T\>** — Generic sync wrapper with dirty tracking and owner-write guard. Only the owner can set values; remote peers receive deltas automatically.
- **NetworkBehaviour** — Optional convenience base class with shortcuts to NetworkObject.
- **NetworkManager** — Singleton managing all NetworkObjects. Handles sync, spawn/despawn, snapshots, migration, RPCs.
- **NetworkTransform** — Hybrid sync component: Spring physics, buffered interpolation, extrapolation, and distance LOD in one. Configurable SyncMethod (Auto/Spring/Interpolation) + ExtrapolationMode + 3-tier distance LOD.
- **NetworkAnimator** — Syncs Animator float/int/bool parameters via packed SyncVar, triggers via RPC. Auto-discovers parameters.
- **EasySync** — Normcore-style no-code sync. Check properties in Inspector to sync them — reflection-based, packed byte[] SyncVar.
- **SyncList\<T\>** — Synchronized list with operation-based delta sync (Add/Set/RemoveAt/Insert/Clear). Only changed ops sent over the wire.
- **SyncDictionary\<TKey, TValue\>** — Synchronized dictionary with operation-based delta sync (Set/Remove/Clear). Key-value pairs for inventories, scores, game state.
- **NetSerializers** — Static type registry for serialization. Built-in handlers for all common types + `INetSerializable` for custom.

### Usage

```csharp
public class Player : NetworkBehaviour
{
    SyncVar<Vector3> Position;
    SyncVar<float> Health;

    protected override void Awake()
    {
        base.Awake();
        Position = Sync(Vector3.zero);
        Health = Sync(100f);
        Health.OnChanged += (old, hp) => Debug.Log($"Health: {old} → {hp}");
        NetworkManager.Instance.RegisterRPC(Net, "TakeDamage", reader => {
            float dmg = NetSerializers.Read<float>(reader);
            Health.Value -= dmg;
        });
    }

    void Update()
    {
        if (!IsOwner) return;
        Position.Value = transform.position;
    }
}

// Spawning:
var player = NetworkManager.Instance.Spawn(prefabId: 0, pos, rot);

// RPC:
NetworkManager.Instance.SendRPC(player, "TakeDamage", RPCTarget.Owner, 25f);
```

### Message IDs (0xA0-0xAF)

| ID | Name | Reliability | Channel | Payload |
|----|------|-------------|---------|---------|
| 0xA0 | STATE_UPDATE | Unreliable | 0 | Packed object count + per-object: networkId, dataLen, dirtyMask, dirty values |
| 0xA1 | SPAWN | Reliable | 1 | prefabId, networkId, ownerId, position, rotation, syncVarCount, all values |
| 0xA2 | DESPAWN | Reliable | 1 | networkId |
| 0xA3 | AUTHORITY | Reliable | 1 | networkId, newOwnerId |
| 0xA4 | SNAPSHOT | Reliable | 1 | objectCount + per-object (same as SPAWN) |
| 0xA5 | SNAPSHOT_REQUEST | Reliable | 1 | empty |
| 0xA6 | RPC | Reliable | 1 | networkId, methodNameHash, argCount, typed args |
| 0xA7 | AUTHORITY_REQUEST | Reliable | 1 | networkId |
| 0xAA | SCENE_LOAD | Reliable | 1 | sceneName, additive (bool) |
| 0xAB | SCENE_UNLOAD | Reliable | 1 | sceneName |
| 0xAC | SCENE_LOADED_ACK | Reliable | 1 | sceneName |

### NetworkId Partitioning

Each peer generates IDs from their own partition: upper 16 bits = FNV-1a hash of local PUID, lower 16 bits = incrementing counter. No collision between peers.

### Host Election

Deterministic — lexicographically lowest PUID string among all connected peers + self. Recomputed on peer connect/disconnect. No communication needed.

### Authority Transfer (Host Migration)

When a peer disconnects, the new host claims orphaned objects by setting OwnerId to self and broadcasting AUTHORITY messages. Objects **continue running** — no destroy/reinstantiate. SyncVars already have latest values.

### Late Join

New peer sends SNAPSHOT_REQUEST to host. Host responds with full SNAPSHOT containing all active NetworkObjects. New peer instantiates from prefab registry with correct state.

### Scene Object Auto-Ownership

NetworkObjects that pre-exist in a scene (placed in the editor, not spawned at runtime) are automatically registered and assigned to the host. Call `NetworkManager.Instance.RegisterSceneObjects()` after scene load, or let it happen automatically when host status is computed.

- Scene objects get deterministic NetworkIds based on their hierarchy path (FNV-1a hash)
- Ownerless objects auto-assign to the current host
- Authority is broadcast to all peers so everyone agrees on ownership
- Works seamlessly with late join (scene objects included in SNAPSHOT)

### NetworkTransform (Hybrid)

All-in-one transform sync component (`NetworkTransform.cs`, ~865 lines). Combines spring physics, buffered interpolation, velocity extrapolation, and distance-based LOD in a single component.

```csharp
// Just add NetworkTransform component to any GameObject with NetworkObject.
// Owner writes transform changes automatically; remote peers sync smoothly.
// Auto mode: Spring for Rigidbody objects, Interpolation for kinematic objects.
```

**Sync Methods (`SyncMethod` enum):**
- **Auto** (default) — Spring if Rigidbody present, Interpolation if kinematic
- **Spring** — Damped spring physics (force-based for Rigidbody, closed-form for Transform). Best for physics objects: balls, vehicles, ragdolls
- **Interpolation** — SmoothSync-style buffered lerp. 30-state buffer, renders in the past (`interpolationDelay`), two-stage easing. Best for kinematic objects: characters, platforms, UI elements

**Extrapolation (`ExtrapolationMode` enum):**
- **None** — Freeze at last known position when buffer runs out
- **Limited** (default) — Predict forward using velocity, capped by `extrapolationTimeLimit` (5s) and `extrapolationDistanceLimit` (20m). Applies gravity and drag if Rigidbody present
- **Unlimited** — Predict forward indefinitely

**Distance LOD (3 tiers with hysteresis):**

| Tier | Distance | Behavior | CPU Cost |
|------|----------|----------|----------|
| **Full** | < `fullSyncDistance` (10m) | Spring or Interpolation (configured method) | Highest |
| **Tweened** | 10m - 30m | Simple lerp toward target | Medium |
| **Simple** | > `simpleSyncDistance` (30m) | Snap to target | Lowest |

Hysteresis (`lodDeadZone` = 5m) prevents tier flickering at boundaries. Rigidbody objects auto-switch to kinematic in Tweened/Simple tiers and restore when returning to Full.

**Rest Detection:** Timeout-based — if no new SyncVar data for `restTimeout` seconds (0.5s default), object is assumed at rest and extrapolation stops. Prevents drift on idle objects.

**Teleport API:**
```csharp
GetComponent<NetworkTransform>().Teleport(newPosition, newRotation);
// On owner: sets position + forces SyncVar sync
// On remotes: large jumps auto-snap via snap threshold
// Also clears state buffer and resets spring velocities
```

**Settings (Inspector, 9 header groups):**

| Header | Settings |
|--------|----------|
| **What to Sync** | Position, Rotation, Scale toggles |
| **Sync Method** | Auto / Spring / Interpolation |
| **Interpolation** | Delay (0.1s), position ease speed (0.85), rotation ease speed (0.85) |
| **Extrapolation** | Mode (Limited), time limit (5s), distance limit (20m) |
| **Spring Physics** | Pos freq (8Hz), pos damping (0.9), rot freq (10Hz), rot damping (0.85) |
| **Snap/Teleport** | Pos snap distance (5m), rot snap angle (90deg) |
| **Send Thresholds** | Position threshold (0.001m), rotation threshold (0.1deg) |
| **Distance LOD** | Enable, full distance (10m), simple distance (30m), dead zone (5m) |
| **Rest Detection** | Rest timeout (0.5s) |

**Non-owner sync pipeline:**
```
SyncVar data → State Buffer → Target Calculation → Application Method
                               (interp/extrap)      (spring/lerp/snap)
```

**Credits:** Spring physics from PhysicsNetworkTransform.cs (DrewMileham, Skylar/CometDev). Interpolation architecture inspired by SmoothSync (Jim Burrows).

### SyncList

Synchronized list type. Tracks operations (Add, Set, RemoveAt, Insert, Clear) and sends minimal deltas.

```csharp
SyncList<string> Inventory;

void Awake() {
    Inventory = SyncList(new List<string>());
    Inventory.OnChanged += (op, index, oldItem, newItem) =>
        Debug.Log($"{op}: [{index}] {oldItem} -> {newItem}");
}

void PickUp(string item) {
    if (!IsOwner) return;
    Inventory.Add(item); // Synced to all peers
}
```

### SyncDictionary

Synchronized key-value dictionary type. Tracks operations (Set, Remove, Clear) and sends minimal deltas.

```csharp
SyncDictionary<string, int> Scores;

void Awake() {
    Scores = SyncDictionary<string, int>(new Dictionary<string, int>());
    Scores.OnChanged += (op, key, oldVal, newVal) =>
        Debug.Log($"{op}: {key} = {oldVal} -> {newVal}");
}

void AddScore(string player, int pts) {
    if (!IsOwner) return;
    Scores[player] = pts; // Synced to all peers
}
```

### DestroyWithOwner (Lifetime Flag)

NetworkObjects have a `DestroyWithOwner` flag (default false). When true, the object is despawned instead of transferred to the new host when its owner disconnects. Useful for player avatars and per-player objects.

```csharp
// Player objects should be destroyed when the player leaves
var player = NetworkManager.Instance.Spawn(playerPrefabId, pos, rot);
player.DestroyWithOwner = true;

// Room state objects persist (default behavior)
var roomState = NetworkManager.Instance.Spawn(roomStatePrefabId, Vector3.zero, Quaternion.identity);
// roomState.DestroyWithOwner = false; // default
```

The flag is synced in the spawn/snapshot wire format so all peers agree on the behavior.

### Object Pooling

NetworkManager includes built-in object pooling. `Despawn()` deactivates and returns objects to a per-prefab pool. `Spawn()` checks the pool first before calling `Instantiate`. Enable via `_enablePooling` on the NetworkManager component. Pre-warm pools with `Prewarm(prefabId, count)`.

### RPC Migration Buffer

Host-targeted and owner-targeted RPCs are automatically buffered during host migration. When a peer disconnects and host re-election occurs, any RPCs sent during that window are queued and replayed once the new host is confirmed. No RPCs are dropped during transition.

### Authority Request

Non-host peers can request ownership of objects via `NetworkManager.Instance.RequestAuthority(obj)`. The host auto-approves by default. Set `OnAuthorityRequested` callback on the host to add custom validation (e.g. distance checks, cooldowns). Uses message ID 0xA7.

### Reliable State Fallback (Eventual Consistency)

STATE_UPDATE is sent unreliable for speed. But if a packet drops, a SyncVar change might never arrive. After 200ms, if the object hasn't been re-dirtied, its full state is resent via reliable SNAPSHOT. This guarantees eventual consistency with minimal overhead — continuously-changing state (like movement) is always unreliable, while one-shot changes (like HP) get reliable delivery.

### NetworkObject References in RPCs

NetworkObject is a registered serializer type. When sent as an RPC arg, it serializes as the NetworkId (uint). The receiver automatically resolves it to the local instance via `NetworkManager.Instance.Objects`.

```csharp
NetworkManager.Instance.SendRPC(target, "GotHitBy", RPCTarget.Owner, attackerNetObj, 25f);

// Receiver:
NetworkManager.Instance.RegisterRPC(Net, "GotHitBy", reader => {
    NetworkObject attacker = NetSerializers.Read<NetworkObject>(reader);
    float dmg = NetSerializers.Read<float>(reader);
});
```

### Sequence-Based Stale Rejection (BufferLast)

STATE_UPDATE packets include a per-object sequence number. Receivers only apply updates where `(newSeq - lastSeq) > 0` using wrapping comparison. Out-of-order packets are silently discarded. This prevents stale state from overwriting newer data on unreliable channels.

### NetworkAnimator

Synchronizes Animator parameters across the network. Packs all float/int/bool parameters into a single SyncVar<byte[]> for bandwidth efficiency. Triggers are sent via RPC (event-based, not state).

```csharp
// Just add NetworkAnimator to any GameObject with Animator + NetworkObject.
// Parameters auto-discovered. Owner changes sync to all peers.

// For triggers (events, not state):
GetComponent<NetworkAnimator>().SetNetworkTrigger("Jump");
```

**Wire format:** `[floatCount:byte][floats...][intCount:byte][ints...][boolCount:byte][boolMask bytes]`

**Settings:**
- `Sync Interval` — How often to check for parameter changes (default 0.1s = 10Hz)
- `Animator` — Auto-detected if not assigned

**Change detection:** Only sends when a parameter actually changes (float threshold 0.001, exact match for int/bool). Uses cached previous values.

### EasySync (No-Code Property Sync)

Inspired by Normcore's EasySync. Sync any public field or property on sibling components without writing code. Just add the EasySync component, check boxes in the Inspector, and properties sync automatically.

```csharp
// No code needed! Just:
// 1. Add EasySync component to any GameObject with NetworkObject
// 2. In Inspector, check the properties you want to sync
// 3. Owner writes → remote peers receive automatically
```

**Custom Inspector** (`EasySyncEditor.cs` in `EOSNative.Editor/`):
- Scans all sibling components for public fields and properties
- Filters to supported types (bool, byte, short, ushort, int, uint, long, ulong, float, double, string, Vector2, Vector3, Quaternion, Color, Color32)
- Skips Transform, NetworkObject, NetworkBehaviour subclasses, and base Unity properties
- Foldout per component, toggle per member
- Undo/Redo support

**Runtime:** Reflection-based read/write, packed into a single SyncVar<byte[]>. Bindings resolved once in Awake (FieldInfo/PropertyInfo cached). Change detection before sending. Try/catch on apply for safety.

**Settings:**
- `Sync Interval` — How often to check for changes (default 0.1s)
- Per-property `WriteAccess` (Owner/Host/All) — stored but only Owner enforced in v1. Host/All planned for v2.

## Connection Statistics (NetworkStats)

Singleton manager (`Net/NetworkStats.cs`, ~600 lines) that tracks per-peer and global connection quality metrics. Auto-creates under EOSManager like other singletons. Uses an `internal static _instance` null-check pattern so hooks in EOSP2PManager have zero overhead when stats aren't being used.

### Ping/Pong Protocol

Custom RTT measurement since EOS SDK doesn't provide it. Message IDs 0xA8 (PING) and 0xA9 (PONG) on channel 2, unreliable unordered, sent via `SendToPeerImmediate` to bypass batching.

- **PING** (8 bytes): `[sequence:u32][senderTimestamp:float32]`
- **PONG** (8 bytes): `[sequence:u32][originalTimestamp:float32]`
- **RTT** = `(Time.unscaledTime - originalTimestamp) * 1000f` ms, EMA smoothed (alpha=0.2)

### Metrics

| Metric | Method | Frequency |
|--------|--------|-----------|
| **RTT** | Ping/pong protocol, EMA smoothed | Every `_pingInterval` (1s) |
| **Packet Loss** | Rolling window: `1.0 - pongs/pings` | 10s window |
| **Bandwidth** | Delta between byte snapshots | Every `_sampleInterval` (0.5s) |
| **NAT Type** | `GetNATType()` + `QueryNATType()` | Once on startup |
| **Queue Info** | `GetPacketQueueInfo()` | On demand via `GetGlobalStats()` |
| **Connection Type** | From `OnConnectionEstablished` callback | On peer connect |

### EOSP2PManager Hooks (3 lines)

1. `SendToPeer()` — `NetworkStats._instance?.RecordBytesSent(peer, data.Length)`
2. `PollPackets()` — `NetworkStats._instance?.RecordBytesReceived(sender, (int)bytesWritten)`
3. `OnConnectionEstablished()` — `NetworkStats._instance?.RecordConnectionType(peer, networkType, establishedType)`

### Public API

```csharp
// Per-peer
PeerStats GetPeerStats(ProductUserId puid)
float RTT(ProductUserId puid)           // ms, -1 if unknown
float PacketLoss(ProductUserId puid)    // 0.0 - 1.0
float ConnectionAge(ProductUserId puid) // seconds
IReadOnlyDictionary<ProductUserId, PeerStats> AllPeerStats

// Global
GlobalStats GetGlobalStats()  // includes queue info
NATType LocalNATType
float AverageRTT
float TotalBandwidthOutKBps / InKBps
void ResetStats()
event Action OnStatsUpdated   // fires every 0.5s
```

### F1 Overlay (Stats Tab)

`DrawNetworkStatsSection()` shows NAT type, peer count, average RTT, bandwidth in/out, queue utilization, and a per-peer table with columns: Name, RTT, Loss%, Type, Out, In, Age. Color-coded: RTT green <50ms / yellow <150ms / red >300ms. Loss green <1% / yellow <5% / red >5%. Direct=green, Relayed=yellow.

## NetworkRoomState (Shared Room Data)

Singleton NetworkObject representing the shared room/game state. Well-known ID `0xFFFF0001`, reserved PrefabId `0xFFF0`. Host auto-creates after first peer connects. DestroyWithOwner = false (survives host migration).

**File:** `Net/NetworkRoomState.cs` (~200 lines)

**SyncVars (index 0-7):** GameMode (string), MapName (string), RoundNumber (int), PlayerCount (int), MaxPlayers (int), RoundTimer (float), Phase (byte: 0=Lobby/1=Loading/2=Playing/3=PostMatch), IsInProgress (bool)

**SyncDictionary (index 8):** `Properties` — dynamic string-string for custom room data

**Lobby attribute mirroring:** Rate-limited (1/sec). GameMode, MapName, IsInProgress auto-push to lobby attributes. Add keys to `SearchablePropertyKeys` for custom mirroring.

**Scene properties:** `_scene`, `_addScene_N`, `_addSceneCount` stored in Properties dict. Read by NetworkSceneManager for late-join scene sync.

```csharp
var room = NetworkManager.Instance.RoomState;
room.GameMode.Value = "deathmatch";          // host writes
room.SetProperty("score_limit", "100");       // host writes custom
int limit = room.GetPropertyInt("score_limit", 50); // typed getter
room.CurrentPhase = GamePhase.Playing;        // typed enum accessor
```

## NetworkPlayerState (Per-User Data)

Per-player NetworkObject. Reserved PrefabId `0xFFF1`. Each peer auto-creates their own on connect. DestroyWithOwner = true (destroyed on disconnect). Standard NetworkId generation (PUID partition).

**File:** `Net/NetworkPlayerState.cs` (~170 lines)

**SyncVars (index 0-7):** DisplayName (string), Team (byte), IsReady (bool), Score (int), Deaths (int), Assists (int), Loadout (string), PlayerSlot (byte)

**SyncDictionary (index 8):** `CustomData` — dynamic string-string per player

**Auto-init:** DisplayName populated from EOSPlayerRegistry on spawn.

```csharp
var me = NetworkManager.Instance.LocalPlayerState;
me.Team.Value = 1;
me.IsReady.Value = true;
me.SetCustom("skin", "gold_armor");

var them = NetworkManager.Instance.GetPlayerState(puid);
string name = them.DisplayName.Value;

foreach (var kvp in NetworkManager.Instance.PlayerStates) { ... }
```

## NetworkSceneManager

Singleton manager for networked scene loading. Host calls LoadScene/LoadSceneAdditive/UnloadScene, all peers follow. Scene info stored on NetworkRoomState properties so late joiners load the correct scenes.

**File:** `Net/NetworkSceneManager.cs` (~300 lines)

**Max 8 additive scenes** (matching Fusion).

**Load flow:**
1. Host calls `LoadScene("Arena_01")`
2. Updates RoomState scene properties
3. Broadcasts MSG_SCENE_LOAD (reliable) to all peers
4. All peers load async via `SceneManager.LoadSceneAsync`
5. After load: `RegisterSceneObjects()` auto-assigns scene NetworkObjects
6. Non-host peers send MSG_SCENE_LOADED_ACK to host
7. Host fires `OnAllPeersLoaded` when all ACKs received

**Late join:** After receiving SNAPSHOT, new peer reads scene info from RoomState and loads the correct scenes.

```csharp
NetworkSceneManager.Instance.LoadScene("Arena_01");
NetworkSceneManager.Instance.LoadSceneAdditive("Props_01");
NetworkSceneManager.Instance.UnloadScene("Props_01");

NetworkSceneManager.Instance.OnSceneLoadCompleted += name => { ... };
NetworkSceneManager.Instance.OnAllPeersLoaded += () => StartRound();
```

## Chunked Snapshot Delivery

Snapshots are sent in priority-ordered chunks of 16 objects per message:

1. **Priority 1:** NetworkRoomState (so late joiners know game state immediately)
2. **Priority 2:** All NetworkPlayerStates (so late joiners know about all players)
3. **Priority 3:** Remaining objects

Each chunk is a separate MSG_SNAPSHOT message, all reliable ordered. HandleSnapshot already handles multiple SNAPSHOT messages via duplicate guard (`_objects.ContainsKey`). After receiving RoomState, late joiners auto-create their PlayerState and sync scenes.

## Typed RPCs ([NetRpc] Attribute + IL Post-Processor)

Mark methods on `NetworkBehaviour` subclasses with `[NetRpc]` for zero-boilerplate typed RPCs. The IL post-processor (Mono.Cecil) rewrites method bodies at compile time — same technique as Mirror, FishNet, and Fusion.

```csharp
[NetRpc(RPCTarget.All)]
public void TakeDamage(float damage)
{
    Health.Value -= damage;
}

// Calling is transparent — all peers execute TakeDamage:
player.TakeDamage(19f);
```

**What the weaver generates (per [NetRpc] method):**
1. `UserCode_TakeDamage(float)` — original method body, moved here
2. `TakeDamage(float)` — dispatch stub: serialize args via `NetSerializers.Write<T>()`, call `SendRPCWeaved()`
3. `__InvokeNetRpc_TakeDamage(NetReader)` — deserializer: `NetSerializers.Read<T>()` each param, call `UserCode_`
4. `__RegisterNetRPCs()` override — registers invoke handlers after NetworkId is assigned

**NetworkBehaviour lifecycle hooks (called by NetworkObject.NotifyNetworkSpawn/Despawn):**
- `OnNetworkSpawn()` — called after NetworkId assigned, RPCs registered. Override for post-spawn init.
- `OnNetworkDespawn()` — called before object deactivated/pooled. Override for cleanup.
- `__RegisterNetRPCs()` — weaver-generated, registers all `[NetRpc]` handlers. Do not call manually.

**NetworkManager new overloads (weaver calls these):**
- `RegisterRPC(target, uint hash, string name, handler)` — hash-based registration (compile-time hash)
- `SendRPCWeaved(target, uint hash, RPCTarget, byte[] argData)` — pre-serialized dispatch
- `SendRPCWeavedToPeer(target, uint hash, ProductUserId, byte[] argData)` — peer-targeted pre-serialized dispatch

**Supported parameter types:** byte, bool, short, ushort, int, uint, long, ulong, float, double, string, Vector2, Vector3, Quaternion, Color, Color32, ProductUserId, byte[], NetworkObject, INetSerializable

**Constraints:** void return only, no ref/out, no generics, no abstract. Violations produce compiler errors from the weaver.

**Backward compatible:** Existing string-based `RegisterRPC`/`SendRPC` API unchanged and fully functional.

**CodeGen assembly:** `EOSNative.CodeGen/` — Editor-only, `noEngineReferences: true`, references Mono.Cecil precompiled DLLs. Depends on `com.unity.nuget.mono-cecil` (1.11.6) in manifest.json.

**Files:**

| File | Description |
|------|-------------|
| `Net/NetRpcAttribute.cs` | `[NetRpc(RPCTarget)]` attribute definition |
| `EOSNative.CodeGen/EOSNative.CodeGen.asmdef` | Assembly def for the IL post-processor |
| `EOSNative.CodeGen/EOSNetRpcPostProcessor.cs` | `ILPostProcessor` entry point |
| `EOSNative.CodeGen/PostProcessorAssemblyResolver.cs` | Custom Cecil assembly resolver |
| `EOSNative.CodeGen/WeaverTypes.cs` | Resolves all Cecil type/method references |
| `EOSNative.CodeGen/RpcWeaver.cs` | Core weaving logic (~400 lines) |

## DemoBallBehaviour (Layer 2 Demo)

NetworkBehaviour component added to each ball in the P2P demo. Demonstrates SyncVars and `[NetRpc]` typed RPCs on runtime-created objects registered via `RegisterExisting()` (no prefabs needed).

**File:** `Demo/DemoBallBehaviour.cs` (~170 lines)

**SyncVars:** Score (int), DisplayName (string), BallColor (Color)

**[NetRpc] Methods:**
- `ApplyImpulse(float dirX, float dirY, float dirZ, float force)` — applies physics impulse (RPCTarget.All)
- `ChangeColor(float r, float g, float b)` — changes ball color, updates SyncVar + renderer (RPCTarget.All)
- `ChatBubble(string message)` — shows floating text above ball for 3s (RPCTarget.All)
- `PlayEffect(byte effectId)` — shows brief visual indicator (RPCTarget.All)
- `RequestScorePoint(int amount)` — asks owner to add score (RPCTarget.Owner)

**Demo controls (in P2PDemoManager):**
- **E** — cycle through color presets
- **Q** — shockwave impulse (pushes self up + all nearby balls outward)
- **T** — chat bubble ("Hello!")
- **R** — random visual effect

**Registration pattern:** Each ball gets `NetworkObject` + `DemoBallBehaviour` added at creation time. A deterministic NetworkId is generated as `0xBB000000 | (FnvHash(puid) & 0x00FFFFFF)`, then registered via `NetworkManager.Instance.RegisterExisting()`.

**`RegisterExisting()` fix (v2.14.0):** Now calls `NotifyNetworkSpawn()` after registration, so `__RegisterNetRPCs()` and `OnNetworkSpawn()` fire correctly on objects created outside of `Spawn()`.

## Packet Compression

Opt-in Deflate compression for message payloads. Transparent to application code — enable and forget.

**Properties on `MessageRouter`:**
- `CompressionEnabled` (bool, default false) — enables/disables compression
- `CompressionThreshold` (int, default 64) — minimum payload bytes before compression is attempted

**Convenience on `NetworkManager`:**
- `CompressionEnabled` — proxies to `Router.CompressionEnabled`

**Wire format (backward compatible):**
- `0x00` = FLAG_SINGLE (uncompressed, unchanged)
- `0x01` = FLAG_BATCH (uncompressed, unchanged)
- `0x02` = FLAG_SINGLE_COMPRESSED — `[flag][deflate(msgId + payload)]`
- `0x03` = FLAG_BATCH_COMPRESSED — `[flag][deflate(count + messages)]`

Old peers that don't understand `0x02`/`0x03` silently ignore them (no handler found). Compression only applied when the compressed output is smaller than the original — otherwise falls back to uncompressed.

```csharp
// Enable compression (opt-in)
NetworkManager.Instance.CompressionEnabled = true;
// Or directly on the router:
EOSP2PManager.Instance.Router.CompressionEnabled = true;
EOSP2PManager.Instance.Router.CompressionThreshold = 128; // custom threshold
```

**Static helpers (internal, used by tests):**
- `MessageRouter.CompressDeflate(byte[], offset, count)` → `byte[]`
- `MessageRouter.DecompressDeflate(byte[], offset, count)` → `byte[]`

## Spectator Mode

A peer can join as a read-only observer. Spectators receive all state updates but cannot spawn objects or become host.

**Properties on `NetworkManager`:**
- `IsSpectator` (bool) — set to true before joining a lobby to join as spectator
- `IsPeerSpectator(ProductUserId)` — check if a specific peer is spectating

**Convenience on `NetworkBehaviour`:**
- `IsSpectator` — proxies to `NetworkManager.Instance.IsSpectator`

**Convenience on `NetworkPlayerState`:**
- `IsSpectating` — reads `_spectator` custom data key

**How it works:**
1. Set `NetworkManager.Instance.IsSpectator = true` before joining a lobby
2. On connect, the local PlayerState is created with `CustomData["_spectator"] = "1"`
3. All peers read this key and populate an internal `_spectators` HashSet
4. Host election skips spectator PUIDs (spectators never become host)
5. `Spawn()` returns null with a warning if `IsSpectator` is true
6. `RPCTarget.Players` sends only to non-spectator peers

**RPCTarget.Players (new enum value = 4):**
- `executeLocal = !IsSpectator` — spectators don't execute locally
- Remote send goes only to non-spectator peers via `SendToNonSpectators()`

**Edge case:** If ALL peers are spectators, the lowest PUID becomes host anyway (with a warning log).

```csharp
// Join as spectator
NetworkManager.Instance.IsSpectator = true;
// Then join lobby normally...

// Send RPC only to players (not spectators)
[NetRpc(RPCTarget.Players)]
public void StartRound() { ... }

// Check from any NetworkBehaviour
if (IsSpectator) return; // skip gameplay logic
```

## Master Client Verification (RPC Validation)

Opt-in callback on NetworkManager that fires before executing any incoming remote RPC. The host (or any peer) can reject unauthorized RPCs. Null = all RPCs allowed (default behavior, zero overhead).

**Field on `NetworkManager`:**
```csharp
public Func<ProductUserId, NetworkObject, uint, bool> OnRPCValidation;
// Parameters: (sender, targetObject, methodHash) → allow?
```

**Helper:**
```csharp
// Convenience: only allow RPCs from the object's owner
NetworkManager.Instance.EnableOwnerOnlyRPCValidation();
// Equivalent to:
NetworkManager.Instance.OnRPCValidation = (sender, target, hash) =>
    target != null && target.OwnerId == sender;
```

**Behavior:** When set, `HandleRPC()` checks the callback after reading networkId + methodHash. If it returns false, logs a warning and skips the handler. The reader position is safe because each message in `DispatchMessage` gets bounded offset/count.

```csharp
// Custom validation: only allow specific RPCs from non-owners
NetworkManager.Instance.OnRPCValidation = (sender, target, hash) =>
{
    if (target == null) return false;
    if (target.OwnerId == sender) return true; // owner can always RPC
    // Allow specific cross-owner RPCs (e.g. damage)
    return hash == NetworkManager.FnvHash("TakeDamage");
};
```

## Automated Tests

Editor-mode unit tests for core networking primitives. Uses Unity Test Framework (`com.unity.test-framework`).

**Location:** `Tests/Editor/` with `EOSNative.Tests.Editor.asmdef`

**Test assembly:** References `EOSNative` and `Epic.OnlineServices` assemblies. Editor-only, `UNITY_INCLUDE_TESTS` define constraint.

| Test File | Tests | Coverage |
|-----------|-------|----------|
| `NetWriterReaderTests.cs` | ~25 | All primitives, packed varints, strings, Unity types, byte arrays, pooling, auto-grow, bounds checking |
| `FnvHashTests.cs` | ~8 | Known values, empty/null, consistency, case sensitivity, collision resistance |
| `NetSerializersTests.cs` | ~20 | All 18 built-in types round-trip, INetSerializable, boxed read/write, type IDs |
| `SyncVarTests.cs` | ~20 | Dirty tracking, OnChanged, owner-write guard, SetInternal bypass, serialize round-trip, multiple SyncVars, dirty mask |
| `SyncListTests.cs` | ~18 | Add/Set/RemoveAt/Insert/Clear, delta serialization, full state, OnChanged, enumerate |
| `SyncDictionaryTests.cs` | ~16 | Set/Remove/Clear, delta serialization, full state, OnChanged, TryGetValue, enumerate |
| `PacketFragmenterTests.cs` | ~12 | Single-fragment fast path, multi-fragment round-trip, out-of-order, stale cleanup, max payload, duplicate ignore |
| `NetworkIdTests.cs` | ~8 | Partition generation, scene object IDs, demo ball IDs, well-known IDs, reserved PrefabIds |
| `CompressionTests.cs` | ~12 | Vector3Half accuracy, compressed rotation (smallest-three) round-trip, edge cases, many angles |
| `PacketCompressionTests.cs` | ~10 | Deflate compress/decompress round-trip, various sizes, offsets, empty data, random data, threshold edge cases |

**Run:** Window > General > Test Runner > EditMode > Run All

## Bug/TODO Tracking

See `BUGS.MD` and `TODO.MD` in the repo root for known issues and planned work.

## Rules

- **Do NOT modify the C# source** in Source/Core or Source/Generated. These are Epic's auto-generated files.
- **Do NOT remove platform support.** All platforms stay included.
- When updating the SDK, preserve the Plugins/ folder organization (by platform subfolder).
- **ALWAYS increment version** in `package.json` before each git push.
