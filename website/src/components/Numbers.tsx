'use client';

import { useEffect, useRef, useState } from 'react';
import SectionIndex from './SectionIndex';
import styles from './Numbers.module.css';
import { fetchCatalogue, pickReferencePackage } from '../lib/catalogue';

/** The bucket the multiplication is shown on - the standard full fill. */
const BUCKET_LITERS = 50;

/**
 * The figures here used to be hardcoded: a rotating fake price, "50 litres in
 * the wallet", "2 920 UAH paid once" and "3 months, the standard term". None of
 * it held up against the system, and three of the four were invented numbers.
 *
 * The price is real and read from the catalogue, but it is not the cheapest row
 * we sell - that is KLO's LPG at 44.90, which put a gas number on the front page
 * as the headline. The headline quotes diesel at OKKO instead: the network the app
 * lists first, and the grade with the largest volumes.
 *
 * The multiplication is the original design's "50 litres = one payment", restored
 * with the live reference price. Nothing accumulates in a wallet - a package is
 * one voucher, redeemed by one fill - so this is arithmetic on a price, not a
 * balance the user is shown they hold.
 */
export default function Numbers() {
  const [stats, setStats] = useState<{
    perLiter: number;
    fuelName: string;
    stationId: string;
    networks: number;
  } | null>(null);
  const ref = useRef<HTMLElement>(null);

  useEffect(() => {
    let alive = true;
    fetchCatalogue()
      .then((rows) => {
        if (!alive) return;
        const reference = pickReferencePackage(rows);
        if (!reference) return;
        setStats({
          perLiter: reference.finalPricePerLiter,
          fuelName: reference.fuelName,
          stationId: reference.stationId,
          networks: new Set(rows.map((r) => r.stationId)).size,
        });
      })
      .catch(() => {
        /* the prices section carries the error state; nothing to say here */
      });
    return () => {
      alive = false;
    };
  }, []);

  useEffect(() => {
    const root = ref.current;
    if (!root) return;
    const items = root.querySelectorAll<HTMLElement>(`.${styles.reveal}`);
    const observer = new IntersectionObserver(
      (entries) => {
        entries.forEach((entry) => {
          if (entry.isIntersecting) {
            entry.target.classList.add('is-visible');
            observer.unobserve(entry.target);
          }
        });
      },
      { threshold: 0.15, rootMargin: '0px 0px -10% 0px' }
    );
    items.forEach((item) => {
      item.classList.add('lf-reveal');
      observer.observe(item);
    });
    return () => observer.disconnect();
  }, []);

  return (
    <section className={`lf-section ${styles.section}`} id="numbers" ref={ref}>
      <div className="lf-grid-bg" aria-hidden="true" />
      <div className={`lf-container ${styles.container}`}>
        <header className={styles.head}>
          <div>
            <span className="lf-eyebrow lf-eyebrow--accent">Числа — це контент</span>
            <h2 className={styles.title}>
              Ціна зупиняється.
              <br />
              Життя продовжується.
            </h2>
          </div>
          <div className={styles.headRight}>
            <SectionIndex current={3} total={10} label="Ціна" />
          </div>
        </header>

        <div className={`${styles.stage} ${styles.reveal}`}>
          <div className={styles.priceBlock}>
            <span className={styles.price}>
              {stats ? stats.perLiter.toFixed(2).replace('.', ',') : '—'}
              <span className={styles.priceUnit}>/ л</span>
            </span>
            <span className={styles.lockState}>
              ✓ Ціна фіксується в момент оплати
            </span>
            <span className={styles.referenceNote}>
              {stats ? `орієнтир: ${stats.fuelName} · ${stats.stationId.toUpperCase()}` : ' '}
            </span>
          </div>

          <div className={styles.row}>
            <div className={styles.stat}>
              <span className={styles.statValue}>
                {stats ? BUCKET_LITERS : '—'}
              </span>
              <span className={styles.statLabel}>
                літрів — повний бак у стандартному пакеті
              </span>
            </div>
            <div className={styles.stat}>
              <span className={styles.statValue}>
                {stats
                  ? (stats.perLiter * BUCKET_LITERS)
                      .toFixed(0)
                      .replace(/\B(?=(\d{3})+(?!\d))/g, ' ')
                  : '—'}
              </span>
              <span className={styles.statLabel}>
                {stats
                  ? `${stats.perLiter.toFixed(2).replace('.', ',')} × ${BUCKET_LITERS} л — сплачено один раз, і все`
                  : 'літрів × ціна — сплачено один раз'}
                , і далі не змінюється
              </span>
            </div>
            <div className={styles.stat}>
              <span className={styles.statValue}>
                {stats ? String(stats.networks).padStart(2, '0') : '—'}
              </span>
              <span className={styles.statLabel}>
                мережі-партнери: OKKO, WOG, KLO, UPG
              </span>
            </div>
          </div>

          <p className={styles.footnote}>
            Пакет — це один ваучер на одну заправку, а не запас літрів у
            гаманці: 50 л — це 50 л, які ти заливаєш за один раз.
          </p>
        </div>
      </div>
    </section>
  );
}