import { useStore } from './appStore';
import { isKnownTheme, syncAppearanceTheme } from './appearance';

// The call bypasses the session client on purpose (see appearance.ts): pinning is asked for
// first, then one plain fetch goes out, so the test mocks exactly those two things.
jest.mock('../api/apiConfig', () => ({ BASE_URL: 'https://test.local' }));
jest.mock('../security/sslPinning', () => ({
  ensureSslPinningInitialized: jest.fn().mockResolvedValue(undefined),
}));

const fetchMock = jest.fn();

/** A response the way `fetch` hands one back: only `ok` and `json` are read. */
function ok(body: unknown) {
  return { ok: true, status: 200, json: async () => body } as any;
}

function reset() {
  fetchMock.mockReset();
  (globalThis as any).fetch = fetchMock;
  useStore.setState({ theme: 'obsidian', themePinned: false });
}

describe('isKnownTheme', () => {
  it('accepts every theme id the build ships', () => {
    expect(isKnownTheme('obsidian')).toBe(true);
    expect(isKnownTheme('lemberg')).toBe(true);
    expect(isKnownTheme('blade')).toBe(true);
  });

  it('refuses anything that is not one of them', () => {
    // The case that matters: an id from a NEWER theme catalogue than this build has. Painting it
    // would resolve to undefined colours, so the app ignores it and keeps what it has.
    expect(isKnownTheme('obsidian-forge')).toBe(false);
    expect(isKnownTheme('sunset')).toBe(false);
    expect(isKnownTheme('')).toBe(false);
    expect(isKnownTheme(undefined)).toBe(false);
    expect(isKnownTheme(7)).toBe(false);
  });
});

describe('syncAppearanceTheme', () => {
  beforeEach(reset);

  it('applies the installation default for a worker who has not chosen a theme', async () => {
    fetchMock.mockResolvedValueOnce(ok({ defaultTheme: 'lemberg' }));

    await expect(syncAppearanceTheme()).resolves.toBe('lemberg');
    expect(useStore.getState().theme).toBe('lemberg');
  });

  it('asks the anonymous appearance endpoint', async () => {
    fetchMock.mockResolvedValueOnce(ok({ defaultTheme: 'nova' }));

    await syncAppearanceTheme();

    expect(fetchMock).toHaveBeenCalledWith(
      'https://test.local/api/app/appearance',
      expect.objectContaining({ method: 'GET' }),
    );
  });

  it('never overrides a theme the worker picked', async () => {
    useStore.getState().setTheme('sorbet');
    fetchMock.mockResolvedValueOnce(ok({ defaultTheme: 'lemberg' }));

    await expect(syncAppearanceTheme()).resolves.toBeNull();
    expect(useStore.getState().theme).toBe('sorbet');
  });

  it('still applies an unchanged default silently', async () => {
    fetchMock.mockResolvedValueOnce(ok({ defaultTheme: 'obsidian' }));

    await expect(syncAppearanceTheme()).resolves.toBeNull();
    expect(useStore.getState().theme).toBe('obsidian');
  });

  it('ignores a theme this build cannot paint', async () => {
    fetchMock.mockResolvedValueOnce(ok({ defaultTheme: 'obsidian-forge' }));

    await expect(syncAppearanceTheme()).resolves.toBeNull();
    expect(useStore.getState().theme).toBe('obsidian');
  });

  it('ignores a body that is not what it claims to be', async () => {
    fetchMock.mockResolvedValueOnce(ok(null));

    await expect(syncAppearanceTheme()).resolves.toBeNull();
    expect(useStore.getState().theme).toBe('obsidian');
  });

  it('keeps the current theme when the request fails', async () => {
    fetchMock.mockResolvedValueOnce({ ok: false, status: 502, json: async () => ({}) } as any);
    await expect(syncAppearanceTheme()).resolves.toBeNull();

    fetchMock.mockRejectedValueOnce(new Error('network down'));
    await expect(syncAppearanceTheme()).resolves.toBeNull();

    expect(useStore.getState().theme).toBe('obsidian');
  });
});

describe('appStore theme pinning', () => {
  beforeEach(reset);

  it('a server theme does not pin, so a later default still applies', () => {
    useStore.getState().applyServerTheme('blue');
    useStore.getState().applyServerTheme('glass');

    expect(useStore.getState().theme).toBe('glass');
    expect(useStore.getState().themePinned).toBe(false);
  });

  it('the profile picker pins, and pinning survives', () => {
    useStore.getState().setTheme('white');

    expect(useStore.getState().theme).toBe('white');
    expect(useStore.getState().themePinned).toBe(true);

    // What the next launch does: the store rehydrates `themePinned: true`, and the server's
    // default must not take it back.
    useStore.getState().applyServerTheme('obsidian');
    expect(useStore.getState().theme).toBe('white');
  });
});
