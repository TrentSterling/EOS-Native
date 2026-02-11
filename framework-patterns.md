# Photon PUN & Fusion 2 Architecture Research

Comprehensive research on Photon's networking frameworks, their architecture decisions, and how they compare to EOS-Native's current implementation.

**Last updated:** 2026-02-10

---

## Table of Contents

1. [PUN Classic (Photon Unity Networking 2)](#1-pun-classic-photon-unity-networking-2)
2. [Photon Fusion 2 — Shared Mode](#2-photon-fusion-2--shared-mode)
3. [Photon Fusion 2 — Host Mode / Server Mode](#3-photon-fusion-2--host-mode--server-mode)
4. [State Authority Model Comparison](#4-state-authority-model-comparison)
5. [RPCs: PUN vs Fusion 2 vs EOS-Native](#5-rpcs-pun-vs-fusion-2-vs-eos-native)
6. [Interest Management](#6-interest-management)
7. [NetworkBehaviour Component Scoping](#7-networkbehaviour-component-scoping)
8. [SimulationBehaviour Pattern](#8-simulationbehaviour-pattern)
9. [Tick-Based Simulation & Prediction](#9-tick-based-simulation--prediction)
10. [Lag Compensation in Fusion 2](#10-lag-compensation-in-fusion-2)
11. [Prefab Registration & Spawning](#11-prefab-registration--spawning)
12. [Comparison: EOS-Native vs Photon](#12-comparison-eos-native-vs-photon)
13. [Key Patterns EOS-Native Could Adopt](#13-key-patterns-eos-native-could-adopt)
14. [Sources](#14-sources)

---

## 1. PUN Classic (Photon Unity Networking 2)

PUN 2 is in maintenance/LTS mode with no further feature updates. New projects should use Fusion or Quantum. However, its patterns remain widely understood and influenced every successor framework.

### Core Architecture

PUN uses a **cloud-relayed, room-based** model. All traffic flows through Photon Cloud servers. There is no direct peer-to-peer connection. One player is designated **Master Client** (similar to "host") who has extra authority (e.g., scene loading, instantiation control).

### PhotonView — The Network Identity

`PhotonView` is PUN's equivalent of a NetworkObject. Every networked GameObject needs one.

Key properties:
- **ViewID** — Unique network identifier (auto-assigned or manual)
- **Owner** — The player who controls this object
- **Observed Components** — List of `IPunObservable` scripts whose state is serialized
- **Synchronization option** — Off / Reliable Delta Compressed / Unreliable / Unreliable On Change
- **Ownership Transfer** — Fixed / Takeover / Request

### OnPhotonSerializeView — Continuous State Sync

The primary mechanism for synchronizing state. Scripts implement `IPunObservable` and are assigned as an "Observed Component" on the PhotonView.

```csharp
public class PlayerSync : MonoBehaviourPun, IPunObservable
{
    public void OnPhotonSerializeView(PhotonStream stream, PhotonMessageInfo info)
    {
        if (stream.IsWriting)  // Owner writes
        {
            stream.SendNext(transform.position);
            stream.SendNext(transform.rotation);
            stream.SendNext(health);
        }
        else  // Remote peers read
        {
            networkPosition = (Vector3)stream.ReceiveNext();
            networkRotation = (Quaternion)stream.ReceiveNext();
            health = (float)stream.ReceiveNext();
        }
    }
}
```

**Serialization rates:**
- `PhotonNetwork.SerializationRate` — How often `OnPhotonSerializeView` is called (default 10/s)
- `PhotonNetwork.SendRate` — How often packets are actually sent (default 20/s)

**Synchronization options:**
| Option | Behavior |
|--------|----------|
| Off | No data transfer |
| Reliable Delta Compressed | Guaranteed delivery, sends null when unchanged |
| Unreliable | Ordered but lossy, no artificial delay |
| Unreliable On Change | Ordered but lossy, pauses when values repeat |

### PUN RPCs

```csharp
[PunRPC]
void TakeDamage(float amount, PhotonMessageInfo info)
{
    health -= amount;
    Debug.Log($"Hit by {info.Sender.NickName}");
}

// Calling:
photonView.RPC("TakeDamage", RpcTarget.All, 25f);
```

**RpcTarget enum:**
- `All` — Every client (including sender)
- `Others` — All except sender
- `MasterClient` — Only the room's Master Client
- `AllBuffered` / `OthersBuffered` — Server remembers and sends to late-joiners
- `AllViaServer` / `OthersViaServer` — Routes through server for ordered execution

**Constraints:**
- Script must be on the same GameObject as PhotonView
- Cannot be static or generic
- Cannot have return values other than void
- Do not attach duplicate component types with RPC methods on the same GameObject
- Method name is passed as a string (reflection-based dispatch)

### RaiseEvent — Custom Events Without PhotonView

```csharp
object[] content = new object[] { new Vector3(10f, 2f, 5f), 1, 2, 5 };
RaiseEventOptions options = new RaiseEventOptions { Receivers = ReceiverGroup.All };
PhotonNetwork.RaiseEvent(42, content, options, SendOptions.SendReliable);
```

- Event codes 0-199 available for custom use
- Can target Interest Groups, specific actors, or receiver groups
- Events can be cached (buffered) for late-joiners
- Receiving via `IOnEventCallback` interface or `EventReceived` delegate

### PUN Interest Groups

Basic channel-based filtering:
- Up to 256 groups (group 0 = broadcast, always subscribed)
- Clients subscribe/unsubscribe dynamically
- PhotonView can be assigned to a group
- RaiseEvent can target specific groups
- **No spatial awareness** — purely manual group assignment

### Custom Properties

Room and player properties stored as key-value Hashtables:
- `SetCustomProperties()` pushes changes through server
- CAS (Check and Swap) with `expectedProperties` for atomic updates
- Callbacks: `OnRoomPropertiesUpdate()`, `OnPlayerPropertiesUpdate()`

### PUN Ownership Transfer

Three modes set on the PhotonView Inspector:
1. **Fixed** — Owner never changes
2. **Takeover** — Any client can claim ownership immediately
3. **Request** — Current owner must approve the transfer

---

## 2. Photon Fusion 2 — Shared Mode

Fusion 2 Shared Mode is the spiritual successor to PUN — similar ease of use but with tick-based simulation, zero-allocation networking, IL weaving, and snapshot interpolation.

### How It Works

- Session runs on **Photon Cloud** (no dedicated server, no host player)
- Cloud acts as a **packet relay** and holds a complete copy of the networked state
- Cloud keeps sessions alive until the last player leaves
- Each client has **State Authority** over objects they spawn
- **No resimulation** — clients never roll back or re-execute ticks
- Uses accurate **snapshot interpolation** (same algorithm as Server Mode in Fusion 2)

### NetworkObject — The Network Identity

Every networked GameObject needs a `NetworkObject` component. Key concepts:
- **NetworkId** — Unique identifier (auto-assigned at spawn)
- **State Authority** — The peer that can write `[Networked]` properties
- **Input Authority** — The peer that provides input (can differ from State Authority)
- **IsSpawnable** — Must be enabled for prefab to appear in the prefab table
- **Allow State Authority Override** — Whether another player can request authority without current owner releasing

### [Networked] Properties — IL-Weaved State

Instead of `OnPhotonSerializeView`, Fusion uses `[Networked]` attribute on auto-properties. IL weaving at compile time rewrites getters/setters to read/write from a shared state buffer.

```csharp
public class Player : NetworkBehaviour
{
    [Networked] public float Health { get; set; }
    [Networked] public Vector3 Position { get; set; }
    [Networked] public NetworkString<_32> PlayerName { get; set; }
    [Networked] public TickTimer CooldownTimer { get; set; }
}
```

**Key rules:**
- Must be auto-properties (`{ get; set; }`) — fields not supported
- Only State Authority can write (writes from non-authority are overwritten)
- Cannot be accessed until `Spawned()` is called
- Replication is one-way: State Authority -> all interested peers
- IL weaving handles compression, validation, and buffer management automatically

### OnChanged Callbacks

```csharp
[Networked, OnChangedRender(nameof(OnHealthChanged))]
public float Health { get; set; }

void OnHealthChanged()
{
    healthBar.SetValue(Health);
}
```

- Fires immediately after the tick where the value changed
- May fire multiple times during resimulation (Host Mode) or be skipped if value oscillates faster than network rate
- Primary advantage over RPCs: tied to state, so late-joiners get the current value

### TickTimer

A network-friendly timer that stores a target tick rather than incrementing a float:

```csharp
[Networked] public TickTimer CooldownTimer { get; set; }

public override void FixedUpdateNetwork()
{
    if (CooldownTimer.ExpiredOrNotRunning(Runner))
    {
        CooldownTimer = TickTimer.CreateFromSeconds(Runner, 2f);
        Fire();
    }
}
```

- Uses less bandwidth (target tick never changes after creation, unlike a decrementing float)
- Automatically correct across prediction/resimulation
- Replaces PUN's `PhotonNetwork.Time` approach

### Shared Mode Authority Model

```
Player A spawns ObjectA → Player A has State Authority over ObjectA
Player B spawns ObjectB → Player B has State Authority over ObjectB

Player A can write [Networked] props on ObjectA → replicated to all
Player A writes [Networked] props on ObjectB → overwritten by next update from Player B
```

Transfer mechanisms:
- `Object.ReleaseStateAuthority()` — Current authority voluntarily releases
- `Object.RequestStateAuthority()` — Another player requests it
- `AllowStateAuthorityOverride` toggle on NetworkObject — Allows hostile takeover
- **SharedModeMasterClient** — Special role (like PUN Master Client), can receive authority for scene objects

### Shared Mode Master Client

One player is designated the "master client" (first to join or next in line if master leaves). The master client has special powers:
- Can claim State Authority over scene objects (objects not spawned by any player)
- Host migration is automatic when master disconnects
- `Runner.IsSharedModeMasterClient` check

---

## 3. Photon Fusion 2 — Host Mode / Server Mode

### Dedicated Server Mode

- Server runs as headless Unity build (`GameMode.Server`)
- Clients connect as `GameMode.Client`
- Server has **full and exclusive State Authority over all objects**
- Clients can only modify state by: (a) sending input, or (b) RPCs
- Strongest cheat protection — server validates everything
- No NAT punch-through needed (public IPs)
- Highest hosting cost

### Client Host Mode

- One player runs as `GameMode.Host` (server + local client)
- Others run as `GameMode.Client`
- Equivalent to Dedicated Server but cheaper (no infrastructure)
- Built-in UDP NAT punch-through; relay fallback (~10% of connections)
- `GameMode.AutoClientOrHost` — First player becomes host automatically
- Host can cheat (has full state authority)

### Host Mode vs Shared Mode Feature Comparison

| Feature | Shared Mode | Host Mode / Server Mode |
|---------|-------------|------------------------|
| **Authority** | Distributed (per-player) | Centralized (host/server) |
| **Prediction** | No (authority writes directly) | Yes (client-side prediction + rollback) |
| **Resimulation** | Never | Yes (on every server snapshot) |
| **CPU Cost** | Lower (no resim) | Higher (resim overhead) |
| **Cheat Protection** | Weak (players control own objects) | Strong (server validates) |
| **Physics** | Per-player authority | Server-authoritative |
| **Interest Management AOI** | 1 region per player | Multiple regions per player |
| **Lag Compensation** | Not available | Full hitbox rewind system |
| **Late-Join** | Cloud holds full state | Server sends snapshot |
| **Mobile/WebGL** | Excellent | Possible but harder |
| **Player Count** | Scales well (100+) | Harder to scale (2-16 typical) |
| **Host Migration** | Automatic (cloud owns session) | Manual (need migration logic) |
| **Ideal For** | Co-op, casual, mobile, party | Competitive, physics-heavy, FPS |

### When To Use Each

**Shared Mode:** Casual games, co-op, mobile, party games, games with many players entering/leaving, WebGL. Similar to PUN but better performance and no runtime allocations.

**Host Mode:** Competitive games with 2-4 players, physics-heavy games, games needing lag compensation, shooters. Direct connection enables better physics sync.

**Dedicated Server:** Competitive with anti-cheat requirements, persistent worlds, esports, any game where trust is paramount.

---

## 4. State Authority Model Comparison

### PUN

```
Master Client = special authority (scene load, instantiation)
Each player owns their PhotonView objects
Ownership transfer: Fixed / Takeover / Request
No concept of "State Authority" vs "Input Authority"
```

### Fusion 2 Shared Mode

```
Each player = State Authority over objects they spawn
Cloud relays state, holds complete copy
Authority transfer via RequestStateAuthority() / ReleaseStateAuthority()
SharedModeMasterClient = special role for scene objects
Input Authority and State Authority are the SAME peer
```

### Fusion 2 Host/Server Mode

```
Host/Server = State Authority over ALL objects
Clients = Input Authority only (can provide input, cannot write state)
State Authority CANNOT be transferred (server always owns state)
Input Authority CAN be transferred via AssignInputAuthority()
Two distinct concepts: who provides input vs who owns state
```

### EOS-Native

```
Deterministic host election (lowest PUID string)
Each player = owner of objects they spawn (OwnerId)
Owner writes SyncVars; remote peers receive deltas
Authority transfer via TransferAuthority() broadcast
Host claims orphaned objects on disconnect
Late-join: host sends full SNAPSHOT to new peer
No concept of "Input Authority" separate from ownership
```

**EOS-Native is closest to Fusion 2 Shared Mode** — distributed authority where each player owns their objects, with a host/master concept for tiebreaking and orphan management.

---

## 5. RPCs: PUN vs Fusion 2 vs EOS-Native

### PUN RPCs

```csharp
// Declaration
[PunRPC]
void ChatMessage(string message, PhotonMessageInfo info) { }

// Invocation (string-based)
photonView.RPC("ChatMessage", RpcTarget.All, "Hello!");
```

- String-based method lookup (slower, error-prone)
- Target: All / Others / MasterClient / Buffered variants / ViaServer
- Buffered RPCs persist for late-joiners (server stores them)
- Must be on same GameObject as PhotonView
- No source filtering (any client can call any RPC)

### Fusion 2 RPCs

```csharp
// Declaration
[Rpc(RpcSources.InputAuthority, RpcTargets.StateAuthority)]
public void RpcRequestDamage(float amount, RpcInfo info = default) { }

// Invocation (direct method call)
RpcRequestDamage(25f);
```

- Direct method call (IL-weaved into network call)
- `RpcSources` — Who can send: All / InputAuthority / StateAuthority / Proxies
- `RpcTargets` — Who executes: All / InputAuthority / StateAuthority / Proxies
- `InvokeLocal` — Whether sender also executes (default true)
- `TickAligned` — Delay execution until matching tick (default true)
- `Channel` — Reliable (default) or Unreliable
- **No buffering** — RPCs are transient, late-joiners never see them
- Targeted RPCs via `[RpcTarget]` on a `PlayerRef` parameter
- Static RPCs available on any `SimulationBehaviour`

### EOS-Native RPCs

```csharp
// Registration (handler-based)
NetworkManager.Instance.RegisterRPC(Net, "TakeDamage", reader => {
    float dmg = NetSerializers.Read<float>(reader);
    Health.Value -= dmg;
});

// Invocation
NetworkManager.Instance.SendRPC(player, "TakeDamage", RPCTarget.Owner, 25f);

// OR with [NetRpc] attribute (IL-weaved, v2.18.0+)
[NetRpc]
void TakeDamage(float amount) { Health.Value -= amount; }

[NetRpc(Target = RPCTarget.All)]
void Announce(string msg) { Debug.Log(msg); }
```

- Two systems: manual `RegisterRPC`/`SendRPC` and attribute-based `[NetRpc]`
- `[NetRpc]` uses IL weaving similar to Fusion
- RPCTarget: All / Others / Owner / Host / Players (skips spectators)
- `[NetRpc(Validated = true)]` routes through host for server-side validation
- `OnRPCValidation` callback for custom validation logic
- Sender validation on all handlers (sender must be owner or host)
- Not buffered for late-joiners (transient, like Fusion)

### Key Differences Summary

| Feature | PUN | Fusion 2 | EOS-Native |
|---------|-----|----------|------------|
| Declaration | `[PunRPC]` | `[Rpc]` | `[NetRpc]` or manual |
| Invocation | String `photonView.RPC()` | Direct method call | Direct call or `SendRPC()` |
| Source Filtering | None | RpcSources enum | Sender validation |
| Target Filtering | RpcTarget enum | RpcTargets enum | RPCTarget enum |
| Buffered/Late-Join | Yes (Buffered variants) | No | No |
| Tick Alignment | No | Yes (TickAligned) | No |
| IL Weaving | No | Yes | Yes (`[NetRpc]`) |
| Validation | None | Source/Target constraints | `[Validated]` + host routing |

---

## 6. Interest Management

### PUN — Interest Groups

- 256 groups, manual assignment
- Group 0 = broadcast (all clients, cannot unsubscribe)
- Groups 1-255 = subscribe/unsubscribe at runtime
- PhotonView assigned to a group; RaiseEvent targets groups
- **No spatial awareness** — developer manually assigns groups
- Good for: chat channels, team filtering, zone-based filtering

### Fusion 2 — Area of Interest (AOI)

Three `ObjectInterest` modes on each NetworkObject:

**1. Area of Interest (AOI)**
- Players define spatial regions via `Runner.AddPlayerAreaOfInterest()`
- Objects inside a player's region receive updates
- Uses `NetworkTRSP` position data for spatial checks
- Host/Server mode: multiple regions per player
- Shared mode: one region per player
- Compatible with NetworkTransform, NetworkRigidbody3D/2D

**2. Global**
- All players always receive updates (no culling)
- Used for game managers, UI state, global objectives

**3. Explicit**
- Objects invisible to all by default
- Manually flag with `SetPlayerAlwaysInterested()`
- Use case: revealed items, scouted areas, minimap pings

**Interest Management Addon (Host/Server only):**
- Editor-based workflow with gizmo preview
- Three shapes: Sphere, Box, Cone
- Custom shape support
- `InterestEnter` / `InterestExit` callbacks
- Not available in Shared Mode

**Per-Behaviour filtering:**
- `ReplicateTo(PlayerRef player)` on NetworkBehaviour
- Only in Server/Host mode
- Filters which behaviours on an object replicate to which players
- Operates after object-level interest (if object is out, no behaviours replicate)

### EOS-Native — Spatial Interest Management

- `InterestManager` + `SpatialHashGrid` (v2.20.0)
- Grid-based spatial hashing
- Objects outside a player's interest radius don't receive state updates
- Attached children inherit interest from root parent
- Detached children (runtime reparented) use their own position
- `SyncVarLOD` component (v2.18.0) — distance-based update throttling with 4 tiers

### Comparison

| Feature | PUN | Fusion 2 | EOS-Native |
|---------|-----|----------|------------|
| Spatial AOI | No | Yes (built-in) | Yes (SpatialHashGrid) |
| Group/Channel | Yes (256 groups) | No (use AOI instead) | No |
| Per-Behaviour Filter | No | Yes (ReplicateTo) | No |
| Shapes | N/A | Sphere/Box/Cone | Grid cells |
| Enter/Exit Callbacks | No | Yes | No |
| LOD/Throttling | No | No (but less data sent) | Yes (SyncVarLOD, 4 tiers) |
| Shared Mode Support | N/A | Partial (1 AOI region) | Yes |

---

## 7. NetworkBehaviour Component Scoping

### PUN — PhotonView + Observed Components

- One PhotonView per networked GameObject (typically)
- Multiple scripts can implement `IPunObservable`
- Only the **single assigned Observed Component** gets `OnPhotonSerializeView` calls
- For multiple synced scripts, need multiple PhotonViews or manual delegation
- RPCs scoped to the PhotonView's GameObject

### Fusion 2 — Multiple NetworkBehaviours Per Object

- **Any number of NetworkBehaviour components** on a NetworkObject and its child transforms
- Each NetworkBehaviour has a **unique network identifier**
- Each represents **part of the NetworkObject's state** (its own `[Networked]` properties)
- All get `FixedUpdateNetwork()` called every tick when the object is in simulation
- Combined state of all behaviours = the object's snapshot for that tick

```csharp
// On one NetworkObject:
public class Health : NetworkBehaviour
{
    [Networked] public float HP { get; set; }
}

public class Movement : NetworkBehaviour
{
    [Networked] public Vector3 Velocity { get; set; }
}

public class Inventory : NetworkBehaviour
{
    [Networked, Capacity(10)]
    public NetworkArray<int> Items => default;
}
```

Each behaviour independently:
- Has its own `[Networked]` properties
- Gets its own `Spawned()` / `Despawned()` / `FixedUpdateNetwork()` / `Render()`
- Can check `HasStateAuthority` / `HasInputAuthority`
- Can declare its own `[Rpc]` methods
- Has a unique `NetworkBehaviourId` for cross-network references

### EOS-Native — NetworkBehaviour + SyncVars

- One `NetworkObject` per networked GameObject
- Multiple `NetworkBehaviour` components supported on the same object
- Each behaviour declares its own `SyncVar<T>` fields
- SyncVars are registered with the parent NetworkObject (ordered list)
- Each behaviour can register its own RPCs
- Dirty tracking is per-SyncVar, aggregated per-NetworkObject for the wire

**Key difference from Fusion:** EOS-Native SyncVars are registered at runtime in `Awake()` via `Sync()` calls, while Fusion uses compile-time IL weaving. EOS-Native's approach is more flexible (no build step) but less performant (reflection/dictionary overhead vs direct buffer access).

---

## 8. SimulationBehaviour Pattern

### Fusion 2 SimulationBehaviour

Base class for `NetworkBehaviour`. Used for scripts that need Fusion's simulation loop but don't live on a NetworkObject.

**Key characteristics:**
- No `[Networked]` properties (cannot sync state)
- Access to `FixedUpdateNetwork()`, `Render()`, and other Fusion callbacks
- Can **modify** `[Networked]` state on other NetworkBehaviours
- Must be registered via `NetworkRunner.AddGlobal()` (unless on Runner's GameObject)
- Perfect for: spawners, game managers, scoring systems, AI directors

```csharp
public class ItemSpawner : SimulationBehaviour
{
    public override void FixedUpdateNetwork()
    {
        if (Runner.IsServer && ShouldSpawnItem())
        {
            Runner.Spawn(itemPrefab, GetSpawnPosition());
        }
    }
}
```

### EOS-Native SimulationBehaviour (v2.37.0)

Abstract MonoBehaviour base for game managers that need tick/peer/host callbacks but don't live on a NetworkObject.

**Key characteristics:**
- Auto-subscribes in `OnEnable`, auto-unsubscribes in `OnDisable`
- Virtual callbacks: `OnTick`, `OnPostTick`, `OnPeerConnected`, `OnPeerDisconnected`, `OnBecameHost`, `OnLostHost`
- No networking state — purely a callback receiver
- Lives in `Runtime/EOSNative/Net/SimulationBehaviour.cs`

```csharp
public class GameManager : SimulationBehaviour
{
    public override void OnTick() { /* runs every network tick */ }
    public override void OnPeerConnected(string puid) { /* new player */ }
    public override void OnBecameHost() { /* we're now the host */ }
}
```

### Comparison

Both frameworks use the same name and same concept. EOS-Native's version provides peer/host lifecycle callbacks that Fusion doesn't need (Fusion exposes those through `INetworkRunnerCallbacks` interface instead). The core pattern is identical: a way to participate in the network simulation loop without being a networked object.

---

## 9. Tick-Based Simulation & Prediction

### Fusion 2 Simulation Loop

Fusion runs a **discrete tick-based simulation** in consistent time steps:

1. **Tick rate** configured in Hz (e.g., 60 Hz = 1/60s per tick)
2. `FixedUpdateNetwork()` called on all NetworkBehaviours every tick
3. After each tick, server calculates, compresses, and broadcasts state deltas
4. **Decoupled from rendering** — tick rate != frame rate

**Server/Host flow:**
```
Tick N → FixedUpdateNetwork() on all objects → Snapshot N captured → Delta sent to clients
```

**Client flow (Host Mode):**
```
1. Receive server snapshot (Tick N)
2. Replace local predicted state with authoritative state
3. Resimulate from Tick N to current local tick (rollback + replay)
4. Continue predicting forward from current tick
```

**Client flow (Shared Mode):**
```
1. Receive state from all authority peers
2. Apply to remote objects (no resimulation needed)
3. Interpolate between snapshots for rendering
4. Write own state for owned objects
```

### Prediction & Resimulation (Host/Server Mode Only)

- Clients predict using local input + last known state
- Server is always N ticks ahead of client's confirmed state
- When server snapshot arrives, client **rolls back** to that tick, applies server state, then **resimulates** forward through all ticks to catch up
- `FixedUpdateNetwork()` runs during both forward ticks and resimulation ticks
- `Runner.IsResimulation` flag tells code if currently in resimulation

### Render Interpolation (All Modes)

- `Render()` callback runs every frame (not every tick)
- Interpolates between two most recent snapshots for smooth visuals
- Adaptive algorithm adjusts buffering based on network conditions
- In Fusion 2, NetworkTransform interpolates the Unity Transform directly (no separate interpolation target needed, unlike Fusion 1)
- `TryGetSnapshotsBuffers()` for custom interpolation

### EOS-Native Tick Simulation

- `TickSimulation` component on NetworkManager
- Configurable tick rate
- `OnTick` event for game logic
- `CurrentTick` / `FixedTickTime` accessible via `InstanceFinder`
- NetworkPrediction component (v2.31.0): records state into `StateHistory` ring buffer, `ApplyCorrection()` for authoritative corrections with visual smoothing
- **No resimulation** — closer to Fusion Shared Mode model
- LagCompensation static utility: rewind all tracked objects, sync physics, execute callback, restore

---

## 10. Lag Compensation in Fusion 2

**Available only in Host/Server Mode.** Not available in Shared Mode.

### How It Works

1. Fusion keeps a **history buffer** of hitbox positions over time
2. When a player fires, the server knows their latency
3. Server **temporarily rewinds** hitboxes to where they were from that player's perspective
4. Performs hit detection against **historical positions**
5. Restores current state

### Sub-Tick Accuracy

Fusion goes beyond discrete tick matching — it interpolates between ticks using the exact interpolation factor the player had when providing input. This delivers precision between network updates.

### Components

**HitboxRoot:**
- Container on topmost node of a networked object
- Groups all child Hitbox components
- Defines bounding sphere for broad-phase filtering

**Hitbox:**
- Individual collision volume
- Max 31 per HitboxRoot
- Uses GameObject's layer for filtering
- Supports dynamic layer changes

### API

```csharp
Runner.LagCompensation.RaycastAll(
    transform.position,
    transform.forward,
    rayLength,
    player: Object.InputAuthority,  // Whose perspective to use
    hits,
    layerMask,
    clearHits: true
);
```

Query types: Raycast, RaycastAll, Sphere/Box Overlaps.

### Filtering

- **Layer Mask** — Standard Unity layers
- **IgnoreInputAuthority** — Auto-exclude querying player's own hitboxes
- **Preprocessing Delegate** — Custom filtering before hit detection

### EOS-Native Lag Compensation

- `LagCompensation` static utility class
- `Compensate(rttMs, callback)` — Rewinds all `NetworkPrediction` tracked objects, syncs physics, executes callback, restores via `finally`
- `StateHistory` ring buffer with interpolated lookup
- Simpler than Fusion (no HitboxRoot/Hitbox component system, uses Unity physics directly)
- Works in any mode (not restricted to host/server)

---

## 11. Prefab Registration & Spawning

### PUN

```csharp
// Prefab must be in Resources/ folder
PhotonNetwork.Instantiate("PlayerPrefab", position, rotation);

// For scene objects:
PhotonNetwork.InstantiateRoomObject("SceneItem", pos, rot);
```

- Prefabs must be in `Resources/` folder (or use custom IPunPrefabPool)
- String-based prefab lookup
- Room objects persist when owner leaves

### Fusion 2

```csharp
Runner.Spawn(prefabReference, position, rotation, inputAuthority);
```

- Prefabs auto-registered via `NetworkPrefabTable` (builds at compile time)
- `Rebuild Prefab Table` menu command scans all NetworkObject prefabs
- Runtime registration via `NetworkPrefabTable.TryAdd()`
- `NetworkObjectProvider` interface for custom instantiation (pooling, addressables, procedural)
- On State Authority: `Spawned()` called immediately after `Runner.Spawn()`
- On non-authority: object spawned when state arrives for unknown NetworkId

### EOS-Native

```csharp
NetworkManager.Instance.Spawn(prefabId: 0, position, rotation);
```

- `NetworkPrefabTable` ScriptableObject (v2.36.0) — list of GameObjects, index = PrefabId
- Auto-registered in `NetworkManager.OnEnable()` before router subscription
- Runtime API: `AddPrefab()`, `RemovePrefabAt()`
- Also supports runtime `RegisterPrefab(prefab, id)` for dynamic registration
- `RegisterExisting()` for scene objects with deterministic IDs

### Comparison

| Feature | PUN | Fusion 2 | EOS-Native |
|---------|-----|----------|------------|
| Registration | Resources/ folder | Auto-scan + NetworkPrefabTable | ScriptableObject table |
| Lookup | String name | GUID / NetworkPrefabId | Integer index |
| Runtime Add | IPunPrefabPool | TryAdd() | AddPrefab() / RegisterPrefab() |
| Scene Objects | InstantiateRoomObject | NetworkObject in scene | RegisterExisting() / RegisterSceneObjects() |
| Custom Provider | IPunPrefabPool | INetworkObjectProvider | No (direct Instantiate) |
| Pooling | Manual | Built-in provider | No built-in pooling |

---

## 12. Comparison: EOS-Native vs Photon

### Architecture Mapping

| Concept | PUN | Fusion 2 | EOS-Native |
|---------|-----|----------|------------|
| Network Identity | PhotonView | NetworkObject | NetworkObject |
| State Sync | OnPhotonSerializeView | [Networked] + IL weave | SyncVar\<T\> + dirty tracking |
| Game Logic Base | MonoBehaviourPun | NetworkBehaviour | NetworkBehaviour |
| Manager Base | N/A | SimulationBehaviour | SimulationBehaviour |
| RPC System | [PunRPC] + string invoke | [Rpc] + direct call | [NetRpc] + IL weave |
| Transform Sync | PhotonTransformView | NetworkTransform | NetworkTransform |
| Animator Sync | PhotonAnimatorView | NetworkMecanimAnimator | NetworkAnimator |
| No-Code Sync | N/A | N/A | EasySync (Normcore-style) |
| Prefab Table | Resources/ | NetworkPrefabTable | NetworkPrefabTable |
| Change Callbacks | N/A | [OnChangedRender] | SyncVar.OnChanged |
| Tick Timer | PhotonNetwork.Time | TickTimer | TickSimulation.CurrentTick |
| Interest Mgmt | Interest Groups (256) | AOI + Explicit + Global | SpatialHashGrid |
| Lag Compensation | None | HitboxRoot + Hitbox | LagCompensation static |
| Prediction | None | Full rollback + resim | NetworkPrediction (ring buffer) |
| Connection | Cloud relay | Cloud relay / Direct P2P | EOS P2P (NAT punch) |
| Session | Photon Room | Fusion Session | EOS Lobby |

### Where EOS-Native Aligns with Fusion Shared Mode

1. **Distributed authority** — Each player owns their objects
2. **No resimulation** — State is applied directly, no rollback
3. **Snapshot interpolation** — Remote objects interpolated between updates
4. **Host/master concept** — Deterministic host election for tiebreaking
5. **Per-player state authority** — Owner writes, others receive
6. **SimulationBehaviour** — Same name, same concept (managers without NetworkObject)
7. **NetworkPrefabTable** — ScriptableObject for prefab registration

### Where EOS-Native Differs

1. **No cloud relay** — Direct P2P through EOS SDK (NAT punch-through, relay fallback)
2. **SyncVar vs IL weaving** — Runtime registration vs compile-time code generation
3. **Manual serialization** — NetWriter/NetReader vs automatic buffer management
4. **Message routing** — MessageRouter with frame batching vs Fusion's internal protocol
5. **No per-behaviour replication filtering** — All SyncVars on an object ship together
6. **Packet fragmentation** — Manual (PacketFragmenter, 1170-byte limit) vs handled by Fusion
7. **No tick-aligned RPCs** — RPCs execute immediately, not on a specific tick
8. **Richer auth model** — `[NetRpc(Validated = true)]` for host-validated RPCs (Fusion doesn't have this)
9. **Nested NetworkObjects** — Full parent/child hierarchy with runtime reparenting (Fusion has limited nesting)
10. **EasySync** — Normcore-style no-code sync via Inspector checkboxes (unique to EOS-Native)

---

## 13. Key Patterns EOS-Native Could Adopt

### High Priority

**1. Per-Behaviour Replication Filtering (from Fusion)**
Currently EOS-Native ships all SyncVars on a NetworkObject together. Fusion's `ReplicateTo(PlayerRef)` per-NetworkBehaviour would allow selective replication. Example: sync Health to everyone but Inventory only to teammates.

**2. Object Interest Modes (from Fusion)**
Three modes per NetworkObject: AOI (spatial), Global (always), Explicit (manual). Currently EOS-Native only has spatial grid. Adding Global (skip interest checks for game managers) and Explicit (manually controlled visibility for fog-of-war) would be valuable.

**3. Tick-Aligned RPCs (from Fusion)**
Fusion's `TickAligned = true` delays RPC execution until the matching simulation tick. This ensures RPCs and state changes happen in the same tick context. Would improve determinism for gameplay-critical RPCs.

### Medium Priority

**4. NetworkString / Fixed Collections (from Fusion)**
Fusion has `NetworkString<_32>`, `NetworkArray<T>`, `NetworkDictionary<K,V>` with fixed capacities that fit in the state buffer. EOS-Native's SyncVar<string> and SyncList/SyncDictionary work but could benefit from fixed-capacity variants for bandwidth optimization.

**5. INetworkObjectProvider (from Fusion)**
Interface for custom object instantiation (pooling, addressables, procedural generation). EOS-Native currently uses direct `Instantiate()`. Adding a provider interface would enable object pooling without modifying NetworkManager.

**6. Change Callback Improvements (from Fusion)**
Fusion's `[OnChangedRender]` fires in the Render phase (interpolation context), not the simulation phase. EOS-Native's `SyncVar.OnChanged` fires immediately on set. Consider adding a render-phase callback for visual updates vs gameplay callbacks.

### Lower Priority / Research

**7. Compile-Time IL Weaving for SyncVars (from Fusion)**
Replacing runtime `Sync()` registration with compile-time code generation would eliminate dictionary lookups and improve serialization performance. Major architectural change but significant performance win.

**8. Sub-Tick Interpolation (from Fusion)**
Fusion interpolates between ticks using the exact interpolation factor. EOS-Native interpolates between received state snapshots. Sub-tick accuracy would improve hit registration in fast-paced games.

**9. Explicit State Authority Transfer Protocol (from Fusion)**
Fusion has `RequestStateAuthority()` / `ReleaseStateAuthority()` as a two-step protocol. EOS-Native has `TransferAuthority()` which is a direct assignment. The request/release pattern prevents race conditions.

**10. AllowStateAuthorityOverride Flag (from Fusion)**
Per-NetworkObject toggle for whether authority can be taken without current owner's consent. Useful for pickup items, interactive objects. Currently EOS-Native always requires explicit transfer.

---

## 14. Sources

### Photon PUN 2
- [PUN 2 Introduction](https://doc.photonengine.com/pun/current/getting-started/pun-intro)
- [RPCs and RaiseEvent](https://doc.photonengine.com/pun/current/gameplay/rpcsandraiseevent)
- [Synchronization and State](https://doc.photonengine.com/pun/current/gameplay/synchronization-and-state)
- [Interest Groups](https://doc.photonengine.com/pun/current/gameplay/interestgroups)
- [PUN Cheat Sheet (GitHub Gist)](https://gist.github.com/ssshake/86b4da6c31258a7188f7fef3dbaf1d26)

### Photon Fusion 2 — Core
- [Fusion 2 Introduction](https://doc.photonengine.com/fusion/current/fusion-2-intro)
- [Choose a Topology](https://doc.photonengine.com/fusion/current/fusion-choose)
- [Network Topologies](https://doc.photonengine.com/fusion/current/manual/network-topologies)
- [Network Object](https://doc.photonengine.com/fusion/current/manual/network-object)
- [Network Behaviour](https://doc.photonengine.com/fusion/current/manual/network-behaviour)
- [Networked Properties](https://doc.photonengine.com/fusion/current/manual/data-transfer/networked-properties)
- [Remote Procedure Calls](https://doc.photonengine.com/fusion/current/manual/data-transfer/rpcs)
- [Spawning](https://doc.photonengine.com/fusion/current/manual/spawning)
- [Network Runner](https://doc.photonengine.com/fusion/current/manual/network-runner)

### Photon Fusion 2 — Advanced
- [Network Simulation Loop](https://doc.photonengine.com/fusion/current/concepts-and-patterns/network-simulation-loop)
- [Lag Compensation](https://doc.photonengine.com/fusion/current/manual/advanced/lag-compensation)
- [Interest Management](https://doc.photonengine.com/fusion/current/manual/advanced/interest-management)
- [Interest Management Addon](https://doc.photonengine.com/fusion/current/addons/interest-management/overview)
- [SimulationBehaviour](https://doc.photonengine.com/fusion/current/manual/advanced/simulation-behaviour)
- [IL Weaving](https://doc.photonengine.com/fusion/current/manual/advanced/network-object-weaving)
- [TickTimer](https://doc.photonengine.com/fusion/current/manual/fusion-types/ticktimer)
- [Shared Mode Master Client](https://doc.photonengine.com/fusion/current/manual/shared-mode-master-client)
- [Advanced Spawning](https://doc.photonengine.com/fusion/current/manual/advanced/advanced-spawning)

### Photon Fusion 2 — Tutorials
- [Shared Mode Basics: Overview](https://doc.photonengine.com/fusion/current/tutorials/shared-mode-basics/overview)
- [Shared Mode: Network Properties](https://doc.photonengine.com/fusion/current/tutorials/shared-mode-basics/4-network-properties)
- [Shared Mode: RPCs](https://doc.photonengine.com/fusion/current/tutorials/shared-mode-basics/5-remote-procedure-calls)
- [Host Mode: Getting Started](https://doc.photonengine.com/fusion/current/tutorials/host-mode-basics/1-getting-started)
- [Host Mode: Prediction](https://doc.photonengine.com/fusion/current/tutorials/host-mode-basics/3-prediction)
- [Host Mode: Property Changes](https://doc.photonengine.com/fusion/current/tutorials/host-mode-basics/5-property-changes)
- [Host Mode: RPCs](https://doc.photonengine.com/fusion/current/tutorials/host-mode-basics/6-remote-procedure-calls)
- [Coming from PUN2](https://doc.photonengine.com/fusion/current/getting-started/migration/coming-from-pun2)

### Photon Fusion 2 — API Reference
- [NetworkBehaviour Class](https://doc-api.photonengine.com/en/fusion/current/class_fusion_1_1_network_behaviour.html)
- [SimulationBehaviour Class](https://doc-api.photonengine.com/en/fusion/current/class_fusion_1_1_simulation_behaviour.html)
- [NetworkPrefabTable Class](https://doc-api.photonengine.com/en/fusion/current/class_fusion_1_1_network_prefab_table.html)
- [NetworkRunner Class](https://doc-api.photonengine.com/en/fusion/current/class_fusion_1_1_network_runner.html)
