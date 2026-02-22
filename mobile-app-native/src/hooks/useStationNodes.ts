import { useQuery } from '@tanstack/react-query';
import { getStationNodes, StationNode } from '../lib/api';

export function useStationNodes() {
    return useQuery<StationNode[]>({
        queryKey: ['station-nodes'],
        queryFn: async () => {
            return await getStationNodes();
        },
    });
}
