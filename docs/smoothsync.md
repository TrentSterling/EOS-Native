# SmoothSync Technical Analysis

Extracted from `E:\GITHUBDROPZ\HOIST\Assets\Smooth Sync\` (FishNet implementation).
Purpose: Understand interpolation/extrapolation algorithms for EOS-Native NetworkTransform improvements.

## Core Architecture

### State Buffer

```csharp
public StateFishNet[] stateBuffer;  // Index 0 is newest
public int stateCount;

public struct StateFishNet
{
    public float ownerTimestamp;        // Owner's local time when state was captured
    public Vector3 position;
    public Quaternion rotation;
    public Vector3 scale;
    public Vector3 velocity;            // Required for extrapolation
    public Vector3 angularVelocity;
    public bool teleport;               // Flag for snap-to behavior
    public bool atPositionalRest;       // Flag to stop position extrapolation
    public bool atRotationalRest;       // Flag to stop rotation extrapolation
    public float receivedTimestamp;     // When this state arrived on receiver
    public int localTimeResetIndicator; // Handles time resets
}
```

**Buffer sizing:** `((int)(sendRate * interpolationBackTime) + 1) * 2`, minimum 30 entries. Sized to hold enough states to cover `interpolationBackTime` at the configured send rate, with 2x multiplier for forced sends.

---

## Interpolation Algorithm

**Core concept:** Blend between two historical states from the buffer using linear interpolation (Lerp). The receiver always renders in the past (`interpolationBackTime` behind the owner's clock).

### Timing Model

```
Owner's Timeline:
    StateA (t=0.0s) -----> StateB (t=0.05s) -----> StateC (t=0.1s)

Receiver's Timeline (interpolationBackTime = 100ms):
    Now = 1.0s, approxOwnerTime = 1.0s
    targetTime = 1.0 - 0.1 = 0.9s  (look into the past)

    Find two states that bracket targetTime, lerp between them.
```

### Algorithm

```csharp
void interpolate(float interpolationTime)
{
    // 1. Find bracketing states (binary search by timestamp)
    int stateIndex = 0;
    for (; stateIndex < stateCount; stateIndex++)
    {
        if (stateBuffer[stateIndex].ownerTimestamp <= interpolationTime)
            break;
    }
    if (stateIndex == stateCount) stateIndex--;

    StateFishNet start = stateBuffer[stateIndex];                      // Older state
    StateFishNet end = stateBuffer[Mathf.Max(stateIndex - 1, 0)];      // Newer state

    // 2. Calculate blend factor (0.0 to 1.0)
    float t = (interpolationTime - start.ownerTimestamp) /
              (end.ownerTimestamp - start.ownerTimestamp);

    // 3. Check for teleportation flags
    shouldTeleport(start, ref end, interpolationTime, ref t);

    // 4. Interpolate all state components
    targetTempState = StateFishNet.Lerp(targetTempState, start, end, t);

    // 5. Apply snap thresholds for large jumps
    if (snapPositionThreshold != 0)
    {
        float positionDifference = (end.position - start.position).magnitude;
        if (positionDifference > snapPositionThreshold)
        {
            targetTempState.position = end.position;  // Snap instead of lerp
            dontEasePosition = true;
        }
    }
}
```

### Lerp Implementation

```csharp
public static StateFishNet Lerp(StateFishNet target, StateFishNet start,
                                 StateFishNet end, float t)
{
    target.position = Vector3.Lerp(start.position, end.position, t);
    target.rotation = Quaternion.Lerp(start.rotation, end.rotation, t);
    target.scale = Vector3.Lerp(start.scale, end.scale, t);
    target.velocity = Vector3.Lerp(start.velocity, end.velocity, t);
    target.angularVelocity = Vector3.Lerp(start.angularVelocity,
                                          end.angularVelocity, t);
    target.ownerTimestamp = Mathf.Lerp(start.ownerTimestamp,
                                       end.ownerTimestamp, t);
    return target;
}
```

---

## Extrapolation Algorithm

**Core concept:** Predict future position/rotation based on velocity when the buffer runs out of states (latency spike, packet loss).

### Extrapolation Modes

- **None**: Freeze when no new data
- **Limited**: Apply time and/or distance limits (default)
- **Unlimited**: Extrapolate forever

### Algorithm

```csharp
bool extrapolate(float interpolationTime)
{
    // Initialize from newest state if not already extrapolating
    if (!extrapolatedLastFrame || targetTempState.ownerTimestamp < stateBuffer[0].ownerTimestamp)
    {
        targetTempState.copyFromState(stateBuffer[0]);
        timeSpentExtrapolating = 0;
    }

    // VELOCITY ESTIMATION for kinematic objects (no synced velocity)
    if (stateCount >= 2)
    {
        if (syncVelocity == SyncMode.NONE && !stateBuffer[0].atPositionalRest)
        {
            // Calculate velocity from two recent position deltas
            targetTempState.velocity =
                (stateBuffer[0].position - stateBuffer[1].position) /
                (stateBuffer[0].ownerTimestamp - stateBuffer[1].ownerTimestamp);
        }
        if (syncAngularVelocity == SyncMode.NONE && !stateBuffer[0].atRotationalRest)
        {
            Quaternion deltaRot = stateBuffer[0].rotation *
                                 Quaternion.Inverse(stateBuffer[1].rotation);
            Vector3 eulerRot = new Vector3(
                Mathf.DeltaAngle(0, deltaRot.eulerAngles.x),
                Mathf.DeltaAngle(0, deltaRot.eulerAngles.y),
                Mathf.DeltaAngle(0, deltaRot.eulerAngles.z)
            );
            targetTempState.angularVelocity = eulerRot /
                (stateBuffer[0].ownerTimestamp - stateBuffer[1].ownerTimestamp);
        }
    }

    if (extrapolationMode == ExtrapolationMode.None) return false;

    // Check time limit
    if (useExtrapolationTimeLimit && timeSpentExtrapolating > extrapolationTimeLimit)
        return false;

    // Stop if not moving (velocity near zero)
    bool hasVelocity = Mathf.Abs(targetTempState.velocity.x) >= .01f ||
                       Mathf.Abs(targetTempState.velocity.y) >= .01f ||
                       Mathf.Abs(targetTempState.velocity.z) >= .01f;
    if (!hasVelocity && !hasAngularVelocity) return false;

    // Calculate time delta
    float timeDif;
    if (timeSpentExtrapolating == 0)
        timeDif = interpolationTime - targetTempState.ownerTimestamp;  // Catch-up
    else
        timeDif = Time.deltaTime;  // Frame-by-frame

    timeSpentExtrapolating += timeDif;

    // --- POSITION EXTRAPOLATION ---
    targetTempState.position += targetTempState.velocity * timeDif;

    // Add gravity if Rigidbody with useGravity
    if (hasRigidbody && rb.useGravity)
        targetTempState.velocity += Physics.gravity * timeDif;

    // Apply linear drag damping
    if (hasRigidbody)
        targetTempState.velocity -= targetTempState.velocity * timeDif * rb.linearDamping;

    // --- ROTATION EXTRAPOLATION ---
    float axisLength = timeDif * targetTempState.angularVelocity.magnitude;
    Quaternion angularRotation = Quaternion.AngleAxis(axisLength,
                                                      targetTempState.angularVelocity);
    targetTempState.rotation = angularRotation * targetTempState.rotation;

    // Apply angular drag
    if (hasRigidbody && rb.angularDamping > 0)
        targetTempState.angularVelocity -= targetTempState.angularVelocity *
                                           timeDif * rb.angularDamping;

    // Check distance limit
    if (useExtrapolationDistanceLimit &&
        Vector3.Distance(stateBuffer[0].position, targetTempState.position) >=
        extrapolationDistanceLimit)
        return false;

    return true;
}
```

---

## Decision Tree: Interpolation vs Extrapolation

```csharp
void applyInterpolationOrExtrapolation()
{
    if (stateCount == 0) return;

    float interpolationTime = approximateNetworkTimeOnOwner - interpolationBackTime;

    // 1. INTERPOLATION: We have states bracketing the target time
    if (stateCount > 1 && stateBuffer[0].ownerTimestamp > interpolationTime)
    {
        interpolate(interpolationTime);
        extrapolatedLastFrame = false;
    }
    // 2. AT REST: Object is idle, just copy final state
    else if (stateBuffer[0].atPositionalRest && stateBuffer[0].atRotationalRest)
    {
        targetTempState.copyFromState(stateBuffer[0]);
        extrapolatedLastFrame = false;
    }
    // 3. EXTRAPOLATION: No data, predict forward
    else if (!isSmoothingAuthorityChanges ||
             localTime - latestAuthorityChangeZeroTime > interpolationBackTime * 2.0f)
    {
        bool success = extrapolate(interpolationTime);
        extrapolatedLastFrame = true;
        triedToExtrapolateTooFar = !success;
    }

    // 4. EASING: Lerp actual transform toward target
    float actualPositionLerpSpeed = positionLerpSpeed;  // Default 0.85

    if (dontEasePosition) actualPositionLerpSpeed = 1.0f;  // Snap

    setPosition(Vector3.Lerp(getPosition(), newPosition, actualPositionLerpSpeed));
    setRotation(Quaternion.Lerp(getRotation(), newRotation, rotationLerpSpeed));
}
```

**Key insight:** There's a TWO-STAGE pipeline:
1. **Target calculation** - interpolation or extrapolation computes where the object *should* be
2. **Easing** - the actual transform lerps toward the target at `positionLerpSpeed` (0.85 default)

This double-lerp provides extra smoothing on top of the interpolation.

---

## Time Synchronization (Dual-Clock)

SmoothSync maintains an estimated owner clock on each receiver, adjusted gradually.

```csharp
void adjustOwnerTime()
{
    // Don't adjust if at rest (no new data)
    if (stateBuffer[0].atPositionalRest && stateBuffer[0].atRotationalRest)
        return;

    // Estimate what owner's current time should be
    float newTime = stateBuffer[0].ownerTimestamp +
                    (localTime - stateBuffer[0].receivedTimestamp);

    float timeCorrection = Mathf.Max(timeCorrectionSpeed * Time.deltaTime, minTimePrecision);
    float timeChangeMagnitude = Mathf.Abs(approximateNetworkTimeOnOwner - newTime);

    if (receivedStatesCounter < sendRate ||      // First states
        timeChangeMagnitude < timeCorrection ||  // Small difference
        timeChangeMagnitude > snapTimeThreshold) // Large jump
    {
        approximateNetworkTimeOnOwner = newTime; // Snap
    }
    else
    {
        // Smoothly drift toward correct time
        if (approximateNetworkTimeOnOwner < newTime)
            approximateNetworkTimeOnOwner += timeCorrection;
        else
            approximateNetworkTimeOnOwner -= timeCorrection;
    }
}

// Estimated owner time auto-advances between updates
public float approximateNetworkTimeOnOwner
{
    get => _ownerTime + (localTime - lastTimeOwnerTimeWasSet);
    set { _ownerTime = value; lastTimeOwnerTimeWasSet = localTime; }
}
```

---

## Rest State Detection

Dramatically reduces extrapolation drift by detecting idle objects.

```csharp
enum RestState { AT_REST, JUST_STARTED_MOVING, MOVING }

// Owner-side detection:
if (positionLastFrame == getPosition())
{
    samePositionCount++;
    if (samePositionCount == atRestThresholdCount)  // Default: 3 frames
    {
        restStatePosition = RestState.AT_REST;
        forceStateSendNextFixedUpdate();  // Broadcast "I stopped"
    }
}
else
{
    if (restStatePosition == RestState.AT_REST)
    {
        restStatePosition = RestState.JUST_STARTED_MOVING;
        forceStateSendNextFixedUpdate();  // Broadcast "I started moving"
    }
}
```

**Receiver-side:** When `atPositionalRest == true`, skip extrapolation entirely - just hold the last known position.

---

## Teleportation & Snap Correction

### Explicit Teleport Flag

```csharp
// Owner calls this to skip interpolation
public void teleport()
{
    // Sets teleport flag on next outgoing state
    // Receiver forces t=1.0 and disables easing
}

void shouldTeleport(StateFishNet start, ref StateFishNet end,
                    float interpolationTime, ref float t)
{
    if (end.teleport == true)
    {
        t = 1;              // Jump to end state instantly
        stopEasing();       // No lerp smoothing either
    }
}
```

### Distance-Based Snap

```csharp
// If states are far apart, snap instead of lerp
if (snapPositionThreshold != 0)
{
    float distance = (end.position - start.position).magnitude;
    if (distance > snapPositionThreshold)
    {
        targetTempState.position = end.position;
        dontEasePosition = true;  // Force lerp speed to 1.0
    }
}
```

---

## Kinematic Object Handling (No Rigidbody)

SmoothSync handles Transform-only objects by **estimating velocity from position deltas**:

```csharp
// No synced velocity? Calculate from recent states
if (syncVelocity == SyncMode.NONE && !stateBuffer[0].atPositionalRest)
{
    targetTempState.velocity =
        (stateBuffer[0].position - stateBuffer[1].position) /
        (stateBuffer[0].ownerTimestamp - stateBuffer[1].ownerTimestamp);
}
```

For extrapolation on kinematic objects:
- No gravity applied (no Rigidbody)
- No drag damping (no Rigidbody)
- Pure linear velocity extrapolation: `pos += vel * dt`
- Distance/time limits still apply

---

## Half-Float Compression

```csharp
// Convert float to 16-bit half for bandwidth savings (4 bytes -> 2 bytes per axis)
public static ushort Compress(float value);     // float -> half
public static float Decompress(ushort value);   // half -> float

// Optional per-component compression flags
public bool isPositionCompressed = false;      // 12 bytes -> 6 bytes
public bool isRotationCompressed = false;
public bool isVelocityCompressed = false;
```

---

## Configuration Options

| Option | Type | Default | Purpose |
|--------|------|---------|---------|
| `interpolationBackTime` | float | 0.1s | How far in past to interpolate |
| `extrapolationMode` | enum | Limited | None/Limited/Unlimited |
| `extrapolationTimeLimit` | float | 5.0s | Max time to extrapolate |
| `extrapolationDistanceLimit` | float | 20.0 | Max distance to extrapolate |
| `sendRate` | float | 30 | Network updates per second |
| `positionLerpSpeed` | float | 0.85 | Easing speed toward target (0-1) |
| `rotationLerpSpeed` | float | 0.85 | Rotation easing speed |
| `timeCorrectionSpeed` | float | 0.1 | Clock drift adjustment speed |
| `snapTimeThreshold` | float | 0.3s | Jump time instead of drifting |
| `snapPositionThreshold` | float | 0 | Snap instead of lerp above this distance |
| `snapRotationThreshold` | float | 0 | Snap instead of lerp above this angle |
| `sendPositionThreshold` | float | 0 | Only send if moved this far |
| `sendRotationThreshold` | float | 0 | Only send if rotated this much |
| `setVelocityInsteadOfPositionOnNonOwners` | bool | false | Physics-friendly mode |
| `useLocalTransformOnly` | bool | false | Sync local space (VR) |
| `atRestThresholdCount` | int | 3 | Frames before declaring at-rest |

---

## Latency Spike Example

```
Frame 0: Receive State@t=0.0s [pos=(0,0,0), vel=(5,0,0)]
Frame 1: Receive State@t=0.033s [pos=(0.165,0,0), vel=(5,0,0)]
Frame 2: No state received! (packet loss)

         → interpolationTime = 0.066s (want to be 100ms in past)
         → stateBuffer[0].ownerTimestamp (0.033s) < interpolationTime
         → Can't interpolate → fall through to extrapolate()

         → startState = latest (pos=0.165, vel=5)
         → position += velocity * deltaTime = 0.165 + 5*0.016 = 0.245
         → Apply drag if Rigidbody

Frame 3: Receive State@t=0.066s [pos=(0.33,0,0)]
         → Back to interpolation, smooth correction via easing lerp
```

---

## Key Insights for EOS-Native NetworkTransform

### What SmoothSync Does Well

1. **Dual-stage pipeline** - target calculation (interp/extrap) + easing (lerp toward target). Two layers of smoothing.
2. **Rest state detection** - saves bandwidth AND prevents extrapolation drift.
3. **Velocity estimation from deltas** - kinematic objects don't need to sync velocity explicitly.
4. **Clock synchronization** - smooth drift correction instead of jarring time jumps.
5. **Graceful degradation** - interpolation -> extrapolation -> freeze, with configurable limits.

### How It Differs from Our Spring Sync

| Aspect | SmoothSync | Our Spring Sync (NetworkTransform) |
|--------|-----------|-----------------------------------|
| **Smoothing method** | Lerp with easing factor (0.85) | Damped spring physics |
| **Extrapolation** | Linear velocity + gravity/drag | Velocity extrapolation only |
| **State buffer** | 30+ timestamped states | Single target state (SyncVars) |
| **Time model** | Estimated owner clock, play in past | No explicit time model |
| **Rest detection** | AT_REST flag, threshold count | None (always syncing) |
| **Kinematic handling** | Velocity estimation from deltas | Same transform path as physics |
| **Compression** | Half-float optional | Vector3Half always |

### What We Could Adopt (Best of Both Worlds)

1. **Rest state detection** - Stop syncing when idle. Saves bandwidth, prevents drift. Easy to add: track `samePositionCount`, broadcast rest flag.

2. **Velocity estimation for kinematic objects** - Our NetworkTransform always uses spring sync. For kinematic objects (no Rigidbody), we could estimate velocity from position deltas and use linear extrapolation + easing instead of spring forces.

3. **Extrapolation limits** - Add configurable time/distance limits to prevent objects from flying off into space during long latency spikes.

4. **Interpolation mode option** - For kinematic objects, offer lerp-based interpolation (SmoothSync-style) as an alternative to spring physics. Springs are great for physics objects but can feel "bouncy" on UI elements, platforms, and other rigid kinematic objects.

5. **Clock drift correction** - Our current approach doesn't track owner time. Adding estimated owner time with gradual correction would improve sync accuracy for objects moving at consistent speeds.

### Recommendation: Hybrid Approach

```
NetworkTransform Mode Selection:
├── Has Rigidbody → Spring Physics (current, preferred)
│   └── Uses damped spring forces, physics-integrated
└── No Rigidbody (Kinematic) → Lerp + Extrapolation (SmoothSync-style)
    ├── Buffer recent states with timestamps
    ├── Interpolate between past states (100ms behind)
    ├── Extrapolate with estimated velocity when buffer runs out
    ├── Ease toward target with configurable lerp speed
    └── Rest detection to stop syncing idle objects
```

This gives us:
- **Spring sync** for physics objects (our strength - feels physically correct)
- **Lerp interpolation** for kinematic objects (SmoothSync's strength - no bouncing)
- **Extrapolation** as fallback for both (velocity-based prediction)
- **Rest detection** for bandwidth savings on both
