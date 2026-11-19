import Image from 'next/image';
import styles from './BrandLockup.module.css';

export default function BrandLockup({ compact = false }: { compact?: boolean }) {
  return (
    <span className={`${styles.lockup} ${compact ? styles.compact : ''}`} aria-hidden="true">
      <span className={styles.lion} />
      <Image className={styles.wordmark} src="/fuelflow-wordmark.webp" alt="" width={236} height={86} />
    </span>
  );
}
