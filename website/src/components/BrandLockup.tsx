import Image from 'next/image';
import styles from './BrandLockup.module.css';

/**
 * The lockup: the lion, then "Fuel Flow" with the brand "F" as the first letter
 * of each word. One committed drawing, so the mark and the three gaps cannot
 * drift apart between the header, the hero and the footer.
 *
 * It is a raster because the wordmark is not typeset: the F is the brand's own
 * glyph, sheared 13.8 degrees to match Inter Tight (the site's face), and kerned
 * to the letters by paint rather than by sidebearing - the F carries none, and
 * its arms overhang its stem. Reproduce it from the master; never retype it.
 *
 * `compact` is the phone mock's own header, drawn at a third of the size, where
 * the lion collapses into a green smudge. It gets the wordmark alone, cut from
 * the same master - the same drawing, minus the part that stops working.
 */
export default function BrandLockup({ compact = false }: { compact?: boolean }) {
  return (
    <span className={`${styles.lockup} ${compact ? styles.compact : ''}`} aria-hidden="true">
      {compact ? (
        <Image className={styles.logo} src="/fuelflow-wordmark.webp" alt="" width={1028} height={160} />
      ) : (
        <Image className={styles.logo} src="/fuelflow-logo.webp" alt="" width={792} height={160} />
      )}
    </span>
  );
}
