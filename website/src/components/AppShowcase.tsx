'use client';

import Image from 'next/image';
import { useEffect, useRef } from 'react';
import styles from './AppShowcase.module.css';

const screens = [
  { src: '/app-networks.jpg', alt: 'Карта мережі: OKKO, WOG, UPG, KLO з ціною від 55 ₴ за літр' },
  { src: '/app-packages.jpg', alt: 'Пакети пального в OKKO: 10, 20 і 50 літрів із ціною за літр' },
  { src: '/app-basket.jpg', alt: 'Кошик: обраний пакет, промокод і сума до сплати' },
  { src: '/app-codes.jpg', alt: 'Мої талони: активний код на 20 літрів із QR-кодом' },
  { src: '/app-company.jpg', alt: 'Компанія: учасники, видані коди й працівники' },
  { src: '/app-map.jpg', alt: 'Карта АЗК із цінами та кнопкою маршруту' },
];

const copies = [0, 1, 2];

export default function AppShowcase() {
  const railRef = useRef<HTMLDivElement>(null);

  useEffect(() => {
    const rail = railRef.current;
    if (!rail) return;

    const copyWidth = () => rail.scrollWidth / copies.length;
    const anchor = () => {
      if (rail.scrollWidth > rail.clientWidth) rail.scrollLeft = copyWidth();
    };

    anchor();
    const observer = new ResizeObserver(anchor);
    observer.observe(rail);

    const onScroll = () => {
      const width = copyWidth();
      if (rail.scrollLeft < width * 0.5) rail.scrollLeft += width;
      else if (rail.scrollLeft > width * 1.5) rail.scrollLeft -= width;
    };

    rail.addEventListener('scroll', onScroll, { passive: true });
    return () => {
      observer.disconnect();
      rail.removeEventListener('scroll', onScroll);
    };
  }, []);

  useEffect(() => {
    const rail = railRef.current;
    if (!rail) return;
    let down = false;
    let startX = 0;
    let startScroll = 0;
    let moved = false;

    const onDown = (event: PointerEvent) => {
      if (event.pointerType !== 'mouse') return;
      down = true;
      moved = false;
      startX = event.clientX;
      startScroll = rail.scrollLeft;
      rail.classList.add(styles.dragging);
    };
    const onMove = (event: PointerEvent) => {
      if (!down) return;
      const dx = event.clientX - startX;
      if (Math.abs(dx) > 4) moved = true;
      rail.scrollLeft = startScroll - dx;
    };
    const onUp = () => {
      down = false;
      rail.classList.remove(styles.dragging);
    };
    const onClick = (event: MouseEvent) => {
      if (!moved) return;
      event.preventDefault();
      event.stopPropagation();
    };

    rail.addEventListener('pointerdown', onDown);
    window.addEventListener('pointermove', onMove);
    window.addEventListener('pointerup', onUp);
    rail.addEventListener('click', onClick, true);
    return () => {
      rail.removeEventListener('pointerdown', onDown);
      window.removeEventListener('pointermove', onMove);
      window.removeEventListener('pointerup', onUp);
      rail.removeEventListener('click', onClick, true);
    };
  }, []);

  return (
    <section className={`lf-section ${styles.section}`} id="app">
      <div className={`lf-container ${styles.head}`}>
        <p className="lf-eyebrow">FuelFlow у роботі</p>
        <h2 className={styles.title}>Від вибору пакета до заправки</h2>
        <p className={styles.intro}>
          Каталог, замовлення й цифровий талон зібрані в одному зрозумілому потоці.
        </p>
      </div>

      <div className={styles.railWrap}>
        <div
          className={styles.rail}
          ref={railRef}
          role="region"
          aria-label="Екрани застосунку FuelFlow"
          tabIndex={0}
        >
          {copies.map((copy) =>
            screens.map((screen) => (
              <figure key={`${copy}-${screen.src}`} className={styles.device}>
                <Image
                  src={screen.src}
                  alt={copy === 1 ? screen.alt : ''}
                  aria-hidden={copy !== 1}
                  width={588}
                  height={1280}
                  sizes="(max-width: 640px) 72vw, (max-width: 1100px) 220px, 22vw"
                  draggable={false}
                />
              </figure>
            )),
          )}
        </div>
      </div>

      <p className={`lf-container ${styles.hint}`}>Проведіть або перетягніть, щоб гортати</p>
    </section>
  );
}
