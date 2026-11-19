import styles from './Services.module.css';

/**
 * The section that carries the product's actual premise: FuelFlow sells a
 * SERVICE, not fuel (planning `SALES_MODEL.md` — "What the customer buys —
 * the sale is an advertising service"). So the page stops describing a fuel
 * shop and starts describing the operating perimeter a transport company
 * actually runs on: contracts, a pool, distribution, suspension, and the
 * paper that goes to the accountant.
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
    title: 'Договори з паливними провайдерами',
    body: 'Юридичні особи, версії контрактів і підписання — у застосунку, поруч із закупленнями й обліком.',
    status: 'live',
    statusLabel: 'працює',
  },
  {
    number: '02',
    title: 'Пул і закуплення',
    body: 'Пально потрібне в будь-який момент: ціна фіксується в момент оплати, а пул лишається на балансі компанії.',
    status: 'live',
    statusLabel: 'працює',
  },
  {
    number: '03',
    title: 'Видача, повернення, розподіл',
    body: 'Видавайте коди працівникам, повертайте невикористане в пул — і рух пального видно без таблиць.',
    status: 'live',
    statusLabel: 'працює',
  },
  {
    number: '04',
    title: 'Призупинення без звільнення',
    body: 'Заморозте використання на час відпустки чи відрядження: людина лишається в компанії, літра — під контролем.',
    status: 'live',
    statusLabel: 'працює',
  },
];

const documents: Item[] = [
  {
    number: '01',
    title: 'Акт звірки',
    body: 'Отримано, видано, використано й повернено — у формі документа, а не у звірці в Excel.',
    status: 'planned',
    statusLabel: 'у планах',
  },
  {
    number: '02',
    title: 'Авансовий рахунок',
    body: 'Рахунок на пально для юридичної особи — з обсягами, сумами й реквізитами.',
    status: 'planned',
    statusLabel: 'у планах',
  },
  {
    number: '03',
    title: 'Податкова накладна',
    body: 'Фіскальний документ замість паперової накладної на пально.',
    status: 'planned',
    statusLabel: 'у планах',
  },
  {
    number: '04',
    title: 'Закриття ПДВ',
    body: 'Період, підсумки й декларація — з даних, які сервіс збирає вже сьогодні.',
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
          <p className="lf-eyebrow">Сервіс, а не крамниця пального</p>
          <h2 className={styles.title}>
            Ми не продаємо пальне.
            <br />
            Ми тримаємо контур, у якому ваша компанія купує, розподіляє і
            звіряє пально.
          </h2>
          <p className={styles.intro}>
            FuelFlow — це інструмент для транспортного бізнесу. Він знає біль
            вашого контуру: хто, скільки, за якою ціною і з яким документом.
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
            <p className="lf-eyebrow">Документація бізнесу</p>
            <h3>Папір, який тримає бухгалтерію</h3>
            <p>
              Документи — окремий напрямок продукту: кожен із них відповідає на
              запитання бухгалтера, а не повторює дані з екрана.
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
