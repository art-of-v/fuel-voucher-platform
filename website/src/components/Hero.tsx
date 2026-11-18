import type { CSSProperties } from 'react';
import AppStoreBadge from './AppStoreBadge';
import BrandLockup from './BrandLockup';
import PhoneMock from './PhoneMock';
import styles from './Hero.module.css';

const STEP = 90;
const delay = (step: number) => ({ '--reveal-delay': `${step * STEP}ms` }) as CSSProperties;

export default function Hero() {
  return (
    <section className={styles.hero} id="top">
      <div className={styles.stage} aria-hidden="true">
        <div className={styles.stageGrid} />
        <div className={styles.stageBrand}>
          <BrandLockup />
        </div>
        <div className={styles.phoneWrap}>
          <PhoneMock />
        </div>
        <div className={styles.controlCard}>
          <span>Fleet control</span>
          <strong>12 500 л</strong>
          <small>24 водії · 4 мережі</small>
        </div>
        <div className={styles.lockedCard}>
          <span>Ціна зафіксована</span>
          <strong>50,00 л</strong>
        </div>
      </div>

      <div className={`${styles.container} lf-container`}>
        <div className={styles.copy}>
          <p className={`${styles.kicker} ${styles.reveal}`} style={delay(0)}>
            Пальне для компаній і автопарків
          </p>
          <h1 className={styles.title}>
            <span className={`${styles.titleLine} ${styles.reveal}`} style={delay(1)}>
              Пальне
            </span>
            <span className={`${styles.titleLine} ${styles.reveal}`} style={delay(2)}>
              під контролем.
            </span>
          </h1>
          <p className={`${styles.lead} ${styles.reveal}`} style={delay(3)}>
            Купуйте літри в паливний пул, видавайте талони водіям і бачте рух
            пального від оплати до колонки.
          </p>
          <div className={`${styles.actions} ${styles.reveal}`} style={delay(4)}>
            <a href="#business-lead" className="lf-btn lf-btn--primary">
              Підключити автопарк
            </a>
            <AppStoreBadge />
          </div>
        </div>

        <dl className={styles.facts}>
          <div>
            <dt>Мережі</dt>
            <dd>OKKO · WOG · KLO · UPG</dd>
          </div>
          <div>
            <dt>Контроль</dt>
            <dd>Видача · відкликання · звірка</dd>
          </div>
          <div>
            <dt>Для водіїв</dt>
            <dd>Талон у застосунку</dd>
          </div>
        </dl>
      </div>
    </section>
  );
}
