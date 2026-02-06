# ParrelSync Support

Test multiplayer locally with multiple Unity editors.

## Overview

EOS-Native has built-in ParrelSync support. Each clone gets a unique device identity automatically — no configuration needed.

## How It Works

When ParrelSync is installed and a clone project is detected:

1. **Device ID uniqueness** - `EOSManager` appends a hash of the clone's project path to the device model string, ensuring each clone creates a separate EOS device identity
2. **Display name suffix** - Each clone gets a unique display name (e.g., "Player_2", "Player_3") to distinguish players in lobbies
3. **Separate credentials** - Device token auth creates unique credentials per clone, so each editor logs in as a different user

## No Setup Required

Just install ParrelSync and create clones. EOS-Native detects clones automatically via reflection:

```csharp
// EOSManager uses reflection to check ParrelSync
// No compile-time dependency required
Type clonesManagerType = Type.GetType("ParrelSync.ClonesManager, ParrelSync");
MethodInfo isCloneMethod = clonesManagerType.GetMethod("IsClone");
bool isClone = (bool)isCloneMethod.Invoke(null, null);
```

This means:
- No `#define` symbols needed
- No assembly references to add
- Works whether ParrelSync is installed or not
- Zero configuration

## Testing Workflow

1. Install [ParrelSync](https://github.com/VeriorPies/ParrelSync) in your Unity project
2. Open **ParrelSync > Clones Manager** and create 1-2 clones
3. Open the clone project(s) in separate Unity editor instances
4. Press Play in the main project — it auto-creates a device token and logs in
5. Press Play in clone(s) — each gets a unique device identity and logs in as a different user
6. Use Quick Match or Host + Join by code to connect

## What Gets Uniquified

| Component | Main Project | Clone |
|-----------|-------------|-------|
| Device Model | `DESKTOP-ABC123` | `DESKTOP-ABC123_-1234567` |
| Display Name | `Player` | `Player_2` |
| Device Token | Unique per model | Unique per model (different) |
| EOS PUID | Unique | Unique (different user) |

## Platform Helper

`EOSPlatformHelper` also detects ParrelSync clones and appends the project path hash to the platform model string. This is used in the preprocessor-based path:

```csharp
#if PARRELSYNC
if (ParrelSync.ClonesManager.IsClone())
{
    model += "_" + ParrelSync.ClonesManager.GetCurrentProjectPath().GetHashCode();
}
#endif
```

Both the reflection-based (EOSManager) and preprocessor-based (EOSPlatformHelper) paths produce the same result.

## Troubleshooting

### Both editors show the same player

- Ensure ParrelSync is installed and the clone was created properly
- Check the Unity console for "ParrelSync clone detected" log message
- Verify clones are opened from the ParrelSync Clones Manager (not just copying the folder)

### Clone fails to login

- Each clone needs its own device token — the first login creates it automatically
- If a clone was previously used as the main project, clear its `Library/` folder
- Check that your EOS application allows multiple device tokens

### Voice not working in clones

- Windows audio devices are shared across editor instances
- Use headphones or set different audio devices per editor
- The F1 overlay Voice tab shows audio device selection
