# EOS Service Limits & Rate Limits

Complete reference of all Epic Online Services hard limits, SDK constants, and rate limits.
SDK version: **1.18.1.2** | Last updated: 2026-02-06

> All SDK constants verified from `EOSSDK/Source/Generated/` source files. Rate limits from [official EOS documentation](https://dev.epicgames.com/docs/game-services/lobbies).

---

## Pricing

| Item | Value |
|------|-------|
| Cost | **Free** — no royalties, no hosting fees, no bandwidth fees |
| CCU cap | No publicly documented hard cap |
| Relay bandwidth cap | No publicly documented hard cap |
| Paid tiers | None |

---

## Lobbies

| Limit | Value | SDK Constant |
|-------|-------|--------------|
| Max members per lobby | **64** | `MAX_LOBBY_MEMBERS` |
| Max lobbies per user | **16** | `MAX_LOBBIES` |
| Max lobby attributes | **64** (SDK) / **100** (server) | `LOBBYMODIFICATION_MAX_ATTRIBUTES` |
| Max member attributes | **64** (SDK) / **100** (server) | same constant |
| Attribute key max length | **64 chars** | `LOBBYMODIFICATION_MAX_ATTRIBUTE_LENGTH` |
| Attribute string value max length | **1,000 chars** | (server-side, not in SDK) |
| Attribute data types | int64, double, bool, UTF-8 string | `AttributeData` struct |
| Max search results | **200** (SDK) / **256** (server) | `MAX_SEARCH_RESULTS` |
| Lobby ID override length | **4–60 chars** | `MIN/MAX_LOBBYIDOVERRIDE_LENGTH` |
| Invite ID max length | **64 chars** | `INVITEID_MAX_LENGTH` |
| Connect string buffer | **256 bytes** | `GETCONNECTSTRING_BUFFER_SIZE` |
| New lobby indexing delay | **up to 3 seconds** | (documentation) |

### Lobby Rate Limits (per user, per minute)

| Operation | Limit |
|-----------|-------|
| Create / Delete / Join lobby | 30/min |
| Change lobby settings | 30/min |
| Invite / Delete invitation | 30/min |
| Kick a player | 30/min |
| Promote member to owner | 30/min |
| Find lobbies | 30/min |
| Find lobbies/invitations by user | 30/min |
| Read lobby data | 100/min |
| Update lobby attributes | 100/min |
| Update member attributes | 100/min |
| Get lobby by ID | 100/min |

---

## Sessions

| Limit | Value | SDK Constant |
|-------|-------|--------------|
| Max registered players | **1,000** | `MAXREGISTEREDPLAYERS` |
| Max sessions per user | **16** | (documentation) |
| Max session attributes | **64** (SDK) / **100** (server) | `SESSIONMODIFICATION_MAX_SESSION_ATTRIBUTES` |
| Attribute name max length | **64 chars** (SDK) / **1,000 chars** (server) | `SESSIONMODIFICATION_MAX_SESSION_ATTRIBUTE_LENGTH` |
| Max search results | **200** | `MAX_SEARCH_RESULTS` |
| Session ID override length | **16–64 chars** | `MIN/MAX_SESSIONIDOVERRIDE_LENGTH` |
| Invite ID max length | **64 chars** | `INVITEID_MAX_LENGTH` |
| Indexing delay | **up to 3 seconds** | (documentation) |

### Session Rate Limits (per user, per minute)

| Operation | Limit |
|-----------|-------|
| Create / Delete / Update session | 30/min |
| Start / Stop session | 30/min |
| Add / Remove players | 100/min |
| Send invites | 100/min |
| Filter sessions | 30/min |

---

## P2P (NAT Traversal & Relay)

| Limit | Value | SDK Constant |
|-------|-------|--------------|
| Max packet size | **1,170 bytes** | `MAX_PACKET_SIZE` |
| Max connections per remote peer | **32** | `MAX_CONNECTIONS` |
| Channels | **0–255** (byte) | `SendPacketOptions.Channel` |
| Socket name max length | **32 chars** (33-byte buffer) | `SOCKETID_SOCKETNAME_SIZE` |
| Default port | **7777** | `SetPortRangeOptions` |
| Default port range | **7777–7876** (100 ports) | Port + MaxAdditionalPortsToTry |
| Unlimited queue sentinel | **0** | `MAX_QUEUE_SIZE_UNLIMITED` |
| Network interruption threshold | **>30 seconds** packet loss | (documentation) |
| Protocol stack | **UDP → DTLS → SCTP** | (documentation) |

### Reliability Modes

| Mode | Behavior |
|------|----------|
| `UnreliableUnordered` | Sent once, may arrive out of order |
| `ReliableUnordered` | Retransmitted, may arrive out of order |
| `ReliableOrdered` | Retransmitted, arrives in order |

### Relay Control

| Mode | Behavior |
|------|----------|
| `NoRelays` | Direct only; restrictive NATs may fail |
| `AllowRelays` (default) | Direct first, relay fallback |
| `ForceRelays` | Relay only; hides IP, adds latency |

> `NoRelays` + `ForceRelays` = **INCOMPATIBLE** (connection will fail)

### NAT Types

| Type | Direct Connect |
|------|---------------|
| Open | All peers |
| Moderate | Open + Moderate |
| Strict | Open only |

---

## Voice / RTC

| Limit | Value | Source |
|-------|-------|--------|
| Max room size (standalone/trusted server) | **64** | SDK 1.16+ (was 16 before 1.16) |
| Voice with lobbies | **16** | Lobby-managed voice rooms |
| Max requests per user/minute | **50** | Voice interface rate limit |
| Audio buffer duration | **10 ms** | Documentation |
| Audio format | **Signed 16-bit interleaved PCM** (`short[]`) | SDK |
| Audio channels | Configurable (`uint`) | SDK |
| Audio sample rate | Configurable (`uint`) | SDK |
| Volume range | **0–100** (50 = unmodified, 100 = 2x gain) | Documentation |
| Codec | **Opus** (WebRTC-based) | Architecture docs |
| Positional audio | **Not supported** | Community confirmed |
| RTC option key max | **256 chars** | `OPTION_KEY_MAXCHARCOUNT` |
| RTC option value max | **256 chars** | `OPTION_VALUE_MAXCHARCOUNT` |
| Participant metadata key max | **256 chars** | `PARTICIPANTMETADATA_KEY_MAXCHARCOUNT` |
| Participant metadata value max | **256 chars** | `PARTICIPANTMETADATA_VALUE_MAXCHARCOUNT` |

### RTCData (Data Channel)

| Limit | Value | Source |
|-------|-------|--------|
| Max packet size | **1,170 bytes** | `RTCDataInterface.MAX_PACKET_SIZE` |
| Max throughput | **~500 messages/sec** | Documentation (exceeding disconnects voice) |
| Enable flag | Must set `ENABLE_DATACHANNEL` at room join | Cannot enable retroactively |
| Delivery model | Broadcast to all participants | No point-to-point |

---

## Player Data Storage

| Limit | Value | Source |
|-------|-------|--------|
| Filename max length | **64 bytes** | `FILENAME_MAX_LENGTH_BYTES` |
| Max individual file size | **200 MB** | Documentation |
| Max total storage per user | **400 MB** | Documentation |
| Max files per user | **1,000** | Documentation |
| Throttle behavior | `PlayerDataStorageUserThrottled` when >400 MB | Documentation |

---

## Title Storage

| Limit | Value | Source |
|-------|-------|--------|
| Filename max length | **64 bytes** | `FILENAME_MAX_LENGTH_BYTES` |
| Max total storage per deployment | **10 GB** | Documentation |
| Encryption key length | **64 hex characters** | Documentation |
| Tag naming | Alphanumeric ASCII + `!-_*'()` | Documentation |

---

## Stats

| Limit | Value | Source |
|-------|-------|--------|
| Max stats per deployment | **500** | Documentation |
| Max ingest stats per call | **3,000** | `MAX_INGEST_STATS` |
| Max query stats per call | **1,000** | `MAX_QUERY_STATS` |
| Stat name max length | **256 chars** | Documentation |
| Stat value type | **32-bit integer** | Documentation |

### Stats Rate Limits

| Operation | Per-User | Per-Deployment |
|-----------|----------|----------------|
| Ingest stats | 60/min, 500 stats/request | 1 req per 5 Client IDs/min |
| Get stats by Player ID | 100/min | — |
| Get stats by Player IDs | 100/min, 64 players/req, 25 stats/player | — |
| Create stat | — | 100/min |
| Delete stat | — | 100/min |

---

## Leaderboards

| Limit | Value | Source |
|-------|-------|--------|
| Global entries retained | **1,000** (+1,000 overflow = 2,000 total) | Documentation |
| Max milestones per deployment | **100** | Documentation |

### Leaderboard Rate Limits

| Operation | Per-User | Per-Deployment |
|-----------|----------|----------------|
| Get single value | 100/min | — |
| Get all values | 10/min | — |
| Create leaderboard | — | 100/min |
| Delete leaderboard | — | 100/min |

---

## Achievements

| Limit | Value |
|-------|-------|
| Total per deployment | **1,000** |
| Stats per achievement (auto-unlock) | **3** |
| Achievement ID max length | **256 chars** |
| Localized text max length | **256 chars** |
| Localized text variations | **22** (overlay supports 16 languages) |
| Icon max file size | **1.02 MB** |
| Icon max resolution | **1024×1024 px** |
| Icon recommended resolution | **192×192 px** |
| Supported icon formats | PNG, JPG, BMP, GIF (non-animated) |

### Achievement Rate Limits

| Operation | Per-User |
|-----------|----------|
| Get all definitions | 10/min |
| Get one definition | 100/min |
| Get player's achievements | 100/min |
| Unlock achievement | 100/min |
| Create/Delete achievement | 100/min (per-deployment) |

---

## Presence

| Limit | Value | SDK Constant |
|-------|-------|--------------|
| Max data keys | **32** | `DATA_MAX_KEYS` |
| Max key length | **64 chars** | `DATA_MAX_KEY_LENGTH` |
| Max value length | **255 chars** | `DATA_MAX_VALUE_LENGTH` |
| Rich text max length | **255 chars** | `RICH_TEXT_MAX_VALUE_LENGTH` |
| Join info max length | **255 chars** | `PRESENCEMODIFICATION_JOININFO_MAX_LENGTH` |

---

## Friends

| Limit | Value |
|-------|-------|
| Max friends per Epic Account | **1,000** |
| Max pending incoming requests | **500** |
| Max pending outgoing requests | **500** |

---

## Identity / Users

| Limit | Value | SDK Constant |
|-------|-------|--------------|
| Epic Account ID max length | **32 chars** | `EPICACCOUNTID_MAX_LENGTH` |
| Product User ID max length | **32 chars** | `PRODUCTUSERID_MAX_LENGTH` |
| Display name max characters | **16** | `MAX_DISPLAYNAME_CHARACTERS` |
| Display name max UTF-8 length | **64 bytes** | `MAX_DISPLAYNAME_UTF8_LENGTH` |
| Login display name max length | **32 chars** | `USERLOGININFO_DISPLAYNAME_MAX_LENGTH` |
| Device model max length | **64 chars** | `CREATEDEVICEID_DEVICEMODEL_MAX_LENGTH` |
| External account ID max length | **256 chars** | `EXTERNAL_ACCOUNT_ID_MAX_LENGTH` |
| Max account IDs per query | **128** | `QUERYEXTERNALACCOUNTMAPPINGS_MAX_ACCOUNT_IDS` |

---

## Reports

| Limit | Value | SDK Constant |
|-------|-------|--------------|
| Report context max length | **4,096 chars** | `REPORTCONTEXT_MAX_LENGTH` |
| Report message max length | **512 chars** | `REPORTMESSAGE_MAX_LENGTH` |

---

## Custom Invites

| Limit | Value | SDK Constant |
|-------|-------|--------------|
| Max payload length | **500 chars** | `MAX_PAYLOAD_LENGTH` |

---

## Anti-Cheat

| Limit | Value | SDK Constant |
|-------|-------|--------------|
| Message to server max size | **512 bytes** | `ONMESSAGETOSERVERCALLBACK_MAX_MESSAGE_SIZE` |
| Message to client max size | **512 bytes** | `ONMESSAGETOCLIENTCALLBACK_MAX_MESSAGE_SIZE` |
| Message to peer max size | **512 bytes** | `ONMESSAGETOPEERCALLBACK_MAX_MESSAGE_SIZE` |
| Register timeout range | **10–120 sec** | `MIN/MAX_REGISTERTIMEOUT` |
| Peer auth timeout range | **40–120 sec** | `MIN/MAX_AUTHENTICATIONTIMEOUT` |
| Log event string max | **39 chars** | `LOGEVENT_STRING_MAX_LENGTH` |
| Weapon name max | **32 chars** | `LOGPLAYERUSEWEAPON_WEAPONNAME_MAX_LENGTH` |
| Max registered event params | **12** | `REGISTEREVENT_MAX_PARAMDEFSCOUNT` |

---

## Ecom (Commerce)

| Limit | Value | SDK Constant |
|-------|-------|--------------|
| Checkout max entries | **10** | `CHECKOUT_MAX_ENTRIES` |
| Entitlement ID max length | **32 chars** | `ENTITLEMENTID_MAX_LENGTH` |
| Query entitlements max IDs | **256** | `QUERYENTITLEMENTS_MAX_ENTITLEMENT_IDS` |
| Query ownership max catalog IDs | **400** | `QUERYOWNERSHIP_MAX_CATALOG_IDS` |
| Query ownership max sandbox IDs | **10** | `QUERYOWNERSHIP_MAX_SANDBOX_IDS` |
| Redeem entitlements max IDs | **32** | `REDEEMENTITLEMENTS_MAX_IDS` |
| Transaction ID max length | **64 chars** | `TRANSACTIONID_MAXIMUM_LENGTH` |

---

## KWS (Kids Web Services)

| Limit | Value | SDK Constant |
|-------|-------|--------------|
| Max permissions | **16** | `MAX_PERMISSIONS` |
| Max permission name length | **32 chars** | `MAX_PERMISSION_LENGTH` |

---

## Platform / Common

| Limit | Value | SDK Constant |
|-------|-------|--------------|
| Client ID max length | **64 chars** | `CLIENTCREDENTIALS_CLIENTID_MAX_LENGTH` |
| Client secret max length | **64 chars** | `CLIENTCREDENTIALS_CLIENTSECRET_MAX_LENGTH` |
| Product name max length | **64 chars** | `INITIALIZEOPTIONS_PRODUCTNAME_MAX_LENGTH` |
| Product version max length | **64 chars** | `INITIALIZEOPTIONS_PRODUCTVERSION_MAX_LENGTH` |
| Deployment ID max length | **64 chars** | `OPTIONS_DEPLOYMENTID_MAX_LENGTH` |
| Encryption key length | **64 chars** | `OPTIONS_ENCRYPTIONKEY_LENGTH` |
| Country code max length | **4 chars** | `COUNTRYCODE_MAX_LENGTH` |
| Locale code max length | **9 chars** | `LOCALECODE_MAX_LENGTH` |

---

## General Throttling Behavior

All EOS services use a two-tier throttling system:

1. **Client-side:** SDK self-throttles locally, returning `EOS_TooManyRequests`
2. **Server-side:** Backend returns HTTP 429 with `Retry-After` header
3. **Per-deployment quotas** are either fixed or scale with CCU

All strings must be **UTF-8** encoded.

---

## Quick Reference Cheat Sheet

```
LOBBIES
  Members per lobby ........... 64
  Lobbies per user ............ 16
  Attributes per lobby ........ 64 (SDK) / 100 (server)
  Attribute key length ........ 64 chars
  Attribute value length ...... 1,000 chars
  Search results .............. 200

P2P
  Packet size ................. 1,170 bytes
  Connections per peer ........ 32
  Channels .................... 0-255
  Protocol .................... UDP → DTLS → SCTP

VOICE
  Standalone room ............. 64 (SDK 1.16+)
  Lobby-managed voice ......... 16
  Audio format ................ 16-bit PCM, 10ms buffers

RTCDATA
  Packet size ................. 1,170 bytes
  Max throughput .............. ~500 msg/sec

STORAGE
  Player: 1,000 files, 200 MB/file, 400 MB total
  Title: 10 GB per deployment

SOCIAL
  Friends ..................... 1,000
  Presence keys ............... 32
  Display name ................ 16 chars

SESSIONS
  Registered players .......... 1,000
  Sessions per user ........... 16
  Attributes .................. 64 (SDK) / 100 (server)
  No host migration!
```

---

## SDK vs Server-Side Discrepancies

| Limit | SDK Constant | Server Docs |
|-------|-------------|-------------|
| Lobby attributes | 64 | 100 |
| Lobby member attributes | 64 | 100 |
| Lobby search results | 200 | 256 |
| Session attributes | 64 | 100 |
| Session attribute name length | 64 | 1,000 |

The SDK constants represent client-side validation. The server may accept higher values, but the SDK will reject them before sending.

---

## Sources

- [EOS Lobbies Documentation](https://dev.epicgames.com/docs/game-services/lobbies)
- [EOS Sessions Documentation](https://dev.epicgames.com/docs/game-services/sessions)
- [EOS P2P Documentation](https://dev.epicgames.com/docs/game-services/p-2-p)
- [EOS Voice Documentation](https://dev.epicgames.com/docs/game-services/real-time-communication-interface/voice)
- [EOS RTC Data Interface](https://dev.epicgames.com/docs/game-services/real-time-communication-interface/rtc-data-interface)
- [EOS Stats Reference](https://dev.epicgames.com/docs/epic-online-services/player-and-game-data/stats-interface/stats-reference)
- [EOS Leaderboards Reference](https://dev.epicgames.com/docs/game-services/leaderboards/leaderboards-reference)
- [EOS Achievements Reference](https://dev.epicgames.com/docs/en-US/game-services/achievements/achievements-reference)
- [EOS Player Data Storage](https://dev.epicgames.com/docs/game-services/player-data-storage)
- [EOS Title Storage](https://dev.epicgames.com/docs/game-services/title-storage)
- [EOS Conventions and Limitations](https://dev.epicgames.com/docs/epic-online-services/eos-get-started/working-with-the-eos-sdk/conventions-and-limitations)
- [EOS Presence Interface](https://dev.epicgames.com/docs/epic-account-services/eos-presence-interface)
- [Epic Friends List Cap](https://www.epicgames.com/help/en-US/epic-accounts-c5719348850459)
- [Understanding EOS Limitations (Unreal Forums)](https://forums.unrealengine.com/t/understanding-epic-online-services-limitations/495026)
- [Voice Room 16-Player Limit (EOS Help)](https://eoshelp.epicgames.com/s/question/0D54z000080D0LmCAK)
- EOS C# SDK v1.18.1.2 source (`EOSSDK/Source/Generated/`)
