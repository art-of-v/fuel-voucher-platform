import Image from 'next/image';
import type { CSSProperties } from 'react';
import AppStoreBadge from './AppStoreBadge';
import BrandLockup from './BrandLockup';
import styles from './Hero.module.css';

const STEP = 90;
const delay = (step: number) => ({ '--reveal-delay': `${step * STEP}ms` }) as CSSProperties;

export default function Hero() {
  return (
    <section className={styles.hero} id="top">
      <picture className={styles.media}>
        <Image
          className={styles.bgImage}
          src="/hero-prototype-clean.webp"
          alt=""
          width={2000}
          height={919}
          sizes="100vw"
          priority
        />
      </picture>
      <div className={styles.scrim} aria-hidden="true" />
      <div className={styles.heroBrand}>
        <BrandLockup />
      </div>
      <Image
        className={styles.stationLogo}
        src="/fuelflow-prototype-logo.webp"
        alt=""
        width={228}
        height={84}
        aria-hidden="true"
      />

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
