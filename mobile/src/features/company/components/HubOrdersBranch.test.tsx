import React from 'react';
import { act, fireEvent, render, screen, within } from '@testing-library/react-native';

import { HubOrdersBranch } from './HubOrdersBranch';
import type { Order, Voucher } from '../../../core/types/api';

// The orders branch exists to answer one question per order — "what did it deliver that
// nobody holds yet?" — so this suite is about the three ways that answer used to be
// wrong: a two-brand order filed under one brand, a handed-over voucher counted as the
// company's, and an issue button that could only come back as a conflict.
jest.mock('../../../core/i18n', () => ({
  __esModule: true,
  useI18n: () => ({ t: (key: string) => key, language: 'uk', setLanguage: jest.fn() }),
}));

// `OrderCard` wraps itself in the reanimated swipe reveal, which reaches for the
// worklets native module and cannot load under Jest. The hub never enables it: the
// branch passes no `onPay`/`onDelete`, so `canSwipe` is false and the swipe wrapper
// contributes nothing but a host view here. Stubbing just that wrapper keeps the real
// card — and the toggles the branch drives — under test.
jest.mock('react-native-gesture-handler/ReanimatedSwipeable', () => {
  const React = require('react');
  const { View } = require('react-native');
  const Swipeable = React.forwardRef(function Swipeable(
    { children }: { children: React.ReactNode },
    ref: unknown,
  ) {
    return React.createElement(View, { ref }, children);
  });
  return { __esModule: true, default: Swipeable };
});

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

function mkOrder(overrides: Partial<Order> = {}): Order {
  return {
    id: 'order-1',
    provider: 'OKKO',
    fuelType: 'a95',
    liters: 10,
    quantity: 1,
    price: 100,
    status: 'FULFILLED',
    createdAt: '2026-01-01T00:00:00Z',
    fulfilledAt: '2026-01-02T00:00:00Z',
    legalEntityId: 'e1',
    lineItems: [{ id: 'l1', provider: 'OKKO', fuelTypeId: 'a95', liters: 10, quantity: 1 }],
    ...overrides,
  };
}

/** The order header the card renders, which is also what a tap there toggles. */
function orderCardLabel(order: Order): string {
  return `ID: ${order.id.slice(0, 10).toUpperCase()}`;
}

function renderBranch(
  overrides: Partial<React.ComponentProps<typeof HubOrdersBranch>> = {},
): ReturnType<typeof render> & { props: React.ComponentProps<typeof HubOrdersBranch> } {
  const props: React.ComponentProps<typeof HubOrdersBranch> = {
    companyOrders: [mkOrder()],
    giftable: [],
    onIssue: jest.fn(),
    ...overrides,
  };
  return { props, ...render(<HubOrdersBranch {...props} />) };
}

/** The branch is behind its own entry point; every test starts by opening it. */
function openBranch() {
  fireEvent.press(screen.getByText('codes.orders'));
}

function openBrand(brand: string) {
  fireEvent.press(screen.getByText(brand));
}

function openOrder(order: Order) {
  fireEvent.press(screen.getByText(orderCardLabel(order)));
  // `OrderCard` springs its body open on the JS driver, so the open animation has to be
  // finished inside act — otherwise its timer keeps updating state after the test ends.
  act(() => {
    jest.advanceTimersByTime(1000);
  });
}

/** The fuel block the branch draws under an open order. */
function undistributedBlock() {
  return within(screen.getByTestId('hub-orders-undistributed'));
}

describe('HubOrdersBranch', () => {
  beforeEach(() => {
    jest.useFakeTimers();
  });

  afterEach(() => {
    jest.useRealTimers();
  });

  it('files an order that bought from two brands under both of them', () => {
    const order = mkOrder({
      provider: 'OKKO',
      lineItems: [
        { id: 'l1', provider: 'OKKO', fuelTypeId: 'a95', liters: 50, quantity: 1 },
        { id: 'l2', provider: 'WOG', fuelTypeId: 'a95', liters: 50, quantity: 1 },
      ],
    });
    renderBranch({ companyOrders: [order] });

    openBranch();
    expect(screen.getByText('OKKO')).toBeTruthy();
    expect(screen.getByText('WOG')).toBeTruthy();

    // An order hidden under the wrong brand is worse than one shown twice: the owner
    // thinks in brands, and half the fuel they bought would otherwise be unreachable.
    openBrand('OKKO');
    openBrand('WOG');
    expect(screen.getAllByText(orderCardLabel(order))).toHaveLength(2);
  });

  it('shows the fuel no worker holds and leaves out the voucher a worker took', () => {
    const order = mkOrder({
      vouchers: [
        mkVoucher({ id: 'pool', amount: 10, externalId: 'POOL-1', workerUserId: null }),
        mkVoucher({
          id: 'held',
          amount: 90,
          externalId: 'HELD-1',
          workerUserId: 'w1',
          workerFirstName: 'Ivan',
        }),
      ],
    });
    renderBranch({ companyOrders: [order] });

    openBranch();
    openBrand('OKKO');
    openOrder(order);

    const undistributed = undistributedBlock();
    expect(undistributed.getByText('POOL-1')).toBeTruthy();
    // Handed over is somebody else's fuel now: the order still lists it, the block
    // under the order must not offer it as the company's.
    expect(undistributed.queryByText('HELD-1')).toBeNull();
    // Litres come from the whole undistributed set, so the owner sees 10 L, not 100 L.
    expect(
      undistributed.getByText('company.hub.undistributedShort · 10 common.liter'),
    ).toBeTruthy();
  });

  it('says nothing extra about an order whose fuel is all handed over', () => {
    const order = mkOrder({
      vouchers: [mkVoucher({ id: 'held', workerUserId: 'w1', externalId: 'HELD-1' })],
    });
    renderBranch({ companyOrders: [order] });

    openBranch();
    openBrand('OKKO');
    openOrder(order);

    expect(screen.queryByTestId('hub-orders-undistributed')).toBeNull();
    expect(screen.queryByText('company.members.gift')).toBeNull();
  });

  it('issues the fuel from an order, and only the fuel the server would accept', () => {
    const order = mkOrder({
      vouchers: [
        mkVoucher({ id: 'pool', amount: 10, workerUserId: null }),
        // No worker holds a redeemed company voucher either, so the branch counts it as
        // undistributed while the gift endpoint rejects it.
        mkVoucher({ id: 'spent', amount: 20, workerUserId: null, status: 'used' }),
      ],
    });
    const { props } = renderBranch({
      companyOrders: [order],
      giftable: [mkVoucher({ id: 'pool' })],
    });

    openBranch();
    openBrand('OKKO');
    openOrder(order);

    fireEvent.press(screen.getByText('company.members.gift'));

    expect(props.onIssue).toHaveBeenCalledWith(['pool']);
  });

  it('offers no issue button for an order with nothing the server would take', () => {
    const order = mkOrder({
      vouchers: [mkVoucher({ id: 'spent', workerUserId: null, status: 'used' })],
    });
    renderBranch({ companyOrders: [order] });

    openBranch();
    openBrand('OKKO');
    openOrder(order);

    // The fuel is listed (it is the company's), but a button here could only fail.
    expect(screen.getByTestId('hub-orders-undistributed')).toBeTruthy();
    expect(screen.queryByText('company.members.gift')).toBeNull();
  });

  it('issues a single voucher when one is tapped', () => {
    const order = mkOrder({
      vouchers: [mkVoucher({ id: 'pool', externalId: 'POOL-1', workerUserId: null })],
    });
    const { props } = renderBranch({
      companyOrders: [order],
      giftable: [mkVoucher({ id: 'pool' })],
    });

    openBranch();
    openBrand('OKKO');
    openOrder(order);

    // The order's own list also names POOL-1, so the tap has to be scoped to the block
    // the branch draws — the same card, opened from here rather than from the receipt.
    fireEvent.press(undistributedBlock().getByText('POOL-1'));

    expect(props.onIssue).toHaveBeenCalledWith(['pool']);
  });

  it('counts the company orders on the entry point and stays closed until pressed', () => {
    renderBranch({ companyOrders: [mkOrder(), mkOrder({ id: 'order-2' })] });

    expect(screen.getByText('2')).toBeTruthy();
    // Collapsed: the branch must not cost a list of brands before it is asked for.
    expect(screen.queryByText('OKKO')).toBeNull();
  });

  it('says so when the company has no orders', () => {
    renderBranch({ companyOrders: [] });

    openBranch();

    expect(screen.getByText('company.hub.noOrders')).toBeTruthy();
  });
});
