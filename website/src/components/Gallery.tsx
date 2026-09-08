'use client';

import { useEffect, useRef } from 'react';
import SectionIndex from './SectionIndex';
import styles from './Gallery.module.css';

export default function Gallery() {
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
      { threshold: 0.1, rootMargin: '0px 0px -5% 0px' }
    );
    items.forEach((item) => {
      item.classList.add('lf-reveal');
      observer.observe(item);
    });
    return () => observer.disconnect();
  }, []);

  return (
    <section className={`lf-section ${styles.section}`} id="gallery" ref={ref}>
      <div className={`lf-container ${styles.container}`}>
        <header className={styles.head}>
          <div>
            <span className="lf-eyebrow">Візуальна ідентичність</span>
            <h2 className={styles.title}>
              Один бренд — від телефона до колонки.
            </h2>
          </div>
          <div className={styles.headRight}>
            <SectionIndex current={8} total={10} label="Бренд" />
          </div>
        </header>

        <div className={styles.grid}>
          <figure className={`${styles.card} ${styles.reveal}`} data-glow>
            <img
              src="/brand-1.jpg"
              alt="Брендинг FuelFlow — фірмовий дизайн"
              width={1200}
              height={1800}
            />
          </figure>
          <figure className={`${styles.card} ${styles.reveal}`} data-glow style={{ transitionDelay: '80ms' }}>
            <img
              src="/brand-2.jpg"
              alt="Брендинг FuelFlow — фірмові матеріали"
              width={1200}
              height={1200}
            />
          </figure>
        </div>
      </div>
    </section>
  );
}