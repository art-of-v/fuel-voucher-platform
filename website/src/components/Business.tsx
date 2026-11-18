import styles from './Business.module.css';

const capabilities = [
  {
    title: 'Видавайте пальне, а не гроші',
    body: 'Водій отримує призначений йому талон у FuelFlow без доступу до бюджету компанії.',
  },
  {
    title: 'Повертайте невикористаний ресурс',
    body: 'Відкличте талон — невикористані літри повернуться у паливний пул компанії.',
  },
  {
    title: 'Зводьте рух без таблиць',
    body: 'Отримано, видано й повернено зводяться в акті звірки автоматично.',
  },
];

const flow = ['Компанія', 'Паливний пул', 'Водій', 'QR', 'Пальне'];

export default function Business() {
  return (
    <section className={`lf-section ${styles.section}`} id="business">
      <div className={`lf-container ${styles.container}`}>
        <div className={styles.copy}>
          <p className="lf-eyebrow">Для компаній і автопарків</p>
          <h2 className={styles.title}>Один пул. Кожен літр на своєму місці.</h2>
          <p className={styles.intro}>
            Купуйте пальне для всієї команди й вирішуйте, хто, коли та скільки
            отримає. FuelFlow лишає контроль у компанії, а водієві — простий талон.
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
