'use client';

import { useEffect, useRef } from 'react';
import SectionIndex from './SectionIndex';
import styles from './Networks.module.css';

const networks = ['OKKO', 'WOG', 'KLO', 'UPG'];

export default function Networks() {
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
    <section
      className={`lf-section lf-section--alt ${styles.section}`}
      id="networks"
      ref={ref}
    >
      <div className={`lf-container ${styles.container}`}>
        <header className={styles.head}>
          <div>
            <span className="lf-eyebrow lf-eyebrow--accent">Мережі</span>
            <h2 className={styles.title}>
              Працюємо там, де ти заправляєшся.
            </h2>
          </div>
          <div className={styles.headRight}>
            <SectionIndex current={3} total={8} label="Мережі" />
          </div>
        </header>

        <div className={styles.list}>
          {networks.map((net, i) => (
            <div
              key={net}
              className={`${styles.row} ${styles.reveal}`}
              style={{ transitionDelay: `${i * 30}ms` }}
            >
              <span className={styles.rowIndex}>
                {String(i + 1).padStart(2, '0')}
              </span>
              <span className={styles.rowName}>{net}</span>
              <span className={styles.rowMeta}>
                <span className={styles.rowDot} />
                активна мережа
              </span>
            </div>
          ))}
        </div>
      </div>
    </section>
  );
}