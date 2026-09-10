import styles from './Footer.module.css';

export default function Footer() {
  const year = new Date().getFullYear();
  return (
    <footer className={styles.footer}>
      <div className={`lf-container ${styles.container}`}>
        <div className={styles.top}>
          <a href="#top" className={styles.logo}>
            <span className={styles.logoMark}>FF</span>
            <span className={styles.logoText}>FuelFlow</span>
          </a>

          <nav className={styles.nav} aria-label="Додаткова навігація">
            <a href="#products">Продукти</a>
            <a href="#how">Процес</a>
            <a href="#prices">Ціни</a>
            <a href="#faq">FAQ</a>
            <a href="/support/">Підтримка</a>
            <a href="#contact">Контакти</a>
          </nav>
        </div>

        <div className={styles.bottom}>
          <div className={styles.copy}>
            © {year} FuelFlow. Всі права захищено.
          </div>
          <div className={styles.legal}>
            <a href="#">Публічна оферта</a>
            <span className={styles.legalDot}>·</span>
            <a href="/privacy/">Політика конфіденційності</a>
          </div>
          <div className={styles.meta}>
            <span className={styles.metaDot} />
            Львів · вул. Городоцька, 128а
          </div>
        </div>
      </div>
    </footer>
  );
}