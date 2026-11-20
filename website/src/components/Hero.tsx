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
        {/* Two crops of one photograph, not one squeezed to fit. The wide frame carries the skyline
            and the whole canopy; a phone sees a 2:1 frame as a letterbox, so it gets a 9:16 slice
            cut around the car and the sign instead - the same pixels, at the aspect the screen
            actually has, which is why nothing important is cropped away on either. No third crop
            for tablets: `object-position: right` already spends that range on the interesting half. */}
        <source media="(max-width: 640px)" srcSet="/hero-forecourt-phone.webp" />
        <Image
          className={styles.bgImage}
          src="/hero-forecourt.webp"
          alt=""
          width={1774}
          height={887}
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
