# Networking

High-level networking built on the [P2P Transport Toolkit](p2p-transport.md). Provides object identity, automatic state sync, spawn/despawn, RPCs, host migration, late-join snapshots, scene management, and more. No external dependencies -- everything runs over the EOS P2P mesh.

All files live in `Runtime/EOSNative/Net/`.

## Quick Start

Create a `NetworkBehaviour`, define SyncVars, spawn it, and send RPCs:

```csharp
using EOSNative.Net;
using UnityEngine;

public class Player : NetworkBehaviour
{
    SyncVar<Vector3> Position;
    SyncVar<float> Health;

    protected override void Awake()
    {
        base.Awake();
        Position = Sync(Vector3.zero);
        Health = Sync(100f);
        Health.OnChanged += (old, hp) => Debug.Log($"Health: {old} -> {hp}");
    }

    void Update()
    {
        if (!IsOwner) return;
        Position.Value = transform.position;
    }
}
```

Register a prefab and spawn:

```csharp
// Register (once, e.g. in a setup script)
NetworkManager.Instance.RegisterPrefab(playerPrefab, prefabId: 0);

// Spawn (local peer becomes owner)
var player = NetworkManager.Instance.Spawn(prefabId: 0, pos, rot);

// Despawn
NetworkManager.Instance.Despawn(player);
```

Send an RPC:

```csharp
NetworkManager.Instance.SendRPC(player, "TakeDamage", RPCTarget.Owner, 25f);
```

## NetworkObject

The core networking component. Attach to any GameObject that needs to sync across the P2P mesh.

### Identity

Every NetworkObject has a unique `NetworkId` (uint). The upper 16 bits are derived from the FNV-1a hash of the owner's ProductUserId, and the lower 16 bits are an incrementing counter. This partitioning means each peer generates IDs from their own range -- no central authority needed, no collisions between peers.

```csharp
// Example: PUID "abc123" hashes to 0x4F2A in upper 16 bits
// First object spawned by that peer: NetworkId = 0x4F2A0000
// Second object:                     NetworkId = 0x4F2A0001
```

### Ownership

```csharp
netObj.OwnerId    // ProductUserId of the owning peer
netObj.IsOwner    // True if the local peer owns this object
netObj.IsHost     // True if the local peer is the current host
```

Only the owner can write SyncVars. Remote peers' writes are silently ignored.

### DestroyWithOwner

Controls what happens when the owner disconnects:

```csharp
// Player avatars -- destroy when the player leaves
player.DestroyWithOwner = true;

// Room state objects -- persist and transfer to new host
roomState.DestroyWithOwner = false;  // default
```

The flag is synced in the spawn/snapshot wire format so all peers agree on the behavior.

### Events

```csharp
netObj.OnOwnerChanged += (oldOwner, newOwner) => { };
netObj.OnNetworkSpawn += () => { };
netObj.OnNetworkDespawn += () => { };
```

### SyncVar Limit

Each NetworkObject supports up to 32 SyncVars (including SyncLists and SyncDictionaries). They share one index space -- the order of `Sync()` / `SyncList()` / `SyncDictionary()` calls in Awake determines their index. The dirty mask is adaptive: 1 byte for up to 8 vars, 2 bytes for up to 16, 4 bytes for up to 32.

## NetworkBehaviour

Optional convenience base class. You can also skip it and use `GetComponent<NetworkObject>()` directly.

### Properties

| Property | Type | Description |
|----------|------|-------------|
| `Net` | NetworkObject | The NetworkObject on this GameObject (auto-added if missing) |
| `NetworkId` | uint | Shortcut to `Net.NetworkId` |
| `IsOwner` | bool | True if the local peer owns this object |
| `IsHost` | bool | True if the local peer is the current host |
| `OwnerId` | ProductUserId | The owning peer's ID |
| `IsSpectator` | bool | True if the local peer is a spectator |
| `Manager` | NetworkManager | Shortcut to `NetworkManager.Instance` |

### Lifecycle Hooks

```csharp
public class MyComponent : NetworkBehaviour
{
    protected override void Awake()
    {
        base.Awake();  // MUST call base.Awake() first
        // Create SyncVars here
    }

    public override void OnNetworkSpawn()
    {
        // Called after NetworkId is assigned and RPCs are registered.
        // Safe to subscribe to events that need NetworkId.
    }

    public override void OnNetworkDespawn()
    {
        // Called before the object is deactivated/pooled.
        // Clean up subscriptions here.
    }
}
```

### Creating SyncVars

Use the `Sync()`, `SyncList()`, and `SyncDictionary()` shortcuts:

```csharp
protected override void Awake()
{
    base.Awake();
    Health = Sync(100f);                                    // SyncVar<float>
    Inventory = SyncList(new List<string>());               // SyncList<string>
    Scores = SyncDictionary<string, int>();                 // SyncDictionary<string, int>
}
```

## SyncVar&lt;T&gt;

Generic synchronized variable with dirty tracking, owner-write guard, and change callbacks.

### Basic Usage

```csharp
SyncVar<float> Health;
SyncVar<string> PlayerName;
SyncVar<Vector3> Position;

protected override void Awake()
{
    base.Awake();
    Health = Sync(100f);
    PlayerName = Sync("Unknown");
    Position = Sync(Vector3.zero);
}

void Update()
{
    if (!IsOwner) return;
    Position.Value = transform.position;  // Only owner can write
}
```

### OnChanged Callback

Fires on ALL peers when the value changes -- immediately on the owner, and when the update arrives on remote peers:

```csharp
Health.OnChanged += (oldValue, newValue) =>
{
    Debug.Log($"Health changed: {oldValue} -> {newValue}");
    if (newValue <= 0) Die();
};
```

### Owner-Write Guard

Only the owning peer can set `Value`. Writes from non-owners are silently ignored. Setting to the same value is a no-op (no dirty flag, no network traffic).

### SetInternal

Force-sets the value without owner checks. Used internally for snapshot and authority transfer:

```csharp
// Only available internally -- for host migration / snapshot apply
syncVar.SetInternal(newValue);
```

### Dirty Mask

Each frame, only dirty SyncVars are serialized. The dirty mask is packed efficiently:

- 1-8 SyncVars: 1-byte mask
- 9-16 SyncVars: 2-byte mask
- 17-32 SyncVars: 4-byte mask

### Supported Types

All types registered in `NetSerializers`: byte, bool, short, ushort, int, uint, long, ulong, float, double, string, Vector2, Vector3, Quaternion, Color, Color32, ProductUserId, byte[], NetworkObject, and any `INetSerializable`.

## SyncList&lt;T&gt;

Synchronized list with operation-based delta sync. Only the changed operations are sent -- not the entire list.

### Operations

| Operation | What it sends |
|-----------|---------------|
| Add | index + new item |
| Set | index + new item |
| Insert | index + new item |
| RemoveAt | index only |
| Clear | nothing |

### Usage

```csharp
SyncList<string> Inventory;

protected override void Awake()
{
    base.Awake();
    Inventory = SyncList(new List<string>());

    Inventory.OnChanged += (op, index, oldItem, newItem) =>
        Debug.Log($"{op}: [{index}] {oldItem} -> {newItem}");
}

void PickUp(string item)
{
    if (!IsOwner) return;
    Inventory.Add(item);        // Synced to all peers
}

void DropSlot(int slot)
{
    if (!IsOwner) return;
    Inventory.RemoveAt(slot);   // Synced to all peers
}
```

### Read-Only Access

Remote peers can read but not write:

```csharp
int count = Inventory.Count;
string first = Inventory[0];
bool has = Inventory.Contains("Sword");

foreach (var item in Inventory)
    Debug.Log(item);
```

### Full State vs Delta

- **Delta** (dirty sync): Sends only pending operations since the last sync
- **Full state** (spawn/snapshot): Sends `[count][item0][item1]...` for late-join correctness

## SyncDictionary&lt;TKey, TValue&gt;

Synchronized key-value dictionary with operation-based delta sync.

### Operations

| Operation | What it sends |
|-----------|---------------|
| Set | key + value |
| Remove | key only |
| Clear | nothing |

### Usage

```csharp
SyncDictionary<string, int> Scores;

protected override void Awake()
{
    base.Awake();
    Scores = SyncDictionary<string, int>();

    Scores.OnChanged += (op, key, oldVal, newVal) =>
        Debug.Log($"{op}: {key} = {oldVal} -> {newVal}");
}

void AddScore(string player, int pts)
{
    if (!IsOwner) return;
    Scores[player] = pts;  // Synced to all peers
}

void RemovePlayer(string player)
{
    if (!IsOwner) return;
    Scores.Remove(player);
}
```

### Read-Only Access

```csharp
int count = Scores.Count;
bool exists = Scores.ContainsKey("player1");
if (Scores.TryGetValue("player1", out int score))
    Debug.Log($"Score: {score}");

foreach (var kvp in Scores)
    Debug.Log($"{kvp.Key}: {kvp.Value}");
```

## NetworkManager

Singleton managing all NetworkObjects. Auto-creates under the EOSManager hierarchy.

### Key Properties

```csharp
var mgr = NetworkManager.Instance;

mgr.IsHost               // Am I the host? (lowest PUID)
mgr.IsOnline             // Connected to at least one peer?
mgr.IsSpectator           // Am I a spectator?
mgr.Objects               // All active NetworkObjects (IReadOnlyDictionary<uint, NetworkObject>)
mgr.ConnectedPlayers      // All connected player PUIDs (IReadOnlyList<ProductUserId>)
mgr.RoomState             // The shared NetworkRoomState (null until host creates it)
mgr.LocalPlayerState      // My NetworkPlayerState (null until created on connect)
mgr.PlayerStates          // All player states (IReadOnlyDictionary<ProductUserId, NetworkPlayerState>)
```

### Spawning and Despawning

```csharp
// Register prefabs (Inspector or runtime)
mgr.RegisterPrefab(playerPrefab, 0);
mgr.RegisterPrefab(bulletPrefab, 1);

// Spawn -- local peer becomes owner, broadcasts to all peers
NetworkObject player = mgr.Spawn(prefabId: 0, position, rotation);

// Despawn -- deactivates locally, broadcasts to all peers
// Only owner or host can despawn
mgr.Despawn(player);

// Register an object created outside of Spawn()
mgr.RegisterExisting(existingNetObj, customNetworkId);

// Register all NetworkObjects already in the scene
// (called automatically on host change, or manually after scene load)
mgr.RegisterSceneObjects();
```

### Host Election

The host is deterministically elected as the peer with the lexicographically lowest PUID string. No communication is needed -- every peer computes the same result. Spectators are excluded from host election.

```csharp
if (NetworkManager.Instance.IsHost)
    Debug.Log("I am the host");
```

Recomputed automatically on peer connect/disconnect.

### Helper Methods

```csharp
// Get a specific player's state
NetworkPlayerState ps = mgr.GetPlayerState(puid);

// Get a player's avatar/character object (first non-state object owned by them)
NetworkObject avatar = mgr.GetPlayerObject(puid);

// Check if a peer is a spectator
bool spec = mgr.IsPeerSpectator(puid);
```

## RPCs (Remote Procedure Calls)

Two approaches: string-based (dynamic) and attribute-based (IL-weaved).

### String-Based RPCs

Register a handler, then send:

```csharp
// Register (in Awake or OnNetworkSpawn)
NetworkManager.Instance.RegisterRPC(Net, "TakeDamage", reader =>
{
    float damage = NetSerializers.Read<float>(reader);
    Health.Value -= damage;
});

// Send to all peers
NetworkManager.Instance.SendRPC(player, "TakeDamage", RPCTarget.All, 25f);

// Send to specific peer
NetworkManager.Instance.SendRPC(player, "TakeDamage", specificPuid, 25f);

// Send to multiple specific peers
NetworkManager.Instance.SendRPC(player, "TakeDamage", puidList, 25f);
```

### RPCTarget Enum

| Target | Behavior |
|--------|----------|
| `All` | All peers including self |
| `Others` | All peers excluding self |
| `Host` | Current host only |
| `Owner` | Object's owner only |
| `Players` | All non-spectator peers (including self if not spectator) |

### [NetRpc] Typed RPCs

Mark methods with `[NetRpc]` for zero-boilerplate RPCs. An IL post-processor (Mono.Cecil) rewrites the method bodies at compile time -- same technique as Mirror, FishNet, and Fusion.

```csharp
public class Player : NetworkBehaviour
{
    SyncVar<float> Health;

    [NetRpc(RPCTarget.All)]
    public void TakeDamage(float damage)
    {
        Health.Value -= damage;
    }

    [NetRpc(RPCTarget.Owner)]
    public void RequestScorePoint(int amount)
    {
        Score.Value += amount;
    }

    void OnCollision()
    {
        // Calling is transparent -- serialization + dispatch is automatic
        TakeDamage(19f);
    }
}
```

**What the weaver generates per method:**

1. `UserCode_TakeDamage(float)` -- your original method body, moved here
2. `TakeDamage(float)` -- dispatch stub: serializes args, calls `SendRPCWeaved()`
3. `__InvokeNetRpc_TakeDamage(NetReader)` -- deserializer: reads args, calls `UserCode_`
4. `__RegisterNetRPCs()` override -- registers invoke handlers after NetworkId is assigned

**Supported parameter types:** byte, bool, short, ushort, int, uint, long, ulong, float, double, string, Vector2, Vector3, Quaternion, Color, Color32, ProductUserId, byte[], NetworkObject, INetSerializable.

**Constraints:**

- void return only
- No ref/out parameters
- No generic methods
- No abstract methods
- Violations produce compiler errors from the weaver

**CodeGen assembly:** `EOSNative.CodeGen/` -- editor-only, references Mono.Cecil. Depends on `com.unity.nuget.mono-cecil` (1.11.6).

### NetworkObject References in RPCs

NetworkObject is a registered serializer type. When sent as an RPC argument, it serializes as the NetworkId. The receiver resolves it back to the local instance automatically:

```csharp
NetworkManager.Instance.SendRPC(target, "GotHitBy", RPCTarget.Owner, attackerNetObj, 25f);

// Receiver:
NetworkManager.Instance.RegisterRPC(Net, "GotHitBy", reader =>
{
    NetworkObject attacker = NetSerializers.Read<NetworkObject>(reader);
    float damage = NetSerializers.Read<float>(reader);
});
```

## SyncVar LOD

Distance-based sync rate throttling. Attach `SyncVarLOD` to any NetworkObject to reduce bandwidth for distant objects. Works on the **owner side** — throttles how often dirty SyncVars are propagated to the send queue.

### Quick Start

```csharp
// Just add the SyncVarLOD component alongside NetworkObject.
// Default tiers work out of the box:
//   0-20m:   full rate (every dirty frame)
//   20-50m:  every 3rd dirty frame
//   50-100m: every 10th dirty frame
//   100m+:   no sync (object is culled)
```

### Custom Tiers

```csharp
var lod = GetComponent<SyncVarLOD>();

lod.Tiers = new List<SyncVarLOD.Tier>
{
    new() { MaxDistance = 30f, SyncEveryNthFrame = 1 },   // full rate
    new() { MaxDistance = 80f, SyncEveryNthFrame = 5 },   // 1/5 rate
    new() { MaxDistance = 150f, SyncEveryNthFrame = 15 }, // 1/15 rate
};
// Beyond 150m: no sync at all
```

### How It Works

1. Each `LateUpdate`, the component calculates the distance to the nearest peer's object
2. Based on the distance, it selects the active tier
3. When `NetworkObject.MarkDirty()` is called, `SyncVarLOD.ShouldPropagateDirty()` counts dirty frames and only allows propagation every Nth frame
4. Objects beyond all tiers are fully culled (no sync traffic)

### Properties

| Property | Type | Description |
|----------|------|-------------|
| `Tiers` | `List<Tier>` | Distance tiers, sorted by MaxDistance ascending |
| `AutoObserverPosition` | bool | Auto-detect nearest peer (default: true) |
| `ObserverPosition` | Vector3 | Manual override for distance reference point |
| `CurrentTier` | int | Active tier index (-1 = culled). Read-only |
| `CurrentSyncRate` | int | Effective sync rate (0 = culled). Read-only |

### Manual Observer Position

For custom camera or spectator logic, disable auto-detection and set the observer position manually:

```csharp
var lod = GetComponent<SyncVarLOD>();
lod.AutoObserverPosition = false;
lod.ObserverPosition = Camera.main.transform.position;
```

### Difference from NetworkTransform LOD

`SyncVarLOD` throttles **all SyncVars** on a NetworkObject at the dirty-flag level. `NetworkTransform` has its own built-in distance LOD that controls **interpolation quality** (spring vs tweened vs snap). They can be used together — SyncVarLOD controls send frequency while NetworkTransform LOD controls visual fidelity.

## Interest Management

Spatial interest management controls which objects each peer receives updates for, based on proximity. Instead of broadcasting every object to every peer, each peer only receives state for objects within their visibility range. This dramatically reduces bandwidth in large game worlds.

### Quick Start

```csharp
// Enable interest management on NetworkManager
NetworkManager.Instance.InterestManagementEnabled = true;

// Optionally add an InterestManager component to customize settings
// (auto-creates if not found when first needed)
var im = InterestManager.Instance;
im.VisRange = 100;  // 100 world units visibility
```

### How It Works

1. A **SpatialHashGrid** projects all NetworkObject positions onto a 2D grid (XZ plane by default)
2. Cell size is `visRange / 2`, so two cells span the full visibility range
3. Every 0.5s (configurable), **InterestManager** rebuilds per-peer interest sets using 9-neighbor grid lookup
4. Objects entering a peer's interest zone trigger a **spawn** message to that peer
5. Objects leaving a peer's interest zone trigger a **despawn** message to that peer
6. All broadcast paths (state updates, spawn, despawn, RPCs) are filtered through interest sets

### Always-Visible Objects

These objects bypass spatial filtering and are always sent to all peers:

- **NetworkRoomState** and **NetworkPlayerState** (reserved prefab IDs)
- Objects owned by the observing peer (you always see your own objects)
- Objects with `AlwaysVisible = true`

```csharp
// Mark an object as globally visible (objectives, world anchors, etc.)
networkObject.AlwaysVisible = true;
```

### Configuration

| Property | Type | Default | Description |
|----------|------|---------|-------------|
| `VisRange` | int | 100 | Visibility range in world units |
| `Hysteresis` | float | 0.1 | Buffer percentage (10%) to prevent flickering at boundaries |
| `RebuildInterval` | float | 0.5 | Seconds between full interest rebuilds |
| `GridAxes` | GridAxes | XZ | Which 2D plane to project onto (XZ for 3D games, XY for 2D) |

### Hysteresis

Once an object becomes visible to a peer, it stays visible until the distance exceeds `visRange * (1 + hysteresis)`. With the default 10% hysteresis, objects at 100m become visible but don't hide until 110m. This prevents objects at the boundary from flickering in and out.

### Integration with SyncVar LOD

Interest management and SyncVar LOD work at different levels and complement each other:

- **Interest Management** (InterestManager) — controls **which peers** receive data about an object. Binary: visible or not.
- **SyncVar LOD** (SyncVarLOD) — controls **how often** data is sent for visible objects. Graduated: full rate, half rate, 1/10 rate, etc.

For maximum bandwidth savings, use both: InterestManager culls distant objects entirely, SyncVarLOD reduces send rate for objects at medium distance.

### Performance

The spatial hash grid uses O(1) cell lookups. The per-peer interest rebuild iterates all objects once per peer, every `RebuildInterval` seconds. For a 64-player game with 1000 objects, that's ~64K checks every 0.5s — negligible CPU cost.

State updates (`SendStateUpdates`) are the hot path. Without interest management, one packet is broadcast to all peers. With interest management, per-peer packets are built containing only the objects that peer can see. This increases CPU slightly but reduces bandwidth proportionally to the culling ratio.

## NetworkTransform

All-in-one transform sync component. Combines spring physics, buffered interpolation, velocity extrapolation, and distance-based LOD in a single component.

```csharp
// Just add NetworkTransform to any GameObject with NetworkObject.
// Owner writes transform changes automatically; remote peers sync smoothly.
```

### Sync Methods

| Method | Best for | How it works |
|--------|----------|-------------|
| **Auto** (default) | Any object | Spring if Rigidbody present, Interpolation if kinematic |
| **Spring** | Physics objects (balls, vehicles, ragdolls) | Damped spring forces guide remote objects toward the target |
| **Interpolation** | Kinematic objects (characters, platforms) | SmoothSync-style buffered lerp, renders in the past |

### Extrapolation

When the state buffer runs out (packet loss, latency spike):

| Mode | Behavior |
|------|----------|
| **None** | Freeze at last known position |
| **Limited** (default) | Predict forward using velocity, capped by 5s and 20m. Applies gravity/drag if Rigidbody present |
| **Unlimited** | Predict forward indefinitely |

### Distance LOD

Three tiers with hysteresis to prevent flickering at boundaries:

| Tier | Distance | Behavior | CPU Cost |
|------|----------|----------|----------|
| **Full** | < 10m | Spring or Interpolation (configured method) | Highest |
| **Tweened** | 10m - 30m | Simple lerp toward target | Medium |
| **Simple** | > 30m | Snap to target | Lowest |

Rigidbody objects auto-switch to kinematic in Tweened/Simple tiers and restore when returning to Full. Hysteresis dead zone is 5m by default.

### Settings

| Group | Settings | Defaults |
|-------|----------|----------|
| **What to Sync** | Position, Rotation, Scale toggles | Pos + Rot on, Scale off |
| **Sync Method** | Auto / Spring / Interpolation | Auto |
| **Interpolation** | Delay, position ease speed, rotation ease speed | 0.1s, 0.85, 0.85 |
| **Extrapolation** | Mode, time limit, distance limit | Limited, 5s, 20m |
| **Spring Physics** | Pos frequency, pos damping, rot frequency, rot damping | 8Hz, 0.9, 10Hz, 0.85 |
| **Snap/Teleport** | Position snap distance, rotation snap angle | 5m, 90deg |
| **Send Thresholds** | Position threshold, rotation threshold | 0.001m, 0.1deg |
| **Distance LOD** | Enable, full distance, simple distance, dead zone | On, 10m, 30m, 5m |
| **Rest Detection** | Rest timeout | 0.5s |

### Teleport

```csharp
GetComponent<NetworkTransform>().Teleport(newPosition, newRotation);
// On owner: sets position + forces SyncVar sync
// On remotes: large jumps auto-snap via snap threshold
// Also clears state buffer and resets spring velocities
```

### Rest Detection

If no new SyncVar data arrives for `restTimeout` seconds (0.5s default), the object is assumed at rest and extrapolation stops. Prevents drift on idle objects.

## NetworkAnimator

Syncs Animator parameters across the network. Packs all float/int/bool parameters into a single `SyncVar<byte[]>` for bandwidth efficiency. Triggers are sent via RPC.

```csharp
// Just add NetworkAnimator to any GameObject with Animator + NetworkObject.
// Parameters auto-discovered. Owner changes sync to all peers.
```

### Usage

```csharp
// State parameters (float, int, bool) sync automatically.
// For triggers (events, not state):
GetComponent<NetworkAnimator>().SetNetworkTrigger("Jump");
```

### Wire Format

```
[floatCount:byte][floats...][intCount:byte][ints...][boolCount:byte][boolMask bytes]
```

### Settings

| Setting | Default | Description |
|---------|---------|-------------|
| Sync Interval | 0.1s | How often to check for parameter changes (10 Hz) |
| Animator | auto-detected | The Animator component to sync |

Change detection only sends when a parameter actually changes (float threshold 0.001, exact match for int/bool).

## EasySync

Normcore-inspired no-code property sync. Check boxes in the Inspector -- properties sync automatically.

```csharp
// No code needed!
// 1. Add EasySync component to any GameObject with NetworkObject
// 2. In Inspector, check the properties you want to sync
// 3. Owner writes -> remote peers receive automatically
```

### How It Works

A custom Inspector (`EasySyncEditor.cs`) scans all sibling components for public fields and properties of supported types. Toggle which ones to sync with checkboxes. At runtime, EasySync uses reflection to read/write values, packing everything into a single `SyncVar<byte[]>`.

### Supported Types

bool, byte, short, ushort, int, uint, long, ulong, float, double, string, Vector2, Vector3, Quaternion, Color, Color32.

### Per-Property WriteAccess

Each synced property can have its own write permission:

| Access | Who Can Write | Use Case |
|--------|--------------|----------|
| **Owner** (default) | NetworkObject owner | Player state (position, health, score) |
| **Host** | Current host only | Game state (round timer, phase, rules) |
| **All** | Any peer (last-write-wins) | Shared cursors, collaborative editing |

Set per-property in the Inspector dropdown. At runtime, `CanWrite()` checks if the local peer is authorized for any binding.

### Interpolation

Toggle "Lerp" per property in the Inspector for smooth remote updates instead of snapping. Only available for numeric and vector types.

**Supported:** float, double, Vector2, Vector3, Quaternion (uses Slerp), Color, Color32, int, short, byte

| Setting | Default | Description |
|---------|---------|-------------|
| Lerp | off | Enable interpolation for this property |
| Speed | 15 | Interpolation speed (higher = faster catch-up) |

Non-writers interpolate toward target values each frame. Once the value is close enough (epsilon-based per type), interpolation stops until the next update.

### Convert to Code

Click **Convert to Code** in the Inspector to export a typed `NetworkBehaviour` .cs file. This generates:

- `SyncVar<T>` declarations for each binding
- Component references cached in `Awake()`
- `OnChanged` callbacks that apply values on remote peers
- `Update()` with WriteAccess-gated writes grouped by access level

Replace the EasySync component with the generated file for compile-time type safety and no reflection overhead.

### Settings

| Setting | Default | Description |
|---------|---------|-------------|
| Sync Interval | 0.1s | How often to check for property changes |

Skips Transform, NetworkObject, NetworkBehaviour subclasses, and base Unity properties.

## NetworkRoomState

Singleton NetworkObject representing the shared room/game state. The host auto-creates it after the first peer connects. Survives host migration (`DestroyWithOwner = false`).

- **Well-known NetworkId:** `0xFFFF0001`
- **Reserved PrefabId:** `0xFFF0`

### SyncVars (index 0-7)

| Index | Name | Type | Description |
|-------|------|------|-------------|
| 0 | GameMode | string | e.g. "deathmatch", "ctf" |
| 1 | MapName | string | Current map/level name |
| 2 | RoundNumber | int | Current round (0 = first) |
| 3 | PlayerCount | int | Current player count |
| 4 | MaxPlayers | int | Maximum allowed (default 16) |
| 5 | RoundTimer | float | Round timer in seconds |
| 6 | Phase | byte | 0=Lobby, 1=Loading, 2=Playing, 3=PostMatch |
| 7 | IsInProgress | bool | Whether a game is in progress |

### Properties SyncDictionary (index 8)

Dynamic `SyncDictionary<string, string>` for arbitrary custom room data:

```csharp
var room = NetworkManager.Instance.RoomState;

// Host writes
room.GameMode.Value = "deathmatch";
room.CurrentPhase = GamePhase.Playing;
room.SetProperty("score_limit", "100");
room.SetProperty("friendly_fire", "true");

// Anyone reads
int limit = room.GetPropertyInt("score_limit", 50);
bool ff = room.GetPropertyBool("friendly_fire");
string map = room.MapName.Value;
```

### Lobby Attribute Mirroring

GameMode, MapName, and IsInProgress automatically push to EOS lobby attributes (rate-limited to 1/sec). Add custom keys to mirror:

```csharp
room.SearchablePropertyKeys.Add("difficulty");
room.SetProperty("difficulty", "hard");
// "difficulty" will now appear in lobby search results
```

### GamePhase Enum

```csharp
public enum GamePhase : byte
{
    Lobby = 0,
    Loading = 1,
    Playing = 2,
    PostMatch = 3,
}

room.CurrentPhase = GamePhase.Playing;
```

## NetworkPlayerState

Per-player NetworkObject. Each peer auto-creates their own on connect. Destroyed when the player disconnects (`DestroyWithOwner = true`).

- **Reserved PrefabId:** `0xFFF1`
- **Standard NetworkId generation** (PUID partition)

### SyncVars (index 0-7)

| Index | Name | Type | Description |
|-------|------|------|-------------|
| 0 | DisplayName | string | Auto-populated from EOSPlayerRegistry |
| 1 | Team | byte | Team index (0 = unassigned) |
| 2 | IsReady | bool | Pre-game ready state |
| 3 | Score | int | Kills/score |
| 4 | Deaths | int | Death count |
| 5 | Assists | int | Assist count |
| 6 | Loadout | string | Class/loadout identifier |
| 7 | PlayerSlot | byte | Seat index |

### CustomData SyncDictionary (index 8)

```csharp
var me = NetworkManager.Instance.LocalPlayerState;
me.Team.Value = 1;
me.IsReady.Value = true;
me.SetCustom("skin", "gold_armor");

var them = NetworkManager.Instance.GetPlayerState(puid);
string name = them.DisplayName.Value;
float kd = them.KDRatio;

// Iterate all players
foreach (var kvp in NetworkManager.Instance.PlayerStates)
{
    Debug.Log($"{kvp.Value.DisplayName.Value}: {kvp.Value.Score.Value} kills");
}
```

### Spectator Detection

```csharp
if (playerState.IsSpectating)
    Debug.Log("This player is spectating");
```

## NetworkSceneManager

Host-driven scene loading. Host calls load/unload, all peers follow. Scene info is stored on NetworkRoomState properties so late joiners load the correct scenes.

### API

```csharp
var sceneMgr = NetworkSceneManager.Instance;

// Load a scene (replaces current -- host only)
sceneMgr.LoadScene("Arena_01");

// Load additive (adds to current -- host only, max 8)
sceneMgr.LoadSceneAdditive("Props_01");

// Unload additive scene (host only)
sceneMgr.UnloadScene("Props_01");
```

### Events

```csharp
sceneMgr.OnSceneLoadCompleted += sceneName =>
{
    Debug.Log($"Scene loaded: {sceneName}");
};

sceneMgr.OnAllPeersLoaded += () =>
{
    // All peers finished loading -- safe to start the round
    StartRound();
};

sceneMgr.OnSceneUnloaded += sceneName => { };
```

### Load Flow

1. Host calls `LoadScene("Arena_01")`
2. Updates RoomState scene properties
3. Broadcasts MSG_SCENE_LOAD (reliable) to all peers
4. All peers load async via `SceneManager.LoadSceneAsync`
5. After load: `RegisterSceneObjects()` auto-assigns scene NetworkObjects to host
6. Non-host peers send MSG_SCENE_LOADED_ACK to host
7. Host fires `OnAllPeersLoaded` when all ACKs received

### Late Join

After receiving the snapshot, new peers read scene info from RoomState and automatically load the correct scenes:

```csharp
// Automatic -- no code needed. NetworkManager calls
// NetworkSceneManager.SyncScenesFromRoomState() after snapshot.
```

### Properties

| Property | Type | Description |
|----------|------|-------------|
| `IsLoading` | bool | True while a scene load is in progress |
| `LoadingSceneName` | string | Name of the scene being loaded (null if idle) |

## Host Migration

### How It Works

Host is always the peer with the lexicographically lowest PUID string. When a peer disconnects:

1. All peers independently recompute the host (deterministic -- same result everywhere)
2. The new host claims orphaned objects (objects owned by the disconnected peer)
3. Objects with `DestroyWithOwner = true` are despawned instead of transferred
4. Objects continue running with their existing SyncVar values -- no destroy/reinstantiate

### Authority Transfer

```csharp
// Transfer ownership (owner or host only)
NetworkManager.Instance.TransferAuthority(obj, newOwnerPuid);

// Request ownership from the host (non-host peers)
NetworkManager.Instance.RequestAuthority(obj);
// Host auto-approves by default
```

### Custom Authority Validation

```csharp
// On the host -- add custom validation for authority requests
NetworkManager.Instance.OnAuthorityRequested = (obj, requester) =>
{
    // Example: only allow if requester is close enough
    float dist = Vector3.Distance(obj.transform.position, GetPlayerPos(requester));
    return dist < 5f;
};
```

### RPC Migration Buffer

Host-targeted and owner-targeted RPCs are automatically buffered during the migration window. When a peer disconnects, any RPCs sent during host re-election are queued and replayed once the new host is confirmed. No RPCs are dropped during transitions.

## Late-Join Snapshots

When a new peer connects, the host sends a full snapshot of all active NetworkObjects.

### Flow

1. New peer sends `MSG_SNAPSHOT_REQUEST` to the host
2. Host responds with chunked `MSG_SNAPSHOT` messages (16 objects per chunk)
3. New peer instantiates objects from the prefab registry with correct state
4. After receiving RoomState, the new peer auto-creates their PlayerState and syncs scenes

### Chunked Delivery

Snapshots are sent in priority-ordered chunks:

| Priority | Objects | Reason |
|----------|---------|--------|
| 1 | NetworkRoomState | Late joiners know game state immediately |
| 2 | All NetworkPlayerStates | Late joiners know about all players |
| 3 | Everything else | Game objects, environment, etc. |

Each chunk is a separate reliable ordered message. The receiver handles duplicates gracefully (objects already present are updated, not re-created).

## Connection Quality

### Reliable State Fallback

STATE_UPDATE is sent unreliable for speed. But if a packet drops, a SyncVar change might never arrive. After 200ms, if the object has not been re-dirtied, its full state is resent via reliable SNAPSHOT. This guarantees eventual consistency with minimal overhead -- continuously-changing state (like movement) stays unreliable, while one-shot changes (like HP) get reliable delivery.

### Sequence-Based Stale Rejection

STATE_UPDATE packets include a per-object sequence number. Receivers only apply updates where `(newSeq - lastSeq) > 0` using wrapping comparison. Out-of-order packets are silently discarded, preventing stale state from overwriting newer data.

### Object Pooling

Built-in per-prefab object pooling reduces GC pressure. `Despawn()` deactivates and returns objects to the pool. `Spawn()` checks the pool before calling `Instantiate`.

```csharp
// Enable pooling (on by default, toggle on NetworkManager component)
// Pre-warm pools at startup
NetworkManager.Instance.Prewarm(prefabId: 0, count: 10);
NetworkManager.Instance.Prewarm(prefabId: 1, count: 50);
```

### Packet Compression

Opt-in Deflate compression for message payloads. Transparent to application code:

```csharp
// Enable compression
NetworkManager.Instance.CompressionEnabled = true;

// Or configure threshold on the router directly
EOSP2PManager.Instance.Router.CompressionEnabled = true;
EOSP2PManager.Instance.Router.CompressionThreshold = 128;  // bytes
```

Compression only applied when the output is smaller than the original. Old peers that do not understand compressed flags silently ignore them.

## Spectator Mode

A peer can join as a read-only observer. Spectators receive all state but cannot spawn objects or become host.

```csharp
// Set before joining a lobby
NetworkManager.Instance.IsSpectator = true;
// Then join lobby normally...

// Check from any NetworkBehaviour
if (IsSpectator) return;  // skip gameplay logic

// Send RPC only to players (not spectators)
[NetRpc(RPCTarget.Players)]
public void StartRound() { ... }

// Check if a specific peer is spectating
if (NetworkManager.Instance.IsPeerSpectator(puid)) { ... }
```

Spectator PUIDs are excluded from host election. If ALL peers are spectators, the lowest PUID becomes host anyway (with a warning).

## Master Client Verification

Opt-in RPC validation callback. When set, fires before executing any incoming remote RPC. Null by default (all RPCs allowed, zero overhead).

```csharp
// Only allow RPCs from the object's owner
NetworkManager.Instance.EnableOwnerOnlyRPCValidation();

// Custom validation
NetworkManager.Instance.OnRPCValidation = (sender, target, methodHash) =>
{
    if (target == null) return false;
    if (target.OwnerId == sender) return true;  // owner always allowed
    // Allow specific cross-owner RPCs
    return methodHash == NetworkManager.FnvHash("TakeDamage");
};

// Disable validation
NetworkManager.Instance.OnRPCValidation = null;
```

## Scene Object Auto-Ownership

NetworkObjects placed in the scene (not spawned at runtime) are automatically registered and assigned to the host.

```csharp
// Call after scene load, or let it happen automatically
NetworkManager.Instance.RegisterSceneObjects();
```

- Scene objects get deterministic NetworkIds based on their hierarchy path (FNV-1a hash)
- Ownerless objects auto-assign to the current host
- Authority is broadcast so all peers agree on ownership
- Works with late join (scene objects are included in snapshots)

## Message IDs

All Layer 2 networking messages use the `0xA0`-`0xAF` range:

| ID | Name | Reliability | Channel | Payload |
|----|------|-------------|---------|---------|
| `0xA0` | STATE_UPDATE | Unreliable | 0 | Packed object count + per-object: networkId, sequence, dataLen, dirtyMask, dirty values |
| `0xA1` | SPAWN | Reliable | 1 | prefabId, networkId, ownerId, position, rotation, destroyWithOwner, syncVarCount, all values |
| `0xA2` | DESPAWN | Reliable | 1 | networkId |
| `0xA3` | AUTHORITY | Reliable | 1 | networkId, newOwnerId |
| `0xA4` | SNAPSHOT | Reliable | 1 | objectCount + per-object (same as SPAWN) |
| `0xA5` | SNAPSHOT_REQUEST | Reliable | 1 | empty |
| `0xA6` | RPC | Reliable | 1 | networkId, methodHash, argData |
| `0xA7` | AUTHORITY_REQUEST | Reliable | 1 | networkId |
| `0xA8` | PING | Unreliable | 2 | sequence(u32), senderTimestamp(f32) |
| `0xA9` | PONG | Unreliable | 2 | sequence(u32), originalTimestamp(f32) |
| `0xAA` | SCENE_LOAD | Reliable | 1 | sceneName, additive(bool) |
| `0xAB` | SCENE_UNLOAD | Reliable | 1 | sceneName |
| `0xAC` | SCENE_LOADED_ACK | Reliable | 1 | sceneName |
| `0xAD` | RPC_VALIDATED | Reliable | 1 | networkId, methodHash, originalTarget, argData |
| `0xAE` | RPC_REBROADCAST | Reliable | 1 | networkId, methodHash, originalTarget, argData |
