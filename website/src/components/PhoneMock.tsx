'use client';

import { useEffect, useState } from 'react';
import { fetchCatalogue, pickReferencePackage } from '../lib/catalogue';
import styles from './PhoneMock.module.css';

const modules = [
  [1, 1, 1, 0, 1, 0, 1, 1, 1],
  [1, 0, 1, 0, 0, 1, 1, 0, 1],
  [1, 1, 1, 0, 1, 0, 1, 1, 1],
  [0, 0, 0, 1, 0, 1, 0, 0, 0],
  [1, 0, 1, 0, 1, 0, 1, 0, 1],
  [0, 1, 0, 1, 0, 1, 0, 1, 0],
  [1, 1, 1, 0, 1, 1, 1, 0, 1],
  [1, 0, 1, 1, 0, 0, 1, 1, 0],
  [1, 1, 1, 0, 1, 0, 1, 0, 1],
];

const MOCK_LITERS = 20;

export default function PhoneMock({ className }: { className?: string }) {
  // Same live catalogue the prices section reads, so the multiplication cannot
  // contradict the table above it the way the hardcoded "₴2 920 · А-95" did.
  const [reference, setReference] = useState<{
    perLiter: number;
    fuelName: string;
    stationId: string;
  } | null>(null);

  useEffect(() => {
    let alive = true;
    fetchCatalogue()
      .then((rows) => {
        const found = pickReferencePackage(rows);
        if (alive && found) {
          setReference({
            perLiter: found.finalPricePerLiter,
            fuelName: found.fuelName,
            stationId: found.stationId.toUpperCase(),
          });
        }
      })
      .catch(() => {
        /* decorative mock; the real numbers live in Numbers and Prices */
      });
    return () => {
      alive = false;
    };
  }, []);

  return (
    <div className={`${styles.phone} ${className ?? ''}`} aria-hidden="true">
      <div className={styles.notch} />
      <div className={styles.screen}>
        <div className={styles.topBar}>
          <span className={styles.brand}>
            FUELFLOW<span className={styles.brandDot}>.</span>
          </span>
          <span className={styles.chip}>Гаманець</span>
        </div>

        <div className={styles.balance}>
          {/* A package is one voucher redeemed by one fill - litres do not
              accumulate in a wallet. So this shows one voucher, and the money
              line is the multiplication back to a price: litres × the live
              reference rate. */}
          <div className={styles.balanceLabel}>Твій ваучер</div>
          <div className={styles.balanceValue}>
            <span className={styles.liters}>{MOCK_LITERS}</span>
            <span className={styles.unit}>літрів</span>
          </div>
          <div className={styles.balanceFoot}>
            <span className={styles.money}>
              {reference
                ? `${reference.perLiter.toFixed(2).replace('.', ',')} × ${MOCK_LITERS} = ${Math.round(
                    reference.perLiter * MOCK_LITERS
                  )
                    .toLocaleString('uk-UA')
                    .replace(/\u00a0/g, ' ')} ₴`
                : '—'}
            </span>
            <span className={styles.chip}>
              {reference ? `${reference.stationId}` : 'OKKO'}
            </span>
          </div>
        </div>

        <div className={styles.qrCard}>
          <svg viewBox="0 0 9 9" className={styles.qrSvg} shapeRendering="crispEdges">
            {modules.map((row, y) =>
              row.map((on, x) =>
                on ? <rect key={`${x}-${y}`} x={x} y={y} width={1} height={1} /> : null
              )
            )}
          </svg>
        </div>

        <div className={styles.ctaBtn}>Показати на колонці</div>
      </div>
    </div>
  );
}