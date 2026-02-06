# Android Build Guide

Building Unity projects with the EOS SDK for Android requires some Gradle configuration that EOS-Native handles automatically.

## It Just Works

As of v2.4.2, EOS-Native includes `EOSAndroidBuildProcessor` which automatically configures your Android build. **No manual Gradle editing is required.**

The build processor runs via `IPostGenerateGradleAndroidProject` and handles:
1. Core library desugaring (required by the EOS AAR)
2. EOS login protocol scheme string resource (required by the AAR's AndroidManifest)

You should see these log messages during Android builds:

```
[EOS-Native] Enabled core library desugaring in launcher/build.gradle
[EOS-Native] Enabled core library desugaring in unityLibrary/build.gradle
[EOS-Native] Injected eos_login_protocol_scheme: eos.your_client_id_here
```

If you see those, everything is configured correctly.

---

## Common Build Errors (and what causes them)

If the automatic processor fails or you're troubleshooting a custom setup, here are the errors you'll encounter and what they mean.

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

**What's happening:** The EOS SDK Android library (`eossdk-StaticSTDC-release.aar`) uses Java 8+ APIs that aren't available on older Android versions. The AAR's metadata declares that core library desugaring must be enabled so the Android build tools can backport these APIs.

**Automatic fix:** `EOSAndroidBuildProcessor` injects the following into both `launcher/build.gradle` and `unityLibrary/build.gradle`:

```gradle
// In the compileOptions block:
compileOptions {
    coreLibraryDesugaringEnabled true
    sourceCompatibility JavaVersion.VERSION_17
    targetCompatibility JavaVersion.VERSION_17
}

// In the dependencies block:
dependencies {
    coreLibraryDesugaring 'com.android.tools:desugar_jdk_libs:2.1.4'
}
```

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

**What's happening:** The EOS SDK AAR includes an `AndroidManifest.xml` that references an Android string resource called `eos_login_protocol_scheme`. This is used for OAuth login deep-link callbacks — when a user logs in via Epic Account on Android, the OS uses this scheme to redirect back to your app. The resource must be defined in your project's string resources or the AAPT resource linker fails.

**Automatic fix:** `EOSAndroidBuildProcessor` reads the `ClientId` from your `EOSConfig` ScriptableObject asset and injects it into `unityLibrary/src/main/res/values/strings.xml`:

```xml
<resources>
    <string name="eos_login_protocol_scheme">eos.yourclientidhere</string>
</resources>
```

The format is `eos.{client_id_lowercase}` as required by the [EOS Android documentation](https://dev.epicgames.com/docs/epic-online-services/platforms/android).

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

**This is harmless.** Unity logs a warning for every native plugin that doesn't match the current build target. The iOS frameworks are correctly configured for iOS-only in the plugin importer — Unity just likes to complain about them during Android builds. These warnings do not affect the build.

---

## Prerequisites

- **EOSConfig asset** — Create one via Assets > Create > EOS Native > Config and fill in your Client ID. The build processor reads this to generate the login scheme. Without it, a placeholder is used (build succeeds but OAuth login won't redirect).
- **Unity 2021.3+** — The build processor uses `IPostGenerateGradleAndroidProject` which is available in all supported Unity versions.
- **Android Build Support** — Install via Unity Hub > Installs > Add Modules.

## How the Build Processor Works

`EOSAndroidBuildProcessor.cs` (in `EOSNative.Editor/`) implements `IPostGenerateGradleAndroidProject` with callback order 99 (runs late to avoid conflicts):

1. Unity generates the Gradle project
2. The processor finds both `launcher/build.gradle` and `unityLibrary/build.gradle`
3. For each: checks if desugaring is already configured, injects it if not
4. Reads the `EOSConfig` asset to get the Client ID
5. Creates/updates `strings.xml` with the `eos_login_protocol_scheme` resource
6. Gradle builds the project with everything configured

The processor is idempotent — it checks before injecting, so running multiple builds won't duplicate entries.

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
|       +-- res/values/
|           +-- strings.xml   <-- Login scheme injected here
+-- shared/
```

The `path` parameter in `OnPostGenerateGradleAndroidProject` points to `unityLibrary/`. The launcher is a sibling directory accessed via `Directory.GetParent(path)`.

## PlayEveryWare Comparison

If you've used the PlayEveryWare EOS plugin before, you may have seen their `eos_dependencies.androidlib` module which provides similar functionality. EOS-Native's approach is lighter:

| | PlayEveryWare | EOS-Native |
|---|---|---|
| **Mechanism** | Separate `.androidlib` module + `IPreprocessBuildWithReport` | `IPostGenerateGradleAndroidProject` injection |
| **Desugaring** | Not needed (older SDK) | Auto-injected |
| **Login scheme** | `eos_values.xml` in androidlib | Injected into `strings.xml` |
| **AndroidX deps** | Bundled in androidlib | Not needed |
| **Gradle template** | Custom `mainTemplate.gradle` required | No templates needed |
| **Config** | Reads from PEW config system | Reads from `EOSConfig` ScriptableObject |

## Tested Configurations

| Unity Version | Gradle | AGP | Android API | Status |
|---|---|---|---|---|
| Unity 6 (6000.0.65f1) | 8.13 | 8.x | 36 (compileSdk) / 23 (minSdk) | Working |

## Still Having Issues?

1. **Clean the Gradle cache:** Delete `Library/Bee/Android/` and rebuild
2. **Check your EOSConfig:** Make sure Client ID is set (Assets > Create > EOS Native > Config)
3. **Check the Console:** Look for `[EOS-Native]` log messages to confirm the processor ran
4. **File an issue:** [github.com/TrentSterling/EOS-Native/issues](https://github.com/TrentSterling/EOS-Native/issues)
