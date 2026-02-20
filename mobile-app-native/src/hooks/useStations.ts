import { useQuery } from '@tanstack/react-query';
import { getStations, getFuelTypes, Station, FuelType } from '../lib/api';

export interface StationWithFuels extends Station {
    fuels: FuelType[];
}

export function useStations() {
    return useQuery<StationWithFuels[]>({
        queryKey: ['stations'],
        queryFn: async () => {
            const [stations, fuels] = await Promise.all([
                getStations(),
                getFuelTypes()
            ]);

            const data: StationWithFuels[] = stations.map(station => ({
                ...station,
                fuels: fuels.filter(f => f.stationId === station.id)
            }));

            return data;
        },
    });
}
