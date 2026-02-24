# EOS Sessions Interface Reference

> Source: [dev.epicgames.com](https://dev.epicgames.com/docs/epic-online-services/multiplayer/lobbies-and-sessions/sessions-interface/sessions-intro)

## Overview

The Sessions Interface enables players to host, find, and interact with online gaming sessions. Sessions can be brief (fill slots, disband) or extended (cycle through multiple matches). **Dedicated servers MUST use Sessions** - they cannot use Lobbies.

> Sessions use per-call HTTP requests (not persistent WebSocket like Lobbies). State updates must be pushed manually.

## Getting Started

Acquire `EOS_HSessions` via `EOS_Platform_GetSessionsInterface`. The platform handle must be ticking for callbacks.

## Session Lifecycle

1. User or dedicated server creates the session and sets initial state
2. Owner may invite users to join
3. Other users join and leave (owner registers/unregisters them)
4. Owner updates game-specific data
5. Owner destroys the session

> **Indexing delay:** Newly created sessions take up to **3 seconds** to appear in search results.

## Active Sessions

Active sessions form locally on each participant's system. Each has a unique `EOS_HActiveSession` handle and a local name (e.g. "Party" or "Game").

```csharp
// Get active session handle
var handleOptions = new CopyActiveSessionHandleOptions { SessionName = "Game" };
var activeSession = sessionsInterface.CopyActiveSessionHandle(ref handleOptions);

// Get session info
var info = activeSession.CopyInfo(new ActiveSession.CopyInfoOptions());
// ... use info ...
info.Release();
```

## Create a Session

Three steps:

1. **Create modification handle:** `EOS_Sessions_CreateSessionModification`
2. **Set properties:** Use modification handle to set attributes, permissions, etc.
3. **Apply:** `EOS_Sessions_UpdateSession` with the modification handle

## Invite Users

Registered session members can invite others:

```csharp
var inviteOptions = new SendInviteOptions {
    SessionName = "Game",
    LocalUserId = localPuid,
    TargetUserId = friendPuid
};
sessionsInterface.SendInvite(ref inviteOptions, null, OnInviteSent);
```

> Max invitation ID string length: **64 characters**

### Accept / Reject

- **Accept:** Join the session using `EOS_HSessionDetails` from the invitation (no dedicated accept function)
- **Reject:** `EOS_Sessions_RejectInvite` - permanently deletes the invitation

### Query Invites

`EOS_Sessions_QueryInvites` refreshes the local cache. Use `GetInviteCount` + `GetInviteIdByIndex` to enumerate.

## Join a Session

```csharp
var joinOptions = new JoinSessionOptions {
    SessionName = "Game",           // unique local name
    SessionHandle = sessionDetails, // EOS_HSessionDetails
    LocalUserId = localPuid,
    bPresenceEnabled = true
};
sessionsInterface.JoinSession(ref joinOptions, null, OnJoinComplete);
```

### Register / Unregister Players

The session **owner** must manually register/unregister players:

```csharp
// Register
var regOptions = new RegisterPlayersOptions {
    SessionName = "Game",
    PlayersToRegister = new[] { playerPuid }
};
sessionsInterface.RegisterPlayers(ref regOptions, null, OnRegistered);
```

The callback includes a `SanctionedPlayers` list - players denied registration due to active sanctions. No error is returned; you must check this list manually.

> **Warning:** Registered players in `PublicAdvertised` sessions are publicly discoverable via `SessionSearch_SetTargetUserId`. External applications could discover server IPs.

### Unregister

```csharp
var unregOptions = new UnregisterPlayersOptions {
    SessionName = "Game",
    PlayersToUnregister = new[] { leavingPuid }
};
sessionsInterface.UnregisterPlayers(ref unregOptions, null, OnUnregistered);
```

## Start / End Play

```csharp
// Start match
sessionsInterface.StartSession(new StartSessionOptions { SessionName = "Game" }, ...);

// End match (returns to pre-start state, does NOT destroy)
sessionsInterface.EndSession(new EndSessionOptions { SessionName = "Game" }, ...);
```

While playing, the backend rejects join attempts if "Join in Progress" is disabled.

## Leave a Session

No dedicated leave function. Destroy your local session via `DestroySession`.

## Destroy a Session

```csharp
sessionsInterface.DestroySession(new DestroySessionOptions { SessionName = "Game" }, ...);
```

> Join requests may arrive after calling destroy but before backend destruction. Reject these players or shut down networking.

## Host Migration

**Sessions do NOT support host migration.** If the owner loses connection, the session is orphaned and no one else can manage it.

## Remote Client Mirroring

Remote clients can optionally mirror these calls to sync local state (without affecting backend):

- `StartSession` / `EndSession`
- `RegisterPlayers` / `UnregisterPlayers`

## Service Limits

| Feature | Limit |
|---------|-------|
| Max concurrent players per session | **1000** |
| Max session attributes | **100** |
| Max attribute name length | **1000 characters** |
| Max sessions per user | **16** |
| Max invitation ID length | **64 characters** |
| Indexing delay | up to **3 seconds** |

## Rate Limits

### Per-User

| Operation | Limit |
|-----------|-------|
| Create session | 30/min |
| Delete session | 30/min |
| Update session | 30/min |
| Add/remove players | 100/min |
| Start/stop session | 30/min |
| Invite a user | 100/min |
| Filter sessions | 30/min |

### Per-Deployment

| Operation | Limit |
|-----------|-------|
| Create session | 30 per 1 CCU/min |
| Delete session | 30 per 1 CCU/min |
| Update session | 30 per 1 CCU/min |
| Add/remove players | 30 per 1 CCU/min |
| Start/stop session | 30 per 1 CCU/min |
| Invite a user | 30 per 1 CCU/min |
| Filter sessions | 30 per 1 CCU/min |

## Lobbies vs Sessions

| Feature | Lobbies | Sessions |
|---------|---------|----------|
| Connection | Persistent WebSocket | Per-call HTTP |
| Real-time sync | Automatic (push) | Manual (must push) |
| Player registration | Automatic | Manual |
| Voice chat | Yes | No |
| Host migration | Yes | **No** |
| Max players | 64 | **1000** |
| Dedicated server | **No** | Yes (required) |

## FAQ

**Q: Why can't I find the session I just created?**
A: Indexing takes up to 3 seconds.

**Q: Why do two players see different session lists?**
A: EOS applies deliberate jittering (random delays) to avoid race conditions. No two players receive identical lists simultaneously.

**Q: Where can I see all sessions in my game?**
A: Developer Portal > Your Product > Epic Online Services > Multiplayer > Sessions.

## See Also

- [Multiplayer Overview](ref-multiplayer.md) - Lobbies vs Sessions comparison
- [Lobby Interface API](ref-lobby-api.md) - Lobby alternative for P2P games
