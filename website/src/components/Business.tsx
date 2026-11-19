import styles from './Business.module.css';

const capabilities = [
  {
    title: 'Видавайте пальне, а не гроші',
    body: 'Водій отримує призначений йому код у FuelFlow без доступу до бюджету компанії.',
  },
  {
    title: 'Повертайте невикористаний ресурс',
    body: 'Невикористане повертається у пул — і знову стає пальном компанії, а не списаною витратою.',
  },
  {
    title: 'Бачте рух без таблиць',
    body: 'Видано, використано й повернено видно в застосунку в реальному часі — по кожному працівнику.',
  },
];

const flow = ['Компанія', 'Паливний пул', 'Водій', 'QR', 'Пальне'];

export default function Business() {
  return (
    <section className={`lf-section ${styles.section}`} id="business">
      <div className={`lf-container ${styles.container}`}>
        <div className={styles.copy}>
          <p className="lf-eyebrow">Паливний пул</p>
          <h2 className={styles.title}>Один пул. Кожен літр на своєму місці.</h2>
          <p className={styles.intro}>
            Забирайте пально для всієї команди в будь-який момент і вирішуйте,
            хто, коли та скільки отримає. FuelFlow лишає контроль у компанії, а
            водієві — простий код на пально.
          </p>
          <a href="#business-lead" className="lf-btn lf-btn--primary">
            Обговорити автопарк
          </a>
        </div>

        <ol className={styles.flow} aria-label="Шлях пального в компанії">
          {flow.map((step, index) => (
            <li key={step} className={styles.flowStep}>
              <span>{String(index + 1).padStart(2, '0')}</span>
              <strong>{step}</strong>
            </li>
          ))}
        </ol>

        <div className={styles.capabilities}>
          {capabilities.map((item) => (
            <article key={item.title}>
              <h3>{item.title}</h3>
              <p>{item.body}</p>
            </article>
          ))}
        </div>
      </div>
    </section>
  );
}
