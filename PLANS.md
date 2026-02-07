# PLANS.md

## Trusted Server Voice Rooms (64-Player Voice)

### Problem
Lobby-managed voice caps at **16 participants**. Standalone trusted server voice rooms support **64 participants** (SDK 1.16+). We want automatic 64-player voice without users managing tokens manually.

### Architecture

```
┌──────────┐       ┌──────────────────┐       ┌──────────────────┐
│  Client   │       │ Cloud Function   │       │  EOS Backend     │
│  (Unity)  │       │ (Lambda/CF/etc)  │       │  (Epic)          │
└────┬──────┘       └──────┬───────────┘       └────────┬─────────┘
     │                     │                            │
     │  1. "I want voice"  │                            │
     │  (PUID, lobbyId)    │                            │
     │ ──────────────────> │                            │
     │  via HTTPS POST     │                            │
     │                     │  2. POST /auth/v1/oauth/token
     │                     │  (client_credentials)      │
     │                     │ ─────────────────────────> │
     │                     │                            │
     │                     │  3. {access_token}          │
     │                     │ <───────────────────────── │
     │                     │                            │
     │                     │  4. POST /rtc/v1/{deploy}/room/{roomId}
     │                     │  {participants: [{puid}]}  │
     │                     │ ─────────────────────────> │
     │                     │                            │
     │                     │  5. {clientBaseUrl, token}  │
     │                     │ <───────────────────────── │
     │                     │                            │
     │  6. {token,         │                            │
     │   clientBaseUrl}    │                            │
     │ <────────────────── │                            │
     │                     │                            │
     │  7. EOS_RTC_JoinRoom(roomName, clientBaseUrl, token)
     │ ────────────────────────────────────────────────> │
     │                     │                            │
     │  8. Voice connected  │                           │
     │ <──────────────────────────────────────────────── │
```

### Voice Web API Endpoints

**Base URL:** `https://api.epicgames.dev/rtc/`

#### 1. Get OAuth Token
```
POST https://api.epicgames.dev/auth/v1/oauth/token
Headers: Authorization: Basic <Base64(ClientId:ClientSecret)>
Body: grant_type=client_credentials&deployment_id=<DeploymentId>

Response: { access_token, expires_in (3600s), expires_at }
```

#### 2. Create Room Tokens
```
POST https://api.epicgames.dev/rtc/v1/{DeploymentId}/room/{RoomId}
Headers: Authorization: Bearer <access_token>
Body: {
  "participants": [
    { "puid": "<ProductUserId>", "clientIp": "<optional>", "hardMuted": false }
  ]
}

Response: {
  "roomId": "match-12345",
  "clientBaseUrl": "wss://...rtcp.on.epicgames.com",
  "deploymentId": "...",
  "participants": [
    { "puid": "...", "token": "<JWT>", "hardMuted": false }
  ]
}
```

#### 3. Remove Participant (Kick)
```
DELETE https://api.epicgames.dev/rtc/v1/{DeploymentId}/room/{RoomId}/participants/{ProductUserId}
Response: 204 No Content
```

#### 4. Modify Participant (Hard Mute)
```
POST https://api.epicgames.dev/rtc/v1/{DeploymentId}/room/{RoomId}/participants/{ProductUserId}
Body: { "hardMuted": true }
Response: 204 No Content
```

### Client-Side: JoinRoomOptions (from SDK)

```csharp
var joinOptions = new Epic.OnlineServices.RTC.JoinRoomOptions
{
    LocalUserId = localPuid,
    RoomName = roomId,                   // Must match server's RoomId
    ClientBaseUrl = response.clientBaseUrl,  // "wss://..."
    ParticipantToken = response.token,      // Per-user JWT
    ParticipantId = null,                   // null = use LocalUserId
    Flags = 0,
    ManualAudioInputEnabled = false,
    ManualAudioOutputEnabled = false
};
```

### Alternative: RTCAdmin SDK Interface (No Web API Needed)

A game client with TrustedServer credentials can use `RTCAdminInterface` directly:

```csharp
// QueryJoinRoomToken — generates tokens for listed users
var options = new RTCAdmin.QueryJoinRoomTokenOptions
{
    LocalUserId = hostPuid,
    RoomName = "match-12345",
    TargetUserIds = new[] { player1Puid, player2Puid, ... },
    TargetUserIpAddresses = null  // optional, for server selection
};

// Kick from trusted server room
var kickOptions = new RTCAdmin.KickOptions
{
    RoomName = "match-12345",
    TargetUserId = badPlayerPuid
};

// Server-side hard mute
var muteOptions = new RTCAdmin.SetParticipantHardMuteOptions
{
    RoomName = "match-12345",
    TargetUserId = toxicPlayerPuid,
    Mute = true
};
```

### Developer Portal Setup

1. Create a new **Client** with `TrustedServer` policy type
2. Enable `Voice:createRoomToken` permission
3. Disable `userRequired` (server acts on behalf of users)
4. Use Client ID + Client Secret for Basic auth

### Implementation Options

#### Option A: Cloud Function (Recommended for production)
- AWS Lambda / Azure Function / Cloudflare Worker
- Holds TrustedServer credentials securely
- Game client calls function via HTTPS
- Function calls EOS Voice Web API, returns tokens
- **Pros:** Credentials never in game build, scalable, secure
- **Cons:** Requires cloud account, adds latency (~100-200ms)

#### Option B: RTCAdmin from Host Client (Simpler, less secure)
- Lobby host uses RTCAdminInterface directly in Unity
- Requires TrustedServer credentials in game build (security risk)
- Host generates tokens, distributes via P2P packets
- **Pros:** No external server needed, works offline
- **Cons:** Client secret extractable from build

#### Option C: Hybrid (Best for EOS-Native)
- Default: Use lobby-managed voice (16 participants, zero config)
- When lobby > 16 members OR user opts in: Switch to trusted server voice
- Cloud function URL configurable in EOSManager inspector
- If no cloud function configured, fall back to lobby voice

### Automatic Flow (Target UX)

1. User creates lobby with voice enabled (same as today)
2. `EOSVoiceManager` detects lobby member count
3. If ≤ 16: Use lobby-managed voice (existing behavior)
4. If > 16 OR `UseTrustedServerVoice` enabled:
   a. Host calls cloud function with lobby members' PUIDs
   b. Cloud function → EOS Voice Web API → returns tokens
   c. Host distributes tokens via P2P reliable packets
   d. Each client calls `RTC.JoinRoom` with their token
5. Late joiners: Host detects new member → requests token → sends via P2P
6. Leavers: Host calls remove participant API

### Token Lifecycle
- OAuth access token: **~1 hour** (3600s), cache and refresh before expiry
- Room participant tokens: Valid as long as room exists and user not kicked
- On disconnect: Client requests new token from host → re-joins

### Security Notes
- **TrustedServer credentials** must NEVER ship in game builds
- Use environment variables / secrets manager in cloud functions
- Each participant token is per-user — only share with that user
- Leaked tokens can be revoked by removing participant via API
- `GameClient` policy credentials are safe in builds but can't create room tokens

### Rate Limits
- Voice interface: **50 requests/user/minute**
- OAuth token: Standard EOS throttling applies
- Room token creation: Not separately documented, general EOS limits apply

### Files to Create/Modify

| File | Action | Description |
|------|--------|-------------|
| `EOSVoiceManager.cs` | Modify | Add trusted server voice path, token distribution |
| `EOSTrustedVoiceProvider.cs` | New | Cloud function HTTP client, token caching |
| `EOSManager.cs` | Modify | Add `TrustedVoiceEndpoint` field, `UseTrustedServerVoice` toggle |
| `EOSNativeStatusUI.cs` | Modify | Show voice mode (lobby vs trusted) in Voice tab |
| `EOSNativeCanvasUI.cs` | Modify | Same for Canvas UI |
| `cloud-function/` | New | Example Lambda/CF worker for token generation |
| `docs/trusted-voice.md` | New | Documentation for setup and configuration |

### Reference Implementation
- [node-eos-voice](https://github.com/Mr-Craig/node-eos-voice) — MIT Node.js wrapper for Voice Web API
- [EOS Voice Web API docs](https://dev.epicgames.com/docs/web-api-ref/voice-web-api)
- [Voice Overview](https://dev.epicgames.com/docs/epic-online-services/multiplayer/voice-and-rtc-interface/voice-interface/voice-overview)

---

## High-Level Networking Framework (EOS-Native Shared Mode)

### Vision

Build a **Fusion Shared Mode-level** networking framework on top of EOS P2P — but easier.
Not a transport for FishNet/Mirror. A standalone framework that owns the full stack.

Key insight: because we own the code top-to-bottom, we can solve problems that
transport-layer solutions can't (seamless migration, zero-destroy pooling, etc).

### Core Principles

1. **Easiest to use ever** — remove all pain points of FishNet/Mirror/Photon setup
2. **Seamless host migration** — never destroy/recreate objects, just transfer authority
3. **Pooled instantiation** — `PooledInstantiate()` / reuse-reset pattern, no Destroy overhead
4. **Cross-platform out of the box** — Windows, Android, VR crossplay, zero extra config
5. **Peer authority (Shared Mode)** — each peer owns their objects, writes directly, no RPCs needed for owned state
6. **No reflection for migration** — we own the serialization, so SyncVar save/restore is built-in, not hacked on top

### Why Not Just Be a Transport?

FishNet and Mirror:
- Force `Destroy()` + `Instantiate()` on host migration (flicker, state loss)
- Require reflection-based SyncVar saving for migration workarounds
- Client-server authority model doesn't map cleanly to P2P/shared mode
- Heavyweight — pull in systems we don't need

Our framework:
- Objects never get destroyed on migration — authority pointer just changes
- SyncVars are first-class: dirty bits, owner-writes, built-in snapshot for late join
- Pooling is native — objects reset and reuse, no GC pressure
- P2P mesh topology — no dedicated server needed, lobby IS the session

### Architecture Layers

```
Layer 3: Game Systems (Prediction, Spatial, Compression)           [TODO]
Layer 2: NetworkObject / SyncVar / Ownership / Pooling / RPCs     [DONE v2.10.0]
         + NetworkAnimator, EasySync, NetworkTransform (Hybrid)
Layer 1: Transport Toolkit (NetWriter, NetReader, PacketFragmenter, MessageRouter) [DONE v2.6.0]
Layer 0: EOS P2P + Lobby (EOSP2PManager, EOSLobbyManager) [DONE]
```

### Layer 2 Status [DONE v2.10.0]

**Implemented (9 files + 1 editor, ~3,600 lines in `Net/`):**
- `NetworkObject` — identity, ownership, SyncVar registry (max 32), adaptive dirty mask
- `SyncVar<T>` — owner-write guard, dirty tracking, OnChanged, snapshot full-state
- `SyncList<T>` — operation-based delta sync (Add/Set/RemoveAt/Insert/Clear)
- `SyncDictionary<TKey, TValue>` — operation-based delta sync (Set/Remove/Clear), key-value pairs
- `NetworkBehaviour` — thin convenience wrapper with `Sync<T>()` and `SyncList<T>()`
- `NetworkTransform` — **Hybrid** (~865 lines): SyncMethod (Auto/Spring/Interpolation), ExtrapolationMode (None/Limited/Unlimited), 3-tier Distance LOD (Full/Tweened/Simple with hysteresis), rest detection, 30-state interpolation buffer, velocity/angular velocity estimation, Teleport() API, Rigidbody kinematic management. Spring physics from PhysicsNetworkTransform (DrewMileham), interpolation from SmoothSync (Jim Burrows).
- `NetworkAnimator` — packed SyncVar for float/int/bool params, trigger RPCs, auto-discover from AnimatorController
- `EasySync` — Normcore-style no-code property sync, reflection-based, Inspector checkboxes
- `EasySyncEditor` — custom Inspector that scans sibling components for syncable members
- `NetworkManager` — singleton, prefab registry, Spawn/Despawn, delta sync in LateUpdate, late-join SNAPSHOT, host migration, generic RPCs (FNV-1a), scene object auto-ownership, object pooling, RPC migration buffer, sequence-based stale rejection, DestroyWithOwner handling
- `NetSerializers` — 19 built-in types, INetSerializable fallback, boxed RPC args
- **Lifetime flags:** `DestroyWithOwner` on NetworkObject — player objects auto-despawn on owner disconnect, room state persists

### Layer 3 Design Goals (Stretch)

- **Prediction/rollback** — client-side prediction for owned objects, Valve-style rollback
- **Spatial interest management** — only sync nearby objects (spatial hash grid)
- **Packet compression** — LZ4 or similar for large payloads
- **Tick-based simulation** — deterministic tick rate (20Hz), decoupled from frame rate
- **Transport bridging** — Steam + EOS transport swapping
- **Scene management** — networked scene loading, additive scenes
- **EasySync v2** — per-property WriteAccess (Host/All via RPC requests), interpolation toggle, "Convert to Code" export
- **Connection statistics** — RTT, packet loss, bandwidth monitoring
- **Auto-reconnect** — automatic reconnection on disconnect with state recovery

### Migration: How It Should Work

```
1. Host disconnects (crash, leave, network drop)
2. Remaining peers detect via EOSP2PManager.OnPeerDisconnected
3. Deterministic election: lowest PUID string = new host
4. New host claims orphaned objects (objects whose owner left)
5. New host sends authority-transfer message to all peers
6. Objects continue running — no Destroy, no Instantiate, no state loss
7. SyncVars already have latest values (they were being synced continuously)
```

### SyncVar Approach (Decided)
- **Hybrid two-tier sync**
- Fast tier: `SyncVar<T>` wrappers sent via P2P packets (60/sec, dirty bits)
- Slow tier: Lobby member/room attributes (persistent, late-join safe)
- `NetworkBehaviour` base class with NetworkId, Owner, dirty serialization
- Late join: Read lobby attributes + host sends P2P full snapshot

### Chat Approach (Decided)
- Live chat: P2P packets (reliable, channel 2)
- Chat history: Host writes rolling `CHAT_LOG` lobby attribute (last 5-10 msgs)
- Late joiners: Read `CHAT_LOG` on join, then live P2P updates

### Competitive Advantages vs Existing Solutions

| Pain Point | FishNet/Mirror | EOS-Native Framework |
|-----------|---------------|---------------------|
| Host migration | Destroy all + recreate | Authority transfer, zero destruction |
| Object pooling | Manual, easy to break | Built-in `PooledInstantiate` |
| SyncVar migration | Reflection hack | Native serialize/deserialize |
| Cross-platform setup | Manual platform config | Auto (Android desugaring, XR) |
| Voice chat | External plugin | Built-in `EOSVoiceManager` |
| Lobby/matchmaking | External plugin | Built-in `EOSLobbyManager` |
| Friends/social | Not included | Built-in social stack |
| Dedicated server | Required for auth model | Optional — P2P mesh works |

---

## EOS Data Budget Cheat Sheet

```
P2P PACKETS (fast, transient)
  Max size .................... 1,170 bytes
  Connections ................. 32 per peer
  Channels .................... 0-255
  Rate ........................ 60+/sec (game rate)

LOBBY ATTRIBUTES (slow, persistent)
  Room-level .................. 64 slots (SDK) / 100 (server)
  Per-member .................. 64 slots (SDK) / 100 (server)
  Key length .................. 64 chars
  Value length ................ 1,000 chars
  Update rate ................. ~100/min (~1.6/sec)

VOICE ROOMS
  Lobby-managed ............... 16 participants
  Trusted server .............. 64 participants (SDK 1.16+)
  RTCData packets ............. 1,170 bytes, ~500 msg/sec max

STORAGE
  Player files ................ 1,000 files, 200 MB each, 400 MB total
  Title storage ............... 10 GB per deployment
```
