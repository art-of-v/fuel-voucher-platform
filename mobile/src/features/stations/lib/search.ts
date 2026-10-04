import type { StationNode } from '../../../core/types/api';

/**
 * Search over the station list shown on the map.
 *
 * Extracted from `app/map.tsx`, where it sat inline in a 1,100-line screen with
 * no test. It is the one piece of real logic that file had which the station
 * library's own tests did not already cover — `rankStations`, `radarWithinRadius`,
 * `rankBrands`, `bestPriceByStation` and `routeTarget` all live in `lib/` and are
 * tested there.
 */

/**
 * Brands whose name is written in Latin in the app but appears in Cyrillic in the
 * network's own data.
 *
 * A Ukrainian customer types "OKKO", "WOG", "KLO", "UPG" — those are how the brands
 * are written and spoken. The station names that come back from the API are
 * Cyrillic ("ОККО", "ВОГ", "КЛО", "ЮПІ"). Matching only on the literal query would
 * make search return nothing for exactly the four networks a customer is most
 * likely to look for, with no error and no empty-state hint that anything is
 * broken.
 *
 * Left-to-right, Latin query to Cyrillic spelling. Only a whole-query match counts:
 * "wog" must not match a street named "Володимирівська", and this keeps that from
 * happening.
 */
export const BRAND_SEARCH_ALIASES: Record<string, string> = {
  okko: 'окко',
  wog: 'вог',
  klo: 'кло',
  upg: 'юпі',
};

/** Fields a query is matched against. */
function searchTargets(node: StationNode): string[] {
  const withAddress = node as StationNode & { address?: string; city?: string };
  return [
    node.name.toLowerCase(),
    withAddress.address?.toLowerCase() ?? '',
    withAddress.city?.toLowerCase() ?? '',
  ];
}

/** Whether any of a station's searchable fields contains the query. */
export function matchesQuery(node: StationNode, query: string): boolean {
  const q = query.toLowerCase().trim();
  if (!q) return true;

  const alias = BRAND_SEARCH_ALIASES[q];

  return searchTargets(node).some((target) => {
    if (target.includes(q)) return true;
    return alias !== undefined && target.includes(alias);
  });
}

/**
 * Stations matching `query`, keeping only those that can actually be shown.
 *
 * A station is dropped when its coordinates are missing or zero. Those points have
 * no place on the map, so leaving them in would let the count in the results header
 * disagree with the number of pins on screen.
 */
export function filterStationsByQuery(
  nodes: readonly StationNode[] | undefined | null,
  query: string,
): StationNode[] {
  if (!nodes) return [];

  return nodes.filter((node) => {
    const lat = parseFloat(node.lat || '0');
    const lng = parseFloat(node.lng || '0');
    if (!lat || !lng) return false;

    return matchesQuery(node, query);
  });
}
