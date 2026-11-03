/*
 * Fuel names are API/brand values, not UI copy (see normalizeFuelName in
 * core/utils/formatters) — they read the same in every locale, so the selector
 * labels live here rather than in i18n. Keys are the canonical ids
 * normalizeFuelName emits.
 */
const FUEL_LABELS: Record<string, string> = {
  'a-92': 'А92',
  'a-95': 'А95',
  'a-95 premium': 'А95 преміум',
  'a-98': 'А98',
  '100': '100',
  '100 premium': '100 преміум',
  diesel: 'ДП',
  'diesel premium': 'ДП преміум',
  adblue: 'AdBlue',
  gas: 'Газ',
  'gas premium': 'Газ преміум',
};

/** The fuel the radar opens on when the network offers it. */
export const DEFAULT_FUEL = 'a-95';

/**
 * The order a pump column runs: petrols by octane with each premium next to the
 * grade it is a step up from, then diesel, then the additive, then autogas.
 *
 * The chips used to follow whichever order the packages came back in, so the same
 * network listed «Газ, А95, ДП, 100, 100 преміум, А95 преміум, ДП преміум…» — a row
 * that reads as random cannot be scanned, and a fuel selector is scanned, not read.
 */
const FUEL_ORDER = [
  'a-92',
  'a-95',
  'a-95 premium',
  'a-98',
  '100',
  '100 premium',
  'diesel',
  'diesel premium',
  'adblue',
  'gas',
  'gas premium',
];

/** A fuel's position in that order; a grade the table has never seen goes last. */
export function fuelRank(canonical: string): number {
  const index = FUEL_ORDER.indexOf(canonical);
  return index === -1 ? FUEL_ORDER.length : index;
}

/** A canonical fuel id as the chips, the sheet title and the price label show it. */
export function fuelLabel(canonical: string): string {
  return FUEL_LABELS[canonical] ?? canonical.toUpperCase();
}
