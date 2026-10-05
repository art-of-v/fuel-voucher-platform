import React from 'react';
import { fireEvent, render, screen } from '@testing-library/react-native';

import { HubWorkersBranch, rosterMembers } from './HubWorkersBranch';
import type { CompanyMemberDto } from '../types';
import type { Voucher } from '../../../core/types/api';

// The roster is where "how much does this person hold, and can I still thaw it" is
// answered, so this suite is about the two things a flat voucher list got wrong: the
// counters come from the vouchers rather than the API's `giftedVoucherCount`, and a
// worker who still holds fuel after being fired keeps a row — without it their frozen
// vouchers would be unreachable from every screen.
jest.mock('../../../core/i18n', () => ({
  __esModule: true,
  useI18n: () => ({ t: (key: string) => key, language: 'uk', setLanguage: jest.fn() }),
}));

function mkMember(overrides: Partial<CompanyMemberDto> = {}): CompanyMemberDto {
  return {
    id: 'm1',
    workerUserId: 'w1',
    workerPhoneNumber: '+380501111111',
    workerFirstName: 'Ivan',
    workerLastName: 'Petrov',
    joinedAtUtc: '2026-01-01T00:00:00Z',
    giftedVoucherCount: 1,
    ...overrides,
  };
}

function mkVoucher(overrides: Partial<Voucher> = {}): Voucher {
  return {
    id: 'v1',
    provider: 'OKKO',
    fuelType: 'a95',
    amount: 10,
    status: 'active',
    legalEntityId: null,
    workerUserId: 'w1',
    ...overrides,
  };
}

function memberName(m: CompanyMemberDto): string {
  return [m.workerFirstName, m.workerLastName].filter(Boolean).join(' ').trim();
}

function renderBranch(overrides: Partial<React.ComponentProps<typeof HubWorkersBranch>> = {}) {
  const props = {
    members: [mkMember()],
    gifted: [] as Voucher[],
    blocked: [] as Voucher[],
    unblock: jest.fn(),
    isFiring: false,
    isBlocking: false,
    isRecalling: false,
    isUnblocking: false,
    memberName,
    openGift: jest.fn(),
    confirmFire: jest.fn(),
    confirmRecall: jest.fn(),
    confirmBlock: jest.fn(),
    ...overrides,
  };
  return { props, ...render(<HubWorkersBranch {...props} />) };
}

/** The roster is behind its own entry point; every test starts by opening it. */
function openRoster() {
  fireEvent.press(screen.getByText('company.members.section'));
}

describe('rosterMembers', () => {
  it('gives a fired worker who still holds fuel a row of their own', () => {
    const rows = rosterMembers(
      [],
      [mkVoucher({ workerUserId: 'gone', workerFirstName: 'Oleksandr' })],
    );

    expect(rows).toHaveLength(1);
    expect(rows[0]).toMatchObject({ workerUserId: 'gone', workerFirstName: 'Oleksandr' });
    // Namespaced so a synthesized row can never collide with a membership id.
    expect(rows[0].id).not.toBe('gone');
  });

  it('does not invent a second row for someone who is still a member', () => {
    const rows = rosterMembers([mkMember()], [mkVoucher({ workerUserId: 'w1' })]);

    expect(rows).toHaveLength(1);
    expect(rows[0].id).toBe('m1');
  });
});

describe('HubWorkersBranch', () => {
  it('counts what each worker holds from the vouchers, not from giftedVoucherCount', () => {
    renderBranch({
      gifted: [
        mkVoucher({ id: 'v1', status: 'active', amount: 10 }),
        mkVoucher({ id: 'v2', status: 'active', amount: 5 }),
        mkVoucher({ id: 'v3', status: 'used' }),
      ],
      blocked: [mkVoucher({ id: 'v4', status: 'blocked', amount: 3 })],
    });

    openRoster();

    expect(
      screen.getByText(
        'codes.stock.issuedShort 2 · codes.stock.leftShort 15 common.liter · codes.stock.usedShort 1 · company.block.section 1',
      ),
    ).toBeTruthy();
  });

  it('opens the issue-voucher modal for the worker whose row was pressed', () => {
    const member = mkMember();
    const { props } = renderBranch({ members: [member], gifted: [mkVoucher()] });

    openRoster();
    fireEvent.press(screen.getByText('company.members.gift'));

    expect(props.openGift).toHaveBeenCalledWith(member);
  });

  it('offers freeze and recall on an assigned voucher and nothing on a spent one', () => {
    const { props } = renderBranch({
      gifted: [mkVoucher({ id: 'v1', status: 'active' }), mkVoucher({ id: 'v2', status: 'used' })],
    });

    openRoster();
    fireEvent.press(screen.getByText('Ivan Petrov'));

    fireEvent.press(screen.getByText('company.block.action'));
    expect(props.confirmBlock).toHaveBeenCalledWith(expect.objectContaining({ id: 'v1' }));

    fireEvent.press(screen.getByText('company.recall.action'));
    expect(props.confirmRecall).toHaveBeenCalledWith(expect.objectContaining({ id: 'v1' }));

    // The spent voucher explains itself instead of offering an action the server rejects.
    expect(screen.getAllByText('company.recall.spentAction')).toHaveLength(1);
  });

  it('keeps a fired worker reachable so their frozen fuel can still be thawed', () => {
    const frozen = mkVoucher({
      id: 'v9',
      workerUserId: 'gone',
      workerFirstName: 'Oleksandr',
      workerLastName: null,
      status: 'blocked',
    });
    const { props } = renderBranch({ members: [], blocked: [frozen] });

    openRoster();
    expect(screen.getByText('Oleksandr')).toBeTruthy();
    // Firing deleted the membership, so the membership actions are gone with it.
    expect(screen.queryByText('company.members.fire')).toBeNull();

    // The thaw lives inside the drill-down, exactly like it did in the blocked list.
    fireEvent.press(screen.getByText('Oleksandr'));
    fireEvent.press(screen.getByText('company.block.unblockAction'));
    expect(props.unblock).toHaveBeenCalledWith('v9');
  });

  it('searches by phone number as well as by name', () => {
    renderBranch({
      members: [
        mkMember(),
        mkMember({
          id: 'm2',
          workerUserId: 'w2',
          workerFirstName: 'Oleksandr',
          workerLastName: null,
          workerPhoneNumber: '+380509999999',
        }),
      ],
    });

    openRoster();
    fireEvent.changeText(screen.getByPlaceholderText('company.hub.searchPlaceholder'), '509999999');

    expect(screen.queryByText('Ivan Petrov')).toBeNull();
    expect(screen.getByText('Oleksandr')).toBeTruthy();
  });

  it('says so when the search matches nobody', () => {
    renderBranch({ members: [mkMember()] });

    openRoster();
    fireEvent.changeText(screen.getByPlaceholderText('company.hub.searchPlaceholder'), 'zzzz');

    expect(screen.getByText('company.hub.noWorkerMatches')).toBeTruthy();
  });

  it('disables a fire in flight', () => {
    const { props } = renderBranch({ isFiring: true });

    openRoster();
    fireEvent.press(screen.getByText('company.members.fire'));

    expect(props.confirmFire).not.toHaveBeenCalled();
  });
});
