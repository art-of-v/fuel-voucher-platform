import styles from './SectionIndex.module.css';

type Props = {
  current: number;
  total: number;
  label: string;
};

export default function SectionIndex({ current, total, label }: Props) {
  const padded = String(current).padStart(2, '0');
  const totalPadded = String(total).padStart(2, '0');
  return (
    <div className={styles.index} aria-hidden="true">
      <span className={styles.num}>{padded}</span>
      <span className={styles.divider}>/</span>
      <span className={styles.total}>{totalPadded}</span>
      <span className={styles.label}>{label}</span>
    </div>
  );
}