# Known Bugs

## Fixed

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

## Open

_(none currently)_
