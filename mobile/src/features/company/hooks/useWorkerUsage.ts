import { useQuery } from '@tanstack/react-query';
import { getWorkerUsage, type WorkerUsageReport } from '../api/workerUsageApi';

/**
 * The worker usage report for the active company (epic #103 S5, #150).
 *
 * Only runs in a worker context: an owner already has the stock and P&L views, and the endpoint
 * would answer with the owner's own (empty) issuance. `enabled` is what keeps that from becoming a
 * pointless request on every owner screen render.
 */
export function useWorkerUsage(legalEntityId: string | null, enabled: boolean) {
  const query = useQuery<WorkerUsageReport>({
    queryKey: ['company', 'worker-usage', legalEntityId],
    queryFn: () => getWorkerUsage(legalEntityId as string),
    enabled: enabled && !!legalEntityId,
    retry: false,
  });

  return {
    report: query.data ?? null,
    isLoading: query.isLoading,
    isError: query.isError,
    error: query.error,
  };
}
