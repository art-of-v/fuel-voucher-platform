/**
 * Loads a native module the installed binary may not have.
 *
 * An OTA update replaces the JS bundle and never the native layer, so a bundle can outlive the
 * modules it was written against: `expo-crypto`, `@expo/app-integrity` and the SSL pinning module
 * all resolve their native side while they are being imported (`requireNativeModule`).
 *
 * A `try/catch` around `require` does NOT cover that case. Metro loads the first require of a
 * module through `guardedLoadModule`, and when the module factory throws it does not rethrow: it
 * calls `global.ErrorUtils.reportFatalError(error)` and returns undefined (metro-runtime,
 * `src/polyfills/require.js`). React Native treats that report as fatal and takes the app down, so
 * the `catch` below never runs - which is exactly how the "guarded" bundle of 2026-10-29 still
 * killed build 236 on the first request to a protected endpoint.
 *
 * The only order that is safe is therefore: check the runtime first, require second.
 * `hasExpoNativeModule` reads the same registry `requireOptionalNativeModule` reads, so a true
 * from it means the `require` that follows will find its native module.
 */

/** The registry the Expo runtime installs on `globalThis.expo`; absent outside React Native (Jest). */
interface ExpoRuntime {
  modules?: Record<string, unknown>;
}

export function hasExpoNativeModule(name: string): boolean {
  try {
    return Boolean((globalThis as unknown as { expo?: ExpoRuntime }).expo?.modules?.[name]);
  } catch {
    // A registry that throws on lookup cannot vouch for the module either.
    return false;
  }
}

/**
 * @param isPresent Whether the running binary registered the module. Required, not optional: a
 *  caller that forgets it reintroduces the fatal `require` this function exists to prevent.
 * @param load The `require` to run - only ever reached when the module is really there.
 */
export function loadOptionalNativeModule<T>(
  name: string,
  isPresent: () => boolean,
  load: () => T,
): T | null {
  try {
    if (!isPresent()) {
      console.warn(`[NativeModule] ${name} is not in this binary; continuing without it`);
      return null;
    }
    return load();
  } catch (error) {
    console.warn(`[NativeModule] ${name} could not be loaded; continuing without it:`, error);
    return null;
  }
}
