# Platforms

EOS Native includes native libraries for all major platforms.

## Supported Platforms

| Platform | Library | Architecture | Size |
|----------|---------|-------------|------|
| Windows | EOSSDK-Win64-Shipping.dll | x86_64 | 19 MB |
| Windows | EOSSDK-Win32-Shipping.dll | x86 | 15 MB |
| macOS | libEOSSDK-Mac-Shipping.dylib | Universal | 46 MB |
| Linux | libEOSSDK-Linux-Shipping.so | x86_64 | 26 MB |
| Linux | libEOSSDK-LinuxArm64-Shipping.so | ARM64 | 23 MB |
| iOS | EOSSDK.framework | ARM64 | ~25 MB |
| Android | eossdk-StaticSTDC-release.aar | ARM64/ARMv7 | 37 MB |

## Feature Support by Platform

| Feature | Windows | Mac | Linux | Android | iOS |
|---------|---------|-----|-------|---------|-----|
| Auth | Yes | Yes | Yes | Yes | Yes |
| Lobbies | Yes | Yes | Yes | Yes | Yes |
| Voice (RTC) | Yes | Yes | Yes | Yes | Yes |
| P2P | Yes | Yes | Yes | Yes | Yes |
| Cloud Storage | Yes | Yes | Yes | Yes | Yes |
| Achievements | Yes | Yes | Yes | Yes | Yes |
| Anti-Cheat | Yes | Yes | Yes | No | No |
| Overlay | Yes | No | No | No | No |

## Windows Notes

- Requires `xaudio2_9redist.dll` for voice/RTC
- DLL path auto-resolved from package location
- Both x86 and x64 included

## Android Notes

- Minimum API level 23
- IL2CPP scripting backend recommended
- AAR includes ARM64 and ARMv7

## iOS Notes

- Requires valid provisioning profile
- Includes both `.framework` and `.xcframework`

## Platform Detection

```csharp
#if UNITY_STANDALONE_WIN || UNITY_EDITOR_WIN
    // Windows-specific code
#endif

#if UNITY_ANDROID
    // Android-specific code
#endif

#if UNITY_IOS
    // iOS-specific code
#endif
```
