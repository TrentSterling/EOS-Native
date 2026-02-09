# Spatial Voice

Three components for immersive spatial voice chat. Distance-based falloff, team isolation, zone triggers, and 3D audio. All files in `Runtime/EOSNative/Voice/`.

> **Prerequisite:** `UseManualAudioOutput` must be set to `true` before creating or joining a lobby when using spatial audio with `EOSVoicePlayer` / `NetworkVoicePlayer`. See [Manual Audio Playback](voice.md#manual-audio-playback) for details.

## EOSVoiceZoneManager

Singleton that controls who you hear and at what volume. Works alongside `EOSVoiceManager` to adjust per-participant volumes dynamically every 0.1 seconds.

### Voice Zone Modes

| Mode | Behavior |
|------|----------|
| Global | Everyone hears everyone at full volume |
| Proximity | Distance-based falloff with configurable exponent, fade start, and max distance |
| Team | Same team = full volume, cross-team = muted or reduced |
| TeamProximity | Team filter combined with distance falloff |
| Custom | Zone-name matching via trigger volumes or API |

### Configuration

#### Proximity Mode

```csharp
var zones = EOSVoiceZoneManager.Instance;

zones.SetZoneMode(VoiceZoneMode.Proximity);
zones.ConfigureProximity(
    maxDistance: 30f,    // Beyond this = silent
    fadeStart: 10f,     // Full volume inside this range
    minVol: 0f,         // Volume at max distance (0-100)
    maxVol: 100f        // Volume inside fade start (0-100)
);
```

#### Team Mode

```csharp
var zones = EOSVoiceZoneManager.Instance;

zones.SetZoneMode(VoiceZoneMode.Team);
zones.SetTeam(1);  // Local player's team

// Set remote player teams as you learn them
zones.SetPlayerTeam(remotePuid, 2);

// Optional: allow cross-team audio at reduced volume
zones.ConfigureTeam(allowCrossTeam: true, crossTeamMultiplier: 0.25f);
```

#### TeamProximity Mode

Combines both team and distance checks. Cross-team players are muted regardless of distance. Same-team players fade with distance.

```csharp
var zones = EOSVoiceZoneManager.Instance;

zones.SetZoneMode(VoiceZoneMode.TeamProximity);
zones.SetTeam(1);
zones.ConfigureProximity(maxDistance: 30f, fadeStart: 10f);
zones.ConfigureTeam(allowCrossTeam: false);
```

#### Custom Zone Mode

Zone-name matching. Players in the same zone hear each other; players in different zones are muted. Typically used with `EOSVoiceTriggerZone` colliders (see below).

```csharp
var zones = EOSVoiceZoneManager.Instance;

zones.SetZoneMode(VoiceZoneMode.Custom);
zones.SetLocalZone("building_A");
zones.SetPlayerZone(remotePuid, "building_B");
// These two players cannot hear each other
```

### Volume Calculation

Each mode uses a different formula to compute the effective volume (0-100) for each remote participant.

**Proximity:**

```
if distance <= fadeStart:  volume = maxVol
if distance >= maxDistance: volume = minVol
else:
    t = pow((distance - fadeStart) / (maxDistance - fadeStart), exponent)
    volume = lerp(maxVol, minVol, t)
```

The `falloffExponent` (default 1.0) controls the curve shape. 1.0 is linear, 2.0 is quadratic (sounds more natural), 0.5 is square root.

**Team:**

```
if sameTeam:    volume = maxVol
if crossTeam:   volume = maxVol * crossTeamMultiplier  (or 0 if cross-team disabled)
```

**Custom:**

```
if sameZone:      volume = maxVol
if differentZone: volume = 0
```

### Volume Ducking

Auto-reduces incoming voice volume when the local player is speaking. Useful for preventing audio overlap in intense situations.

```csharp
var zones = EOSVoiceZoneManager.Instance;

zones.EnableVolumeDucking = true;
// Volume multiplier while local player is speaking (0-1)
// 0.5 = incoming voices drop to half volume while you talk
```

The ducking factor fades in and out smoothly using `MoveTowards` at the configured `_duckingSpeed` (default 5 units/sec). Set in the Inspector or via code.

### Audio Occlusion

Raycast-based wall muting. When enabled, a `Physics.Raycast` is cast from the local player to each voice participant. If a wall blocks line of sight, the participant's volume is reduced.

```csharp
var zones = EOSVoiceZoneManager.Instance;

zones.EnableAudioOcclusion = true;
zones.OcclusionLayerMask = LayerMask.GetMask("Walls", "Obstacles");
zones.OcclusionVolumeMultiplier = 0.15f; // 85% reduction behind walls
```

Works with all voice zone modes. The occlusion multiplier stacks with proximity falloff and ducking.

| Setting | Default | Description |
|---------|---------|-------------|
| EnableAudioOcclusion | false | Enable raycast wall muting |
| OcclusionLayerMask | Everything | Which layers block voice |
| OcclusionVolumeMultiplier | 0.15 | Volume when occluded (0 = silent) |
| OcclusionRayHeight | 1.5 | Height offset for raycasts (head level) |

**Query:** `zones.IsPlayerOccluded(puid)` returns whether a player is currently behind a wall.

**Performance:** One raycast per participant per update cycle (~10/sec). Negligible cost for up to 16 players.

### Spatial Hash Grid (100+ Players)

For large player counts (100+), the brute-force proximity check becomes expensive. Enable `UseSpatialGrid` to use a 2D spatial hash grid for O(N) lookups instead of O(N^2):

```csharp
var zones = EOSVoiceZoneManager.Instance;

zones.UseSpatialGrid = true;
zones.GridCellSize = 15f; // roughly maxHearingDistance / 2
```

When enabled (in Proximity or TeamProximity mode):
- All player positions are hashed into grid cells each update cycle
- Only players in the local player's cell + 8 neighbors are processed for volume calculation
- Far-away players are immediately set to minimum volume without distance checks
- Grid is automatically cleaned up on UnregisterPlayer/ClearAllPlayers

| Setting | Default | Description |
|---------|---------|-------------|
| UseSpatialGrid | false | Enable grid-based proximity |
| GridCellSize | 15 | Cell size in world units (use maxHearingDistance / 2) |

The grid operates on the XZ plane (same as the networking `SpatialHashGrid`). Set `GridCellSize` to roughly half your `MaxHearingDistance` for optimal cell granularity.

### Voice Priority (Bandwidth Management)

Limit simultaneous voice streams when player count exceeds what the audio pipeline can handle:

```csharp
var zones = EOSVoiceZoneManager.Instance;

zones.MaxActiveVoiceStreams = 16; // 0 = unlimited (default)
```

When the number of audible participants exceeds `MaxActiveVoiceStreams`, the lowest-priority players are muted. Priority scoring:

| Factor | Score | Description |
|--------|-------|-------------|
| Speaking | +1000 | Currently transmitting voice |
| Proximity | 0 to maxDistance | Closer = higher priority |
| Same team | +100 | Team/TeamProximity modes only |

Players already at minimum volume (out of range) don't count toward the limit.

**Query:** `zones.GetPlayerPriority(puid)` returns the current priority score for debugging (-1 if not scored).

### Auto-Discover

When `_autoDiscoverNetworkObjects` is enabled (default), the zone manager automatically scans `NetworkManager.Instance.Objects` for GameObjects tagged with the configured `_playerTag` (default `"Player"`). It registers their transforms by PUID for position tracking.

This runs every update cycle, so newly spawned players are picked up automatically. You can also register players manually:

```csharp
var zones = EOSVoiceZoneManager.Instance;

// Manual registration
zones.RegisterLocalPlayer(localPlayerTransform);
zones.RegisterPlayer(remotePuid, remotePlayerTransform);

// Cleanup
zones.UnregisterPlayer(remotePuid);
zones.ClearAllPlayers();
```

### Events

```csharp
var zones = EOSVoiceZoneManager.Instance;

zones.OnZoneModeChanged += (mode) => Debug.Log($"Mode: {mode}");
zones.OnPlayerVolumeChanged += (puid, volume) => Debug.Log($"{puid}: {volume}%");
zones.OnPlayerEnteredRange += (puid) => Debug.Log($"{puid} in range");
zones.OnPlayerExitedRange += (puid) => Debug.Log($"{puid} out of range");
```

### Queries

```csharp
var zones = EOSVoiceZoneManager.Instance;

float volume = zones.GetPlayerVolume(puid);       // Current effective volume
float distance = zones.GetDistanceToPlayer(puid); // Distance in units, -1 if unknown
bool inRange = zones.IsPlayerInRange(puid);        // In hearing range?
List<string> nearby = zones.GetPlayersInRange();   // All players in range
```

## EOSVoiceTriggerZone

Collider-based trigger volumes for Custom zone mode. Attach to a GameObject with a trigger Collider to define voice isolation areas.

### Setup

1. Create a GameObject in your scene
2. Add a Collider component (Box, Sphere, or Capsule) and set **Is Trigger** to `true`
3. Add the `EOSVoiceTriggerZone` component
4. Set the **Zone Name** (players in the same zone hear each other)
5. Set `EOSVoiceZoneManager` to Custom mode

```csharp
// The zone manager must be in Custom mode for trigger zones to work
EOSVoiceZoneManager.Instance.SetZoneMode(VoiceZoneMode.Custom);
```

### How It Works

- **OnTriggerEnter:** Detects the player via `NetworkObject` on the collider. For the local player, calls `SetLocalZone()`. For remote players, calls `SetPlayerZone()`.
- **OnTriggerExit:** Resets the player to the default zone. If an `EOSVoiceTriggerZone` has `_isDefaultZone` set to `true`, that zone name is used as the default. Otherwise, the default is `"default"`.

### Tag-Based Filtering

The `_playerTag` field (default `"Player"`) filters which GameObjects are treated as players. Only colliders with a matching tag are processed. Set to empty to accept all colliders.

### Default Zone

Mark one `EOSVoiceTriggerZone` as the default zone (`_isDefaultZone = true`). When a player exits a trigger and is not inside another one, they return to this default zone. If no default zone exists, they go to `"default"`.

### Editor Gizmos

Each trigger zone draws a colored gizmo in the Scene view matching the collider shape (Box, Sphere, or Capsule). The zone name label appears above the object when selected. Customize the gizmo color via the Inspector.

### Inspector

The custom Inspector shows a help box explaining the setup flow and warns if the Collider is missing or not set to trigger mode. A button is provided to fix the trigger setting.

## NetworkVoicePlayer

`NetworkBehaviour` wrapper that auto-wires `EOSVoicePlayer` to the correct participant based on `NetworkObject` ownership. Add this to a player prefab alongside an `AudioSource` and everything is automatic.

### Usage

```csharp
// Add these components to your player prefab:
// - AudioSource
// - NetworkVoicePlayer (auto-adds EOSVoicePlayer if missing)
// Everything else is automatic.
```

### Local vs Remote Behavior

**Local player (IsOwner = true):**
- Voice playback is disabled (you do not hear your own voice)
- `EOSVoicePlayer` and `AudioSource` are disabled
- Registers with `EOSVoiceZoneManager` as the local player for position tracking

**Remote player (IsOwner = false):**
- Participant PUID is set from `NetworkObject.OwnerId`
- 3D audio settings are applied to the `AudioSource`
- Registers with `EOSVoiceZoneManager` for position tracking
- Speaking indicator is created (if enabled)

### 3D Audio Settings

Configure spatial audio in the Inspector:

| Setting | Default | Description |
|---------|---------|-------------|
| Spatial Blend | 1.0 | 0 = 2D, 1 = full 3D spatial audio |
| Doppler Level | 1.0 | 0 = off, higher = more exaggerated |
| Min Distance | 1.0 | Distance before volume attenuates |
| Max Distance | 50.0 | Distance where sound is inaudible |
| Rolloff Mode | Logarithmic | Linear or Logarithmic attenuation |

### Voice Effects

`NetworkVoicePlayer` exposes voice effects from the underlying `EOSVoicePlayer`:

```csharp
var voicePlayer = GetComponent<NetworkVoicePlayer>();

// Reverb (environmental audio effect)
voicePlayer.EnableReverb = true;
voicePlayer.ReverbPreset = AudioReverbPreset.Cave;

// Pitch shifting (voice disguise, effects)
voicePlayer.EnablePitchShift = true;
voicePlayer.PitchShift = 0.8f;  // Lower pitch
```

### Speaking Indicator

An animated sphere appears above remote players when they are speaking. Enabled by default.

**Behavior:**
- Appears with a bounce-ease animation (`SmoothDamp`) when the participant starts speaking
- Subtle sine pulse while actively talking
- Stays visible for 0.5 seconds after speech stops, then fades out
- Alpha fades based on current scale

**Inspector settings:**

| Setting | Default | Description |
|---------|---------|-------------|
| Show Speaking Indicator | true | Enable/disable the indicator |
| Indicator Color | Blue (0.2, 0.6, 1.0, 0.8) | Color of the sphere |
| Indicator Height | 2.2 | Offset above the player position |
| Indicator Base Size | 0.15 | Minimum visible size |
| Indicator Max Size | 0.35 | Size when actively speaking |

### Manual Participant Assignment

If you need to set the participant PUID from a source other than `NetworkObject.OwnerId`:

```csharp
var voicePlayer = GetComponent<NetworkVoicePlayer>();
voicePlayer.SetParticipantPuid("some-puid-string");
```

## UseManualAudioOutput

The `UseManualAudioOutput` property on `EOSVoiceManager` controls whether the EOS SDK auto-plays received voice audio or delivers frames for manual playback via `EOSVoicePlayer`.

| Value | Behavior | Use Case |
|-------|----------|----------|
| `false` (default) | SDK auto-plays voice through the system audio device | Standard voice chat, no spatial audio needed |
| `true` | SDK does NOT auto-play. Frames delivered via `OnAudioFrameReceived` only | Spatial 3D voice with `EOSVoicePlayer` / `NetworkVoicePlayer` |

### When to Use Manual Mode

Set `UseManualAudioOutput = true` when your player prefabs have `NetworkVoicePlayer` (or `EOSVoicePlayer` with an `AudioSource`) and you want voice to come from the player's position in 3D space.

Leave it at `false` (default) for standard lobby voice chat where spatial positioning is not needed.

### Setting It

Set this **before** creating or joining a lobby. Both `CreateLobbyAsync` and `JoinLobbyByIdAsync` read this value and pass it to `LocalRTCOptions.UseManualAudioOutput` in the EOS SDK.

```csharp
// Enable manual audio for spatial voice
EOSVoiceManager.Instance.UseManualAudioOutput = true;

// Then create or join a lobby with voice enabled
await EOSLobbyManager.Instance.CreateLobbyAsync(new CreateLobbyOptions
{
    EnableVoice = true,
    MaxPlayers = 8
});
```

### Consistency Between Create and Join

Both the lobby create path and the lobby join path set `LocalRTCOptions.UseManualAudioOutput` from `EOSVoiceManager.Instance.UseManualAudioOutput`. This ensures consistent audio behavior regardless of whether you are the lobby creator or a joiner.

> **History:** Previously, `UseManualAudioOutput` was hardcoded to `true` on lobby creation and not set on join. This caused the lobby creator to hear nothing (no `EOSVoicePlayer` rendering the audio) while the joiner got SDK auto-play. The fix was making both paths read the same configurable property.

## Full Example

Putting it all together with proximity voice:

```csharp
// 1. Enable manual audio output for spatial voice
EOSVoiceManager.Instance.UseManualAudioOutput = true;

// 2. Create a voice lobby
await EOSLobbyManager.Instance.CreateLobbyAsync(new CreateLobbyOptions
{
    EnableVoice = true,
    MaxPlayers = 8
});

// 3. Configure proximity voice
var zones = EOSVoiceZoneManager.Instance;
zones.SetZoneMode(VoiceZoneMode.Proximity);
zones.ConfigureProximity(maxDistance: 30f, fadeStart: 10f);
zones.EnableVolumeDucking = true;

// 4. Player prefab has: NetworkObject + NetworkVoicePlayer + AudioSource
//    Everything else is automatic.
```
