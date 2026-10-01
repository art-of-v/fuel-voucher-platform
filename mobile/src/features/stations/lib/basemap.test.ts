import Constants from 'expo-constants';

import { resolveCartoApiKey } from './basemap';

jest.mock('expo-constants', () => ({
  __esModule: true,
  default: { expoConfig: { extra: { cartoApiKey: '' } } },
}));

describe('resolveCartoApiKey', () => {
  const extra = (Constants as unknown as { expoConfig: { extra: Record<string, unknown> } })
    .expoConfig.extra;

  beforeEach(() => {
    delete process.env.EXPO_PUBLIC_CARTO_API_KEY;
    extra.cartoApiKey = '';
  });

  it('prefers the EXPO_PUBLIC_CARTO_API_KEY env var (EAS build) over app.json', () => {
    process.env.EXPO_PUBLIC_CARTO_API_KEY = 'env-key';
    extra.cartoApiKey = 'extra-key';

    expect(resolveCartoApiKey()).toBe('env-key');
  });

  it('falls back to app.json expo.extra.cartoApiKey when the env var is unset (Xcode Archive)', () => {
    extra.cartoApiKey = 'extra-key';

    expect(resolveCartoApiKey()).toBe('extra-key');
  });

  it('treats an empty env var as unset and falls through to app.json', () => {
    process.env.EXPO_PUBLIC_CARTO_API_KEY = '';
    extra.cartoApiKey = 'extra-key';

    expect(resolveCartoApiKey()).toBe('extra-key');
  });

  it('returns null when neither the env var nor app.json provides a key', () => {
    expect(resolveCartoApiKey()).toBeNull();
  });
});
