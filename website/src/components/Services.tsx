import styles from './Services.module.css';

/**
 * The section that carries the product's actual premise: FuelFlow sells a
 * SERVICE, not fuel (planning `SALES_MODEL.md` — "What the customer buys —
 * the sale is an advertising service"). So the page stops describing a fuel
 * shop and starts describing the job a transport company actually has to do:
 * contracts, a pool, codes for the crew, and the paper that goes to the
 * accountant.
 *
 * Every status chip here is a claim about the app, not about the marketing:
 * `live` is something the shipped code does today, `planned` is something the
 * roadmap says it will do. The two sets are separate arrays on purpose —
 * flipping a chip is a one-word change, and a chip that lies is worse than a
 * chip that is quiet.
 */
type Status = 'live' | 'planned';

type Item = {
  number: string;
  title: string;
  body: string;
  status: Status;
  statusLabel: string;
};

const capabilities: Item[] = [
  {
    number: '01',
    title: 'Договори з постачальниками',
    body: 'Юридичні особи, версії контрактів і підписання в застосунку.',
    status: 'live',
    statusLabel: 'працює',
  },
  {
    number: '02',
    title: 'Закуплення',
    body: 'Пальне в будь-який момент. Ціна фіксується в момент оплати.',
    status: 'live',
    statusLabel: 'працює',
  },
  {
    number: '03',
    title: 'Видача кодів',
    body: 'Код у гаманці конкретної людини.',
    status: 'live',
    statusLabel: 'працює',
  },
  {
    number: '04',
    title: 'Призупинення',
    body: 'Код на паузі, літри лишаються в компанії.',
    status: 'live',
    statusLabel: 'працює',
  },
];

const documents: Item[] = [
  {
    number: '01',
    title: 'Акт звірки',
    body: 'Отримано, видано, використано, повернено.',
    status: 'planned',
    statusLabel: 'у планах',
  },
  {
    number: '02',
    title: 'Авансовий рахунок',
    body: 'Обсяги, суми, реквізити.',
    status: 'planned',
    statusLabel: 'у планах',
  },
  {
    number: '03',
    title: 'Податкова накладна',
    body: 'Фіскальний документ замість паперової.',
    status: 'planned',
    statusLabel: 'у планах',
  },
  {
    number: '04',
    title: 'Закриття ПДВ',
    body: 'Період, підсумки, декларація.',
    status: 'planned',
    statusLabel: 'у планах',
  },
];

function StatusChip({ status, label }: { status: Status; label: string }) {
  return (
    <span
      className={`${styles.chip} ${status === 'live' ? styles.chipLive : styles.chipPlanned}`}
    >
      <span className={styles.chipDot} aria-hidden="true" />
      {label}
    </span>
  );
}

export default function Services() {
  return (
    <section className={`lf-section ${styles.section}`} id="services">
      <div className={`lf-container ${styles.container}`}>
        <div className={styles.manifest}>
          <p className="lf-eyebrow">Що входить у сервіс</p>
          <h2 className={styles.title}>
            Від договору до{' '}
            <span className={styles.accent}>акта звірки</span>
          </h2>
          <p className={styles.intro}>
            Керуйте пальним так, як керуєте автопарком
          </p>
        </div>

        <ol className={styles.grid} aria-label="Сервіси FuelFlow для бізнесу">
          {capabilities.map((item) => (
            <li key={item.number} className={styles.item}>
              <div className={styles.itemHead}>
                <span className={styles.number}>{item.number}</span>
                <StatusChip status={item.status} label={item.statusLabel} />
              </div>
              <h3>{item.title}</h3>
              <p>{item.body}</p>
            </li>
          ))}
        </ol>

        <div className={styles.docs} id="documents">
          <div className={styles.docsHead}>
            <p className="lf-eyebrow">Документація</p>
            <h3>Документи, які закривають період</h3>
            <p>
              Кожен документ звірений із даними застосунку й готовий до
              бухгалтерії
            </p>
          </div>

          <ul className={styles.docsList}>
            {documents.map((doc) => (
              <li key={doc.number} className={styles.doc}>
                <div className={styles.docHead}>
                  <h4>{doc.title}</h4>
                  <StatusChip status={doc.status} label={doc.statusLabel} />
                </div>
                <p>{doc.body}</p>
              </li>
            ))}
          </ul>
        </div>
      </div>
    </section>
  );
}
