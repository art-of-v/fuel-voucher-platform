import { useQuery } from '@tanstack/react-query';
import { getStationNodes } from '../api/getStationNodes';

export function useStationNodes() {
  return useQuery({
    queryKey: ['station-nodes'],
    queryFn: getStationNodes,
  });
}
