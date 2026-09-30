import { useQuery } from '@tanstack/react-query';
import { getPackages } from '../api/getPackages';
import type { FuelPackage } from '../../../core/types/api';

/**
 * Every store-front package (all stations + fuels), for the map price radar.
 *
 * `usePackages` stays gated on a specific station + fuel for the buy flow; the radar
 * needs the whole set to rank every brand at once, so it fetches unfiltered under its
 * own query key. `getPackages` already returns the leak-safe PublicPackageResponse
 * projection (no supplier cost / margin — planning #52).
 */
export function useAllPackages() {
  return useQuery<FuelPackage[]>({
    queryKey: ['packages', 'all'],
    queryFn: getPackages,
  });
}
