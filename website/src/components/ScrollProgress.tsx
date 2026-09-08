'use client';

import { useEffect, useRef, useState } from 'react';

/** Fixed scroll-progress bar at the very top of the viewport. */
export default function ScrollProgress() {
  const ref = useRef<HTMLDivElement>(null);
  const [hidden, setHidden] = useState(true);

  useEffect(() => {
    const onScroll = () => {
      const doc = document.documentElement;
      const max = doc.scrollHeight - window.innerHeight;
      const p = max > 0 ? window.scrollY / max : 0;
      if (ref.current) ref.current.style.transform = `scaleX(${p})`;
      setHidden(window.scrollY < 40);
    };
    onScroll();
    window.addEventListener('scroll', onScroll, { passive: true });
    return () => window.removeEventListener('scroll', onScroll);
  }, []);

  return (
    <div
      ref={ref}
      className="scrollProgress"
      style={{ opacity: hidden ? 0 : 1, transition: 'opacity 200ms' }}
      aria-hidden="true"
    />
  );
}