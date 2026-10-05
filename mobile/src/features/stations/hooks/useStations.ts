import { useQuery } from '@tanstack/react-query';
import { getStations, getFuelTypes } from '../api/getStations';
import { useAppStateActive } from '../../../core/hooks/useAppStateActive';
import type { StationWithFuels } from '../../../core/types/api';

export function useStations() {
  const isActive = useAppStateActive();

  return useQuery<StationWithFuels[]>({
    queryKey: ['stations'],
    queryFn: async () => {
      const [stations, fuels] = await Promise.all([getStations(), getFuelTypes()]);

      return stations.map((station) => ({
        ...station,
        fuels: fuels.filter((f) => f.stationId === station.id),
      }));
    },
    // Admin changes prices (cost/margin/pump) while the catalog stays open; poll
    // gently so the customer never has to pull-to-refresh (planning #127). Gated
    // on foreground via useAppStateActive so the timer can't fire on resume and
    // race the token-refresh single-flight (the #26 spurious-logout shape;
    // planning #45) — same safe pattern as the notifications poll.
    staleTime: 15_000,
    refetchInterval: isActive ? 30_000 : false,
  });
}
