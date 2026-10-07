import {
  daysUntilExpiration,
  isExpiringSoon,
  resolveBrand,
  isWalletEmpty,
  unusedLitres,
  countUsed,
  brandColorFor,
  splitByIssuanceReceipt,
  isLiveWalletVoucher,
  type WalletCounts,
  type WalletSection,
} from './display';

/**
 * These rules shipped inside `app/my-codes.tsx` — a single ~700-line function —
 * with no test. Each one has a visible wrong answer when it breaks, which is why
 * they are worth pinning rather than re-reading.
 */

const DAY = 86_400_000;
/** A fixed instant so nothing here depends on the wall clock. */
const NOW = Date.parse('2026-03-01T12:00:00Z');

function daysFromNow(days: number) {
  return new Date(NOW + days * DAY).toISOString();
}

describe('daysUntilExpiration', () => {
  it('returns whole days remaining', () => {
    expect(daysUntilExpiration(daysFromNow(10), NOW)).toBe(10);
  });

  it('rounds a partial day up, so "expires later today" is 1, not 0', () => {
    // `Math.ceil` rather than `round` or `floor`: a voucher expiring in two
    // hours must still read as a day left, or a same-day expiry reads as already
    // gone.
    expect(daysUntilExpiration(daysFromNow(0.2), NOW)).toBe(1);
  });

  it('returns 0 on the expiry date itself', () => {
    expect(daysUntilExpiration(daysFromNow(0), NOW)).toBe(0);
  });

  it('returns a negative number once expired, and does not clamp', () => {
    // Clamping to 0 would make an expired voucher indistinguishable from one
    // expiring today; the caller colours them differently.
    expect(daysUntilExpiration(daysFromNow(-3), NOW)).toBe(-3);
  });

  it('returns null when there is no date', () => {
    expect(daysUntilExpiration(undefined, NOW)).toBeNull();
    expect(daysUntilExpiration(null, NOW)).toBeNull();
    expect(daysUntilExpiration('', NOW)).toBeNull();
  });

  it('returns null for an unparseable date rather than NaN', () => {
    // NaN would poison every comparison downstream and quietly mark the voucher
    // as "not expiring".
    expect(daysUntilExpiration('not-a-date', NOW)).toBeNull();
  });
});

describe('isExpiringSoon', () => {
  it('warns at the threshold and below', () => {
    expect(isExpiringSoon(30)).toBe(true);
    expect(isExpiringSoon(29)).toBe(true);
    expect(isExpiringSoon(0)).toBe(true);
    expect(isExpiringSoon(-1)).toBe(true);
  });

  it('does not warn comfortably before it', () => {
    expect(isExpiringSoon(31)).toBe(false);
    expect(isExpiringSoon(400)).toBe(false);
  });

  it('never warns about a voucher with no expiry date', () => {
    // `null` is not 0. Treating it as 0 would flag every undated voucher forever.
    expect(isExpiringSoon(null)).toBe(false);
  });

  it('honours a different threshold', () => {
    expect(isExpiringSoon(10, 7)).toBe(false);
    expect(isExpiringSoon(7, 7)).toBe(true);
  });
});

describe('resolveBrand', () => {
  it.each([
    ['okko', 'OKKO'],
    ['wog', 'WOG'],
    ['upg', 'UPG'],
    ['klo', 'KLO'],
    ['shell', 'Shell'],
    ['socar', 'Socar'],
  ])('resolves %s from "%s"', (expected, provider) => {
    expect(resolveBrand(provider)).toBe(expected);
  });

  it('is case-insensitive', () => {
    expect(resolveBrand('sHeLl')).toBe('shell');
  });

  it('matches inside a longer name, not only an exact one', () => {
    // Matched on a substring because providers do not arrive as clean ids.
    expect(resolveBrand('Shell Ukraine')).toBe('shell');
    expect(resolveBrand('WOG Retail')).toBe('wog');
  });

  it('matches Latin only, which is all the API emits', () => {
    // Verified against the backend: every `Provider = "..."` it writes is Latin
    // ("KLO", "OKKO", "wog"), and no Cyrillic provider exists. Adding Cyrillic
    // matching here would be speculative — it would look like support for a case
    // that cannot occur, and would need its own test to justify.
    expect(resolveBrand('ПАТ ОККО')).toBeNull();
  });

  it('returns null for an unknown provider rather than guessing', () => {
    // The caller falls back to the theme primary. Guessing a brand from a
    // near-miss would paint the wrong network's colour on the card.
    expect(resolveBrand('Приватбанк')).toBeNull();
    expect(resolveBrand('')).toBeNull();
    expect(resolveBrand(undefined)).toBeNull();
  });
});

describe('isWalletEmpty', () => {
  const zero: WalletCounts = {
    pendingOrders: 0,
    fulfilledOrders: 0,
    renewalOrders: 0,
    unassignedVouchers: 0,
    poolVouchers: 0,
    workersWithStock: 0,
    issuedToMe: 0,
    issuanceReceipts: 0,
  };

  const cases: [WalletSection, Partial<WalletCounts>][] = [
    ['personal', { pendingOrders: 1 }],
    ['personal', { fulfilledOrders: 1 }],
    ['personal', { renewalOrders: 1 }],
    ['personal', { unassignedVouchers: 1 }],
    ['company', { pendingOrders: 1 }],
    ['company', { poolVouchers: 1 }],
    ['company', { workersWithStock: 1 }],
    ['worker', { issuedToMe: 1 }],
    // A worker whose only fuel is inside handover receipts has a populated wallet:
    // the vouchers moved into receipts, they did not disappear.
    ['worker', { issuanceReceipts: 1, issuedToMe: 0 }],
  ];

  it.each(cases)('%s is not empty when %o', (section, counts) => {
    expect(isWalletEmpty(section, { ...zero, ...counts })).toBe(false);
  });

  it.each<WalletSection>(['personal', 'company', 'worker'])(
    '%s is empty when nothing belongs to it',
    (section) => {
      expect(isWalletEmpty(section, zero)).toBe(true);
    },
  );

  it("does not show a worker the employer's orders and stock", () => {
    // The point of the worker branch: fuel issued to someone else must not make
    // *this* worker's wallet look populated.
    expect(
      isWalletEmpty('worker', {
        ...zero,
        pendingOrders: 3,
        poolVouchers: 10,
        workersWithStock: 2,
      }),
    ).toBe(true);
  });

  it('does not treat a company with only assigned stock as empty', () => {
    // Fulfilled vouchers are assigned to orders, so they live against a worker
    // rather than in the pool. A company holding only assigned stock still has
    // stock.
    expect(isWalletEmpty('company', { ...zero, workersWithStock: 1 })).toBe(false);
  });

  it("does not count another section's vouchers against a personal wallet", () => {
    expect(isWalletEmpty('personal', { ...zero, poolVouchers: 5, issuedToMe: 5 })).toBe(true);
  });
});

describe('unusedLitres', () => {
  it('sums the amount of everything not used', () => {
    expect(
      unusedLitres([
        { status: 'active', amount: 40 },
        { status: 'used', amount: 50 },
        { status: 'active', amount: 25 },
      ]),
    ).toBe(65);
  });

  it('treats a missing amount as zero rather than NaN', () => {
    expect(unusedLitres([{ status: 'active' }, { status: 'active', amount: 10 }])).toBe(10);
  });

  it('is zero for an empty list', () => {
    expect(unusedLitres([])).toBe(0);
  });
});

describe('countUsed', () => {
  it('counts only the used ones', () => {
    expect(countUsed([{ status: 'used' }, { status: 'active' }, { status: 'used' }])).toBe(2);
  });

  it('is zero for an empty list', () => {
    expect(countUsed([])).toBe(0);
  });
});

describe('brandColorFor', () => {
  // Only the shape matters here: one brand colour and one fallback. Asserting on
  // the real palette would fail this test every time a brand colour is re-tuned,
  // which is not the thing being pinned.
  const tokens = {
    colors: {
      primary: '#ACTION',
      text: { brand: { okko: '#OKKO' } },
    },
  } as any;

  it('resolves a known provider to that brand colour', () => {
    expect(brandColorFor('okko', tokens)).toBe('#OKKO');
  });

  it('falls back to the action colour for an unknown provider', () => {
    // The important one. Guessing a nearby brand would paint the wrong network's
    // colour on the card, and a brand missing from the palette must not yield
    // undefined either — that renders as no colour at all.
    expect(brandColorFor('some-new-provider', tokens)).toBe('#ACTION');
    expect(brandColorFor('brand-not-in-palette', tokens)).toBe('#ACTION');
  });

  it('falls back for a missing provider rather than throwing', () => {
    expect(brandColorFor(null, tokens)).toBe('#ACTION');
    expect(brandColorFor(undefined, tokens)).toBe('#ACTION');
    expect(brandColorFor('', tokens)).toBe('#ACTION');
  });
});

describe('splitByIssuanceReceipt', () => {
  const receipt = (id: string, ...voucherIds: string[]) => ({
    id,
    vouchers: voucherIds.map((vid) => ({ id: vid })),
  });

  it('files each voucher under the receipt that delivered it', () => {
    const { receipts, vouchersInReceipts, loose } = splitByIssuanceReceipt(
      [receipt('r1', 'v1', 'v2'), receipt('r2', 'v3')],
      [{ id: 'v1' }, { id: 'v2' }, { id: 'v3' }],
    );

    expect(receipts).toHaveLength(2);
    expect(vouchersInReceipts.map((v) => v.id)).toEqual(['v1', 'v2', 'v3']);
    expect(loose).toEqual([]);
  });

  it('leaves fuel that belongs to no receipt loose rather than inventing one', () => {
    // Predates issuance orders, or arrived another way. Filing it under a handover
    // would claim a company handed it over when nobody recorded that.
    const { vouchersInReceipts, loose } = splitByIssuanceReceipt(
      [receipt('r1', 'v1')],
      [{ id: 'v1' }, { id: 'legacy' }],
    );

    expect(vouchersInReceipts.map((v) => v.id)).toEqual(['v1']);
    expect(loose.map((v) => v.id)).toEqual(['legacy']);
  });

  it('copes with a receipt carrying no vouchers and a null voucher list', () => {
    const { vouchersInReceipts, loose } = splitByIssuanceReceipt(
      [{ id: 'r1', vouchers: null }, { id: 'r2' } as never],
      [{ id: 'v1' }],
    );

    expect(vouchersInReceipts).toEqual([]);
    expect(loose.map((v) => v.id)).toEqual(['v1']);
  });
});

describe('isLiveWalletVoucher', () => {
  it('accepts the statuses fuel can still be refuelled with, whatever the casing', () => {
    expect(isLiveWalletVoucher({ status: 'Assigned' })).toBe(true);
    expect(isLiveWalletVoucher({ status: 'Available' })).toBe(true);
    expect(isLiveWalletVoucher({ status: 'active' })).toBe(true);
  });

  it('rejects spent, lapsed and unavailable fuel', () => {
    // These are the statuses that leave an order with nothing to show, which is what drops a spent
    // order out of the wallet instead of listing a dead card.
    ['Used', 'Expired', 'Blocked', 'Deactivated', 'Imported', 'VerificationFailed'].forEach(
      (status) => expect(isLiveWalletVoucher({ status })).toBe(false),
    );
  });

  it('rejects a missing status rather than treating it as live', () => {
    expect(isLiveWalletVoucher({ status: null })).toBe(false);
    expect(isLiveWalletVoucher({})).toBe(false);
  });
});
