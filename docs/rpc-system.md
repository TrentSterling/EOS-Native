# Typed RPCs ([NetRpc])

Zero-boilerplate typed RPCs using IL post-processing. Mark a method with `[NetRpc]` and call it like a normal method. The weaver handles serialization and network dispatch automatically. Same technique as Mirror, FishNet, and Fusion.

## Basic Usage

```csharp
public class Player : NetworkBehaviour
{
    SyncVar<float> Health;

    protected override void Awake()
    {
        base.Awake();
        Health = Sync(100f);
    }

    [NetRpc(RPCTarget.All)]
    public void TakeDamage(float damage)
    {
        Health.Value -= damage;
    }
}

// Calling is transparent - all peers execute TakeDamage:
player.TakeDamage(19f);
```

No registration, no serialization code, no string names. The IL post-processor rewrites the method at compile time.

## How It Works

The `EOSNetRpcPostProcessor` (Mono.Cecil IL post-processor) scans for methods marked with `[NetRpc]` and rewrites them at compile time. For each attributed method, the weaver generates:

1. **`UserCode_TakeDamage(float)`** -- The original method body, moved here
2. **`TakeDamage(float)`** -- Dispatch stub: serializes args via `NetSerializers.Write<T>()`, calls `SendRPCWeaved()`
3. **`__InvokeNetRpc_TakeDamage(NetReader)`** -- Deserializer: `NetSerializers.Read<T>()` each param, calls `UserCode_`
4. **`__RegisterNetRPCs()`** -- Override that registers invoke handlers after NetworkId is assigned

Method identification uses compile-time FNV-1a hashing of the method name. No string lookups at runtime.

## RPCTarget Options

| Target | Description |
|--------|-------------|
| `RPCTarget.All` | Execute on all peers including self |
| `RPCTarget.Others` | Execute on all peers excluding self |
| `RPCTarget.Host` | Execute on the current host only |
| `RPCTarget.Owner` | Execute on the object's owner only |
| `RPCTarget.Players` | Execute on all non-spectator peers (skips spectators) |

```csharp
[NetRpc(RPCTarget.All)]
public void Explode() { }

[NetRpc(RPCTarget.Owner)]
public void RequestScorePoint(int amount) { }

[NetRpc(RPCTarget.Host)]
public void RequestSpawn(Vector3 position) { }

[NetRpc(RPCTarget.Players)]
public void StartRound() { }
```

## Supported Parameter Types

All types registered in `NetSerializers` can be used as RPC parameters:

| Type | Notes |
|------|-------|
| `byte` | |
| `bool` | |
| `short`, `ushort` | |
| `int`, `uint` | |
| `long`, `ulong` | |
| `float` | |
| `double` | |
| `string` | UTF-8, ushort length prefix |
| `Vector2` | |
| `Vector3` | |
| `Quaternion` | |
| `Color` | |
| `Color32` | |
| `ProductUserId` | EOS user identifier |
| `byte[]` | Raw byte array |
| `NetworkObject` | Serialized as NetworkId (uint), auto-resolved on receiver |
| `INetSerializable` | Custom types implementing the interface |

## Host-Validated RPCs

For anti-cheat and authoritative gameplay, RPCs can be routed through the host for validation before being broadcast. The host acts as a relay - it receives the RPC, runs an optional validator, and only rebroadcasts to all peers if approved.

### Basic Usage

```csharp
public class Player : NetworkBehaviour
{
    SyncVar<int> Score;

    [NetRpc(RPCTarget.All, Validated = true)]
    public void AddScore(int amount)
    {
        Score.Value += amount;
    }
}
```

With `Validated = true`, calling `AddScore(10)` sends the RPC to the host first. The host validates and rebroadcasts to all peers (including executing locally). Without a validator method, the host auto-approves (relay-only mode).

### Adding a Validator

Define a method named `Validate_<MethodName>` on the same class. The IL weaver auto-discovers it by naming convention - no `nameof` or registration needed.

```csharp
public class Player : NetworkBehaviour
{
    SyncVar<int> Score;

    [NetRpc(RPCTarget.All, Validated = true)]
    public void AddScore(int amount)
    {
        Score.Value += amount;
    }

    // Auto-discovered by the weaver - runs on the HOST only
    private bool Validate_AddScore(ProductUserId sender, NetworkObject target, byte[] argData)
    {
        // Only the object owner can add score
        if (!sender.Equals(target.OwnerId)) return false;

        // Deserialize args to inspect values
        var reader = new NetReader(argData);
        int amount = NetSerializers.Read<int>(reader);

        // Reject unreasonable values
        return amount > 0 && amount <= 100;
    }
}
```

**Validator signature:** `bool Validate_X(ProductUserId sender, NetworkObject target, byte[] argData)`

- `sender` - the peer who sent the RPC
- `target` - the NetworkObject the RPC targets
- `argData` - raw serialized arguments (use NetReader to deserialize)
- Return `true` to approve (host rebroadcasts), `false` to reject (RPC is dropped)

### Flow Diagram

```
Client calls AddScore(10)
    → Serialized as MSG_RPC_VALIDATED (0xAD)
    → Sent to Host only

Host receives:
    → Runs Validate_AddScore(sender, target, args)
    → If approved: rebroadcasts as MSG_RPC_REBROADCAST (0xAE) to ALL peers
    → If rejected: drops silently

All peers (including host) receive rebroadcast:
    → Execute AddScore(10) normally
```

If the host calls a validated RPC, it validates locally and rebroadcasts without the network round-trip.

### When to Use

| Scenario | Use Validated? |
|----------|---------------|
| Score/currency changes | Yes - prevent clients from awarding arbitrary points |
| Damage dealing | Yes - validate damage amount and source |
| Spawning items | Yes - validate spawn location and rate |
| Movement updates | No - too frequent, use SyncVars instead |
| Cosmetic effects | No - no gameplay impact |
| Chat messages | Maybe - could validate for profanity/rate limiting |

### No Validator = Relay Only

If you omit the `Validate_` method, the host auto-approves and relays. This is useful when you want host-authoritative broadcast ordering without custom validation logic.

## Constraints

The weaver enforces these constraints at compile time. Violations produce compiler errors.

- **void return only** -- RPCs cannot return values
- **No ref/out parameters** -- All parameters are passed by value
- **No generic methods** -- The method itself cannot be generic
- **No abstract methods** -- Must have a concrete body to rewrite

## NetworkBehaviour Lifecycle

When using `[NetRpc]`, the weaver generates a `__RegisterNetRPCs()` override that registers all RPC handlers. These lifecycle hooks are called by `NetworkObject`:

```csharp
public class MyPlayer : NetworkBehaviour
{
    // Called after NetworkId is assigned and RPCs are registered
    public override void OnNetworkSpawn()
    {
        Debug.Log($"Spawned with NetworkId {Net.NetworkId}");
    }

    // Called before the object is deactivated or pooled
    public override void OnNetworkDespawn()
    {
        Debug.Log("Cleaning up");
    }
}
```

## String-Based RPCs (Legacy)

The original string-based RPC API is fully functional and backward compatible. Use it when you need dynamic RPC names or when working outside of `NetworkBehaviour`.

### Registration

```csharp
NetworkManager.Instance.RegisterRPC(Net, "TakeDamage", reader =>
{
    float dmg = NetSerializers.Read<float>(reader);
    Health.Value -= dmg;
});
```

### Sending

```csharp
NetworkManager.Instance.SendRPC(target, "TakeDamage", RPCTarget.Owner, 25f);
```

### When to Use Each

| Approach | Best For |
|----------|----------|
| `[NetRpc]` | Standard gameplay RPCs on NetworkBehaviour subclasses |
| `RegisterRPC` / `SendRPC` | Dynamic RPCs, non-NetworkBehaviour code, runtime-registered objects |

Both approaches can coexist on the same object. The weaver does not interfere with manually registered RPCs.

## RPC Migration Buffer

Host-targeted and owner-targeted RPCs are automatically buffered during host migration. When a peer disconnects and host re-election occurs, any RPCs sent during that window are queued and replayed once the new host is confirmed. No RPCs are dropped during transition.

## CodeGen Assembly

The IL post-processor lives in `EOSNative.CodeGen/` inside the package Runtime folder. It is editor-only and has no engine references.

| File | Description |
|------|-------------|
| `EOSNative.CodeGen.asmdef` | Assembly definition (editor-only, `noEngineReferences: true`) |
| `EOSNetRpcPostProcessor.cs` | `ILPostProcessor` entry point |
| `RpcWeaver.cs` | Core weaving logic (~400 lines) |
| `WeaverTypes.cs` | Resolves all Cecil type/method references |
| `PostProcessorAssemblyResolver.cs` | Custom Cecil assembly resolver |

**Dependency:** Requires `com.unity.nuget.mono-cecil` (1.11.6) in the project's `manifest.json`. The package provides the Mono.Cecil DLLs used for IL rewriting.
