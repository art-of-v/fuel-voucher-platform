import React from 'react';
import { render, screen, fireEvent } from '@testing-library/react-native';

import { VoucherDetailModal } from './VoucherDetailModal';
import type { Voucher } from '../core/types/api';

// i18n: echo the key, but honour positional {0}/{1} like the real t() so the
// valid-range line is asserted the way a user would read it.
jest.mock('../core/i18n', () => ({
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
    provider: 'okko',
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
