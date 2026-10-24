import { loadOptionalNativeModule } from './optionalNativeModule';

describe('loadOptionalNativeModule', () => {
  it('returns the module when the binary has it', () => {
    expect(loadOptionalNativeModule('demo', () => ({ ready: true }))).toEqual({ ready: true });
  });

  it('returns null when the binary cannot resolve the module', () => {
    const warn = jest.spyOn(console, 'warn').mockImplementation(() => {});

    expect(
      loadOptionalNativeModule('demo', () => {
        throw new Error("Cannot find native module 'Demo'");
      }),
    ).toBeNull();
    expect(warn).toHaveBeenCalled();

    warn.mockRestore();
  });
});
