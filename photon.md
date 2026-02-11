# Photon PUN2 & Fusion 2 Architecture Reference

Dense technical reference for improving EOS-Native networking. Focus on API patterns, authority models, and lessons applicable to peer-to-peer shared authority.

---

## 1. PUN2 Architecture

### Server Topology
- **Name Server** -> resolves region, returns Master Server address
- **Master Server** -> lobby, matchmaking, room listing. Does NOT relay game traffic.
- **Game Server** -> actual room host. All game traffic relays through this server.
- Clients NEVER connect directly to each other. All data passes through Photon Cloud relay.
- Master Client = first player in room. If they leave, next player promoted automatically.

### Connection Flow
```
Client -> Name Server (region) -> Master Server (lobby/matchmaking) -> Game Server (room)
```

### Authority Model
- **Distributed authority** (no central simulation). Each client owns their PhotonView objects.
- Master Client handles scene-level logic, room state, AI, etc.
- `photonView.IsMine` = local ownership check. `PhotonNetwork.IsMasterClient` = host check.
- Ownership transfer: `photonView.TransferOwnership(newOwner)` or `photonView.RequestOwnership()`.

---

## 2. PUN2 Room/Lobby System

### Room Properties
```csharp
RoomOptions opts = new RoomOptions();
opts.MaxPlayers = 8;
opts.IsVisible = true;           // visible in lobby listing
opts.IsOpen = true;              // accepting joins
opts.PlayerTtl = 30000;          // ms before inactive player removed
opts.EmptyRoomTtl = 0;          // ms room stays after last player leaves
opts.CustomRoomProperties = new Hashtable { {"map", "arena"}, {"mode", "tdm"} };
opts.CustomRoomPropertiesForLobby = new string[] { "map", "mode" }; // exposed to lobby filter
PhotonNetwork.CreateRoom("RoomName", opts);
```

### Custom Properties (Hashtable)
- Room: `PhotonNetwork.CurrentRoom.SetCustomProperties(hashtable)` -- delta update only changed keys
- Player: `PhotonNetwork.LocalPlayer.SetCustomProperties(hashtable)`
- Read: `room.CustomProperties["key"]`, `player.CustomProperties["key"]`
- Lobby filtering: `JoinRandomRoom(expectedProps, maxPlayers)` -- only rooms matching props

### Matchmaking
- `JoinRandomRoom()` -- server picks random matching room
- `JoinOrCreateRoom()` -- join if exists, create if not (atomic)
- SQL lobby: advanced filtering with `SqlLobby` type + `WHERE` clauses on properties

---

## 3. PUN2 RPC System

### RpcTarget Enum
| Value | Behavior |
|-------|----------|
| `All` | Execute on all clients including sender |
| `Others` | All except sender |
| `MasterClient` | Only master client |
| `AllBuffered` | All + cached for late joiners |
| `OthersBuffered` | Others + cached for late joiners |
| `AllViaServer` | Routed through server (ordered) |
| `AllBufferedViaServer` | Ordered + cached |

### API
```csharp
[PunRPC]
void TakeDamage(int amount, PhotonMessageInfo info) {
    // info.Sender, info.photonView, info.SentServerTimestamp
}
photonView.RPC("TakeDamage", RpcTarget.All, 25);
```

### Rules
- Method must be on same GameObject as PhotonView. Not static, not generic.
- Parameters must be PUN-serializable (primitives, Vector3, Quaternion, byte[], etc.).
- Buffered RPCs accumulate on server -- long buffer = slow join. Use sparingly.
- RPCs lost if receiver hasn't loaded the scene yet. Use `AutomaticallySyncScene`.

### RaiseEvent (alternative)
- `PhotonNetwork.RaiseEvent(eventCode, data, options, sendOptions)` -- no PhotonView needed
- Event codes 0-199 for custom use. Interest groups for pub/sub. Cacheable for late joiners.

---

## 4. PUN2 Serialization

### IPunObservable
```csharp
public class MySync : MonoBehaviourPun, IPunObservable {
    public void OnPhotonSerializeView(PhotonStream stream, PhotonMessageInfo info) {
        if (stream.IsWriting) {       // owner sends
            stream.SendNext(health);
            stream.SendNext(transform.position);
        } else {                      // remote receives
            health = (int)stream.ReceiveNext();
            networkPos = (Vector3)stream.ReceiveNext();
        }
    }
}
```

### Sync Modes & Rates
- **Reliable Delta Compressed** / **Unreliable** / **Unreliable On Change** / **Off**
- `SerializationRate` (default 10/sec) -- OnPhotonSerializeView frequency
- `SendRate` (default 20/sec) -- message dispatch frequency. Independent of frame rate.

---

## 5. Photon Fusion Modes

### Shared Mode (most relevant to EOS-Native)
- **GameMode.Shared** -- Photon Cloud Room holds authoritative state copy
- Each client has **StateAuthority** over objects they spawn
- Authority transferable: `Object.RequestStateAuthority()` (request, not assign)
- `AllowStateAuthorityOverride` flag on NetworkObject controls transfer policy
- **No prediction/rollback** -- state changes apply immediately on authority, replicate to others
- **No lag compensation** -- not available in shared mode
- InputAuthority == StateAuthority in shared mode (same concept)
- Tick rate limited to 32 Hz, send rate to 16 Hz

### Host Mode
- **GameMode.Host** (host) + **GameMode.Client** (players)
- Host has exclusive StateAuthority over ALL objects
- Clients send input, host simulates, clients predict + rollback on mismatch
- Full prediction, lag compensation, re-simulation
- Host is both server and player (has local input)

### Server Mode (Dedicated)
- **GameMode.Server** + **GameMode.Client**
- Same as Host Mode but server has no local player
- `PlayerRef.None` is StateAuthority for all objects
- Headless Unity build

### Feature Matrix
| Feature | Shared | Host | Server |
|---------|--------|------|--------|
| Client-side prediction | No | Yes | Yes |
| Lag compensation | No | Yes | Yes |
| Re-simulation | No | Yes | Yes |
| Any client can spawn | Yes | No (host only) | No (server only) |
| Authority transfer | Yes | N/A | N/A |
| Max tick rate | 32 Hz | 64 Hz | 64 Hz |
| Requires server binary | No | No | Yes |

---

## 6. Fusion NetworkObject / NetworkBehaviour

### NetworkObject
- One per networked entity (equivalent to PhotonView in PUN)
- Assigned unique `NetworkId` (uint) by server/cloud on spawn
- Contains state buffer for all child NetworkBehaviours
- Flags: `AllowStateAuthorityOverride`, `DestroyWhenStateAuthorityLeaves`
- Nested NetworkObjects supported -- each independent entity with own NetworkId
- Scene objects: auto-attached when scene loads, master client gets authority

### NetworkBehaviour
- Must be on same GameObject (or child) as NetworkObject
- Override `FixedUpdateNetwork()` for simulation, `Render()` for visuals
- Authority checks: `HasStateAuthority`, `HasInputAuthority`, `IsProxy`
- Lifecycle: `Spawned()` -> simulation -> `Despawned()`
- Cannot access `[Networked]` properties before `Spawned()`

### Spawning
```csharp
// Shared mode -- any client can spawn
Runner.Spawn(prefab, position, rotation, inputAuthority);

// Host mode -- only host can spawn
Runner.Spawn(prefab, position, rotation, inputAuthority);

// Despawn (only StateAuthority can call)
Runner.Despawn(networkObject);
```

### Prefab Registration
- Automatic: Fusion scans project for prefabs with NetworkObject
- Manual: NetworkProjectConfig > Prefab Table
- Each prefab gets a unique `NetworkPrefabId` at edit time

---

## 7. Fusion [Networked] Properties (SyncVars)

### Syntax
```csharp
public class PlayerBehaviour : NetworkBehaviour {
    [Networked] public int Health { get; set; }
    [Networked] public NetworkBool IsAlive { get; set; }
    [Networked] public Vector3 Velocity { get; set; }

    // Collections require [Capacity]
    [Networked, Capacity(32)]
    public NetworkArray<int> Inventory { get; }

    [Networked, Capacity(8)]
    public NetworkDictionary<PlayerRef, int> Scores { get; }

    [Networked, Capacity(64)]
    public NetworkString<_64> PlayerName { get; set; }
}
```

### Supported Types
- Primitives: byte, int, float, double, long, etc.
- Unity: Vector2/3/4, Quaternion, Color, Bounds, Matrix4x4, Rect
- Fusion: NetworkBool, NetworkString, PlayerRef, TickTimer, NetworkId, Angle
- Collections: NetworkArray<T>, NetworkDictionary<K,V>, NetworkLinkedList<T>
- Custom: any blittable struct implementing INetworkStruct
- Enums, System.Guid

### Change Detection -- OnChangedRender
```csharp
[Networked, OnChangedRender(nameof(OnHealthChanged))]
public int Health { get; set; }

void OnHealthChanged() {
    healthBar.SetValue(Health);  // runs in Render, not simulation
}
```
- Runs during Render frame (Unity Update), NOT FixedUpdateNetwork
- NOT called on first spawn -- must initialize manually in `Spawned()`
- May fire multiple times per frame (re-simulation) or skip if value bounces back

### Change Detection -- ChangeDetector (advanced)
```csharp
private ChangeDetector _changeDetector;
public override void Spawned() {
    _changeDetector = GetChangeDetector(ChangeDetector.Source.SimulationState);
}
public override void Render() {
    foreach (var change in _changeDetector.DetectChanges(this)) {
        switch (change) {
            case nameof(Health): OnHealthChanged(); break;
        }
    }
}
```
- More flexible -- works in FixedUpdateNetwork or Render
- Can compare old vs new values

---

## 8. Fusion Tick System & Prediction

### Simulation Loop
```
Unity Update() {
    Fusion BeforeUpdate callbacks
    while (pendingTicks > 0) {
        FixedUpdateNetwork()        // ALL NetworkBehaviours
        // State captured as snapshot
        pendingTicks--
    }
    Render()                        // interpolation between snapshots
}
```

### Tick Configuration
- Tick rate in `NetworkProjectConfig > Simulation > Tick Rate` (Hz)
- `Runner.DeltaTime` = 1/tickRate (fixed timestep, NOT Time.deltaTime)
- `Runner.Tick` = current simulation tick number

### Prediction (Host/Server Mode Only)
1. Client reads local input, stamps with current tick
2. Client simulates locally (prediction) using that input
3. Input sent to server. Server simulates authoritatively.
4. Server state arrives at client (past tick). Client compares with prediction.
5. If mismatch: rollback to server state, re-simulate forward with stored inputs.
6. Smooth -- most predictions correct, rollback rare.

### Prediction in Shared Mode
- Minimal. Authority changes apply immediately, no rollback.
- Remote objects: receive snapshots, interpolate in Render().
- No input prediction for non-owned objects.

### Render Interpolation
- `Render()` runs every Unity frame (decoupled from tick rate)
- Interpolates between two recent snapshots for smooth visuals
- `Runner.InterpFrom` / `Runner.InterpTo` / `Runner.InterpAlpha`
- Proxies (non-authority) always rendered via interpolation

---

## 9. Fusion Interest Management

### Area of Interest (AOI)
```csharp
// Define player's view region (per frame)
Runner.AddPlayerAreaOfInterest(playerRef, position, radius);
```
- Objects outside all players' AOI regions: no state updates sent
- Requires `NetworkTRSP` component on NetworkObject
- Shared Mode: one AOI region per player. Host/Server: multiple regions.
- Cell-based spatial hashing under the hood.

### Object Interest Modes
| Mode | Behavior |
|------|----------|
| **AreaOfInterest** | Position-based culling (default) |
| **Global** | Always replicated to all players |
| **Explicit** | Only to players flagged via `SetPlayerAlwaysInterested()` |

### AreaOfInterestOverride
- Parented NetworkObjects use parent's position for AOI by default
- `AutoAOIOverride` on NetworkTransform handles this automatically
- Ensures child objects (carried items, etc.) share parent's interest

### Additional Features
- `ReplicateTo(PlayerRef)` override -- per-behaviour filtering (Host/Server only, NOT Shared)
- `IInterestEnter` / `IInterestExit` callbacks when interest changes

---

## 10. Key Differences: PUN2 vs Fusion

| Aspect | PUN2 | Fusion 2 |
|--------|------|----------|
| State model | Distributed, each client authoritative | Tick-based snapshots, authority-gated |
| Sync mechanism | `OnPhotonSerializeView` stream | `[Networked]` IL-weaved properties |
| RPCs | String-based `photonView.RPC("name")` | Attribute `[Rpc]` with compile-time checks |
| Buffered RPCs | Yes (`RpcTarget.*Buffered`) | No -- use [Networked] for persistence |
| Prediction | None | Full (Host/Server), None (Shared) |
| Lag compensation | None | Hitbox rollback (Host/Server) |
| Tick system | None (frame-based) | Deterministic tick simulation |
| Interest mgmt | Interest groups (manual) | Spatial AOI (automatic) |
| Matchmaking | `PhotonNetwork.JoinRoom()` | `Runner.StartGame(startArgs)` |
| Late join state | Buffered RPCs + serialization | Full snapshot from authority |
| Status | Maintenance/LTS (no new features) | Active development |
| Topology | Relay only (Photon Cloud) | Shared/Host/Dedicated Server |

### Key Insight
PUN2 is event-driven (RPCs + serialization callbacks). Fusion is state-driven ([Networked] properties as source of truth, RPCs for transient events only). This is the same philosophical shift EOS-Native made with SyncVars as primary state + RPCs for actions.

---

## 11. Lessons for EOS-Native

### Already Adopted (good parity)
- **Per-object authority** -- matches Fusion Shared Mode exactly
- **SyncVar as source of truth** -- matches [Networked] properties philosophy
- **No buffered RPCs** -- correct; use SyncVar snapshots for late-join state
- **NetworkObject/NetworkBehaviour split** -- mirrors Fusion architecture
- **Prefab table registration** -- mirrors Fusion's NetworkPrefabId system
- **Spatial interest management** -- SpatialHashGrid parallels Fusion AOI
- **Nested NetworkObjects** -- supported in both, similar authority independence

### Worth Adopting
1. **OnChangedRender pattern** -- Fusion separates simulation change from render-time visual update. EOS-Native `SyncVar.OnValueChanged` fires immediately; consider a Render-phase change detection option for visual-only callbacks.
2. **ChangeDetector iteration** -- Fusion's `foreach (var change in detector.DetectChanges())` is elegant for batch processing. Consider similar API for NetworkBehaviour.
3. **AreaOfInterestOverride for parented objects** -- automatic AOI inheritance for child NetworkObjects. EOS-Native should ensure reparented objects inherit parent's interest region.
4. **RequestStateAuthority()** -- Fusion's request-based authority transfer (not assign). EOS-Native `TransferAuthority` is direct assignment. Consider adding a request flow with approval callback for competitive scenarios.
5. **ISpawned/IDespawned interfaces** -- Fusion uses interfaces for lifecycle callbacks, separating them from MonoBehaviour lifecycle. Cleaner than relying on OnEnable/OnDisable for network events.
6. **Deterministic tick agreement** -- Fusion Cloud Room maintains global tick. EOS-Native ticks are local-only. For shared mode, consider a lightweight tick sync protocol (host broadcasts tick, peers offset).

### Fusion Shared Mode Limitations EOS-Native Already Surpasses
- **Client-side prediction** -- Fusion Shared has NONE. EOS-Native has it (v2.31.0).
- **Tick rate** -- Fusion Shared capped at 32 Hz. EOS-Native uncapped.
- **No relay dependency** -- EOS-Native uses EOS P2P (NAT punchthrough + relay fallback). Fusion always routes through Photon Cloud. Lower latency when punchthrough succeeds.
- **No CCU billing** -- EOS is free. Photon charges per CCU.

### Anti-Patterns to Avoid (from PUN2)
- **Buffered RPCs** -- accumulate on server, slow joins, stale state. SyncVar snapshots are strictly better.
- **String-based RPC dispatch** -- `photonView.RPC("MethodName")` is error-prone. Attribute + IL weaving (as EOS-Native does with [NetRpc]) is correct.
- **Frame-based serialization** -- PUN's `OnPhotonSerializeView` runs at serialization rate regardless of changes. Dirty-flag SyncVars are more efficient.
- **Hashtable custom properties** -- type-unsafe, allocation-heavy. EOS lobby attributes (typed) are better.

### API Patterns Worth Studying
```csharp
// Fusion: clean authority guard
public override void FixedUpdateNetwork() {
    if (!HasStateAuthority) return;
    // only authority simulates
}

// Fusion: input-driven prediction
if (GetInput(out NetworkInputData input)) {
    rb.AddForce(input.direction * speed);
}

// Fusion: spawn with authority assignment
var obj = Runner.Spawn(prefab, pos, rot, inputAuthority: player);

// Fusion: targeted RPC
[Rpc(RpcSources.InputAuthority, RpcTargets.StateAuthority)]
public void RpcRequestAction(ActionType action) { }
```

---

*Sources: [PUN2 Docs](https://doc.photonengine.com/pun/current/), [Fusion 2 Docs](https://doc.photonengine.com/fusion/current/), [Fusion Shared Mode](https://doc.photonengine.com/fusion/current/tutorials/shared-mode-basics/overview), [Fusion Interest Mgmt](https://doc.photonengine.com/fusion/current/manual/advanced/interest-management), [PUN2 to Fusion Migration](https://doc.photonengine.com/fusion/current/getting-started/migration/coming-from-pun2)*
