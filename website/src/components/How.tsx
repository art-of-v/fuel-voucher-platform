import Image from 'next/image';
import styles from './How.module.css';

const steps = [
  {
    title: 'Обрати',
    body: 'Виберіть мережу, пальне й пакет від 2 до 50 літрів. Доступні обсяги залежать від мережі.',
    meta: 'Каталог у застосунку',
  },
  {
    title: 'Оплатити',
    body: 'Ціна фіксується в момент оплати через Monobank і більше не змінюється для цього талона.',
    meta: 'Ціна зафіксована',
  },
  {
    title: 'Отримати',
    body: 'Цифровий талон зʼявляється у вашому гаманці FuelFlow разом із мережею, обсягом і строком дії.',
    meta: 'Талон у телефоні',
  },
  {
    title: 'Показати',
    body: 'Відкрийте талон на станції та покажіть QR-код працівнику або сканеру мережі.',
    meta: 'OKKO · WOG · KLO · UPG',
  },
  {
    title: 'Заправитися',
    body: 'Один пакет — одна заправка. Обирайте пакет під потрібний обсяг пального.',
    meta: 'Одна поїздка',
  },
];

export default function How() {
  return (
    <section className={`lf-section ${styles.section}`} id="how">
      <div className={`lf-container ${styles.container}`}>
        <header className={styles.head}>
          <p className="lf-eyebrow">Для водіїв</p>
          <h2 className={styles.title}>Від каталогу до колонки — пʼять кроків.</h2>
          <p className={styles.intro}>
            Без пластикової картки й паперових талонів. Потрібний пакет зберігається
            у FuelFlow до заправки.
          </p>
        </header>

        <div className={styles.body}>
          <figure className={styles.media}>
            <Image
              src="/driver-interior.jpg"
              alt="Водій у салоні автомобіля"
              width={896}
              height={1200}
              sizes="(max-width: 1000px) 100vw, 42vw"
            />
          </figure>

          <ol className={styles.steps}>
            {steps.map((step, index) => (
              <li key={step.title} className={styles.step}>
                <span className={styles.number}>{String(index + 1).padStart(2, '0')}</span>
                <div>
                  <h3>{step.title}</h3>
                  <p>{step.body}</p>
                </div>
                <small>{step.meta}</small>
              </li>
            ))}
          </ol>
        </div>
      </div>
    </section>
  );
}
