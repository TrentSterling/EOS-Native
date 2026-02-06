# Voice Chat

Built-in voice communication using EOS Real-Time Communication (RTC).

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
