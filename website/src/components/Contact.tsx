'use client';

import { FormEvent, useState } from 'react';
import styles from './Contact.module.css';

type Status = 'idle' | 'sending' | 'sent' | 'error';
type FormState = {
  companyName: string;
  contactName: string;
  phone: string;
  email: string;
  fleetSize: string;
  message: string;
  website: string;
};
type FieldName = keyof Pick<
  FormState,
  'companyName' | 'contactName' | 'phone' | 'email' | 'fleetSize' | 'message'
>;
type FieldErrors = Partial<Record<FieldName, string>>;

const API_BASE = process.env.NEXT_PUBLIC_API_URL ?? 'https://api.fuelflowmarketplace.com';
const EMPTY_FORM: FormState = {
  companyName: '',
  contactName: '',
  phone: '',
  email: '',
  fleetSize: '',
  message: '',
  website: '',
};

function validate(form: FormState): FieldErrors {
  const errors: FieldErrors = {};
  if (!form.companyName.trim()) errors.companyName = 'Вкажіть назву компанії.';
  if (!form.contactName.trim()) errors.contactName = 'Вкажіть контактну особу.';
  if (!/^\+[1-9]\d{7,14}$/.test(form.phone.replace(/[\s()-]/g, ''))) {
    errors.phone = 'Вкажіть номер у міжнародному форматі, наприклад +380…';
  }
  if (form.email.trim() && !/^[^\s@]+@[^\s@]+\.[^\s@]+$/.test(form.email.trim())) {
    errors.email = 'Перевірте адресу email.';
  }
  if (form.fleetSize && (!Number.isInteger(Number(form.fleetSize)) || Number(form.fleetSize) <= 0)) {
    errors.fleetSize = 'Вкажіть додатну кількість автомобілів.';
  }
  return errors;
}

export default function Contact() {
  const [status, setStatus] = useState<Status>('idle');
  const [form, setForm] = useState<FormState>(EMPTY_FORM);
  const [fieldErrors, setFieldErrors] = useState<FieldErrors>({});
  const [errorMessage, setErrorMessage] = useState('');

  const update = (field: keyof FormState, value: string) => {
    setForm((current) => ({ ...current, [field]: value }));
    if (field in fieldErrors) {
      setFieldErrors((current) => ({ ...current, [field]: undefined }));
    }
  };

  const onSubmit = async (event: FormEvent<HTMLFormElement>) => {
    event.preventDefault();
    if (status === 'sending') return;

    const errors = validate(form);
    if (Object.keys(errors).length) {
      setFieldErrors(errors);
      setStatus('error');
      setErrorMessage('Перевірте позначені поля.');
      return;
    }

    setStatus('sending');
    setFieldErrors({});
    setErrorMessage('');

    try {
      const response = await fetch(`${API_BASE}/api/business-leads`, {
        method: 'POST',
        headers: { 'Content-Type': 'application/json' },
        body: JSON.stringify({
          companyName: form.companyName.trim(),
          contactName: form.contactName.trim(),
          phone: form.phone.replace(/[\s()-]/g, ''),
          email: form.email.trim() || null,
          fleetSize: form.fleetSize ? Number(form.fleetSize) : null,
          message: form.message.trim() || null,
          website: form.website,
        }),
      });

      if (response.status === 202 || response.status === 204) {
        setStatus('sent');
        setForm(EMPTY_FORM);
        return;
      }

      if (response.status === 400) {
        const data = await response.json().catch(() => null);
        const apiErrors = data?.errors as Record<string, string[]> | undefined;
        const mapped: FieldErrors = {};
        if (apiErrors) {
          for (const [key, messages] of Object.entries(apiErrors)) {
            const field = key.charAt(0).toLowerCase() + key.slice(1);
            if (field in EMPTY_FORM && messages?.[0]) {
              mapped[field as FieldName] = messages[0];
            }
          }
        }
        setFieldErrors(mapped);
        setErrorMessage('Перевірте заповнені поля.');
        setStatus('error');
        return;
      }

      if (response.status === 429) {
        setErrorMessage('Забагато заявок поспіль. Спробуйте через годину або зателефонуйте нам.');
        setStatus('error');
        return;
      }

      setErrorMessage('Не вдалося надіслати заявку. Спробуйте ще раз або зателефонуйте нам.');
      setStatus('error');
    } catch {
      setErrorMessage('Немає зʼєднання. Перевірте інтернет і спробуйте ще раз.');
      setStatus('error');
    }
  };

  const errorFor = (field: FieldName) => fieldErrors[field];

  return (
    <section className={`lf-section ${styles.section}`} id="business-lead">
      <span id="contact" className={styles.anchor} aria-hidden="true" />
      <div className={`lf-container ${styles.container}`}>
        <header className={styles.head}>
          <p className="lf-eyebrow">Підключення бізнесу</p>
          <h2 className={styles.title}>Розкажіть про свій автопарк.</h2>
          <p className={styles.lead}>
            Залиште робочий контакт. Команда FuelFlow уточнить потребу в пальному й
            покаже, як організувати пул, розподіл і документи для ваших водіїв.
          </p>
        </header>

        <div className={styles.layout}>
          <aside className={styles.aside}>
            <p>Що обговоримо</p>
            <ul>
              <li>обсяг і мережі заправок</li>
              <li>договори з паливними провайдерами</li>
              <li>видачу, розподіл і призупинення</li>
              <li>контроль залишків і документи</li>
            </ul>
            <a href="tel:+380970011771">+380 97 001 1771</a>
            <span>Пн–Пт · 09:00–18:00</span>
          </aside>

          {status === 'sent' ? (
            <div className={styles.success} role="status" aria-live="polite">
              <span aria-hidden="true">✓</span>
              <h3>Заявку прийнято.</h3>
              <p>Ми звʼяжемося з вами протягом робочого дня.</p>
              <button type="button" className="lf-btn lf-btn--ghost" onClick={() => setStatus('idle')}>
                Надіслати ще одну
              </button>
            </div>
          ) : (
            <form className={styles.form} onSubmit={onSubmit} noValidate>
              <div className={styles.formGrid}>
                <Field
                  id="lead-company"
                  label="Компанія"
                  value={form.companyName}
                  onChange={(value) => update('companyName', value)}
                  placeholder="ТОВ Приклад"
                  autoComplete="organization"
                  maxLength={200}
                  error={errorFor('companyName')}
                  required
                />
                <Field
                  id="lead-contact"
                  label="Контактна особа"
                  value={form.contactName}
                  onChange={(value) => update('contactName', value)}
                  placeholder="Іван Петренко"
                  autoComplete="name"
                  maxLength={200}
                  error={errorFor('contactName')}
                  required
                />
                <Field
                  id="lead-phone"
                  label="Телефон"
                  value={form.phone}
                  onChange={(value) => update('phone', value)}
                  placeholder="+380…"
                  type="tel"
                  inputMode="tel"
                  autoComplete="tel"
                  maxLength={20}
                  error={errorFor('phone')}
                  required
                />
                <Field
                  id="lead-email"
                  label="Email (необовʼязково)"
                  value={form.email}
                  onChange={(value) => update('email', value)}
                  placeholder="name@company.ua"
                  type="email"
                  inputMode="email"
                  autoComplete="email"
                  maxLength={254}
                  error={errorFor('email')}
                />
                <Field
                  id="lead-fleet"
                  label="Автомобілів (необовʼязково)"
                  value={form.fleetSize}
                  onChange={(value) => update('fleetSize', value)}
                  placeholder="25"
                  type="number"
                  inputMode="numeric"
                  min="1"
                  error={errorFor('fleetSize')}
                />
                <div className={`${styles.field} ${styles.messageField}`}>
                  <label htmlFor="lead-message">Потреба (необовʼязково)</label>
                  <textarea
                    id="lead-message"
                    rows={4}
                    maxLength={2000}
                    value={form.message}
                    placeholder="Мережі, орієнтовний обсяг або питання"
                    aria-invalid={Boolean(errorFor('message'))}
                    aria-describedby={errorFor('message') ? 'lead-message-error' : undefined}
                    onChange={(event) => update('message', event.target.value)}
                  />
                  {errorFor('message') ? (
                    <span id="lead-message-error" className={styles.fieldError}>
                      {errorFor('message')}
                    </span>
                  ) : null}
                </div>
              </div>

              <div className={styles.honeypot} aria-hidden="true">
                <label htmlFor="lead-website">Website</label>
                <input
                  id="lead-website"
                  type="text"
                  tabIndex={-1}
                  autoComplete="off"
                  value={form.website}
                  onChange={(event) => update('website', event.target.value)}
                />
              </div>

              <div className={styles.formFoot}>
                <button type="submit" className="lf-btn lf-btn--primary" disabled={status === 'sending'}>
                  {status === 'sending' ? 'Надсилаємо…' : 'Надіслати заявку'}
                </button>
                <p>
                  Надсилаючи форму, ви погоджуєтеся з{' '}
                  <a href="/privacy/">політикою конфіденційності</a>.
                </p>
              </div>

              <div className={styles.status} aria-live="polite">
                {status === 'error' ? <p role="alert">{errorMessage}</p> : null}
              </div>
            </form>
          )}
        </div>
      </div>
    </section>
  );
}

type FieldProps = {
  id: string;
  label: string;
  value: string;
  onChange: (value: string) => void;
  error?: string;
  type?: string;
  inputMode?: 'text' | 'tel' | 'email' | 'numeric';
  placeholder?: string;
  autoComplete?: string;
  maxLength?: number;
  min?: string;
  required?: boolean;
};

function Field({ id, label, value, onChange, error, ...inputProps }: FieldProps) {
  const errorId = `${id}-error`;
  return (
    <div className={styles.field}>
      <label htmlFor={id}>{label}</label>
      <input
        id={id}
        value={value}
        aria-invalid={Boolean(error)}
        aria-describedby={error ? errorId : undefined}
        onChange={(event) => onChange(event.target.value)}
        {...inputProps}
      />
      {error ? (
        <span id={errorId} className={styles.fieldError}>
          {error}
        </span>
      ) : null}
    </div>
  );
}
