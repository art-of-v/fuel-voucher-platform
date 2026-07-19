import { useQuery } from '@tanstack/react-query';
import { getPackages } from '../api/getPackages';
import { normalizeFuelName } from '../../../core/utils/formatters';
import type { FuelPackage } from '../../../core/types/api';

export function usePackages(stationId?: string, fuelName?: string) {
  return useQuery<FuelPackage[]>({
    queryKey: ['packages', stationId, fuelName],
    queryFn: async () => {
      const allPackages = await getPackages();
      return allPackages.filter(pkg => {
        if (stationId && pkg.stationId !== stationId) return false;
        if (fuelName && normalizeFuelName(pkg.fuelName) !== normalizeFuelName(fuelName))
          return false;
        return true;
      }).sort((a, b) => a.liters - b.liters);
    },
    enabled: !!stationId && !!fuelName,
  });
}
