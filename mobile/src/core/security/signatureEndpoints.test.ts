import { requiresDeviceSignature } from './signatureEndpoints';

/**
 * The two lists here mirror DeviceAuth:RequireSignatureForEndpoints and
 * DeviceAuth:RequireSignatureForEndpointsFrom on the backend. A route the backend protects but
 * this side does not sign is a request that answers 401 forever, and a route signed here for no
 * reason is a biometric prompt the customer did not need - so the boundaries between "signed"
 * and "read-only" are pinned here.
 */
describe('requiresDeviceSignature', () => {
  it.each([
    ['POST', '/api/purchases'],
    ['POST', '/api/purchases/bulk'],
    ['POST', '/api/purchases/renew'],
    ['POST', '/api/purchases/'],
    ['POST', '/api/purchases?dryRun=true'],
  ])('signs the checkout route %s %s from every version', (method, endpoint) => {
    expect(requiresDeviceSignature(method, endpoint)).toBe(true);
  });

  it.each([
    '/api/purchases/my',
    '/api/purchases/savings',
    '/api/purchases/renew/quote',
    '/api/purchases/renew/config',
    '/api/purchases/term-quote',
    '/api/vouchers/my',
    '/api/vouchers/inventory',
  ])('leaves the read %s unsigned', (endpoint) => {
    expect(requiresDeviceSignature('GET', endpoint)).toBe(false);
  });

  it('signs burning and restoring a voucher', () => {
    const id = '3f1c8d2a-1b4e-4a7f-9a01-2c3d4e5f6a7b';

    expect(requiresDeviceSignature('PATCH', `/api/vouchers/${id}/mark-used`)).toBe(true);
    expect(requiresDeviceSignature('PATCH', `/api/vouchers/${id}/restore`)).toBe(true);
  });

  it('does not sign a voucher read that merely contains an action word', () => {
    expect(requiresDeviceSignature('GET', '/api/vouchers/3f1c/mark-used')).toBe(false);
    expect(requiresDeviceSignature('PATCH', '/api/vouchers/3f1c/mark-used/extra')).toBe(false);
    expect(requiresDeviceSignature('PATCH', '/api/vouchers/mark-used')).toBe(false);
  });

  it('signs cancelling a purchase, and only the DELETE', () => {
    const orderId = '3f1c8d2a-1b4e-4a7f-9a01-2c3d4e5f6a7b';

    expect(requiresDeviceSignature('DELETE', `/api/purchases/${orderId}`)).toBe(true);
    expect(requiresDeviceSignature('DELETE', `/api/purchases/${orderId}/`)).toBe(true);
    expect(requiresDeviceSignature('GET', `/api/purchases/${orderId}`)).toBe(false);
  });

  it('treats a missing method as a read', () => {
    expect(requiresDeviceSignature('', '/api/vouchers/3f1c/mark-used')).toBe(false);
    expect(requiresDeviceSignature('', '/api/purchases')).toBe(true);
  });
});
