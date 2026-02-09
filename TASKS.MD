# TASKS

All open tasks for EOS-Native, organized by category and priority.

## Networking Features

- [ ] EasySync v2 — per-property WriteAccess (Host/All), interpolation toggle, "Convert to Code" export
- [x] Spatial hash — interest management, only sync nearby objects (v2.20.0)
- [ ] Tick-based simulation — deterministic tick rate, decoupled from frame rate
- [ ] RPC registration on NetworkObject — drop NetworkBehaviour requirement
- [ ] NetworkBehaviour-only architecture — each NB gets its own ID, no separate NetworkObject

## SyncVar LOD System (community: AlmightyMikkel)

- [x] SyncVar LOD tiers — `SyncVarLOD` component with configurable distance tiers and sync rate multipliers (v2.18.0)
- [x] Distance-based sync throttle — auto-discovers nearest peer, throttles MarkDirty() on owner side (v2.18.0)
- [ ] Per-component LOD checkboxes — Inspector UI to toggle what syncs at each tier

## Security & Validation (from security audit)

- [x] Host-validated RPCs — `[NetRpc(Validated = true)]` 2-hop relay through host (v2.18.0)
- [x] Decompression size limit — 1 MB cap on DecompressDeflate output (v2.19.0)
- [x] Join code entropy — configurable length 4-8 digits, default 6 (v2.18.0)
- [x] Per-peer RPC rate limiting — `MaxMessagesPerPeerPerSecond` on NetworkManager (v2.19.0)
- [ ] Network rules — PurrNet-style validation rules
- [x] Password hashing — SHA256+salt for lobby passwords (v2.19.0)
- [ ] Credential storage — move ClientSecret/EncryptionKey out of ScriptableObject plaintext
- [ ] Remove EOSConfig.asset from git — add to .gitignore, document user must create own
- [x] Fragment reassembly limits — max 8 per sender, max index 256 (v2.19.0)
- [x] Batch count validation — max 256 messages per batch (v2.19.0)
- [x] String/byte array length caps — 64KB strings, 1MB byte arrays (v2.19.0)
- [x] Pooled buffer zeroing — Array.Clear on NetWriter.Reset() (v2.19.0)
- [x] Invite payload validation — 512 char limit, printable chars only (v2.19.0)
- [ ] Tighter ProGuard rules — narrow `-keep` to public APIs only

## Spatial Voice

- [ ] Audio occlusion — raycast-based wall muting
- [ ] Grid-based spatial partitioning — for 100+ players
- [ ] Voice priority system — bandwidth management at scale

## Multi-Transport & Crossplay

- [ ] Multi-transport support — EOS + Steam crossplay
- [ ] FishNet transport thin layer — basic lobbies + host migration, OG repo compile fix

## Prediction & Rollback

- [ ] Client-side prediction — input prediction for owned objects
- [ ] Collider rollback — lag compensation for hit detection
- [ ] Study Valve networking docs — Source/CS:GO PDFs for rollback architecture
- [ ] Study Photon PUN architecture — shared-mode patterns

## Server & Cloud

- [ ] Dedicated server support — server joins lobby, server-authoritative validation
- [ ] Cloudscripting via VPS — $5 VPS for cloud validation / Discord bot integration
- [ ] TrontCloud integration — currencies, persistent progression, server-authoritative economy

## Discord Integration

- [ ] Discord store integration
- [ ] Bot integration for game state

## Target Projects

- [ ] MonkePortals
- [ ] Restore dead FishNet EOS games/projects
- [ ] "Turnkey multiplayer" / "Wheelz-ready multiplayer"

## Platform Testing

- [ ] Windows x64 (Editor + Build)
- [ ] Windows x86 (Build only)
- [ ] macOS Universal (Editor + Build)
- [ ] Linux x64 (Editor + Build)
- [ ] Linux ARM64 (Build only)
- [ ] iOS ARM64 (Build + device)

## QA: Auth & Login

- [ ] Dev auth tool login (Editor)
- [ ] Account portal login (Build)
- [ ] Device ID login (ParrelSync clones, Android)
- [ ] ParrelSync clone login flow end-to-end
- [ ] Logout + re-login without restart

## QA: Lobbies

- [ ] Create lobby (named + unnamed)
- [ ] Quick match — join existing lobby
- [ ] Quick match — auto-host when none found
- [ ] Join by lobby code
- [ ] Search lobbies with filters (game mode, region, skill range)
- [ ] Max players enforcement (2-64 range)
- [ ] Leave lobby cleanly
- [ ] Kick member (host only)
- [ ] Host migration (host leaves, new host elected)
- [ ] Public vs invite-only lobbies
- [ ] LobbyOptions fluent builder presets

## QA: Voice / RTC

- [ ] Voice auto-connect on lobby join
- [ ] Mute/unmute self
- [ ] Mute/unmute remote participants
- [ ] Mic level bar reflects actual input
- [ ] Audio device switching (input + output)
- [ ] Audio device hotplug detection
- [ ] XAudio2 DLL loads correctly (Windows)
- [ ] Voice disconnect on lobby leave
- [ ] Voice with 3+ participants

## QA: Chat

- [ ] Lobby chat send/receive
- [ ] Chat message history on join
- [ ] Chat with multiple participants
- [ ] Enter-to-send in both OnGUI and Canvas UI
- [ ] Global chat join/leave/send

## QA: P2P Ball Demo

- [ ] Auto-create managers on lobby join
- [ ] Spawn local ball on lobby join
- [ ] Remote balls appear when peers connect
- [ ] WASD movement + jump syncs across peers
- [ ] Spring physics smoothing on remote balls
- [ ] Ball despawn on peer disconnect
- [ ] 3+ player mesh
- [ ] Works with both Input System and legacy Input
- [ ] Layer 2 SyncVars sync (Score, DisplayName, BallColor)
- [ ] Layer 2 RPCs work (E=color, Q=shockwave, T=chat, R=effect)

## QA: Unit Tests

- [ ] All 210 editor-mode tests pass

## QA: F1 OnGUI Overlay

- [ ] F1 toggle opens/closes overlay
- [ ] All 6 tabs render without errors
- [ ] Report popup modal
- [ ] Player profile popup
- [ ] Lobby chat input field
- [ ] Stats query/ingest
- [ ] Leaderboard display
- [ ] Achievement progress display
- [ ] Cloud storage file list/write/delete
- [ ] Replay list/playback

## QA: Canvas UI

- [ ] EOS button toggle (bottom-right)
- [ ] 3-finger tap toggle (mobile)
- [ ] All 6 tabs render without errors
- [ ] InputField text visible while typing
- [ ] Lobby chat scroll + Enter-to-send
- [ ] Mic level bar animates smoothly
- [ ] Audio device picker buttons
- [ ] No text flickering
- [ ] Works on Android

## QA: Console

- [ ] Toggle button with error count badge
- [ ] Log/Warning/Error filter buttons
- [ ] Collapse duplicate messages
- [ ] Clear button
- [ ] Max 200 entries respected
- [ ] Color-coded by log type

## QA: Overlay Mode

- [ ] Auto mode — Canvas on mobile, OnGUI on desktop
- [ ] OnGUI mode — only F1 overlay
- [ ] Canvas mode — only Canvas UI
- [ ] Both mode — both active
- [ ] None mode — no UI created
- [ ] Console toggle independent

## QA: Ported Managers

- [ ] EOSAfkManager — idle detection + host immunity
- [ ] EOSVoteKickManager — vote + threshold kick
- [ ] EOSMapVoteManager — tally + winner
- [ ] EOSRematchManager — offer + votes
- [ ] EOSBackfillManager — request + joinable
- [ ] EOSMetrics — session telemetry
- [ ] EOSReplayHighlights — event triggers
- [ ] EOSReplayVoicePlayer — playback
- [ ] EOSReplayVoiceRecorder — recording

## QA: Singletons

- [ ] All auto-create singletons parent under EOSManager
- [ ] No duplicate singletons on scene reload
- [ ] DontDestroyOnLoad fallback
- [ ] Clean shutdown (no OnDestroy errors)

## QA: Installation

- [ ] UPM git URL install (fresh project)
- [ ] Embedded in Assets/ (copy into project)
- [ ] EOS_DISABLE define strips compilation
- [ ] Setup Wizard opens and validates credentials
