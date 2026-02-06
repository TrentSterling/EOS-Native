# Architecture

Technical overview of EOS Native.

## Package Structure

```
Assets/com.tront.eos-native/
├── package.json                         (UPM manifest)
├── README.md
├── LICENSE.md
├── Runtime/
│   ├── Epic.OnlineServices.asmdef       (assembly definition)
│   ├── EOSSDK/
│   │   ├── Source/
│   │   │   ├── Core/                    (13 files - P/Invoke helpers)
│   │   │   └── Generated/              (1048+ files - API bindings)
│   │   └── Plugins/
│   │       ├── Windows/x64/            (EOSSDK + xaudio2)
│   │       ├── Windows/x86/
│   │       ├── macOS/
│   │       ├── Linux/
│   │       ├── iOS/
│   │       └── Android/
│   └── EOSNative/
│       ├── Core/                        EOSManager, EOSConfig
│       ├── Lobbies/                     EOSLobbyManager, EOSLobbyChatManager
│       ├── Voice/                       EOSVoiceManager
│       ├── Party/                       EOSPartyManager
│       ├── Social/                      Friends, Presence, Invites, LFG, Clans
│       ├── Moderation/                  Achievements, Reports, Sanctions
│       ├── AntiCheat/                   EOSAntiCheatManager
│       ├── Storage/                     Player/Title Storage
│       ├── Replay/                      Recording, Playback, Storage
│       ├── UI/                          Status overlay, Toasts
│       └── Logging/                     Debug logger
└── EOSNative.Editor/                    Setup wizard, menus, inspectors
```

## Singleton Pattern

Most managers use auto-creating singletons:

```csharp
public static EOSLobbyManager Instance
{
    get
    {
        if (_instance == null)
        {
            _instance = FindAnyObjectByType<EOSLobbyManager>();
            if (_instance == null)
            {
                var go = new GameObject("EOSLobbyManager");
                _instance = go.AddComponent<EOSLobbyManager>();
                DontDestroyOnLoad(go);
            }
        }
        return _instance;
    }
}
```

This means consumers don't need to manually place components in the scene. Just access `.Instance` and it exists.

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

- **`Epic.OnlineServices`** assembly name matches what every EOS consumer expects
- **`allowUnsafeCode: true`** required for P/Invoke interop
- **`!EOS_DISABLE`** - add `EOS_DISABLE` to strip EOS from compilation

## Namespace

Manager code uses:
```csharp
namespace EOSNative.Core
namespace EOSNative.Voice
namespace EOSNative.Lobbies
// etc.
```

EOS SDK uses:
```csharp
namespace Epic.OnlineServices
namespace Epic.OnlineServices.Platform
namespace Epic.OnlineServices.Lobby
// etc.
```

## Async Pattern

All operations use async/await with `TaskCompletionSource<T>`:

```csharp
public async Task<Result> CreateLobbyAsync(CreateLobbyOptions options)
{
    var tcs = new TaskCompletionSource<Result>();

    _lobbyInterface.CreateLobby(ref eosOptions, null, (ref CreateLobbyCallbackInfo info) =>
    {
        tcs.SetResult(info.ResultCode);
    });

    return await tcs.Task;
}
```

## SDK Initialization

- `PlatformInterface.Initialize` runs only once per process
- `Result.AlreadyConfigured` is normal in Editor (persists across play sessions)
- `Platform.Tick()` must be called every frame for callbacks to fire

## Framework Agnostic

EOS Native has **no dependencies** on any networking framework. It works with:
- FishNet
- Mirror
- Netcode for GameObjects
- Custom solutions
- No networking at all (lobbies, voice, storage work standalone)

The companion **FishNet EOS Native Transport** (`com.tront.fishnet-eos-native`) adds FishNet-specific P2P transport on top.
