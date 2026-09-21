import { classifyVoucher, type Voucher } from './api';

// classifyVoucher decides what a user may do with a voucher (see COMPANY_WORKERS.md
// §5). It is a security-relevant guard: the my-codes screen hides the "mark used"
// action based on its result, and useMyCodes re-checks it before redeeming (a
// 'blocked' or 'gifted_to_worker' voucher must never be redeemable by the viewer).
// The precedence between the rules is the part worth pinning down in tests.

// Minimal builder — only the three fields classifyVoucher actually reads.
function voucher(overrides: Partial<Voucher> = {}): Voucher {
  return {
    id: 'v1',
    provider: 'okko',
    fuelType: 'a95',
    amount: 10,
    status: 'active',
    legalEntityId: null,
    workerUserId: null,
    ...overrides,
  };
}

describe('classifyVoucher', () => {
  it('classifies a voucher with no legal entity as personal', () => {
    expect(classifyVoucher(voucher(), 'user-1')).toBe('personal');
  });

  it('classifies a company voucher with no assigned worker as company_pool', () => {
    expect(
      classifyVoucher(voucher({ legalEntityId: 'company-1' }), 'user-1'),
    ).toBe('company_pool');
  });

  it('classifies a company voucher assigned to the current user as gifted_to_me', () => {
    expect(
      classifyVoucher(
        voucher({ legalEntityId: 'company-1', workerUserId: 'user-1' }),
        'user-1',
      ),
    ).toBe('gifted_to_me');
  });

  it('classifies a company voucher assigned to another worker as gifted_to_worker', () => {
    expect(
      classifyVoucher(
        voucher({ legalEntityId: 'company-1', workerUserId: 'user-2' }),
        'user-1',
      ),
    ).toBe('gifted_to_worker');
  });

  describe('blocked status takes precedence over every other rule', () => {
    it('is blocked even when it would otherwise be personal', () => {
      expect(classifyVoucher(voucher({ status: 'blocked' }), 'user-1')).toBe(
        'blocked',
      );
    });

    it('is blocked even when assigned to the current user', () => {
      expect(
        classifyVoucher(
          voucher({
            status: 'blocked',
            legalEntityId: 'company-1',
            workerUserId: 'user-1',
          }),
          'user-1',
        ),
      ).toBe('blocked');
    });

    it('matches status case-insensitively (backend may send "BLOCKED")', () => {
      expect(classifyVoucher(voucher({ status: 'BLOCKED' }), 'user-1')).toBe(
        'blocked',
      );
    });
  });

  describe('current user id is absent', () => {
    // A voucher assigned to *some* worker must never resolve to gifted_to_me when
    // we don't know who is viewing — that would hand another worker's voucher to
    // an unauthenticated/unknown caller.
    it('does not treat an assigned voucher as gifted_to_me when userId is undefined', () => {
      expect(
        classifyVoucher(
          voucher({ legalEntityId: 'company-1', workerUserId: 'user-2' }),
        ),
      ).toBe('gifted_to_worker');
    });

    it('does not treat an assigned voucher as gifted_to_me when userId is null', () => {
      expect(
        classifyVoucher(
          voucher({ legalEntityId: 'company-1', workerUserId: 'user-1' }),
          null,
        ),
      ).toBe('gifted_to_worker');
    });
  });
});
