# EOS C# SDK Reference

> Source: [dev.epicgames.com](https://dev.epicgames.com/docs/epic-online-services/eos-get-started/eossdkc-sharp-getting-started)

## Overview

The EOS C# SDK provides an object-oriented wrapper around the native C SDK, following C# conventions. API version numbers in data structures are pre-populated automatically.

## Requirements

- **.NET Framework 3.5** or higher (or compatible equivalent)
- Sample projects need .NET Core 3.1 + Visual Studio 2019+

## Integration Steps

1. Include EOS SDK C# source files in your project
2. Ensure the native library binary is accessible (e.g. `EOSSDK-Win64-Shipping.dll`)
3. Set platform symbol if needed (e.g. `EOS_PLATFORM_WINDOWS_64`) in `Epic.OnlineServices.Common`

## Core Lifecycle

```csharp
// 1. Initialize
var initOptions = new InitializeOptions {
    ProductName = "MyGame",
    ProductVersion = "1.0.0"
};
PlatformInterface.Initialize(ref initOptions);

// 2. Create Platform
var platformOptions = new Options {
    ProductId = "...",
    SandboxId = "...",
    DeploymentId = "...",
    ClientCredentials = new ClientCredentials { ClientId = "...", ClientSecret = "..." }
};
var platform = PlatformInterface.Create(ref platformOptions);

// 3. Tick (every frame)
platform.Tick();

// 4. Shutdown (NOT in Unity Editor!)
platform.Release();
PlatformInterface.Shutdown();
```

## Handle Objects & Memory

### Automatic Handling

Most API results (structs, info copies) are automatically marshaled - no manual release needed.

```csharp
// Structs returned by Copy* functions are auto-managed
var info = lobbyDetails.CopyInfo(...); // No release needed
```

### Manual Handle Release

Functions like `CreatePresenceModification` return `Handle` objects that **must be manually released**:

```csharp
var handle = presenceInterface.CreatePresenceModification(...);
handle.SetData(...);
handle.Release(); // Required!
```

## Tick Requirements

```csharp
platform.Tick();
```

- Call from game thread regularly
- **Every frame** is ideal; every 100ms is acceptable minimum
- Processes async operations, fires callbacks
- `TickBudgetInMilliseconds` in platform options controls work budget per tick

## Dynamic Bindings

For on-demand library loading/unloading (especially Unity Editor):

### Setup

1. Define `EOS_DYNAMIC_BINDINGS` preprocessor symbol
2. Load library dynamically at runtime

### Windows Example

```csharp
[DllImport("Kernel32.dll")]
private static extern IntPtr LoadLibrary(string lpLibFileName);

[DllImport("Kernel32.dll")]
private static extern int FreeLibrary(IntPtr hLibModule);

[DllImport("Kernel32.dll")]
private static extern IntPtr GetProcAddress(IntPtr hModule, string lpProcName);

// Load
var libraryPointer = LoadLibrary(libraryPath);
Bindings.Hook(libraryPointer, GetProcAddress);

// Unload
Bindings.Unhook();
FreeLibrary(libraryPointer);
```

> Hook platform-specific bindings classes **in addition to** base `Epic.OnlineServices.Bindings`.

## EOS Overlay (Unity)

### Requirements

Must complete **before** creating graphics devices:

1. Load EOS SDK library
2. Call `EOS_Initialize`
3. Call `EOS_Platform_Create`

### Unity Implementation

Use a "GfxPlugin"-prefixed native rendering plugin:

1. Create native library with `GfxPlugin` prefix
2. Export `UnityPluginLoad(void*)` that loads SDK and creates platform
3. Export function returning the platform handle to construct `PlatformInterface` in C#

> **The EOS overlay is NOT supported in Unity Editor.** Disable it explicitly in editor.

### Required for

- Social Features
- Authorization
- Purchasing (Desktop)

## Custom Memory Delegates

For platforms requiring custom allocators (consoles):

```csharp
// Create native library exporting allocation functions
// Pass function pointers to InitializeOptions:
var initOptions = new InitializeOptions {
    AllocateMemoryFunction = GetAllocateFunctionPointer(),
    ReallocateMemoryFunction = GetReallocateFunctionPointer(),
    ReleaseMemoryFunction = GetReleaseFunctionPointer()
};
```

## Unity-Specific Considerations

### Editor vs Standalone

| Context | SDK Lifetime | Bindings |
|---------|-------------|----------|
| **Standalone Build** | Native rendering plugin | Static |
| **Editor** | MonoBehaviour | Dynamic (since SDK 1.12) |

### Critical Rules

1. **Never call Release or Shutdown in editor** - prevents reinitialization without restart
2. **Overlay not supported in editor** - disable explicitly
3. **Dynamic bindings required in editor** (SDK 1.12+) for on-demand load/unload
4. **Tick in Update()** - call `platform.Tick()` in MonoBehaviour.Update

### Platform Symbols

Set in project settings or asmdef:

| Platform | Symbol |
|----------|--------|
| Windows x64 | `EOS_PLATFORM_WINDOWS_64` |
| Windows x86 | `EOS_PLATFORM_WINDOWS_32` |
| macOS | `EOS_PLATFORM_MACOS` |
| Linux | `EOS_PLATFORM_LINUX` |
| iOS | `EOS_PLATFORM_IOS` |
| Android | `EOS_PLATFORM_ANDROID` |

## Sample Applications

| Sample | Description |
|--------|-------------|
| **SimpleAuth** | Authentication + presence (sign-in, presence editing) |
| **SimpleOverlayPurchasing** | In-game purchasing via overlay (EGS partners only) |
| **VoiceServer** | RESTful trusted voice server |
| **VoiceClient** | WPF voice client (rooms, devices, mute, kick) |

## See Also

- [Platform Interface](ref-platform.md) - SDK initialization and lifecycle
- [SDK Conventions](ref-conventions.md) - Error codes, threading, throttling
