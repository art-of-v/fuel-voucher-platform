'use client';

import { useEffect, useRef } from 'react';
import SectionIndex from './SectionIndex';
import Marquee from './Marquee';
import styles from './Partners.module.css';

export default function Partners() {
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
    <section className={`lf-section lf-section--alt ${styles.section}`} id="networks" ref={ref}>
      <div className={`lf-container ${styles.container}`}>
        <header className={styles.head}>
          <div>
            <span className="lf-eyebrow lf-eyebrow--accent">Партнери</span>
            <h2 className={styles.title}>
              Заправляйся там,
              <br />
              де зручно.
            </h2>
          </div>
          <div className={styles.headRight}>
            <SectionIndex current={6} total={10} label="Партнери" />
          </div>
        </header>
      </div>

      <Marquee />
    </section>
  );
}