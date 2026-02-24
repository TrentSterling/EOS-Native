# EOS Voice Web API Reference

> Source: [dev.epicgames.com](https://dev.epicgames.com/docs/web-api-ref/voice-web-api)

## Authentication

All endpoints require a Bearer token obtained via Client Credentials:

```
POST https://api.epicgames.dev/auth/v1/oauth/token
Content-Type: application/x-www-form-urlencoded
Authorization: Basic <Base64(ClientId:ClientSecret)>

grant_type=client_credentials&deployment_id=<DeploymentId>
```

Response: `{ access_token, expires_in (3600s), expires_at, features: ["Voice"] }`

## Base URL

```
https://api.epicgames.dev/rtc/
```

All endpoints require:
```
Authorization: Bearer <access_token>
Content-Type: application/json
```

---

## Create Room Tokens

Creates a voice room (auto-created if new) and generates participant tokens.

```
POST /rtc/v1/{DeploymentId}/room/{RoomId}
```

### Request Body

```json
{
  "participants": [
    {
      "puid": "<ProductUserId>",
      "clientIp": "<optional, for server selection>",
      "hardMuted": false
    }
  ]
}
```

| Field | Required | Description |
|-------|----------|-------------|
| `participants[].puid` | Yes | EOS ProductUserId |
| `participants[].clientIp` | No | Player's public IP (selects closest media server) |
| `participants[].hardMuted` | Yes | Initial server-side mute status |

### Response (200)

```json
{
  "roomId": "match-12345",
  "clientBaseUrl": "wss://...rtcp.on.epicgames.com",
  "deploymentId": "<DeploymentId>",
  "participants": [
    {
      "puid": "<ProductUserId>",
      "token": "<JWT>",
      "hardMuted": false
    }
  ]
}
```

> Each participant token is per-user. Only share with the corresponding player.

> Call multiple times for the same room to add more participants.

---

## Remove Participant (Kick)

```
DELETE /rtc/v1/{DeploymentId}/room/{RoomId}/participants/{ProductUserId}
```

Response: `204 No Content`

Removes player from voice room and revokes their token.

---

## Modify Participant (Hard Mute)

```
POST /rtc/v1/{DeploymentId}/room/{RoomId}/participants/{ProductUserId}
```

### Request Body

```json
{
  "hardMuted": true
}
```

Response: `204 No Content`

Server-side mute - independent of participant's local mute status.

---

## RTCAdmin SDK Alternative

Instead of the web API, a trusted server running the EOS SDK can use `RTCAdminInterface`:

| Function | Purpose |
|----------|---------|
| `QueryJoinRoomToken` | Generate tokens for listed users |
| `Kick` | Remove participant from room |
| `SetParticipantHardMute` | Server-side mute/unmute |

```csharp
// QueryJoinRoomToken
var options = new RTCAdmin.QueryJoinRoomTokenOptions {
    LocalUserId = serverPuid,
    RoomName = "match-12345",
    TargetUserIds = new[] { player1, player2 }
};

// Kick
var kickOpts = new RTCAdmin.KickOptions {
    RoomName = "match-12345",
    TargetUserId = badPlayer
};

// Hard Mute
var muteOpts = new RTCAdmin.SetParticipantHardMuteOptions {
    RoomName = "match-12345",
    TargetUserId = toxicPlayer,
    Mute = true
};
```

---

## Client-Side: JoinRoom

After receiving token from server:

```csharp
var joinOptions = new RTC.JoinRoomOptions {
    LocalUserId = localPuid,
    RoomName = "match-12345",
    ClientBaseUrl = "wss://...",     // from server response
    ParticipantToken = "<JWT>",      // per-user token
    ParticipantId = null,            // null = use LocalUserId
    Flags = 0,
    ManualAudioInputEnabled = false,
    ManualAudioOutputEnabled = false
};
```

---

## Developer Portal Setup

1. Create new Client with **TrustedServer** policy
2. Enable `Voice:createRoomToken` permission
3. Disable `userRequired`
4. Use Client ID + Secret for Basic auth header

---

## Reference Implementation

- [node-eos-voice](https://github.com/Mr-Craig/node-eos-voice) - MIT Node.js wrapper (npm: `node-eos-voice`)
