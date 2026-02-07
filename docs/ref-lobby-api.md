# EOS Lobby Interface API Reference

> Source: [dev.epicgames.com](https://dev.epicgames.com/docs/en-US/api-ref/interfaces/lobby)

## Overview

The Lobby Interface enables multiplayer lobby functionality — creation, member management, invitations, real-time attribute sync, and voice integration via RTC.

## Core Functions

### Lobby Lifecycle

| Function | Description |
|----------|-------------|
| `CreateLobby` | Create a new lobby with specified parameters |
| `JoinLobby` | Join an existing lobby |
| `JoinLobbyById` | Join a lobby using its unique identifier |
| `LeaveLobby` | Leave current lobby |
| `DestroyLobby` | Delete a lobby (owner only) |
| `UpdateLobby` | Modify lobby properties and settings |

### Lobby Discovery

| Function | Description |
|----------|-------------|
| `CreateLobbySearch` | Initialize a search operation |
| `LobbySearch_Find` | Execute the search |
| `LobbySearch_SetParameter` | Add search filter criteria |
| `LobbySearch_RemoveParameter` | Remove a filter |
| `LobbySearch_SetMaxResults` | Limit number of results |
| `LobbySearch_SetLobbyId` | Filter by specific lobby ID |
| `LobbySearch_SetTargetUserId` | Search for lobbies containing a specific user |

### Invite Management

| Function | Description |
|----------|-------------|
| `SendInvite` | Send a lobby invitation |
| `QueryInvites` | Retrieve pending invitations |
| `RejectInvite` | Decline a received invitation |
| `GetInviteCount` | Number of pending invites |
| `GetInviteIdByIndex` | Get invite by index |

### Lobby Details

| Function | Description |
|----------|-------------|
| `CopyLobbyDetailsHandle` | Get handle to lobby info |
| `CopyLobbyDetailsHandleByInviteId` | Get lobby details from an invite |
| `CopyLobbyDetailsHandleByUiEventId` | Get lobby details via UI event |
| `LobbyDetails_CopyInfo` | Extract lobby metadata |
| `LobbyDetails_GetAttributeCount` | Count custom lobby attributes |
| `LobbyDetails_CopyAttributeByIndex` | Get attribute by position |
| `LobbyDetails_CopyAttributeByKey` | Get attribute by name |

### Member Management

| Function | Description |
|----------|-------------|
| `LobbyDetails_GetMemberCount` | Number of members |
| `LobbyDetails_GetMemberByIndex` | Access member by position |
| `LobbyDetails_CopyMemberInfo` | Extract member information |
| `LobbyDetails_GetMemberAttributeCount` | Count member attributes |
| `LobbyDetails_CopyMemberAttributeByIndex` | Get member attribute by position |
| `LobbyDetails_CopyMemberAttributeByKey` | Get member attribute by name |
| `LobbyDetails_GetLobbyOwner` | Get lobby owner |
| `PromoteMember` | Promote a member to owner |
| `KickMember` | Remove a member (owner only) |
| `HardMuteMember` | Server-side mute a member |

### RTC Integration

| Function | Description |
|----------|-------------|
| `JoinRTCRoom` | Connect to lobby voice channel |
| `LeaveRTCRoom` | Disconnect from RTC |
| `GetRTCRoomName` | Get the RTC room identifier |
| `IsRTCRoomConnected` | Check RTC connection status |

### Utility

| Function | Description |
|----------|-------------|
| `GetConnectString` | Generate a connection string for the lobby |
| `ParseConnectString` | Decode a connection string |

## Notification Callbacks

Register with `AddNotify*`, unregister with `RemoveNotify*`:

| Notification | Fires When |
|-------------|------------|
| `JoinLobbyAccepted` | Join request approved |
| `LeaveLobbyRequested` | Owner closes lobby |
| `LobbyInviteReceived` | Incoming invitation |
| `LobbyInviteAccepted` | Recipient accepts invitation |
| `LobbyInviteRejected` | Invitation declined |
| `LobbyMemberStatusReceived` | Member join/leave/disconnect |
| `LobbyMemberUpdateReceived` | Member attribute changes |
| `LobbyUpdateReceived` | Lobby property modifications |
| `RTCRoomConnectionChanged` | RTC connection state transitions |
| `SendLobbyNativeInviteRequested` | Platform-specific invite handling |

## Key Data Structures

### Options Structs

| Struct | Purpose |
|--------|---------|
| `CreateLobbyOptions` | Lobby creation parameters |
| `JoinLobbyOptions` | Join settings |
| `JoinLobbyByIdOptions` | Join by identifier |
| `LeaveLobbyOptions` | Leave configuration |
| `DestroyLobbyOptions` | Deletion parameters |
| `UpdateLobbyOptions` | Modification parameters |

### Lobby Modification

| Struct | Purpose |
|--------|---------|
| `LobbyModification_AddAttributeOptions` | Add/update custom attribute |
| `LobbyModification_SetMaxMembersOptions` | Set max capacity |
| `LobbyModification_SetPermissionLevelOptions` | Set access control |
| `LobbyModification_SetBucketIdOptions` | Set bucket ID for search grouping |
| `LobbyModification_SetInvitesAllowedOptions` | Toggle invitations |

### Info Containers

| Struct | Purpose |
|--------|---------|
| `LobbyDetails_Info` | Lobby metadata |
| `LobbyDetails_MemberInfo` | Member details |

## Enumerations

| Enum | Values |
|------|--------|
| `EOS_ELobbyAttributeVisibility` | Controls attribute visibility scope |
| `EOS_ELobbyMemberStatus` | Joined, Left, Disconnected, Kicked, Promoted, Closed |
| `EOS_ELobbyPermissionLevel` | PublicAdvertised, JoinViaPresence, InviteOnly |

## Service Limits

| Limit | Value |
|-------|-------|
| Max members per lobby | **64** |
| Max lobbies per user | **16** |
| Max session attributes | **100** |
| Max member attributes | **100** |
| Max attribute value length | **1000 characters** |
| Max search results | **256** |
| Lobby-managed voice | **16 participants** |
| Indexing delay (new lobby visible in search) | up to **3 seconds** |

> **Note:** The SDK constant `LOBBYMODIFICATION_MAX_ATTRIBUTES` is 64, but the backend service limit is 100. The SDK constant is the per-modification limit, not the total attribute limit.

## Rate Limits (Per-User)

| Operation | Limit |
|-----------|-------|
| Connect | 30/min |
| Create lobby | 30/min |
| Delete lobby | 30/min |
| Join lobby | 30/min |
| Read lobby data | 100/min |
| Update lobby attributes | 100/min |
| Update member attributes | 100/min |
| Change lobby settings | 30/min |
| Invite a user | 30/min |
| Delete invitation | 30/min |
| Kick a player | 30/min |
| Promote member | 30/min |
| Find lobbies | 30/min |
| Get lobby by ID | 100/min |
| Find lobbies by user | 30/min |
| Find invitations by user | 30/min |

## See Also

- [Lobbies & Sessions Introduction](ref-multiplayer.md) — When to use lobbies vs sessions
- [Voice Interface](ref-voice.md) — Voice chat with lobbies
- [RTC Data Interface](ref-rtcdata.md) — Data channel over voice rooms
