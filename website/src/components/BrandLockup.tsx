import Image from 'next/image';
import styles from './BrandLockup.module.css';

export default function BrandLockup({ compact = false }: { compact?: boolean }) {
  return (
    <span className={`${styles.lockup} ${compact ? styles.compact : ''}`} aria-hidden="true">
      <span className={styles.lion} />
      <Image className={styles.prototype} src="/fuelflow-prototype-logo.webp" alt="" width={228} height={84} />
    </span>
  );
}
