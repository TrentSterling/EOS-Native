# EOS RTC Data Interface Reference

> Source: [dev.epicgames.com](https://dev.epicgames.com/docs/epic-online-services/multiplayer/voice-and-rtc-interface/rtc-data-interface)

## Overview

The RTC Data Interface allows sending arbitrary data packets through RTC voice rooms. Piggybacks on the same WebRTC connection as voice audio.

## Key Limits

| Limit | Value |
|-------|-------|
| Max packet size | **1,170 bytes** |
| Max throughput | **~500 messages/sec** (exceeding disconnects voice!) |
| Enable flag | Must set `EOS_RTC_JOINROOMFLAGS_ENABLE_DATACHANNEL` at join time |
| Delivery model | Broadcast to all subscribed participants |

> **Warning:** The RTC-data service can receive nearly 500 messages per second. If you exceed this limit, voice chat disconnects.

> Data channel cannot be enabled retroactively — must be set when joining the room.

## API

### SendData

```csharp
var options = new RTCData.SendDataOptions {
    LocalUserId = localPuid,
    RoomName = "match-12345",
    Data = new ArraySegment<byte>(myBytes)
};
Result result = rtcDataInterface.SendData(ref options);
```

### AddNotifyDataReceived

```csharp
var recvOptions = new RTCData.AddNotifyDataReceivedOptions {
    LocalUserId = localPuid,
    RoomName = "match-12345"
};
ulong notifId = rtcDataInterface.AddNotifyDataReceived(
    ref recvOptions, null,
    (ref DataReceivedCallbackInfo info) => {
        // info.Data contains received bytes
        // info.ParticipantId is the sender
    }
);
```

### UpdateReceiving / UpdateSending

Control whether data is being sent/received per room.

## Setup Flow

1. Set `EOS_RTC_JOINROOMFLAGS_ENABLE_DATACHANNEL` flag when calling `JoinRoom`
2. After joining, call `UpdateSending` and `UpdateReceiving` to enable
3. Wait for "Enabled" status via `AddNotifyParticipantUpdated` before sending
4. Register `AddNotifyDataReceived` to receive messages

> `UpdateSending` / `UpdateReceiving` can be called again to disable data transmission.

## Multithreading

- Call RTC-data functions from the **game thread** or you get `EOS_InvalidRequest`
- **Exception:** `SendData` can be called from any thread
- Notifications may arrive on **any thread** (not guaranteed game thread)
- Lock the game thread during send/receive to prevent conflicts

## Custom Protocol Recommendations

**Large messages:** Implement packetizers to split oversized messages into multiple packets under 1170 bytes.

**Targeted messages:** Add `ParticipantId` to your message format. Recipients filter by this field (RTCData broadcasts to all by default).

**Cross-platform:** Send multibyte numbers in network byte order (`htonl`/`ntohl`) to handle endianness differences.

## Use Cases

- In-game text chat through voice rooms
- Game state synchronization (small payloads)
- Custom signaling alongside voice
- Lightweight data channel when P2P is overkill

## Comparison with P2P

| Feature | RTCData | P2P |
|---------|---------|-----|
| Requires voice room | Yes | No |
| Max packet | 1,170 bytes | 1,170 bytes |
| Throughput cap | ~500 msg/sec | No documented cap |
| Delivery | Broadcast all | Per-peer routing |
| Connection setup | Automatic with voice | Manual accept/connect |
| Reliability modes | N/A (best effort) | Unreliable/Reliable/Ordered |
| Threading | SendData = any thread | SendPacket = game thread |
