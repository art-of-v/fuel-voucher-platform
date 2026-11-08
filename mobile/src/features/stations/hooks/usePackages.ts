import { useQuery } from '@tanstack/react-query';
import { getPackages } from '../api/getPackages';
import { useAppStateActive } from '../../../core/hooks/useAppStateActive';
import type { FuelPackage } from '../../../core/types/api';

export function usePackages(brandId?: string, fuelTypeId?: string) {
  const isActive = useAppStateActive();

  return useQuery<FuelPackage[]>({
    queryKey: ['packages', brandId, fuelTypeId],
    queryFn: async () => {
      const allPackages = await getPackages();
      return allPackages
        .filter((pkg) => {
          if (brandId && pkg.brandId !== brandId) return false;
          if (fuelTypeId && pkg.fuelTypeId !== fuelTypeId) return false;
          return true;
        })
        .sort((a, b) => a.liters - b.liters);
    },
    enabled: !!brandId && !!fuelTypeId,
    // Keep the nominals screen in sync with admin price edits without a manual
    // refresh (planning #127); foreground-gated like useStations so the resume
    // tick can't race the token refresh (#26 / planning #45).
    staleTime: 15_000,
    refetchInterval: isActive ? 30_000 : false,
  });
}
