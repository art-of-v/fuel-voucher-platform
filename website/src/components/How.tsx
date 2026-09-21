'use client';

import { useEffect, useRef } from 'react';
import SectionIndex from './SectionIndex';
import styles from './How.module.css';

const steps = [
  {
    code: '01',
    title: 'Купуй',
    body: 'Обери обсяг: 10, 20 або 50 літрів. А-95, ДП або газ на карті станцій OKKO, WOG, KLO, UPG.',
    meta: '10 л · 20 л · 50 л',
  },
  {
    code: '02',
    title: 'Фіксуй',
    body: 'Сьогоднішня ціна зафіксована в момент оплати. ₴99,20/л — і більше не зміниться.',
    meta: 'Price locked',
  },
  {
    code: '03',
    title: 'Зберігай',
    body: 'Літри живуть у гаманці FuelFlow. Це не папірець — це цифровий актив у телефоні.',
    meta: 'FuelFlow wallet',
  },
  {
    code: '04',
    title: 'Показуй',
    body: 'Подався на станцію — відкрив ваучер і показав QR-код сканеру. Одним пальцем.',
    meta: 'QR voucher',
  },
  {
    code: '05',
    title: 'Заправляйся',
    body: 'Колонка працює. Невикористані літри залишаються у гаманці до наступної заправки.',
    meta: 'Fuel',
  },
];

export default function How() {
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
    <section className={`lf-section lf-section--deep ${styles.section}`} id="how" ref={ref}>
      <div className="lf-grid-bg" aria-hidden="true" />
      <div className={`lf-container ${styles.container}`}>
        <header className={styles.head}>
          <div>
            <span className="lf-eyebrow">Як це працює</span>
            <h2 className={styles.title}>
              П&apos;ять кроків.
              <br />
              Нуль паперу.
            </h2>
          </div>
          <div className={styles.headRight}>
            <SectionIndex current={2} total={10} label="Продукт" />
          </div>
        </header>

        <ol className={styles.steps}>
          {steps.map((step) => (
            <li key={step.code} className={styles.step}>
              <span className={styles.stepCode}>{step.code}</span>
              <h3 className={styles.stepTitle}>{step.title}</h3>
              <p className={styles.stepBody}>{step.body}</p>
              <span className={styles.stepTime}>
                <span className={styles.stepTimeLabel}>{step.meta}</span>
              </span>
            </li>
          ))}
        </ol>
      </div>
    </section>
  );
}