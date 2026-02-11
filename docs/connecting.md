# Connecting: Lobby to Networking

How to go from "in a lobby" to "networked game with spawned players." This page explains the full connection flow and the minimal setup required.

## The Flow

```
EOS Login → Join/Create Lobby → P2P Connects Automatically → NetworkManager Goes Online → Players Spawn
```

Each step happens automatically when the previous one completes. Here's what each layer does:

| Step | Who Does It | What Triggers It |
|------|-------------|------------------|
| 1. EOS Init + Login | `EOSManager` (Auto Initialize + Auto Login) | Scene load |
| 2. Join/Create Lobby | You (via code, Inspector, or F1 overlay) | User action |
| 3. P2P Connection | `EOSP2PManager` (auto-created singleton) | Lobby join event |
| 4. NetworkManager Online | `NetworkManager` | First peer connects via P2P |
| 5. Player Spawn | `PlayerSpawner` component | NetworkManager goes online |

## Minimal Setup (5 Minutes)

### 1. Scene Setup

Add these components to your scene:

```
EOSManager          → Handles SDK init + login (auto-creates all other singletons)
NetworkManager      → Handles spawning, SyncVars, RPCs, host migration
PlayerSpawner       → Auto-spawns your player prefab when networking activates
```

**EOSManager** is the only one you need to create manually. `NetworkManager` and `PlayerSpawner` can be on the same or separate GameObjects.

### 2. Player Prefab

Create a prefab with:

```
YourPlayerPrefab
├── NetworkObject        (required — gives network identity)
├── NetworkTransform     (optional — syncs position/rotation)
└── YourPlayerScript     (your NetworkBehaviour with SyncVars)
```

### 3. Register the Prefab

**Option A: Network Prefab Table (Recommended)**

1. Right-click in Project → Create → EOS Native → Network Prefab Table
2. Drag your player prefab into the table's list
3. Assign the table to `NetworkManager.PrefabTable` in the Inspector

**Option B: Manual Registration**

In a setup script:
```csharp
NetworkManager.Instance.RegisterPrefab(playerPrefab, prefabId: 0);
```

**Option C: Spawn with Auto-Register**

Just call `Spawn(prefab)` — it auto-registers if not already registered:
```csharp
NetworkManager.Instance.Spawn(playerPrefab, position, rotation);
```

### 4. Configure PlayerSpawner

In the Inspector on your `PlayerSpawner` component:
- **Player Prefab**: Your player prefab
- **Prefab Id**: The index in the prefab table (or the ID you used in `RegisterPrefab`)
- **Spawn Points**: (Optional) Array of Transforms for spawn positions

### 5. Join a Lobby

From code:
```csharp
// Quick match — finds a lobby or creates one
await EOSLobbyManager.Instance.QuickMatchOrHostAsync();

// Or create directly
await EOSLobbyManager.Instance.CreateLobbyAsync(new LobbyOptions()
    .WithName("My Room")
    .WithMaxPlayers(4)
    .WithVoice());

// Or join by code
await EOSLobbyManager.Instance.JoinLobbyByCodeAsync("1234");
```

Or just use the F1 overlay (press F1 in Play Mode) → Lobbies tab → Quick Match / Create / Join by Code.

### 6. Done

When a second player joins the same lobby:
1. P2P handshakes exchange automatically
2. `NetworkManager.IsOnline` becomes `true`
3. `PlayerSpawner` spawns your player prefab
4. SyncVars, RPCs, and NetworkTransform start syncing

## Connection State

Check the current state at any time:

```csharp
using EOSNative.Net;

// Are we in a lobby?
bool inLobby = EOSLobbyManager.Instance.IsInLobby;

// Are we connected to peers? (P2P established)
bool online = NetworkManager.Instance.IsOnline;
// or: InstanceFinder.IsOnline

// Am I the host? (first peer / lobby owner)
bool host = NetworkManager.Instance.IsHost;
// or: InstanceFinder.IsHost

// How many peers are connected?
int peers = EOSP2PManager.Instance.PeerCount;
```

### State Diagram

```
Not Initialized → Initialized → Logged In → In Lobby → Online (peers connected)
                                                      → Offline (alone in lobby)
```

**Important:** `IsOnline` requires at least one other peer to be connected via P2P. If you're the only person in the lobby, `IsOnline` is `false`. This is by design — there's no one to network with. Use `EOSLobbyManager.Instance.IsInLobby` to check if you're in a lobby regardless of peer count.

## What Happens Automatically

When you join a lobby, the following chain fires without any code on your part:

1. **`EOSLobbyManager.OnLobbyJoined`** fires
2. **`EOSP2PManager.OnLobbyJoined()`** calls `Initialize()`, accepts existing members, sends handshake packets
3. **Handshake retry** — if the initial handshake doesn't connect (timing/NAT), automatic retries fire every 2 seconds (up to 5 times)
4. **`EOSP2PManager.OnPeerConnected`** fires for each peer that establishes a P2P link
5. **`NetworkManager.OnPeerConnected()`** elects a host, creates `NetworkRoomState` and `NetworkPlayerState` objects
6. **`PlayerSpawner`** detects online state and calls `Spawn()` for the local player

**Voice** also auto-connects when the lobby has voice enabled — no manual wiring needed.

## Manual Control

If you don't want `PlayerSpawner` and prefer manual spawning:

```csharp
using EOSNative.Net;
using EOSNative.P2P;

public class MyGameStarter : MonoBehaviour
{
    [SerializeField] private GameObject playerPrefab;

    void OnEnable()
    {
        // Listen for peer connections
        EOSP2PManager.Instance.OnPeerConnected += OnPeerConnected;
    }

    void OnDisable()
    {
        if (EOSP2PManager.Instance != null)
            EOSP2PManager.Instance.OnPeerConnected -= OnPeerConnected;
    }

    void OnPeerConnected(string peerId)
    {
        if (!NetworkManager.Instance.IsHost) return;

        // Host spawns things when peers join
        // Each peer's PlayerSpawner (or manual code) spawns their own player
    }

    public void SpawnMyPlayer(Vector3 position)
    {
        // Spawn with auto-register (no need for RegisterPrefab)
        var obj = NetworkManager.Instance.Spawn(playerPrefab, position, Quaternion.identity);
        // obj is your local NetworkObject — you're the owner
    }
}
```

Or use `SimulationBehaviour` for a manager that auto-subscribes to tick and peer events:

```csharp
using EOSNative.Net;

public class MatchManager : SimulationBehaviour
{
    protected override void OnBecameHost()
    {
        Debug.Log("I am now the host!");
    }

    protected override void OnPeerConnected(string peerId)
    {
        Debug.Log($"Peer joined: {peerId}");
    }

    protected override void OnTick(uint tick, float deltaTime)
    {
        // Runs at fixed tick rate, decoupled from frame rate
    }
}
```

## Common Pitfalls

### "IsOnline stays false"

You're alone in the lobby. `IsOnline` requires at least one other peer connected via P2P. Open a second instance (ParrelSync clone or build) and join the same lobby.

### "Nothing spawns"

Check:
1. Do you have a `PlayerSpawner` component in the scene?
2. Is the player prefab assigned to `PlayerSpawner.PlayerPrefab`?
3. Is the prefab registered? (Either via `NetworkPrefabTable`, `RegisterPrefab()`, or `Spawn(GameObject)`)
4. Does the prefab have a `NetworkObject` component?

### "Player spawns but doesn't sync"

- **Position:** Add `NetworkTransform` to the prefab
- **Custom data:** Use `SyncVar<T>` in a `NetworkBehaviour` on the prefab
- **Only the owner can write SyncVars.** Check `IsOwner` before setting values.

### "Peer connects but immediately disconnects"

The P2P handshake requires **both sides** to accept the connection AND at least one side to send data. This is handled automatically by `EOSP2PManager`, but if you're doing manual P2P management, ensure you call `AcceptPeer()` on both sides.

### "Host migration breaks everything"

It shouldn't — host migration is automatic. When the host leaves, the next peer is elected. All `NetworkObject` references remain valid. `DestroyWithOwner = true` objects are cleaned up. But if you're caching the host's PUID, update it when `OnHostChanged` fires.

## Testing Locally

### ParrelSync (Recommended)

1. Install [ParrelSync](parrelsync.md) via the Setup Wizard's Dependencies tab
2. Open a clone (`ParrelSync > Clones Manager > Open in New Editor`)
3. Both editors share the same project files but run independently
4. Play in both → join the same lobby → networking activates

### Build + Editor

1. Build your project (File > Build and Run)
2. Play in the Editor
3. Join the same lobby from both instances

### Quick Test

Fastest path to see networking work:

1. Setup EOSManager with credentials
2. Add `NetworkManager` + `PlayerSpawner` to scene (assign any prefab with `NetworkObject`)
3. Open two instances (ParrelSync or build)
4. In both: Play → F1 → Lobbies → Quick Match
5. Watch the player prefab spawn on both screens

## Next Steps

- [Networking Overview](networking.md) — SyncVars, RPCs, NetworkObject, spawning details
- [Typed RPCs](rpc-system.md) — `[NetRpc]` attribute for zero-boilerplate RPCs
- [Nested Objects & Reparenting](nested-objects.md) — Parent-child NetworkObject hierarchies
- [P2P Transport](p2p-transport.md) — Low-level packet system
- [Connection Statistics](connection-stats.md) — RTT, packet loss, bandwidth monitoring
