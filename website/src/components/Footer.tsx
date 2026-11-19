'use client';

import { usePathname } from 'next/navigation';
import { homeAnchor } from '@/lib/anchors';
import BrandLockup from './BrandLockup';
import styles from './Footer.module.css';

const navLinks = [
  { name: 'Сервіси', href: '#services' },
  { name: 'Паливний пул', href: '#business' },
  { name: 'Застосунок', href: '#app' },
  { name: 'Ціни', href: '#prices' },
  { name: 'Як це працює', href: '#how' },
  { name: 'Підтримка', href: '/support/' },
];

export default function Footer() {
  const year = new Date().getFullYear();
  const pathname = usePathname();
  const hrefFor = (href: string) =>
    href.startsWith('#') ? homeAnchor(pathname, href) : href;

  return (
    <footer className={styles.footer}>
      <div className={`lf-container ${styles.container}`}>
        <div className={styles.brand}>
          <a href={homeAnchor(pathname, '#top')} className={styles.logo} aria-label="Fuel Flow — на головну">
            <BrandLockup />
          </a>
          <p>Сервіс для транспортного бізнесу: пул, розподіл, звірка, документи.</p>
        </div>

        <nav className={styles.nav} aria-label="Додаткова навігація">
          {navLinks.map((link) => (
            <a key={link.href} href={hrefFor(link.href)}>
              {link.name}
            </a>
          ))}
        </nav>

        <div className={styles.contact}>
          <a href="tel:+380970011771">+380 97 001 1771</a>
          <span>Пн–Пт · 09:00–18:00</span>
          <span>79023, Львів · вул. Городоцька, 128а</span>
        </div>

        <div className={styles.bottom}>
          <span>© {year} FuelFlow</span>
          <div>
            <a href="/terms/">Публічна оферта</a>
            <a href="/privacy/">Політика конфіденційності</a>
          </div>
        </div>
      </div>
    </footer>
  );
}
