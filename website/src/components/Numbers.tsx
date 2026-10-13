'use client';

import { useEffect, useRef, useState } from 'react';
import SectionIndex from './SectionIndex';
import styles from './Numbers.module.css';
import { fetchCatalogue, pickReferencePackage } from '../lib/catalogue';

/** The bucket the multiplication is shown on - the standard full fill. */
const BUCKET_LITERS = 50;

/** Applied inline when the price locks. See the note at the element. */
const LOCKED_COLOUR = { color: 'var(--accent)' } as const;

/**
 * The price band: the numbers move, then stop and turn green.
 *
 * This animation was removed in #1076 because the values it cycled through
 * (97.60, 99.90, 98.40, 100.30, 99.20) were invented, and a moving number that
 * looks measured is worse than a static one that lies - it implies the motion is
 * data.
 *
 * So the motion is back and the fiction is not: it cycles through the real
 * per-litre prices of the anchor network, every one of which is a price someone
 * can buy today, and locks on the reference - diesel at OKKO.
 */
export default function Numbers() {
  const [stats, setStats] = useState<{
    perLiter: number;
    networks: number;
    reel: number[];
  } | null>(null);
  const [locked, setLocked] = useState(false);
  const [tick, setTick] = useState(0);
  const ref = useRef<HTMLElement>(null);

  useEffect(() => {
    let alive = true;
    fetchCatalogue()
      .then((rows) => {
        if (!alive) return;
        const reference = pickReferencePackage(rows);
        if (!reference) return;

        // Everything the anchor network really sells, per litre, deduplicated.
        // Sorted so the sequence is stable between renders instead of depending
        // on whatever order the endpoint happened to return.
        const reel = [
          ...new Set(
            rows
              .filter((r) => r.stationId === reference.stationId)
              .map((r) => r.finalPricePerLiter)
          ),
        ].sort((a, b) => a - b);

        setStats({
          perLiter: reference.finalPricePerLiter,
          networks: new Set(rows.map((r) => r.stationId)).size,
          reel: reel.length > 1 ? reel : [reference.finalPricePerLiter],
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
    if (!stats) return;
    const root = ref.current;
    if (!root) return;

    let interval: ReturnType<typeof setInterval> | undefined;
    let timer: ReturnType<typeof setTimeout> | undefined;

    // Both setState calls live inside the observer callback, never in the effect
    // body - a synchronous setState there cascades renders, and eslint's
    // react-hooks/set-state-in-effect fails the build on it.
    const io = new IntersectionObserver(
      ([entry]) => {
        if (!entry.isIntersecting) return;

        if (window.matchMedia('(prefers-reduced-motion: reduce)').matches) {
          setLocked(true);
          return;
        }

        interval = setInterval(() => setTick((t) => t + 1), 900);
        timer = setTimeout(() => {
          if (interval) clearInterval(interval);
          setLocked(true);
        }, 4500);
      },
      { threshold: 0.5 }
    );
    io.observe(root);
    return () => {
      io.disconnect();
      if (interval) clearInterval(interval);
      if (timer) clearTimeout(timer);
    };
  }, [stats]);

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
            {/* The lock colour is inline, not a class. It went through a parent-modifier
                selector once and then a single class once, and both times the deployed rule was
                present, ordered correctly, and still produced a white number. An inline style
                cannot be lost to a stylesheet that is missing, stale or out of order - which is
                the only thing that can be relied on for a decorative effect that has failed
                twice. */}
            <span className={styles.price} style={locked ? LOCKED_COLOUR : undefined}>
              {(() => {
                if (!stats) return '—';
                const shown = locked
                  ? stats.perLiter
                  : stats.reel[tick % stats.reel.length];
                return shown.toFixed(2).replace('.', ',');
              })()}
              <span className={styles.priceUnit}>/ л</span>
            </span>
            <span className={styles.lockState} style={locked ? LOCKED_COLOUR : undefined}>
              {locked
                ? '✓ Зафіксовано в момент оплати'
                : 'Ціна рухається, поки не оплатив…'}
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