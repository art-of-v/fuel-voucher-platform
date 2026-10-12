'use client';

/**
 * The live package catalogue, read from the same anonymous endpoint the app uses.
 *
 * Why this exists: the prices section used to hardcode a table of per-network
 * discounts ("OKKO -4.00/l", "KLO -3.00/l", ...). None of it was true. The real
 * catalogue is whatever the operator currently has in stock - as of 2026-10-12
 * that is 50 packages across four networks, priced 84.50 to 102.90 UAH/l, in
 * volumes of 2, 3, 5, 10, 20 and 50 litres - and the volume set differs per
 * network. A hardcoded table cannot be right, because the stock changes daily.
 *
 * What this deliberately does NOT do: compute a saving. `originalPrice` is
 * derived from the pump price we ingested, which exists today for three
 * packages out of fifty (OKKO diesel, 2/3/10 L). Inventing a discount where we
 * do not know the pump price is what planning #73 forbids - the struck-through
 * price must never be the supplier cost, and where there is no pump price there
 * is nothing to strike through. So a saving is shown only when the API says
 * originalPrice > price.
 */

export interface PublicPackage {
  id: string;
  stationId: string;
  fuelTypeId: string;
  fuelName: string;
  liters: number;
  price: number;
  originalPrice: number;
  finalPricePerLiter: number;
}

export interface NetworkOffer {
  stationId: string;
  name: string;
  /** Cheapest package per litre in this network - the headline number. */
  fromPerLiter: number;
  /** Volumes this network actually stocks, ascending. */
  volumes: number[];
  fuels: { fuelName: string; perLiter: number; saving: number | null }[];
}

const API_BASE = process.env.NEXT_PUBLIC_API_URL ?? 'https://api.palne.shop';

const NETWORK_NAMES: Record<string, string> = {
  okko: 'OKKO',
  wog: 'WOG',
  klo: 'KLO',
  upg: 'UPG',
};

/**
 * A test fuel type left in production by an import ("QA-CHECK-95", 5 L, 1 UAH).
 * It is served by the public endpoint like any other package, so it reaches the
 * app and would reach this page. It is a data bug, not a copy bug - the row has
 * to be deleted from production - and this filter is the seatbelt until then.
 * Remove it once the catalogue no longer contains it.
 */
function isTestArtefact(pkg: PublicPackage): boolean {
  return (
    pkg.fuelTypeId.toLowerCase().includes('qa-') ||
    pkg.fuelName.toUpperCase().includes('QA-')
  );
}

export function isTestPackage(pkg: PublicPackage): boolean {
  return isTestArtefact(pkg);
}

let inFlight: Promise<PublicPackage[]> | null = null;

/**
 * One request per page load, shared by every component that needs the
 * catalogue, so Prices and Numbers cannot disagree with each other.
 */
export function fetchCatalogue(): Promise<PublicPackage[]> {
  if (inFlight) return inFlight;
  inFlight = fetch(`${API_BASE}/api/packages`, { cache: 'no-store' })
    .then((res) => {
      if (!res.ok) throw new Error(`packages responded ${res.status}`);
      return res.json() as Promise<PublicPackage[]>;
    })
    .then((rows) => (Array.isArray(rows) ? rows.filter((r) => !isTestArtefact(r)) : []))
    .catch((err) => {
      // Let the next mount retry instead of caching the failure forever.
      inFlight = null;
      throw err;
    });
  return inFlight;
}

export function groupByNetwork(packages: PublicPackage[]): NetworkOffer[] {
  const byStation = new Map<string, PublicPackage[]>();
  for (const pkg of packages) {
    const list = byStation.get(pkg.stationId);
    if (list) list.push(pkg);
    else byStation.set(pkg.stationId, [pkg]);
  }

  return [...byStation.entries()]
    .map(([stationId, rows]) => {
      const perFuel = new Map<string, { perLiter: number; saving: number | null }>();
      for (const row of rows) {
        const saving = row.originalPrice > row.price ? row.originalPrice - row.price : null;
        const existing = perFuel.get(row.fuelName);
        if (!existing) {
          perFuel.set(row.fuelName, { perLiter: row.finalPricePerLiter, saving });
        } else {
          // Keep the cheapest volume's litre price as the headline, but never
          // lose a real saving to a more expensive variant of the same fuel.
          existing.perLiter = Math.min(existing.perLiter, row.finalPricePerLiter);
          existing.saving = Math.max(existing.saving ?? 0, saving ?? 0) || null;
        }
      }

      const fuels = [...perFuel.entries()]
        .map(([fuelName, v]) => ({ fuelName, perLiter: v.perLiter, saving: v.saving }))
        .sort((a, b) => a.perLiter - b.perLiter);

      return {
        stationId,
        name: NETWORK_NAMES[stationId.toLowerCase()] ?? stationId.toUpperCase(),
        fromPerLiter: fuels[0]?.perLiter ?? 0,
        volumes: [...new Set(rows.map((r) => r.liters))].sort((a, b) => a - b),
        fuels,
      };
    })
    .sort((a, b) => a.fromPerLiter - b.fromPerLiter);
}

export function formatMoney(value: number): string {
  return `${value.toFixed(2).replace('.', ',')} ₴`;
}

export function formatLiters(liters: number[]): string {
  return liters.join(' · ');
}
