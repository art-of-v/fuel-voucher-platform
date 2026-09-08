'use client';

import { useEffect, useRef } from 'react';
import PhoneMock from './PhoneMock';
import styles from './Final.module.css';

export default function Final() {
  const ref = useRef<HTMLElement>(null);

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
    <section className={styles.section} id="download" ref={ref}>
      <div className="lf-grid-bg" aria-hidden="true" />
      <div className={`lf-container ${styles.container}`}>
        <div className={styles.copy}>
          <span className={`lf-eyebrow lf-eyebrow--accent ${styles.reveal}`}>
            FuelFlow
          </span>
          <h2 className={`${styles.title} ${styles.reveal}`}>
            Готовий заправлятися
            <br />
            по-новому?
          </h2>
          <p className={`${styles.lead} ${styles.reveal}`}>
            Купуй сьогодні. Фіксуй ціну. Заправляйся за QR.
          </p>
          <div className={`${styles.actions} ${styles.reveal}`}>
            <a href="#contact" className={`lf-btn lf-btn--primary ${styles.cta}`} data-magnetic>
              Завантажити FuelFlow →
            </a>
            <a href="#business" className={`lf-btn lf-btn--ghost ${styles.cta}`} data-magnetic>
              Для бізнесу →
            </a>
          </div>
        </div>
        <div className={styles.phoneWrap}>
          <div data-tilt>
            <PhoneMock className={styles.reveal} />
          </div>
        </div>
      </div>
    </section>
  );
}