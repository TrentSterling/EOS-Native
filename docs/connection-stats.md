# Connection Statistics

Real-time network quality monitoring. Track RTT, packet loss, bandwidth, NAT type, and per-peer connection metrics.

## NetworkStats

`NetworkStats` is an auto-create singleton that lives under the EOSManager hierarchy. It uses an `internal static _instance` pattern so that hooks in `EOSP2PManager` have zero overhead when stats are not being used.

```csharp
var stats = NetworkStats.Instance;

float avgRtt = stats.AverageRTT;
float outKBps = stats.TotalBandwidthOutKBps;
NATType nat = stats.LocalNATType;
```

## Metrics

| Metric | Method | Frequency |
|--------|--------|-----------|
| RTT | Ping/pong protocol, EMA smoothed (alpha=0.2) | Every `_pingInterval` (1s) |
| Packet Loss | Rolling window: `1.0 - pongs/pings` | 10s window |
| Bandwidth | Delta between byte snapshots | Every `_sampleInterval` (0.5s) |
| NAT Type | `GetNATType()` + `QueryNATType()` | Once on startup |
| Queue Info | `GetPacketQueueInfo()` | On demand via `GetGlobalStats()` |
| Connection Type | From `OnConnectionEstablished` callback | On peer connect |

## Ping/Pong Protocol

Custom RTT measurement since the EOS SDK does not provide one. Uses dedicated message IDs on a separate channel to avoid interference with game traffic.

- **Message IDs:** `0xA8` (PING), `0xA9` (PONG)
- **Channel:** 2 (unreliable unordered)
- **Sent via:** `SendToPeerImmediate` to bypass frame batching

Each ping and pong is 8 bytes:

| Field | Type | Description |
|-------|------|-------------|
| sequence | uint32 | Incrementing per-peer sequence number |
| timestamp | float32 | Sender's `Time.unscaledTime` when ping was sent |

RTT is calculated as `(Time.unscaledTime - originalTimestamp) * 1000f` milliseconds, then smoothed with exponential moving average (alpha = 0.2). The first measurement is used directly without smoothing.

## EOSP2PManager Hooks

Three lines in `EOSP2PManager` feed data into NetworkStats. The null-check on `_instance` ensures zero allocation and zero overhead when NetworkStats has not been created.

```
SendToPeer()             → NetworkStats._instance?.RecordBytesSent(peer, data.Length)
PollPackets()            → NetworkStats._instance?.RecordBytesReceived(sender, bytesWritten)
OnConnectionEstablished  → NetworkStats._instance?.RecordConnectionType(peer, networkType, establishedType)
```

## Public API

### Per-Peer

```csharp
var stats = NetworkStats.Instance;

// Get full stats object for a peer
PeerStats peer = stats.GetPeerStats(puid);

// Convenience methods
float rtt = stats.RTT(puid);               // ms, -1 if unknown
float loss = stats.PacketLoss(puid);        // 0.0 - 1.0
float age = stats.ConnectionAge(puid);      // seconds since connected

// Iterate all peers
IReadOnlyDictionary<ProductUserId, PeerStats> all = stats.AllPeerStats;
foreach (var kvp in all)
{
    Debug.Log($"{kvp.Key}: {kvp.Value.RTT}ms, {kvp.Value.PacketLoss:P0} loss");
}
```

### Global

```csharp
var stats = NetworkStats.Instance;

// Aggregated stats with queue info
GlobalStats global = stats.GetGlobalStats();
Debug.Log($"Avg RTT: {global.AverageRTT}ms");
Debug.Log($"Out: {global.BandwidthOutKBps:F1} KB/s, In: {global.BandwidthInKBps:F1} KB/s");
Debug.Log($"Queue: {global.OutgoingQueuePackets} packets queued");

// Quick access properties
NATType nat = stats.LocalNATType;
float avgRtt = stats.AverageRTT;
float outKBps = stats.TotalBandwidthOutKBps;
float inKBps = stats.TotalBandwidthInKBps;
```

### Events

```csharp
// Fires every sample interval (0.5s) with updated stats
NetworkStats.Instance.OnStatsUpdated += () =>
{
    UpdateStatsUI();
};
```

### Reset

```csharp
// Clear all tracked data and start fresh
NetworkStats.Instance.ResetStats();
```

## PeerStats Fields

| Field | Type | Description |
|-------|------|-------------|
| PeerId | ProductUserId | The peer's EOS user ID |
| RTT | float | Round-trip time in ms (-1 if unknown) |
| PacketLoss | float | 0.0 to 1.0 over rolling 10s window |
| BytesSent | long | Total bytes sent to this peer |
| BytesReceived | long | Total bytes received from this peer |
| PacketsSent | long | Total packets sent to this peer |
| PacketsReceived | long | Total packets received from this peer |
| ConnectionType | NetworkConnectionType | Direct or Relayed |
| EstablishedType | ConnectionEstablishedType | How the connection was established |
| ConnectedTime | float | `Time.unscaledTime` when peer connected |

## F1 Overlay

The Stats tab in the F1 overlay displays a full network statistics dashboard:

- **Header:** NAT type, peer count, average RTT, total bandwidth in/out
- **Queue:** Incoming and outgoing packet queue utilization
- **Per-peer table:** Name, RTT, Loss%, Connection Type, Out KB/s, In KB/s, Age

Color coding:

| Metric | Green | Yellow | Red |
|--------|-------|--------|-----|
| RTT | < 50ms | < 150ms | > 300ms |
| Packet Loss | < 1% | < 5% | > 5% |
| Connection | Direct | Relayed | -- |

## Configuration

These fields are configurable on the NetworkStats component in the Inspector:

| Setting | Default | Description |
|---------|---------|-------------|
| Ping Interval | 1.0s | How often to send pings to each peer |
| Sample Interval | 0.5s | How often to sample bandwidth and fire `OnStatsUpdated` |
| Max Samples | 60 | Bandwidth sample history length |
| Loss Window Duration | 10.0s | Rolling window for packet loss calculation |
