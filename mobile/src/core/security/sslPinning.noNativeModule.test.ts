import { ensureSslPinningInitialized } from './sslPinning';

jest.mock('react-native-ssl-public-key-pinning', () => {
  throw new Error("Cannot find native module 'SslPublicKeyPinning'");
});

/**
 * Pinning fails open by design: a binary without the module must still be able to talk to the
 * API, loudly and without a launch-time outage.
 */
describe('sslPinning without the pinning native module', () => {
  it('lets the request through unpinned', async () => {
    const warn = jest.spyOn(console, 'warn').mockImplementation(() => {});

    await expect(ensureSslPinningInitialized('https://api.palne.shop')).resolves.toBeUndefined();
    expect(warn).toHaveBeenCalledWith(
      '[SslPinning] native module unavailable; pinning is not active',
    );

    warn.mockRestore();
  });
});
