'use client';

import { useEffect, useRef } from 'react';
import SectionIndex from './SectionIndex';
import styles from './Prices.module.css';

const priceData = [
  { network: 'OKKO', a95: 4.0, dp: 4.0, gas: 1.2 },
  { network: 'WOG', a95: 4.0, dp: 4.0, gas: 1.2 },
  { network: 'KLO', a95: 3.0, dp: 3.0, gas: 1.0 },
  { network: 'UPG', a95: 2.5, dp: 2.5, gas: 0.8 },
];

function fmt(value: number) {
  return `−${value.toFixed(2)}`;
}

export default function Prices() {
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
    <section className={`lf-section ${styles.section}`} id="prices" ref={ref}>
      <div className={`lf-container ${styles.container}`}>
        <header className={styles.head}>
          <div>
            <span className="lf-eyebrow">Ціна зафіксована</span>
            <h2 className={styles.title}>
              Сьогоднішня ціна = завтрашнє пальне.
            </h2>
            <p className={styles.intro}>
              50 л А-95 сьогодні — за зафіксованою ціною пакета. Ціна на
              колонці зросла на 3 ₴/л — твій ваучер уже врятував 150 ₴.
              Чим більший пакет — тим більша захищена сума.
            </p>
          </div>
          <div className={styles.headRight}>
            <SectionIndex current={5} total={10} label="Ціни" />
          </div>
        </header>

        <div className={styles.grid}>
          {priceData.map((row, i) => (
            <article
              key={row.network}
              className={`${styles.card} ${styles.reveal}`} data-glow
              style={{ transitionDelay: `${i * 50}ms` }}
            >
              <div className={styles.cardHead}>
                <span className={styles.cardIndex}>
                  {String(i + 1).padStart(2, '0')}
                </span>
                <span className={styles.cardName}>{row.network}</span>
              </div>

              <div className={styles.heroPrice}>
                <span className={styles.heroLabel}>A-95</span>
                <div className={styles.heroValue}>
                  <span className={styles.heroMinus}>−</span>
                  {row.a95.toFixed(2)}
                </div>
                <span className={styles.heroUnit}>грн / літр</span>
              </div>

              <div className={styles.secondary}>
                <div className={styles.secondaryRow}>
                  <span className={styles.secondaryLabel}>ДП</span>
                  <span className={styles.secondaryValue}>
                    {fmt(row.dp)}{' '}
                    <span className={styles.secondaryUnit}>грн</span>
                  </span>
                </div>
                <div className={styles.secondaryRow}>
                  <span className={styles.secondaryLabel}>Газ</span>
                  <span className={styles.secondaryValue}>
                    {fmt(row.gas)}{' '}
                    <span className={styles.secondaryUnit}>грн</span>
                  </span>
                </div>
              </div>

              <div className={styles.cardFoot}>
                <a href="#contact" className={styles.cardLink}>
                  Купити літри →
                </a>
              </div>
            </article>
          ))}
        </div>

        <p className={styles.note}>
          * Знижки вказані у гривнях на літр відносно ціни на стелі АЗС.
          Ціна фіксується в момент покупки і не змінюється до заправки.
        </p>
      </div>
    </section>
  );
}