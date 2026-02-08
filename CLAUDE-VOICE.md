# CLAUDE-VOICE.md

Detailed voice/RTC system reference. See CLAUDE.md for project overview and rules.

## Audio Device Selection (Mic/Speaker)

`EOSVoiceManager` provides runtime mic/speaker device switching via EOS RTCAudio APIs:

- `QueryAudioDevices()` - Queries input/output device lists and registers for hotplug notifications. **Auto-called on voice connect** (no manual Refresh needed)
- `GetInputDevices()` / `GetOutputDevices()` - Returns cached device info lists
- `SetInputDevice(deviceId)` - Switches active microphone by `RealDeviceId`
- `SetOutputDevice(deviceId)` - Switches active speaker by `RealDeviceId`
- `OnAudioDevicesChanged` event - Fires when devices are added/removed
- `CurrentInputDeviceId` / `CurrentOutputDeviceId` - Track selected device
- `LocalMicLevel` (float, 0-1) - Real-time mic level via Unity `Microphone` API (RMS of 256 samples, scaled 8x). Starts capture when voice is connected and unmuted, stops on disconnect/mute. Used by both OnGUI and Canvas UI level bars.
- `LocalAudioStatus` (RTCAudioStatus) - Local user's audio status from SDK. `Unsupported` (0) means no audio devices / pipeline not initialized.
- `LastUpdateSendingResult` (Result) - Result of last mute/unmute call. Useful for diagnosing SDK rejecting audio changes.
- `AudioDevicesQueried` (bool) - Whether device enumeration has completed at least once.

The F1 overlay Voice tab exposes dropdown selectors for input/output devices with a Refresh button. The Canvas UI Voice tab also has an Audio Devices section with Refresh button, input/output device selection buttons (green = selected), and real-time mic level bar.

**Note:** `AudioBeforeSend` was tested for real RMS-based mic levels but causes `StackOverflowException` in `PlatformInterface.Tick()` — the EOS C# SDK queues audio frame callbacks and overflows processing them. `IsSpeaking()` proxy was also tested but unreliable when alone in a lobby (EOS VAD may not trigger). Unity `Microphone` API is the reliable solution.

## Spatial Voice System (Voice Zones + NetworkVoicePlayer)

Three components for immersive spatial voice chat. All files in `Runtime/EOSNative/Voice/`.

### EOSVoiceZoneManager

Singleton that controls WHO you hear and at WHAT volume. Works alongside `EOSVoiceManager` to adjust per-participant volumes dynamically.

**5 Voice Zone Modes:**

| Mode | Behavior |
|------|----------|
| Global | Everyone hears everyone at full volume |
| Proximity | Distance-based falloff (configurable exponent, fade start/end) |
| Team | Same team = full volume, cross-team = muted or reduced |
| TeamProximity | Team filter + distance falloff combined |
| Custom | Zone-name matching (via trigger volumes or API) |

**Volume Calculation:**
- Proximity: `t = pow((dist - fadeStart) / (max - fadeStart), exponent)`, lerp(maxVol, minVol, t)
- Team: sameTeam → maxVol, crossTeam → maxVol * crossMultiplier (or 0)
- Custom: sameZone → maxVol, differentZone → 0

**Volume Ducking:** Opt-in feature that auto-reduces incoming voice when local player is speaking. `MoveTowards` fade with configurable multiplier and speed.

**Auto-discover:** Scans `NetworkManager.Instance.Objects` for tagged objects, registers transforms by PUID.

**Update loop:** Every `_updateInterval` (0.1s), iterate participants, calculate volume, call `EOSVoiceManager.SetParticipantVolume()` if change > threshold.

```csharp
// Set mode
EOSVoiceZoneManager.Instance.SetZoneMode(VoiceZoneMode.Proximity);
EOSVoiceZoneManager.Instance.ConfigureProximity(maxDistance: 30f, fadeStart: 10f);

// Team mode
EOSVoiceZoneManager.Instance.SetZoneMode(VoiceZoneMode.Team);
EOSVoiceZoneManager.Instance.SetTeam(1);
EOSVoiceZoneManager.Instance.SetPlayerTeam(remotePuid, 2);

// Events
EOSVoiceZoneManager.Instance.OnPlayerEnteredRange += puid => Debug.Log($"{puid} in range");
EOSVoiceZoneManager.Instance.OnPlayerExitedRange += puid => Debug.Log($"{puid} out of range");
```

### EOSVoiceTriggerZone

Collider-based trigger volumes for Custom zone mode. Attach to a GameObject with a trigger Collider.

- `OnTriggerEnter`: Detects player via `NetworkObject`, calls `SetLocalZone()` or `SetPlayerZone()`
- `OnTriggerExit`: Resets to default zone
- Editor gizmo visualization (Box, Sphere, Capsule)
- Tag-based player filtering

### NetworkVoicePlayer

`NetworkBehaviour` wrapper that auto-wires `EOSVoicePlayer` to the correct participant based on `NetworkObject` ownership.

**Usage:** Add to a player prefab alongside an `AudioSource`. Everything is automatic:
- Local player: disables voice playback (don't hear yourself), registers with zone manager
- Remote player: sets participant PUID, applies 3D audio settings, creates speaking indicator

**Speaking Indicator:** Animated blue sphere above the player's head that appears when speaking. Uses `SmoothDamp` for bounce-ease animation, subtle sine pulse while talking, fades out when silent. Configurable color, height, and size via Inspector.

```csharp
// Just add these components to your player prefab:
// - AudioSource
// - NetworkVoicePlayer (auto-adds EOSVoicePlayer + NetworkObject)
// Everything else is automatic.
```
