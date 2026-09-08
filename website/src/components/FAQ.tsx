'use client';

import { useEffect, useRef, useState } from 'react';
import SectionIndex from './SectionIndex';
import styles from './FAQ.module.css';

const faqItems = [
  {
    q: 'Що таке FuelFlow?',
    a: 'Це цифровий паливний гаманець. Ви купуєте паливо в літрах за сьогоднішньою ціною — воно зберігається у телефоні як цифровий ваучер. Заправляєтеся, коли потрібно, пред’явивши QR-код на станції.',
  },
  {
    q: 'Де можна заправитися?',
    a: 'На всіх станціях мереж-партнерів: OKKO, WOG, KLO, UPG. Карта найближчих станцій вбудована в застосунок.',
  },
  {
    q: 'Що буде, якщо ціна на пальне зміниться?',
    a: 'Нічого. Ваші літри вже оплачені за зафіксованою ціною. Скільки б пальне не коштувало на колонці — ви платите стільки, скільки заплатили. Різниця — ваша економія.',
  },
  {
    q: 'А якщо ваучер не надійшов після оплати?',
    a: 'Замовлення виконується автоматично протягом хвилин. Якщо ваучерів обраної мережі немає в наявності — невикористана сума повертається на вашу картку без жодних заяв.',
  },
  {
    q: 'Як компанія забезпечує паливом водіїв?',
    a: 'Власник створює паливний пул компанії, купує літри за оптовими цінами і роздає ваучери водіям у два дотики. Водія звільнено — ваучер відкликано, і він просто перестає працювати. Акт звірки формується автоматично.',
  },
  {
    q: 'Чи безпечно це?',
    a: 'Так. Оплата проходить через Monobank із криптографічним підписом кожного платежу. Вхід — за номером телефону з SMS-кодом, а кожна покупка підтверджується підписом вашого пристрою. Жоден ваучер не можна використати двічі.',
  },
];

export default function FAQ() {
  const [open, setOpen] = useState<number | null>(0);
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
    <section className={`lf-section ${styles.section}`} id="faq" ref={ref}>
      <div className={`lf-container ${styles.container}`}>
        <header className={styles.head}>
          <div>
            <span className="lf-eyebrow">FAQ</span>
            <h2 className={styles.title}>
              Поширені питання.
            </h2>
            <p className={styles.intro}>
              Якщо відповіді немає — залиште заявку, і ми передзвонимо
              протягом 15 хвилин у робочий час.
            </p>
          </div>
          <div className={styles.headRight}>
            <SectionIndex current={10} total={10} label="FAQ" />
          </div>
        </header>

        <div className={styles.list}>
          {faqItems.map((item, i) => {
            const isOpen = open === i;
            return (
              <div
                key={i}
                className={`${styles.item} ${isOpen ? styles.itemOpen : ''} ${styles.reveal}`}
              >
                <button
                  className={styles.trigger}
                  onClick={() => setOpen(isOpen ? null : i)}
                  aria-expanded={isOpen}
                >
                  <span className={styles.triggerIndex}>
                    {String(i + 1).padStart(2, '0')}
                  </span>
                  <span className={styles.triggerText}>{item.q}</span>
                  <span className={styles.triggerIcon} aria-hidden="true">
                    <span className={styles.iconBar} />
                    <span
                      className={`${styles.iconBar} ${styles.iconBarAlt} ${
                        isOpen ? styles.iconBarHidden : ''
                      }`}
                    />
                  </span>
                </button>
                <div
                  className={styles.content}
                  style={{
                    gridTemplateRows: isOpen ? '1fr' : '0fr',
                  }}
                >
                  <div className={styles.contentInner}>
                    <p className={styles.answer}>{item.a}</p>
                  </div>
                </div>
              </div>
            );
          })}
        </div>
      </div>
    </section>
  );
}