import React from 'react';
import { Image } from 'react-native';
import { render, screen, fireEvent } from '@testing-library/react-native';

import { VoucherDetailModal } from './VoucherDetailModal';
import type { Voucher } from '../../../core/types/api';

// i18n: echo the key, but honour positional {0}/{1} like the real t() so the
// valid-range line is asserted the way a user would read it.
jest.mock('../../../core/i18n', () => ({
  useI18n: () => ({
    t: (key: string, ...params: string[]) => {
      let out = key;
      params.forEach((p, i) => (out = out.replace(new RegExp(`\\{${i}\\}`, 'g'), p)));
      return out;
    },
    language: 'en',
    setLanguage: jest.fn(),
  }),
}));

// expo-blur renders a native view in tests; stub to a plain view.
jest.mock('expo-blur', () => ({ BlurView: 'BlurView' }));

function makeVoucher(overrides: Partial<Voucher> = {}): Voucher {
  return {
    id: 'v1',
    brand: 'okko',
    fuelType: 'a95',
    fuelName: 'A95',
    amount: 10,
    status: 'active',
    legalEntityId: null,
    workerUserId: null,
    expirationDate: '2026-11-12',
    ...overrides,
  };
}

const noop = () => {};

describe('VoucherDetailModal history', () => {
  it('shows no History button when the voucher has no history', () => {
    render(
      <VoucherDetailModal
        visible
        voucher={makeVoucher({ history: [] })}
        onClose={noop}
        onToggleUsed={noop}
        brandColor="#123456"
      />,
    );
    expect(screen.queryByText('voucher.history.title')).toBeNull();
  });

  it('reveals the purchase+renewal timeline when History is tapped', () => {
    const voucher = makeVoucher({
      history: [
        { type: 'Purchase', date: '2026-01-01T00:00:00Z', liters: 10, amount: 500 },
        {
          type: 'Renewal',
          date: '2026-02-01T00:00:00Z',
          liters: 10,
          amount: 300,
          validFrom: '2026-01-08',
          validTo: '2026-02-08',
          termCode: '1m',
        },
      ],
    });

    render(
      <VoucherDetailModal
        visible
        voucher={voucher}
        onClose={noop}
        onToggleUsed={noop}
        brandColor="#123456"
      />,
    );

    // The timeline is collapsed until the button is pressed.
    expect(screen.queryByText(/voucher\.history\.purchased/)).toBeNull();

    fireEvent.press(screen.getByTestId('voucher-history-toggle'));

    expect(screen.getByText(/voucher\.history\.purchased/)).toBeTruthy();
    expect(screen.getByText(/voucher\.history\.renewed/)).toBeTruthy();
  });
});

/**
 * A report that waits for supplier evidence is not a spendable voucher: the QR must not be on
 * screen and the only action left is the holder's undo.
 */
describe('VoucherDetailModal pending reconciliation', () => {
  it('shows the waiting state and no QR', () => {
    render(
      <VoucherDetailModal
        visible
        voucher={makeVoucher({
          status: 'pendingreconciliation',
          imageUrl: 'data:image/png;base64,QR',
        })}
        onClose={noop}
        onToggleUsed={noop}
        brandColor="#123456"
      />,
    );

    expect(screen.getByText('voucher.pending.awaitingConfirmation')).toBeTruthy();
    expect(screen.getByText('codes.pendingReconciliation')).toBeTruthy();
    // No picture of the code: presenting it again would invite a second dispense.
    expect(screen.UNSAFE_queryByType(Image)).toBeNull();
  });

  it('offers the holder the undo, not a second redemption', () => {
    const onToggleUsed = jest.fn();
    render(
      <VoucherDetailModal
        visible
        voucher={makeVoucher({ status: 'pendingreconciliation' })}
        onClose={noop}
        onToggleUsed={onToggleUsed}
        brandColor="#123456"
      />,
    );

    expect(screen.getByText('codes.withdrawReport')).toBeTruthy();
    expect(screen.queryByText('codes.markAsUsed')).toBeNull();

    fireEvent.press(screen.getByText('codes.withdrawReport'));
    expect(onToggleUsed).toHaveBeenCalledTimes(1);
  });
});
