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
