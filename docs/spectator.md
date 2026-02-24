# Spectator Mode & RPC Validation

## Spectator Mode

Join a game as a read-only observer. Spectators receive all state updates (object sync, RPCs, scene loads) but cannot spawn objects or become host.

### How to Use

Set `IsSpectator = true` on `NetworkManager` before joining a lobby:

```csharp
// Join as spectator
NetworkManager.Instance.IsSpectator = true;

// Then join lobby normally
await EOSLobbyManager.Instance.JoinLobbyByCodeAsync("1234");
```

When the spectator connects, their `NetworkPlayerState` is automatically created with `CustomData["_spectator"] = "1"`. All peers read this key and track which users are spectating.

### Checking Spectator Status

```csharp
// Check self
if (NetworkManager.Instance.IsSpectator)
    Debug.Log("I am spectating");

// Check a specific peer
if (NetworkManager.Instance.IsPeerSpectator(puid))
    Debug.Log($"{puid} is spectating");

// From a NetworkBehaviour
if (IsSpectator)
    return; // Skip gameplay logic
```

### What Spectators Cannot Do

- **Spawn objects:** `Spawn()` returns null with a warning
- **Become host:** Host election skips spectator PUIDs
- **Execute player RPCs:** `RPCTarget.Players` skips spectators (see below)

### RPCTarget.Players

`RPCTarget.Players` (value 4) sends RPCs only to non-spectator peers:

```csharp
[NetRpc(RPCTarget.Players)]
public void StartRound()
{
    // Only executes on players, not spectators
    roundActive = true;
}
```

Behavior:
- `executeLocal` = true only if the caller is not a spectator
- Remote send goes only to non-spectator peers via `SendToNonSpectators()`

### Edge Cases

If all connected peers are spectators, the lexicographically lowest PUID becomes host anyway. A warning is logged when this occurs.

### RPCTarget Reference

| Target | Value | Behavior |
|--------|-------|----------|
| All | 0 | Send to all peers including self |
| Others | 1 | Send to all peers excluding self |
| Host | 2 | Send to the current host only |
| Owner | 3 | Send to the object's owner only |
| Players | 4 | Send to all non-spectator peers (including self if not spectator) |

## RPC Validation

Opt-in callback on `NetworkManager` that fires before executing any incoming remote RPC. The host (or any peer) can reject unauthorized RPCs. When the callback is null (the default), all RPCs are allowed with zero overhead.

### OnRPCValidation

```csharp
// Signature
public Func<ProductUserId, NetworkObject, uint, bool> OnRPCValidation;
// Parameters: (sender, targetObject, methodHash) -> allow?
```

When set, `HandleRPC()` checks this callback after reading the networkId and methodHash from the incoming message. If it returns false, a warning is logged and the RPC handler is skipped.

### Owner-Only Validation

The most common pattern is to only allow RPCs from the object's owner:

```csharp
// Convenience helper - one line
NetworkManager.Instance.EnableOwnerOnlyRPCValidation();

// Equivalent to:
NetworkManager.Instance.OnRPCValidation = (sender, target, hash) =>
    target != null && target.OwnerId == sender;
```

### Custom Validation

Allow specific RPCs from non-owners while blocking everything else:

```csharp
NetworkManager.Instance.OnRPCValidation = (sender, target, hash) =>
{
    if (target == null) return false;

    // Owner can always send RPCs to their own objects
    if (target.OwnerId == sender) return true;

    // Allow specific cross-owner RPCs (e.g. damage, interact)
    return hash == NetworkManager.FnvHash("TakeDamage")
        || hash == NetworkManager.FnvHash("Interact");
};
```

### Disabling Validation

```csharp
// Set to null to disable (zero overhead, all RPCs allowed)
NetworkManager.Instance.OnRPCValidation = null;
```
