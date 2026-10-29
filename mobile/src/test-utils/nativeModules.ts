/**
 * Pretends the running binary registered these Expo native modules.
 *
 * The security loaders ask `globalThis.expo.modules` - the registry the Expo runtime installs -
 * before they require a package, because requiring a module the binary does not have is fatal
 * inside Metro and cannot be caught (see `src/core/security/optionalNativeModule.ts`). Jest has no
 * such runtime, so a test that exercises the "the binary has the module" path has to provide the
 * registry itself; a test that exercises the other path simply leaves it out.
 *
 * Returns a restore function, so the stub never leaks into the next test.
 */
export function stubExpoNativeModules(modules: Record<string, unknown>): () => void {
  const target = globalThis as unknown as { expo?: { modules?: Record<string, unknown> } };
  const previous = target.expo;

  target.expo = { modules };

  return () => {
    if (previous === undefined) {
      delete target.expo;
    } else {
      target.expo = previous;
    }
  };
}
