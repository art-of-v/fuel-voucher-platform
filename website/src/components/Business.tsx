import Image from 'next/image';
import styles from './Business.module.css';

const flow = ['Компанія', 'Закуплення', 'Водій', 'QR', 'Пальне'];

export default function Business() {
  return (
    <section className={`lf-section ${styles.section}`} id="business">
      <div className={`lf-container ${styles.container}`}>
        <div className={styles.copy}>
          <p className="lf-eyebrow">Для автопарку</p>
          <h2 className={styles.title}>Пальне для всієї команди</h2>
          <p className={styles.intro}>
            Беріть пальне для всієї команди в будь-який момент і вирішуйте, хто,
            коли та скільки отримає. Контроль лишається у компанії, а водій
            отримує код у своєму гаманці
          </p>
          <a href="#business-lead" className="lf-btn lf-btn--primary">
            Обговорити автопарк
          </a>
        </div>

        <figure className={styles.media}>
          <Image
            src="/driver-phone.jpg"
            alt="Водій оплачує пальне кодом у застосунку біля колонки"
            width={896}
            height={1200}
            sizes="(max-width: 900px) 100vw, 38vw"
          />
        </figure>

        <ol className={styles.flow} aria-label="Шлях пального в компанії">
          {flow.map((step, index) => (
            <li key={step} className={styles.flowStep}>
              <span>{String(index + 1).padStart(2, '0')}</span>
              <strong>{step}</strong>
            </li>
          ))}
        </ol>
      </div>
    </section>
  );
}
