#!/usr/bin/env node
/**
 * Gate: the hero must actually be visible, and the photo behind it must be a
 * background rather than a second, already-finished advert.
 *
 * Both failures shipped as a page that looked fine in review and in the DOM.
 *
 * 1. INVISIBLE HERO. Hero is the one section that never got the scroll-reveal
 *    observer the other eleven have, so its copy sat at opacity 0 forever. The
 *    obvious fix - copy the other sections' IntersectionObserver - cannot work:
 *    the headline lines are revealed by `clip-path: inset(-10% 0 110% 0)`, and a
 *    clip-path that hides an element empties its intersection rect, so the
 *    observer sees zero intersection and never fires. Hero now reveals with a
 *    plain CSS animation, and phase 2 asserts the stronger property that the
 *    whole fix settled on: the copy is fully visible with JavaScript switched
 *    off. The subtler version of the same trap was a CSS Modules one - inside a
 *    module every class in a selector is hashed, so `.reveal.is-visible`
 *    compiled to `.Hero_reveal__x.Hero_is-visible__y`, a class nothing ever
 *    adds, while the JS adds the unhashed literal `is-visible` that only
 *    globals.css can match. Driving the reveal from a class name meant the LCP
 *    depended on hydration, on requestAnimationFrame, and on a hashed name all
 *    being right; all three have failed here, and none is visible in review
 *    because the DOM is correct and the copy is simply not painted.
 *
 * 2. COPY BAKED INTO THE PHOTO. The hero background shipped as an AI-generated
 *    marketing mockup with its own headline, subhead, body line and CTA
 *    composited into the pixels, sitting underneath a live HTML headline saying
 *    the same things. At desktop widths the live copy covered it. At 502px
 *    `background-size: cover` re-crops the photo, the baked copy slid out from
 *    under the live headline and its tail - the "ВО" of "ПАЛИВО" - landed on top
 *    of the App Store badge. No DOM node contains that text, so no DOM
 *    assertion can see it; it has to be measured in the pixels of the asset.
 *
 * Runs against the built export, because hashed CSS and static export paths are
 * properties of the shipped output.
 */

import { launchBrowser, report, requireBrowser, serveOut } from './lib/harness.mjs';

/* Boundaries matter more than round numbers: that is where a media query flips. */
const WIDTHS = [1920, 1440, 1366, 1024, 812, 769, 768, 623, 540, 481, 480, 430, 390, 360, 320];

/** The reveal must be finished this long after load, or it has stalled. */
const SETTLE_TIMEOUT = 5000;

/**
 * A run of consecutive rows that each carry at least this many bright, low
 * saturation pixels in the strip the live headline occupies. Measured on the
 * shipped photo: the baked copy produced 120-283 such pixels per row, and every
 * clean row in the same strip produced at most 25. The gap is wide enough that
 * a bright window or a headlight cannot trip this.
 */
const COPY_ROW_PIXELS = 80;
const COPY_MIN_ROWS = 8;
/** Only the left band is checked: that is where the live headline is painted. */
const COPY_BAND = 0.55;

const puppeteer = await requireBrowser();
const site = await serveOut();
const browser = await launchBrowser(puppeteer);
const failures = [];
const rows = [];

try {
  const page = await browser.newPage();

  /* ── Phase 1: is the hero actually painted? ── */
  for (const width of WIDTHS) {
    await page.setViewport({ width, height: 900 });
    await page.goto(`${site.origin}/`, { waitUntil: 'networkidle0' });
    await page.evaluate(() => document.fonts.ready);

    /* Wait for the reveal to actually finish rather than guessing a sleep. The
     * stagger runs to 630ms and the clip-path transition to 0.9s, so a fixed
     * pause either wastes time or samples a transition in flight and reports it
     * as a failure. The invariant a visitor cares about is "it ends up visible",
     * so that is what is waited on - a hardcoded delay would also break the
     * moment someone tunes a duration. */
    const settled = await page.evaluate(async (timeout) => {
      const bottomInset = (clip) => {
        const m = /^inset\(([^)]*)\)$/.exec(clip);
        if (!m) return null;
        const p = m[1].trim().split(/\s+/).map(parseFloat);
        if (p.some(Number.isNaN)) return null;
        return p.length >= 3 ? p[2] : p[0];
      };
      const done = () => [...document.querySelectorAll('#top [class*="Hero_reveal"]')].every((el) => {
        const cs = getComputedStyle(el);
        const bottom = bottomInset(cs.clipPath);
        return parseFloat(cs.opacity) >= 0.999 && (bottom === null || bottom <= 0);
      });
      const deadline = Date.now() + timeout;
      while (Date.now() < deadline) {
        if (done()) return true;
        await new Promise((r) => setTimeout(r, 100));
      }
      return done();
    }, SETTLE_TIMEOUT);

    const m = await page.evaluate(() => {
      /* `inset(t r b l)` follows the usual CSS shorthand rules, so the bottom
       * edge is the first value for 1-2 values and the third for 3-4. Chrome
       * collapses `inset(-10% 0px -10% 0px)` to `inset(-10% 0px)`, so both
       * forms have to be read correctly or the check silently passes. */
      const bottomInset = (clip) => {
        const m = /^inset\(([^)]*)\)$/.exec(clip);
        if (!m) return null;
        const p = m[1].trim().split(/\s+/).map(parseFloat);
        if (p.some(Number.isNaN)) return null;
        return p.length >= 3 ? p[2] : p[0];
      };

      const doc = document.documentElement;
      const items = [...document.querySelectorAll('#top [class*="Hero_reveal"]')];
      const faded = [];
      const masked = [];
      for (const el of items) {
        const cs = getComputedStyle(el);
        const label = (el.textContent || '').trim().slice(0, 22) || el.className.split(' ')[0];
        if (parseFloat(cs.opacity) < 0.999) faded.push(`${label}@${cs.opacity}`);
        const bottom = bottomInset(cs.clipPath);
        if (bottom !== null && bottom > 0) masked.push(`${label}@inset(${bottom}%)`);
      }

      const badge = [...document.querySelectorAll('#top a')].find((a) =>
        /app ?store/i.test(`${a.textContent}${a.getAttribute('aria-label') || ''}`));
      const br = badge ? badge.getBoundingClientRect() : null;

      return {
        total: items.length,
        faded,
        masked,
        overflow: doc.scrollWidth > doc.clientWidth ? `${doc.scrollWidth}>${doc.clientWidth}` : null,
        badge: br
          ? { w: Math.round(br.width), h: Math.round(br.height), right: Math.round(br.right), vw: doc.clientWidth }
          : null,
      };
    });

    if (!m.total) {
      failures.push(`${width}px: no hero reveal elements matched - the gate is measuring nothing`);
    }
    if (!settled) {
      failures.push(
        `${width}px: the hero reveal never finished within ${SETTLE_TIMEOUT / 1000}s - ` +
        `${m.faded.length ? `still faded: ${m.faded.join(', ')}; ` : ''}` +
        `${m.masked.length ? `still clipped: ${m.masked.join(', ')}` : ''}`.trim(),
      );
    }
    if (m.overflow) {
      failures.push(`${width}px: the page scrolls sideways (${m.overflow})`);
    }
    if (!m.badge) {
      failures.push(`${width}px: the hero has no App Store badge`);
    } else if (m.badge.w < 100 || m.badge.h < 44) {
      failures.push(`${width}px: the App Store badge collapsed to ${m.badge.w}x${m.badge.h}`);
    } else if (m.badge.right > m.badge.vw + 1) {
      failures.push(`${width}px: the App Store badge runs off the right edge (${m.badge.right} in ${m.badge.vw})`);
    }

    rows.push(
      `  ${String(width).padStart(4)}px  reveals=${m.total}` +
      `  settled=${settled ? 'yes' : 'NO'}` +
      `  faded=${m.faded.length}` +
      `  clipped=${m.masked.length}` +
      `  badge=${m.badge ? `${m.badge.w}x${m.badge.h}` : 'MISSING'}` +
      `  overflow=${m.overflow || 'none'}`,
    );
  }

  /* ── Phase 2: is it visible with JavaScript switched off? ── */
  /* This is the invariant the fix actually settled on. The hero is the LCP, so
   * it must not be able to depend on script to exist: no hydration, no
   * requestAnimationFrame, no class name surviving CSS Modules hashing. Any
   * future change that reintroduces a JS-gated entrance fails here, loudly and
   * without needing a reproduction. */
  console.log('\n=== hero with JavaScript disabled ===');
  const noJsPage = await browser.newPage();
  await noJsPage.setJavaScriptEnabled(false);
  for (const width of [1440, 390]) {
    await noJsPage.setViewport({ width, height: 900 });
    await noJsPage.goto(`${site.origin}/`, { waitUntil: 'networkidle0' });
    await new Promise((r) => setTimeout(r, 600));

    const m = await noJsPage.evaluate(() => {
      const bottomInset = (clip) => {
        const match = /^inset\(([^)]*)\)$/.exec(clip);
        if (!match) return null;
        const p = match[1].trim().split(/\s+/).map(parseFloat);
        if (p.some(Number.isNaN)) return null;
        return p.length >= 3 ? p[2] : p[0];
      };
      const items = [...document.querySelectorAll('#top [class*="Hero_reveal"]')];
      const broken = [];
      for (const el of items) {
        const cs = getComputedStyle(el);
        const bottom = bottomInset(cs.clipPath);
        const label = (el.textContent || '').trim().slice(0, 22) || el.className.split(' ')[0];
        if (parseFloat(cs.opacity) < 0.999) broken.push(`${label}@opacity ${cs.opacity}`);
        if (bottom !== null && bottom > 0) broken.push(`${label}@clip-path inset(${bottom}%)`);
      }
      return { total: items.length, broken };
    });

    if (!m.total) {
      failures.push(`${width}px (no JS): no hero reveal elements matched - the gate is measuring nothing`);
    } else if (m.broken.length) {
      failures.push(
        `${width}px (no JS): the hero copy is hidden without JavaScript (${m.broken.join(', ')}).` +
        ' The entrance must be a CSS animation whose hidden start state lives in the keyframe, not a' +
        ' class toggled by script - the hero is the LCP and cannot depend on hydration to be painted.',
      );
    }
    rows.push(`  ${String(width).padStart(4)}px (no JS)  reveals=${m.total}  broken=${m.broken.length}`);
  }
  await noJsPage.close();

  /* ── Phase 3: does the background photo carry its own copy? ── */
  await page.setViewport({ width: 1440, height: 900 });
  await page.goto(`${site.origin}/`, { waitUntil: 'networkidle0' });
  const baked = await page.evaluate(async (band, rowPixels) => {
    const url = /url\("?([^")]+)"?\)/.exec(
      getComputedStyle(document.querySelector('#top [class*="Hero_bgImage"]')).backgroundImage,
    )?.[1];
    if (!url) return { error: 'the hero background has no background-image' };

    const bmp = await createImageBitmap(await (await fetch(url, { cache: 'no-store' })).blob());
    const c = document.createElement('canvas');
    c.width = bmp.width;
    c.height = bmp.height;
    const ctx = c.getContext('2d', { willReadFrequently: true });
    ctx.drawImage(bmp, 0, 0);
    const { data, width: W, height: H } = ctx.getImageData(0, 0, c.width, c.height);
    const bandX = Math.floor(W * band);

    let run = 0;
    let longest = 0;
    let at = -1;
    for (let y = 0; y < H; y++) {
      let bright = 0;
      for (let x = 0; x < bandX; x++) {
        const i = (y * W + x) * 4;
        const r = data[i], g = data[i + 1], b = data[i + 2];
        const mx = Math.max(r, g, b), mn = Math.min(r, g, b);
        const sat = mx ? (mx - mn) / mx : 0;
        const L = 0.2126 * r + 0.7152 * g + 0.0722 * b;
        if (L > 150 && sat < 0.16) bright++;
      }
      run = bright >= rowPixels ? run + 1 : 0;
      if (run > longest) {
        longest = run;
        at = y - run + 1;
      }
    }
    return { url, size: `${W}x${H}`, longest, at };
  }, COPY_BAND, COPY_ROW_PIXELS);

  console.log('\n=== hero background asset ===');
  console.log(
    baked.error
      ? `  ${baked.error}`
      : `  ${baked.url.split('/').pop()}  ${baked.size}  longest text-like run=${baked.longest} rows (fails at ${COPY_MIN_ROWS})`,
  );

  if (baked.error) {
    failures.push(baked.error);
  } else if (baked.longest >= COPY_MIN_ROWS) {
    failures.push(
      `the hero background (${baked.url}) has baked-in copy: ${baked.longest} consecutive rows of text-like` +
      ` pixels in the left ${Math.round(COPY_BAND * 100)}%, starting at y=${baked.at} of ${baked.size}.` +
      ' It is a finished advert, not a background - the live headline is painted on top of it.',
    );
  }
} finally {
  await browser.close();
  site.close();
}

console.log('\n[hero] reveal ladder');
for (const r of rows) console.log(r);

if (failures.length) {
  console.error('\nSee the reveal rules in website/src/components/Hero.module.css and the mount effect in Hero.tsx.');
}
report(
  'hero',
  failures,
  `copy settles visible and unclipped across ${WIDTHS.length} widths, the CTA fits, and the background carries no baked-in copy.`,
);
