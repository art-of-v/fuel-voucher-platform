import { useQuery } from '@tanstack/react-query';
import { getStationNodes } from '../api/getStationNodes';
import type { StationNode } from '../../../core/types/api';

export function useStationNodes() {
  return useQuery<StationNode[]>({
    queryKey: ['station-nodes'],
    queryFn: getStationNodes,
  });
}
