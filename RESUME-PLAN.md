# Resume Plan — EOS-Native Session 2026-02-09

## What We Did Today

### v2.31.0: Client-Side Prediction & Lag Compensation (SHIPPED)
- Created 3 new files in `Runtime/EOSNative/Net/`:
  - `StateSnapshot.cs` — `StateSnapshot` struct + `StateHistory` ring buffer (64 entries, O(1) record/lookup, sub-tick interpolation)
  - `NetworkPrediction.cs` — Opt-in component: tick recording, `ApplyCorrection()` with visual smoothing (exponential decay blend)
  - `LagCompensation.cs` — Static rewind utility: `Compensate(rttMs, callback)` and `Compensate(ProductUserId, callback)`
- Updated: `package.json` (2.31.0), `CHANGELOG.MD`, `TODO.MD`, `CLAUDE-NETWORKING.md`, `docs/networking.md`, `MEMORY.md`
- Committed and pushed: `fd87658`

### Fusion Shared Mode Parity Audit
- **Result: ~95% parity** (better than expected)
- `TransferAuthority()` + `RequestAuthority()` + `OnAuthorityRequested` callback already existed — closes the dynamic authority gap
- Areas where we exceed Fusion Shared Mode: RPC host validation, direct peer RPC, SyncVarLOD, offline mode, spectators, EasySync no-code, lobby attribute mirroring, RPC buffering during migration
- Only real remaining gap: deterministic tick synchronization across peers (Fusion mandates lock-step; ours is optional and local-only)

### Offline Mode Audit
- **Result: Comprehensive and production-ready**
- 36 guard paths across NetworkManager covering all network I/O
- All 6 RPC send paths execute locally, SyncVars work, spawn/despawn local-only
- RoomState + PlayerState auto-created, host election no-op
- No gaps found

## Current State

- **Version:** 2.31.0
- **Branch:** main (up to date with origin)
- **Open bugs:** None
- **All tests pass** (user confirmed)

## What To Work On Next (Priority Order)

### 1. Multi-transport support (EOS + Steam crossplay)
- TODO item under "Multi-Transport & Crossplay"
- Design an `ITransport` interface that abstracts EOS P2P vs Steam Networking Sockets
- Allow both transports to coexist in one lobby (crossplay)

### 2. FishNet transport thin layer
- Lightweight FishNet transport adapter on top of EOS-Native
- Basic lobbies + host migration
- Fix OG repo compile issues

### 3. Dedicated server support
- Server joins lobby as a peer, acts as authoritative host
- Server-authoritative validation (beyond host-validated RPCs)

### 4. Study Valve & Photon networking docs
- Research items — inform future architecture decisions
- Valve: Source/CS:GO rollback PDFs
- Photon: PUN shared-mode patterns

### 5. Target projects (MonkePortals, restore dead FishNet EOS games)

### 6. Discord integration (store + bot)

## Key Files to Know

| File | Purpose |
|------|---------|
| `Runtime/EOSNative/Core/EOSManager.cs` | Main manager, SDK init, auth, platform tick |
| `Runtime/EOSNative/Net/NetworkManager.cs` | All networking: spawn, sync, RPCs, migration, offline |
| `Runtime/EOSNative/Net/NetworkObject.cs` | Object identity, ownership, SyncVar registry |
| `Runtime/EOSNative/Net/NetworkPrediction.cs` | NEW: prediction + lag comp component |
| `Runtime/EOSNative/Net/LagCompensation.cs` | NEW: static rewind utility |
| `Runtime/EOSNative/Net/StateSnapshot.cs` | NEW: state struct + ring buffer |
| `Runtime/EOSNative/Net/TickSimulation.cs` | Fixed-rate tick simulation |
| `Runtime/EOSNative/Net/NetworkStats.cs` | RTT, packet loss, bandwidth stats |
| `package.json` | Version (currently 2.31.0) |
| `CLAUDE.md` + `CLAUDE-NETWORKING.md` | Architecture reference |
| `TODO.MD` | Feature checklist |
| `BUGS.MD` | Bug tracker (all fixed, none open) |

## Conventions Reminder
- **ALWAYS increment version** in `package.json` before each git push
- **DO NOT modify** `Source/Core/` or `Source/Generated/` (Epic's auto-generated files)
- Update `CLAUDE.md`, `TODO.MD`, and `MEMORY.md` at every resting point
- Singleton pattern: parent under `EOSManager.Instance.transform`, `_shuttingDown` guard
- `EOSDebugLogger`: `Log(category, class, msg)`, `LogWarning(category, class, msg)`, `LogError(class, msg)` (no category)
- Rigidbody API: `rb.linearVelocity` (Unity 6+), not `rb.velocity`
