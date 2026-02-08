package com.tront.eosnative;

import android.app.Activity;

/**
 * Helper class that calls EOSSDK.init() from the app's own classloader context.
 * This ensures that JNI_OnLoad can find EOS SDK Java classes via FindClass()
 * and RegisterNatives runs correctly for methods like EOSLogger.Log.
 *
 * Shipped as an .androidlib module so it's always compiled into the APK —
 * no build-time code generation needed. The app's classloader includes both
 * this class and the EOS AAR classes, so FindClass() resolves correctly.
 *
 * IMPORTANT: Do NOT call System.loadLibrary("EOSSDK") before EOSSDK.init().
 * The AAR's init() handles all native library loading internally.
 */
public class EOSNativeInit {
    private static boolean sInitialized = false;
    private static String sLastError = null;

    /**
     * Initialize the EOS SDK from the correct classloader context.
     * @param activity The current Android Activity
     * @return true if init succeeded, false if it threw (native lib may still be loaded)
     */
    public static boolean init(Activity activity) {
        if (sInitialized) return true;

        // Set thread context classloader to the app's classloader.
        // This helps JNI_OnLoad's FindClass resolve AAR classes on Android
        // versions where it checks the thread context classloader.
        try {
            Thread.currentThread().setContextClassLoader(
                activity.getClassLoader());
        } catch (Throwable t) {
            // Non-fatal — continue without classloader hint
            android.util.Log.w("EOSNativeInit", "setContextClassLoader failed: " + t.getMessage());
        }

        try {
            // Let the AAR handle native library loading internally.
            // EOSSDK.init() calls System.loadLibrary from the correct classloader,
            // so JNI_OnLoad's FindClass resolves AAR Java classes properly.
            com.epicgames.mobile.eossdk.EOSSDK.init(activity);
            sInitialized = true;
            sLastError = null;
            android.util.Log.i("EOSNativeInit", "EOSSDK.init() succeeded");
            return true;
        } catch (Throwable t) {
            // Catch Throwable, not Exception — UnsatisfiedLinkError extends Error, not Exception.
            sLastError = t.getClass().getName() + ": " + t.getMessage();
            android.util.Log.e("EOSNativeInit", "EOSSDK.init() failed: " + sLastError, t);
            // Mark as initialized anyway — the native library IS loaded (P/Invoke works),
            // and a retry won't help. Java audio pipeline may be broken though.
            sInitialized = true;
            return false;
        }
    }

    /**
     * Get the last error message from init, or null if init succeeded.
     */
    public static String getLastError() {
        return sLastError;
    }
}
