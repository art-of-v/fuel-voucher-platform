'use client';

import { useEffect } from 'react';

/**
 * Global motion layer. Wires declarative data-attributes to effects:
 *   data-parallax="0.12" — rAF parallax (speed relative to viewport center)
 *   data-magnetic        — button leans toward the cursor
 *   data-glow            — cursor-following radial highlight (uses --mx/--my)
 *   data-tilt            — subtle 3D tilt following the cursor
 * All effects are desktop-pointer only and disabled for reduced motion.
 */
export default function Effects() {
  useEffect(() => {
    if (window.matchMedia('(prefers-reduced-motion: reduce)').matches) return;
    if (!window.matchMedia('(pointer: fine)').matches) return;

    const fine = { passive: true };
    let raf = 0;

    /* ── Parallax ── */
    const parallaxEls = Array.from(
      document.querySelectorAll<HTMLElement>('[data-parallax]')
    );
    const onScroll = () => {
      cancelAnimationFrame(raf);
      raf = requestAnimationFrame(() => {
        const vh = window.innerHeight;
        for (const el of parallaxEls) {
          const speed = parseFloat(el.dataset.parallax || '0.1');
          const rect = el.getBoundingClientRect();
          const centerDelta = rect.top + rect.height / 2 - vh / 2;
          el.style.transform = `translate3d(0, ${(-centerDelta * speed).toFixed(1)}px, 0)`;
        }
      });
    };
    if (parallaxEls.length) {
      window.addEventListener('scroll', onScroll, fine);
      onScroll();
    }

    /* ── Magnetic buttons ── */
    const magnetics = document.querySelectorAll<HTMLElement>('[data-magnetic]');
    const magneticMove = (e: MouseEvent) => {
      const el = e.currentTarget as HTMLElement;
      const rect = el.getBoundingClientRect();
      const dx = e.clientX - (rect.left + rect.width / 2);
      const dy = e.clientY - (rect.top + rect.height / 2);
      el.style.transform = `translate(${dx * 0.18}px, ${dy * 0.28}px)`;
    };
    const magneticLeave = (e: MouseEvent) => {
      (e.currentTarget as HTMLElement).style.transform = '';
    };
    magnetics.forEach((el) => {
      el.addEventListener('mousemove', magneticMove, fine);
      el.addEventListener('mouseleave', magneticLeave);
    });

    /* ── Cursor glow ── */
    const glows = document.querySelectorAll<HTMLElement>('[data-glow]');
    const glowMove = (e: MouseEvent) => {
      const el = e.currentTarget as HTMLElement;
      const rect = el.getBoundingClientRect();
      el.style.setProperty('--mx', `${e.clientX - rect.left}px`);
      el.style.setProperty('--my', `${e.clientY - rect.top}px`);
    };
    glows.forEach((el) => el.addEventListener('mousemove', glowMove, fine));

    /* ── 3D tilt ── */
    const tilts = document.querySelectorAll<HTMLElement>('[data-tilt]');
    const tiltMove = (e: MouseEvent) => {
      const el = e.currentTarget as HTMLElement;
      const rect = el.getBoundingClientRect();
      const px = (e.clientX - rect.left) / rect.width - 0.5;
      const py = (e.clientY - rect.top) / rect.height - 0.5;
      el.style.transform = `perspective(900px) rotateX(${(-py * 6).toFixed(2)}deg) rotateY(${(px * 6).toFixed(2)}deg)`;
    };
    const tiltLeave = (e: MouseEvent) => {
      (e.currentTarget as HTMLElement).style.transform = '';
    };
    tilts.forEach((el) => {
      el.addEventListener('mousemove', tiltMove, fine);
      el.addEventListener('mouseleave', tiltLeave);
    });

    return () => {
      window.removeEventListener('scroll', onScroll);
      cancelAnimationFrame(raf);
      magnetics.forEach((el) => {
        el.removeEventListener('mousemove', magneticMove);
        el.removeEventListener('mouseleave', magneticLeave);
      });
      glows.forEach((el) => el.removeEventListener('mousemove', glowMove));
      tilts.forEach((el) => {
        el.removeEventListener('mousemove', tiltMove);
        el.removeEventListener('mouseleave', tiltLeave);
      });
    };
  }, []);

  return null;
}