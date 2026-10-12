'use client';

import { useEffect, useRef, useState } from 'react';
import SectionIndex from './SectionIndex';
import styles from './Numbers.module.css';
import { fetchCatalogue } from '../lib/catalogue';

/**
 * The three figures here used to be hardcoded: a rotating fake price
 * (97.60 -> 99.20 UAH/l), "50 litres in the wallet" and "3 months, the
 * standard term". None of it held up against the system:
 *
 * - the price was invented, and the animation made it look measured;
 * - litres do not accumulate in a wallet - a package is one voucher, redeemed
 *   by one fill, so a "50 litres in the wallet" balance describes a mechanic
 *   that does not exist;
 * - there is no three-month term. A voucher's term is the expiry printed on it
 *   by the network and parsed at import.
 *
 * So the numbers are now read from the same catalogue the prices section uses,
 * and the two figures that are not prices state the real model instead.
 */
export default function Numbers() {
  const [stats, setStats] = useState<{ fromPerLiter: number; networks: number } | null>(
    null
  );
  const ref = useRef<HTMLElement>(null);

  useEffect(() => {
    let alive = true;
    fetchCatalogue()
      .then((rows) => {
        if (!alive) return;
        const perLiter = rows.map((r) => r.finalPricePerLiter);
        if (!perLiter.length) return;
        setStats({
          fromPerLiter: Math.min(...perLiter),
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
              {stats ? (
                <>
                  від {stats.fromPerLiter.toFixed(2).replace('.', ',')}
                </>
              ) : (
                '—'
              )}
              <span className={styles.priceUnit}>/ л</span>
            </span>
            <span className={styles.lockState}>
              ✓ Ціна фіксується в момент оплати
            </span>
          </div>

          <div className={styles.row}>
            <div className={styles.stat}>
              <span className={styles.statValue}>2–50</span>
              <span className={styles.statLabel}>
                літрів в одному пакеті — від 2 до 50, набір залежить від мережі
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
            <div className={styles.stat}>
              <span className={styles.statValue}>1 = 1</span>
              <span className={styles.statLabel}>
                пакет — це один ваучер на одну заправку
              </span>
            </div>
          </div>
        </div>
      </div>
    </section>
  );
}