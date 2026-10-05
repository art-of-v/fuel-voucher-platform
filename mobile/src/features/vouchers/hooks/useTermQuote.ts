import { useEffect, useState } from 'react';
import { getTermQuote, type TermQuote } from '../api/termQuote';

/**
 * Loads the term ladder for one package line.
 *
 * Returns `null` while loading and stays `null` on failure, because a quote that cannot be fetched must
 * never block buying fuel: the caller falls back to the full remaining term at the normal price, which is
 * exactly what shipping the feature off looks like anyway.
 */
export function useTermQuote(
  stationId: string | undefined,
  fuelTypeId: string | undefined,
  liters: number | undefined,
): TermQuote | null {
  const [quote, setQuote] = useState<TermQuote | null>(null);

  useEffect(() => {
    if (!stationId || !fuelTypeId || !liters) {
      setQuote(null);
      return;
    }

    let cancelled = false;
    setQuote(null);
    getTermQuote(stationId, fuelTypeId, liters)
      .then((loaded) => {
        if (!cancelled) setQuote(loaded);
      })
      .catch(() => {
        if (!cancelled) setQuote(null);
      });

    return () => {
      cancelled = true;
    };
  }, [stationId, fuelTypeId, liters]);

  return quote;
}
