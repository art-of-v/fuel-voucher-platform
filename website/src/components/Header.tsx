'use client';

import { useEffect, useRef, useState } from 'react';
import { usePathname } from 'next/navigation';
import { APP_STORE_URL } from '@/config/store';
import { homeAnchor } from '@/lib/anchors';
import BrandLockup from './BrandLockup';
import styles from './Header.module.css';

const navLinks = [
  { name: 'Сервіси', href: '#services' },
  { name: 'Застосунок', href: '#app' },
  { name: 'Ціни', href: '#prices' },
  { name: 'Процес', href: '#how' },
  { name: 'FAQ', href: '#faq' },
  { name: 'Підтримка', href: '/support/' },
];

export default function Header() {
  const [scrolled, setScrolled] = useState(false);
  const [open, setOpen] = useState(false);
  const pathname = usePathname();
  const menuRef = useRef<HTMLDivElement>(null);
  const burgerRef = useRef<HTMLButtonElement>(null);

  useEffect(() => {
    const onScroll = () => setScrolled(window.scrollY > 24);
    onScroll();
    window.addEventListener('scroll', onScroll, { passive: true });
    return () => window.removeEventListener('scroll', onScroll);
  }, []);

  useEffect(() => {
    if (!open) return;

    document.body.style.overflow = 'hidden';
    const menu = menuRef.current;
    const burger = burgerRef.current;
    const focusable = menu?.querySelectorAll<HTMLElement>('a, button:not([disabled])') ?? [];
    const focusTimer = window.setTimeout(() => focusable[0]?.focus({ preventScroll: true }), 0);

    const onKeyDown = (event: KeyboardEvent) => {
      if (event.key === 'Escape') {
        setOpen(false);
        return;
      }
      if (event.key !== 'Tab' || focusable.length === 0) return;
      const first = focusable[0];
      const last = focusable[focusable.length - 1];
      if (event.shiftKey && document.activeElement === first) {
        event.preventDefault();
        last.focus();
      } else if (!event.shiftKey && document.activeElement === last) {
        event.preventDefault();
        first.focus();
      }
    };

    document.addEventListener('keydown', onKeyDown);
    return () => {
      window.clearTimeout(focusTimer);
      document.body.style.overflow = '';
      document.removeEventListener('keydown', onKeyDown);
      burger?.focus();
    };
  }, [open]);

  const close = () => setOpen(false);
  const hrefFor = (href: string) =>
    href.startsWith('#') ? homeAnchor(pathname, href) : href;

  return (
    <>
      <header className={`${styles.header} ${scrolled ? styles.scrolled : ''}`}>
        <div className={`${styles.container} lf-container`}>
          <a
            href={homeAnchor(pathname, '#top')}
            className={styles.logo}
            aria-label="Fuel Flow — на головну"
          >
            <BrandLockup />
          </a>

          <nav
            className={`${styles.nav} ${scrolled ? styles.navVisible : ''}`}
            aria-label="Основна навігація"
          >
            {navLinks.map((link) => (
              <a key={link.href} href={hrefFor(link.href)} className={styles.navLink}>
                {link.name}
              </a>
            ))}
          </nav>

          <div className={styles.right}>
            <a href="tel:+380970011771" className={styles.phone}>
              +380 97 001 1771
            </a>
            <a
              href={homeAnchor(pathname, '#business-lead')}
              className={`lf-btn lf-btn--primary ${styles.cta}`}
            >
              <span className={styles.ctaFull}>Підключити автопарк</span>
              <span className={styles.ctaShort}>Автопарк</span>
            </a>
          </div>

          <button
            ref={burgerRef}
            type="button"
            className={styles.burger}
            aria-label={open ? 'Закрити меню' : 'Відкрити меню'}
            aria-expanded={open}
            aria-controls="site-menu"
            onClick={() => setOpen((value) => !value)}
          >
            <span className={`${styles.bLine} ${open ? styles.bLine1Open : ''}`} />
            <span className={`${styles.bLine} ${open ? styles.bLine2Open : ''}`} />
          </button>
        </div>
      </header>

      <div
        id="site-menu"
        ref={menuRef}
        className={`${styles.mobile} ${open ? styles.mobileOpen : ''}`}
        aria-hidden={!open}
        inert={!open}
      >
        <nav className={styles.mobileNav} aria-label="Мобільна навігація">
          {navLinks.map((link) => (
            <a
              key={link.href}
              href={hrefFor(link.href)}
              className={styles.mobileLink}
              onClick={close}
            >
              {link.name}
            </a>
          ))}
          <a
            href={APP_STORE_URL}
            target="_blank"
            rel="noopener noreferrer"
            className={styles.mobileLinkSecondary}
            onClick={close}
          >
            Завантажити застосунок
          </a>
        </nav>
        <div className={styles.mobileFoot}>
          <a href="tel:+380970011771" className={styles.mobilePhone} onClick={close}>
            +380 97 001 1771
          </a>
          <span className={styles.mobileMeta}>Пн–Пт · 09:00–18:00 · Львів</span>
        </div>
      </div>
    </>
  );
}
