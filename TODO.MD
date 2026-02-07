# TODO

## High Priority

### Parity with transport demo
- Ensure all features from the FishNet EOS Native Transport demo work correctly with this package
- Quick match, lobbies, voice, join by code

### Lean P2P Networking Layer (aka "DIY Fusion w/ blackjack and hookers")
Build a lightweight high-level networking layer on top of the raw EOS P2P interface. No FishNet, no Photon, no middleware — just EOS packets and vibes.

**Core transport features:**
- [x] Packet fragmentation (handle payloads larger than EOS max packet size)
- [x] Packet packing/batching (combine multiple small messages per frame)
- [x] SyncList\<T\> (operation-based collection sync with delta tracking)
- [x] SyncDictionary\<TKey, TValue\> (key-value collection sync with delta tracking)
- [x] DestroyWithOwner lifetime flag (player objects auto-despawn on owner disconnect)
- [x] Sequence-based stale packet rejection (per-object sequence numbers)
- [x] RPC migration buffer (queue RPCs during host transition, flush after)
- [ ] Packet compression (PurrNet-style pickling / LZ4 / similar)
- [x] BufferLast (only deliver most recent state, drop stale packets)
- [x] Object pooling (reuse network objects instead of destroy/create)
- [ ] Scene management (networked scene loading, additive scenes)

**High-level networking:**
- [x] Generic RPCs — `SendCustomNetworkEvent(string functionName, args...)` (VRChat Udon / Photon PUN style)
- [x] SyncVar<T> — dirty-tracked state with owner-write guard and OnChanged callbacks
- [x] NetworkObject / NetworkBehaviour — identity, ownership, spawn/despawn
- [x] NetworkTransform — hybrid sync: spring physics, buffered interpolation, extrapolation, distance LOD
- [x] Distance LOD (3-tier: Full/Tweened/Simple with hysteresis, from PhysicsNetworkTransform)
- [x] Extrapolation (velocity prediction with gravity/drag, time/distance limits)
- [x] Rest detection (timeout-based, stops extrapolation on idle objects)
- [x] Teleport API (snap + buffer clear + spring reset)
- [x] NetworkAnimator — packed SyncVar for float/int/bool params, trigger RPCs
- [x] EasySync — Normcore-style no-code property sync (Inspector checkboxes, reflection-based)
- [x] Scene object auto-ownership — pre-existing scene objects auto-assign to host
- [ ] EasySync v2 — per-property WriteAccess (Host/All), interpolation toggle, "Convert to Code" export
- [ ] Shared room data (live room properties, like Photon shared mode)
- [ ] Per-user data (player properties synced across peers)
- [ ] Master client verification (optional server-authority validation)
- [ ] Spatial hash (interest management — only sync nearby objects)
- [ ] Tick-based simulation (deterministic tick rate, decoupled from frame rate)
- [ ] Connection statistics / network profiler (RTT, packet loss, bandwidth)
- [ ] Auto-reconnect (automatic reconnection with state recovery)

**Migration & Authority:**
- [x] Zero-hitch host migration (no scene takedown, own the object lifetime)
- [x] Peer-authority model (each peer owns their objects, master client arbitrates)
- [x] Late-join snapshots (new peers receive full state dump from host)
- [ ] P2P direct connections for fast RPCs (skip relay when possible)

**Prediction & Rollback:**
- [ ] Client-side prediction (input prediction for owned objects)
- [ ] Collider rollback (lag compensation for hit detection)
- [ ] Study Valve networking docs (Source/CS:GO PDFs) for rollback architecture
- [ ] Study Photon PUN architecture for shared-mode patterns

**Target projects:**
- [ ] MonkePortals
- [ ] Restore dead FishNet EOS games/projects
- [ ] "Turnkey multiplayer" / "Wheelz-ready multiplayer"

## Medium Priority

### Discord Integration
- [ ] Discord store integration
- [ ] Bot integration for game state

## Low Priority

### Testing

**Platform matrix:**
- [ ] Windows x64 (Editor + Build)
- [ ] Windows x86 (Build only)
- [ ] macOS Universal (Editor + Build)
- [ ] Linux x64 (Editor + Build)
- [ ] Linux ARM64 (Build only)
- [ ] iOS ARM64 (Build + device)
- [ ] Android (Build + device — verify extractNativeLibs + desugaring)

**Auth & Login:**
- [ ] Dev auth tool login (Editor)
- [ ] Account portal login (Build)
- [ ] Device ID login (ParrelSync clones, Android)
- [ ] ParrelSync clone login flow end-to-end (unique device IDs per clone)
- [ ] Logout + re-login without restart

**Lobbies:**
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
- [ ] LobbyOptions fluent builder (presets: QuickMatch, ForGameMode, ForSkillRange)

**Voice / RTC:**
- [ ] Voice auto-connect on lobby join (when voice enabled)
- [ ] Mute/unmute self
- [ ] Mute/unmute remote participants
- [ ] Mic level bar reflects actual input (Unity Microphone API)
- [ ] Audio device switching (input + output)
- [ ] Audio device hotplug detection (OnAudioDevicesChanged)
- [ ] XAudio2 DLL loads correctly (Windows — all 4 candidate paths)
- [ ] Voice disconnect on lobby leave
- [ ] Voice with 3+ participants

**Chat:**
- [ ] Lobby chat send/receive
- [ ] Chat message history on join
- [ ] Chat with multiple participants
- [ ] Enter-to-send in both OnGUI and Canvas UI
- [ ] Global chat (EOSGlobalChatManager) join/leave/send

**P2P Ball Demo:**
- [ ] Auto-create EOSP2PManager + P2PDemoManager on lobby join
- [ ] Spawn local ball on lobby join
- [ ] Remote balls appear when peers connect (join packet exchange)
- [ ] WASD movement + jump syncs across peers
- [ ] Spring physics smoothing on remote balls
- [ ] Ball despawn on peer disconnect (leave packet)
- [ ] 3+ player mesh (all peers see all balls)
- [ ] Works with both Input System and legacy Input

**UI — F1 OnGUI Overlay (EOSNativeStatusUI):**
- [ ] F1 toggle opens/closes overlay
- [ ] All 6 tabs render without errors (Status, Lobbies, Voice, Social, Stats, Tools)
- [ ] Report popup modal (from lobby member list)
- [ ] Player profile popup (info button per member)
- [ ] Lobby chat input field visible and functional
- [ ] Stats query/ingest
- [ ] Leaderboard display
- [ ] Achievement progress display
- [ ] Cloud storage file list/write/delete
- [ ] Replay list/playback

**UI — Canvas UI (EOSNativeCanvasUI):**
- [ ] EOS button toggle (bottom-right, 80x80)
- [ ] 3-finger tap toggle (mobile)
- [ ] All 4 tabs render without errors (Status, Lobbies, Voice, Social)
- [ ] InputField text visible while typing (chat, lobby name)
- [ ] Lobby chat scroll + Enter-to-send
- [ ] Mic level bar animates smoothly
- [ ] Audio device picker buttons (green = selected)
- [ ] No text flickering in chat log or console
- [ ] Works on Android (where OnGUI may not render)

**UI — Console (EOSNativeConsole):**
- [ ] Toggle button with error count badge (bottom-left)
- [ ] Log/Warning/Error filter buttons
- [ ] Collapse duplicate messages
- [ ] Clear button
- [ ] Max 200 entries respected
- [ ] Color-coded by log type

**UI — Overlay Mode (EOSManager):**
- [ ] Auto mode — Canvas on mobile, OnGUI on desktop
- [ ] OnGUI mode — only F1 overlay
- [ ] Canvas mode — only Canvas UI
- [ ] Both mode — both active simultaneously
- [ ] None mode — no UI created
- [ ] Console toggle independent of overlay mode

**Ported Managers (smoke tests):**
- [ ] EOSAfkManager — idle detection fires, host immunity works
- [ ] EOSVoteKickManager — vote initiates, threshold triggers kick
- [ ] EOSMapVoteManager — votes tally, winner selected
- [ ] EOSRematchManager — rematch offer appears, votes counted
- [ ] EOSBackfillManager — backfill request created, joinable
- [ ] EOSMetrics — session begin/end telemetry sent
- [ ] EOSReplayHighlights — highlight detection triggers on events
- [ ] EOSReplayVoicePlayer — voice plays during replay
- [ ] EOSReplayVoiceRecorder — voice recorded during session

**Singleton lifecycle:**
- [ ] All auto-create singletons parent under EOSManager in hierarchy
- [ ] No duplicate singletons on scene reload
- [ ] DontDestroyOnLoad fallback when EOSManager not yet created
- [ ] Clean shutdown (no OnDestroy errors/crashes)

**Installation methods:**
- [ ] UPM git URL install (fresh project)
- [ ] Embedded in Assets/ (copy into project)
- [ ] EOS_DISABLE define strips compilation
- [ ] Setup Wizard opens and validates credentials

## Completed

- [x] Fix Quick Match to auto-host when no match found
- [x] Add voice toggle to Inspector host controls
- [x] ParrelSync support (device ID uniqueness per clone)
- [x] XAudio2 DLL path resolution for UPM layout
- [x] Native DLL loading path fix (Path.GetFullPath for virtual paths)
- [x] Dark theme UI overhaul for F1 overlay (foldouts, level bars, dark backgrounds)
- [x] Mic/speaker device selection (EOSVoiceManager + F1 overlay Voice tab)
- [x] Host migration toggle in Inspector and F1 overlay
- [x] Port EOSGlobalChatManager from FishNet
- [x] Port EOSReplayHighlights from FishNet
- [x] Port EOSReplayVoicePlayer from FishNet
- [x] Port EOSReplayVoiceRecorder from FishNet
- [x] Port EOSMetrics from FishNet
- [x] Create CHANGELOG.MD
- [x] Expand F1 overlay from 4 to 6 tabs (17 new sections ported from FishNet UI)
- [x] Auto-create EOSLobbyChatManager singleton
- [x] All foldouts default to expanded
- [x] Fix ranked matchmaking OnDestroy crash during shutdown
- [x] Port EOSAfkManager from FishNet (framework-agnostic)
- [x] Port EOSVoteKickManager from FishNet (lobby attributes)
- [x] Port EOSMapVoteManager from FishNet (lobby attributes)
- [x] Port EOSRematchManager from FishNet (lobby system)
- [x] Port EOSBackfillManager from FishNet (lobby search)
- [x] Add KickMemberAsync to EOSLobbyManager
- [x] Write reputation.md docs
- [x] Write match-history.md docs
- [x] Write globalchat.md docs
- [x] Write highlights.md docs
- [x] Write votekick.md, mapvote.md, backfill.md, rematch.md, afk.md docs
- [x] Update _sidebar.md with Lobby Systems section
- [x] Player profile popup in F1 overlay (info button per lobby member)
- [x] Inspector lobby name field (was unnamed)
- [x] Inspector max players field (was hardcoded to 4, now 2-64)
- [x] ParrelSync docs page (parrelsync.md)
