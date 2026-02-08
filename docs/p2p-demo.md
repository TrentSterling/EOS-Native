# P2P Ball Demo

A simple multiplayer demo using the raw EOS P2P interface. Players control a ball with WASD, can jump, and collide with each other. Positions sync across peers using spring physics. No FishNet or other high-level transport is required.

Built on the [P2P Transport Toolkit](p2p-transport.md) (Layer 1) with optional [Layer 2](architecture.md) integration via `DemoBallBehaviour`.

## Overview

The demo uses a **peer-authority model**: each player owns their ball. Local physics runs normally through Unity's Rigidbody system. Remote balls are guided toward received positions via damped spring forces, producing smooth networked movement without jitter.

### Files

| File | Location | Description |
|------|----------|-------------|
| EOSP2PManager | `P2P/` | Singleton P2P mesh manager (auto-accept, send/receive, lobby integration, MessageRouter) |
| NetWriter | `P2P/` | Binary serializer with auto-growing buffer, packed ints, pooling |
| NetReader | `P2P/` | Binary deserializer with bounds checking |
| PacketFragmenter | `P2P/` | Fragment/reassemble for 1170-byte EOS limit |
| MessageRouter | `P2P/` | Typed message dispatch with frame batching |
| P2PSpringSync | `P2P/` | Spring physics sync component (Vector3Half, smallest-three rotation, damped springs) |
| P2PPlayerBall | `Demo/` | WASD ball controller (Input System + legacy fallback) |
| P2PDemoCamera | `Demo/` | Top-down follow camera |
| P2PDemoManager | `Demo/` | Scene manager (uses MessageRouter for typed dispatch, spawn/despawn) |

## How It Works

### Flow

1. Join or create a lobby via the F1 overlay (or `EOSLobbyManager` API)
2. `EOSP2PManager` detects the lobby and establishes P2P connections with all members
3. Peers exchange **Join** packets (with ball color)
4. Each peer spawns a local ball and remote balls for every connected peer
5. Positions sync every `FixedUpdate` via **Position** packets
6. On disconnect, a **Leave** packet triggers remote ball cleanup

### Message Format

All messages are sent through the `MessageRouter` with typed dispatch:

| Type | MsgId | Channel | Reliability | Payload |
|------|-------|---------|-------------|---------|
| Position | `0x01` | 0 | Unreliable | Vector3Half(6) + compressed rot(4) = 10 bytes |
| Join | `0x02` | 1 | Reliable | R(1) + G(1) + B(1) = 3 bytes |
| Leave | `0x03` | 1 | Reliable | 0 bytes |

Position packets are kept small (10 bytes) using half-precision vectors and smallest-three quaternion compression.

### Registration Example

```csharp
var router = EOSP2PManager.Instance.Router;
router.Register(0x01, HandlePosition);
router.Register(0x02, HandleJoin);
router.Register(0x03, HandleLeave);
```

## Controls

### Keyboard

| Key | Action |
|-----|--------|
| W / A / S / D | Move ball |
| Space | Jump |
| E | Cycle color preset |
| Q | Shockwave impulse (pushes self up + nearby balls outward) |
| T | Chat bubble ("Hello!") |
| R | Random visual effect |

### Mobile

Virtual joystick (bottom-left) and jump button (bottom-right) are always created at runtime. The joystick uses EventSystem drag handlers (`IPointerDownHandler`, `IDragHandler`, `IPointerUpHandler`) so it works with both mouse and touch input. Keyboard input combines with joystick input seamlessly.

### Movement Physics

`P2PPlayerBall` applies forces to a `Rigidbody`:

```csharp
// Acceleration with speed cap
// Friction when no input
// Jump with ground check (SphereCast)
```

| Setting | Default |
|---------|---------|
| Acceleration | 25 |
| Max Speed | 12 |
| Friction | 0.15 |
| Jump Force | 8 |
| Ground Check Distance | 0.15 |
| Jump Cooldown | 0.1s |

## Spring Physics Sync

`P2PSpringSync` handles transform synchronization for remote balls. Instead of snapping to received positions, it applies damped spring forces that smoothly guide the ball toward the target.

### Compression

| Data | Encoding | Size |
|------|----------|------|
| Position | Vector3Half (half-precision floats) | 6 bytes |
| Rotation | Smallest-three quaternion compression | 4 bytes |
| **Total per update** | | **10 bytes** |

### How Springs Work

Each remote ball maintains a target position received from the network. A damped spring force is calculated each `FixedUpdate`:

```
force = springConstant * (targetPos - currentPos) - dampingFactor * velocity
```

This produces smooth, physically plausible motion that:
- Absorbs network jitter naturally
- Handles packet loss gracefully (ball glides toward last known position)
- Preserves collision interactions between local and remote balls

### Credits

Spring physics ported from `PhysicsNetworkTransform.cs` (DrewMileham original method, Skylar/CometDev Mirror implementation).

## DemoBallBehaviour (Layer 2 Integration)

`DemoBallBehaviour` is a `NetworkBehaviour` component that demonstrates how Layer 2 networking (SyncVars and typed RPCs) can be added to runtime-created objects.

### SyncVars

```csharp
public SyncVar<int> Score;
public SyncVar<string> DisplayName;
public SyncVar<Color> BallColor;
```

SyncVars are initialized in `Awake()` and automatically sync to all peers:

```csharp
protected override void Awake()
{
    base.Awake();
    Score = Sync(0);
    DisplayName = Sync(string.Empty);
    BallColor = Sync(Color.white);

    BallColor.OnChanged += (_, newColor) => ApplyColorToRenderer(newColor);
}
```

### Typed RPCs

All RPCs use the `[NetRpc]` attribute for zero-boilerplate dispatch:

```csharp
[NetRpc(RPCTarget.All)]
public void ApplyImpulse(float dirX, float dirY, float dirZ, float force)
{
    var rb = GetComponent<Rigidbody>();
    if (rb != null)
        rb.AddForce(new Vector3(dirX, dirY, dirZ).normalized * force, ForceMode.Impulse);
}

[NetRpc(RPCTarget.All)]
public void ChangeColor(float r, float g, float b)
{
    var color = new Color(r, g, b);
    if (IsOwner) BallColor.Value = color;
    ApplyColorToRenderer(color);
}

[NetRpc(RPCTarget.All)]
public void ChatBubble(string message)
{
    _chatMessage = message;
    _chatExpireTime = Time.time + 3f;
}

[NetRpc(RPCTarget.All)]
public void PlayEffect(byte effectId)
{
    _activeEffect = effectId;
    _effectExpireTime = Time.time + 1f;
}

[NetRpc(RPCTarget.Owner)]
public void RequestScorePoint(int amount)
{
    if (IsOwner) Score.Value += amount;
}
```

### Registration Pattern (No Prefabs)

Demo balls are created at runtime without prefabs. Each ball gets `NetworkObject` + `DemoBallBehaviour` added at creation time. A deterministic `NetworkId` is generated from the owner's PUID:

```
NetworkId = 0xBB000000 | (FnvHash(puid) & 0x00FFFFFF)
```

The ball is then registered with the networking layer via `NetworkManager.Instance.RegisterExisting()`, which calls `NotifyNetworkSpawn()` so that `__RegisterNetRPCs()` and `OnNetworkSpawn()` fire correctly.

## HUD Diagnostics

The demo includes an OnGUI overlay showing real-time state:

- P2P peer count
- Remote ball count
- Local ball status
- Voice connection, participant count, and mute state
- Per-player scores and display names (floating above each ball)
- Chat bubbles (3-second duration, displayed above the ball)
- Effect indicators (1-second duration, displayed below the score)

Press **F1** to toggle the full EOS Native status overlay for lobby management, voice controls, and social features while the demo is running.

## Scene Generation

`P2PDemoManager` generates the demo scene at runtime:

- A ground plane
- Scattered crate obstacles
- A top-down follow camera (`P2PDemoCamera`)

No scene file is required. The demo works in any empty scene as long as `P2PDemoManager` is present (auto-created via singleton pattern).
