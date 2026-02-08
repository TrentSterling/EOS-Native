# Android Build Guide

Building Unity projects with the EOS SDK for Android requires Gradle configuration, Java classloader setup, and platform-specific initialization. EOS-Native handles all of this automatically.

## It Just Works

As of v2.4.2, EOS-Native includes `EOSAndroidBuildProcessor` which automatically configures your Android build. **No manual Gradle editing is required.**

The build processor runs via `IPostGenerateGradleAndroidProject` and handles six things:

1. Core library desugaring (required by the EOS AAR)
2. AndroidX dependencies (transitive Maven deps Unity doesn't resolve)
3. Native library extraction (`extractNativeLibs` in AndroidManifest)
4. EOS login protocol scheme string resource
5. Java init helper (`EOSNativeLoader.java` for VOIP classloader fix)
6. ProGuard keep rules (prevent R8 from stripping JNI classes)

You should see these log messages during Android builds:

```
[EOS-Native] Enabled core library desugaring in launcher/build.gradle
[EOS-Native] Enabled core library desugaring in unityLibrary/build.gradle
[EOS-Native] Injected eos_login_protocol_scheme: eos.your_client_id_here
[EOS-Native] Generated EOSNativeLoader.java
[EOS-Native] Generated proguard-eos.pro
```

If you see those, everything is configured correctly.

---

## Prerequisites

- **EOSConfig asset** -- Create one via Assets > Create > EOS Native > Config and fill in your Client ID. The build processor reads this to generate the login scheme. Without it, a placeholder is used (build succeeds but OAuth login won't redirect).
- **Unity 2021.3+** -- The build processor uses `IPostGenerateGradleAndroidProject` which is available in all supported Unity versions.
- **Android Build Support** -- Install via Unity Hub > Installs > Add Modules.
- **Minimum API 23+** -- Required for runtime permission requests.
- **IL2CPP scripting backend** -- Recommended for production builds.

---

## Java Classloader Fix (Critical for VOIP)

This is the most important section for understanding Android voice chat. Without this fix, VOIP silently fails on Android.

### The Problem

The EOS native library (`libEOSSDK.so`) contains a `JNI_OnLoad` function that registers native methods for Java classes like `com.epicgames.mobile.eossdk.EOSLogger`. Per the [Android JNI specification](https://developer.android.com/training/articles/perf-jni), `FindClass` inside `JNI_OnLoad` uses the **classloader of the caller** -- the class whose code called `System.loadLibrary`.

Without the fix, the native library gets loaded by C# P/Invoke's `dlopen` (no Java frame on the call stack). When `JNI_OnLoad` runs, it has no Java caller, so `FindClass` falls back to the **system classloader**. The system classloader cannot find app-level classes like `EOSLogger`. `FindClass` fails, `RegisterNatives` never runs, and the RTC/Audio subsystems silently fail to initialize.

`EOSSDK.init(activity)` does NOT call `System.loadLibrary("EOSSDK")`. It only initializes Java-side state. The native library load and the Java init are separate steps, and both must happen correctly.

### The Fix

`EOSAndroidBuildProcessor` generates `EOSNativeLoader.java` at build time. This Java class calls `System.loadLibrary("EOSSDK")` from within APK code compiled into the dex:

```java
public class EOSNativeLoader {
    public static void initEOS(android.app.Activity activity) {
        System.loadLibrary("EOSSDK");       // Java caller = app classloader
        com.epicgames.mobile.eossdk.EOSSDK.init(activity);
    }
}
```

Since `EOSNativeLoader` is compiled into the APK's dex (loaded by the app classloader), `FindClass("com/epicgames/mobile/eossdk/EOSLogger")` succeeds inside `JNI_OnLoad`. `RegisterNatives` runs. RTC/Audio subsystems initialize correctly.

**`System.loadLibrary("EOSSDK")` MUST be called from Java code, not from C# via `AndroidJavaClass`.** The entire point is that the Java caller's classloader is used by `JNI_OnLoad`. Calling it from C# JNI bridge code results in the wrong classloader.

### Why Setting Thread Context Classloader Doesn't Work

Setting the thread's context classloader via `Thread.currentThread().setContextClassLoader(...)` from C# does not help. `FindClass` in `JNI_OnLoad` uses the call-stack classloader (walking the stack for a Java frame), not the thread context classloader. There must be a real Java frame on the call stack.

### Fallback Behavior

If the `EOSNativeLoader` class is not found at runtime (for example, in builds made before the build processor was added), `EOSManager` falls back to calling `EOSSDK.init(activity)` directly from C# with a warning log:

```
[EOS] EOSNativeLoader not found — falling back to direct EOSSDK.init(). Voice may not work.
```

In this fallback path, `System.loadLibrary` is never called from Java, so `JNI_OnLoad` uses the system classloader and voice will not function.

---

## Initialization Flow

Android initialization follows a specific sequence, starting at the earliest possible Unity callback.

### Step-by-Step

1. **`[SubsystemRegistration]` -- `EarlyAndroidInit()`** runs before any MonoBehaviour. This is the earliest timing available in Unity.
2. **C# calls `EOSNativeLoader.initEOS(activity)`** via `AndroidJavaClass`. The Activity reference is obtained from `UnityPlayer.currentActivity`.
3. **`EOSNativeLoader.initEOS()` calls `System.loadLibrary("EOSSDK")`** from Java code. The JVM loads `libEOSSDK.so` and calls `JNI_OnLoad` with the app classloader active.
4. **`JNI_OnLoad` runs `FindClass` and `RegisterNatives`** for EOS Java classes (EOSLogger, RTC helpers, etc.). This succeeds because the app classloader is used.
5. **`EOSNativeLoader.initEOS()` calls `EOSSDK.init(activity)`** to initialize Java-side EOS state.
6. **`EOSManager.Start()` runs `Initialize()`** which calls `PlatformInterface.Initialize()`. This is the first P/Invoke into the native library, which is safe because the library was already loaded in step 3.

### AndroidInitializeOptions

On Android, `PlatformInterface.Initialize()` **must** use `AndroidInitializeOptions`, not the generic `InitializeOptions`. The Android-specific struct includes:

- A `Reserved` field set to `{1, 1}` (required by the SDK internals)
- `SystemInitializeOptions` containing Android-specific file paths

Using the generic `InitializeOptions` on Android causes RTC/Audio subsystems to silently not initialize. The SDK generates both overloads -- the Android-specific one is in `Source/Generated/Android/Platform/PlatformInterface.cs`.

---

## RTC/Voice Prerequisites

For voice chat to work on Android, **all four** of these conditions must be met. Missing any one of them causes voice to silently fail.

1. **`RTCOptions` must be set on platform creation.** The `Options` struct passed to `PlatformInterface.Create()` must include `RTCOptions = new RTCOptions()`. Setting it to `null` (the default) tells the SDK to skip RTC initialization entirely. This was the primary cause of RTC/Audio interfaces showing as unavailable on Android.

2. **`RECORD_AUDIO` permission must be in AndroidManifest AND requested at runtime.** `EOSAndroidBuildProcessor` auto-injects the manifest declaration. `EOSManager.Awake()` calls `EOSPlatformHelper.RequestMicrophonePermission()` for all Android devices. On API 23+, the manifest declaration alone is insufficient -- the app must call `Permission.RequestUserPermission("android.permission.RECORD_AUDIO")` at runtime.

3. **`System.loadLibrary("EOSSDK")` must be called from Java code.** `EOSNativeLoader.java` (generated by the build processor) handles this. Without it, `JNI_OnLoad` uses the system classloader and `RegisterNatives` fails for RTC/Audio classes. See the [Java Classloader Fix](#java-classloader-fix-critical-for-voip) section above.

4. **Unity `Microphone.Start()` must NOT run on Android.** The EOS SDK opens its own `AudioRecord` for voice transmission. On Android versions below 10, only one `AudioRecord` can exist at a time. On Android 10+, concurrent audio capture has priority rules that may silence one client. `EOSVoiceManager.StartMicCapture()` is disabled on Android to avoid this conflict. The mic level bar in the UI shows 0% on Android, but EOS voice works correctly through its own audio pipeline.

---

## Build Processor Details

`EOSAndroidBuildProcessor.cs` (in `EOSNative.Editor/`) implements `IPostGenerateGradleAndroidProject` with callback order 99 (runs late to avoid conflicts with other processors).

### Core Library Desugaring

The EOS SDK AAR (`eossdk-StaticSTDC-release.aar`) uses Java 8+ APIs that aren't available on older Android versions. The processor injects desugaring config into both `launcher/build.gradle` and `unityLibrary/build.gradle`:

```gradle
compileOptions {
    coreLibraryDesugaringEnabled true
    sourceCompatibility JavaVersion.VERSION_17
    targetCompatibility JavaVersion.VERSION_17
}

dependencies {
    coreLibraryDesugaring 'com.android.tools:desugar_jdk_libs:2.1.4'
}
```

### AndroidX Dependencies

Unity does not resolve the EOS AAR's transitive Maven dependencies. The processor injects them explicitly:

| Dependency | Version | Why |
|------------|---------|-----|
| `androidx.appcompat:appcompat` | 1.5.1 | Core Android compatibility library |
| `androidx.constraintlayout:constraintlayout` | 2.1.4 | Layout support used by EOS UI |
| `androidx.security:security-crypto` | 1.0.0 | Encrypted shared preferences |
| `androidx.browser:browser` | 1.4.0 | Chrome Custom Tabs for account portal login |

The `browser` dependency is critical. Without it, calling `EOSSDK.init()` throws `NoClassDefFoundError: CustomTabsServiceConnection` because the EOS SDK uses Chrome Custom Tabs for Epic Account login flows.

The processor also ensures `google()` is present in the repositories block for AndroidX resolution.

### Extract Native Libraries

Injects `android:extractNativeLibs="true"` into `AndroidManifest.xml`'s `<application>` tag. This is required so native `.so` files from the EOS AAR are extracted at install time. Without it, `System.loadLibrary` may fail with `UnsatisfiedLinkError` because the OS cannot find the unextracted library.

### EOS Login Scheme

Reads the `ClientId` from your `EOSConfig` ScriptableObject and injects it into `unityLibrary/src/main/res/values/strings.xml`:

```xml
<resources>
    <string name="eos_login_protocol_scheme">eos.yourclientidhere</string>
</resources>
```

The format is `eos.{client_id_lowercase}` as required by the EOS SDK for OAuth deep-link callbacks.

### Java Init Helper

Generates `EOSNativeLoader.java` with `System.loadLibrary("EOSSDK")` called from Java classloader context. See the [Java Classloader Fix](#java-classloader-fix-critical-for-voip) section for full details.

### ProGuard Keep Rules

Generates `proguard-eos.pro` to prevent R8/ProGuard from stripping EOS SDK Java classes needed for JNI native method registration. Without these rules, release builds with minification enabled will strip the Java classes that `RegisterNatives` targets, causing voice to fail even though the classloader fix is in place.

### Permissions

The processor auto-injects two permissions into the Android manifest:

- `android.permission.RECORD_AUDIO` -- Required for voice chat microphone access
- `android.permission.ACCESS_WIFI_STATE` -- Used by EOS for network type detection

`RECORD_AUDIO` is declared in the manifest but also requires a runtime permission request on API 23+. `EOSManager.Awake()` handles the runtime request automatically.

### Idempotent Injection

The processor checks before injecting every modification. Running multiple builds will not duplicate entries in Gradle files, manifests, or resource files.

---

## AudioRecord Conflict

The EOS SDK manages its own `AudioRecord` instance for voice transmission on Android. This creates a conflict with Unity's `Microphone` API.

### Android < 10

Only one `AudioRecord` can exist at a time. If Unity's `Microphone.Start()` opens an `AudioRecord` first, the EOS SDK's attempt to open one will fail (or vice versa). Whichever opens second loses.

### Android 10+

Android 10 introduced concurrent audio capture, but it follows priority rules. The system may silently mute the lower-priority capture client. The result is unpredictable -- sometimes Unity's capture works and EOS voice is silent, sometimes the reverse.

### How EOS-Native Handles This

`EOSVoiceManager.StartMicCapture()` is disabled on Android. The voice manager skips all `Microphone.Start()` calls when running on Android. This means:

- The mic level bar in the F1 overlay and Canvas UI shows 0% on Android (by design)
- EOS voice chat works correctly through the SDK's own audio pipeline
- There is no way to show a local mic level indicator on Android without risking the AudioRecord conflict

Voice audio is still transmitted and received normally. The only missing feature is the local mic level visualization.

---

## Auto-Recovery on App Resume

When Android suspends the app (screen off, task switch, home button), EOS Connect auth tokens expire after a few minutes. On resume, the SDK is still initialized (platform exists, interfaces are valid) but the user is no longer logged in. `EOSManager.TryAutoRecover()` handles this transparently.

### Recovery Flow

1. **On pause:** `OnApplicationPause(true)` caches the current `IsLoggedIn` state and `CurrentLobby.LobbyId`.
2. **On resume:** `OnApplicationPause(false)` fires. If `IsInitialized && !IsLoggedIn && _wasLoggedInBeforePause`, waits 500ms for the SDK to stabilize, then calls `LoginSmartAsync()` to re-authenticate.
3. **After login:** If a lobby ID was cached, attempts `JoinLobbyByIdAsync()` to rejoin. P2P connections and voice recover automatically via the existing handshake retry and voice auto-connect mechanisms.
4. **Auth expiration while foregrounded:** `OnLoginStatusChanged` also sets the recovery flags, so `TryAutoRecover()` fires on the next focus/pause cycle even if the app was not backgrounded.

The `_isRecovering` flag prevents double-fire since both `OnApplicationPause(false)` and `OnApplicationFocus(true)` fire on Android resume. All recovery steps are logged to the runtime console.

---

## Diagnostics

### Startup Logging

`LoadAndroidLibrary()` logs the following at startup:

- Android API level
- Device model
- Supported ABIs (e.g., `arm64-v8a`, `armeabi-v7a`)

These values help diagnose device-specific issues.

### AndroidJavaInitSuccess

The `EOSManager.AndroidJavaInitSuccess` property tracks whether the Java-side init (via `EOSNativeLoader.initEOS()`) completed without exceptions. Check this in the F1 overlay Status tab or in code:

```csharp
if (!EOSManager.Instance.AndroidJavaInitSuccess)
    Debug.LogWarning("Java init failed — voice will not work");
```

### Console Early Init

`EOSNativeConsole.Instance` is created in `EOSManager.Awake()`, before `LoadNativeLibrary()` and `LoadAndroidLibrary()` are called. This ensures that all errors during the native library loading sequence are captured in the runtime console. Without this, early Android init failures would be invisible on devices where `adb logcat` is not connected.

---

## Common Build Errors

If the automatic processor fails or you are troubleshooting a custom setup, here are the errors you will encounter.

### Error: "Dependency requires core library desugaring"

**Full error message:**
```
Execution failed for task ':launcher:checkReleaseAarMetadata'.
> A failure occurred while executing com.android.build.gradle.internal.tasks.CheckAarMetadataWorkAction
   > An issue was found when checking AAR metadata:

       1.  Dependency ':eossdk-StaticSTDC-release:' requires core library desugaring to be enabled
           for :launcher.

           See https://developer.android.com/studio/write/java8-support.html for more details.
```

**What's happening:** The EOS SDK AAR uses Java 8+ APIs that aren't available on older Android versions. The AAR's metadata declares that core library desugaring must be enabled.

**Automatic fix:** `EOSAndroidBuildProcessor` injects the desugaring config into both Gradle files. See [Core Library Desugaring](#core-library-desugaring) above.

**Manual fix (if needed):**
1. In Unity: Player Settings > Android > Publishing Settings
2. Enable **Custom Launcher Gradle Template**
3. Open the generated `Assets/Plugins/Android/launcherTemplate.gradle`
4. Add `coreLibraryDesugaringEnabled true` inside the `compileOptions { }` block
5. Add `coreLibraryDesugaring 'com.android.tools:desugar_jdk_libs:2.1.4'` inside `dependencies { }`
6. Repeat for `mainTemplate.gradle` if needed

---

### Error: "resource string/eos_login_protocol_scheme not found"

**Full error message:**
```
Execution failed for task ':launcher:processReleaseResources'.
> Android resource linking failed
  ERROR: .gradle/caches/.../jetified-eossdk-StaticSTDC-release/AndroidManifest.xml:13:13-44:
  AAPT: error: resource string/eos_login_protocol_scheme
  (aka com.YourCompany.YourGame:string/eos_login_protocol_scheme) not found.
```

**What's happening:** The EOS SDK AAR's `AndroidManifest.xml` references `eos_login_protocol_scheme` for OAuth login deep-link callbacks.

**Automatic fix:** `EOSAndroidBuildProcessor` reads the `ClientId` from your `EOSConfig` and injects it into `strings.xml`. See [EOS Login Scheme](#eos-login-scheme) above.

**Manual fix (if needed):**
1. Create the file `Assets/Plugins/Android/res/values/eos_values.xml`
2. Add:
```xml
<resources>
    <string name="eos_login_protocol_scheme">eos.yourclientidhere</string>
</resources>
```
3. Replace `yourclientidhere` with your EOS Client ID (lowercase)
4. You can find your Client ID in the [Epic Developer Portal](https://dev.epicgames.com/portal) under Product Settings > SDK Credentials

---

### Warning: "Plugin is not supported on Android"

```
Plugin 'Assets/com.tront.eos-native/Runtime/EOSSDK/Plugins/iOS/EOSSDK.xcframework'
is not supported on Android, please deselect it in Plugin Inspector
```

**This is harmless.** Unity logs a warning for every native plugin that doesn't match the current build target. The iOS frameworks are correctly configured for iOS-only in the plugin importer. These warnings do not affect the build.

---

## Troubleshooting

### "Failed to create lobby: InvalidRequest"

**Cause:** Attempting to create a lobby with `EnableRTCRoom = true` on a platform where RTC is not fully initialized.

**What happens:** `EOSLobbyManager.CreateLobbyAsync()` includes automatic voice fallback logic. If the SDK returns an error when creating a lobby with voice enabled, it automatically retries without voice. This prevents `InvalidRequest` errors on Android and other platforms where the RTC module may not be initialized.

**If it still fails:** Check that `AndroidInitializeOptions` is being used (not generic `InitializeOptions`), and that `RTCOptions = new RTCOptions()` is set on the platform options.

### RTCAudioStatus.Unsupported

`RTCAudioStatus.Unsupported` has the numeric value `0`, which is the default `int` value. It means "no audio devices available" or "audio pipeline not initialized."

**Common causes:**
- `RTCOptions` was null during platform creation (RTC skipped entirely)
- `EOSNativeLoader.java` was not generated (old build without the build processor)
- `System.loadLibrary("EOSSDK")` was called from C# instead of Java
- ProGuard stripped the JNI target classes

**Diagnosis:** Check `AndroidJavaInitSuccess` in the F1 overlay Status tab. If it shows `false`, the Java-side initialization failed and voice will not work.

### LocalAudioStatus Flipping Between Enabled and Unsupported

**Cause:** AudioRecord conflict. Unity's `Microphone.Start()` and the EOS SDK are competing for the same audio hardware.

**Fix:** This should not happen in current versions. `EOSVoiceManager` disables `Microphone.Start()` on Android. If you see this behavior, check that you are not calling `Microphone.Start()` from your own code while EOS voice is active.

### Voice Connected but All Participants Silent

**Checklist:**
1. Verify `RECORD_AUDIO` runtime permission was granted (check Android Settings > Apps > Permissions)
2. Verify `AndroidJavaInitSuccess` is `true` in the F1 overlay
3. Verify `RTCOptions` is set (not null) in platform creation options
4. Verify the lobby was created with `EnableVoice = true`
5. Check the Voice Diagnostics section in the F1 overlay for interface status

### UnsatisfiedLinkError on Library Load

**Cause:** `android:extractNativeLibs` is not set to `true` in the manifest, so native `.so` files from the EOS AAR are not extracted at install time.

**Fix:** This is handled automatically by the build processor. If you are using custom Gradle templates that override the manifest, ensure `android:extractNativeLibs="true"` is present on the `<application>` tag.

### NoClassDefFoundError: CustomTabsServiceConnection

**Cause:** Missing `androidx.browser:browser` dependency. The EOS SDK uses Chrome Custom Tabs for Epic Account login flows.

**Fix:** This is handled automatically by the build processor. If using custom Gradle templates, add `implementation 'androidx.browser:browser:1.4.0'` to your dependencies block.

---

## Gradle Project Structure

For reference, Unity generates this Gradle structure for Android builds:

```
Library/Bee/Android/Prj/IL2CPP/Gradle/
+-- launcher/
|   +-- build.gradle          <-- App module (needs desugaring)
+-- unityLibrary/
|   +-- build.gradle          <-- Library module (has EOS AAR dependency)
|   +-- libs/
|   |   +-- eossdk-StaticSTDC-release.aar
|   +-- src/main/
|       +-- java/.../EOSNativeLoader.java  <-- Generated by build processor
|       +-- res/values/
|       |   +-- strings.xml   <-- Login scheme injected here
|       +-- proguard-eos.pro  <-- Generated keep rules
+-- shared/
```

The `path` parameter in `OnPostGenerateGradleAndroidProject` points to `unityLibrary/`. The launcher is a sibling directory accessed via `Directory.GetParent(path)`.

## PlayEveryWare Comparison

If you have used the PlayEveryWare EOS plugin before, you may have seen their `eos_dependencies.androidlib` module which provides similar functionality. EOS-Native's approach is lighter:

| | PlayEveryWare | EOS-Native |
|---|---|---|
| **Mechanism** | Separate `.androidlib` module + `IPreprocessBuildWithReport` | `IPostGenerateGradleAndroidProject` injection |
| **Desugaring** | Not needed (older SDK) | Auto-injected |
| **Login scheme** | `eos_values.xml` in androidlib | Injected into `strings.xml` |
| **AndroidX deps** | Bundled in androidlib | Auto-injected into Gradle |
| **Gradle template** | Custom `mainTemplate.gradle` required | No templates needed |
| **Config** | Reads from PEW config system | Reads from `EOSConfig` ScriptableObject |
| **JNI classloader** | Not handled | `EOSNativeLoader.java` generated |
| **ProGuard** | Manual rules | Auto-generated `proguard-eos.pro` |

## Tested Configurations

| Unity Version | Gradle | AGP | Android API | Status |
|---|---|---|---|---|
| Unity 6 (6000.0.65f1) | 8.13 | 8.x | 36 (compileSdk) / 23 (minSdk) | Working |

## Still Having Issues?

1. **Clean the Gradle cache:** Delete `Library/Bee/Android/` and rebuild
2. **Check your EOSConfig:** Make sure Client ID is set (Assets > Create > EOS Native > Config)
3. **Check the Console:** Look for `[EOS-Native]` log messages to confirm the processor ran
4. **Check AndroidJavaInitSuccess:** In the F1 overlay Status tab, verify Java init succeeded
5. **Check Voice Diagnostics:** In the F1 overlay Voice tab, expand the Voice Diagnostics section
6. **File an issue:** [github.com/TrentSterling/EOS-Native/issues](https://github.com/TrentSterling/EOS-Native/issues)
