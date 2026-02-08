# Voice Chat

Built-in voice communication using EOS Real-Time Communication (RTC).

> For spatial/3D voice with distance-based falloff, team isolation, and trigger zones, see [Spatial Voice](voice-zones.md).

## Overview

Voice chat is lobby-based. When players join a lobby with voice enabled, the RTC channel connects automatically. Voice persists through host migration.

## Basic Usage

```csharp
var voice = EOSVoiceManager.Instance;

// Mute/unmute yourself
voice.SetMuted(true);
voice.SetMuted(false);
voice.ToggleMute();

// Check mute state
bool isMuted = voice.IsMuted;
bool connected = voice.IsConnected;
```

## Audio Device Selection

Switch microphones and speakers at runtime:

```csharp
var voice = EOSVoiceManager.Instance;

// Query available devices (call once at start or on refresh)
voice.QueryAudioDevices();

// Get device lists
var inputs = voice.InputDevices;    // List of microphones
var outputs = voice.OutputDevices;  // List of speakers

// Switch devices by ID
voice.SetInputDevice(inputs[1].DeviceId);
voice.SetOutputDevice(outputs[0].DeviceId);

// Current device tracking
string currentMic = voice.CurrentInputDeviceId;
string currentSpeaker = voice.CurrentOutputDeviceId;

// Listen for device hotplug (e.g. headset plugged in)
voice.OnAudioDevicesChanged += () =>
{
    // Re-query and update UI
    voice.QueryAudioDevices();
};
```

The F1 overlay Voice tab includes dropdown selectors for input/output devices with a Refresh button.

## Per-Player Controls

### Volume Control

```csharp
// Set participant volume
voice.SetParticipantVolume(puid, 50f);  // 50% volume

// Mute a specific participant
voice.SetParticipantMuted(puid, true);
```

## Audio Levels

Monitor voice activity:

```csharp
// Check if player is currently speaking
bool speaking = voice.IsSpeaking(puid);

// Get all currently speaking players
var speakers = voice.GetSpeakingParticipants();

// Get all participants
var participants = voice.GetAllParticipants();
int count = voice.ParticipantCount;
```

## Raw Audio Access

For custom audio processing or playback:

```csharp
// Get raw audio frames (48kHz mono int16)
if (voice.TryGetAudioFrames(puid, out short[] frames))
{
    // Process frames...
}

// Check queued frames
int queued = voice.GetQueuedFrameCount(puid);

// Clear buffer
voice.ClearAudioBuffer(puid);
```

> **Warning:** `OnAudioFrameReceived` fires from the audio thread, not the main thread.

## Events

```csharp
voice.OnVoiceConnectionChanged += (isConnected) => { };
voice.OnParticipantSpeaking += (puid, isSpeaking) => { };
voice.OnAudioFrameReceived += (puid, frames) => { };  // Audio thread!
voice.OnParticipantAudioStatusChanged += (puid, status) => { };
voice.OnAudioDevicesChanged += () => { };  // Device hotplug
```

## Local Microphone Level

Monitor the local player's mic level for UI meters:

```csharp
var voice = EOSVoiceManager.Instance;

// Real-time mic level (0.0 - 1.0)
float level = voice.LocalMicLevel;
```

The mic level is calculated from the RMS of 256 audio samples, scaled by 8x for visual responsiveness. Capture starts automatically when voice is connected and unmuted, and stops on disconnect or mute.

Both the F1 overlay and Canvas UI use this for their mic level bars.

> **Android note:** `LocalMicLevel` always returns 0 on Android. The Unity `Microphone` API is disabled on Android to avoid conflicting with the EOS SDK's own `AudioRecord` capture. See [Android Voice Notes](#android-voice-notes) for details.

## Voice Diagnostics

Properties for diagnosing voice issues, especially on Android:

```csharp
var voice = EOSVoiceManager.Instance;

// Local user's audio status from the SDK
RTCAudioStatus status = voice.LocalAudioStatus;
// Possible values: Disabled, Enabled, Unsupported, NotSupported

// Result of the last mute/unmute call
Result result = voice.LastUpdateSendingResult;

// Whether device enumeration has completed
bool queried = voice.AudioDevicesQueried;
```

**`LocalAudioStatus` values:**
- `Enabled` -- Audio pipeline is working. Mic is active.
- `Disabled` -- Audio is available but currently muted.
- `Unsupported` (value 0) -- No audio devices found, or the audio pipeline did not initialize. This is the default integer value, so it also appears when the SDK has not reported any status yet.

**Auto-unmute:** When the RTC room connects, `LogVoiceDiagnostics()` is called automatically. It attempts to unmute the local player and logs the SDK result. If `UpdateSending` returns an error, check `LastUpdateSendingResult` for the specific failure code.

**F1 overlay:** The Voice tab includes a "Voice Diagnostics" foldout showing RTC/RTCAudio interface availability, `LocalAudioStatus`, `LastUpdateSendingResult`, and device counts.

## Manual Audio Playback

By default, the EOS SDK auto-plays received voice through the system audio device. For spatial 3D voice, you need manual mode so that `EOSVoicePlayer` components render audio through Unity `AudioSource` objects positioned in the scene.

```csharp
// Set BEFORE creating or joining a lobby
EOSVoiceManager.Instance.UseManualAudioOutput = true;
```

| Value | Behavior |
|-------|----------|
| `false` (default) | SDK handles playback automatically. No `EOSVoicePlayer` needed. |
| `true` | SDK delivers frames via `OnAudioFrameReceived`. Requires `EOSVoicePlayer` or `NetworkVoicePlayer` with `AudioSource` for playback. |

Both the lobby create and join paths read this property, ensuring consistent behavior regardless of who creates the lobby.

See [Spatial Voice](voice-zones.md) for the full setup guide with `NetworkVoicePlayer` and `EOSVoiceZoneManager`.

## Android Voice Notes

Voice chat works on Android, but there are platform-specific considerations.

### AudioRecord Conflict

The EOS SDK opens its own `AudioRecord` for voice capture on Android. Unity's `Microphone` API also opens an `AudioRecord`. On Android versions before 10, only one `AudioRecord` can exist at a time. On Android 10+, concurrent capture has priority rules that may silence one client.

To avoid this conflict, `EOSVoiceManager` disables the Unity `Microphone` capture on Android entirely. As a result, `LocalMicLevel` always returns 0 on Android. The mic level bar in the UI will not animate, but EOS voice transmission works correctly.

### Runtime Permission

The `RECORD_AUDIO` permission must be both declared in AndroidManifest.xml **and** requested at runtime on API 23+. The manifest declaration alone is not sufficient.

`EOSAndroidBuildProcessor` auto-injects the manifest declaration. `EOSManager.Awake()` calls `RequestMicrophonePermission()` at startup for all Android devices. No manual setup is required.

### Lobby Creation Fallback

If the EOS SDK returns an error when creating a lobby with voice enabled (e.g., RTC module not initialized on the device), `CreateLobbyAsync` automatically retries without voice. This prevents `InvalidRequest` errors on platforms where RTC is unavailable.

## Platform Support

| Platform | Voice Support |
|----------|---------------|
| Windows | Yes (requires XAudio2) |
| Mac | Yes |
| Linux | Yes |
| Android | Yes |
| iOS | Yes |

### Windows XAudio2

The EOS SDK requires `xaudio2_9redist.dll` on Windows for RTC. The path is auto-resolved from multiple locations:

1. UPM package path
2. Embedded Assets path
3. Legacy flat layout

If you see "Failed to load custom XAudio2.9 dll", verify the DLL exists in `Runtime/EOSSDK/Plugins/Windows/x64/`.

## Limits

| Limit | Value |
|-------|-------|
| Max participants | 64 per room (SDK 1.16+) |
| Audio sample rate | 48000 Hz |
| Audio channels | 1 (mono) |

## Debug

Press **F1** and switch to the **Voice** tab to see:
- RTC connection status
- Mute state
- Input/output device selection dropdowns
- Participant list with speaking indicators and level bars
- Audio status per participant

## Troubleshooting

### No Audio

1. Check microphone permissions (especially on mobile)
2. Verify voice is connected in F1 Voice tab
3. Ensure player isn't muted
4. Confirm lobby was created with `EnableVoice = true`

### Echo/Feedback

Enable echo cancellation in EOS Developer Portal settings.
