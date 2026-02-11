# Valve Networking Architecture: Research Document

> Research compiled 2026-02-10 for EOS-Native project reference.
> Covers Source Engine, CS:GO, and CS2 (Source 2) networking models.

---

## Table of Contents

1. [Overview and Historical Context](#1-overview-and-historical-context)
2. [Tick-Based Simulation Model](#2-tick-based-simulation-model)
3. [CS2 Subtick System](#3-cs2-subtick-system)
4. [Client-Server Data Flow](#4-client-server-data-flow)
5. [Client-Side Prediction](#5-client-side-prediction)
6. [Server Reconciliation and Rollback](#6-server-reconciliation-and-rollback)
7. [Entity Interpolation and Extrapolation](#7-entity-interpolation-and-extrapolation)
8. [Lag Compensation for Hit Detection](#8-lag-compensation-for-hit-detection)
9. [Key Networking Parameters](#9-key-networking-parameters)
10. [Dumb Terminal vs Smart Client](#10-dumb-terminal-vs-smart-client)
11. [Applying to Peer-to-Peer (EOS-Native)](#11-applying-to-peer-to-peer-eos-native)
12. [Sources](#12-sources)

---

## 1. Overview and Historical Context

Valve's networking architecture, first documented by Yahn Bernier in his 2001 paper "Latency Compensating Methods in Client/Server In-game Protocol Design and Optimization," is the foundational model for nearly all modern FPS multiplayer networking. Developed for Half-Life and its mods (Counter-Strike, Team Fortress Classic), it established the patterns that Source Engine, CS:GO, and CS2 all build upon.

The core philosophy: **the server is the single authoritative source of truth**, but the client is not a passive viewer -- it actively predicts, interpolates, and compensates to hide latency from the player.

The three pillars of the Valve networking model:

1. **Client-side prediction** -- the local player's movement is simulated immediately on the client
2. **Entity interpolation** -- remote entities are rendered slightly in the past, smoothly interpolated between server snapshots
3. **Lag compensation** -- the server rewinds time when processing shots to see what the shooting player actually saw

These three systems interact to create the illusion that latency does not exist, even on connections with 50-150ms round-trip time.

---

## 2. Tick-Based Simulation Model

### How Ticks Work

The Source engine simulates the game in **discrete time steps called ticks**. Each tick, the server:

1. Processes incoming user commands (usercmds) from all clients
2. Runs a physics simulation step
3. Checks game rules (round timers, win conditions, etc.)
4. Updates all object states
5. Decides if any client needs a world update
6. Takes a snapshot of the current world state if necessary

### Tick Rates

| Game | Default Tickrate | Tick Interval |
|------|-----------------|---------------|
| Half-Life 2 / Source | 66.666 ticks/sec | 15ms |
| CS:GO Matchmaking | 64 ticks/sec | 15.625ms |
| CS:GO FACEIT/ESEA | 128 ticks/sec | 7.8125ms |
| CS2 (Source 2) | 64 ticks/sec | 15.625ms |
| Team Fortress 2 | 66.666 ticks/sec | 15ms |
| Left 4 Dead 2 | 30 ticks/sec | 33.333ms |

The tickrate defines the temporal resolution of the simulation. At 64 tick, the smallest unit of game time is 15.625ms. Any action that happens between ticks is quantized to the nearest tick boundary -- this is the fundamental limitation that CS2's subtick system addresses.

### Tick vs Frame Rate

The server tickrate and the client's rendering frame rate are decoupled. A client running at 300 FPS still only sends and receives updates at the tickrate. The client's renderer interpolates between received states to produce smooth visuals regardless of tick timing.

### Why Tickrate Matters

Higher tickrates provide:
- **Finer temporal resolution** for hit detection and movement
- **Lower worst-case input delay** (max delay = one tick interval)
- **More accurate physics** simulation
- **Higher server CPU cost** (linear scaling)
- **Higher bandwidth** (more snapshots per second)

The tradeoff is always CPU/bandwidth cost vs simulation fidelity. CS:GO's competitive community strongly advocated for 128-tick because the 15.625ms quantization at 64-tick could cause "feeling" differences in movement techniques like bunny-hopping and in shot registration near tick boundaries.

---

## 3. CS2 Subtick System

### The Problem Subtick Solves

In traditional tick-based systems, if you click to fire between tick N and tick N+1, the game waits until tick N+1 to register the shot. Your crosshair may have moved in that interval. At 64 tick, this means up to 15.625ms of input quantization error -- your shot lands where your crosshair was at tick N+1, not where it was when you clicked.

### How Subtick Works

CS2 introduced a **subtick system** that operates on three principles:

1. **Input Timestamping**: When a player performs an action (shoots, moves, throws a grenade), the client records the exact timestamp of that action down to sub-millisecond precision. This timestamp is sent to the server alongside the action data.

2. **Server-Side Ordering**: The server receives all player actions for a tick, reads their timestamps, and processes them in chronological order within the tick. Actions at timestamp 1.3 ticks are processed before actions at timestamp 1.7 ticks, even though both arrive in the same tick's packet.

3. **Interpolated State Reconstruction**: For hit detection, the server can reconstruct the game state at the precise sub-tick moment of the shot, not just at tick boundaries.

### What Gets Subtick Treatment

Not all game systems use subtick precision:

| System | Subtick? | Notes |
|--------|----------|-------|
| Shooting / hit detection | Yes | Precise timestamp on fire action |
| Movement inputs | Yes | Position tracked between ticks |
| Grenade throws | Yes | Exact release timing matters |
| Player physics | No | Simulated at 64Hz tick boundaries |
| Recoil patterns | No | Hardcoded to match 128-tick CS:GO behavior |
| Grenade physics | No | Simulated at tick boundaries |

### Subtick vs 128-Tick

Valve's claim is that subtick at 64Hz provides equivalent or better precision than 128-tick for the things that matter most (shot registration, movement responsiveness) while keeping server CPU costs at the 64-tick level. The physics and recoil systems are tuned to behave identically to 128-tick CS:GO.

The community remains divided. Some professional players report feeling a difference, particularly in movement techniques that depend on tick-precise inputs. The debate continues as of 2026.

### Implications for Game Developers

Subtick is an engineering choice: you pay the complexity cost of timestamp-based input ordering in exchange for decoupling input precision from simulation rate. It is most valuable when:
- Server CPU is constrained (matchmaking at scale)
- Input precision matters more than physics precision
- You want to avoid the "128 tick or unplayable" community pressure

---

## 4. Client-Server Data Flow

### The Complete Data Path

```
CLIENT                                   SERVER
  |                                        |
  |  1. Sample input (keyboard/mouse)      |
  |  2. Create usercmd                     |
  |  3. Predict movement locally           |
  |  4. Buffer usercmd (circular buffer)   |
  |                                        |
  |  ---- usercmd packet (cl_cmdrate) ---> |
  |       [cmd_number, buttons, angles,    |
  |        forward, side, up, impulse]     |
  |                                        |
  |                    5. Receive usercmds  |
  |                    6. Execute usercmds  |
  |                       (PlayerRunCommand)|
  |                    7. Run physics step  |
  |                    8. Check game rules  |
  |                    9. Take snapshot     |
  |                   10. Delta-compress    |
  |                                        |
  |  <-- snapshot packet (cl_updaterate) - |
  |       [ack_number, delta entities,     |
  |        events, server tick]            |
  |                                        |
  | 11. Receive snapshot                   |
  | 12. Check prediction against server    |
  | 13. Reconcile if mismatch             |
  | 14. Interpolate remote entities        |
  | 15. Render frame                       |
  |                                        |
```

### Usercmd Structure (CUserCmd)

The usercmd is the atomic unit of client input. It contains:

- **command_number**: Sequential ID, monotonically increasing. Used for acknowledgment matching.
- **tick_count**: The client tick when this command was generated
- **viewangles**: Pitch, yaw, roll of the player's view
- **forwardmove / sidemove / upmove**: Movement input magnitudes
- **buttons**: Bitmask of pressed buttons (fire, jump, duck, use, etc.)
- **impulse**: Special command byte (weapon switch, etc.)
- **weaponselect / weaponsubtype**: Weapon switch data
- **mousedx / mousedy**: Raw mouse movement delta
- **random_seed**: Seed for prediction-deterministic random numbers (derived from command_number)

Usercmds are **delta-compressed** against the previous command before transmission. Typically 2+ usercmds are packed into a single UDP packet at the `cl_cmdrate` frequency.

### Delta Compression of Snapshots

The server does not send a full world state every update. Instead:

1. Each packet contains an **acknowledgment number** referencing the last snapshot the client confirmed receiving.
2. The server sends only the **delta** (differences) since that acknowledged snapshot.
3. If the client has not acknowledged any snapshot (game start or heavy packet loss), the server sends a **full snapshot**.
4. Entity data is encoded with **bit-level precision** -- a position change of 0.001 units takes fewer bits than a change of 100 units.

This dramatically reduces bandwidth. A typical Source game sends 20-66 snapshots per second, but most frames are small deltas.

### Acknowledgment Flow

Both directions carry acknowledgment numbers:
- **Client -> Server**: "I have received and processed snapshot #N"
- **Server -> Client**: "I have received and processed usercmds up to command #M"

The server uses the client's acknowledged snapshot number to compute the delta. The client uses the server's acknowledged command number to discard old prediction history that has been confirmed.

---

## 5. Client-Side Prediction

### The Problem

Without prediction, the client must wait a full round-trip time (RTT) to see the result of pressing a key. At 100ms RTT, pressing "forward" would take 100ms before you see your character move. This feels terrible.

### How It Works

The client runs **the same movement simulation code as the server**. When the player presses a key:

1. Client creates a usercmd
2. Client **immediately** runs the movement simulation locally using that usercmd
3. Client updates the local player's position/state to the predicted result
4. Client sends the usercmd to the server
5. Client stores the usercmd and resulting state in a **circular buffer**, indexed by command_number

The player sees instant response to their input. The server processes the same usercmd later and sends back the "official" result.

### What Gets Predicted

In Source engine, prediction covers:

- **Player movement**: Position, velocity, ground state, ducking, jumping, swimming
- **Weapon state**: Ammo counts, reload timing, fire rate timing, spread/recoil
- **View effects**: View punch (recoil camera kick), screen shakes
- **Sound effects**: Weapon fire sounds play immediately (marked as predicted)

### What Does NOT Get Predicted

- Other players' positions (these use interpolation instead)
- Physics objects (world props, ragdolls)
- Game rules (round start/end)
- Damage / health changes on other players

### The Prediction Contract

For prediction to work correctly:
1. The prediction code on the client must be **identical** to the server code (shared code / shared movement functions)
2. The random number generator must be **deterministic** given the same seed (usercmd `random_seed` derived from `command_number`)
3. State must be **fully determined** by the previous state plus the input -- no hidden server-side variables that the client doesn't know about

If these conditions are met, the client's prediction will match the server's result in the vast majority of cases.

### First-Time Prediction Guard

When the client re-simulates old commands during reconciliation, it must avoid re-triggering effects (sounds, particles, muzzle flashes). The client marks usercmds as "already predicted" and skips effect spawning on re-simulation. Effects only play on the **first** prediction of each command.

---

## 6. Server Reconciliation and Rollback

### Detecting Prediction Errors

When the client receives a server snapshot containing the authoritative result for a previously-predicted usercmd:

1. Client looks up the **command_number** the server has acknowledged
2. Client retrieves the **predicted state** it stored for that command_number from the circular buffer
3. Client compares the predicted state against the server's authoritative state
4. If they match (within tolerance): prediction was correct, discard old buffer entries
5. If they don't match: **prediction error** -- must reconcile

### The Reconciliation Process

When a prediction error is detected:

1. **Accept the server state** as the new ground truth at the acknowledged command's tick
2. **Re-simulate all subsequent commands** that the client has predicted but the server hasn't yet acknowledged
3. Each command is replayed using the same usercmd inputs but starting from the corrected server state
4. The final result becomes the new predicted state

```
Time:     cmd 100    cmd 101    cmd 102    cmd 103    cmd 104 (current)
Predicted: P100       P101       P102       P103       P104
Server:    S100 (received, != P100)

Reconciliation:
  Start from S100
  Re-simulate cmd 101 -> new P101'
  Re-simulate cmd 102 -> new P102'
  Re-simulate cmd 103 -> new P103'
  Re-simulate cmd 104 -> new P104' (new current position)
```

### When Prediction Errors Occur

Prediction errors are relatively rare with correct shared code, but they happen when:

- **Server rejects movement**: Anti-cheat detects invalid movement, server blocks it
- **Collision with server-side entities**: Another player's hitbox blocks movement, but the client didn't know their exact position
- **Server-side triggers**: Walking over a trigger that changes velocity (teleporter, push volume)
- **Floating point differences**: Different CPU architectures or compiler optimizations produce slightly different floating point results
- **Packet loss**: Client predicted multiple frames without any server correction, accumulating error

### Visual Impact

Small prediction errors (< 1 unit) are usually absorbed smoothly. Large errors cause visible "rubber banding" -- the player's view snaps to the corrected position. Good netcode minimizes the frequency and magnitude of these corrections.

---

## 7. Entity Interpolation and Extrapolation

### Entity Interpolation

Remote entities (other players, moving objects) are NOT predicted. Instead, they are rendered **in the past**, smoothly interpolated between two recently received server snapshots.

#### How It Works

1. The client maintains a buffer of recent snapshots for each entity
2. The **render time** is shifted back by the interpolation period (default 100ms)
3. At render time T, the client finds the two snapshots that bracket T
4. Position, rotation, and animation state are linearly interpolated between those two snapshots
5. The entity is rendered at the interpolated state

#### The Interpolation Period (Lerp)

The interpolation period is calculated as:

```
interpolation_period = max(cl_interp, cl_interp_ratio / cl_updaterate)
```

Default values:
- `cl_interp` = 0.1 (100ms)
- `cl_interp_ratio` = 2
- `cl_updaterate` = 64

With defaults: `max(0.1, 2/64)` = `max(0.1, 0.03125)` = **0.1 seconds** (100ms)

For competitive CS:GO at 128-tick:
- `cl_interp` = 0
- `cl_interp_ratio` = 1
- `cl_updaterate` = 128
- Result: `max(0, 1/128)` = **7.8ms** (one tick)

#### Why Render in the Past?

By rendering 100ms in the past, the client always has two snapshots to interpolate between, even if one packet is lost. With `cl_interp_ratio` = 2 at 64-tick (snapshots every 15.6ms), the 100ms buffer accommodates ~6 snapshots -- so even several consecutive lost packets don't cause visual glitches.

Reducing `cl_interp_ratio` to 1 means the buffer only covers one snapshot interval -- any single dropped packet causes interpolation to fail.

#### What Gets Interpolated

- Position (x, y, z)
- Rotation / view angles
- Animation sequence and frame
- Bone positions (for player models)
- Entity-specific networked variables (door open amount, health bar, etc.)

### Entity Extrapolation

When interpolation runs out of data (multiple consecutive dropped packets exhaust the buffer), Source falls back to **extrapolation**:

1. `cl_extrapolate` = 1 (enabled by default in most Source games)
2. The engine takes the entity's last known position and velocity
3. It linearly extrapolates forward: `predicted_pos = last_pos + velocity * time_since_last_snapshot`
4. Extrapolation is capped at `cl_extrapolate_amount` = **0.25 seconds** (250ms)
5. Beyond 250ms of packet loss, entities freeze at their last extrapolated position

Extrapolation is inherently less accurate than interpolation because it assumes constant velocity. A player who was running left and suddenly stops will be rendered continuing to run left until a new snapshot arrives. For this reason, it is used only as a fallback.

### Interpolation vs Prediction Summary

| Technique | Applied To | View Delay | Accuracy | Packet Loss Tolerance |
|-----------|-----------|------------|----------|----------------------|
| Prediction | Local player only | None (instant) | High (same physics code) | Medium (re-simulate) |
| Interpolation | Remote entities | lerp period (100ms default) | High (between known states) | High (buffered) |
| Extrapolation | Remote entities (fallback) | None | Low (assumes constant velocity) | Low (degrades fast) |

---

## 8. Lag Compensation for Hit Detection

### The Fundamental Problem

The local player is rendered at their **current predicted position** (present time). Remote players are rendered at their **interpolated position** (in the past by lerp amount). When the local player shoots at a visible enemy, the enemy is actually at a different position on the server right now.

Without lag compensation, the player must "lead" their shots to account for the enemy's interpolation delay plus their own network latency. This is unacceptable for a competitive FPS.

### Server-Side Lag Compensation (Hitbox Rewinding)

Valve's solution is **server-side rewinding**. When the server processes a shot:

1. **Calculate rewind time**: `rewind_time = client_latency + interpolation_period`
   - This is the total delay between what the player saw and the current server state
   - Capped by `sv_maxunlag` (default 1.0 second)

2. **StartLagCompensation()**: The server temporarily moves all compensated entities back to their positions at `current_server_time - rewind_time`
   - Uses the **LagRecord** history: a rolling buffer of positions, rotations, hitbox poses, and animation states for each player, stored for up to 1 second

3. **Perform the hit trace**: With entities rewound, the server fires the hitscan ray (or checks projectile collision) against the historical positions

4. **FinishLagCompensation()**: All entities are restored to their current positions

5. **Apply results**: If the rewound trace hit a player, damage is applied to that player at their current position

### The LagRecord Structure

For each compensated entity, the server maintains a linked list of historical states:

```
LagRecord {
    float           simulationTime;   // Server tick time of this record
    Vector          origin;           // Entity position
    QAngle          angles;           // Entity rotation
    float           animTime;         // Animation timestamp
    int             sequence;         // Animation sequence
    float           cycle;            // Animation cycle position
    // Hitbox data (bone transforms) for animation-accurate rewinding
}
```

Records older than `sv_maxunlag` seconds are discarded. The list typically contains 64-128 entries at 64-tick (1 second of history).

### What Gets Rewound

Starting with Alien Swarm (2010), Valve provides three levels of lag compensation detail:

1. **Position only**: Rewind entity origin and angles. Cheapest. Sufficient for large hitboxes.
2. **Position + Hitboxes**: Rewind entity position and reconstruct full bone/hitbox poses from animation state. Standard for player models. This is what CS:GO and CS2 use.
3. **On-demand hitbox**: Only rewind hitboxes for entities that a preliminary ray test indicates might be hit. Optimization for scenes with many compensated entities.

### The Attacker Advantage / Defender Disadvantage

Lag compensation inherently favors the attacker:

- **Attacker's perspective**: "I aimed at the enemy and clicked. The server confirmed my hit." This feels fair -- shots land where you aim.
- **Defender's perspective**: "I was behind cover when I died." This happens because the attacker's client showed you in the open (due to interpolation delay), and the server agreed with the attacker after rewinding.

This is called "peeker's advantage" in competitive FPS. The first player to peek a corner has an advantage proportional to the sum of both players' latencies plus interpolation. At 50ms ping each with 100ms interpolation:

```
peeker_advantage = attacker_latency + defender_latency + interpolation
                 = 25ms + 25ms + ~15ms ≈ 65ms (at minimum interp)
```

During this window, the peeker can see and shoot the defender before the defender's screen shows the peeker.

### Anti-Abuse: sv_maxunlag

`sv_maxunlag` (default 1.0 second) caps the maximum rewind. A player with 1500ms ping cannot rewind 1.5 seconds into the past. Beyond the cap, the server uses the oldest available LagRecord. This prevents extreme abuse but means high-latency players experience degraded hit registration.

### Interaction with Prediction and Interpolation

The lag compensation system explicitly accounts for the client's interpolation period:

```
total_rewind = current_server_time - usercmd.tick_count - half_rtt_latency
```

The `usercmd.tick_count` tells the server exactly which server tick the client was viewing when the shot was fired. The server subtracts this from the current time to get the rewind amount, automatically including both network latency and interpolation delay.

---

## 9. Key Networking Parameters

### Client-Side CVars

| CVar | Default | Description |
|------|---------|-------------|
| `cl_updaterate` | 64 | Requested snapshot rate from server (snapshots/sec). Cannot exceed server tickrate. |
| `cl_cmdrate` | 64 | Rate at which client sends usercmd packets to server (packets/sec). |
| `cl_interp` | 0.1 | Minimum interpolation period in seconds (100ms). |
| `cl_interp_ratio` | 2 | Interpolation period as multiple of update interval. |
| `cl_extrapolate` | 1 | Enable extrapolation as fallback when interpolation buffer runs out. |
| `cl_extrapolate_amount` | 0.25 | Maximum extrapolation duration in seconds. |
| `cl_predict` | 1 | Enable client-side prediction (0 = dumb terminal mode). |
| `cl_lagcompensation` | 1 | Enable client-side lag compensation indication (cosmetic). |
| `rate` | 786432 | Maximum bytes per second the client can receive. |

### Server-Side CVars

| CVar | Default | Description |
|------|---------|-------------|
| `sv_maxupdaterate` | 66 | Maximum snapshot rate server will send. |
| `sv_minupdaterate` | 10 | Minimum snapshot rate server will send. |
| `sv_maxcmdrate` | 66 | Maximum usercmd rate server accepts. |
| `sv_mincmdrate` | 10 | Minimum usercmd rate server accepts. |
| `sv_maxunlag` | 1.0 | Maximum lag compensation rewind in seconds. |
| `sv_maxrate` | 0 | Maximum bandwidth per client (0 = unlimited). |
| `sv_minrate` | 0 | Minimum bandwidth per client. |

### Competitive CS:GO Settings

For 128-tick competitive servers:
```
cl_cmdrate 128
cl_updaterate 128
cl_interp 0
cl_interp_ratio 1
rate 786432
```

This minimizes interpolation delay to one tick (7.8ms) and maximizes input/output rates. The tradeoff is zero tolerance for packet loss in the interpolation buffer.

### Bandwidth Calculation

Approximate bandwidth per client:
```
downstream = updaterate * average_snapshot_size
           = 64 * ~200 bytes (delta compressed)
           ≈ 12.8 KB/s = ~100 kbps

upstream   = cmdrate * average_usercmd_size
           = 64 * ~50 bytes (delta compressed)
           ≈ 3.2 KB/s = ~25 kbps
```

These are rough averages. Actual bandwidth varies dramatically based on scene complexity, number of players, and how much state is changing.

---

## 10. Dumb Terminal vs Smart Client

### The Spectrum

Game networking exists on a spectrum between two extremes:

**Pure Dumb Terminal** (Quake 1 model):
- Client sends inputs to server
- Server processes everything
- Server sends complete world state back
- Client just renders what server says
- **Pros**: Perfect server authority, simple client, impossible to desync
- **Cons**: All actions delayed by full RTT, unplayable at >50ms latency

**Pure Smart Client** (Peer-to-peer lockstep):
- Each client simulates the full game
- Inputs are exchanged directly between clients
- All clients must agree on state
- **Pros**: Zero input latency, distributed load
- **Cons**: Vulnerable to cheating, desync risk, must wait for slowest player

### Valve's Hybrid (Authoritative Server + Predictive Client)

Valve's model sits in the middle, leaning heavily toward server authority:

```
Server Authority: ████████████████████░░░ (~90%)
Client Prediction: ░░░░░░░░░░░░░░░░░░░██░ (~10%)
```

The server is **fully authoritative** over all game state. The client predicts only its own local player's movement and weapon state. Everything else comes from the server.

### Tradeoff Analysis

| Aspect | Dumb Terminal | Valve Hybrid | Full Prediction |
|--------|--------------|--------------|-----------------|
| Input latency | Full RTT | Near-zero (predicted) | Zero |
| Remote entity accuracy | Server-authoritative | Server-authoritative, interpolated | Predicted, may desync |
| Hit detection | Server-only | Server with lag compensation | Client-side or rollback |
| Cheat resistance | Maximum | Very high | Low (P2P) to medium |
| Bandwidth | High (full state) | Medium (delta compressed) | Low (inputs only) |
| CPU distribution | Server-heavy | Server-heavy | Distributed |
| Visual artifacts | None | Rare prediction corrections | Frequent rollback pops |
| Complexity | Low | High | Very high |

### Why Valve Chose This Model

1. **Competitive integrity**: Server authority prevents most cheating
2. **Acceptable latency hiding**: Prediction covers the local player (most important), interpolation smooths everyone else
3. **Scalable**: Server-side logic can be optimized independently of client
4. **Proven**: 25+ years of refinement from Half-Life through CS2

### The Cost

The defender disadvantage (peeker's advantage) is the inherent tradeoff. In any system where:
- The attacker predicts their own movement locally (sees around corners instantly)
- The defender sees the attacker with interpolation delay

...there will always be an asymmetry favoring the aggressor. This is a fundamental limitation, not a bug. The only way to eliminate it is to add input delay to the attacker (making the game feel sluggish) or to have zero latency.

---

## 11. Applying to Peer-to-Peer (EOS-Native)

### Architectural Differences

Valve's model assumes a **dedicated authoritative server**. EOS-Native uses **host-authoritative peer-to-peer** where one player's machine acts as both client and server. This changes several fundamental assumptions.

| Aspect | Valve Dedicated Server | EOS-Native P2P Host |
|--------|----------------------|---------------------|
| Server location | Data center (low ping to all) | Player's machine (0ms to self, variable to others) |
| Server CPU | Dedicated hardware | Shared with host's game rendering |
| Host advantage | None (separate machine) | 0ms latency for host player |
| Trust model | Server trusted, all clients untrusted | Host trusted, clients untrusted |
| Host failure | Rare (enterprise hardware) | Common (player leaves, Alt+F4) |
| Bandwidth | Symmetric high-speed | Asymmetric (host upload is bottleneck) |

### What Translates Directly

These Valve patterns work identically in host-authoritative P2P:

1. **Server-side simulation on the host**: The host runs the authoritative game loop at a fixed tickrate, just like a dedicated server. This is exactly what `NetworkManager.TickSimulation` does in EOS-Native.

2. **Usercmd-based input**: Clients send inputs, host processes them. This is the `[NetRpc]` system and `MSG_SYNC` / `MSG_RPC` messages.

3. **Delta state synchronization**: Host sends state updates to clients. This is the SyncVar / SyncList / SyncDictionary system. Delta compression applies identically.

4. **Client-side prediction for the local player**: The client can predict its own movement without waiting for the host. EOS-Native's `NetworkTransform` with `OwnerAuthority` is a form of this -- the owner moves immediately and the host validates.

5. **Entity interpolation for remote players**: Clients interpolate between received states for smooth rendering of other players. `NetworkTransform`'s interpolation settings serve this purpose.

### What Changes in P2P

#### The Host Advantage Problem

In a dedicated server model, all players have symmetric latency to the server. In P2P, the host has 0ms latency. This means:

- **Host's prediction is always correct** (no round-trip, no reconciliation needed)
- **Host sees all remote players with minimal delay** (only one-way latency, not RTT)
- **Clients have full RTT delay** for all interactions with the host

Mitigation strategies:
- **Input delay for the host**: Artificially add delay to the host's local prediction to match the average client latency. This makes the host "feel" like a client. Costly to the host's experience.
- **Client-side hit detection with host validation**: Let clients detect their own hits and have the host validate/reject. Reduces peeker's disadvantage for clients at the cost of potential client-side cheating.
- **Accept the asymmetry**: For casual games, the host advantage is often acceptable. For competitive games, it is not.

#### Lag Compensation in P2P

Hitbox rewinding works identically on the host as it would on a dedicated server. The host stores position history and rewinds when processing client shots. The key difference:

- **The host never needs to rewind itself** -- its own shots are processed at current time
- **Clients need full rewind** -- their shots must be rewound by their RTT + interpolation period

This exacerbates the host advantage for FPS hit detection. The host's shots always register at current positions while clients' shots must go through the rewind system.

For EOS-Native specifically, the current `NetworkManager` does not implement hitbox rewinding. Adding it would require:
1. Position history buffer on each `NetworkObject` (already partially addressed by `SyncVar` with change tracking)
2. A rewind-simulate-restore cycle when processing client RPCs marked as "lag compensated"
3. Timestamp data in the RPC to indicate when the client fired

#### Bandwidth Constraints

In P2P, the host's **upload bandwidth** is the bottleneck. A dedicated server might have 1 Gbps symmetric. A residential connection might have 10-50 Mbps upload.

For EOS-Native:
- The `PacketFragmenter` handles large messages by splitting them
- `CompressionEnabled` on `MessageRouter` reduces bandwidth with Deflate
- `SyncVarLOD` reduces update frequency for distant objects
- `InterestManager` with `SpatialHashGrid` prevents sending irrelevant updates

These are all good mitigations. Additional strategies:
- **Adaptive updaterate**: Reduce snapshot frequency per-client based on measured bandwidth
- **Priority-based updates**: Update nearby/visible entities more frequently than distant ones (the `InterestManager` already does this via spatial partitioning)
- **Variable quantization**: Reduce precision for less important data (e.g., 8-bit rotation for distant players vs 16-bit for nearby)

#### Host Migration

When the host disconnects in Valve's model, the match ends (or a new server takes over via matchmaking). In P2P with EOS-Native:

- `ReconnectGracePeriod` and `_hibernatedPeers` handle temporary disconnections
- `RecomputeHost()` and `OnHostChanged` enable dynamic host migration
- State must be synchronized to the new host, which is expensive and error-prone

This is one area where P2P is fundamentally more complex than dedicated servers.

### Recommended Architecture for EOS-Native

Based on Valve's patterns, adapted for P2P:

1. **Keep the host-authoritative model**: The host runs the tick simulation, processes all RPCs, owns the ground truth. This is already how EOS-Native works.

2. **Client-side prediction for owner-authority objects**: Objects with `OwnerAuthority = true` on `NetworkTransform` should predict locally and accept server corrections. This is partially implemented.

3. **Interpolation for remote objects**: All non-owned `NetworkTransform` objects should interpolate between received states. The `InterpolationSpeed` setting already supports this.

4. **Consider lag compensation for competitive games**: If EOS-Native is used for FPS games, implement server-side position history and rewind. This is the single biggest missing feature for competitive viability.

5. **Subtick-style input timestamping**: For competitive scenarios, attach precise timestamps to client inputs so the host can process them with sub-tick accuracy. This would improve hit registration without increasing tickrate (and CPU cost).

6. **Accept the host advantage for casual games**: For party games, co-op, and non-competitive titles, the host advantage is acceptable and not worth the complexity cost of mitigating.

### Summary: What to Take from Valve

| Valve Pattern | EOS-Native Status | Priority |
|--------------|-------------------|----------|
| Tick-based simulation | Implemented (TickSimulation) | Done |
| Client-side prediction | Partial (owner authority) | Medium |
| Server reconciliation | Not implemented | Medium |
| Entity interpolation | Implemented (NetworkTransform) | Done |
| Entity extrapolation | Not implemented | Low |
| Lag compensation / hitbox rewind | Not implemented | High (for FPS) |
| Delta compression | Partial (SyncVar change tracking) | Medium |
| Subtick input timestamping | Not implemented | Low-Medium |
| Interest management | Implemented (SpatialHashGrid) | Done |
| Bandwidth adaptation | Partial (SyncVarLOD) | Medium |

---

## 12. Sources

### Primary Valve Documentation
- [Source Multiplayer Networking - Valve Developer Community](https://developer.valvesoftware.com/wiki/Source_Multiplayer_Networking)
- [Latency Compensating Methods in Client/Server In-game Protocol Design and Optimization - Valve Developer Community (Yahn Bernier)](https://developer.valvesoftware.com/wiki/Latency_Compensating_Methods_in_Client/Server_In-game_Protocol_Design_and_Optimization)
- [Lag Compensation - Valve Developer Community](https://developer.valvesoftware.com/wiki/Lag_Compensation)
- [Prediction - Valve Developer Community](https://developer.valvesoftware.com/wiki/Prediction)
- [Interpolation - Valve Developer Community](https://developer.valvesoftware.com/wiki/Interpolation)
- [Usercmd - Valve Developer Community](https://developer.valvesoftware.com/wiki/Usercmd)
- [Networking Entities - Valve Developer Community](https://developer.valvesoftware.com/wiki/Networking_Entities)

### CS2 Subtick Analysis
- [CS2 Tick Rate, Subtick & Commands Explained - Turboboost.gg](https://turboboost.gg/article/cs2-tickrate-subtick-and-commands-explained)
- [CS2 Sub-tick Explained - Profilerr](https://profilerr.net/cs2-sub-tick-explained-how-does-it-work-and-is-it-better-than-128/)
- [CS2 Tick Rate & Subtick System: Full Guide - Skin.club](https://community.skin.club/en/articles/what-is-subtick-and-how-does-it-work)
- [CS2 Tick Rate Explained - DMarket](https://dmarket.com/blog/cs2-tick-rate/)
- [Subtick System In Counter-Strike 2: A Deep Dive - ExitLag](https://www.exitlag.com/blog/subtick-counter-strike-2/)

### Supplementary Networking References
- [Gabriel Gambetta - Fast-Paced Multiplayer Series](https://www.gabrielgambetta.com/client-server-game-architecture.html)
- [What Every Programmer Needs To Know About Game Networking - Gaffer On Games](https://gafferongames.com/post/what_every_programmer_needs_to_know_about_game_networking/)
- [Multiplayer Networking Resources - Curated List](https://multiplayernetworking.com/)
- [Netcode Concepts Part 2: Topology - Meseta](https://meseta.medium.com/netcode-concepts-part-2-topology-ad64f9f8f1e6)
- [Lag Compensation in FPS Games - Outscal](https://outscal.com/blog/lag-compensation-in-fps-games-the-hidden-systems-making-your-shots-count)
- [Steam Community Guide: Rates, Interp, & LERP](https://steamcommunity.com/sharedfiles/filedetails/?id=864504043)
- [Steam Community Guide: Technical Explications on Rate/Updaterate/Cmdrate/Interp](https://steamcommunity.com/sharedfiles/filedetails/?id=501119397)
- [Source Engine Lag Compensation Source Code (player_lagcompensation.cpp)](https://github.com/VSES/SourceEngine2007/blob/master/se2007/game/server/player_lagcompensation.cpp)
- [Valve GameNetworkingSockets (P2P library)](https://github.com/ValveSoftware/GameNetworkingSockets)

### Academic / Technical Papers
- [Yahn W. Bernier - Latency Compensating Methods (PDF)](https://www.gamedevs.org/uploads/latency-compensation-in-client-server-protocols.pdf)
- [A Survey and Taxonomy of Latency Compensation Techniques for Network Computer Games - ACM](https://dl.acm.org/doi/10.1145/3519023)
