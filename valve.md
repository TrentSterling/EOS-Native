# Valve / Source Engine Networking Architecture

Reference material for building rollback/prediction in EOS-Native (shared authority P2P).

Sources: [Source Multiplayer Networking](https://developer.valvesoftware.com/wiki/Source_Multiplayer_Networking), [Latency Compensating Methods](https://developer.valvesoftware.com/wiki/Latency_Compensating_Methods_in_Client/Server_In-game_Protocol_Design_and_Optimization) (Yahn Bernier 2001), [Prediction](https://developer.valvesoftware.com/wiki/Prediction), [Lag Compensation](https://developer.valvesoftware.com/wiki/Lag_Compensation), [Usercmd](https://developer.valvesoftware.com/wiki/Usercmd), [Networking Entities](https://developer.valvesoftware.com/wiki/Networking_Entities), [PVS](https://developer.valvesoftware.com/wiki/PVS) (all Valve Developer Wiki), [Snapshot Interpolation](https://gafferongames.com/post/snapshot_interpolation/), [State Synchronization](https://gafferongames.com/post/state_synchronization/), [Snapshot Compression](https://gafferongames.com/post/snapshot_compression/) (Gaffer on Games), [Client-Side Prediction and Server Reconciliation](https://www.gabrielgambetta.com/client-side-prediction-server-reconciliation.html) (Gabriel Gambetta)

---

## 1. Tick System

Source runs a **fixed-timestep simulation** on the server. All game logic, physics, and input processing happen at discrete tick boundaries.

| Game | Tickrate | Timestep |
|------|----------|----------|
| CS:GO Matchmaking | 64 Hz | 15.625 ms |
| CS:GO FACEIT/ESEA | 128 Hz | 7.8125 ms |
| CS2 (all modes) | 64 Hz + sub-tick | 15.625 ms (sim), sub-ms input precision |
| TF2 | 66 Hz | ~15.15 ms |
| Left 4 Dead 2 | 30 Hz | 33.33 ms |

### Server Tick Processing Order

Each tick, the server executes in this exact order:

1. **Read incoming packets** -- dequeue UserCmds from all clients
2. **Execute UserCmds** -- `CBasePlayer::PlayerRunCommand()` processes buffered commands. Lag compensation wraps this step.
3. **Run physics simulation** -- PhysX step at tick delta
4. **Run Think functions** -- entity logic, game rules, AI
5. **Check win/loss conditions** -- game rules evaluate
6. **Build snapshots** -- capture entity state for clients in PVS
7. **Send snapshots** -- transmit delta-compressed updates

### Client Tick

The client also runs at the server's tickrate internally. `cl_cmdrate` (default 64) controls how many UserCmd packets per second are sent. Multiple ticks' worth of UserCmds can be batched into a single packet. `cl_updaterate` (default 64) controls desired snapshot receive rate.

**Key cvar:** `sv_tickrate` -- immutable after server start. The client discovers it and locks its simulation to match.

---

## 2. Client-Side Prediction

### The Problem

RTT of 50-100ms means the player's character responds 50-100ms late to every input. Unacceptable for FPS.

### The Solution

The client runs the **same movement code** as the server, using local input immediately. When the server confirms (or denies) the result, the client reconciles.

### Execution Flow

```
Client Frame:
  1. Sample input -> create CUserCmd #N
  2. Store CUserCmd #N in circular buffer
  3. Run prediction: apply CUserCmd #N to local player state
  4. Render predicted state (player sees instant response)
  5. Send CUserCmd #N to server (possibly batched with N-1, N-2)

Server receives CUserCmd #N:
  1. StartLagCompensation() -- rewind world
  2. PlayerRunCommand(cmd #N) -- execute identical movement code
  3. FinishLagCompensation() -- restore world
  4. Include resulting state in next snapshot

Client receives snapshot containing result of cmd #N:
  1. Compare server state vs predicted state for cmd #N
  2. If match: discard old prediction data, continue
  3. If mismatch: RESIMULATE from server state, replaying cmds N+1..current
```

### What Gets Predicted

- Player movement (position, velocity, ground state, crouch, jump)
- Weapon firing (ammo count, fire rate cooldown, spread seed)
- Grenade throwing (release timing)
- Use/interact actions

### What Does NOT Get Predicted

- Other players' movement (interpolated instead)
- World physics objects
- Game state (round timer, score)
- Hit registration (server-authoritative)

### Resimulation (Rollback)

On misprediction, the client must replay all unacknowledged commands against the corrected server state. This means:

- The movement code must be **deterministic given the same inputs and state**
- `random_seed` in CUserCmd is derived from command number, so spread patterns match
- Resimulation is invisible to the player (happens in one frame)

### Error Smoothing

Raw correction would cause visible teleporting. Source smooths it:

- `cl_smooth 1` -- enable prediction error smoothing (default on)
- `cl_smoothtime 0.1` -- smooth correction over 100ms
- Implementation: offset between predicted and corrected position is stored, then lerped to zero over `cl_smoothtime`
- The entity's render position = actual position + decaying error offset

---

## 3. Lag Compensation (Hitbox Rewinding)

### The Core Idea

When the server processes a shot, it rewinds all other players to where they were **when the shooter actually fired**, not where they are now. This makes hit detection feel responsive despite latency.

### Rewind Time Calculation

```
CommandExecutionTime = CurrentServerTime - PacketLatency - ClientViewInterpolation
```

Typically: `ServerTime - (RTT/2) - cl_interp` (100ms default interp).

### Implementation

```
Server processes fire command:
  1. StartLagCompensation(player, cmd)
     - Calculate target time from cmd.tick_count
     - For EVERY other player entity:
       a. Look up position history at target time
       b. Interpolate between two nearest history entries
       c. Teleport hitboxes to historical position
  2. Execute trace/ray for the shot against rewound hitboxes
  3. Apply damage if hit
  4. FinishLagCompensation()
     - Restore all entities to current positions
```

### Position History Buffer

- Server stores **1 second** of position history per entity (ring buffer)
- Each entry: `{ position, angles, simulation_time, hitbox_data }`
- At 64 tick: 64 entries per entity per second
- History is indexed by simulation time, not tick number

### Limits and Tradeoffs

- **sv_maxunlag** (default 1.0s) -- maximum rewind. High-ping players beyond this get no compensation.
- **Moving targets:** If a target was moving fast, you can shoot where they *were* and still hit. From the target's perspective, they get shot "around corners" or "after they're behind cover." This is the fundamental tradeoff -- favoring the attacker's experience.
- **cl_interp abuse:** Lower interp = less rewind needed = fairer. Source caps `cl_interp_ratio` to prevent exploits.

### What Gets Rewound

- Player bounding boxes and hitboxes (bones/skeletal positions)
- NOT world geometry, NOT physics objects, NOT projectiles

---

## 4. Entity Interpolation

### Why

Without interpolation, remote entities render only at snapshot positions (20-64 Hz), causing visible stuttering. Interpolation renders entities between two known good states.

### How

The client renders remote entities with a **deliberate delay** (default 100ms, `cl_interp`). This ensures there are always at least two snapshots available to interpolate between, even with one dropped packet.

```
RenderTime = CurrentClientTime - cl_interp

For each remote entity:
  Find snapshot A (before RenderTime) and B (after RenderTime)
  fraction = (RenderTime - A.time) / (B.time - A.time)
  RenderPosition = Lerp(A.position, B.position, fraction)
  RenderAngles = Slerp(A.angles, B.angles, fraction)
```

### Interpolation vs Extrapolation

- **Interpolation** (default): render between two known states. Always smooth. Cost: `cl_interp` ms of visual delay.
- **Extrapolation**: predict forward from last known state. Used only during packet loss (cap 0.25s). Rubber-bands on resume.
- Effective interp formula: `cl_interp = cl_interp_ratio / cl_updaterate`. At 64 tick ratio 1: 15.6ms. At ratio 2: 31.2ms.

---

## 5. Server-Authoritative Architecture

The server is the **single source of truth**. Clients send only inputs (CUserCmd), never state. Server validates all commands (speed caps, rate limits, position sanity). PVS filtering serves as partial anti-wallhack: enemy data behind walls is never sent. The prediction loophole: clients DO run game logic locally, so cheats can read predicted state -- PVS mitigates but doesn't eliminate this.

---

## 6. Input / Command System (UserCmd)

### CUserCmd Structure

```cpp
struct CUserCmd {
    int     command_number;    // sequential, for matching predictions
    int     tick_count;        // client tick when created
    QAngle  viewangles;        // pitch, yaw, roll
    float   forwardmove;       // +forward/-back (max +-450)
    float   sidemove;          // +moveleft/-moveright (max +-450)
    float   upmove;            // +moveup/-movedown (ladders, swim)
    int     buttons;           // bitmask: IN_ATTACK, IN_JUMP, IN_DUCK, etc.
    byte    impulse;           // impulse commands (flashlight, spray)
    int     weaponselect;      // weapon switch
    int     weaponsubtype;
    int     random_seed;       // derived from command_number for determinism
    short   mousedx;           // raw mouse delta X
    short   mousedy;           // raw mouse delta Y
    bool    hasbeenpredicted;  // client-side flag
};
```

### Command Flow

1. **Creation:** `IBaseClientDLL::CreateMove()` called once per tick. Input devices sampled. `C_BasePlayer::CreateMove()` populates the struct.
2. **Storage:** Circular buffer `CInput::m_pCommands[]` on client.
3. **Transmission:** `IBaseClientDLL::WriteUsercmdDeltaToBuffer()` delta-compresses commands. Multiple cmds batched per packet. Redundancy: recent unacknowledged cmds re-sent (handles packet loss).
4. **Reception:** `IGameServerClients::ProcessUsercmds()` decompresses deltas.
5. **Execution:** `CBasePlayer::PlayerRunCommand()` inside `GameFrame`. `CPlayerMove::ProcessMovement()` applies the movement.

### Delta Compression of Commands

Commands are delta-encoded: only fields that changed from the previous command are transmitted. Viewangle deltas are quantized. Button field uses bitmask XOR. This keeps per-tick command data to ~10-20 bytes.

### Command Buffer (Server)

The server maintains a small command buffer per player to smooth out jitter:
- `sv_maxcmdrate` -- max commands per second accepted
- `sv_mincmdrate` -- minimum (forces clients to send at least this often)
- If buffer runs empty: server extrapolates last command (player keeps moving in same direction)
- If buffer overflows: oldest commands dropped

---

## 7. Entity Snapshot System

### Snapshot Contents

A snapshot is a complete serialization of all networked entity state visible to a specific client. Contains:

- Entity index + serial number (handle)
- Class identifier (what DataTable to use for decode)
- Packed property data (only networked fields)

### DataTables and SendProps

Source uses a **property declaration system** (analogous to SyncVars):

```cpp
// Server-side declaration
IMPLEMENT_SERVERCLASS_ST(CMyEntity, DT_MyEntity)
    SendPropInt(SENDINFO(m_iHealth)),
    SendPropFloat(SENDINFO(m_flSpeed)),
    SendPropVector(SENDINFO(m_vecOrigin)),
END_SEND_TABLE()
```

- Only declared properties are networked
- `NetworkStateChanged()` flags an entity as dirty for next snapshot
- **SendProxy functions** can transform data before transmission (quantize, clamp, conditionally exclude)

### Delta Compression

Full snapshots are enormous. Source uses delta compression against a known baseline:

1. **Full snapshot:** Sent on connect and after heavy packet loss. Contains all entity state.
2. **Delta snapshot:** "This is snapshot #110 relative to baseline #100." Only changed properties since baseline are included.
3. **Acknowledgment:** Client ACKs received snapshots. Server tracks last ACK'd snapshot per client as the new baseline.
4. **Property-level deltas:** If only `m_iHealth` changed on entity #42, only that one property is in the delta.

### PVS (Potentially Visible Set) Filtering

The server only includes entities **visible to the client** in snapshots:

- BSP-based visibility: precomputed per-leaf visibility matrix
- Each bit in a byte buffer represents one visleaf cluster
- Server checks: is the entity's leaf visible from the client's leaf?
- Reduces snapshot size dramatically on complex maps
- Also serves as anti-wallhack: enemy data behind walls is simply never sent

### PAS and Bandwidth

**PAS (Potentially Audible Set):** Same concept for sound, looser than PVS (sound travels through walls). `sv_maxrate`/`sv_minrate` cap bytes/sec per client. If bandwidth-limited, server prioritizes by distance and change frequency. Static entities consume zero bandwidth after initial snapshot.

---

## 8. CS2 Sub-Tick System

CS2 (Source 2) replaced the traditional tick-locked input with **sub-tick precision**:

### How It Works

- Server still simulates at 64 Hz fixed timestep
- Client timestamps every input action with **sub-tick precision** (up to 1/128th of a tick, ~122 microseconds)
- Movement and shooting are no longer quantized to tick boundaries
- When server processes a tick, it reads all timestamped actions within that tick window and applies them at their precise sub-tick time

### Key Insight for P2P

Sub-tick is essentially **decoupling input sampling from simulation rate**. The simulation still runs at fixed timestep, but inputs carry fractional tick offsets. Shots, jumps, grenade releases all get sub-ms precision instead of being quantized to 15.6ms tick boundaries. Physics simulation remains tick-aligned at 64Hz. This is achievable in P2P by timestamping inputs with high-resolution time and applying them proportionally within the fixed tick.

---

## 9. Lessons for EOS-Native (Shared Authority P2P)

### What Source Does That We Cannot

| Source Feature | Why It Works | P2P Limitation |
|----------------|-------------|----------------|
| Server-authoritative movement | Single truth, server runs all movement | No dedicated server; each peer owns their objects |
| Lag compensation (hitbox rewind) | Server has ALL entity history | Peers only have their own history + received snapshots |
| Full PVS filtering | Server controls what each client sees | Each peer broadcasts to all; interest management approximates PVS |
| Anti-cheat validation | Server validates all inputs | Host validates, but host can cheat |

### What We CAN Adopt

**1. Fixed Timestep Tick (already have `TickSimulation`)**
- Match Source: decouple render from simulation. Run game logic at fixed rate (e.g., 60 Hz).
- UserCmds equivalent: each tick, sample input, store in buffer, send to authority.

**2. Input Buffering + Redundancy**
- Source sends last N unacknowledged commands in every packet.
- EOS-Native should do the same: each input packet contains cmds [lastAcked+1 .. current].
- Handles packet loss without explicit retransmission.

**3. Prediction for Owned Objects**
- Each peer predicts their own objects immediately (already doing this).
- On receiving authority corrections, resimulate from corrected state.
- Store state + input history per predicted object (ring buffer, 1 second).
- Resimulation: restore state to server snapshot, replay stored inputs forward.

**4. Interpolation for Remote Objects**
- Remote objects should render with deliberate delay (2-3 snapshots behind).
- Buffer incoming snapshots, interpolate between them.
- `NetworkTransform` already does lerp; add configurable interp delay.
- Fallback to extrapolation on packet loss (cap at 250ms like Source).

**5. Prediction Error Smoothing**
- Never snap-correct on misprediction. Store error offset, decay over ~100ms.
- Separates visual position from authoritative position during correction.

**6. Delta Compression (already have in `SyncVar`)**
- Source only sends changed properties. SyncVar dirty flags do this.
- Consider property-level deltas for `NetworkTransform` (only send changed axes).

**7. Command Timestamp for Lag Compensation**
- Even without full server rewind, the authority peer can compensate.
- When processing a remote hit claim, check: "at the claimed tick, was the target near the claimed position?"
- Store position history on authoritative objects. Validate claims against history.
- This is "lightweight lag compensation" -- not full hitbox rewind, but position-proximity validation.

**8. Interest Management = PVS Equivalent**
- `InterestManager` + `SpatialHashGrid` already approximates PVS.
- Key: don't send data the peer doesn't need. Bandwidth savings compound.

### Prediction Architecture for EOS-Native

```
Per-tick on owning peer:
  1. Sample input, create InputCmd { tick, buttons, axes, timestamp }
  2. Store InputCmd in ring buffer (size = 1s / tickInterval)
  3. Apply InputCmd to predicted state immediately
  4. Send InputCmd to authority (redundant: include last N unacked)
  5. Render predicted state

On receiving authority state for tick T:
  1. Compare authority state vs stored predicted state at tick T
  2. If close enough (within tolerance): discard old entries, done
  3. If misprediction:
     a. Set object state = authority state at tick T
     b. Replay InputCmds from T+1 to current tick
     c. Store visual error offset = old_render_pos - new_render_pos
     d. Decay offset over smoothTime (100ms default)

Authority peer processing remote inputs:
  1. Receive InputCmd batch from remote peer
  2. Validate (speed, rate, sanity)
  3. Apply to the peer's objects
  4. Broadcast resulting state in next snapshot
```

### Key Differences From Source

| Concern | Source (Dedicated Server) | EOS-Native (P2P Shared Authority) |
|---------|--------------------------|-----------------------------------|
| Who runs movement | Server only (clients predict) | Owner runs, host validates |
| Misprediction source | Server disagreeing with client | Authority peer disagreeing with owner |
| Lag comp scope | Server rewinds ALL entities | Authority peer rewinds owned entities only |
| Snapshot frequency | 20-64 Hz server to each client | Tick rate, peer to peer |
| Trust boundary | Server trusts nothing | Host semi-trusted; peers trust owner for movement |
| Physics authority | Server | Owner (or host for unowned) |

### Recommended Implementation Priority

1. **Input buffer + redundancy** -- eliminates most packet-loss stutter. Low complexity.
2. **Interpolation buffer for remote objects** -- smooths rendering. Medium complexity.
3. **Prediction error smoothing** -- visual polish on corrections. Low complexity.
4. **Full prediction + resimulation** -- store state history, replay on correction. High complexity. Requires deterministic simulation for owned objects.
5. **Lightweight lag compensation** -- position history validation for hit claims. Medium complexity. Critical for competitive FPS.
6. **Sub-tick input timestamps** -- decouple input precision from tick rate. Medium complexity. Nice-to-have for competitive.
