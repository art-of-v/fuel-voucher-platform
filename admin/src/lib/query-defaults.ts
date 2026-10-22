import { QueryClient } from "@tanstack/react-query";
import { apiRequest } from "./api-client";

/**
 * Fallback for any `useQuery` whose `queryKey[0]` is an API path.
 *
 * <p>
 * It used to be a bare `fetch` with the bearer header stapled on. That path had
 * none of the guarantees `apiRequest` provides: no timeout, no retry, and no
 * recovery on a 401. So an access token that expired simply emptied the view —
 * the contracts tab, the reconciliation counts, the voucher imports — with no
 * refresh, no error and no way for the operator to tell the difference between
 * "no data" and "not allowed to read it". Delegating to `apiRequest` means a
 * query that names a path in its key behaves like every other admin call.
 * </p>
 */
export const defaultQueryFn = async ({
  queryKey,
}: {
  queryKey: readonly unknown[];
}) => apiRequest("GET", queryKey[0] as string);

export const queryClient = new QueryClient({
  defaultOptions: {
    queries: {
      queryFn: defaultQueryFn,
      // Admin data changes arrive out-of-band (mobile purchases, other admins, background
      // jobs), so every page needs to converge without a manual browser reload. Poll on a 15s
      // baseline and refetch on tab focus for all queries. React Query does not poll a hidden
      // tab (refetchIntervalInBackground stays false), and only mounted/enabled queries poll,
      // so pages gated on activeTab refetch only while they are open. Pages needing a different
      // cadence (orders: faster adaptive; report: on-demand) override these per-query.
      //
      // retry: false here is React Query's own retry of the queryFn. The transport-level retry
      // inside apiRequest is a separate thing and still applies, so a 5xx is retried twice
      // before the query is failed.
      refetchInterval: 15_000,
      refetchOnWindowFocus: true,
      staleTime: 0,
      retry: false,
    },
  },
});
