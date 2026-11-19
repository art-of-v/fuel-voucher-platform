import Image from 'next/image';
import type { CSSProperties } from 'react';
import AppStoreBadge from './AppStoreBadge';
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
          width={1212}
          height={565}
          sizes="100vw"
          priority
        />
      </picture>
      <div className={styles.scrim} aria-hidden="true" />

      <div className={`${styles.container} lf-container`}>
        <div className={styles.copy}>
          <p className={`${styles.kicker} ${styles.reveal}`} style={delay(0)}>
            Сервіс навколо пального
          </p>
          <h1 className={styles.title}>
            <span className={`${styles.titleLine} ${styles.reveal}`} style={delay(1)}>
              Пальне
            </span>
            <span className={`${styles.titleLine} ${styles.reveal} ${styles.accent}`} style={delay(2)}>
              під контролем
            </span>
          </h1>
          <p className={`${styles.lead} ${styles.reveal}`} style={delay(3)}>
            Комплексні рішення для постачальників, автопарків і водіїв: від
            договору й закуплення до коду в телефоні
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
            <dt>Для кого</dt>
            <dd>Постачальники · автопарки · водії</dd>
          </div>
          <div>
            <dt>Сервіс</dt>
            <dd>Договори · коди · розподіл · документи</dd>
          </div>
          <div>
            <dt>Мережі</dt>
            <dd>OKKO · WOG · KLO · UPG</dd>
          </div>
        </dl>
      </div>
    </section>
  );
}
