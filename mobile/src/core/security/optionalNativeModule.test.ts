import { hasExpoNativeModule, loadOptionalNativeModule } from './optionalNativeModule';

const present = () => true;
const absent = () => false;

describe('loadOptionalNativeModule', () => {
  it('returns the module when the binary has it', () => {
    expect(loadOptionalNativeModule('demo', present, () => ({ ready: true }))).toEqual({
      ready: true,
    });
  });

  it('never runs the require when the binary does not have the module', () => {
    const load = jest.fn(() => ({ ready: true }));

    expect(loadOptionalNativeModule('demo', absent, load)).toBeNull();
    // The point of the probe: reaching the require is what Metro turns into a fatal error.
    expect(load).not.toHaveBeenCalled();
    expect(console.warn).toHaveBeenCalledWith(
      '[NativeModule] demo is not in this binary; continuing without it',
    );
  });

  it('still returns null when the require throws anyway', () => {
    expect(
      loadOptionalNativeModule('demo', present, () => {
        throw new Error("Cannot find native module 'Demo'");
      }),
    ).toBeNull();
    expect(console.warn).toHaveBeenCalled();
  });
});

describe('hasExpoNativeModule', () => {
  afterEach(() => {
    delete (globalThis as unknown as { expo?: unknown }).expo;
  });

  it('reads the registry the Expo runtime installs', () => {
    (globalThis as unknown as { expo?: unknown }).expo = {
      modules: { ExpoCrypto: {} },
    };

    expect(hasExpoNativeModule('ExpoCrypto')).toBe(true);
    expect(hasExpoNativeModule('ExpoAppIntegrity')).toBe(false);
  });

  it('says no when the runtime has no registry at all', () => {
    expect(hasExpoNativeModule('ExpoCrypto')).toBe(false);
  });

  it('says no when the registry throws on lookup', () => {
    (globalThis as unknown as { expo?: unknown }).expo = {
      get modules(): Record<string, unknown> {
        throw new Error('registry unavailable');
      },
    };

    expect(hasExpoNativeModule('ExpoCrypto')).toBe(false);
  });
});
