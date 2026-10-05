import React, { useState } from 'react';
import { fireEvent, render, screen } from '@testing-library/react-native';

import { IssueVoucherModal, type GiftTarget } from './IssueVoucherModal';
import type { CompanyMemberDto } from '../types';
import type { Voucher } from '../../../core/types/api';

// One sheet now serves two callers: the roster names a worker, the hub's orders branch
// points it at fuel and has nobody to name. This suite guards the seam between them —
// that the roster's sheet is untouched, and that fuel cannot be issued to nobody.
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
    giftedVoucherCount: 0,
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
    legalEntityId: 'e1',
    workerUserId: null,
    ...overrides,
  };
}

function memberName(m: CompanyMemberDto): string {
  return [m.workerFirstName, m.workerLastName].filter(Boolean).join(' ').trim();
}

/**
 * A real state setter, because the orders flow writes the chosen recipient back into
 * the target — a stub callback could not show the sheet becoming the roster's again.
 */
function renderSheet(props: {
  giftTarget: GiftTarget;
  selected?: string[];
  members?: CompanyMemberDto[];
  giftable?: Voucher[];
  gift?: (workerUserId: string, voucherIds: string[]) => void;
}) {
  const gift = props.gift ?? jest.fn();
  function Harness() {
    const [giftTarget, setGiftTarget] = useState<GiftTarget | null>(props.giftTarget);
    const giftable = props.giftable ?? [mkVoucher({ id: 'pool' })];
    return (
      <IssueVoucherModal
        members={props.members ?? [mkMember()]}
        giftable={giftable}
        giftGroups={giftable.map((v) => ({ provider: v.provider, items: [v] }))}
        gift={gift}
        isGifting={false}
        allGiftableSelected={giftable.every((v) => (props.selected ?? []).includes(v.id))}
        giftTarget={giftTarget}
        setGiftTarget={setGiftTarget}
        selected={new Set(props.selected ?? ['pool'])}
        toggleSelectAll={jest.fn()}
        toggleSelected={jest.fn()}
        memberName={memberName}
      />
    );
  }
  render(<Harness />);
  return { gift };
}

describe('IssueVoucherModal', () => {
  it('leaves the roster flow as it was: a named worker, no recipient to pick', () => {
    const { gift } = renderSheet({
      giftTarget: { workerUserId: 'w1', label: 'Ivan Petrov' },
    });

    expect(screen.getByText('company.gift.subtitle')).toBeTruthy();
    // Picking a recipient here would be asking the owner a question they already answered.
    expect(screen.queryByText('company.members.section')).toBeNull();

    fireEvent.press(screen.getByText('company.gift.confirm'));

    expect(gift).toHaveBeenCalledWith('w1', ['pool']);
  });

  it('asks who the fuel is for, and issues to nobody until one is picked', () => {
    const { gift } = renderSheet({ giftTarget: { workerUserId: null, label: '' } });

    // A sheet naming nobody would read as a bug, so it names the fuel's state instead.
    expect(screen.getByText('company.hub.undistributedShort')).toBeTruthy();
    expect(screen.getByText('company.members.section')).toBeTruthy();

    fireEvent.press(screen.getByText('company.gift.confirm'));
    expect(gift).not.toHaveBeenCalled();

    fireEvent.press(screen.getByText('Ivan Petrov'));

    // Picked: the picker is gone and this is the roster's sheet again.
    expect(screen.queryByText('company.members.section')).toBeNull();
    expect(screen.getByText('company.gift.subtitle')).toBeTruthy();

    fireEvent.press(screen.getByText('company.gift.confirm'));
    expect(gift).toHaveBeenCalledWith('w1', ['pool']);
  });

  it('says so, rather than failing, when there is no worker to issue to', () => {
    const { gift } = renderSheet({ giftTarget: { workerUserId: null, label: '' }, members: [] });

    expect(screen.getByText('company.members.empty')).toBeTruthy();
    // The button is still there, but with nobody to issue to it can only lie.
    fireEvent.press(screen.getByText('company.gift.confirm'));
    expect(gift).not.toHaveBeenCalled();
  });

  it('will not issue a named worker nothing', () => {
    const { gift } = renderSheet({
      giftTarget: { workerUserId: 'w1', label: 'Ivan Petrov' },
      selected: [],
    });

    fireEvent.press(screen.getByText('company.gift.confirmZero'));

    expect(gift).not.toHaveBeenCalled();
  });
});
