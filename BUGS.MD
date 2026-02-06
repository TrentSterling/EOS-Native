# Known Bugs

## Fixed

### XAudio2 DLL path resolution fails in UPM layout
- **Status:** FIXED
- **Reporter:** Internal (2026-02-06)
- **Description:** "Failed to load custom XAudio2.9 dll" error because `GetXAudio2DllPath()` had a hardcoded path from the old flat project layout (`Assets/Plugins/EOSSDK/Windows/x64/`), but the UPM package puts the DLL at `Assets/com.tront.eos-native/Runtime/EOSSDK/Plugins/Windows/x64/`.
- **Fix:** Updated to search multiple candidate paths (UPM, embedded, legacy flat) and log which path was found.
- **Files:** `EOSManager.cs` (~lines 659-695)

### Native DLL loading fails when installed as UPM git package
- **Status:** FIXED
- **Reporter:** Internal (2026-02-06)
- **Description:** "Failed to load EOS SDK library" because `AssetDatabase.GUIDToAssetPath()` returns virtual path `Packages/com.tront.eos-native/...` but kernel32 `LoadLibrary` needs an absolute filesystem path.
- **Fix:** Wrapped with `Path.GetFullPath()` to resolve to actual path in `Library/PackageCache/`.
- **Files:** `EOSManager.cs` (~line 1217)

### Host migration on by default with no toggle
- **Status:** FIXED
- **Reporter:** CometDev (2026-02-06)
- **Description:** Host migration was always enabled with no way to disable it from Inspector or runtime UI.
- **Fix:** Added host migration toggle to both Inspector lobby controls and F1 overlay Lobbies tab.
- **Files:** `EOSManager.cs`, `EOSNativeStatusUI.cs`

### Quick Match doesn't auto-host when no match found
- **Status:** FIXED
- **Reporter:** CometDev (2026-02-06)
- **Description:** Quick Match button in both the Inspector and runtime F1 overlay only called `QuickMatchAsync()` (search-only). When no lobbies were found, it just showed "No lobbies found" instead of automatically hosting a new lobby.
- **Fix:** Changed both Inspector (`EOSManager.cs` EOSManagerEditor) and runtime UI (`EOSNativeStatusUI.cs`) to call `QuickMatchOrHostAsync()` which automatically creates and hosts a new lobby when no existing lobbies are found.
- **Files:** `EOSManager.cs` (inspector QuickMatch method), `EOSNativeStatusUI.cs` (QuickMatch method)

### No voice toggle in Inspector host controls
- **Status:** FIXED
- **Reporter:** CometDev (2026-02-06)
- **Description:** The Inspector's Host button hardcoded `EnableVoice = true` with no way to toggle it off. The runtime F1 overlay had a voice toggle, but the Inspector did not.
- **Fix:** Added `_enableVoice` toggle to the Inspector lobby controls. Both Host and Quick Match now respect this toggle.
- **Files:** `EOSManager.cs` (EOSManagerEditor class)

### Ranked matchmaking crash on application quit
- **Status:** FIXED
- **Reporter:** Internal (2026-02-06)
- **Description:** `[EOSPlayerDataStorage] JSON serialize failed: Object reference not set to an instance of an object` during `OnDestroy` when Unity is quitting. `JsonUtility.ToJson` throws because Unity's serialization system is already shutting down.
- **Fix:** Added `OnApplicationQuit` guard to skip save during shutdown, plus try-catch safety net.
- **Files:** `EOSRankedMatchmaking.cs` (`OnDestroy`)

### Chat manager not auto-creating
- **Status:** FIXED
- **Reporter:** Internal (2026-02-06)
- **Description:** F1 overlay Lobby Chat section showed "Chat manager not found" because `EOSLobbyChatManager` was a find-only singleton (no auto-create). If no `EOSLobbyChatManager` component existed in the scene, it would never be created.
- **Fix:** Added auto-create pattern to `EOSLobbyChatManager.Instance` (creates `[EOSLobbyChatManager]` GameObject with DontDestroyOnLoad).
- **Files:** `EOSLobbyChatManager.cs` (Instance property)

## Open

_(none currently)_
