# EOS Multiplayer Overview

> Source: [dev.epicgames.com](https://dev.epicgames.com/docs/epic-online-services/multiplayer/introduction) and [Lobbies & Sessions Intro](https://dev.epicgames.com/docs/epic-online-services/multiplayer/lobbies-and-sessions/lobbies-and-sessions-introduction)

## Multiplayer Interfaces

| Interface | Purpose |
|-----------|---------|
| **P2P (NAT P2P)** | Direct player-to-player connections |
| **Lobby Interface** | Create, join, leave lobbies with persistent connections |
| **Sessions Interface** | Host, discover, manage game sessions |
| **Voice / RTC** | Cross-platform voice chat and text |

All multiplayer services are **EOS Game Services** - players authenticate via any supported identity provider (no Epic account required).

---

## Lobbies vs Sessions

> **Important:** Games using dedicated servers MUST use Sessions. Dedicated servers CANNOT use Lobbies.

### Connection Model

| Feature | Lobbies | Sessions |
|---------|---------|----------|
| Connection type | **Persistent WebSocket** | Per-call HTTP requests |
| Real-time sync | Automatic (push updates) | Manual (must push updates) |
| Player registration | Automatic on join/leave | Manual register/unregister |
| NAT/firewall issues | Possible on restrictive networks | Same |

### Built-in Features

| Feature | Lobbies | Sessions |
|---------|---------|----------|
| Voice chat | Yes | No |
| Host migration | Yes (auto on disconnect) | No |
| Kick members | Yes (owner) | No |
| Searchable attributes | Yes | Yes |
| Player invites | Yes | Yes |
| Presence join | Yes | Yes |
| Multiple simultaneous | Yes (up to 16) | Yes (up to 16) |
| Local-only attributes | Yes (not shared) | Yes |

### When to Use What

| Scenario | Use |
|----------|-----|
| P2P game, no dedicated server | **Lobbies** |
| Dedicated server game | **Sessions** (required) |
| Pre-game team formation | **Lobbies** |
| Quick matchmaking only | **Sessions** |
| Voice chat needed | **Lobbies** (or trusted server voice) |
| Both lobby + dedicated server | Lobby for social, Session for gameplay |

### Performance Tips

1. Set non-searchable attributes to **local only** (lobby local attrs are NOT shared with members)
2. Assign **bucket IDs** to improve search performance (group by region, game type)
3. Use sessions for **public-only searches** if that's all you need
4. Prefer **invites over searches** when possible (faster joining)

---

## Common Use Cases

### Lobby + Dedicated Server
1. Dedicated server registers a session
2. Players A & B are in lobby
3. Player A finds server session, shares IP via lobby attribute
4. Both connect to dedicated server
5. Server registers players, game starts

### P2P via Lobby
1. Players A & B in lobby
2. Player A acts as listen server
3. Player A shares IP through lobby
4. Player B connects peer-to-peer
5. Game starts

### Direct Session Join
1. Server creates session
2. Players search, find session
3. Join session, connect using session IP
4. Server registers players

---

## EOS Service Types

| Type | Auth | Requires Epic Account? | Includes |
|------|------|----------------------|----------|
| **Game Services** | Connect Interface | No (any identity provider) | Multiplayer, Stats, Storage, Anti-Cheat |
| **Epic Account Services** | Auth Interface | Yes | Friends, Presence, Social Overlay |
