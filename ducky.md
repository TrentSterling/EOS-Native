# Ducky Feedback Log

First external tester feedback — Discord DMs, 2026-02-10.

## Raw Conversation

**DUCKY — 11:11 AM**
- Installing EOS-Native
- "The API is very fishy I like it"
- Build profile errors — switching to Android fixed them
- "Looks like it only compiles for Android lmao"
- Does EOS-Native support nesting NetworkObjects? "cuz it should"

**DUCKY — 11:22 AM**
- Screenshot of P2P demo in action — "so peak dude"
- "I think it should come with a PlayerSpawner like Fishy"

**DUCKY — 11:30 AM**
- Wants SimulationBehaviour equivalent (Fusion) for singletons in same space as NetworkManager
- Prefab string→id method, or better: NetObject→id like FishNet
- Wants InstanceFinder like FishNet

**DUCKY — 11:39 AM**
- "I don't think the docs ever state how to connect, like thru the net manager"
- "Unless it does that automatically when it detects ur in a lobby?"
- Screenshot: "HUH??" — random objects spawning (P2PDemoManager auto-creates level geometry + balls)
- "Why does it just spawn those"

**DUCKY — 11:49 AM**
- Found the auto-create culprit
- "I imagine this has to be the culprit" (screenshot of P2PDemoManager or similar)

**DUCKY — 12:13 PM**
- Had to use Unity Inspector Debug mode to see a hidden field
- "You need to add that to the Inspector"
- Screenshot: progress — getting somewhere

**DUCKY — 12:22 PM**
- "This shit is super nice so far I fw it"
- "Just think some stuff is unclear in the docs and some QOL to push would be epic"
- "Also totally needs some logo for when u do full release"

**DUCKY — 12:23 PM**
- "How do I actually activate the networking once I'm in the lobby?"
- "Like connect NetworkManager to the EOS P2P shit"

**DUCKY — 12:24 PM**
- "Disable this by default btw and branch that to a separate scene or like a sample package in UPM"
  (referring to P2PDemoManager auto-spawning demo objects)

**!Tront — 12:24 PM**
- "Was supposed to be automagic, part of the P2P demo stuff"
- "When P2P demo is active, as soon as lobby is connected you should have a playerball spawn"
- "But it also spawns a full level thingy because I was working with a blank scene for so long"

**DUCKY — 12:25 PM**
- "Yeah everything spawned in P2P demo I think"
- "But now I can't get anything to happen"
- "This is the current setup"
- Screenshot: Scene hierarchy shows `Player Sync Manager`, `Network Manager`, `GlobalAudio`, `Wristwatch Lights Manager`, a `Cube`, `Text Map`, and several `Cable` objects. Inspector shows `Network Manager (Script)` with Prefabs list containing `Element 0 = Network Player`. `Enable Pooling` unchecked. `Monitor Status` section visible.
- Dark scene with green-lit geometry (looks like a game level, not the P2P demo cubes)
- P2PDemoManager is NOT in the hierarchy — Ducky disabled/removed it

**DUCKY — 12:26 PM**
- "I'm watching the 'isOnline' property in playmode"
- isOnline stays false — NetworkManager never activates
- **Core confusion:** Ducky has NetworkManager set up with a player prefab, joined a lobby via EOS, but doesn't know what triggers the NetworkManager to go online. There's no documented bridge between "I'm in a lobby" → "NetworkManager.IsOnline = true".

**!Tront — 12:32 PM**
- "playerspawner inbound, non-android fix inbound"

**DUCKY — 12:32 PM**
- "hell yeah"

**!Tront — 12:34 PM**
- "isonline requires peers believe it or not, will rewrite that"

**DUCKY — 12:34 PM**
- "like another peer in the lobby with you?"

**!Tront — 12:34-12:35 PM**
- "if you launch parallelsync and join the lobby you should see it update yeah"
- "it should also update members and all that and even establish the p2p link"
- "just wont playerspawn"

**DUCKY — 12:35 PM**
- "also elaborating on this, mostly want it to easily attach to events in the network manager and shit"
- "interesting. I had my own player spawner running but didn't see my player"
- Screenshot: Ducky's own PlayerSpawner script calling `NetworkManager.Instance.Spawn()` — but gated behind `IsOnline` check
- "oh wait. I'm stupid. It didn't spawn because it was waiting for isonline which was never true lol"

**!Tront — 12:36 PM**
- "haha woops, yeah the API needs a lot of work and clarification...and testing"
- "but I do want it to be that easy"
- "isonline should be true as soon as it gets lobbied. Will correct it"

**DUCKY — 12:37 PM**
- "I think a decently high priority is making the spawn method take a string (prefab name) or network object reference like fishnet to make things simpler"
- "also a prefab pool thing that automatically collects networkobject prefabs like fishnets, like the defaultprefabpool scriptable object"

**DUCKY — 12:39 PM**
- "is nesting net objects allowed yet"

**!Tront — 12:41 PM**
- Discussed NetworkBehaviour IDs with CometDev — each NB could have an ID, wouldn't need NetObjects on every child, can target specific scripts over the net. Not finalized.
- "so the answer is maybe"

**DUCKY — 12:41 PM**
- "ooh interesting"
- "so is my current net player structure allowed?"
- Screenshot: Prefab hierarchy:
  - `Network Player` (root) — NetworkObject
  - `Head` (child) — NetworkObject + NetworkTransform
  - `LeftHand` (child) — NetworkObject + NetworkTransform
  - `RightHand` (child) — NetworkObject + NetworkTransform
- "root has netobj, head and hands have netobj and nettransform"
- **This is a VR player setup** — needs separate transform sync for head + both hands

## Extracted Issues

### BUG: Only compiles on Android build profile
- **Severity:** Critical
- **Description:** Compile errors appear on non-Android build profiles (e.g. Windows). Switching build profile to Android fixes them.
- **Likely cause:** `#if UNITY_ANDROID` guards around code that references Android-only APIs without proper fallbacks, OR Android-specific code not properly guarded.

### BUG: P2PDemoManager auto-creates demo objects in any project
- **Severity:** High
- **Description:** P2PDemoManager auto-creates level geometry (ground plane, obstacles) and player balls as soon as you enter play mode with a lobby. This is confusing for users who just want to use the networking layer.
- **Fix needed:** Disable P2PDemoManager by default. Move demo content to a separate scene or UPM sample package.

### BUG: Inspector hides important fields (needs Debug mode)
- **Severity:** Medium
- **Description:** Some important configuration fields are not exposed in the custom Inspector, requiring users to switch to Debug mode to find them.
- **Fix needed:** Audit all custom inspectors and ensure all user-facing fields are visible.

### FEATURE: Runtime Reparenting — IMPLEMENTED (v2.34.0)
- **Priority:** High
- **Description:** Runtime `SetNetworkParent()`/`DetachFromNetworkParent()` for dynamic hierarchy changes.
- **Use case:** VR weapon pickup/drop, ragdoll detachment, inventory system.
- **Implementation:** `MSG_REPARENT` (0xAF) message, `OriginalParentNetworkId` tracks spawn-time parent (never changes), `_originalChildren` registry tracks all spawn-time children per root. Detached children are serialized inline with their original root (with detach flag + world pos/rot). Dynamically attached roots appear as top-level snapshot entries + MSG_REPARENT for late joiners. Wire format: `[Flags:byte]` added after `[LocalIndex:byte]` per child (breaking change from v2.33.0).

### FEATURE: Nested NetworkObjects — IMPLEMENTED (v2.33.0)
- **Priority:** High
- **Description:** Support NetworkObjects as children of other NetworkObjects (parent-child hierarchy).
- **Use case (Ducky):** VR player with root NetworkObject + child Head/LeftHand/RightHand each needing their own NetworkObject + NetworkTransform.
- **Implementation:** `Spawn()` discovers all child NetworkObjects via `GetComponentsInChildren<NetworkObject>(true)`. Each child gets its own NetworkId and `ParentNetworkId` pointing to the root. Children are serialized alongside the root in spawn/snapshot messages. Despawn/TransferAuthority on root cascades to all children automatically. Direct child despawn/transfer is blocked with a warning.
- **Wire format:** Spawn/snapshot messages append `[ChildCount:byte]` after root SyncVars, then per child: `[NetworkId:u32] [LocalIndex:byte] [DataLen:u16] [SyncVarCount:byte] [SyncVarData...]`. Single-object prefabs: ChildCount=0 (1 extra byte overhead).
- **Interest management:** Children inherit interest from their root parent.

### FEATURE: PlayerSpawner component
- **Priority:** High
- **Description:** A component like FishNet's PlayerSpawner that auto-spawns a player prefab when connecting. Assign a prefab, it spawns per-player.

### FEATURE: SimulationBehaviour equivalent
- **Priority:** Medium
- **Description:** A base class for singleton-like network behaviours that exist at the same level as NetworkManager (not attached to a NetworkObject). Like Fusion's SimulationBehaviour.

### FEATURE: Spawn overloads (string name, GameObject reference)
- **Priority:** High (Ducky 12:37 PM)
- **Description:** `Spawn()` currently takes a `ushort prefabId`. Ducky wants `Spawn("PrefabName")` or `Spawn(prefabGameObject)` like FishNet — simpler API, no need to know numeric IDs.

### FEATURE: Auto-collecting prefab pool (DefaultPrefabPool ScriptableObject)
- **Priority:** Medium (Ducky 12:37 PM)
- **Description:** A ScriptableObject that automatically discovers all prefabs with NetworkObject components, like FishNet's `DefaultPrefabPool`. Currently prefabs must be manually added to NetworkManager's Prefabs list by index.

### FEATURE: Prefab ID registry
- **Priority:** Medium
- **Description:** Prefab string→int ID mapping, or better: NetObject→id like FishNet. Overlaps with auto-collecting prefab pool above.

### FEATURE: InstanceFinder
- **Priority:** Medium
- **Description:** Static accessor like FishNet's `InstanceFinder` for finding NetworkManager, TimeManager, etc. without GetComponent/FindObjectOfType.

### BLOCKER: NetworkManager doesn't auto-activate when lobby joins
- **Priority:** CRITICAL (Ducky is stuck here right now)
- **Description:** Ducky has NetworkManager in scene with a player prefab assigned, joined an EOS lobby, but `IsOnline` stays false. There is NO automatic bridge between lobby join → P2P activation → NetworkManager online. P2PDemoManager was handling this internally but Ducky (correctly) disabled it.
- **Root cause:** The P2P → NetworkManager activation flow is buried inside P2PDemoManager. Without it, there's no component that says "when lobby connects, start networking."
- **Fix needed:** Either:
  - (A) NetworkManager auto-detects lobby join and activates P2P automatically, OR
  - (B) A simple `PlayerSpawner` / `NetworkStarter` component that bridges lobby → P2P → NetworkManager, OR
  - (C) Clear docs/API: `NetworkManager.Instance.StartOnline()` or similar one-liner

### DOCS: How to connect / activate networking
- **Priority:** Critical
- **Description:** Docs don't explain how to connect the NetworkManager to EOS P2P after joining a lobby. Is it automatic? Manual? What triggers `IsOnline = true`?
- **Ducky quote:** "I don't think the docs ever state how to connect, like thru the net manager" / "Unless it does that automatically when it detects ur in a lobby?"

### DOCS: Logo / branding
- **Priority:** Low
- **Description:** Need a logo for the package for eventual public release.

## Ducky's Current State (2026-02-10 12:26 PM)
- **Scene:** Custom game level (not P2P demo), dark environment with green lighting, cables, wristwatch
- **Hierarchy:** `Player Sync Manager`, `Network Manager`, `GlobalAudio`, `Wristwatch Lights Manager`, `Cube`, `Text Map`, multiple `Cable` objects
- **Setup:** NetworkManager in scene with `Network Player` prefab in Prefabs[0]. Enable Pooling unchecked.
- **P2PDemoManager:** Disabled/removed (good — that was spawning demo junk)
- **Status:** Can login + join lobby via EOS. NetworkManager `IsOnline = false`. Nothing spawns.
- **Blocked on:** No way to go from "in a lobby" → "networking active" without P2PDemoManager

## Technical Analysis

### Why IsOnline Stays False
`NetworkManager.IsOnline` is NOT a flag — it's a derived property:
```csharp
public bool IsOnline => EOSP2PManager.Instance?.Peers?.Count > 0;
```
It requires at least ONE connected P2P peer. Two possible reasons it's false:
1. **Ducky is alone in the lobby** — needs a second player to connect
2. **P2P handshakes aren't firing** — EOSP2PManager.OnLobbyJoined may not be triggering

### The Automatic Flow (when it works)
1. Join lobby → `EOSLobbyManager.OnLobbyJoined` fires
2. → `EOSP2PManager.OnLobbyJoined()` calls `Initialize()` + sends handshakes to existing members
3. → P2P connections establish → `OnPeerConnected` fires
4. → `NetworkManager.OnPeerConnected()` elects host, creates RoomState/PlayerState
5. → `IsOnline` becomes true (peers > 0)

### The REAL Problem: No PlayerSpawner
Even when peers connect and IsOnline becomes true, **nothing auto-spawns a player prefab**. P2PDemoManager had its own `SpawnLocalBall()` — that's demo code. NetworkManager has a `Prefabs` list but NO auto-spawn logic. Ducky assigned `Network Player` to Prefabs[0] expecting it would auto-spawn like FishNet/Fusion — it doesn't.

**What's needed:** A `PlayerSpawner` component that:
- Detects when P2P peer connects
- Auto-spawns the assigned player prefab for the local player
- Assigns ownership to that player
- Despawns on disconnect

## Resolutions

### PlayerSpawner Component — BUILT (v2.32.0)
- **File:** `Runtime/EOSNative/Net/PlayerSpawner.cs`
- **What it does:** Assign a player prefab + optional spawn points in Inspector. Auto-spawns local player on first peer connect, auto-despawns on lobby leave or all peers disconnect.
- **How Ducky uses it:**
  1. Add `PlayerSpawner` component to a GameObject in the scene
  2. Assign `Network Player` prefab to `Player Prefab` field
  3. Set `Prefab Id` to 0 (matches NetworkManager's Prefabs[0])
  4. Optionally assign spawn point Transforms
  5. Join a lobby with another player → player auto-spawns
- **API:** `LocalPlayer` (NetworkObject), `HasSpawned` (bool), `OnLocalPlayerSpawned`/`OnLocalPlayerDespawned` events, `SpawnLocalPlayer()`/`DespawnLocalPlayer()` manual control

### Compile Errors — CANNOT REPRODUCE
- Full audit found all Android-specific code is properly `#if UNITY_ANDROID` guarded
- EOSNative.Editor asmdef has `includePlatforms: ["Editor"]`
- Likely cause: Unity cache issue or transient import error
- **Ask Ducky for exact error messages if it reproduces**

### NetworkObject Inspector Emptiness — FIXED (v2.34.0)
- **Problem:** Ducky reported "jumpscared by net objects just have no options" — NetworkObject Inspector showed nothing at edit time, just "Enter Play Mode to see network identity."
- **Root Cause:** `DestroyWithOwner` had `[SerializeField]` on the property (not the backing field) — Unity ignores this. `AlwaysVisible` had no serialization at all. `DrawDefaultInspector()` had nothing to draw.
- **Fix:** Converted both to proper `[SerializeField] private bool` backing fields. Inspector now shows **Configuration** section at edit time with "Destroy With Owner" and "Always Visible" toggles + tooltips. **Hierarchy** section shows parent/child NetworkObject relationships. **Runtime Status** section in play mode shows Network ID, Owner, SyncVar count, parent net ID, etc.

### Voice Offline on Create Lobby vs Quick Match — INVESTIGATION
- **Reported:** Ducky said voice shows "offline" when using "Create Lobby" button but "online" when using "Quick Match" button.
- **Investigation:** Both code paths use identical voice handling. `CreateLobbyAsync` and `QuickMatchOrHostAsync` both read `EnableVoice` from the same `_lobbyVoice` toggle (default `true`). Both call `CreateLobbyInternal` with the same parameters. No code asymmetry found.
- **Possible causes:**
  1. Voice toggle was off when testing Create Lobby, on when testing Quick Match
  2. EOS SDK RTC initialization timing — Quick Match searches first (gives SDK more init time), Create Lobby fires immediately
  3. Voice auto-retry fallback silently disabled voice on first attempt
- **Status:** Need more details from Ducky (exact steps, toggle state, platform)

### Runtime Reparenting — IMPLEMENTED (v2.34.0)
- `SetNetworkParent()`/`DetachFromNetworkParent()` on NetworkObject
- MSG_REPARENT (0xAF) syncs hierarchy changes to all peers + late joiners
- Wire format: Flags byte per child in spawn/snapshot (0x00=attached, 0x01=detached)
- Position sync handled by NetworkTransform (no redundant pos/rot in detach data)

## Action Items
1. ~~Fix lobby→P2P→NetworkManager bridge~~ → **DONE: PlayerSpawner**
2. ~~Fix compile errors on non-Android~~ → **CANNOT REPRODUCE (need exact errors)**
3. Disable P2PDemoManager by default / move to UPM sample
4. Write "Getting Started" / "Connecting" docs page
5. ~~Expose hidden Inspector fields~~ → **DONE: v2.34.0 (DestroyWithOwner, AlwaysVisible, Hierarchy section)**
6. ~~Implement nested NetworkObject support~~ → **DONE: v2.33.0**
7. Consider SimulationBehaviour, InstanceFinder, PrefabId patterns
8. ~~Implement runtime reparenting~~ → **DONE: v2.34.0**
9. Investigate voice offline on Create Lobby (need more details from Ducky)
