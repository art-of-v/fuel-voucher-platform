'use client';

import { useEffect, useRef } from 'react';
import SectionIndex from './SectionIndex';
import styles from './AppShowcase.module.css';

const screens = Array.from({ length: 10 }, (_, i) => `/design-${i + 1}.jpg`);

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

  const scroll = (dir: -1 | 1) => {
    const rail = railRef.current;
    if (!rail) return;
    const card = rail.querySelector<HTMLElement>(`.${styles.device}`);
    const step = card ? card.offsetWidth + 24 : 360;
    rail.scrollBy({ left: dir * step, behavior: 'smooth' });
  };

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
            {screens.map((src, i) => (
              <figure
                key={src}
                className={`${styles.device} ${styles.reveal} lf-reveal--scale`} data-tilt data-glow
                style={{ transitionDelay: `${(i % 5) * 70}ms` }}
              >
                <img src={src} alt={`FuelFlow — екран застосунку ${i + 1}`} loading="lazy" />
              </figure>
            ))}
          </div>

          <button
            type="button"
            className={`${styles.arrow} ${styles.arrowLeft}`}
            onClick={() => scroll(-1)}
            aria-label="Попередній екран"
          >
            ←
          </button>
          <button
            type="button"
            className={`${styles.arrow} ${styles.arrowRight}`}
            onClick={() => scroll(1)}
            aria-label="Наступний екран"
          >
            →
          </button>

          <span className={styles.hint}>Гортай або тягни — 10 екранів</span>
        </div>
      </div>
    </section>
  );
}