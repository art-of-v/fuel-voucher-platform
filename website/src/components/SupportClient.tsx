'use client';

import { useState, FormEvent } from 'react';
import Header from './Header';
import Footer from './Footer';
import styles from './Support.module.css';

type Status = 'idle' | 'sending' | 'sent' | 'error';

const API_BASE = process.env.NEXT_PUBLIC_API_URL ?? 'https://api.palne.shop';

export default function SupportClient() {
  const [status, setStatus] = useState<Status>('idle');
  const [errorMsg, setErrorMsg] = useState('');
  const [form, setForm] = useState({ email: '', message: '', website: '' });

  const onSubmit = async (e: FormEvent<HTMLFormElement>) => {
    e.preventDefault();
    if (status === 'sending') return;
    if (!form.email.trim() || !form.message.trim()) return;

    setStatus('sending');
    setErrorMsg('');

    try {
      const res = await fetch(`${API_BASE}/api/support/messages`, {
        method: 'POST',
        headers: { 'Content-Type': 'application/json' },
        body: JSON.stringify({
          email: form.email.trim(),
          message: form.message.trim(),
          // Honeypot: invisible to humans, autofill bait for bots.
          website: form.website,
        }),
      });

      if (res.status === 202 || res.status === 204) {
        setStatus('sent');
        setForm({ email: '', message: '', website: '' });
        return;
      }

      if (res.status === 429) {
        setErrorMsg('Забагато повідомлень поспіль. Спробуйте через годину або зателефонуйте нам.');
        setStatus('error');
        return;
      }

      if (res.status === 400) {
        const data = await res.json().catch(() => null);
        const first = data?.errors
          ? (Object.values(data.errors)[0] as string[] | undefined)?.[0]
          : null;
        setErrorMsg(first ?? 'Перевірте заповнені поля.');
        setStatus('error');
        return;
      }

      setErrorMsg('Щось пішло не так. Спробуйте ще раз або зателефонуйте: +380 97 001 1771.');
      setStatus('error');
    } catch {
      setErrorMsg('Немає зʼєднання. Перевірте інтернет і спробуйте ще раз.');
      setStatus('error');
    }
  };

  return (
    <>
      <Header />
      <main id="top">
        <section className={`lf-section lf-section--deep ${styles.section}`}>
          <div className={`lf-container ${styles.container}`}>
            <header className={styles.head}>
              <span className="lf-eyebrow">Підтримка</span>
              <h1 className={styles.title}>
                Напишіть нам.
              </h1>
              <p className={styles.lead}>
                Питання щодо оплати, талонів або застосунку? Опишіть ситуацію —
                відповімо на вказаний email протягом робочого дня.
              </p>
            </header>

            <div className={styles.grid}>
              {status === 'sent' ? (
                <div className={styles.done} role="status">
                  <span className={styles.doneMark}>✓</span>
                  <h2 className={styles.doneTitle}>Повідомлення надіслано</h2>
                  <p className={styles.doneText}>
                    Дякуємо! Відповімо на ваш email протягом робочого дня
                    (Пн–Пт, 09:00–18:00). Якщо питання термінове — зателефонуйте:
                    <a href="tel:+380970011771" className={styles.donePhone}>
                      +380 97 001 1771
                    </a>
                  </p>
                  <button
                    type="button"
                    className={`lf-btn lf-btn--ghost ${styles.again}`}
                    onClick={() => setStatus('idle')}
                  >
                    Написати ще раз
                  </button>
                </div>
              ) : (
                <form className={styles.form} onSubmit={onSubmit} noValidate={false}>
                  <div className={styles.field}>
                    <label className={styles.label} htmlFor="sp-email">
                      Email
                    </label>
                    <input
                      id="sp-email"
                      type="email"
                      className={styles.input}
                      placeholder="you@example.com"
                      autoComplete="email"
                      required
                      maxLength={254}
                      value={form.email}
                      onChange={(e) => setForm({ ...form, email: e.target.value })}
                    />
                  </div>

                  <div className={styles.field}>
                    <label className={styles.label} htmlFor="sp-message">
                      Повідомлення
                    </label>
                    <textarea
                      id="sp-message"
                      className={styles.textarea}
                      placeholder="Опишіть питання: номер замовлення, спосіб оплати, що саме сталося…"
                      rows={6}
                      required
                      maxLength={4000}
                      value={form.message}
                      onChange={(e) => setForm({ ...form, message: e.target.value })}
                    />
                  </div>

                  {/* Honeypot — hidden from humans via CSS, visible to dumb bots */}
                  <div className={styles.hp} aria-hidden="true">
                    <label htmlFor="sp-website">Website</label>
                    <input
                      id="sp-website"
                      type="text"
                      tabIndex={-1}
                      autoComplete="off"
                      value={form.website}
                      onChange={(e) => setForm({ ...form, website: e.target.value })}
                    />
                  </div>

                  {status === 'error' && (
                    <p className={styles.error} role="alert">
                      {errorMsg}
                    </p>
                  )}

                  <div className={styles.formFoot}>
                    <button
                      type="submit"
                      className={`lf-btn lf-btn--primary ${styles.submit}`}
                      disabled={status === 'sending'}
                    >
                      {status === 'sending' ? 'Надсилаємо…' : 'Надіслати'}
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
              )}

              <aside className={styles.aside}>
                <a href="mailto:palne.shopua@gmail.com" className={styles.asideLink}>
                  <span className={styles.asideLabel}>Email</span>
                  <span className={styles.asideValueSm}>palne.shopua@gmail.com</span>
                </a>
                <a href="tel:+380970011771" className={styles.asideLink}>
                  <span className={styles.asideLabel}>Телефон</span>
                  <span className={styles.asideValue}>+380 97 001 1771</span>
                </a>
                <div className={styles.asideLink}>
                  <span className={styles.asideLabel}>Графік</span>
                  <span className={styles.asideValueSm}>Пн–Пт · 09:00–18:00</span>
                </div>
              </aside>
            </div>
          </div>
        </section>
      </main>
      <Footer />
    </>
  );
}
