'use client';

import { useEffect, useRef, useState, FormEvent } from 'react';
import styles from './Contact.module.css';

type Status = 'idle' | 'sending' | 'sent';

export default function Contact() {
  const [status, setStatus] = useState<Status>('idle');
  const [form, setForm] = useState({ name: '', phone: '', message: '' });
  const ref = useRef<HTMLElement>(null);

  useEffect(() => {
    const root = ref.current;
    if (!root) return;
    const items = root.querySelectorAll<HTMLElement>(`.${styles.reveal}`);
    const observer = new IntersectionObserver(
      (entries) => {
        entries.forEach((entry) => {
          if (entry.isIntersecting) {
            entry.target.classList.add('is-visible');
            observer.unobserve(entry.target);
          }
        });
      },
      { threshold: 0.15, rootMargin: '0px 0px -10% 0px' }
    );
    items.forEach((item) => {
      item.classList.add('lf-reveal');
      observer.observe(item);
    });
    return () => observer.disconnect();
  }, []);

  const onSubmit = (e: FormEvent<HTMLFormElement>) => {
    e.preventDefault();
    if (!form.name.trim() || !form.phone.trim()) return;
    setStatus('sending');

    const subject = `Заявка з сайту — ${form.name}`;
    const body = `Імʼя: ${form.name}\nТелефон: ${form.phone}\n\nПовідомлення:\n${form.message || '—'}`;
    const mailto = `mailto:?subject=${encodeURIComponent(subject)}&body=${encodeURIComponent(body)}`;

    window.location.href = mailto;
    setStatus('sent');
  };

  return (
    <section className={`lf-section ${styles.section}`} id="contact" ref={ref}>
      <div className={`lf-container ${styles.container}`}>
        <header className={styles.head}>
          <span className={`lf-eyebrow ${styles.reveal}`}>Контакти</span>
          <h2 className={`${styles.title} ${styles.reveal}`}>
            Почни з перших літрів.
          </h2>
          <p className={`${styles.lead} ${styles.reveal}`}>
            Залиш контакти — команда FuelFlow передзвонить протягом 15 хвилин
            у робочий час і допоможе завантажити застосунок та зробити першу
            покупку.
          </p>
        </header>

        <div className={styles.grid}>
          <form className={`${styles.form} ${styles.reveal}`} onSubmit={onSubmit}>
            <div className={styles.formRow}>
              <div className={styles.field}>
                <label className={styles.label} htmlFor="lf-name">
                  Імʼя
                </label>
                <input
                  id="lf-name"
                  type="text"
                  className={styles.input}
                  placeholder="Іван Петренко"
                  required
                  value={form.name}
                  onChange={(e) => setForm({ ...form, name: e.target.value })}
                />
              </div>
              <div className={styles.field}>
                <label className={styles.label} htmlFor="lf-phone">
                  Телефон
                </label>
                <input
                  id="lf-phone"
                  type="tel"
                  className={styles.input}
                  placeholder="+380..."
                  required
                  value={form.phone}
                  onChange={(e) => setForm({ ...form, phone: e.target.value })}
                />
              </div>
            </div>

            <div className={styles.field}>
              <label className={styles.label} htmlFor="lf-message">
                Повідомлення
                <span className={styles.optional}>— необовʼязково</span>
              </label>
              <textarea
                id="lf-message"
                className={styles.textarea}
                placeholder="Яке пальне вас цікавить? Який обʼєм?"
                rows={3}
                value={form.message}
                onChange={(e) => setForm({ ...form, message: e.target.value })}
              />
            </div>

            <div className={styles.formFoot}>
              <button
                type="submit"
                className={`lf-btn lf-btn--primary ${styles.submit}`}
                disabled={status === 'sending'}
              >
                {status === 'sent'
                  ? '✓ Заявку прийнято'
                  : 'Надіслати заявку'}
                <svg
                  width="14"
                  height="14"
                  viewBox="0 0 14 14"
                  fill="none"
                  aria-hidden="true"
                >
                  <path
                    d="M2 7H12M12 7L7.5 2.5M12 7L7.5 11.5"
                    stroke="currentColor"
                    strokeWidth="1.5"
                    strokeLinecap="round"
                    strokeLinejoin="round"
                  />
                </svg>
              </button>
              <p className={styles.terms}>
                Натискаючи, ви погоджуєтесь з обробкою персональних даних.
              </p>
            </div>
          </form>

          <aside className={`${styles.aside} ${styles.reveal}`}>
            <a href="tel:+380970011771" className={styles.asideLink}>
              <span className={styles.asideLabel}>Телефон</span>
              <span className={styles.asideValue}>+380 97 001 1771</span>
            </a>
            <div className={styles.asideLink}>
              <span className={styles.asideLabel}>Контактна особа</span>
              <span className={styles.asideValueSm}>
                Колесніков Сергій Юрійович
              </span>
            </div>
            <div className={styles.asideLink}>
              <span className={styles.asideLabel}>Адреса</span>
              <span className={styles.asideValueSm}>
                79023, м. Львів
                <br />
                вул. Городоцька, 128а
              </span>
            </div>
            <div className={styles.asideLink}>
              <span className={styles.asideLabel}>Графік</span>
              <span className={styles.asideValueSm}>Пн–Пт · 09:00–18:00</span>
            </div>
          </aside>
        </div>
      </div>
    </section>
  );
}