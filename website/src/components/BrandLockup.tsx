import Image from 'next/image';
import styles from './BrandLockup.module.css';

/**
 * The brand as the prototype draws it: the lime "F" bolt and the words, one
 * committed asset. The lion is the product's mark - it stays in the icons, the
 * favicon and the launcher art - but it is not part of the lockup.
 */
export default function BrandLockup({ compact = false }: { compact?: boolean }) {
  return (
    <span className={`${styles.lockup} ${compact ? styles.compact : ''}`} aria-hidden="true">
      <Image className={styles.logo} src="/fuelflow-logo.webp" alt="" width={339} height={81} />
    </span>
  );
}
