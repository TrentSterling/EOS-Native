# P2P Transport

Foundation layer for typed peer-to-peer messaging. Provides binary serialization, packet fragmentation, message dispatch, and frame batching. All files in `Runtime/EOSNative/P2P/`.

This is a standalone transport toolkit that works without FishNet or any other networking framework. It powers both the [P2P Ball Demo](p2p-demo.md) and the higher-level [Layer 2 networking](architecture.md).

## EOSP2PManager

Singleton P2P mesh manager. Discovers peers from the lobby member list, establishes direct P2P connections, and polls for incoming packets every frame.

### Basic Usage

```csharp
var p2p = EOSP2PManager.Instance;

// Check state
bool active = p2p.IsActive;
int peerCount = p2p.Peers.Count;

// Send raw data to a specific peer
p2p.SendToPeer(peerPuid, channel: 0, data, PacketReliability.ReliableOrdered);

// Send raw data to all connected peers
p2p.SendToAll(channel: 0, data, PacketReliability.UnreliableUnordered);

// Bypass batching for latency-sensitive messages
p2p.SendToPeerImmediate(peerPuid, channel: 0, data, PacketReliability.UnreliableUnordered);
```

### Events

```csharp
p2p.OnPeerConnected += (puid) => Debug.Log($"Peer connected: {puid}");
p2p.OnPeerDisconnected += (puid) => Debug.Log($"Peer disconnected: {puid}");
p2p.OnPacketReceived += (sender, channel, data) => { /* raw packet handler */ };
```

### Router Property

The `Router` property provides access to the typed [MessageRouter](#messagerouter). It is lazy-created on first access and auto-subscribes to `OnPacketReceived`. You do not need to manually wire the subscription.

```csharp
var router = EOSP2PManager.Instance.Router;
router.Register(0x01, HandlePosition);
```

### Lobby Integration

`EOSP2PManager` hooks into `EOSLobbyManager` events automatically. When you join a lobby, P2P connections form with all members. When you leave, connections are torn down. No manual initialization is needed.

## NetWriter / NetReader

Binary serializer/deserializer pair with auto-growing buffer and packed integer support.

### Writing

```csharp
var writer = NetWriterPool.Get();
writer.WriteVector3Half(position);
writer.WriteUInt32(compressedRot);
writer.WriteString("hello");
writer.WritePackedInt32(-42);
writer.WriteBool(true);
writer.WriteColor32(color);

// Access raw data
byte[] data = writer.ToArray();

NetWriterPool.Return(writer);
```

### Reading

```csharp
var reader = new NetReader(data);
Vector3 pos = reader.ReadVector3Half();
uint rot = reader.ReadUInt32();
string msg = reader.ReadString();
int val = reader.ReadPackedInt32();
bool flag = reader.ReadBool();
Color32 col = reader.ReadColor32();
```

### Supported Types

| Type | Write Method | Read Method | Size |
|------|-------------|-------------|------|
| byte | `WriteByte` | `ReadByte` | 1 byte |
| bool | `WriteBool` | `ReadBool` | 1 byte |
| Int16 / UInt16 | `WriteInt16` / `WriteUInt16` | `ReadInt16` / `ReadUInt16` | 2 bytes |
| Int32 / UInt32 | `WriteInt32` / `WriteUInt32` | `ReadInt32` / `ReadUInt32` | 4 bytes |
| Int64 / UInt64 | `WriteInt64` / `WriteUInt64` | `ReadInt64` / `ReadUInt64` | 8 bytes |
| float | `WriteFloat` | `ReadFloat` | 4 bytes |
| double | `WriteDouble` | `ReadDouble` | 8 bytes |
| Packed UInt32 | `WritePackedUInt32` | `ReadPackedUInt32` | 1-5 bytes (varint) |
| Packed Int32 | `WritePackedInt32` | `ReadPackedInt32` | 1-5 bytes (varint) |
| Packed UInt64 | `WritePackedUInt64` | `ReadPackedUInt64` | 1-10 bytes (varint) |
| string | `WriteString` | `ReadString` | 2 + UTF-8 bytes |
| Vector2 | `WriteVector2` | `ReadVector2` | 8 bytes |
| Vector3 | `WriteVector3` | `ReadVector3` | 12 bytes |
| Vector3Half | `WriteVector3Half` | `ReadVector3Half` | 6 bytes |
| Quaternion | `WriteQuaternion` | `ReadQuaternion` | 16 bytes |
| Compressed Rotation | `WriteUInt32` | `ReadUInt32` | 4 bytes |
| Color | `WriteColor` | `ReadColor` | 16 bytes |
| Color32 | `WriteColor32` | `ReadColor32` | 4 bytes |
| byte[] | `WriteBytes` | `ReadBytes` | 4 + length |
| ProductUserId | `WriteProductUserId` | `ReadProductUserId` | 2 + string bytes |

Strings are UTF-8 encoded with a ushort length prefix.

### Pooling

Use `NetWriterPool` for allocation-free reuse in hot paths:

```csharp
var writer = NetWriterPool.Get();
// ... write data ...
byte[] data = writer.ToArray();
NetWriterPool.Return(writer);
```

Always return writers to the pool when done. The pool grows on demand and has no upper limit.

## PacketFragmenter

EOS P2P has a hard limit of 1170 bytes per packet. The `PacketFragmenter` transparently splits large messages into fragments and reassembles them on the receiving side.

### Fragment Header

Each fragment carries a 7-byte header:

```
[packetId:u32] [fragmentIndex:u16] [lastFragment:u8]
```

- **packetId** -- unique ID for this logical message
- **fragmentIndex** -- which fragment this is (0-based)
- **lastFragment** -- 1 if this is the final fragment, 0 otherwise

### Limits

| Property | Value |
|----------|-------|
| Max EOS P2P packet | 1170 bytes |
| Fragment header | 7 bytes |
| Max payload per fragment | 1163 bytes |

### Behavior

- **Single-fragment fast path:** Messages under 1163 bytes skip the dictionary lookup entirely and return directly.
- **Stale cleanup:** Incomplete fragment assemblies are discarded after 5 seconds to prevent memory leaks from lost packets.
- **Duplicate ignore:** Receiving the same fragment twice has no effect.
- **Out-of-order support:** Fragments can arrive in any order and still reassemble correctly.

Fragmentation is handled internally by the `MessageRouter`. You do not need to interact with `PacketFragmenter` directly.

## MessageRouter

The central dispatch layer. Register handlers by message ID, send typed messages, and let the router handle batching and fragmentation.

### Registering Handlers

```csharp
var router = EOSP2PManager.Instance.Router;

router.Register(0x01, (sender, reader) =>
{
    Vector3 pos = reader.ReadVector3Half();
    uint rot = reader.ReadUInt32();
    ApplyRemotePosition(sender, pos, rot);
});

router.Register(0x02, HandleJoinMessage);
router.Register(0x03, HandleLeaveMessage);

// Remove a handler
router.Unregister(0x03);
```

### Sending Messages

```csharp
var writer = NetWriterPool.Get();
writer.WriteVector3Half(pos);
writer.WriteUInt32(compressedRot);

// Send to all peers (queued for batching)
router.SendToAll(0x01, writer, PacketReliability.UnreliableUnordered);

// Send to a specific peer
router.SendToPeer(0x02, writer, peerPuid, PacketReliability.ReliableOrdered);

NetWriterPool.Return(writer);
```

### Wire Format

```
EOS P2P Packet (max 1170 bytes)
+-- Fragment Header (7 bytes)
|   [packetId:u32] [fragmentIndex:u16] [lastFragment:u8]
+-- Router Envelope
    [batchFlag:u8]  (0x00=single, 0x01=batched)
    +-- Single: [msgId:u8] [payload...]
    +-- Batch:  [count:u16] [len:u16][msgId:u8][payload] ...
```

### Frame Batching

When `BatchingEnabled` is true (the default), messages are not sent immediately. Instead, they are grouped by `(channel, reliability, target)` and flushed once per frame in `LateUpdate`. This reduces the number of EOS P2P send calls when multiple messages are queued in the same frame.

```csharp
// Disable batching for immediate sends
router.BatchingEnabled = false;
```

### Backward Compatibility

`EOSP2PManager.OnPacketReceived` still fires for all raw packets regardless of whether the router is in use. The router is opt-in -- existing code that reads raw packets works unchanged.

## Packet Compression

Opt-in Deflate compression for message payloads. Transparent to application code.

### Enabling Compression

```csharp
// Via NetworkManager (Layer 2)
NetworkManager.Instance.CompressionEnabled = true;

// Or directly on the router (Layer 1)
EOSP2PManager.Instance.Router.CompressionEnabled = true;
EOSP2PManager.Instance.Router.CompressionThreshold = 128; // bytes
```

### Properties

| Property | Default | Description |
|----------|---------|-------------|
| `CompressionEnabled` | `false` | Enable/disable Deflate compression |
| `CompressionThreshold` | `64` | Minimum payload size (bytes) before compression is attempted |

### Wire Format Flags

| Flag | Value | Meaning |
|------|-------|---------|
| `FLAG_SINGLE` | `0x00` | Single uncompressed message |
| `FLAG_BATCH` | `0x01` | Batched uncompressed messages |
| `FLAG_SINGLE_COMPRESSED` | `0x02` | Single Deflate-compressed message |
| `FLAG_BATCH_COMPRESSED` | `0x03` | Batched Deflate-compressed messages |

Compression is only applied when the compressed output is smaller than the original. Otherwise it falls back to uncompressed automatically.

### Backward Compatibility

Old peers that do not understand flags `0x02` and `0x03` silently ignore compressed messages (no handler found). Mixed-version peers will still communicate over uncompressed packets.

## P2P Connection Establishment

EOS P2P requires **both sides** to call `AcceptConnection()` AND at least one side to call `SendPacket()` for a connection to establish. `AcceptConnection()` alone is passive -- it only tells the SDK "I will accept data from this peer if they send something."

### The Race Condition

When joining an existing lobby, `OnMemberJoined` only fires for **new** members joining after you. Existing members are already there -- no event fires. Without pre-accepting existing members, the joiner never calls `AcceptPeer()`, and since `SendToAll` only iterates established peers, nobody sends data. The connection deadlocks.

### The Fix

`EOSP2PManager` handles this automatically in three places:

1. **`OnLobbyJoined`** -- After initialization, enumerate all existing lobby members via `EOSLobbyManager.GetMemberPuids()`, call `AcceptPeer()` for each, and send a handshake packet (msgId `0xFE`, silently ignored by the receiver) to trigger the P2P connection request.

2. **`OnMemberJoined`** -- In addition to `AcceptPeer()`, also send a handshake packet so the host kick-starts the connection to new joiners.

3. **Handshake retry** -- If the mesh is active and in a lobby but `_peers.Count == 0`, re-enumerate members and re-send handshakes every 2 seconds, up to 5 retries. Retries stop when any peer connects or max retries are reached. The retry counter resets on lobby leave, new lobby join, or when all peers disconnect.

### GetMemberPuids

`EOSLobbyManager.GetMemberPuids()` returns all `ProductUserId` values in the current lobby by iterating `LobbyDetails.GetMemberByIndex()`. Returns an empty list if not in a lobby.

```csharp
List<ProductUserId> members = EOSLobbyManager.Instance.GetMemberPuids();
```

### Diagnostic Logging

Connection establishment produces detailed log output:

| Event | What is Logged |
|-------|----------------|
| `Initialize()` | Warning if P2P interface or LocalUserId is null |
| `AcceptPeer()` | Warning if skipped; Result from `AcceptConnection()` |
| `SendToPeer()` | Warning if `SendPacket()` returns non-Success |
| Connection established | Connection type (Direct/Relayed) and network type |
| Connection closed | Disconnection reason |
| Handshake sends | Member count, context (OnLobbyJoined/OnMemberJoined/Retry N), peer state |

### Router Auto-Subscription

`Router.ProcessIncoming` is auto-subscribed to `OnPacketReceived` when the `Router` property is first accessed (lazy creation). External code does not need to manually wire `OnPacketReceived += Router.ProcessIncoming`. The `-= +=` pattern in `OnEnable`/`OnDisable` prevents stale subscriptions across enable/disable cycles.
