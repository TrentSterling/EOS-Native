# EOS Voice Interface Reference

> Source: [dev.epicgames.com](https://dev.epicgames.com/docs/epic-online-services/multiplayer/voice-and-rtc-interface/voice-interface/voice-overview)

## Overview

The Voice Interface enables voice chat across multiple platforms. Supports one-on-one and group communications during matches and in lobbies.

- **SDK Version:** 1.13+ required
- **Platform Support:** Linux is NOT supported for EOS Voice
- **Authentication:** Connections only between authenticated users through Voice servers

> **Nintendo Switch:** Cannot link Nintendo Switch accounts to Epic accounts, so blocked Switch accounts are NOT blocked in EOS Voice.

## Integration Methods

### Method 1: Voice with Lobbies (Simple)

The lobby system handles room management automatically.

- Lobby generates room IDs and creates tokens for members
- Lobbies authorize join, kick, and mute operations
- Set `bEnableRTCRoom = true` when creating lobby
- **Max participants: 16**

**Client Policy:**
- `userRequired` = enabled
- Allow **Lobbies** actions (connect, readLobby, etc.)
- Allow **Voice** with `lobbyConference` only
- Do NOT allow `createRoomToken`, `mute`, or `kick`

**Flow:**
1. Client creates lobby with `bEnableRTCRoom` enabled
2. Lobby generates room tokens and returns media server URL
3. SDK uses token to join voice room automatically
4. Other users joining lobby get tokens automatically
5. Use `EOS_RTC` and `EOS_RTCAudio` interfaces for audio

### Method 2: Voice with Trusted Server (64 players)

Your backend manages room creation and authorization.

- Maximum flexibility in room management
- Custom authorization logic
- **Max participants: 64** (SDK 1.16+)

**Server Policy:**
- `userRequired` = disabled
- Enable **Voice** permissions: `createRoomToken`, `kick`, `mute`

**Client Policy:**
- Should NOT have any Voice permissions
- Client doesn't make requests to EOS Voice backend directly

> **Security:** `createRoomToken`, `mute`, and `kick` can operate on ANY room, issue tokens to ANY user. Never give these to game clients.

**Flow:**
1. Client requests server to enter voice room
2. Server authenticates with ClientId + ClientSecret via Connect interface
3. Server generates roomId, requests tokens for players' PUIDs
   - **Dedicated servers:** Use `EOS_Platform_GetRTCAdminInterface`
   - **Web apps:** Use Voice Web API with Client Auth token
4. Server returns token + media server URL to clients
5. Client calls `EOS_RTC_JoinRoom` with provided token

## RTC Interface Handles

| Handle | Purpose | Used By |
|--------|---------|---------|
| `EOS_HRTCAdmin` | Create tokens, kick, mute | Trusted server only |
| `EOS_HRTC` | Room-level (join/leave notifications) | All clients |
| `EOS_HRTCAudio` | Audio management (mute, volume, devices) | All clients |

> **Important:** Voice rooms managed by lobbies are SEPARATE from trusted server rooms. Admin functions (kick/mute) only work within their respective systems.

## Windows Requirements

Must pass `EOS_Windows_RTCOptions` with `XAudio29DllPath` in Platform Interface RTCOptions before creating platform.

## Best Practices

- Use `EOS_RTC_BlockParticipant` for bidirectional muting (platform blocklist compliance)
- Check parental control settings before enabling Voice

## Usage Limitations

| Feature | Service Limit |
|---------|---------------|
| Max room size (trusted server) | **64** |
| Voice with lobbies | **16** |
| Max requests per user/minute | **50** |

> SDK 1.16+: Max room size 64. Earlier versions: 16 only.

## Voice Metrics (Developer Portal)

Available under Analytics > Epic Online Services:
- Connected Users per Platform
- Voice Users Status (Connected, Error, Disconnected)
- Join Room error rates
- Room Sizes distribution
- Detailed error information

Data available for previous 30 days with time interval filtering.

## Voice Web API

See [ref-voice-webapi.md](ref-voice-webapi.md) for the REST API endpoints.
