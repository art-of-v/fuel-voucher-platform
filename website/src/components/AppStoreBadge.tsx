import { APP_STORE_URL } from '@/config/store';
import AppleGlyph from './AppleGlyph';
import styles from './AppStoreBadge.module.css';

/**
 * The canonical "get the app" control: a real, tappable link to the live App
 * Store listing. Inverted (light on dark) because every surface it sits on is
 * black — the standard black-on-white badge would disappear into the page.
 */
export default function AppStoreBadge({
  className,
  label = 'Завантажити в',
}: {
  className?: string;
  label?: string;
}) {
  return (
    <a
      href={APP_STORE_URL}
      target="_blank"
      rel="noopener noreferrer"
      className={`${styles.badge} ${className ?? ''}`}
      data-magnetic
    >
      <AppleGlyph size={26} className={styles.mark} />
      <span className={styles.copy}>
        <span className={styles.label}>{label}</span>
        <span className={styles.name}>App Store</span>
      </span>
    </a>
  );
}