# CLAUDE-P2P.md

Detailed P2P transport (Layer 1) reference. See CLAUDE.md for project overview and rules.

## P2P Transport Toolkit (Layer 1)

Foundation for typed P2P messaging. Provides binary serialization, packet fragmentation, message dispatch, and frame batching. All files in `Runtime/EOSNative/P2P/`.

### NetWriter / NetReader

Binary serializer/deserializer pair with auto-growing buffer and packed integer support.

```csharp
// Write
var writer = NetWriterPool.Get();
writer.WriteVector3Half(position);
writer.WriteUInt32(compressedRot);
writer.WriteString("hello");
writer.WritePackedInt32(-42);

// Read
var reader = new NetReader(data);
Vector3 pos = reader.ReadVector3Half();
uint rot = reader.ReadUInt32();
string msg = reader.ReadString();
int val = reader.ReadPackedInt32();

NetWriterPool.Return(writer);
```

**Supported types:** byte, bool, int16/uint16, int32/uint32, int64/uint64, float, double, packed uint32/int32/uint64 (varint), string (UTF-8 ushort-prefixed), Vector2, Vector3, Quaternion, Vector3Half (6 bytes), compressed rotation (4 bytes), Color, Color32, byte[], ProductUserId.

**Pooling:** `NetWriterPool.Get()` / `NetWriterPool.Return()` for allocation-free reuse.

### PacketFragmenter

Splits messages exceeding the EOS P2P limit (1170 bytes) into fragments and reassembles them.

- **Header:** 7 bytes `[packetId:u32][fragmentIndex:u16][lastFragment:u8]`
- **Max payload per fragment:** 1163 bytes
- **Single-fragment fast path:** No dictionary lookup, direct return
- **Stale cleanup:** Incomplete fragments discarded after 5 seconds

### MessageRouter

Message registration, typed dispatch, and frame batching. The "glue" layer.

```csharp
// Register handlers
var router = EOSP2PManager.Instance.Router;
router.Register(0x01, HandlePosition);
router.Register(0x02, HandleJoin);

// Subscribe router to raw packets
EOSP2PManager.Instance.OnPacketReceived += router.ProcessIncoming;

// Send typed messages (queued for batching)
var writer = NetWriterPool.Get();
writer.WriteVector3Half(pos);
router.SendToAll(0x01, writer, PacketReliability.UnreliableUnordered);
NetWriterPool.Return(writer);
```

**Wire format:**
```
EOS P2P Packet (max 1170 bytes)
├── Fragment Header (7 bytes)
│   [packetId:u32] [fragmentIndex:u16] [lastFragment:u8]
└── Router Envelope
    [batchFlag:u8]  (0x00=single, 0x01=batched)
    ├── Single: [msgId:u8] [payload...]
    └── Batch:  [count:u16] [len:u16][msgId:u8][payload] ...
```

**Batching:** Groups messages by (channel, reliability, target) and flushes once per frame in LateUpdate. Reduces P2P send calls when multiple messages queue in the same frame.

**Backward compatibility:** `EOSP2PManager.OnPacketReceived` still fires for all raw packets. The router is opt-in — old code works unchanged.

## P2P Ball Demo

A simple multiplayer demo using the raw EOS P2P interface (no FishNet, no high-level transport). Players control a ball with WASD, can jump, and collide. Positions sync across peers using spring physics.

**Peer-authority model:** Each player owns their ball. Local physics runs normally; remote balls are guided toward received positions via damped spring forces.

**Files (5):**

| File | Location | Description |
|------|----------|-------------|
| EOSP2PManager | `P2P/` | Reusable singleton P2P mesh manager (auto-accept, send/receive, lobby integration, MessageRouter) |
| NetWriter | `P2P/` | Binary serializer with auto-growing buffer, packed ints, pooling |
| NetReader | `P2P/` | Binary deserializer with bounds checking |
| PacketFragmenter | `P2P/` | Fragment/reassemble for 1170-byte EOS limit |
| MessageRouter | `P2P/` | Typed message dispatch with frame batching |
| P2PSpringSync | `P2P/` | Spring physics sync component (Vector3Half, smallest-three rotation, damped springs) |
| P2PPlayerBall | `Demo/` | WASD ball controller (Input System + legacy fallback) |
| P2PDemoCamera | `Demo/` | Top-down follow camera |
| P2PDemoManager | `Demo/` | Scene manager (uses MessageRouter for typed dispatch, spawn/despawn) |

**Message format (via MessageRouter):**

| Type | MsgId | Channel | Reliability | Payload |
|------|-------|---------|-------------|---------|
| Position | 0x01 | 0 | Unreliable | Vector3Half(6) + compressed rot(4) = 10 bytes |
| Join | 0x02 | 1 | Reliable | R(1) + G(1) + B(1) = 3 bytes |
| Leave | 0x03 | 1 | Reliable | 0 bytes |

**Mobile controls:** Virtual joystick (bottom-left) and jump button (bottom-right) always created. Joystick uses EventSystem drag handlers (`IPointerDownHandler`/`IDragHandler`/`IPointerUpHandler`) — works with both mouse and touch. Keyboard input combines with joystick input seamlessly.

**HUD diagnostics:** OnGUI overlay shows P2P peer count, remote ball count, local ball status, voice connection/participant/mute state, and per-player scores.

**Flow:** Join/create lobby via F1 overlay -> EOSP2PManager detects lobby -> P2P connections form -> exchange join packets -> spring-sync positions every FixedUpdate.

**Credits:** Spring physics ported from PhysicsNetworkTransform.cs (DrewMileham original method, Skylar/CometDev Mirror implementation).

## P2P Connection Establishment (Host-Order Fix + Retry)

EOS P2P requires **both sides** to call `AcceptConnection()` AND at least one side to call `SendPacket()` for a connection to establish. `AcceptConnection()` alone is passive — it only tells the SDK "I'll accept data from this peer if they send something."

**The race condition (fixed in v2.17.1):** When joining an existing lobby, `OnMemberJoined` only fires for **new** members joining after you. Existing members are already there — no event fires. Without pre-accepting existing members, the joiner never calls `AcceptPeer()`, and since `SendToAll` only iterates established peers (`_peers`), nobody sends data either. The connection deadlocks.

**Fix in `EOSP2PManager`:**
1. **`OnLobbyJoined`:** After `Initialize()`, enumerate all existing lobby members via `EOSLobbyManager.GetMemberPuids()`, call `AcceptPeer()` for each, and send a handshake packet (msgId 0xFE via Router, silently ignored by receiver) to trigger the P2P connection request.
2. **`OnMemberJoined`:** In addition to the existing `AcceptPeer()`, also send a handshake packet so the host kick-starts the connection to new joiners.
3. **Handshake retry (v2.17.2):** If `IsActive` and in a lobby but `_peers.Count == 0`, re-enumerate members and re-send handshakes every 2 seconds, up to 5 times. Handles timing issues where the initial handshake fails or the remote side isn't ready yet. Retries stop when any peer connects or max retries reached. Reset on lobby leave, new lobby join, or when all peers disconnect.

**Diagnostic logging (v2.17.2):**
- `Initialize()` logs a warning if P2P interface or LocalUserId is null (was a silent early return)
- `AcceptPeer()` logs a warning if skipped, and logs the Result from `P2P.AcceptConnection()`
- `SendToPeer()` logs a warning if `P2P.SendPacket()` returns non-Success
- `OnConnectionEstablished` logs connection type (Direct/Relayed) and network type
- `OnConnectionClosed` logs the disconnection reason
- Handshake sends log member count, context (OnLobbyJoined/OnMemberJoined/Retry N), and peer state

**Router auto-subscription (v2.17.2):**
`Router.ProcessIncoming` is now auto-subscribed to `OnPacketReceived` when the Router property is first accessed (lazy creation). External code no longer needs to manually wire `OnPacketReceived += Router.ProcessIncoming`. The `-= +=` pattern in `OnEnable`/`OnDisable` prevents stale subscriptions across enable/disable cycles.

**`GetMemberPuids()` on EOSLobbyManager:** New public method that enumerates all member `ProductUserId`s in the current lobby via `LobbyDetails.GetMemberByIndex()`. Returns empty list if not in a lobby.
