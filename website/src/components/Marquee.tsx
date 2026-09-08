import styles from './Marquee.module.css';

const partners = ['OKKO', 'WOG', 'KLO', 'UPG'];

export default function Marquee() {
  const group = [...partners, ...partners];
  return (
    <div className={styles.marquee} aria-hidden="true">
      <div className={styles.track}>
        {[0, 1].map((g) => (
          <div className={styles.group} key={g}>
            {group.map((name, i) => (
              <span className={styles.item} key={`${g}-${i}`}>
                <span className={styles.index}>{String((i % partners.length) + 1).padStart(2, '0')}</span>
                <span className={styles.name}>{name}</span>
                <span className={styles.dot} />
              </span>
            ))}
          </div>
        ))}
      </div>
    </div>
  );
}