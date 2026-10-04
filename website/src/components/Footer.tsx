'use client';

import { usePathname } from 'next/navigation';
import { homeAnchor } from '@/lib/anchors';
import styles from './Footer.module.css';

const navLinks = [
  { name: 'Застосунок', href: '#app' },
  { name: 'Процес', href: '#how' },
  { name: 'Ціни', href: '#prices' },
  { name: 'FAQ', href: '#faq' },
  { name: 'Підтримка', href: '/support/' },
  { name: 'Контакти', href: '#contact' },
];

export default function Footer() {
  const year = new Date().getFullYear();
  const pathname = usePathname();
  return (
    <footer className={styles.footer}>
      <div className={`lf-container ${styles.container}`}>
        <div className={styles.top}>
          <a href={homeAnchor(pathname, '#top')} className={styles.logo}>
            <span className={styles.logoMark}>FF</span>
            <span className={styles.logoText}>FuelFlow</span>
          </a>

          <nav className={styles.nav} aria-label="Додаткова навігація">
            {navLinks.map((link) => (
              <a
                key={link.href}
                href={
                  link.href.startsWith('#')
                    ? homeAnchor(pathname, link.href)
                    : link.href
                }
              >
                {link.name}
              </a>
            ))}
          </nav>
        </div>

        <div className={styles.bottom}>
          <div className={styles.copy}>
            © {year} FuelFlow. Всі права захищено.
          </div>
          <div className={styles.legal}>
            <a href="/terms/">Публічна оферта</a>
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