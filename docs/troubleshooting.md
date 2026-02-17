# Troubleshooting

Common issues and solutions.

## SDK Initialization

### "Result.AlreadyConfigured"

**Cause**: EOS SDK can only be initialized once per process.

**Solution**: This is normal in the Unity Editor. The SDK persists across play mode sessions. No action needed.

### "Cannot reinitialize after Shutdown"

**Cause**: Called `EOSManager.Shutdown()` then tried to use EOS again.

**Solution**: Avoid calling `Shutdown()` unless the application is closing.

### "Callbacks never fire"

**Cause**: `Tick()` is not being called.

**Solution**: Ensure `EOSManager` is in the scene and active. It calls `Platform.Tick()` in `Update()`.

## Lobby Issues

### "Failed to create lobby"

**Causes**:
- Rate limit exceeded (30/min)
- Already in max lobbies (16)
- Invalid attributes

**Solutions**:
1. Wait and retry
2. Leave other lobbies first
3. Check attribute key/value lengths (64/1000 char max)

### Ghost lobbies (joining dead/empty lobbies)

**Symptoms**: Client tries to join a lobby but gets stuck, or joins a lobby with no host.

**Cause**: EOS lobbies can linger in search results after all players leave. These "ghost lobbies" have 0 members or a null owner PUID.

**Solution**: EOS-Native v2.60.0+ automatically filters ghost lobbies at every level — search results, direct ID lookups, friend searches, and post-join validation. If you're on an older version, check `LobbyData.IsGhost` before joining:

```csharp
if (!lobbyData.IsGhost)
    await EOSLobbyManager.Instance.JoinLobbyByIdAsync(lobbyData.LobbyId);
```

### Transport stays connected after leaving lobby

**Cause**: Using `LeaveLobbySync()` on a version before v2.60.0 — it was missing the `OnLobbyLeft` event, so FishNet/P2P/NetworkManager were never notified to stop.

**Solution**: Update to v2.60.0+. `LeaveLobbySync()` now fires `OnLobbyLeft` like `LeaveLobbyAsync()` does.

### "Lobby code not found"

**Causes**:
- Typo in code
- Lobby was destroyed
- Lobby is private

**Solutions**:
1. Verify the exact lobby code
2. Have host confirm lobby still exists
3. Check lobby visibility settings

## Voice Issues

### "Failed to load custom XAudio2.9 dll"

**Cause**: XAudio2 DLL not found at expected path.

**Solution**: The DLL should be at `Runtime/EOSSDK/Plugins/Windows/x64/xaudio2_9redist.dll`. The path resolver checks multiple locations automatically. Verify the file exists.

### Host can't hear anyone (one-way voice)

**Symptoms**: Host can transmit voice (others hear the host), but host cannot hear any other participants. Clients hear each other fine.

**Cause**: RTC notification race condition in versions before v2.61.0. When the host creates a lobby, clients could join the RTC room before the host finished registering `OnParticipantStatusChanged`, so those participants were never tracked.

**Solution**: Update to v2.61.0+. Participant registration is now lazy — any incoming audio data or status update auto-registers the participant.

### Can't reconnect after leaving lobby

**Cause**: In versions before v2.61.0, `LeaveLobbyAsync()` cleared state after the EOS leave call. Stale notifications during the await window could overwrite the cleared state.

**Solution**: Update to v2.61.0+. State cleanup now happens before the EOS leave call.

### Host migration takes too long

**Cause**: In versions before v2.61.0, the lobby data retry loop waited up to 6.5 seconds for the EOS SDK cache to populate.

**Solution**: Update to v2.61.0+. Worst-case retry time reduced from 6.5s to 1.75s.

### "No voice audio"

**Causes**:
- Microphone permissions denied
- User is muted
- RTC not connected
- Lobby not created with voice enabled

**Solutions**:
1. Check platform microphone permissions
2. Verify `IsMuted` is false in F1 Voice tab
3. Verify lobby has `EnableVoice = true`

### "Wrong microphone/speakers"

**Cause**: Default audio device is not the one you want.

**Solution**: Use the F1 overlay Voice tab to select input/output devices from the dropdowns. Or programmatically:

```csharp
var voice = EOSVoiceManager.Instance;
voice.QueryAudioDevices();
voice.SetInputDevice(desiredDeviceId);
voice.SetOutputDevice(desiredDeviceId);
```

### "Echo/feedback"

**Cause**: Echo cancellation not enabled.

**Solution**: Configure echo cancellation in EOS Developer Portal settings.

## DLL Issues

### "DLL not found" (Windows)

**Cause**: Platform-specific DLLs not configured correctly.

**Solution**:
1. Select DLL in Project window
2. Check Inspector for platform settings
3. Ensure `EOSSDK-Win64-Shipping.dll` → Windows x64
4. Ensure `EOSSDK-Win32-Shipping.dll` → Windows x86

### "EntryPointNotFoundException"

**Cause**: Wrong DLL version or architecture mismatch.

**Solution**: Verify DLL matches your EOS SDK version.

## Credentials Issues

### "Invalid encryption key"

**Cause**: Key is not exactly 64 hex characters.

**Solution**: Use the Setup Wizard to generate a valid key.

### "Authentication failed"

**Causes**:
- Invalid credentials
- Wrong sandbox/deployment
- Portal configuration issue

**Solutions**:
1. Verify all credentials match EOS Developer Portal
2. Check sandbox and deployment IDs
3. Ensure DeviceID auth is enabled in portal

## Android Issues

### "Native library failed to load"

**Solutions**:
1. Enable IL2CPP scripting backend
2. Set minimum API level 23+
3. Verify AAR is included in `Plugins/Android/`

## Performance Issues

### "Frame rate drops"

**Cause**: Main thread blocked by network operations.

**Solution**: All operations should use async/await. Don't call `.Result` on tasks.

## Getting Help

1. Press F1 for the debug overlay
2. Enable relevant debug categories
3. Check Console for error messages
4. Review [GitHub Issues](https://github.com/TrentSterling/EOS-Native/issues)
5. Consult [EOS Documentation](https://dev.epicgames.com/docs)
