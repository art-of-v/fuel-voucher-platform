import styles from './PhoneMock.module.css';

const modules = [
  [1, 1, 1, 0, 1, 0, 1, 1, 1],
  [1, 0, 1, 0, 0, 1, 1, 0, 1],
  [1, 1, 1, 0, 1, 0, 1, 1, 1],
  [0, 0, 0, 1, 0, 1, 0, 0, 0],
  [1, 0, 1, 0, 1, 0, 1, 0, 1],
  [0, 1, 0, 1, 0, 1, 0, 1, 0],
  [1, 1, 1, 0, 1, 1, 1, 0, 1],
  [1, 0, 1, 1, 0, 0, 1, 1, 0],
  [1, 1, 1, 0, 1, 0, 1, 0, 1],
];

export default function PhoneMock({ className }: { className?: string }) {
  return (
    <div className={`${styles.phone} ${className ?? ''}`} aria-hidden="true">
      <div className={styles.notch} />
      <div className={styles.screen}>
        <div className={styles.topBar}>
          <span className={styles.brand}>
            FUELFLOW<span className={styles.brandDot}>.</span>
          </span>
          <span className={styles.chip}>Гаманець</span>
        </div>

        <div className={styles.balance}>
          <div className={styles.balanceLabel}>Паливо в гаманці</div>
          <div className={styles.balanceValue}>
            <span className={styles.liters}>50</span>
            <span className={styles.unit}>літрів</span>
          </div>
          <div className={styles.balanceFoot}>
            <span className={styles.money}>₴2 920 · А-95</span>
            <span className={styles.chip}>OKKO</span>
          </div>
        </div>

        <div className={styles.qrCard}>
          <svg viewBox="0 0 9 9" className={styles.qrSvg} shapeRendering="crispEdges">
            {modules.map((row, y) =>
              row.map((on, x) =>
                on ? <rect key={`${x}-${y}`} x={x} y={y} width={1} height={1} /> : null
              )
            )}
          </svg>
        </div>

        <div className={styles.ctaBtn}>Показати на колонці</div>
      </div>
    </div>
  );
}