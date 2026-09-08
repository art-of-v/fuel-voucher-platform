'use client';

import { useEffect, useRef } from 'react';
import SectionIndex from './SectionIndex';
import styles from './Business.module.css';

const features = [
  {
    title: 'Паливний пул компанії',
    body: 'Купуйте літри за оптовими цінами — весь обсяг зберігається в одному пулі на вашому екрані.',
  },
  {
    title: 'Видача водіям у два дотики',
    body: 'Подаруйте ваучер водієві напряму в застосунку. Він бачить лише свій QR — без доступу до грошей компанії.',
  },
  {
    title: 'Відкликання одним дотиком',
    body: 'Водія звільнено — відкличте ваучер, і він просто перестане працювати. Літри повертаються в пул.',
  },
  {
    title: 'Акт звірки без розбіжностей',
    body: 'Отримано = видано + повернено. Повний пакет документів з ПДВ для бухгалтерії — без Excel і ручних звірок.',
  },
];

export default function Business() {
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
    <section className={`lf-section lf-section--deep ${styles.section}`} id="business" ref={ref}>
      <div className={`lf-container ${styles.container}`}>
        <header className={styles.head}>
          <div>
            <span className="lf-eyebrow lf-eyebrow--accent">Компаніям і автопаркам</span>
            <h2 className={styles.title}>
              Дай водіям пальне. Не гроші компанії.
            </h2>
            <p className={styles.intro}>
              Компанія → паливний пул → водії → QR → пальне.
              Кожен літр під контролем — від закупівлі до колонки.
            </p>
          </div>
          <div className={styles.headRight}>
            <SectionIndex current={7} total={10} label="Бізнес" />
          </div>
        </header>

        <div className={`${styles.flow} ${styles.reveal}`} aria-hidden="true">
          {['Компанія', 'Паливний пул', 'Водії', 'QR', 'Пальне'].map((step, i) => (
            <span className={styles.flowItem} key={step}>
              <span className={styles.flowStep}>{step}</span>
              {i < 4 && <span className={styles.flowArrow} />}
            </span>
          ))}
        </div>

        <div className={styles.grid}>
          {features.map((f, i) => (
            <article
              key={f.title}
              className={`${styles.card} ${styles.reveal}`} data-glow
              style={{ transitionDelay: `${i * 50}ms` }}
            >
              <span className={styles.cardIndex}>
                {String(i + 1).padStart(2, '0')}
              </span>
              <h3 className={styles.cardTitle}>{f.title}</h3>
              <p className={styles.cardBody}>{f.body}</p>
            </article>
          ))}
        </div>

        <div className={`${styles.ctaRow} ${styles.reveal}`}>
          <a href="#contact" className={`lf-btn lf-btn--primary ${styles.cta}`}>
            Підключити компанію
          </a>
          <span className={styles.ctaNote}>
            Видавай · відкликай · контролюй — усе в застосунку
          </span>
        </div>
      </div>
    </section>
  );
}