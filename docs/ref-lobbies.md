# EOS Lobbies Reference

> Source: [dev.epicgames.com](https://dev.epicgames.com/docs/epic-online-services/multiplayer/lobbies-and-sessions/lobby-interface/lobbies-intro)

## Overview

Lobbies provide persistent connections between players to share game and player state with real-time updates. Players can create or join lobbies to form teams, select pre-game options, and wait for additional players.

## Lobby Lifecycle

1. **Creation** — User creates lobby, becomes first member and owner. Indexing takes up to 3 seconds.
2. **Setup** — Owner sets initial state, may invite users.
3. **Member Activity** — Users join/leave, update state, invite others.
4. **Game State** — Owner updates game-specific data.
5. **Member Removal** — Owner can kick members or transfer ownership.
6. **Multiple Rounds** — Play multiple rounds without destroying lobby.
7. **Destruction** — Owner destroys lobby.

> **Warning:** Removing `lobbies:connect` permission from a client policy closes the lobby within minutes.

## CreateLobby Options

| Property | Description |
|----------|-------------|
| `LocalUserId` | Creating user (becomes owner) |
| `MaxLobbyMembers` | Max users allowed (up to 64) |
| `PermissionLevel` | Public or private visibility |
| `BucketId` | Bucket ID for categorization |
| `bPresenceEnabled` | Associate with presence info |
| `bAllowInvites` | Whether members can invite |
| `bDisableHostMigration` | Keep lobby open if host leaves |
| `bEnableRTCRoom` | Create voice room for members |
| `AllowedPlatformIds` | Platform filtering array |
| `bCrossplayOptOut` | Disable crossplay |

## Invites

- `SendInvite` — Members invite others
- `RejectInvite` — Permanently delete invitation
- `QueryInvites` — Refresh all pending invitations (useful at startup)
- `GetInviteCount` / `GetInviteIdByIndex` — Read cached invitations
- `AddNotifyLobbyInviteReceived` — Real-time invitation notifications
- `CopyLobbyDetailsHandleByInviteId` — Get handle to join from invite

## Join / Leave

- `JoinLobby` — Join with valid `LobbyDetails` handle. Can be in multiple lobbies simultaneously.
- `LeaveLobby` — Leave lobby. If owner leaves, EOS selects new owner.

## Kick Members

Owner calls `KickMember` — all remaining members notified with `EOS_LMSC_KICKED` event.

## Destroy Lobby

Owner calls `DestroyLobby` — removes all remaining members, triggers `EOS_LMS_CLOSED` status.

## Service Limits

| Feature | Limit |
|---------|-------|
| Max players in a lobby | **64** |
| Max session attributes | **100** |
| Max member attributes | **100** |
| String attribute length | **1,000 characters** |

## Per-User Rate Limits

| Operation | Limit |
|-----------|-------|
| Connect | 30/min |
| Create a lobby | 30/min |
| Delete a lobby | 30/min |
| Join a lobby | 30/min |
| Lobbies per user (simultaneous) | **16** |
| Read lobby data | 100/min |
| Update lobby attributes | 100/min |
| Update member attributes | 100/min |
| Change lobby settings | 30/min |
| Invite a user | 30/min |
| Delete an invitation | 30/min |
| Kick a player | 30/min |
| Promote to owner | 30/min |
| Find lobbies | 30/min |
| Get lobby by ID | 100/min |
| Find lobbies by user | 30/min |
| Find invitations by user | 30/min |
| Find lobby by invitation | 30/min |
| Max search results per query | **256** |
