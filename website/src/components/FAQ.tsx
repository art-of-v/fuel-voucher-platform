'use client';

import { useState } from 'react';
import styles from './FAQ.module.css';

const faqItems = [
  {
    q: 'Що таке FuelFlow?',
    a: 'Це цифровий паливний гаманець для водіїв і компаній. Ви купуєте пакет пального за актуальною ціною, зберігаєте талон у застосунку й використовуєте його на станції мережі-партнера.',
  },
  {
    q: 'Що таке «пакет»?',
    a: 'Пакет — це визначена кількість літрів одного пального в одній мережі. Один куплений пакет дорівнює одному талону й одній заправці. Набір обсягів залежить від мережі.',
  },
  {
    q: 'Скільки дійсний талон?',
    a: 'Строк задає мережа-постачальник. FuelFlow показує у гаманці саме той строк, який має виданий талон. Якщо він добігає, зверніться у підтримку.',
  },
  {
    q: 'Де можна заправитися?',
    a: 'На станціях мереж-партнерів OKKO, WOG, KLO та UPG. Карта найближчих станцій доступна у застосунку.',
  },
  {
    q: 'Що буде, якщо ціна на пальне зміниться?',
    a: 'Ціна вашого пакета вже зафіксована в момент оплати й не зміниться до його використання.',
  },
  {
    q: 'Що робити, якщо талон не надійшов?',
    a: 'Замовлення обробляється автоматично протягом хвилин. Якщо видати талон неможливо, команда FuelFlow оформить повернення на картку, з якої було сплачено.',
  },
  {
    q: 'Як компанія забезпечує водіїв пальним?',
    a: 'Власник купує пальне в пул компанії й видає талони конкретним водіям. Невикористаний талон можна відкликати, а рух літрів — перевірити в акті звірки.',
  },
  {
    q: 'Як захищені операції?',
    a: 'Вхід підтверджується SMS-кодом, оплата відбувається через Monobank, а критичні операції застосунку захищені підписом пристрою.',
  },
];

export default function FAQ() {
  const [open, setOpen] = useState<number | null>(0);

  return (
    <section className={`lf-section ${styles.section}`} id="faq">
      <div className={`lf-container ${styles.container}`}>
        <header className={styles.head}>
          <p className="lf-eyebrow">Питання і відповіді</p>
          <h2 className={styles.title}>Коротко про те, що важливо до першої заправки.</h2>
          <p className={styles.intro}>
            Не знайшли відповіді? Напишіть у підтримку — команда відповість протягом робочого дня.
          </p>
        </header>

        <div className={styles.list}>
          {faqItems.map((item, index) => {
            const isOpen = open === index;
            const triggerId = `faq-trigger-${index}`;
            const panelId = `faq-panel-${index}`;
            return (
              <div key={item.q} className={`${styles.item} ${isOpen ? styles.itemOpen : ''}`}>
                <button
                  id={triggerId}
                  type="button"
                  className={styles.trigger}
                  aria-expanded={isOpen}
                  aria-controls={panelId}
                  onClick={() => setOpen(isOpen ? null : index)}
                >
                  <span>{item.q}</span>
                  <span className={styles.icon} aria-hidden="true" />
                </button>
                <div
                  id={panelId}
                  className={styles.content}
                  role="region"
                  aria-labelledby={triggerId}
                  hidden={!isOpen}
                >
                  <p>{item.a}</p>
                </div>
              </div>
            );
          })}
        </div>
      </div>
    </section>
  );
}
