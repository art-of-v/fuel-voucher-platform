import { isRenewableVoucher, countRenewable } from './eligibility';
import type { Voucher } from '../../../core/types/api';

// Fixed clock so threshold maths is deterministic regardless of when the suite runs.
const NOW = new Date('2026-09-30T00:00:00Z').getTime();
const cfg = { enabled: true, thresholdDays: 14 };

function makeVoucher(overrides: Partial<Voucher> = {}): Voucher {
  return {
    id: 'v1',
    provider: 'okko',
    fuelType: 'a95',
    amount: 10,
    status: 'active',
    legalEntityId: null,
    workerUserId: null,
    expirationDate: '2026-10-05', // 5 days out → within the 14-day threshold
    ...overrides,
  };
}

describe('isRenewableVoucher', () => {
  it('accepts an owned voucher that is near expiry with the feature on', () => {
    expect(isRenewableVoucher(makeVoucher(), 'me', cfg, NOW)).toBe(true);
  });

  it('rejects when the feature is disabled', () => {
    expect(
      isRenewableVoucher(makeVoucher(), 'me', { enabled: false, thresholdDays: 14 }, NOW),
    ).toBe(false);
  });

  it('rejects when there is no config', () => {
    expect(isRenewableVoucher(makeVoucher(), 'me', null, NOW)).toBe(false);
  });

  it('rejects an already-used voucher', () => {
    expect(isRenewableVoucher(makeVoucher({ status: 'used' }), 'me', cfg, NOW)).toBe(false);
  });

  it('rejects a blocked voucher', () => {
    expect(isRenewableVoucher(makeVoucher({ status: 'blocked' }), 'me', cfg, NOW)).toBe(false);
  });

  it('rejects a voucher gifted to another worker (owner cannot act on it)', () => {
    const v = makeVoucher({ legalEntityId: 'co-1', workerUserId: 'other-worker' });
    expect(isRenewableVoucher(v, 'me', cfg, NOW)).toBe(false);
  });

  it('rejects a voucher a company issued to me (renewal is a self-service checkout)', () => {
    // The backend quotes renewal only for `AssignedToUserId == caller`; a voucher issued
    // by a company keeps the owner as the purchasing user, so offering it would only
    // earn a "not your voucher" rejection (multi-company epic #103, S5).
    const v = makeVoucher({ legalEntityId: 'co-1', workerUserId: 'me' });
    expect(isRenewableVoucher(v, 'me', cfg, NOW)).toBe(false);
  });

  it('accepts a company-pool voucher', () => {
    const v = makeVoucher({ legalEntityId: 'co-1', workerUserId: null });
    expect(isRenewableVoucher(v, 'me', cfg, NOW)).toBe(true);
  });

  it('rejects a voucher with no expiry date', () => {
    expect(isRenewableVoucher(makeVoucher({ expirationDate: undefined }), 'me', cfg, NOW)).toBe(
      false,
    );
  });

  it('rejects a voucher expiring beyond the threshold', () => {
    expect(isRenewableVoucher(makeVoucher({ expirationDate: '2026-11-01' }), 'me', cfg, NOW)).toBe(
      false,
    );
  });

  it('accepts a voucher expiring exactly at the threshold but not one day past it', () => {
    expect(isRenewableVoucher(makeVoucher({ expirationDate: '2026-10-14' }), 'me', cfg, NOW)).toBe(
      true,
    );
    expect(isRenewableVoucher(makeVoucher({ expirationDate: '2026-10-15' }), 'me', cfg, NOW)).toBe(
      false,
    );
  });

  it('accepts an already-expired voucher (the replace branch)', () => {
    expect(isRenewableVoucher(makeVoucher({ expirationDate: '2026-03-18' }), 'me', cfg, NOW)).toBe(
      true,
    );
  });
});

describe('countRenewable', () => {
  it('counts only the eligible vouchers', () => {
    const vouchers = [
      makeVoucher({ id: 'a' }), // near expiry → eligible
      makeVoucher({ id: 'b', expirationDate: '2026-03-18' }), // expired → eligible
      makeVoucher({ id: 'c', status: 'used' }), // used → no
      makeVoucher({ id: 'd', expirationDate: '2026-12-31' }), // far off → no
      makeVoucher({ id: 'e', expirationDate: undefined }), // no expiry → no
    ];
    expect(countRenewable(vouchers, 'me', cfg, NOW)).toBe(2);
  });

  it('is zero when the feature is off', () => {
    expect(countRenewable([makeVoucher()], 'me', { enabled: false, thresholdDays: 14 }, NOW)).toBe(
      0,
    );
  });
});
