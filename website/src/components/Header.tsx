'use client';

import { useEffect, useState } from 'react';
import { usePathname } from 'next/navigation';
import styles from './Header.module.css';

const navLinks = [
  { name: 'Мережі', href: '#networks' },
  { name: 'Як це працює', href: '#how' },
  { name: 'Ціни', href: '#prices' },
  { name: 'Бізнес', href: '#business' },
  { name: 'Питання', href: '#faq' },
  { name: 'Підтримка', href: '/support/' },
];

export default function Header() {
  const [scrolled, setScrolled] = useState(false);
  const [open, setOpen] = useState(false);
  const pathname = usePathname();

  // Anchors only exist on the home page; from /support they must be prefixed
  // with "/" so "Ціни" navigates back to /#prices instead of dying silently.
  const prefix = pathname === '/' ? '' : '/';

  useEffect(() => {
    const onScroll = () => setScrolled(window.scrollY > 24);
    onScroll();
    window.addEventListener('scroll', onScroll, { passive: true });
    return () => window.removeEventListener('scroll', onScroll);
  }, []);

  useEffect(() => {
    if (open) document.body.style.overflow = 'hidden';
    else document.body.style.overflow = '';
    return () => {
      document.body.style.overflow = '';
    };
  }, [open]);

  return (
    <>
      <header
        className={`${styles.header} ${scrolled ? styles.scrolled : ''}`}
      >
        <div className={`${styles.container} lf-container`}>
          <a
            href={prefix ? '/#top' : '#top'}
            className={styles.logo}
            aria-label="FuelFlow — на головну"
          >
            <span className={styles.logoMark}>FF</span>
            <span className={styles.logoText}>FuelFlow</span>
          </a>
          <nav
            className={`${styles.nav} ${scrolled ? styles.navVisible : ''}`}
            aria-label="Основна навігація"
          >
            {navLinks.map((link) => (
              <a
                key={link.href}
                href={link.href.startsWith('#') ? `${prefix}${link.href}` : link.href}
                className={styles.navLink}
              >
                {link.name}
              </a>
            ))}
          </nav>

          <div className={styles.right}>
            <a href="tel:+380970011771" className={styles.phone}>
              +380 97 001 1771
            </a>
            <a
              href={prefix ? '/#contact' : '#contact'}
              className={`lf-btn lf-btn--primary ${styles.cta}`}
            >
              Завантажити
            </a>
          </div>

          <button
            className={styles.burger}
            aria-label={open ? 'Закрити меню' : 'Відкрити меню'}
            aria-expanded={open}
            onClick={() => setOpen((v) => !v)}
          >
            <span
              className={`${styles.bLine} ${open ? styles.bLine1Open : ''}`}
            />
            <span
              className={`${styles.bLine} ${open ? styles.bLine2Open : ''}`}
            />
          </button>
        </div>
      </header>

      <div
        className={`${styles.mobile} ${open ? styles.mobileOpen : ''}`}
        aria-hidden={!open}
      >
        <nav className={styles.mobileNav}>
          {navLinks.map((link, i) => (
            <a
              key={link.href}
              href={link.href.startsWith('#') ? `${prefix}${link.href}` : link.href}
              className={styles.mobileLink}
              onClick={() => setOpen(false)}
            >
              <span className={styles.mobileLinkIndex}>
                {String(i + 1).padStart(2, '0')}
              </span>
              <span>{link.name}</span>
            </a>
          ))}
          <a
            href={prefix ? '/#contact' : '#contact'}
            className={styles.mobileLink}
            onClick={() => setOpen(false)}
          >
            <span className={styles.mobileLinkIndex}>→</span>
            <span>Завантажити FuelFlow</span>
          </a>
        </nav>
        <div className={styles.mobileFoot}>
          <a
            href="tel:+380970011771"
            className={styles.mobilePhone}
            onClick={() => setOpen(false)}
          >
            +380 97 001 1771
          </a>
          <span className={styles.mobileMeta}>
            Пн–Пт · 09:00–18:00 · Львів
          </span>
        </div>
      </div>
    </>
  );
}