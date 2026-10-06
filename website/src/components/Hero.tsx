import type { CSSProperties } from 'react';
import AppStoreBadge from './AppStoreBadge';
import styles from './Hero.module.css';

/** Stagger step between reveal steps, in ms. */
const STEP = 90;

/**
 * Per-element stagger, inlined so it is part of the server-rendered HTML.
 *
 * The reveal used to be a mount effect that toggled `lf-reveal` and the literal
 * `is-visible`. That made the hero - the largest contentful paint, the first
 * thing anyone sees - depend on three separate things all going right: hydration
 * completing, requestAnimationFrame firing, and a class name surviving CSS
 * Modules hashing. Every one of those has failed here at least once, and none of
 * them is visible in review: the DOM looks correct, the copy is simply not
 * painted. A CSS animation starts on first paint, so the hidden start state
 * lives in the keyframe's `from` rather than in the rule, and the copy is
 * visible whenever the animation does not run at all.
 */
const delay = (step: number) => ({ '--reveal-delay': `${step * STEP}ms` }) as CSSProperties;

export default function Hero() {
  return (
    <section className={styles.hero} id="top">
      <div className={styles.bg} aria-hidden="true">
        <div className={styles.bgImage} data-parallax="0.08" />
        <div className={styles.bgScrim} />
      </div>
      <div className="lf-grid-bg" aria-hidden="true" />

      <div className={`${styles.container} lf-container`}>
        <div className={styles.left}>
          <div className={`${styles.meta} ${styles.reveal}`} style={delay(0)}>
            <span className={styles.metaDot} />
            <span className={styles.metaLabel}>FuelFlow · цифровий паливний гаманець</span>
          </div>

          <h1 className={styles.title}>
            <span className={`${styles.titleMain} ${styles.reveal}`} style={delay(1)}>
              Купуй паливо
            </span>
            <span className={`${styles.titleSub} ${styles.reveal}`} style={delay(2)}>
              сьогодні<span className={styles.titleAccent}>.</span>
            </span>
            <span className={`${styles.titleLine2} ${styles.reveal}`} style={delay(3)}>
              Зафіксуй ціну.
            </span>
          </h1>

          <p className={`${styles.lead} ${styles.reveal}`} style={delay(4)}>
            Купуй літри наперед, зберігай паливо у застосунку та
            заправляйся за QR-кодом. Гроші → літри → телефон → QR → пальне.
          </p>

          <div className={`${styles.actions} ${styles.reveal}`} style={delay(5)}>
            <AppStoreBadge />
            <a href="#business" className={`lf-btn lf-btn--ghost ${styles.cta}`}>
              Для бізнесу →
            </a>
          </div>

          <div className={`${styles.spec} ${styles.reveal}`} style={delay(6)}>
            <div className={styles.specRow}>
              <span className={styles.specLabel}>Мереж</span>
              <span className={styles.specValue}>04</span>
            </div>
            <div className={styles.specRow}>
              <span className={styles.specLabel}>Пакет</span>
              <span className={styles.specValue}>50 л</span>
            </div>
            <div className={styles.specRow}>
              <span className={styles.specLabel}>Оплата</span>
              <span className={styles.specValue}>Monobank</span>
            </div>
            <div className={styles.specRow}>
              <span className={styles.specLabel}>Ціна</span>
              <span className={styles.specValue}>Зафіксована</span>
            </div>
          </div>
        </div>
      </div>

      <a
        href="#numbers"
        className={`${styles.scroll} ${styles.reveal}`}
        style={delay(7)}
        aria-label="Прокрутити далі"
      >
        <span className={styles.scrollLabel}>SCROLL</span>
        <span className={styles.scrollLine} />
      </a>
    </section>
  );
}
