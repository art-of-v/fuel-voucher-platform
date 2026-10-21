import React from 'react';
import { fireEvent, render, screen } from '@testing-library/react-native';

import { HubWorkersBranch } from './HubWorkersBranch';
import type { CompanyMemberDto } from '../types';
import type { Voucher } from '../../../core/types/api';

// The roster is where "how much does this person hold, and can I still thaw it" is
// answered, so this suite is about the two things a flat voucher list got wrong: the
// counters come from the vouchers rather than the API's `giftedVoucherCount`, and the
// fuel of a worker who no longer works here is filed under the company rather than
// under their name (planning #294).
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
    brand: 'OKKO',
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
    isReturningToPool: false,
    memberName,
    openGift: jest.fn(),
    confirmFire: jest.fn(),
    confirmRecall: jest.fn(),
    confirmBlock: jest.fn(),
    confirmReturnToPool: jest.fn(),
    ...overrides,
  };
  return { props, ...render(<HubWorkersBranch {...props} />) };
}

/** The roster is behind its own entry point; every test starts by opening it. */
function openRoster() {
  fireEvent.press(screen.getByText('company.members.section'));
}

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

  it("files a fired worker's frozen fuel under the company, not under their name", () => {
    const frozen = mkVoucher({
      id: 'v9',
      workerUserId: 'gone',
      workerFirstName: 'Oleksandr',
      workerLastName: null,
      status: 'blocked',
    });
    const { props } = renderBranch({ members: [], blocked: [frozen] });

    openRoster();
    // The company's group — not a roster row for somebody who no longer works here.
    expect(screen.getByText('company.hub.companyFuel')).toBeTruthy();
    expect(screen.getByText('company.block.section 1')).toBeTruthy();
    expect(screen.queryByText('Oleksandr')).toBeNull();

    // The fuel still has to be actionable, so the way back to the pool stays.
    fireEvent.press(screen.getByText('company.hub.companyFuel'));
    fireEvent.press(screen.getByText('company.recall.action'));
    expect(props.confirmReturnToPool).toHaveBeenCalledWith(expect.objectContaining({ id: 'v9' }));
  });

  it("offers the way back to the pool, and not the thaw, on a fired worker's fuel", () => {
    const frozen = mkVoucher({ id: 'v9', workerUserId: 'gone', status: 'blocked' });
    const { props } = renderBranch({ members: [], blocked: [frozen] });

    openRoster();
    fireEvent.press(screen.getByText('company.hub.companyFuel'));

    // A thaw hands the paper to whoever holds it — and that is the person who was just
    // fired, so it is not offered here (planning #558).
    expect(screen.queryByText('company.block.unblockAction')).toBeNull();
    expect(screen.getByText('company.recall.action')).toBeTruthy();
    expect(props.unblock).not.toHaveBeenCalled();
  });

  it('files the fuel a firing froze on the way out under the company too', () => {
    // The vouchers that were still active when the worker was fired became company fuel
    // and frozen at the same moment: no worker holds them and no door is open either.
    const frozenStock = mkVoucher({ id: 'v10', workerUserId: null, status: 'blocked' });
    const { props } = renderBranch({ members: [], blocked: [frozenStock] });

    openRoster();
    expect(screen.getByText('company.hub.companyFuel')).toBeTruthy();
    expect(screen.getByText('company.block.section 1')).toBeTruthy();

    fireEvent.press(screen.getByText('company.hub.companyFuel'));
    fireEvent.press(screen.getByText('company.recall.action'));
    expect(props.confirmReturnToPool).toHaveBeenCalledWith(expect.objectContaining({ id: 'v10' }));
  });

  it('still offers the thaw on a frozen voucher of a worker who works here', () => {
    const frozen = mkVoucher({ id: 'v11', workerUserId: 'w1', status: 'blocked' });
    const { props } = renderBranch({ members: [mkMember()], blocked: [frozen] });

    openRoster();
    fireEvent.press(screen.getByText('Ivan Petrov'));

    // The worker still works here, so the fuel can go back to them — and it can also go
    // back to the pool, which is the same word and the same fact.
    fireEvent.press(screen.getByText('company.block.unblockAction'));
    expect(props.unblock).toHaveBeenCalledWith('v11');

    fireEvent.press(screen.getByText('company.recall.action'));
    expect(props.confirmReturnToPool).toHaveBeenCalledWith(expect.objectContaining({ id: 'v11' }));
  });

  it('keeps the company fuel visible while the owner searches for a worker', () => {
    const frozen = mkVoucher({ id: 'v9', workerUserId: 'gone', status: 'blocked' });
    renderBranch({ members: [mkMember()], blocked: [frozen] });

    openRoster();
    fireEvent.changeText(screen.getByPlaceholderText('company.hub.searchPlaceholder'), 'zzzz');

    // It is a fact about the company, not a row about a person, so a name search
    // must not hide the fuel the owner still has to act on.
    expect(screen.getByText('company.hub.companyFuel')).toBeTruthy();
    expect(screen.getByText('company.hub.noWorkerMatches')).toBeTruthy();
  });

  it('does not put the fuel of a current worker into the company group', () => {
    renderBranch({ members: [mkMember()], gifted: [mkVoucher({ id: 'v1', status: 'active' })] });

    openRoster();

    expect(screen.queryByText('company.hub.companyFuel')).toBeNull();
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
