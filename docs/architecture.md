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
│       ├── Social/                      Friends, Presence, Invites, LFG, Clans, GlobalChat
│       ├── Moderation/                  Achievements, Reports, Sanctions
│       ├── AntiCheat/                   EOSAntiCheatManager
│       ├── Storage/                     Player/Title Storage
│       ├── Replay/                      Recording, Playback, Storage, Highlights, Voice
│       ├── UI/                          Status overlay, Toasts
│       ├── Debug/                       Debug settings, logger
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

## Networking Stack

EOS Native includes a complete networking stack organized into two layers. No external networking framework (FishNet, Mirror, Netcode, etc.) is required.

### Layer 1: P2P Transport Toolkit

The foundation layer provides binary serialization, packet management, and message dispatch. All files live in `Runtime/EOSNative/P2P/`.

| Component | Description |
|-----------|-------------|
| **NetWriter / NetReader** | Binary serializer/deserializer pair with auto-growing buffer, packed varint support, pooling, and built-in handlers for all common Unity types (Vector3, Quaternion, Color, etc.) |
| **PacketFragmenter** | Splits messages exceeding the EOS P2P limit (1170 bytes) into fragments and reassembles them. 7-byte header per fragment. Single-fragment fast path with no dictionary lookup. Stale cleanup after 5 seconds. |
| **MessageRouter** | Typed message registration and dispatch with frame batching. Groups messages by (channel, reliability, target) and flushes once per frame. Supports opt-in Deflate compression. |
| **EOSP2PManager** | Reusable singleton P2P mesh manager. Auto-accepts connections, manages peer lifecycle, integrates with lobby events. Handles handshake retry for cross-platform connection establishment. |

Wire format: each EOS P2P packet (max 1170 bytes) contains a fragment header (packetId, fragmentIndex, lastFragment) followed by a router envelope (batch flag, message IDs, payloads).

See [P2P Transport](p2p-transport.md) for full details.

### Layer 2: High-Level Networking

Built on top of Layer 1, this provides object identity, state synchronization, spawning, authority, and RPCs. All files live in `Runtime/EOSNative/Net/`.

| Component | Description |
|-----------|-------------|
| **NetworkObject** | Core component on any synced GameObject. Manages identity (NetworkId), ownership (OwnerId), and an ordered list of SyncVars. |
| **NetworkBehaviour** | Optional convenience base class with shortcuts to NetworkObject, ownership checks, and RPC support. |
| **SyncVar\<T\>** | Generic sync wrapper with dirty tracking, owner-write guard, and OnChanged callbacks. |
| **SyncList\<T\>** | Synchronized list with operation-based delta sync (Add, Set, RemoveAt, Insert, Clear). |
| **SyncDictionary\<TKey, TValue\>** | Synchronized dictionary with operation-based delta sync (Set, Remove, Clear). |
| **NetworkManager** | Singleton managing all NetworkObjects. Handles sync, spawn/despawn, snapshots, host migration, RPCs, pooling, and scene objects. |
| **NetworkTransform** | Hybrid transform sync: spring physics, buffered interpolation, extrapolation, and 3-tier distance LOD in one component. |
| **NetworkAnimator** | Syncs Animator parameters via packed SyncVar. Triggers sent via RPC. Auto-discovers parameters. |
| **EasySync** | No-code property sync inspired by Normcore. Check boxes in the Inspector to sync fields on sibling components. Reflection-based. |
| **NetworkRoomState** | Singleton NetworkObject for shared room/game state. Well-known ID, survives host migration. Auto-mirrors to lobby attributes. |
| **NetworkPlayerState** | Per-player NetworkObject. Auto-created on connect, destroyed on disconnect. Holds name, team, score, custom data. |
| **NetworkSceneManager** | Networked scene loading. Host calls LoadScene, all peers follow. Scene info stored on RoomState for late joiners. |
| **[NetRpc] Attribute** | IL post-processor (Mono.Cecil) rewrites `[NetRpc]`-marked methods into typed RPCs at compile time. Zero boilerplate. |
| **NetworkStats** | Per-peer and global connection quality metrics: RTT (ping/pong), packet loss, bandwidth, NAT type, connection type. |

See [Networking Overview](networking.md) for full details.

### Design Philosophy

- **Peer-authority model** -- each player owns their objects. No dedicated server required.
- **Deterministic host election** -- lexicographically lowest PUID among all peers. No communication needed, all peers agree independently.
- **Zero-hitch host migration** -- when the host disconnects, the new host claims orphaned objects by updating OwnerId. Objects continue running with their current SyncVar values. No destroy/reinstantiate cycle.
- **EOS-first** -- built directly on the EOS P2P interface, similar to how Unreal Engine uses EOS natively. No middleware, no relay servers beyond what EOS provides.
- **No external networking framework dependency** -- works standalone. The companion FishNet transport is a separate package for projects that need FishNet specifically.
- **Eventual consistency** -- state updates are sent unreliable for speed. If a packet drops, a reliable fallback resends after 200ms. Continuously-changing state stays unreliable; one-shot changes get reliable delivery automatically.
- **NetworkId partitioning** -- each peer generates IDs from their own partition (upper 16 bits = hash of PUID, lower 16 bits = counter). No collision between peers, no coordination needed.
