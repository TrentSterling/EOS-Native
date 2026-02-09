# CLAUDE-NETWORKING.md

Detailed Layer 2 networking reference. See CLAUDE.md for project overview and rules.

## Layer 2: NetworkObject / SyncVar / NetworkManager

High-level networking built on the P2P Transport Toolkit (Layer 1). Provides object identity, automatic state sync, spawn/despawn, late-join snapshots, authority transfer, and RPCs. All files in `Runtime/EOSNative/Net/`.

### Architecture

- **NetworkObject** — Core component on any synced GameObject. Manages identity (NetworkId), ownership (OwnerId), and an ordered list of SyncVars.
- **SyncVar\<T\>** — Generic sync wrapper with dirty tracking and owner-write guard. Only the owner can set values; remote peers receive deltas automatically.
- **NetworkBehaviour** — Optional convenience base class with shortcuts to NetworkObject.
- **NetworkManager** — Singleton managing all NetworkObjects. Handles sync, spawn/despawn, snapshots, migration, RPCs.
- **NetworkTransform** — Hybrid sync component: Spring physics, buffered interpolation, extrapolation, and distance LOD in one. Configurable SyncMethod (Auto/Spring/Interpolation) + ExtrapolationMode + 3-tier distance LOD.
- **NetworkAnimator** — Syncs Animator float/int/bool parameters via packed SyncVar, triggers via RPC. Auto-discovers parameters.
- **EasySync** — Normcore-style no-code sync. Check properties in Inspector to sync them — reflection-based, packed byte[] SyncVar.
- **SyncList\<T\>** — Synchronized list with operation-based delta sync (Add/Set/RemoveAt/Insert/Clear). Only changed ops sent over the wire.
- **SyncDictionary\<TKey, TValue\>** — Synchronized dictionary with operation-based delta sync (Set/Remove/Clear). Key-value pairs for inventories, scores, game state.
- **NetSerializers** — Static type registry for serialization. Built-in handlers for all common types + `INetSerializable` for custom.

### Usage

```csharp
public class Player : NetworkBehaviour
{
    SyncVar<Vector3> Position;
    SyncVar<float> Health;

    protected override void Awake()
    {
        base.Awake();
        Position = Sync(Vector3.zero);
        Health = Sync(100f);
        Health.OnChanged += (old, hp) => Debug.Log($"Health: {old} → {hp}");
        NetworkManager.Instance.RegisterRPC(Net, "TakeDamage", reader => {
            float dmg = NetSerializers.Read<float>(reader);
            Health.Value -= dmg;
        });
    }

    void Update()
    {
        if (!IsOwner) return;
        Position.Value = transform.position;
    }
}

// Spawning:
var player = NetworkManager.Instance.Spawn(prefabId: 0, pos, rot);

// RPC:
NetworkManager.Instance.SendRPC(player, "TakeDamage", RPCTarget.Owner, 25f);
```

### Message IDs (0xA0-0xAF)

| ID | Name | Reliability | Channel | Payload |
|----|------|-------------|---------|---------|
| 0xA0 | STATE_UPDATE | Unreliable | 0 | Packed object count + per-object: networkId, dataLen, dirtyMask, dirty values |
| 0xA1 | SPAWN | Reliable | 1 | prefabId, networkId, ownerId, position, rotation, syncVarCount, all values |
| 0xA2 | DESPAWN | Reliable | 1 | networkId |
| 0xA3 | AUTHORITY | Reliable | 1 | networkId, newOwnerId |
| 0xA4 | SNAPSHOT | Reliable | 1 | objectCount + per-object (same as SPAWN) |
| 0xA5 | SNAPSHOT_REQUEST | Reliable | 1 | empty |
| 0xA6 | RPC | Reliable | 1 | networkId, methodNameHash, argCount, typed args |
| 0xA7 | AUTHORITY_REQUEST | Reliable | 1 | networkId |
| 0xAA | SCENE_LOAD | Reliable | 1 | sceneName, additive (bool) |
| 0xAB | SCENE_UNLOAD | Reliable | 1 | sceneName |
| 0xAC | SCENE_LOADED_ACK | Reliable | 1 | sceneName |

### NetworkId Partitioning

Each peer generates IDs from their own partition: upper 16 bits = FNV-1a hash of local PUID, lower 16 bits = incrementing counter. No collision between peers.

### Host Election

Deterministic — lexicographically lowest PUID string among all connected peers + self. Recomputed on peer connect/disconnect. No communication needed.

### Authority Transfer (Host Migration)

When a peer disconnects, the new host claims orphaned objects by setting OwnerId to self and broadcasting AUTHORITY messages. Objects **continue running** — no destroy/reinstantiate. SyncVars already have latest values.

### Late Join

New peer sends SNAPSHOT_REQUEST to host. Host responds with full SNAPSHOT containing all active NetworkObjects. New peer instantiates from prefab registry with correct state.

### Scene Object Auto-Ownership

NetworkObjects that pre-exist in a scene (placed in the editor, not spawned at runtime) are automatically registered and assigned to the host. Call `NetworkManager.Instance.RegisterSceneObjects()` after scene load, or let it happen automatically when host status is computed.

- Scene objects get deterministic NetworkIds based on their hierarchy path (FNV-1a hash)
- Ownerless objects auto-assign to the current host
- Authority is broadcast to all peers so everyone agrees on ownership
- Works seamlessly with late join (scene objects included in SNAPSHOT)

### NetworkTransform (Hybrid)

All-in-one transform sync component (`NetworkTransform.cs`, ~865 lines). Combines spring physics, buffered interpolation, velocity extrapolation, and distance-based LOD in a single component.

```csharp
// Just add NetworkTransform component to any GameObject with NetworkObject.
// Owner writes transform changes automatically; remote peers sync smoothly.
// Auto mode: Spring for Rigidbody objects, Interpolation for kinematic objects.
```

**Sync Methods (`SyncMethod` enum):**
- **Auto** (default) — Spring if Rigidbody present, Interpolation if kinematic
- **Spring** — Damped spring physics (force-based for Rigidbody, closed-form for Transform). Best for physics objects: balls, vehicles, ragdolls
- **Interpolation** — SmoothSync-style buffered lerp. 30-state buffer, renders in the past (`interpolationDelay`), two-stage easing. Best for kinematic objects: characters, platforms, UI elements

**Extrapolation (`ExtrapolationMode` enum):**
- **None** — Freeze at last known position when buffer runs out
- **Limited** (default) — Predict forward using velocity, capped by `extrapolationTimeLimit` (5s) and `extrapolationDistanceLimit` (20m). Applies gravity and drag if Rigidbody present
- **Unlimited** — Predict forward indefinitely

**Distance LOD (3 tiers with hysteresis):**

| Tier | Distance | Behavior | CPU Cost |
|------|----------|----------|----------|
| **Full** | < `fullSyncDistance` (10m) | Spring or Interpolation (configured method) | Highest |
| **Tweened** | 10m - 30m | Simple lerp toward target | Medium |
| **Simple** | > `simpleSyncDistance` (30m) | Snap to target | Lowest |

Hysteresis (`lodDeadZone` = 5m) prevents tier flickering at boundaries. Rigidbody objects auto-switch to kinematic in Tweened/Simple tiers and restore when returning to Full.

**Rest Detection:** Timeout-based — if no new SyncVar data for `restTimeout` seconds (0.5s default), object is assumed at rest and extrapolation stops. Prevents drift on idle objects.

**Teleport API:**
```csharp
GetComponent<NetworkTransform>().Teleport(newPosition, newRotation);
// On owner: sets position + forces SyncVar sync
// On remotes: large jumps auto-snap via snap threshold
// Also clears state buffer and resets spring velocities
```

**Settings (Inspector, 9 header groups):**

| Header | Settings |
|--------|----------|
| **What to Sync** | Position, Rotation, Scale toggles |
| **Sync Method** | Auto / Spring / Interpolation |
| **Interpolation** | Delay (0.1s), position ease speed (0.85), rotation ease speed (0.85) |
| **Extrapolation** | Mode (Limited), time limit (5s), distance limit (20m) |
| **Spring Physics** | Pos freq (8Hz), pos damping (0.9), rot freq (10Hz), rot damping (0.85) |
| **Snap/Teleport** | Pos snap distance (5m), rot snap angle (90deg) |
| **Send Thresholds** | Position threshold (0.001m), rotation threshold (0.1deg) |
| **Distance LOD** | Enable, full distance (10m), simple distance (30m), dead zone (5m) |
| **Rest Detection** | Rest timeout (0.5s) |

**Non-owner sync pipeline:**
```
SyncVar data → State Buffer → Target Calculation → Application Method
                               (interp/extrap)      (spring/lerp/snap)
```

**Credits:** Spring physics from PhysicsNetworkTransform.cs (DrewMileham, Skylar/CometDev). Interpolation architecture inspired by SmoothSync (Jim Burrows).

### SyncList

Synchronized list type. Tracks operations (Add, Set, RemoveAt, Insert, Clear) and sends minimal deltas.

```csharp
SyncList<string> Inventory;

void Awake() {
    Inventory = SyncList(new List<string>());
    Inventory.OnChanged += (op, index, oldItem, newItem) =>
        Debug.Log($"{op}: [{index}] {oldItem} -> {newItem}");
}

void PickUp(string item) {
    if (!IsOwner) return;
    Inventory.Add(item); // Synced to all peers
}
```

### SyncDictionary

Synchronized key-value dictionary type. Tracks operations (Set, Remove, Clear) and sends minimal deltas.

```csharp
SyncDictionary<string, int> Scores;

void Awake() {
    Scores = SyncDictionary<string, int>(new Dictionary<string, int>());
    Scores.OnChanged += (op, key, oldVal, newVal) =>
        Debug.Log($"{op}: {key} = {oldVal} -> {newVal}");
}

void AddScore(string player, int pts) {
    if (!IsOwner) return;
    Scores[player] = pts; // Synced to all peers
}
```

### DestroyWithOwner (Lifetime Flag)

NetworkObjects have a `DestroyWithOwner` flag (default false). When true, the object is despawned instead of transferred to the new host when its owner disconnects. Useful for player avatars and per-player objects.

```csharp
// Player objects should be destroyed when the player leaves
var player = NetworkManager.Instance.Spawn(playerPrefabId, pos, rot);
player.DestroyWithOwner = true;

// Room state objects persist (default behavior)
var roomState = NetworkManager.Instance.Spawn(roomStatePrefabId, Vector3.zero, Quaternion.identity);
// roomState.DestroyWithOwner = false; // default
```

The flag is synced in the spawn/snapshot wire format so all peers agree on the behavior.

### Object Pooling

NetworkManager includes built-in object pooling. `Despawn()` deactivates and returns objects to a per-prefab pool. `Spawn()` checks the pool first before calling `Instantiate`. Enable via `_enablePooling` on the NetworkManager component. Pre-warm pools with `Prewarm(prefabId, count)`.

### RPC Migration Buffer

Host-targeted and owner-targeted RPCs are automatically buffered during host migration. When a peer disconnects and host re-election occurs, any RPCs sent during that window are queued and replayed once the new host is confirmed. No RPCs are dropped during transition.

### Authority Request

Non-host peers can request ownership of objects via `NetworkManager.Instance.RequestAuthority(obj)`. The host auto-approves by default. Set `OnAuthorityRequested` callback on the host to add custom validation (e.g. distance checks, cooldowns). Uses message ID 0xA7.

### Reliable State Fallback (Eventual Consistency)

STATE_UPDATE is sent unreliable for speed. But if a packet drops, a SyncVar change might never arrive. After 200ms, if the object hasn't been re-dirtied, its full state is resent via reliable SNAPSHOT. This guarantees eventual consistency with minimal overhead — continuously-changing state (like movement) is always unreliable, while one-shot changes (like HP) get reliable delivery.

### NetworkObject References in RPCs

NetworkObject is a registered serializer type. When sent as an RPC arg, it serializes as the NetworkId (uint). The receiver automatically resolves it to the local instance via `NetworkManager.Instance.Objects`.

```csharp
NetworkManager.Instance.SendRPC(target, "GotHitBy", RPCTarget.Owner, attackerNetObj, 25f);

// Receiver:
NetworkManager.Instance.RegisterRPC(Net, "GotHitBy", reader => {
    NetworkObject attacker = NetSerializers.Read<NetworkObject>(reader);
    float dmg = NetSerializers.Read<float>(reader);
});
```

### Sequence-Based Stale Rejection (BufferLast)

STATE_UPDATE packets include a per-object sequence number. Receivers only apply updates where `(newSeq - lastSeq) > 0` using wrapping comparison. Out-of-order packets are silently discarded. This prevents stale state from overwriting newer data on unreliable channels.

### NetworkAnimator

Synchronizes Animator parameters across the network. Packs all float/int/bool parameters into a single SyncVar<byte[]> for bandwidth efficiency. Triggers are sent via RPC (event-based, not state).

```csharp
// Just add NetworkAnimator to any GameObject with Animator + NetworkObject.
// Parameters auto-discovered. Owner changes sync to all peers.

// For triggers (events, not state):
GetComponent<NetworkAnimator>().SetNetworkTrigger("Jump");
```

**Wire format:** `[floatCount:byte][floats...][intCount:byte][ints...][boolCount:byte][boolMask bytes]`

**Settings:**
- `Sync Interval` — How often to check for parameter changes (default 0.1s = 10Hz)
- `Animator` — Auto-detected if not assigned

**Change detection:** Only sends when a parameter actually changes (float threshold 0.001, exact match for int/bool). Uses cached previous values.

### EasySync (No-Code Property Sync)

Inspired by Normcore's EasySync. Sync any public field or property on sibling components without writing code. Just add the EasySync component, check boxes in the Inspector, and properties sync automatically.

```csharp
// No code needed! Just:
// 1. Add EasySync component to any GameObject with NetworkObject
// 2. In Inspector, check the properties you want to sync
// 3. Owner writes → remote peers receive automatically
```

**Custom Inspector** (`EasySyncEditor.cs` in `EOSNative.Editor/`):
- Scans all sibling components for public fields and properties
- Filters to supported types (bool, byte, short, ushort, int, uint, long, ulong, float, double, string, Vector2, Vector3, Quaternion, Color, Color32)
- Skips Transform, NetworkObject, NetworkBehaviour subclasses, and base Unity properties
- Foldout per component, toggle per member
- Undo/Redo support

**Runtime:** Reflection-based read/write, packed into a single SyncVar<byte[]>. Bindings resolved once in Awake (FieldInfo/PropertyInfo cached). Change detection before sending. Try/catch on apply for safety.

**v2 Features (v2.21.0):**

**Per-property WriteAccess:**
- `Owner` (default) — only the NetworkObject owner can write
- `Host` — only the host can write (game state managed by host)
- `All` — any peer can write (last-write-wins, use sparingly)
- `CanWrite()` checks all bindings — if ANY allows the local peer to write, the entire state is packed and sent (all bindings share one SyncVar<byte[]>)
- `OnStateReceived()` only applies state for non-writers

**Per-property Interpolation:**
- `Interpolate` (bool) toggle per binding — when enabled, remote peers lerp toward target values instead of snapping
- `InterpolateSpeed` (float, default 15) — higher = faster catch-up. `t = Clamp01(speed * deltaTime)`
- Supported types: float, double, Vector2, Vector3, Quaternion (Slerp), Color, Color32, int, short, byte
- Target-value pattern: `ApplyState()` stores targets, `ApplyInterpolation()` runs each frame for non-writers
- `IsCloseEnough()` with per-type epsilon to stop interpolating when converged

**"Convert to Code" export:**
- Inspector "Convert to Code" button generates a typed `NetworkBehaviour` .cs file
- Groups bindings by WriteAccess level for clean if-blocks in `Update()`
- Generates: SyncVar<T> declarations, component references, `Awake()` with `Sync<T>()` calls and `OnChanged` callbacks, `Update()` with access-gated value writes
- Type resolution: `GetCSharpTypeName()` resolves types across assemblies, `TypeToKeyword()` maps System types to C# keywords
- Save dialog + `AssetDatabase.Refresh()` for immediate compilation

**Settings:**
- `Sync Interval` — How often to check for changes (default 0.1s)
- Per-property `WriteAccess` (Owner/Host/All) — enforced at runtime via `CanWrite()`
- Per-property `Interpolate` toggle + `InterpolateSpeed` — smooth remote updates for numeric/vector types

## Connection Statistics (NetworkStats)

Singleton manager (`Net/NetworkStats.cs`, ~600 lines) that tracks per-peer and global connection quality metrics. Auto-creates under EOSManager like other singletons. Uses an `internal static _instance` null-check pattern so hooks in EOSP2PManager have zero overhead when stats aren't being used.

### Ping/Pong Protocol

Custom RTT measurement since EOS SDK doesn't provide it. Message IDs 0xA8 (PING) and 0xA9 (PONG) on channel 2, unreliable unordered, sent via `SendToPeerImmediate` to bypass batching.

- **PING** (8 bytes): `[sequence:u32][senderTimestamp:float32]`
- **PONG** (8 bytes): `[sequence:u32][originalTimestamp:float32]`
- **RTT** = `(Time.unscaledTime - originalTimestamp) * 1000f` ms, EMA smoothed (alpha=0.2)

### Metrics

| Metric | Method | Frequency |
|--------|--------|-----------|
| **RTT** | Ping/pong protocol, EMA smoothed | Every `_pingInterval` (1s) |
| **Packet Loss** | Rolling window: `1.0 - pongs/pings` | 10s window |
| **Bandwidth** | Delta between byte snapshots | Every `_sampleInterval` (0.5s) |
| **NAT Type** | `GetNATType()` + `QueryNATType()` | Once on startup |
| **Queue Info** | `GetPacketQueueInfo()` | On demand via `GetGlobalStats()` |
| **Connection Type** | From `OnConnectionEstablished` callback | On peer connect |

### EOSP2PManager Hooks (3 lines)

1. `SendToPeer()` — `NetworkStats._instance?.RecordBytesSent(peer, data.Length)`
2. `PollPackets()` — `NetworkStats._instance?.RecordBytesReceived(sender, (int)bytesWritten)`
3. `OnConnectionEstablished()` — `NetworkStats._instance?.RecordConnectionType(peer, networkType, establishedType)`

### Public API

```csharp
// Per-peer
PeerStats GetPeerStats(ProductUserId puid)
float RTT(ProductUserId puid)           // ms, -1 if unknown
float PacketLoss(ProductUserId puid)    // 0.0 - 1.0
float ConnectionAge(ProductUserId puid) // seconds
IReadOnlyDictionary<ProductUserId, PeerStats> AllPeerStats

// Global
GlobalStats GetGlobalStats()  // includes queue info
NATType LocalNATType
float AverageRTT
float TotalBandwidthOutKBps / InKBps
void ResetStats()
event Action OnStatsUpdated   // fires every 0.5s
```

## NetworkRoomState (Shared Room Data)

Singleton NetworkObject representing the shared room/game state. Well-known ID `0xFFFF0001`, reserved PrefabId `0xFFF0`. Host auto-creates after first peer connects. DestroyWithOwner = false (survives host migration).

**File:** `Net/NetworkRoomState.cs` (~200 lines)

**SyncVars (index 0-7):** GameMode (string), MapName (string), RoundNumber (int), PlayerCount (int), MaxPlayers (int), RoundTimer (float), Phase (byte: 0=Lobby/1=Loading/2=Playing/3=PostMatch), IsInProgress (bool)

**SyncDictionary (index 8):** `Properties` — dynamic string-string for custom room data

**Lobby attribute mirroring:** Rate-limited (1/sec). GameMode, MapName, IsInProgress auto-push to lobby attributes. Add keys to `SearchablePropertyKeys` for custom mirroring.

**Scene properties:** `_scene`, `_addScene_N`, `_addSceneCount` stored in Properties dict. Read by NetworkSceneManager for late-join scene sync.

```csharp
var room = NetworkManager.Instance.RoomState;
room.GameMode.Value = "deathmatch";          // host writes
room.SetProperty("score_limit", "100");       // host writes custom
int limit = room.GetPropertyInt("score_limit", 50); // typed getter
room.CurrentPhase = GamePhase.Playing;        // typed enum accessor
```

## NetworkPlayerState (Per-User Data)

Per-player NetworkObject. Reserved PrefabId `0xFFF1`. Each peer auto-creates their own on connect. DestroyWithOwner = true (destroyed on disconnect). Standard NetworkId generation (PUID partition).

**File:** `Net/NetworkPlayerState.cs` (~170 lines)

**SyncVars (index 0-7):** DisplayName (string), Team (byte), IsReady (bool), Score (int), Deaths (int), Assists (int), Loadout (string), PlayerSlot (byte)

**SyncDictionary (index 8):** `CustomData` — dynamic string-string per player

**Auto-init:** DisplayName populated from EOSPlayerRegistry on spawn.

```csharp
var me = NetworkManager.Instance.LocalPlayerState;
me.Team.Value = 1;
me.IsReady.Value = true;
me.SetCustom("skin", "gold_armor");

var them = NetworkManager.Instance.GetPlayerState(puid);
string name = them.DisplayName.Value;

foreach (var kvp in NetworkManager.Instance.PlayerStates) { ... }
```

## NetworkSceneManager

Singleton manager for networked scene loading. Host calls LoadScene/LoadSceneAdditive/UnloadScene, all peers follow. Scene info stored on NetworkRoomState properties so late joiners load the correct scenes.

**File:** `Net/NetworkSceneManager.cs` (~300 lines)

**Max 8 additive scenes** (matching Fusion).

**Load flow:**
1. Host calls `LoadScene("Arena_01")`
2. Updates RoomState scene properties
3. Broadcasts MSG_SCENE_LOAD (reliable) to all peers
4. All peers load async via `SceneManager.LoadSceneAsync`
5. After load: `RegisterSceneObjects()` auto-assigns scene NetworkObjects
6. Non-host peers send MSG_SCENE_LOADED_ACK to host
7. Host fires `OnAllPeersLoaded` when all ACKs received

**Late join:** After receiving SNAPSHOT, new peer reads scene info from RoomState and loads the correct scenes.

```csharp
NetworkSceneManager.Instance.LoadScene("Arena_01");
NetworkSceneManager.Instance.LoadSceneAdditive("Props_01");
NetworkSceneManager.Instance.UnloadScene("Props_01");

NetworkSceneManager.Instance.OnSceneLoadCompleted += name => { ... };
NetworkSceneManager.Instance.OnAllPeersLoaded += () => StartRound();
```

## Chunked Snapshot Delivery

Snapshots are sent in priority-ordered chunks of 16 objects per message:

1. **Priority 1:** NetworkRoomState (so late joiners know game state immediately)
2. **Priority 2:** All NetworkPlayerStates (so late joiners know about all players)
3. **Priority 3:** Remaining objects

Each chunk is a separate MSG_SNAPSHOT message, all reliable ordered. HandleSnapshot already handles multiple SNAPSHOT messages via duplicate guard (`_objects.ContainsKey`). After receiving RoomState, late joiners auto-create their PlayerState and sync scenes.

## Typed RPCs ([NetRpc] Attribute + IL Post-Processor)

Mark methods on `NetworkBehaviour` or `NetworkObject` subclasses with `[NetRpc]` for zero-boilerplate typed RPCs. The IL post-processor (Mono.Cecil) rewrites method bodies at compile time — same technique as Mirror, FishNet, and Fusion.

**NetworkObject subclass RPCs (v2.29.0):** The weaver now also processes `[NetRpc]` on direct `NetworkObject` subclasses, not just `NetworkBehaviour`. This allows combining identity, SyncVars, and RPCs on a single component. The weaver emits `this` (instead of `this.Net`) for the NetworkObject reference. Both patterns coexist — existing `NetworkBehaviour` RPCs work unchanged.

```csharp
// New: RPCs directly on NetworkObject subclass (no separate NetworkBehaviour needed)
public class MyNetworkedThing : NetworkObject
{
    [NetRpc(RPCTarget.All)]
    public void Explode(float radius) { /* ... */ }
}

// Existing: RPCs on NetworkBehaviour (still works, unchanged)
public class MyBehaviour : NetworkBehaviour
{
    [NetRpc(RPCTarget.All)]
    public void Jump() { /* ... */ }
}
```

```csharp
[NetRpc(RPCTarget.All)]
public void TakeDamage(float damage)
{
    Health.Value -= damage;
}

// Calling is transparent — all peers execute TakeDamage:
player.TakeDamage(19f);
```

**What the weaver generates (per [NetRpc] method):**
1. `UserCode_TakeDamage(float)` — original method body, moved here
2. `TakeDamage(float)` — dispatch stub: serialize args via `NetSerializers.Write<T>()`, call `SendRPCWeaved()`
3. `__InvokeNetRpc_TakeDamage(NetReader)` — deserializer: `NetSerializers.Read<T>()` each param, call `UserCode_`
4. `__RegisterNetRPCs()` override — registers invoke handlers after NetworkId is assigned

**NetworkBehaviour lifecycle hooks (called by NetworkObject.NotifyNetworkSpawn/Despawn):**
- `OnNetworkSpawn()` — called after NetworkId assigned, RPCs registered. Override for post-spawn init.
- `OnNetworkDespawn()` — called before object deactivated/pooled. Override for cleanup.
- `__RegisterNetRPCs()` — weaver-generated, registers all `[NetRpc]` handlers. Do not call manually.

**NetworkManager new overloads (weaver calls these):**
- `RegisterRPC(target, uint hash, string name, handler)` — hash-based registration (compile-time hash)
- `SendRPCWeaved(target, uint hash, RPCTarget, byte[] argData)` — pre-serialized dispatch
- `SendRPCWeavedToPeer(target, uint hash, ProductUserId, byte[] argData)` — peer-targeted pre-serialized dispatch

**Supported parameter types:** byte, bool, short, ushort, int, uint, long, ulong, float, double, string, Vector2, Vector3, Quaternion, Color, Color32, ProductUserId, byte[], NetworkObject, INetSerializable

**Constraints:** void return only, no ref/out, no generics, no abstract. Violations produce compiler errors from the weaver.

**Backward compatible:** Existing string-based `RegisterRPC`/`SendRPC` API unchanged and fully functional.

**CodeGen assembly:** `EOSNative.CodeGen/` — Editor-only, `noEngineReferences: true`, references Mono.Cecil precompiled DLLs. Depends on `com.unity.nuget.mono-cecil` (1.11.6) in manifest.json.

**Files:**

| File | Description |
|------|-------------|
| `Net/NetRpcAttribute.cs` | `[NetRpc(RPCTarget)]` attribute definition |
| `EOSNative.CodeGen/EOSNative.CodeGen.asmdef` | Assembly def for the IL post-processor |
| `EOSNative.CodeGen/EOSNetRpcPostProcessor.cs` | `ILPostProcessor` entry point |
| `EOSNative.CodeGen/PostProcessorAssemblyResolver.cs` | Custom Cecil assembly resolver |
| `EOSNative.CodeGen/WeaverTypes.cs` | Resolves all Cecil type/method references |
| `EOSNative.CodeGen/RpcWeaver.cs` | Core weaving logic (~400 lines) |

## DemoBallBehaviour (Layer 2 Demo)

NetworkBehaviour component added to each ball in the P2P demo. Demonstrates SyncVars and `[NetRpc]` typed RPCs on runtime-created objects registered via `RegisterExisting()` (no prefabs needed).

**File:** `Demo/DemoBallBehaviour.cs` (~170 lines)

**SyncVars:** Score (int), DisplayName (string), BallColor (Color)

**[NetRpc] Methods:**
- `ApplyImpulse(float dirX, float dirY, float dirZ, float force)` — applies physics impulse (RPCTarget.All)
- `ChangeColor(float r, float g, float b)` — changes ball color, updates SyncVar + renderer (RPCTarget.All)
- `ChatBubble(string message)` — shows floating text above ball for 3s (RPCTarget.All)
- `PlayEffect(byte effectId)` — shows brief visual indicator (RPCTarget.All)
- `RequestScorePoint(int amount)` — asks owner to add score (RPCTarget.Owner)

**Demo controls (in P2PDemoManager):**
- **E** — cycle through color presets
- **Q** — shockwave impulse (pushes self up + all nearby balls outward)
- **T** — chat bubble ("Hello!")
- **R** — random visual effect

**Registration pattern:** Each ball gets `NetworkObject` + `DemoBallBehaviour` added at creation time. A deterministic NetworkId is generated as `0xBB000000 | (FnvHash(puid) & 0x00FFFFFF)`, then registered via `NetworkManager.Instance.RegisterExisting()`.

**`RegisterExisting()` fix (v2.14.0):** Now calls `NotifyNetworkSpawn()` after registration, so `__RegisterNetRPCs()` and `OnNetworkSpawn()` fire correctly on objects created outside of `Spawn()`.

## Interest Management (v2.20.0)

Spatial interest management — each peer only receives state for nearby objects. Mirror-style 2D grid with FishNet-style hysteresis.

**Files:**
- `SpatialHashGrid.cs` — 2D uniform spatial hash grid. Cell size = `visRange / 2`. 9-neighbor lookup for visibility.
- `InterestManager.cs` — MonoBehaviour singleton. Per-peer interest sets with enter/leave events.
- `NetworkObject.AlwaysVisible` — opt-out flag for globally visible objects.

**Opt-in:** `NetworkManager.InterestManagementEnabled = true`

**Filtered broadcast paths:**
- `SendStateUpdates()` — per-peer filtered packets (hot path; pre-serializes dirty data once, then builds per-peer packets)
- `BroadcastSpawn()`, `Despawn()` — `SendToInterestedPeers()` helper
- `CheckReliableFallback()` — filtered reliable snapshots
- RPC broadcasts (All/Others/Players) — filtered through `SendToInterestedPeers()` / `SendToInterestedNonSpectators()`
- `HandleSnapshotRequest()` — late-join snapshots filtered for the joining peer
- `SendRPCValidated()` / `HandleRPCValidated()` — host rebroadcast filtered

**NOT filtered (always broadcast to all):**
- `TransferAuthority()` — new owner must know even if object isn't visible yet
- `ClaimOrphanedObjects()` — host migration authority broadcast
- Host/Owner-targeted RPCs — these go to a specific peer, not broadcast

**Interest enter/exit:**
- `OnInterestEnter(peer, networkId)` → sends SPAWN to that peer (owner sends only)
- `OnInterestExit(peer, networkId)` → sends DESPAWN to that peer (owner sends only)

**Always-visible objects:** RoomState, PlayerState, owner's own objects, `AlwaysVisible = true`

**Configuration:** `VisRange` (100), `Hysteresis` (0.1 = 10%), `RebuildInterval` (0.5s), `GridAxes` (XZ/XY)

**Performance:** SpatialHashGrid uses `Dictionary<long, HashSet<uint>>` for O(1) cell lookup. `PackCell()` packs 2D grid coords into a long key. Per-peer interest rebuild runs every 0.5s (configurable). State update hot path: one pre-serialize pass, then per-peer packet assembly.

## Packet Compression

Opt-in Deflate compression for message payloads. Transparent to application code — enable and forget.

**Properties on `MessageRouter`:**
- `CompressionEnabled` (bool, default false) — enables/disables compression
- `CompressionThreshold` (int, default 64) — minimum payload bytes before compression is attempted

**Convenience on `NetworkManager`:**
- `CompressionEnabled` — proxies to `Router.CompressionEnabled`

**Wire format (backward compatible):**
- `0x00` = FLAG_SINGLE (uncompressed, unchanged)
- `0x01` = FLAG_BATCH (uncompressed, unchanged)
- `0x02` = FLAG_SINGLE_COMPRESSED — `[flag][deflate(msgId + payload)]`
- `0x03` = FLAG_BATCH_COMPRESSED — `[flag][deflate(count + messages)]`

Old peers that don't understand `0x02`/`0x03` silently ignore them (no handler found). Compression only applied when the compressed output is smaller than the original — otherwise falls back to uncompressed.

```csharp
// Enable compression (opt-in)
NetworkManager.Instance.CompressionEnabled = true;
// Or directly on the router:
EOSP2PManager.Instance.Router.CompressionEnabled = true;
EOSP2PManager.Instance.Router.CompressionThreshold = 128; // custom threshold
```

**Static helpers (internal, used by tests):**
- `MessageRouter.CompressDeflate(byte[], offset, count)` → `byte[]`
- `MessageRouter.DecompressDeflate(byte[], offset, count)` → `byte[]`

## Spectator Mode

A peer can join as a read-only observer. Spectators receive all state updates but cannot spawn objects or become host.

**Properties on `NetworkManager`:**
- `IsSpectator` (bool) — set to true before joining a lobby to join as spectator
- `IsPeerSpectator(ProductUserId)` — check if a specific peer is spectating

**Convenience on `NetworkBehaviour`:**
- `IsSpectator` — proxies to `NetworkManager.Instance.IsSpectator`

**Convenience on `NetworkPlayerState`:**
- `IsSpectating` — reads `_spectator` custom data key

**How it works:**
1. Set `NetworkManager.Instance.IsSpectator = true` before joining a lobby
2. On connect, the local PlayerState is created with `CustomData["_spectator"] = "1"`
3. All peers read this key and populate an internal `_spectators` HashSet
4. Host election skips spectator PUIDs (spectators never become host)
5. `Spawn()` returns null with a warning if `IsSpectator` is true
6. `RPCTarget.Players` sends only to non-spectator peers

**RPCTarget.Players (new enum value = 4):**
- `executeLocal = !IsSpectator` — spectators don't execute locally
- Remote send goes only to non-spectator peers via `SendToNonSpectators()`

**Edge case:** If ALL peers are spectators, the lowest PUID becomes host anyway (with a warning log).

```csharp
// Join as spectator
NetworkManager.Instance.IsSpectator = true;
// Then join lobby normally...

// Send RPC only to players (not spectators)
[NetRpc(RPCTarget.Players)]
public void StartRound() { ... }

// Check from any NetworkBehaviour
if (IsSpectator) return; // skip gameplay logic
```

## Master Client Verification (RPC Validation)

Opt-in callback on NetworkManager that fires before executing any incoming remote RPC. The host (or any peer) can reject unauthorized RPCs. Null = all RPCs allowed (default behavior, zero overhead).

**Field on `NetworkManager`:**
```csharp
public Func<ProductUserId, NetworkObject, uint, bool> OnRPCValidation;
// Parameters: (sender, targetObject, methodHash) → allow?
```

**Helper:**
```csharp
// Convenience: only allow RPCs from the object's owner
NetworkManager.Instance.EnableOwnerOnlyRPCValidation();
// Equivalent to:
NetworkManager.Instance.OnRPCValidation = (sender, target, hash) =>
    target != null && target.OwnerId == sender;
```

**Behavior:** When set, `HandleRPC()` checks the callback after reading networkId + methodHash. If it returns false, logs a warning and skips the handler. The reader position is safe because each message in `DispatchMessage` gets bounded offset/count.

```csharp
// Custom validation: only allow specific RPCs from non-owners
NetworkManager.Instance.OnRPCValidation = (sender, target, hash) =>
{
    if (target == null) return false;
    if (target.OwnerId == sender) return true; // owner can always RPC
    // Allow specific cross-owner RPCs (e.g. damage)
    return hash == NetworkManager.FnvHash("TakeDamage");
};
```

## Host-Validated RPCs

Mark RPCs with `Validated = true` to route them through the host for validation before broadcast. The host can run game-specific checks (range, cooldowns, economy) and reject unauthorized RPCs.

```csharp
// On your NetworkBehaviour:
[NetRpc(RPCTarget.All, Validated = true)]
public void DealDamage(float amount) { Health.Value -= amount; }

// Optional validator (auto-discovered by naming convention):
// Runs ONLY on host. Return true = approve, false = reject.
bool Validate_DealDamage(ProductUserId sender, NetworkObject target, byte[] argData)
{
    if (amount > 100) return false;  // cap damage
    // Deserialize args if needed: var reader = new NetReader(argData, 0, argData.Length);
    return true;
}
```

**Flow:** Client calls `DealDamage(50)` → weaver sends to host only (MSG_RPC_VALIDATED 0xAD) → host runs `Validate_DealDamage` if present (auto-approves if not) → host rebroadcasts (MSG_RPC_REBROADCAST 0xAE) → all peers execute.

**Wire format (both 0xAD and 0xAE):** `[networkId:u32][methodHash:u32][originalTarget:u8][argData...]`

**Key points:**
- No `nameof` needed — weaver auto-discovers `Validate_X` by naming convention
- Without a validator method, host auto-approves (relay-only mode — still prevents direct peer spoofing)
- Adds ~20-40ms latency (one extra hop through host)
- Rebroadcast only accepted from host (sender == GetHostPuid())
- If host IS the caller, validation + rebroadcast happens locally

## Network Rules — SyncVar Write Access (v2.25.0)

Per-variable write access control for SyncVar, SyncList, and SyncDictionary. Replaces the hardcoded owner-only write guard with a configurable `SyncVarWriteAccess` enum.

**Enum:** `SyncVarWriteAccess` in `SyncVar.cs`
- `Owner` (0, default) — only the NetworkObject owner can write
- `Host` (1) — only the host peer can write
- `All` (2) — any peer can write (last-write-wins)

**Registration API:**
```csharp
Health = Sync(100f);                                    // Owner (default)
GamePhase = Sync(0, SyncVarWriteAccess.Host);           // Host-only
SharedState = Sync("", SyncVarWriteAccess.All);         // Anyone
Inventory = SyncList<string>(writeAccess: SyncVarWriteAccess.Host);
Scores = SyncDictionary<string, int>(writeAccess: SyncVarWriteAccess.All);
```

**Write-time guard:** Each `SyncVar<T>.Value` setter calls `CanWrite()` which checks the local peer against the WriteAccess level. Non-writers' assignments are silently ignored (same as before for owner-only).

**Receiver-side validation:** `HandleStateUpdate` calls `ValidateSyncVarSender()`:
1. If `OnSyncVarWrite` callback is set → use it (custom rules)
2. Otherwise check `NetworkObject.MaxWriteAccess` (most permissive SyncVar on the object):
   - `All` → accept from anyone
   - `Host` → accept from owner OR host
   - `Owner` → accept from owner only

**`OnSyncVarWrite` callback** on NetworkManager — mirrors `OnRPCValidation` pattern:
```csharp
public Func<ProductUserId, NetworkObject, bool> OnSyncVarWrite;
```

**Convenience helpers:**
- `EnableOwnerOnlySyncVarValidation()` — forces all writes to owner-only (ignores WriteAccess)
- `EnableOwnerOrHostSyncVarValidation()` — allows owner OR host to write any object

**`NetworkObject.MaxWriteAccess`** — returns most permissive `SyncVarWriteAccess` among all SyncVars. Early-outs on `All`. Used for efficient O(1) receiver-side checks (avoids per-SyncVar iteration on every packet).

## Tick-Based Simulation (v2.22.0)

Fixed-rate simulation decoupled from rendering frame rate. When enabled, network state updates fire at a consistent tick rate instead of every frame.

**File:** `Net/TickSimulation.cs` — MonoBehaviour singleton, auto-creates under EOSManager.

**How it works:**
- Accumulator-driven: `_tickAccumulator += Time.deltaTime`, fires ticks when `>= FixedTickTime`
- Multiple ticks per frame allowed (handles lag spikes), capped at 10 to prevent spiral of death
- `OnTick(uint tickIndex, float fixedDeltaTime)` — for deterministic game logic
- `OnPostTick()` — used by NetworkManager to send state updates after game logic

**NetworkManager integration:**
- `OnEnable` subscribes `OnSimulationPostTick` to `TickSimulation.OnPostTick`
- `LateUpdate` checks `_tickSubscribed`: if true, skips state updates (they run on tick); if false, runs frame-based
- `SendStateUpdates()` + `CheckReliableFallback()` move to tick boundary when active
- Rate limit reset stays frame-based (doesn't need tick precision)
- Packet polling and message flushing stay frame-based (EOSP2PManager.Update/LateUpdate)

**SyncVarLOD integration:**
- Observer position + tier calculations run once per tick instead of every frame
- `_lastTickUpdated` tracks last processed tick to skip redundant updates
- Dirty counter still counts MarkDirty calls, but MarkDirty only fires on tick boundaries now

**Public API:**

| Property | Type | Description |
|----------|------|-------------|
| `TickRate` | int | Ticks/sec (0 = disabled, 30 = default) |
| `FixedTickTime` | float | 1/TickRate seconds |
| `CurrentTick` | uint | Monotonic tick counter |
| `SimulationTime` | float | CurrentTick * FixedTickTime |
| `Alpha` | float | 0..1 interpolation fraction for rendering between ticks |
| `IsEnabled` | bool | True if TickRate > 0 |

**NetworkBehaviour convenience:** `CurrentTick`, `FixedTickTime` protected properties.

**Configuration:** Set `TickSimulation.Instance.TickRate` before or during play. Common values: 20 (casual), 30 (standard), 60 (competitive). 0 = disabled (frame-based, backward compatible).

## Automated Tests

Editor-mode unit tests for core networking primitives. Uses Unity Test Framework (`com.unity.test-framework`).

**Location:** `Tests/Editor/` with `EOSNative.Tests.Editor.asmdef`

**Test assembly:** References `EOSNative` and `Epic.OnlineServices` assemblies. Editor-only, `UNITY_INCLUDE_TESTS` define constraint.

| Test File | Tests | Coverage |
|-----------|-------|----------|
| `NetWriterReaderTests.cs` | ~25 | All primitives, packed varints, strings, Unity types, byte arrays, pooling, auto-grow, bounds checking |
| `FnvHashTests.cs` | ~8 | Known values, empty/null, consistency, case sensitivity, collision resistance |
| `NetSerializersTests.cs` | ~20 | All 18 built-in types round-trip, INetSerializable, boxed read/write, type IDs |
| `SyncVarTests.cs` | ~20 | Dirty tracking, OnChanged, owner-write guard, SetInternal bypass, serialize round-trip, multiple SyncVars, dirty mask |
| `SyncListTests.cs` | ~18 | Add/Set/RemoveAt/Insert/Clear, delta serialization, full state, OnChanged, enumerate |
| `SyncDictionaryTests.cs` | ~16 | Set/Remove/Clear, delta serialization, full state, OnChanged, TryGetValue, enumerate |
| `PacketFragmenterTests.cs` | ~12 | Single-fragment fast path, multi-fragment round-trip, out-of-order, stale cleanup, max payload, duplicate ignore |
| `NetworkIdTests.cs` | ~8 | Partition generation, scene object IDs, demo ball IDs, well-known IDs, reserved PrefabIds |
| `CompressionTests.cs` | ~12 | Vector3Half accuracy, compressed rotation (smallest-three) round-trip, edge cases, many angles |
| `PacketCompressionTests.cs` | ~10 | Deflate compress/decompress round-trip, various sizes, offsets, empty data, random data, threshold edge cases |

**Run:** Window > General > Test Runner > EditMode > Run All
