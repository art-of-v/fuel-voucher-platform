'use client';

import { useEffect, useRef, useState } from 'react';
import Counter from './Counter';
import SectionIndex from './SectionIndex';
import styles from './Numbers.module.css';

const prices = [97.6, 99.9, 98.4, 100.3, 99.2];

export default function Numbers() {
  const [locked, setLocked] = useState(false);
  const [tick, setTick] = useState(0);
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

  useEffect(() => {
    if (window.matchMedia('(prefers-reduced-motion: reduce)').matches) {
      setLocked(true);
      return;
    }
    const root = ref.current;
    if (!root) return;
    let interval: ReturnType<typeof setInterval> | undefined;
    let timer: ReturnType<typeof setTimeout> | undefined;
    const io = new IntersectionObserver(
      ([entry]) => {
        if (entry.isIntersecting && !locked) {
          interval = setInterval(() => setTick((t) => t + 1), 900);
          timer = setTimeout(() => {
            clearInterval(interval);
            setLocked(true);
          }, 4500);
        }
      },
      { threshold: 0.5 }
    );
    io.observe(root);
    return () => {
      io.disconnect();
      clearInterval(interval);
      clearTimeout(timer);
    };
    // eslint-disable-next-line react-hooks/exhaustive-deps
  }, []);

  const price = locked ? prices[prices.length - 1] : prices[tick % prices.length];

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
          <div className={`${styles.priceBlock} ${locked ? styles.priceLocked : ''}`}>
            <span className={styles.price}>
              ₴{price.toFixed(2)}
              <span className={styles.priceUnit}>/ л</span>
            </span>
            <span className={styles.lockState}>
              {locked ? '✓ Зафіксовано' : 'Ціна на колонці рухається…'}
            </span>
          </div>

          <div className={styles.row}>
            <div className={styles.stat}>
              <Counter to={50} className={styles.statValue} />
              <span className={styles.statLabel}>літрів у гаманці</span>
            </div>
            <div className={styles.stat}>
              <Counter to={2920} prefix="₴" className={styles.statValue} />
              <span className={styles.statLabel}>сплачено одного разу</span>
            </div>
            <div className={styles.stat}>
              <Counter to={3} suffix=" міс" className={styles.statValue} />
              <span className={styles.statLabel}>
                стандартний термін · або індивідуально, з можливістю
                продовження
              </span>
            </div>
          </div>
        </div>
      </div>
    </section>
  );
}