'use client';

import { useEffect, useRef, useState } from 'react';
import SectionIndex from './SectionIndex';
import styles from './Prices.module.css';
import {
  fetchCatalogue,
  formatMoney,
  formatLiters,
  groupByNetwork,
  type NetworkOffer,
} from '../lib/catalogue';

type State =
  | { status: 'loading' }
  | { status: 'ready'; networks: NetworkOffer[] }
  | { status: 'error' };

export default function Prices() {
  const [state, setState] = useState<State>({ status: 'loading' });
  const ref = useRef<HTMLElement>(null);

  useEffect(() => {
    let alive = true;
    fetchCatalogue()
      .then((rows) => {
        if (alive) setState({ status: 'ready', networks: groupByNetwork(rows) });
      })
      .catch(() => {
        if (alive) setState({ status: 'error' });
      });
    return () => {
      alive = false;
    };
  }, []);

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
              Ціна фіксується в момент оплати й не змінюється до заправки.
              Нижче — актуальний каталог пакетів, які зараз є в наявності: він
              читається з нашого сервера, а не зі сторінки, тож не може
              розійтися з тим, що ти побачиш у застосунку.
            </p>
          </div>
          <div className={styles.headRight}>
            <SectionIndex current={5} total={10} label="Ціни" />
          </div>
        </header>

        {state.status === 'loading' && (
          <p className={styles.note}>Завантажуємо актуальні ціни…</p>
        )}

        {state.status === 'error' && (
          <p className={styles.note}>
            Не вдалося завантажити каталог прямо зараз. Актуальні ціни завжди
            видно в застосунку — або зателефонуйте нам, і ми підкажемо.
          </p>
        )}

        {state.status === 'ready' && (
          <>
            <div className={styles.grid}>
              {state.networks.map((offer, i) => (
                <article
                  key={offer.stationId}
                  className={`${styles.card} ${styles.reveal}`}
                  data-glow
                  style={{ transitionDelay: `${i * 50}ms` }}
                >
                  <div className={styles.cardHead}>
                    <span className={styles.cardIndex}>
                      {String(i + 1).padStart(2, '0')}
                    </span>
                    <span className={styles.cardName}>{offer.name}</span>
                  </div>

                  <div className={styles.heroPrice}>
                    <span className={styles.heroLabel}>Від</span>
                    <div className={styles.heroValue}>
                      {offer.fromPerLiter.toFixed(2).replace('.', ',')}
                    </div>
                    <span className={styles.heroUnit}>грн / літр</span>
                  </div>

                  <ul className={styles.fuelList}>
                    {offer.fuels.map((fuel) => (
                      <li key={fuel.fuelName} className={styles.fuelRow}>
                        <span className={styles.fuelName}>{fuel.fuelName}</span>
                        <span className={styles.fuelValue}>
                          {fuel.perLiter.toFixed(2).replace('.', ',')}
                          {fuel.saving !== null && (
                            <span className={styles.fuelSaving}>
                              {' '}
                              · {formatMoney(fuel.saving)} дешевше за колонку
                            </span>
                          )}
                        </span>
                      </li>
                    ))}
                  </ul>

                  <div className={styles.cardFoot}>
                    <span className={styles.cardNote}>
                      Обсяги: {formatLiters(offer.volumes)} л
                    </span>
                  </div>
                </article>
              ))}
            </div>

            <p className={styles.note}>
              * Ціни — за літр, для найдешевшого пакета цієї мережі. Обсяги
              відрізняються від мережі до мережі: одна й та сама мережа може
              мати 2 і 50 л, а інша — лише 10 і 20. Порівняння «дешевше за
              колонку» показується тільки там, де ми знаємо ціну на колонці
              цієї позиції; решта цін — це наша ціна, без вигаданої вигоди.
            </p>
          </>
        )}

        <p className={styles.cta}>
          <a href="#contact" className={styles.cardLink}>
            Купити літри →
          </a>
        </p>
      </div>
    </section>
  );
}
