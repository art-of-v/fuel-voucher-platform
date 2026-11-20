'use client';

import { useState } from 'react';
import styles from './FAQ.module.css';

const faqItems = [
  {
    q: 'Що таке FuelFlow?',
    a: 'Комплексні рішення у вигляді сервісів для всіх, чия робота чи побут пов\'язані з пальним і транспортом: постачальників, транспортних компаній і водіїв. Договори, закуплення, коди, контроль залишків і документи в одному застосунку.',
  },
  {
    q: 'Ви продаєте пальне?',
    a: 'Ні. Ми продаємо сервіс: дбаємо, щоб платіжні інструменти паливних компаній знаходили своїх клієнтів без зайвої бюрократії, нам важливо, щоб водії купували пальне за привабливими цінами, а власники бізнесу мали доступ до бухгалтерії своїх транспортних підприємств 24/7.',
  },
  {
    q: 'Ви чийсь конкурент?',
    a: 'Ні. Ми не мережа і не постачальник пального. Ми робимо їхню роботу зручнішою, а ваші процеси простішими й прозорішими.',
  },
  {
    q: 'Що входить у сервіс для компанії?',
    a: 'Договори з постачальниками, закуплення в будь-який момент, видача кодів працівникам, повернення невикористаного в запас компанії, призупинення коду на час відпустки та прозорий залишок по кожному учаснику.',
  },
  {
    q: 'А документи для бухгалтерії?',
    a: 'Акти звірки, авансові рахунки, податкові накладні та закриття ПДВ за період. Це окремий напрямок продукту, і ми готуємо його зараз. Сьогодні весь рух пального по компанії вже видно в застосунку.',
  },
  {
    q: 'Де можна заправитися?',
    a: 'На станціях мереж-партнерів OKKO, WOG, KLO та UPG. Карта найближчих станцій доступна в застосунку.',
  },
  {
    q: 'Що буде, якщо ціна на пальне зміниться?',
    a: 'Ціна фіксується в момент оплати й не змінюється протягом терміну дії пакета. Термін надрукований на кожному талоні.',
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
          <h2 className={styles.title}>Питання, які нам ставлять</h2>
          <p className={styles.intro}>
            Не знайшли відповіді? Напишіть у підтримку, команда відповість протягом
            робочого дня
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
