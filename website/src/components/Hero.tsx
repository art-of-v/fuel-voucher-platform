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
            Сервіс для транспортного бізнесу
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
            Договори з постачальниками, закуплене пальне, коди для водіїв та
            звірка витрат в одному застосунку
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
            <dt>Сервіс</dt>
            <dd>Договори · закуплення · розподіл · документи</dd>
          </div>
          <div>
            <dt>Для водіїв</dt>
            <dd>Код на пальні · ціна зафіксована</dd>
          </div>
        </dl>
      </div>
    </section>
  );
}
