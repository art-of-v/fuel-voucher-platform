'use client';

import { useEffect, useRef } from 'react';

interface Props {
  to: number;
  decimals?: number;
  prefix?: string;
  suffix?: string;
  duration?: number;
  className?: string;
}

function fmt(value: number, decimals: number) {
  const fixed = value.toFixed(decimals);
  const [int, frac] = fixed.split('.');
  const grouped = int.replace(/\B(?=(\d{3})+(?!\d))/g, '\u2009');
  return frac ? `${grouped},${frac}` : grouped;
}

export default function Counter({
  to,
  decimals = 0,
  prefix = '',
  suffix = '',
  duration = 1600,
  className,
}: Props) {
  const ref = useRef<HTMLSpanElement>(null);

  useEffect(() => {
    const el = ref.current;
    if (!el) return;
    el.textContent = `${prefix}${fmt(to, decimals)}${suffix}`;
    if (window.matchMedia('(prefers-reduced-motion: reduce)').matches) return;

    let raf = 0;
    let started = false;
    const io = new IntersectionObserver(
      ([entry]) => {
        if (!entry.isIntersecting || started) return;
        started = true;
        const t0 = performance.now();
        const tick = (t: number) => {
          const p = Math.min(1, (t - t0) / duration);
          const eased = 1 - Math.pow(1 - p, 3);
          el.textContent = `${prefix}${fmt(to * eased, decimals)}${suffix}`;
          if (p < 1) raf = requestAnimationFrame(tick);
        };
        raf = requestAnimationFrame(tick);
      },
      { threshold: 0.4 }
    );
    io.observe(el);
    return () => {
      io.disconnect();
      cancelAnimationFrame(raf);
    };
  }, [to, decimals, prefix, suffix, duration]);

  return (
    <span ref={ref} className={className}>
      {prefix}
      {fmt(to, decimals)}
      {suffix}
    </span>
  );
}