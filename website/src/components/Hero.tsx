'use client';

import { useEffect, useRef } from 'react';
import AppStoreBadge from './AppStoreBadge';
import styles from './Hero.module.css';

export default function Hero() {
  const ref = useRef<HTMLElement>(null);

  useEffect(() => {
    const root = ref.current;
    if (!root) return;

    /* The hero is above the fold and is the largest contentful paint, so it
     * reveals on mount rather than on scroll. That is also the only thing that
     * can work here: the headline lines are revealed by a clip-path, and a
     * clip-path that hides an element empties its intersection rect - so a
     * scroll-triggered reveal of those lines can never complete, and the one
     * thing a visitor sees first is the one thing that stays invisible.
     *
     * `lf-reveal` plus the literal `is-visible` is the same mechanism the other
     * eleven sections use, and it has to stay literal: globals.css is not a CSS
     * module, so `is-visible` there is unhashed and the JS can match it. This
     * component's own module rules use :global() for the same reason. */
    const items = root.querySelectorAll<HTMLElement>(`.${styles.reveal}`);
    items.forEach((item, i) => {
      item.style.transitionDelay = `${i * 90}ms`;
      item.classList.add('lf-reveal');
    });

    const frame = requestAnimationFrame(() => {
      items.forEach((item) => item.classList.add('is-visible'));
    });
    return () => cancelAnimationFrame(frame);
  }, []);

  return (
    <section className={styles.hero} id="top" ref={ref}>
      <div className={styles.bg} aria-hidden="true">
        <div className={styles.bgImage} data-parallax="0.08" />
        <div className={styles.bgScrim} />
      </div>
      <div className="lf-grid-bg" aria-hidden="true" />

      <div className={`${styles.container} lf-container`}>
        <div className={styles.left}>
          <div className={`${styles.meta} ${styles.reveal}`}>
            <span className={styles.metaDot} />
            <span className={styles.metaLabel}>FuelFlow · цифровий паливний гаманець</span>
          </div>

          <h1 className={styles.title}>
            <span className={`${styles.titleMain} ${styles.reveal}`}>
              Купуй паливо
            </span>
            <span className={`${styles.titleSub} ${styles.reveal}`}>
              сьогодні<span className={styles.titleAccent}>.</span>
            </span>
            <span className={`${styles.titleLine2} ${styles.reveal}`}>
              Зафіксуй ціну.
            </span>
          </h1>

          <p className={`${styles.lead} ${styles.reveal}`}>
            Купуй літри наперед, зберігай паливо у застосунку та
            заправляйся за QR-кодом. Гроші → літри → телефон → QR → пальне.
          </p>

          <div className={`${styles.actions} ${styles.reveal}`}>
            <AppStoreBadge />
            <a href="#business" className={`lf-btn lf-btn--ghost ${styles.cta}`}>
              Для бізнесу →
            </a>
          </div>

          <div className={`${styles.spec} ${styles.reveal}`}>
            <div className={styles.specRow}>
              <span className={styles.specLabel}>Мереж</span>
              <span className={styles.specValue}>04</span>
            </div>
            <div className={styles.specRow}>
              <span className={styles.specLabel}>Пакет</span>
              <span className={styles.specValue}>50 л</span>
            </div>
            <div className={styles.specRow}>
              <span className={styles.specLabel}>Оплата</span>
              <span className={styles.specValue}>Monobank</span>
            </div>
            <div className={styles.specRow}>
              <span className={styles.specLabel}>Ціна</span>
              <span className={styles.specValue}>Зафіксована</span>
            </div>
          </div>
        </div>
      </div>

      <a href="#numbers" className={`${styles.scroll} ${styles.reveal}`} aria-label="Прокрутити далі">
        <span className={styles.scrollLabel}>SCROLL</span>
        <span className={styles.scrollLine} />
      </a>
    </section>
  );
}