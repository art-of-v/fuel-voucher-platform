import type { ReactNode } from 'react';
import type { Voucher } from '../../../core/types/api';
import type { WorkerUsageReport } from '../../company/api/workerUsageApi';
import { WorkerFuelHeader, WorkerUsageSection } from '../../company/components';
// Through the barrel, like the screen imported it: that is the module its test replaces.
import { WalletSummaryBar } from '.';

type CodesWalletHeaderProps = {
  isWorkerContext: boolean;
  currentCompany: { name?: string | null } | null;
  workerIssued: Voucher[];
  workerUsedCount: number;
  workerLeftCount: number;
  workerPendingCount: number;
  workerLitersLeft: number;
  usageReport: WorkerUsageReport | null;
  usageLoading: boolean;
  ordersCount: number;
  fulfilledCount: number;
  pendingCount: number;
  /** The spent-fuel chip, rendered by the screen so both header and empty state share it. */
  spentFilter: ReactNode;
};

/**
 * The wallet header, extracted from `app/my-codes.tsx` unchanged (planning #807).
 *
 * Worker context (multi-company epic #103 S5): the fuel this company issued to me - issued /
 * used / remaining / pending counters, then the handover receipts. The counters cover every
 * voucher the worker holds, whether it sits inside a receipt or predates them, so the header
 * never disagrees with the list.
 *
 * A pending voucher is counted on its own: its fuel is already dispensed and waits for the
 * supplier's evidence, so it is neither used nor still in the worker's hands.
 *
 * Every other context gets the three-count summary bar plus the spent-fuel chip instead.
 */
export function CodesWalletHeader({
  isWorkerContext,
  currentCompany,
  workerIssued,
  workerUsedCount,
  workerLeftCount,
  workerPendingCount,
  workerLitersLeft,
  usageReport,
  usageLoading,
  ordersCount,
  fulfilledCount,
  pendingCount,
  spentFilter,
}: CodesWalletHeaderProps) {
  return isWorkerContext ? (
    <>
      <WorkerFuelHeader
        currentCompany={currentCompany}
        workerIssued={workerIssued}
        workerUsedCount={workerUsedCount}
        workerLeftCount={workerLeftCount}
        workerPendingCount={workerPendingCount}
        workerLitersLeft={workerLitersLeft}
      />
      <WorkerUsageSection report={usageReport} isLoading={usageLoading} />
    </>
  ) : (
    <>
      <WalletSummaryBar
        ordersCount={ordersCount}
        fulfilledCount={fulfilledCount}
        pendingCount={pendingCount}
      />
      {spentFilter}
    </>
  );
}
