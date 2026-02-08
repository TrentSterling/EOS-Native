# CLAUDE-ANDROID.md

Detailed Android build and voice reference. See CLAUDE.md for project overview and rules.

## Android Build (Core Library Desugaring + AndroidX + Native Libs)

The EOS SDK AAR (`eossdk-StaticSTDC-release.aar`) requires Java 8 core library desugaring and several AndroidX libraries. Without them, Android builds fail at either build time or runtime.

`EOSAndroidBuildProcessor.cs` (in `EOSNative.Editor/`) automatically injects the required Gradle config into both `launcher/build.gradle` and `unityLibrary/build.gradle` via `IPostGenerateGradleAndroidProject`. It handles six things:

1. **Core library desugaring:** `coreLibraryDesugaringEnabled true` in `compileOptions` + `coreLibraryDesugaring 'com.android.tools:desugar_jdk_libs:2.1.4'` in `dependencies`
2. **AndroidX dependencies:** The EOS AAR's transitive Maven dependencies aren't resolved by Unity, so we inject them explicitly:
   - `androidx.appcompat:appcompat:1.5.1`
   - `androidx.constraintlayout:constraintlayout:2.1.4`
   - `androidx.security:security-crypto:1.0.0`
   - `androidx.browser:browser:1.4.0` — **required** for Chrome Custom Tabs (account portal login). Without this, `EOSSDK.init()` throws `NoClassDefFoundError: CustomTabsServiceConnection`
3. **Extract native libs:** Injects `android:extractNativeLibs="true"` into AndroidManifest.xml's `<application>` tag — required so native `.so` files from the EOS AAR are extracted at install time (without this, `System.loadLibrary` may fail with `UnsatisfiedLinkError`)
4. **EOS login scheme:** Injects `eos_login_protocol_scheme` string resource into `strings.xml`
5. **Java init helper:** Generates `EOSNativeLoader.java` with `System.loadLibrary("EOSSDK")` called from Java classloader context — ensures `JNI_OnLoad`'s `FindClass` uses the app classloader so `RegisterNatives` succeeds for `EOSLogger` and other native methods
6. **ProGuard keep rules:** Generates `proguard-eos.pro` to prevent R8/ProGuard from stripping EOS SDK Java classes needed for JNI native method registration

Also ensures `google()` repository is present for AndroidX resolution and injects RECORD_AUDIO/ACCESS_WIFI_STATE permissions. No manual Gradle template editing is required.

### Android SDK Loading (Java Classloader Fix)

**Approach:** `EOSNativeLoader.java` (generated at build time by `EOSAndroidBuildProcessor`) calls `System.loadLibrary("EOSSDK")` from Java code compiled into the APK, then calls `EOSSDK.init(activity)`. C# calls `EOSNativeLoader.initEOS(activity)` via `AndroidJavaClass` at `SubsystemRegistration` timing (earliest possible).

**Why it works:** Per [Android JNI docs](https://developer.android.com/training/articles/perf-jni), `FindClass` in `JNI_OnLoad` uses the **caller's classloader** — the classloader of the class that called `System.loadLibrary`. Since `EOSNativeLoader` is compiled into the APK's dex (loaded by the app classloader), `FindClass("com/epicgames/mobile/eossdk/EOSLogger")` succeeds → `RegisterNatives` runs → RTC/Audio subsystems work.

**Why previous approaches failed:** `EOSSDK.init(activity)` does NOT call `System.loadLibrary("EOSSDK")`. The native library was only loaded later by P/Invoke's `dlopen` from C++ code — no Java frame on the stack → `JNI_OnLoad` used the system classloader → `FindClass` failed. Setting the thread context classloader doesn't help because `FindClass` in `JNI_OnLoad` uses the call-stack classloader, not the thread context classloader.

**CRITICAL:** `System.loadLibrary("EOSSDK")` MUST be called from Java code (not from C# JNI bridge). The whole point is that the Java caller's classloader is used by `JNI_OnLoad`.

**Flow:** `[SubsystemRegistration] EarlyAndroidInit()` → `EOSNativeLoader.initEOS(activity)` → `System.loadLibrary("EOSSDK")` (from Java) → `JNI_OnLoad` (app classloader) → `EOSSDK.init(activity)`. Then `EOSManager.Start()` → `Initialize()` → `PlatformInterface.Initialize()` (first P/Invoke, safe because lib already loaded).

**Fallback:** If `EOSNativeLoader` class is not found (old builds without the build processor), falls back to direct `EOSSDK.init(activity)` with a warning that voice may not work.

**Diagnostics:** `LoadAndroidLibrary()` logs API level, device model, and supported ABIs at startup. The `AndroidJavaInitSuccess` property tracks whether Java-side init succeeded.

**Console early init:** `EOSNativeConsole.Instance` is created in `Awake()` (before `LoadNativeLibrary` and `LoadAndroidLibrary`) so all startup errors are captured in the runtime console.

### Android RTC/Voice Prerequisites

For RTC/Voice to work on Android, **four things** must be in place:

1. **`RTCOptions` must be set on platform creation** — The generic `Options` struct must include `RTCOptions = new RTCOptions()`. Setting it to null (the default) tells the SDK to **skip RTC initialization entirely**. This was the primary cause of RTC/Audio showing RED on Android.

2. **`RECORD_AUDIO` permission must be in AndroidManifest AND requested at runtime** — `EOSAndroidBuildProcessor` auto-injects the manifest declaration, and `EOSManager.Awake()` calls `EOSPlatformHelper.RequestMicrophonePermission()` for all Android devices (not just Quest). On API 23+, the manifest declaration alone is insufficient — the app must request permission via `Permission.RequestUserPermission()` at runtime.

3. **`System.loadLibrary("EOSSDK")` must be called from Java** — `EOSNativeLoader.java` (generated by build processor) handles this. Without it, `JNI_OnLoad` uses the system classloader and `RegisterNatives` fails for RTC/Audio classes.

4. **Unity `Microphone.Start()` must NOT run on Android** — The EOS SDK opens its own `AudioRecord` for voice transmission. On Android < 10, only one `AudioRecord` can exist. On Android 10+, concurrent capture has priority rules that may silence one client. `EOSVoiceManager.StartMicCapture()` is disabled on Android to avoid this conflict. The mic level bar shows 0% on Android, but EOS voice works correctly.

### Auto-Recovery on App Resume

When Android suspends the app (screen off, task switch), EOS Connect auth tokens expire after a few minutes. On resume, the SDK is still initialized (platform exists, interfaces are valid) but the user is no longer logged in. `EOSManager.TryAutoRecover()` handles this transparently:

1. **On pause:** Caches `IsLoggedIn` state and `CurrentLobby.LobbyId`
2. **On resume:** If `IsInitialized && !IsLoggedIn && _wasLoggedInBeforePause`, waits 500ms for SDK to stabilize, then calls `LoginSmartAsync()` to re-authenticate
3. **After login:** If a lobby ID was cached, attempts `JoinLobbyByIdAsync()` to rejoin. P2P connections and voice recover automatically via existing handshake retry and voice auto-connect mechanisms
4. **Auth expiration while foregrounded:** `OnLoginStatusChanged` also sets the recovery flags, so `TryAutoRecover()` fires on the next focus/pause cycle even if the app wasn't backgrounded

The `_isRecovering` flag prevents double-fire since both `OnApplicationPause(false)` and `OnApplicationFocus(true)` fire on Android resume. All recovery steps are logged to the console for visibility.

### Android SDK Initialization

On Android, `PlatformInterface.Initialize()` MUST use `AndroidInitializeOptions` (not generic `InitializeOptions`). The Android-specific struct includes a `Reserved` field set to `{1, 1}` and `SystemInitializeOptions` for Android file paths. Using the generic struct causes RTC/Audio subsystems to not initialize. The SDK generates both overloads in `Source/Generated/Android/Platform/PlatformInterface.cs`.
