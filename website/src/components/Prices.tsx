'use client';

import Image from 'next/image';
import { useEffect, useState } from 'react';
import PhoneMock from './PhoneMock';
import styles from './Prices.module.css';
import {
  fetchCatalogue,
  formatLiters,
  formatMoney,
  groupByNetwork,
  pickReferencePackage,
  type NetworkOffer,
  type PublicPackage,
} from '../lib/catalogue';

type State =
  | { status: 'loading' }
  | { status: 'ready'; networks: NetworkOffer[]; reference: PublicPackage | null }
  | { status: 'error' };

export default function Prices() {
  const [state, setState] = useState<State>({ status: 'loading' });

  useEffect(() => {
    let alive = true;
    fetchCatalogue()
      .then((rows) => {
        if (alive) {
          setState({
            status: 'ready',
            networks: groupByNetwork(rows),
            reference: pickReferencePackage(rows),
          });
        }
      })
      .catch(() => {
        if (alive) setState({ status: 'error' });
      });
    return () => {
      alive = false;
    };
  }, []);

  return (
    <section className={`lf-section ${styles.section}`} id="prices">
      <span id="numbers" className={styles.anchor} aria-hidden="true" />
      <span id="networks" className={styles.anchor} aria-hidden="true" />
      <div className={`lf-container ${styles.container}`}>
        <header className={styles.head}>
          <div>
            <p className="lf-eyebrow">Живий каталог</p>
            <h2 className={styles.title}>Ціна фіксується. Пальне лишається вашим.</h2>
          </div>
          <p className={styles.intro}>
            Тут немає вручну набраних тарифів. Сторінка читає той самий каталог,
            який бачить застосунок FuelFlow.
          </p>
        </header>

        <div className={styles.stage}>
          <div className={styles.visual}>
            <Image
              src="/fuel-nozzle.jpg"
              alt="Паливний пістолет на заправній колонці"
              width={1600}
              height={1067}
              sizes="(max-width: 1050px) 100vw, 42vw"
            />
            <div className={styles.reference} aria-live="polite">
              <span>Орієнтир каталогу</span>
              <strong>
                {state.status === 'ready' && state.reference
                  ? state.reference.finalPricePerLiter.toFixed(2).replace('.', ',')
                  : '—'}
              </strong>
              <small>грн / літр</small>
              {state.status === 'ready' && state.reference ? (
                <p>
                  {state.reference.brandSlug.toUpperCase()} · {state.reference.fuelName}
                </p>
              ) : null}
            </div>
          </div>

          <div className={styles.ledger}>
            {state.status === 'loading' ? (
              <p className={styles.state} role="status">Завантажуємо актуальний каталог…</p>
            ) : null}

            {state.status === 'error' ? (
              <div className={styles.state} role="status">
                <strong>Каталог тимчасово недоступний.</strong>
                <p>Актуальні ціни залишаються доступними у застосунку FuelFlow.</p>
              </div>
            ) : null}

            {state.status === 'ready' && state.networks.length === 0 ? (
              <div className={styles.state} role="status">
                <strong>Доступні пакети оновлюються.</strong>
                <p>Перевірте каталог у застосунку трохи пізніше.</p>
              </div>
            ) : null}

            {state.status === 'ready'
              ? state.networks.map((offer) => (
                  <article key={offer.brandSlug} className={styles.network}>
                    <div className={styles.networkName}>
                      <h3>{offer.name}</h3>
                      <p>{offer.cheapestFuel}</p>
                    </div>
                    <div className={styles.networkPrice}>
                      <strong>{offer.fromPerLiter.toFixed(2).replace('.', ',')}</strong>
                      <span>грн / л</span>
                    </div>
                    <div className={styles.networkMeta}>
                      <span>{formatLiters(offer.volumes)} л</span>
                      {offer.fuels.some((fuel) => fuel.saving !== null) ? (
                        <span>
                          економія до{' '}
                          {formatMoney(
                            Math.max(...offer.fuels.map((fuel) => fuel.saving ?? 0)),
                          )}
                        </span>
                      ) : (
                        <span>актуальна ціна FuelFlow</span>
                      )}
                    </div>
                  </article>
                ))
              : null}

            <p className={styles.note}>
              Ціна вказана за літр у найдешевшому доступному пакеті мережі.
              Порівняння з колонкою показується лише там, де сервер має актуальну
              ціну на АЗС.
            </p>
          </div>
        </div>

        <div className={styles.phoneBand}>
          <div>
            <p className="lf-eyebrow">Для водія</p>
            <h3>Талон, ціна й мережа — в одному місці.</h3>
            <p>
              Оберіть пакет у каталозі, оплатіть через Monobank і відкрийте талон
              у своєму гаманці FuelFlow.
            </p>
          </div>
          <PhoneMock />
        </div>
      </div>
    </section>
  );
}
