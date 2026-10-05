import { useQuery } from '@tanstack/react-query';
import { getTermQuote, type TermQuote } from '../api/termQuote';
import { useAppStateActive } from '../../../core/hooks/useAppStateActive';

/**
 * The term ladder for one package line, on the same footing as the rest of the catalog.
 *
 * This used to be a `useEffect` + `useState` fetch, which meant the ladder was right only until the card
 * unmounted: a manager repricing a term, or a station adding fuel, would not reach an open screen, and the
 * customer would choose from prices already superseded. It also meant every package card on the screen
 * issued its own request for what is largely the same ladder.
 *
 * As a React Query hook it inherits the pattern `usePackages` and `useStations` already use — a gentle
 * foreground-gated poll plus a stale window — so the terms and their prices track the server the same way
 * the fuel list does, without a pull-to-refresh. The poll is gated on foreground because a timer firing on
 * resume can race the token refresh and produce the spurious-logout shape (#26 / planning #45).
 *
 * A failed fetch resolves to `null`, which renders no picker at all: the card then shows its normal price,
 * exactly as it did before short-term selling existed, so a ladder that cannot be read never blocks buying
 * fuel.
 */
export function useTermQuote(
  stationId: string | undefined,
  fuelTypeId: string | undefined,
  liters: number | undefined,
): TermQuote | null {
  const isActive = useAppStateActive();
  const enabled = !!stationId && !!fuelTypeId && !!liters;

  const { data } = useQuery<TermQuote>({
    queryKey: ['term-quote', stationId, fuelTypeId, liters],
    queryFn: async () => getTermQuote(stationId as string, fuelTypeId as string, liters as number),
    enabled,
    // The ladder changes rarely; the per-line price changes whenever the catalog does. A minute of
    // staleness is short enough that a repriced term is never what the customer finally pays, and the
    // server recomputes at checkout regardless.
    staleTime: 60_000,
    refetchInterval: isActive ? 60_000 : false,
    retry: false,
  });

  return enabled ? (data ?? null) : null;
}
