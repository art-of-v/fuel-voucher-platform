import { APP_STORE_NAME, APP_STORE_QR_SRC } from '@/config/store';
import styles from './StoreQrCard.module.css';

/**
 * Scannable counterpart to the App Store button: the QR encodes the exact same
 * listing, so a visitor standing at the page with a phone in hand does not have
 * to hunt for the button. The image is a build-time SVG (see
 * scripts/generate-store-qr.mjs) on a solid white plate, which is what camera
 * decoders need to lock on quickly.
 */
export default function StoreQrCard({ className }: { className?: string }) {
  return (
    <div className={`${styles.card} ${className ?? ''}`}>
      <div className={styles.qrPlate}>
        <img
          src={APP_STORE_QR_SRC}
          alt={`QR-код для встановлення ${APP_STORE_NAME} з App Store`}
          width={37}
          height={37}
          loading="lazy"
          decoding="async"
        />
      </div>
      <div className={styles.copy}>
        <span className={styles.eyebrow}>Для iPhone</span>
        <span className={styles.title}>Скануй — і застосунок встановлено</span>
        <span className={styles.text}>
          Наведи камеру на код, App Store відкриється сам.
        </span>
        <span className={styles.meta}>{APP_STORE_NAME} · безкоштовно</span>
      </div>
    </div>
  );
}