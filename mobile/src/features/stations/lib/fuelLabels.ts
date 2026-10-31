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
  gas: 'Газ',
  'gas premium': 'Газ преміум',
};

/** The fuel the radar opens on when the network offers it. */
export const DEFAULT_FUEL = 'a-95';

/** A canonical fuel id as the chips, the sheet title and the price label show it. */
export function fuelLabel(canonical: string): string {
  return FUEL_LABELS[canonical] ?? canonical.toUpperCase();
}
