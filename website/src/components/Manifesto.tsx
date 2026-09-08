'use client';

import { useEffect, useRef } from 'react';
import SectionIndex from './SectionIndex';
import styles from './Manifesto.module.css';

const statements = [
  {
    code: 'I',
    headline: 'Пальне — це цифра. Цифра живе в телефоні.',
    body:
      'FuelFlow перетворює паливо на цифровий продукт. Гроші стають літрами, літри — ваучером, ваучер — QR-кодом. Не папірцева картка. Не талон. Цифровий паливний гаманець.',
  },
  {
    code: 'II',
    headline: 'Ціна на колонці зростає. Твоя — ні.',
    body:
      'Купуй літри за сьогоднішньою ціною — вона зафіксована назавжди. Через місяць пальне може коштувати більше, але твої літри вже оплачені. Різниця — твоя економія.',
  },
  {
    code: 'III',
    headline: 'Показав QR. Заправився.',
    body:
      'Жодних карток на касі, жодної готівки, жодних чеків. Один код у телефоні — і колонка працює. Це найкоротший шлях від рішення до заправки.',
  },
];

export default function Manifesto() {
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
    <section className={`lf-section ${styles.section}`} id="manifesto" ref={ref}>
      <div className={`lf-container ${styles.container}`}>
        <header className={styles.head}>
          <div className={styles.headLeft}>
            <span className="lf-eyebrow">Маніфест</span>
            <h2 className={styles.title}>
              Пальне вже у твоєму телефоні.
            </h2>
          </div>
          <div className={styles.headRight}>
            <SectionIndex current={9} total={10} label="Гаманець" />
          </div>
        </header>

        <ol className={styles.list}>
          {statements.map((s) => (
            <li key={s.code} className={styles.item}>
              <div className={styles.itemMarker}>
                <span className={styles.itemCode}>{s.code}</span>
              </div>
              <div className={styles.itemBody}>
                <h3 className={styles.itemHeadline}>{s.headline}</h3>
                <p className={styles.itemText}>{s.body}</p>
              </div>
            </li>
          ))}
        </ol>
      </div>
    </section>
  );
}