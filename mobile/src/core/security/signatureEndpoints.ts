// Which requests the app signs with the device key. Kept in its own module so the rule can be
// tested without standing up the network client, and so the mirror of the backend's two lists
// reads in one place.

// Must stay in sync with the backend's DeviceAuth:RequireSignatureForEndpoints.
// Checkout goes through /api/purchases, /api/purchases/bulk and /api/purchases/renew - the
// backend rejects unsigned requests on those routes. Matching is EXACT (see
// requiresDeviceSignature), which is why the read-only /api/purchases/renew/quote and
// /renew/config previews are NOT signed and never prompt for biometrics before payment.
export const SIGNATURE_REQUIRED_ENDPOINTS = Object.freeze([
  '/api/purchases',
  '/api/purchases/bulk',
  '/api/purchases/renew',
]);

// Must stay in sync with the backend's DeviceAuth:RequireSignatureForEndpointsFrom, which is
// enforced from app version 1.3.0 onward. Signing them now costs nothing - the backend does not
// require it yet - and means enforcement can be switched on without a second publishing round.
// Burning and restoring are PATCH, so a signed GET would be a biometric prompt for a call that
// cannot exist.
const VOUCHER_ACTION_PATH = /^\/api\/vouchers\/[^/]+\/(mark-used|restore)$/;
const PURCHASE_CANCELLATION_PATH = /^\/api\/purchases\/[^/]+$/;

/**
 * Whether this request has to carry a device signature.
 *
 * A request needs one when its path is EXACTLY one of the protected routes (query string and any
 * trailing slash stripped first). A substring test would wrongly sign every nested read - e.g.
 * the renewal quote lives under /api/purchases/renew/... but must stay signature-free.
 */
export function requiresDeviceSignature(method: string, endpoint: string): boolean {
  const queryStart = endpoint.indexOf('?');
  let path = queryStart >= 0 ? endpoint.slice(0, queryStart) : endpoint;
  if (path.length > 1 && path.endsWith('/')) path = path.slice(0, -1);

  if (SIGNATURE_REQUIRED_ENDPOINTS.includes(path)) return true;

  const verb = (method || 'GET').toUpperCase();

  if (verb === 'PATCH' && VOUCHER_ACTION_PATH.test(path)) return true;

  return verb === 'DELETE' && PURCHASE_CANCELLATION_PATH.test(path);
}
