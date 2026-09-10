'use client';

import { useEffect, useRef } from 'react';
import SectionIndex from './SectionIndex';
import styles from './AppShowcase.module.css';

const screens = Array.from({ length: 10 }, (_, i) => `/design-${i + 1}.jpg`);

/* The rail renders the same ten screens three times and silently re-seats the
 * scroll position by one copy length whenever it drifts near a boundary, so
 * swiping/dragging never dead-ends and never visibly jumps - the copies are
 * pixel-identical. There are no arrow buttons: the rail is the only control. */
const copies = [0, 1, 2];

export default function AppShowcase() {
  const ref = useRef<HTMLElement>(null);
  const railRef = useRef<HTMLDivElement>(null);

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
      { threshold: 0.05 }
    );
    items.forEach((item) => {
      item.classList.add('lf-reveal');
      observer.observe(item);
    });
    return () => observer.disconnect();
  }, []);

  /* Seamless looping: keep the scroll position anchored to the middle copy.
   * Re-anchoring is a plain (non-smooth) scrollLeft assignment offset by an
   * exact number of cards, so scroll-snap positions stay aligned and the
   * frame the user sees does not change. */
  useEffect(() => {
    const rail = railRef.current;
    if (!rail) return;

    const copyWidth = () => rail.scrollWidth / copies.length;
    rail.scrollLeft = copyWidth();

    const onScroll = () => {
      const w = copyWidth();
      if (rail.scrollLeft < w * 0.5) {
        rail.scrollLeft += w;
      } else if (rail.scrollLeft > w * 1.5) {
        rail.scrollLeft -= w;
      }
    };

    rail.addEventListener('scroll', onScroll, { passive: true });
    return () => rail.removeEventListener('scroll', onScroll);
  }, []);

  /* Drag-to-scroll with a mouse */
  useEffect(() => {
    const rail = railRef.current;
    if (!rail) return;
    let down = false;
    let startX = 0;
    let startScroll = 0;
    let moved = false;

    const onDown = (e: PointerEvent) => {
      if (e.pointerType !== 'mouse') return;
      down = true;
      moved = false;
      startX = e.clientX;
      startScroll = rail.scrollLeft;
      rail.classList.add(styles.dragging);
    };
    const onMove = (e: PointerEvent) => {
      if (!down) return;
      const dx = e.clientX - startX;
      if (Math.abs(dx) > 4) moved = true;
      rail.scrollLeft = startScroll - dx;
    };
    const onUp = () => {
      down = false;
      rail.classList.remove(styles.dragging);
    };
    const onClickCapture = (e: MouseEvent) => {
      if (moved) {
        e.preventDefault();
        e.stopPropagation();
      }
    };

    rail.addEventListener('pointerdown', onDown);
    window.addEventListener('pointermove', onMove);
    window.addEventListener('pointerup', onUp);
    rail.addEventListener('click', onClickCapture, true);
    return () => {
      rail.removeEventListener('pointerdown', onDown);
      window.removeEventListener('pointermove', onMove);
      window.removeEventListener('pointerup', onUp);
      rail.removeEventListener('click', onClickCapture, true);
    };
  }, []);

  return (
    <section className={`lf-section ${styles.section}`} id="app" ref={ref}>
      <div className={`lf-container ${styles.container}`}>
        <header className={styles.head}>
          <div>
            <span className="lf-eyebrow">Застосунок</span>
            <h2 className={styles.title}>
              Один застосунок.
              <br />
              Усе паливо — в ньому.
            </h2>
          </div>
          <div className={styles.headRight}>
            <SectionIndex current={4} total={10} label="Застосунок" />
          </div>
        </header>

        <div className={styles.railWrap}>
          <div className={styles.rail} ref={railRef}>
            {copies.map((copy) =>
              screens.map((src, i) => (
                <figure
                  key={`${copy}-${src}`}
                  className={`${styles.device} ${styles.reveal} lf-reveal--scale`} data-tilt data-glow
                  style={{ transitionDelay: `${(i % 5) * 70}ms` }}
                >
                  <img src={src} alt={`FuelFlow — екран застосунку ${i + 1}`} loading="lazy" />
                </figure>
              ))
            )}
          </div>

          <span className={styles.hint}>Гортай — стрічка без кінця</span>
        </div>
      </div>
    </section>
  );
}