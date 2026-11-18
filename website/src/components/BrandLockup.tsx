import Image from 'next/image';
import styles from './BrandLockup.module.css';

export default function BrandLockup({ compact = false }: { compact?: boolean }) {
  return (
    <span className={`${styles.lockup} ${compact ? styles.compact : ''}`} aria-hidden="true">
      <Image className={styles.mark} src="/fuelflow-mark.svg" alt="" width={64} height={40} />
      <span className={styles.wordmark}>Fuel Flow</span>
    </span>
  );
}
