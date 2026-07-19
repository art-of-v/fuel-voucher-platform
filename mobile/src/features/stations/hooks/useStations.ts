import { useQuery } from '@tanstack/react-query';
import { getStations, getFuelTypes } from '../api/getStations';
import type { StationWithFuels } from '../types';

export function useStations() {
  return useQuery<StationWithFuels[]>({
    queryKey: ['stations'],
    queryFn: async () => {
      const [stations, fuels] = await Promise.all([
        getStations(),
        getFuelTypes(),
      ]);

      return stations.map(station => ({
        ...station,
        fuels: fuels.filter(f => f.stationId === station.id),
      }));
    },
  });
}
