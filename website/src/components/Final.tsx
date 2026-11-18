import Image from 'next/image';
import AppStoreBadge from './AppStoreBadge';
import PhoneMock from './PhoneMock';
import StoreQrCard from './StoreQrCard';
import styles from './Final.module.css';

export default function Final() {
  return (
    <section className={styles.section} id="download">
      <Image
        className={styles.media}
        src="/road-journey.jpg"
        alt="Дорога попереду з салону автомобіля"
        width={1600}
        height={1067}
        sizes="100vw"
      />
      <div className={styles.scrim} aria-hidden="true" />
      <div className={`lf-container ${styles.container}`}>
        <div className={styles.copy}>
          <p className="lf-eyebrow">Для особистих поїздок</p>
          <h2 className={styles.title}>Ваше пальне вже у телефоні.</h2>
          <p className={styles.lead}>
            Купуйте пакет за актуальною ціною, зберігайте талон у FuelFlow і
            заправляйтеся у мережі-партнері, коли потрібно.
          </p>
          <div className={styles.actions}>
            <AppStoreBadge />
            <a href="#business-lead" className="lf-btn lf-btn--ghost">
              Для автопарку
            </a>
          </div>
          <div className={styles.contacts}>
            <a href="tel:+380970011771">+380 97 001 1771</a>
            <a href="/support/">Підтримка</a>
            <span>Львів · вул. Городоцька, 128а</span>
          </div>
        </div>

        <div className={styles.product}>
          <PhoneMock />
          <StoreQrCard className={styles.qr} />
        </div>
      </div>
    </section>
  );
}
