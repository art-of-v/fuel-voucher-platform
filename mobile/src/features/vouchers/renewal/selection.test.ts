import { canRenewVoucher, toVoucherIdsParam, RENEWAL_MAX_BATCH } from './selection';

// #95 multi-select renewal entry. canRenewVoucher is the gate the wallet applies
// per card (which are selectable) and to the whole section (whether to offer the
// entry point) — it must match the single-voucher modal gate it replaced, so
// these pin every branch against a fixed clock. Days-to-expiry is inclusive of the
// threshold and an already-expired voucher (negative days) still qualifies.

const NOW = new Date('2026-01-01T00:00:00.000Z').getTime();
const THRESHOLD = 14;
const cfg = { enabled: true, thresholdDays: THRESHOLD };

/** ISO expiry `days` from NOW (negative = already expired). */
function expiryInDays(days: number): string {
  return new Date(NOW + days * 86400000).toISOString();
}

/** A plain personal voucher expiring inside the window unless overridden. */
function voucher(over: Partial<Parameters<typeof canRenewVoucher>[0]> = {}) {
  return {
    status: 'active',
    expirationDate: expiryInDays(10),
    legalEntityId: null,
    workerUserId: null,
    ...over,
  };
}

describe('canRenewVoucher', () => {
  it('offers a personal voucher expiring within the threshold', () => {
    expect(canRenewVoucher(voucher(), cfg, 'me', NOW)).toBe(true);
  });

  it('treats the threshold as inclusive', () => {
    expect(canRenewVoucher(voucher({ expirationDate: expiryInDays(THRESHOLD) }), cfg, 'me', NOW)).toBe(true);
  });

  it('still offers an already-expired voucher (negative days ≤ threshold)', () => {
    expect(canRenewVoucher(voucher({ expirationDate: expiryInDays(-5) }), cfg, 'me', NOW)).toBe(true);
  });

  it('rejects a voucher whose expiry is beyond the window', () => {
    expect(canRenewVoucher(voucher({ expirationDate: expiryInDays(30) }), cfg, 'me', NOW)).toBe(false);
  });

  it('is off entirely when the feature is disabled or unconfigured', () => {
    expect(canRenewVoucher(voucher(), { enabled: false, thresholdDays: THRESHOLD }, 'me', NOW)).toBe(false);
    expect(canRenewVoucher(voucher(), null, 'me', NOW)).toBe(false);
    expect(canRenewVoucher(voucher(), undefined, 'me', NOW)).toBe(false);
  });

  it('rejects a used voucher even inside the window', () => {
    expect(canRenewVoucher(voucher({ status: 'used' }), cfg, 'me', NOW)).toBe(false);
  });

  it('rejects a blocked voucher', () => {
    expect(canRenewVoucher(voucher({ status: 'blocked' }), cfg, 'me', NOW)).toBe(false);
  });

  it('rejects a voucher gifted out to a worker, but allows one gifted to me', () => {
    const giftedOut = voucher({ legalEntityId: 'co-1', workerUserId: 'someone-else' });
    const giftedToMe = voucher({ legalEntityId: 'co-1', workerUserId: 'me' });
    expect(canRenewVoucher(giftedOut, cfg, 'me', NOW)).toBe(false);
    expect(canRenewVoucher(giftedToMe, cfg, 'me', NOW)).toBe(true);
  });

  it('allows a company-pool voucher (no worker assigned)', () => {
    expect(canRenewVoucher(voucher({ legalEntityId: 'co-1', workerUserId: null }), cfg, 'me', NOW)).toBe(true);
  });

  it('rejects a voucher with no expiry date', () => {
    expect(canRenewVoucher(voucher({ expirationDate: undefined }), cfg, 'me', NOW)).toBe(false);
  });
});

describe('toVoucherIdsParam', () => {
  it('joins ids into the CSV the renew screen parses, preserving order', () => {
    expect(toVoucherIdsParam(['a', 'b', 'c'])).toBe('a,b,c');
    expect(toVoucherIdsParam(new Set(['x', 'y']))).toBe('x,y');
    expect(toVoucherIdsParam([])).toBe('');
  });
});

describe('RENEWAL_MAX_BATCH', () => {
  it('matches the backend MaxItems cap of 50', () => {
    expect(RENEWAL_MAX_BATCH).toBe(50);
  });
});
