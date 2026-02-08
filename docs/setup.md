# Setup Guide

Complete setup guide for EOS Native.

## Prerequisites

- Unity 2021.3+ (tested with Unity 6)
- EOS Developer Portal account

## Step 1: Install EOS Native

### Via Package Manager (Git URL)

1. Open Package Manager (`Window > Package Manager`)
2. Click `+` > `Add package from git URL`
3. Enter: `https://github.com/TrentSterling/EOS-Native.git?path=Assets/com.tront.eos-native`

### Via Local Package

1. Clone or download the repository
2. In Package Manager, click `+` > `Add package from disk`
3. Select `Assets/com.tront.eos-native/package.json`

## Step 2: Configure EOS Credentials

### Opening the Setup Wizard

`EOS Native > Setup Wizard` in the Unity Editor menu bar.

The Setup Wizard is a three-tab editor window that handles configuration, dependencies, and project info. See the [Setup Wizard](#setup-wizard) section below for full details on each tab.

### Getting Credentials from EOS Portal

1. Go to [dev.epicgames.com/portal](https://dev.epicgames.com/portal)
2. Create or select your product
3. Navigate to **Product Settings**
4. Note down:
   - Product ID
   - Sandbox ID
   - Deployment ID
   - Client ID
   - Client Secret

### Required Fields

| Field | Description |
|-------|-------------|
| Product Name | Your game's name |
| Product ID | From EOS Portal |
| Sandbox ID | From EOS Portal |
| Deployment ID | From EOS Portal |
| Client ID | From EOS Portal |
| Client Secret | From EOS Portal |
| Encryption Key | 64 hex characters (auto-generated) |

### Encryption Key

The encryption key must be **exactly 64 hexadecimal characters**. The wizard can auto-generate one for you.

Example valid key:
```
1234567890ABCDEF1234567890ABCDEF1234567890ABCDEF1234567890ABCDEF
```

## Step 3: Add EOSManager to Scene

### Automatic

`Tools > EOS Native > Setup Scene`

### Manual

1. Create empty GameObject
2. Add `EOSManager` component
3. Assign your EOSConfig asset
4. Enable **Auto Initialize** and **Auto Login**

### Auto-Created Components

The following are **auto-created singletons** - you don't need to add them manually:
- EOSLobbyManager
- EOSVoiceManager
- EOSLobbyChatManager
- EOSPartyManager
- EOSFriends
- EOSStats
- EOSAchievements
- EOSLeaderboards
- And more...

## Step 4: Verify Setup

1. Enter Play Mode
2. Check Console for "EOS Initialized" message
3. Press **F1** to open debug overlay
4. Verify status shows SDK Initialized + Logged In

## Platform-Specific Setup

### Windows

DLLs are configured automatically. Verify in Inspector:
- `EOSSDK-Win64-Shipping.dll` → Windows x64
- `EOSSDK-Win32-Shipping.dll` → Windows x86
- `xaudio2_9redist.dll` → Required for voice (auto-resolved)

### macOS

- `libEOSSDK-Mac-Shipping.dylib` → macOS Universal

### Linux

- `libEOSSDK-Linux-Shipping.so` → Linux x64
- `libEOSSDK-LinuxArm64-Shipping.so` → Linux ARM64

### Android

1. Set minimum API level to 23+
2. Enable IL2CPP scripting backend
3. AAR is included automatically

### iOS

1. Requires valid provisioning profile
2. Framework included automatically

## Conditional Compilation

Add `EOS_DISABLE` to Scripting Define Symbols to strip EOS from compilation entirely. Useful for builds without EOS or when other packages reference EOS but the SDK isn't installed.

## Troubleshooting

### "The type or namespace 'Epic' could not be found"

**Cause:** Package not installed or assembly not resolving.

**Fix:** Verify the package appears in Package Manager and `Epic.OnlineServices.asmdef` exists.

### "Result.AlreadyConfigured"

**Cause:** EOS SDK can only be initialized once per process.

**Fix:** This is normal when re-entering Play Mode. Restart Unity if persistent.

### "Encryption key invalid"

**Cause:** Key isn't exactly 64 hex characters.

**Fix:** Use the wizard's auto-generate button.

### "Failed to load custom XAudio2.9 dll"

**Cause:** XAudio2 DLL not found at expected path.

**Fix:** The DLL should be at `Runtime/EOSSDK/Plugins/Windows/x64/xaudio2_9redist.dll`. The path resolver searches multiple locations automatically.

## Setup Wizard

Accessible via **EOS Native > Setup Wizard** in the Unity Editor menu bar. The wizard is implemented in `EOSSetupWizard.cs` (in `EOSNative.Editor/`) and provides three tabs: Setup, Dependencies, and About.

### Setup Tab

The Setup tab walks you through configuring your EOS credentials.

**Config ScriptableObject:** At the top, select an existing `EOSConfig` asset or create a new one. The config stores all your EOS credentials and is referenced by the EOSManager component at runtime.

**4-Step Configuration Guide:**

| Step | Fields |
|------|--------|
| 1. Product Info | Product Name |
| 2. Product IDs | Product ID, Sandbox ID, Deployment ID |
| 3. Client Credentials | Client ID, Client Secret |
| 4. Encryption Key | 64 hex characters (auto-generate button available) |

All values come from the [EOS Developer Portal](https://dev.epicgames.com/portal) under your product's settings.

**Validation:** A quick-check button validates that all required fields are populated and that the encryption key is exactly 64 hexadecimal characters. Invalid fields are highlighted so you can fix them before entering Play Mode.

### Dependencies Tab

The Dependencies tab shows the status of optional packages and helps you install or remove them without leaving the editor.

**ParrelSync:**
- Shows whether ParrelSync is currently installed
- **Install** button adds ParrelSync via its git URL (`https://github.com/VeriorPies/ParrelSync.git?path=/ParrelSync`)
- **Remove** button removes it from the project
- **Open GitHub** button opens the ParrelSync repository in your browser
- Installation and removal edit `Packages/manifest.json` directly and call `Client.Resolve()` to apply changes

**Input System:**
- Shows whether Unity's new Input System package is installed
- EOS Native supports both the legacy Input Manager and the new Input System

**uGUI (UnityEngine.UI):**
- Shows whether the Unity UI package is available
- Required for the Canvas UI overlay (`EOSNativeCanvasUI`)

### About Tab

The About tab displays project information and useful links.

| Item | Details |
|------|---------|
| Package Version | Read from `package.json` at runtime |
| SDK Version | 1.18.1.2 (Epic Online Services C# SDK) |
| Description | Brief summary of what EOS Native provides |
| Links | Documentation site, GitHub repository, Epic Developer Portal, EOS SDK documentation |
| Feature List | 14 features including lobbies, voice, chat, friends, parties, stats, achievements, leaderboards, replays, anti-cheat, cloud storage, and more |
| Platform Table | 7 supported platforms: Windows x64, Windows x86, macOS, Linux x64, Linux ARM64, iOS, Android |
| Credits | Attribution and acknowledgments |
