/**
 * Loads a native module the installed binary may not have.
 *
 * An OTA update replaces the JS bundle and never the native layer, so a bundle can outlive the
 * modules it was written against. A module that resolves its native side while it is being
 * imported (`requireNativeModule`) throws right there - and a throw during import takes the whole
 * bundle down before the app can show anything, let alone a sign-in screen.
 *
 * So the module is resolved at call time, inside a try/catch, and "no module" is a feature that is
 * unavailable - never a crash. The caller decides what that means: no attestation proof, no
 * pinning, an unprotected request.
 */
export function loadOptionalNativeModule<T>(name: string, load: () => T): T | null {
  try {
    return load();
  } catch (error) {
    console.warn(`[NativeModule] ${name} is not in this binary; continuing without it:`, error);
    return null;
  }
}
