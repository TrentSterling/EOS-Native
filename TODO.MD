# TODO

## High Priority

### Parity with transport demo
- Ensure all features from the FishNet EOS Native Transport demo work correctly with this package
- Quick match, lobbies, voice, join by code

### Lean P2P Networking Layer (aka "DIY Fusion w/ blackjack and hookers")
Build a lightweight high-level networking layer on top of the raw EOS P2P interface. No FishNet, no Photon, no middleware — just EOS packets and vibes. **And it's 100% FREE.**

**Official target: 16 players** (EOS lobbies cap at 64, but 16 is the sweet spot for all features).
RPCs go direct peer-to-peer (not routed through host like FishNet/Mirror). EOS-first architecture — like what UE does natively with EOS, but for Unity.

**Core transport features:**
- [x] Packet fragmentation (handle payloads larger than EOS max packet size)
- [x] Packet packing/batching (combine multiple small messages per frame)
- [x] SyncList\<T\> (operation-based collection sync with delta tracking)
- [x] SyncDictionary\<TKey, TValue\> (key-value collection sync with delta tracking)
- [x] DestroyWithOwner lifetime flag (player objects auto-despawn on owner disconnect)
- [x] Sequence-based stale packet rejection (per-object sequence numbers)
- [x] RPC migration buffer (queue RPCs during host transition, flush after)
- [x] Packet compression (Deflate with threshold, opt-in)
- [x] BufferLast (only deliver most recent state, drop stale packets)
- [x] Object pooling (reuse network objects instead of destroy/create)
- [x] Scene management (networked scene loading, additive scenes)

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
- [x] [NetRpc] attribute + IL post-processor — typed RPCs with zero-boilerplate (Mirror/FishNet/Fusion style)
- [x] EasySync v2 — per-property WriteAccess (Host/All), interpolation toggle, "Convert to Code" export (v2.21.0)
- [x] Shared room data (NetworkRoomState — live room properties with lobby attribute mirroring)
- [x] Per-user data (NetworkPlayerState — per-player synced properties)
- [x] Master client verification (OnRPCValidation callback + EnableOwnerOnlyRPCValidation helper)
- [x] Sender validation on message handlers (HandleStateUpdate, HandleSpawn, HandleDespawn, HandleAuthority, HandleSnapshot verify sender == owner or host)
- [x] Spatial hash (interest management — only sync nearby objects) (v2.20.0)
- [x] Tick-based simulation (deterministic tick rate, decoupled from frame rate) (v2.22.0)
- [x] Connection statistics / network profiler (RTT, packet loss, bandwidth)
- [x] Auto-reconnect (automatic reconnection with state recovery)

**Migration & Authority:**
- [x] Zero-hitch host migration (no scene takedown, own the object lifetime)
- [x] Peer-authority model (each peer owns their objects, master client arbitrates)
- [x] Late-join snapshots (new peers receive full state dump from host)
- [x] P2P direct connections for fast RPCs (EOS SDK handles relay vs direct automatically, NetworkStats tracks connection type)

**Player Management:**
- [x] PlayerSpawner — auto-spawn player prefab on peer connect, despawn on lobby leave, spawn points, events (v2.32.0, community request — Ducky)
- [x] Nested NetworkObjects — parent-child hierarchy support (v2.33.0, community request — Ducky)
- [x] Runtime reparenting — `SetNetworkParent()`/`DetachFromNetworkParent()` for dynamic hierarchy changes (attach weapons, pick up objects, detach limbs) (v2.34.0, community request — Ducky)
- [x] Weapon pickup/drop demo — DemoWeapon with F/G keys, tests reparenting, ownership transfer, late-join snapshots (v2.35.0, community request — Ducky)
- [x] Network Prefab Table — `NetworkPrefabTable` ScriptableObject (drag & drop prefab registry, index = PrefabId), auto-register in NetworkManager.OnEnable. Demo weapons now use proper `Spawn()` flow instead of `RegisterExisting()` (v2.36.0, community request — Ducky)
- [x] Concrete P2P demo scene — Pre-built scene with ground, walls, crates, camera, light. PlayerBall + Weapon + Crate prefabs with materials. P2PDemoManager uses `Instantiate(_ballPrefab)` instead of `CreatePrimitiveSafe`. Removed all runtime scene generation (v2.36.0)
- [x] InstanceFinder — static accessor for NetworkManager, P2PManager, LobbyManager, TickSimulation, etc. (v2.37.0, community request — Ducky)
- [x] SimulationBehaviour — base class for singleton network behaviours not on a NetworkObject, auto-subscribes tick/peer/host events (v2.37.0, community request — Ducky)
- [x] Reconnect without despawning — PersistOnDisconnect flag on NetworkObject, grace period (default 30s), hibernation dictionary, auto-restore ownership on reconnect (v2.37.0, community request — Ducky)

**QOL & API Polish** (community request — CometDev):
- [x] `NetworkManager.GetPlayerObject(puid)` — lookup a player's NetworkObject by their PUID (v2.16.1)
- [x] `NetworkManager.ConnectedPlayers` — easy-access list of connected player PUIDs (v2.16.1)
- [x] `NetworkManager.IsOnline` property — simple bool for "am I connected to a session" (v2.16.1)
- [x] RPC on NetworkObject subclasses — [NetRpc] methods now work directly on NetworkObject subclasses (v2.29.0)
- [x] NetworkBehaviour-only architecture — Investigated, NOT pursuing. Two-component model matches industry standard (Mirror/FishNet/Fusion), avoids SyncVar registry/RPC routing/interest filtering complexity (v2.29.0)

**SyncVar LOD System** (community request — AlmightyMikkel):
- [x] SyncVar LOD tiers — `SyncVarLOD` component with configurable distance tiers and sync rate multipliers (v2.18.0)
- [x] Distance-based sync throttle — auto-discovers nearest peer, throttles MarkDirty() on owner side (v2.18.0)
- [x] Per-component LOD checkboxes — SyncVarLOD Inspector with per-SyncVar toggles per tier (v2.26.0)
- [x] Per-SyncVar LOD masks — selectively sync specific vars at each tier (v2.24.0)

**Spatial Voice:**
- [x] EOSVoiceZoneManager — 5 modes (Global, Proximity, Team, TeamProximity, Custom), volume ducking, auto-discover
- [x] EOSVoiceTriggerZone — Collider-based zone triggers with gizmos
- [x] NetworkVoicePlayer — NetworkBehaviour auto-wiring EOSVoicePlayer to NetworkObject ownership
- [x] VOIP speaking indicator — Animated sphere above player head when talking
- [x] Audio occlusion (raycast-based wall muting) (v2.23.0)
- [x] Grid-based spatial partitioning (for 100+ players) (v2.27.0)
- [x] Voice priority system (bandwidth management at scale) (v2.28.0)

**Multi-Transport & Crossplay:**
- [ ] Multi-transport support (EOS + Steam crossplay)
- [ ] FishNet transport thin layer (basic lobbies + host migration, OG repo compile fix)
- [x] Spectator mode (IsSpectator flag, RPCTarget.Players, host election skip, spawn guard)

**Prediction & Rollback:**
- [x] Client-side prediction (input prediction for owned objects) (v2.31.0)
- [x] Collider rollback (lag compensation for hit detection) (v2.31.0)
- [x] Study Valve networking docs (Source/CS:GO PDFs) for rollback architecture (valve.md)
- [x] Study Photon PUN architecture for shared-mode patterns (photon.md)

**Security & Validation:**
- [x] Host-validated RPCs — `[NetRpc(Validated = true)]` routes through host, auto-discovers `Validate_X` methods (v2.18.0)
- [x] Configurable join code length — default 6 digits (1M possibilities), range 4-8 (v2.18.0)
- [x] Decompression size limit (cap `DecompressDeflate` output to prevent compression bombs) (v2.19.0)
- [x] Per-peer RPC rate limiting (cap messages/sec per sender to prevent flood DoS) (v2.19.0)

**Advanced:**
- [x] Network rules (SyncVarWriteAccess + OnSyncVarWrite callback) (v2.25.0)
- [x] Offline mode (local-only networking without EOS/P2P/lobby, for single-player and testing) (v2.30.0)
- [ ] Dedicated server support (server joins lobby, server-authoritative validation)
- [ ] Cloudscripting via VPS ($5 VPS for cloud validation / Discord bot integration)

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
- [x] Android (Build + device — v2.15.8: VOIP CONFIRMED WORKING! Phone-to-PC voice verified. 12 chapters of JNI/classloader/permission/AudioRecord battles. See `android-jni-battles.md`)

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
- [ ] Layer 2 SyncVars sync (Score, DisplayName, BallColor visible on late-join)
- [ ] Layer 2 RPCs work (E=color, Q=shockwave, T=chat, R=effect)

**Automated Unit Tests:**
- [x] All 694 editor-mode tests pass (Window > Test Runner > EditMode > Run All)

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
- [ ] All 6 tabs render without errors (Status, Lobbies, Voice, Social, Stats, Tools)
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
- [x] Connection statistics / network profiler (NetworkStats.cs — RTT, packet loss, bandwidth, NAT type, per-peer stats)
- [x] F1 overlay Network Stats section in Stats tab (color-coded RTT/loss/NAT, per-peer table)
- [x] NetworkRoomState — shared room data with typed SyncVars, SyncDictionary, lobby attribute mirroring
- [x] NetworkPlayerState — per-player synced state (name, team, score, custom data), auto-created on connect
- [x] NetworkSceneManager — networked scene loading (single + additive), late-join scene sync, all-peers-loaded event
- [x] Chunked snapshot delivery with priority ordering (RoomState first, PlayerStates second, then objects)
- [x] [NetRpc] IL post-processor with Mono.Cecil (typed RPCs, auto-serialize params, compile-time hash)
- [x] DemoBallBehaviour — Layer 2 demo with SyncVars (Score, DisplayName, BallColor) and [NetRpc] methods on runtime-created balls
- [x] Fix RegisterExisting() to call NotifyNetworkSpawn() (RPCs and OnNetworkSpawn now fire for objects registered outside Spawn())
- [x] Automated unit tests — 21 test files (~331 tests) for NetWriter/Reader, FnvHash, NetSerializers, SyncVar, SyncList, SyncDictionary, PacketFragmenter, NetworkId, Compression, LobbyOptions, MessageRouter, NetworkObjectHierarchy, NetworkPrefabTable, RegisterExisting, NetworkManagerCore, SpawnDespawn, InstanceFinder, SimulationBehaviour, ReconnectHibernation, SpawnOverloads
- [x] Fix INetSerializable struct boxing bug in NetSerializers.Read (Deserialize mutated boxed copy, not original)
- [x] Fix Input System compatibility in P2PDemoManager (EOS_HAS_INPUT_SYSTEM guard for E/Q/T/R keys)
- [x] Add InternalsVisibleTo for test assembly (AssemblyInfo.cs)
- [x] Packet compression — Deflate with configurable threshold (64 bytes), opt-in via CompressionEnabled, backward compatible wire format (FLAG_SINGLE_COMPRESSED 0x02, FLAG_BATCH_COMPRESSED 0x03)
- [x] Master client verification — OnRPCValidation callback on NetworkManager, EnableOwnerOnlyRPCValidation() convenience helper
- [x] Spectator mode — IsSpectator flag, RPCTarget.Players (skips spectators), spectator-aware host election, spawn guard, PlayerState _spectator custom data
- [x] Android VOIP — 12-chapter JNI/classloader saga: desugaring, AndroidX, extractNativeLibs, RTCOptions, AndroidInitializeOptions, ProGuard, System.loadLibrary from Java, runtime RECORD_AUDIO permission, Unity Microphone AudioRecord conflict fix. Phone-to-PC voice confirmed working in v2.15.8.
- [x] Spatial voice system — EOSVoiceZoneManager (5 modes), EOSVoiceTriggerZone (collider zones), NetworkVoicePlayer (Layer 2 auto-wiring + speaking indicator)
- [x] Canvas UI scroll fix — Clamped movement, reduced sensitivity from 40 to 15 (v2.16.1)
- [x] Mobile controls for P2P Ball Demo — Virtual joystick + jump button on Android/iOS (v2.16.1)
- [x] Player lookup API — GetPlayerObject, ConnectedPlayers, IsOnline on NetworkManager (v2.16.1)
- [x] Custom inspectors for runtime components — NetworkManager, NetworkObject, NetworkRoomState, NetworkPlayerState, EOSP2PManager, NetworkSceneManager, EOSNativeConsole (v2.16.1)
- [x] Player Registry inspector improved — 16-char PUID with selectable label, friend/blocked counts (v2.16.1)
- [x] Mobile controls work on PC + mobile — Removed platform gate, replaced raw touch with EventSystem drag handlers (v2.16.2)
- [x] Console stack traces for all log types — file:line info visible for Debug.Log in dev builds (v2.16.2)
- [x] HUD diagnostics in P2P Ball Demo — P2P peers, voice status, remote ball count in OnGUI (v2.16.2)
- [x] Canvas UI full parity — Expanded from 4 to 6 tabs (Stats + Tools), social enhancements (invites, epic account, notes, block/invite per-player), profile/report popups, VOIP safety guard on EOSVoiceZoneManager (v2.17.0)
- [x] P2P connection race fix — Joiner pre-accepts + handshakes existing lobby members in OnLobbyJoined, host also sends handshake in OnMemberJoined. Fixes host-order join bug (v2.17.1)
- [x] Canvas UI Tools tab NullRef fix — LFG Browse section guarded by lfgMgr null check (v2.17.1)
- [x] P2P handshake retry — Up to 5 retries at 2s intervals when in lobby but no peers connected. Handles timing/race conditions automatically (v2.17.2)
- [x] Fix double Router.ProcessIncoming subscription — Auto-subscribe in EOSP2PManager.Router getter, removed manual wiring from P2PDemoManager and NetworkManager (v2.17.2)
- [x] P2P diagnostic logging — Result checking on AcceptConnection/SendPacket, null-state warnings on Initialize/AcceptPeer, connection type/reason in events (v2.17.2)
- [x] Sender validation on message handlers — HandleStateUpdate, HandleSpawn, HandleDespawn, HandleAuthority, HandleSnapshot verify sender is owner or host. Prevents peers from writing to objects they don't own, despawning others' objects, or claiming authority. Zero latency cost (v2.17.5)
- [x] Host-validated RPCs — `[NetRpc(Validated = true)]` routes RPCs through host, auto-discovers `Validate_X` methods by naming convention, host rebroadcasts if approved. Adds ~20-40ms latency (one hop). New msg types 0xA8/0xA9. IL weaver updated (v2.18.0)
- [x] SyncVar LOD — `SyncVarLOD` component with distance-based sync tiers, owner-side throttle, auto nearest-peer distance, customizable tiers via Inspector (v2.18.0)
- [x] Configurable join code length — `JoinCodeLength` on EOSLobbyManager (default 6, range 4-8), per-lobby override via LobbyOptions (v2.18.0)
- [x] InstanceFinder — static accessor class for all key managers (NetworkManager, P2PManager, LobbyManager, TickSimulation, InterestManager, SceneManager) + convenience shortcuts (IsHost, IsOnline, IsOffline, CurrentTick, FixedTickTime) (v2.37.0)
- [x] SimulationBehaviour — abstract MonoBehaviour base for game managers (match manager, wave spawner, score tracker) with auto-subscribing tick/peer/host callbacks (v2.37.0)
- [x] Reconnect without despawning — PersistOnDisconnect on NetworkObject, ReconnectGracePeriod on NetworkManager (default 30s), hibernation dictionary, auto-restore ownership on reconnect. OnHostChanged event on NetworkManager (v2.37.0)
- [x] Spawn overloads — `Spawn(GameObject, ...)` with auto-register, `Spawn(string, ...)` by prefab name, `GetPrefabId(GameObject)` helper. `NetworkPrefabTable.IndexOf(GameObject)`. P2PDemoManager disabled by default in scene (v2.38.0, community request — Ducky)
- [x] Per-NetworkBehaviour component IDs — each NB addressable via `componentIndex` byte in RPC wire format. SELF_COMPONENT_INDEX=0xFF sentinel. IL weaver updated for 5-param RegisterRPC/SendRPC. +1 byte per RPC overhead (v2.40.0, community request — CometDev)
- [x] Per-NB scoped SyncVars — each NetworkBehaviour owns its own SyncVar pool (up to 32 per NB). Sectioned wire format: `[sectionCount][foreach: componentIndex + mask + values]`. `_notifyDirty` Action callback pattern. Unlimited SyncVars per object (32 per NB x N behaviours) (v2.40.0, community request — CometDev)
- [x] Auto-collecting prefab pool — `NetworkPrefabTable.CollectAll()` scans project for prefabs with NetworkObject. Custom editor with "Collect All" button, folder filter, "Remove Nulls". Inspector in EOSRuntimeInspectors.cs (v2.40.0, community request — Ducky)
- [x] "Connecting" docs page — `docs/connecting.md` explains lobby→P2P→NetworkManager→PlayerSpawner flow. Added to sidebar + quickstart next steps (v2.40.0, community request — Ducky)
- [x] Voice toggle UI fix — Added Voice/Migrate toggles to F1 overlay's "Join / Quick Match" section. Both sections now show shared toggles. Fixes voice offline confusion when switching between Create Lobby and Quick Match (v2.40.0, community request — Ducky)
- [x] Comprehensive SyncVar type tests — 43 new EditMode tests covering ALL 19 builtin serializer types (byte, bool, short, ushort, int, uint, long, ulong, float, double, string, Vector2, Vector3, Quaternion, Color, Color32, ProductUserId, byte[], NetworkObject). Mixed-type dirty/full-state round-trips (v2.41.0)
- [x] Per-NB SyncVar tests — 28 new EditMode tests for sectioned wire format: Sync<T>() registration, ComponentIndex assignment, NB dirty tracking, NB serialization round-trips, sectioned SerializeDirty/SerializeAll with self+NB sections, ClearDirty, adaptive mask sizes, OnChanged callbacks (v2.41.0)
- [x] Per-NB component ID tests — 16 new EditMode tests for RPC routing: ComponentIndex assignment, RegisterRPC with componentIndex, RPC routing to correct NB in offline mode, SELF fallback, legacy backward compat, arg data passing, validated RPCs (v2.41.0)
- [x] DemoBallStats — second NetworkBehaviour on PlayerBall prefab demonstrating per-NB scoped SyncVars with 7 varied types: Health (float), IsGrounded (bool), Velocity (Vector3), Kills (short), Deaths (short), PlayTime (double), Team (byte). Stats HUD, damage/heal/team RPCs (v2.41.0)
- [x] NB.Net fix for inactive clones — `NotifyNetworkSpawn()` now sets `NB.Net = this` so convenience props work even when Awake() didn't run. NB.Net setter changed from `private` to `internal` (v2.42.0)
- [x] Convenience properties on NetworkBehaviour — IsSpawned, HasAuthority, IsOnline, IsOffline. Convenience methods: Despawn(), GiveOwnership(), RemoveOwnership() (v2.42.0)
- [x] NetworkBehaviour lifecycle hooks — OnStartHost, OnStopHost, OnStartOwner, OnStopOwner, OnOwnershipChanged. Wired into NotifyOwnerChanged and host election (v2.42.0)
- [x] SyncHashSet\<T\> — operation-based delta sync collection (Add/Remove/Clear) with OnChanged callbacks, full state snapshots, ISyncVar implementation. 25 tests (v2.42.0)
- [x] Singleton auto-create prevention — IsOwner, IsHost, IsOnline property getters use `._instance` instead of `.Instance` to prevent auto-create + DontDestroyOnLoad in tests. TransferAuthority offline guard added (v2.42.0)
- [x] End-to-end tests — 22 tests covering spawn+sync dirty, SerializeAll round-trips, despawn lifecycle, respawn cycles, multi-object independence, NB convenience integration, DespawnAll (v2.42.0)
- [x] Lifecycle hook tests — 22 tests for convenience props, spawn/despawn hooks, host/ownership changed callbacks, GiveOwnership/RemoveOwnership, multiple behaviour notification (v2.42.0)
- [x] Spawn payload hooks — WriteSpawnData/ReadSpawnData on NetworkBehaviour, length-prefixed per-NB sections in spawn wire format, wired into spawn, snapshot, reserved objects, and child data paths. 19 new tests (v2.44.0)
- [x] Tick/Peer lifecycle hooks — OnTick(uint), OnPeerConnected(ProductUserId), OnPeerDisconnected(ProductUserId) on NetworkBehaviour. NetworkManager subscribes TickSimulation.OnTick and dispatches to all spawned objects. Peer events dispatched in OnPeerConnected/OnPeerDisconnected (v2.44.0)
- [x] TargetRpc (RPCTarget.Peer) — send RPC to a specific peer via `SendRPCWeavedToPeer(target, componentIndex, hash, peer, data)`. Early guard in SendRPCWeaved logs error if RPCTarget.Peer used without dedicated overload (v2.45.0)
- [x] [HostOnly]/[OwnerOnly] guard attributes — `RPCGuard` flags enum, `RegisterRPCGuard()` per-RPC registration, receiver-side guard checking in HandleRPC, guard cleanup in UnregisterRPCs. 22 new tests (v2.45.0)
- [x] RPC improvements — RunLocally, ExcludeOwner, Channel selection, Reliability on NetRpcAttribute. Extended SendRPCWeaved overload with all options. SendToInterestedPeersExcluding/SendToInterestedNonSpectatorsExcluding helpers. 22 new tests (v2.46.0)
- [x] Packet compression and security tests — Deflate round-trip, decompression bomb defense, compressed single/batch dispatch, batch count limits, malformed batch handling, rate limiting, 27 new tests (v2.46.0)
- [x] SyncTimer/SyncStopwatch utility sync types — SyncTimer (countdown with Start/Pause/Resume/Reset/Tick, OnExpired event) and SyncStopwatch (elapsed time with Start/Stop/Reset/Restart/Tick). Both implement ISyncVar, 5-byte wire format (float + running byte). Factory methods on both NetworkObject and NetworkBehaviour. 45 new tests (v2.47.0)
