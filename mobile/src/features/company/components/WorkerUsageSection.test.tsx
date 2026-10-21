import React from 'react';
import { render, screen } from '@testing-library/react-native';

import { WorkerUsageSection } from './WorkerUsageSection';
import type { WorkerUsageItem, WorkerUsageReport } from '../api/workerUsageApi';

/**
 * The usage report is what the header's counts cannot answer: litres, and when (#150 W2).
 *
 * The dates are the point of this suite. They come from the server's `issuedAtUtc` /
 * `usedAtUtc` columns, not from anything the client derives, because a derived date moves when
 * the owner touches the voucher - which is the exact failure those columns exist to prevent. So
 * the suite pins three things: the litres, the dates as the server sent them, and the rule that
 * a voucher with no date at all is not listed with an empty one beside a real one.
 */

jest.mock('../../../core/i18n', () => ({
  __esModule: true,
  useI18n: () => ({ t: (key: string) => key, language: 'uk', setLanguage: jest.fn() }),
}));

function item(overrides: Partial<WorkerUsageItem> = {}): WorkerUsageItem {
  return {
    id: 'w1',
    brand: 'OKKO',
    fuelTypeId: 'a95',
    voucherNumber: 'E45C4796',
    liters: 10,
    status: 'Assigned',
    expirationDate: '2026-12-31T00:00:00Z',
    issuedAtUtc: '2026-10-12T08:30:00Z',
    usedAtUtc: null,
    isUsed: false,
    ...overrides,
  };
}

function report(overrides: Partial<WorkerUsageReport> = {}): WorkerUsageReport {
  return {
    legalEntityId: 'company-1',
    totals: {
      count: 1,
      litersReceived: 10,
      litersUsed: 0,
      litersRemaining: 10,
      countUsed: 0,
      countRemaining: 1,
    },
    items: [item()],
    ...overrides,
  };
}

describe('WorkerUsageSection', () => {
  it('renders nothing when there is no fuel', () => {
    const { toJSON } = render(
      <WorkerUsageSection
        report={report({ totals: { ...report().totals, count: 0 }, items: [] })}
        isLoading={false}
      />,
    );

    // An empty section would only take room away from the list the worker came for.
    expect(toJSON()).toBeNull();
  });

  it('renders nothing while the report is loading', () => {
    const { toJSON } = render(<WorkerUsageSection report={null} isLoading />);

    expect(toJSON()).toBeNull();
  });

  it('shows litres received, used and left', () => {
    render(<WorkerUsageSection report={report()} isLoading={false} />);

    expect(screen.getByText('company.usage.title')).toBeTruthy();
    expect(screen.getByText('codes.stock.issuedShort')).toBeTruthy();
    expect(screen.getByText('codes.stock.usedShort')).toBeTruthy();
    expect(screen.getByText('codes.stock.leftShort')).toBeTruthy();
    // 10 received, 0 used, 10 left. Litres, not counts: the header above has the counts.
    expect(screen.getAllByText('10')).toHaveLength(2);
    expect(screen.getByText('0')).toBeTruthy();
  });

  it('shows the day the fuel was received on', () => {
    render(<WorkerUsageSection report={report()} isLoading={false} />);

    // Day precision, local time: a report answers "which month did I burn this in", and the
    // time of day would invite reading the stamp as a redemption timestamp.
    expect(screen.getByText(/company\.usage\.received \d{2}\.\d{2}\.\d{4}/)).toBeTruthy();
  });

  it('shows the day the fuel was used on, when it was', () => {
    render(
      <WorkerUsageSection
        report={report({ items: [item({ usedAtUtc: '2026-10-14T09:00:00Z', isUsed: true })] })}
        isLoading={false}
      />,
    );

    expect(screen.getByText(/company\.usage\.used \d{2}\.\d{2}\.\d{4}/)).toBeTruthy();
  });

  it('does not list a voucher that carries no date at all', () => {
    render(
      <WorkerUsageSection
        report={report({
          items: [item({ id: 'w2', voucherNumber: 'OLD-ONE', issuedAtUtc: null, usedAtUtc: null })],
        })}
        isLoading={false}
      />,
    );

    // An empty date beside a real one reads as a fuel that was received on a day that never
    // happened. The row is not listed at all.
    expect(screen.queryByText(/OLD-ONE/)).toBeNull();
  });

  it('says the received date is unknown rather than leaving it blank', () => {
    render(
      <WorkerUsageSection
        report={report({
          items: [item({ issuedAtUtc: null, usedAtUtc: '2026-10-14T09:00:00Z', isUsed: true })],
        })}
        isLoading={false}
      />,
    );

    // The row reads "received <date> · used <date>" as one line, so the unknown marker is
    // matched as a fragment rather than as the whole line.
    expect(screen.getByText(/company\.usage\.receivedUnknown/)).toBeTruthy();
  });
});
