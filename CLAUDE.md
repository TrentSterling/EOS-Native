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
- **defineConstraints: `!EOS_DISABLE`** - add `EOS_DISABLE` to scripting defines to strip EOS from compilation

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
var options = new LobbyOptions()
    .WithName("Pro Players")
    .WithGameMode("deathmatch")
    .WithMaxPlayers(16)
    .WithVoice()
    .WithRegion("us-east");

await lobbyMgr.CreateLobbyAsync(options);   // → LobbyCreateOptions
await lobbyMgr.SearchLobbiesAsync(options);  // → LobbySearchOptions

var quick = LobbyOptions.QuickMatch();
var ranked = LobbyOptions.ForSkillRange(1500, 200);
```

Fields are split into shared (name, gamemode, maxplayers), create-only (voice, crossplay, hostmigration), and search-only (maxresults, skill range, platform/input filters). Irrelevant fields are gracefully ignored during conversion.

## Singleton Auto-Creation Pattern

Most managers use a lazy auto-create singleton pattern: `FindAnyObjectByType<T>()` first, then `new GameObject` parented under `EOSManager.Instance.transform` if not found. This keeps the hierarchy clean — all auto-created managers appear as children of the EOSManager object instead of flooding the DontDestroyOnLoad root.

```csharp
if (_instance == null)
{
    var go = new GameObject("ManagerName");
    if (EOSManager.Instance != null)
        go.transform.SetParent(EOSManager.Instance.transform);
    else
        DontDestroyOnLoad(go);
    _instance = go.AddComponent<ManagerType>();
}
```

The Awake method also guards against duplicate DontDestroyOnLoad: `if (transform.parent == null) DontDestroyOnLoad(gameObject);`

**Auto-create singletons:** EOSLobbyManager, EOSVoiceManager, EOSStats, EOSAchievements, EOSPartyManager, EOSAntiCheatManager, EOSPlayerRegistry, EOSReports, EOSSanctions, EOSCustomInvites, EOSFriends, EOSLeaderboards, EOSPresence, EOSUserInfo, EOSPlayerDataStorage, EOSTitleStorage, EOSMetrics, EOSP2PManager, P2PDemoManager, and more.

**Find-only singletons (no auto-create):** EOSTournamentManager, EOSClanManager, EOSGlobalChatManager, EOSReplayHighlights, EOSReplayVoicePlayer, EOSReplayVoiceRecorder.

## Network Prefab Table

`NetworkPrefabTable` is a ScriptableObject for registering spawnable prefabs. Index in the list = PrefabId used by `NetworkManager.Spawn()`.

**Setup:** Right-click → Create → EOS Native → Network Prefab Table. Drag prefabs into the list. Assign to `NetworkManager.PrefabTable` in Inspector.

**Auto-registration:** When `NetworkManager.OnEnable()` fires, all prefabs in the table are registered via `RegisterPrefab(prefab, index)` before router subscription. Table entries merge with runtime `RegisterPrefab()` calls — table takes priority for overlapping IDs.

**Runtime API:** `NetworkPrefabTable.AddPrefab(go)` appends, `RemovePrefabAt(index)` removes. `OnValidate()` warns if entries lack `NetworkObject` components.

**Backward compat:** `RegisterPrefab()`, `RegisterExisting()`, and the serialized `_prefabs` list on NetworkManager all still work. The table is optional.

## Documentation Site

Full docsify documentation at `docs/` folder (39 files). Live at **https://tront.xyz/EOS-Native/** (GitHub Pages, `/docs` on `main` branch).

## Inspector Quick Match Behavior

The Inspector's Quick Match button calls `QuickMatchOrHostAsync()` which:
1. Searches for available lobbies
2. If found, joins a random one
3. **If none found, automatically creates and hosts a new lobby**

Both the Inspector (EOSManagerEditor) and the runtime F1 overlay (EOSNativeStatusUI) use this pattern. The Inspector also exposes Lobby Name, Max Players, Voice toggle, and Host Migration toggle.

### Lobby Creation Voice Fallback

`CreateLobbyAsync` includes automatic voice fallback logic. If the EOS SDK returns an error when creating a lobby with `EnableRTCRoom = true` (e.g., on platforms where RTC isn't available), it automatically retries without voice. This prevents `InvalidRequest` errors on Android and other platforms where the RTC module may not be initialized.

### Voice Audio Output Mode (UseManualAudioOutput)

Both `CreateLobbyInternal` and `JoinLobbyByIdAsync` set `LocalRTCOptions.UseManualAudioOutput` from `EOSVoiceManager.Instance.UseManualAudioOutput`.

- **`false` (default)** — SDK auto-plays received voice audio. Works out of the box for any lobby/demo without needing `EOSVoicePlayer` components. `AddNotifyAudioBeforeRender` callbacks still fire for speaking indicators, VU meters, and custom audio processing.
- **`true`** — SDK does NOT auto-play. Audio frames are delivered only via `OnAudioBeforeRender` callback. Requires `EOSVoicePlayer`/`NetworkVoicePlayer` components with `AudioSource` for playback. Use this for spatial 3D voice.

Set `EOSVoiceManager.Instance.UseManualAudioOutput = true` **before** creating or joining a lobby when using `NetworkVoicePlayer` on player prefabs.

**History:** Previously `UseManualAudioOutput` was hardcoded to `true` on lobby creation and not set on join, causing the lobby creator to hear nothing (no `EOSVoicePlayer` in the demo) while the joiner got SDK auto-play. This manifested as "PC creates lobby, Android joins, no voice" but "Android creates lobby, PC joins, voice works" — the asymmetry was purely about who was creator (manual, broken) vs joiner (auto-play, working).

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

## Detailed Reference Documentation

Subsystem details are split into companion files to keep this file fast to load:

- **[CLAUDE-NETWORKING.md](CLAUDE-NETWORKING.md)** — Layer 2 networking: NetworkObject, SyncVar, NetworkManager, NetworkTransform, SyncList, SyncDictionary, RPCs, [NetRpc] IL weaver, spectator mode, RPC validation, NetworkStats, NetworkRoomState, NetworkPlayerState, NetworkSceneManager, packet compression, nested NetworkObjects, runtime reparenting, automated tests
- **[CLAUDE-P2P.md](CLAUDE-P2P.md)** — Layer 1 P2P transport: NetWriter/NetReader, PacketFragmenter, MessageRouter, P2P Ball Demo, P2P connection establishment (host-order fix + retry)
- **[CLAUDE-VOICE.md](CLAUDE-VOICE.md)** — Voice/RTC: audio device selection, spatial voice system (EOSVoiceZoneManager, EOSVoiceTriggerZone, NetworkVoicePlayer)
- **[CLAUDE-ANDROID.md](CLAUDE-ANDROID.md)** — Android build: Gradle config, desugaring, AndroidX, Java classloader fix, RTC prerequisites, AndroidInitializeOptions
- **[CLAUDE-UI.md](CLAUDE-UI.md)** — Runtime UI: F1 overlay tabs, Canvas UI, ported managers, runtime console, setup wizard

## Bug/TODO Tracking

See `BUGS.MD` and `TODO.MD` in the repo root for known issues and planned work.

## Rules

- **Do NOT modify the C# source** in Source/Core or Source/Generated. These are Epic's auto-generated files.
- **Do NOT remove platform support.** All platforms stay included.
- When updating the SDK, preserve the Plugins/ folder organization (by platform subfolder).
- **ALWAYS increment version** in `package.json` before each git push.
