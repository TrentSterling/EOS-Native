# Nested NetworkObjects & Reparenting

Support for hierarchical NetworkObject prefabs and runtime hierarchy changes.

## Nested NetworkObjects (v2.33.0)

Place multiple NetworkObjects in a prefab hierarchy. When the root is spawned, all child NetworkObjects are automatically discovered, assigned NetworkIds, and synchronized.

### Setup

```
Network Player (root)       ← NetworkObject
├── Head                    ← NetworkObject + NetworkTransform
├── LeftHand                ← NetworkObject + NetworkTransform
└── RightHand               ← NetworkObject + NetworkTransform
```

No special setup needed. Just add `NetworkObject` (and optionally `NetworkTransform`) to each child that needs independent sync. `Spawn()` handles the rest.

### How It Works

1. `Spawn()` calls `GetComponentsInChildren<NetworkObject>(true)` — root is index 0, children follow
2. Each child gets its own `NetworkId` with `ParentNetworkId` pointing to the root
3. Children share the root's `PrefabId`, `OwnerId`, and `DestroyWithOwner`
4. Despawn/TransferAuthority on the root cascades to all children automatically
5. Direct child despawn/transfer is blocked (logs a warning)

### Properties

```csharp
netObj.ParentNetworkId       // 0 = root, non-zero = parent's NetworkId
netObj.IsChildNetworkObject  // true if ParentNetworkId != 0
netObj.IsRootNetworkObject   // true if ParentNetworkId == 0
```

### Interest Management

Children inherit spatial interest from their root parent. If the root is visible to a peer, all children are visible too.

### Wire Format

Spawn/snapshot messages serialize children inline with the root:

```
[Root data...] [ChildCount:byte]
Per child: [NetworkId:u32] [LocalIndex:byte] [Flags:byte] [DataLen:u16]
           [SyncVarCount:byte] [SyncVarData...]
```

Single-object prefabs: `ChildCount = 0` (1 extra byte overhead).

## Runtime Reparenting (v2.34.0)

Detach children from or attach objects to network hierarchies at runtime. Use cases: weapon pickup/drop, VR hand grabbing, ragdoll detachment, inventory systems.

### API

```csharp
// Detach a child — becomes an independent root object
childNetObj.DetachFromNetworkParent();

// Attach an object to a new parent (must be a root NetworkObject)
weaponNetObj.SetNetworkParent(playerHandNetObj);

// Or via NetworkManager directly
NetworkManager.Instance.ReparentObject(obj, newParent);  // null = detach
```

Only the **owner** or **host** can reparent. World position/rotation are preserved (`worldPositionStays: true`).

### Events

```csharp
netObj.OnReparented += (oldParent, newParent) =>
{
    // oldParent/newParent are NetworkObject references (null = no parent)
    if (newParent == null)
        Debug.Log("Detached!");
    else
        Debug.Log($"Attached to {newParent.NetworkId}");
};
```

### Examples

**Weapon pickup/drop:**

```csharp
public class Weapon : NetworkBehaviour
{
    public void PickUp(NetworkObject hand)
    {
        if (!IsOwner) return;
        Net.SetNetworkParent(hand);
        transform.localPosition = Vector3.zero;
        transform.localRotation = Quaternion.identity;
    }

    public void Drop()
    {
        if (!IsOwner) return;
        Net.DetachFromNetworkParent();
        // weapon is now an independent root at its current world position
    }
}
```

**VR hand detachment:**

```csharp
// Detach a hand from the VR player (e.g., ragdoll on death)
leftHandNetObj.DetachFromNetworkParent();
// Hand keeps its world position, becomes independently tracked
```

### Rules & Constraints

- **Target must be a root.** You cannot attach to a child NetworkObject — only root objects can be parents.
- **Owner inherits.** When attaching to a parent, the child inherits the parent's `OwnerId`.
- **Position sync is NetworkTransform's job.** Reparenting only changes the hierarchy and notifies peers. Position interpolation is handled by NetworkTransform.
- **OriginalParentNetworkId is immutable.** Set once at spawn, tracks which root prefab a child was originally part of. Used internally for snapshot serialization.
- **Detached children survive root despawn.** If the root is despawned, any previously detached children remain as independent objects.
- **Offline mode works.** Reparenting applies locally without network broadcast.

### Late-Join Snapshots

Late joiners receive the correct hierarchy state:

- **Original children** (still attached or detached) are serialized inline with their root, with a flags byte (`0x00` = attached, `0x01` = detached)
- **Dynamically attached roots** (originally independent, now children) appear as top-level snapshot entries, then `MSG_REPARENT` sets their parent after the snapshot

### Inspector

The NetworkObject Inspector shows:

- **Configuration** (edit time): Destroy With Owner, Always Visible
- **Hierarchy** (edit + play): Parent NetworkObject reference, child count
- **Runtime Status** (play mode): Network ID, Owner, Is Registered, SyncVar Count, Parent Net ID
