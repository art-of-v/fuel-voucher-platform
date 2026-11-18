#!/usr/bin/env node
/**
 * Gate: the hero must remain visible across every responsive boundary and carry
 * the approved Fuel Flow product-stage branding rather than the retired station photo.
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
 * 2. VISUAL REGRESSION. A previous hero used an AI-generated marketing mockup
 *    with baked-in copy, then a generic station photo replaced it. The approved
 *    prototype is product-led instead: it carries the new Fuel Flow mark, the
 *    wallet phone and fleet-control information as live interface elements.
 *    Phase 3 pins that composition and refuses the retired station-photo hero.
 *
 * Runs against the built export, because hashed CSS and static export paths are
 * properties of the shipped output.
 */

import { launchBrowser, report, requireBrowser, serveOut } from './lib/harness.mjs';

/* Boundaries matter more than round numbers: that is where a media query flips. */
const WIDTHS = [1920, 1440, 1366, 1024, 812, 769, 768, 623, 540, 481, 480, 430, 390, 360, 320];

/** The reveal must be finished this long after load, or it has stalled. */
const SETTLE_TIMEOUT = 5000;

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

  /* ── Phase 2b: reduced motion renders the settled state immediately ── */
  const reducedPage = await browser.newPage();
  await reducedPage.emulateMediaFeatures([{ name: 'prefers-reduced-motion', value: 'reduce' }]);
  await reducedPage.setViewport({ width: 390, height: 900 });
  await reducedPage.goto(`${site.origin}/`, { waitUntil: 'networkidle0' });
  const reduced = await reducedPage.evaluate(() => {
    const items = [...document.querySelectorAll('#top [class*="Hero_reveal"]')];
    const visual = document.querySelector('#top [class*="Hero_phoneWrap"]');
    return {
      count: items.length,
      hidden: items.filter((element) => parseFloat(getComputedStyle(element).opacity) < 0.999).length,
      animated: items.filter((element) => getComputedStyle(element).animationName !== 'none').length,
      visualAnimated: visual ? getComputedStyle(visual).animationName !== 'none' : true,
    };
  });
  if (!reduced.count || reduced.hidden || reduced.animated || reduced.visualAnimated) {
    failures.push(`390px (reduced motion): hero is not settled (${JSON.stringify(reduced)})`);
  }
  rows.push(
    `   390px (reduced motion)  reveals=${reduced.count}  hidden=${reduced.hidden}  animated=${reduced.animated}`,
  );
  await reducedPage.close();

  /* ── Phase 3: approved brand/product stage, no retired station photo ── */
  await page.setViewport({ width: 1440, height: 900 });
  await page.goto(`${site.origin}/`, { waitUntil: 'networkidle0' });
  const stage = await page.evaluate(() => {
    const hero = document.querySelector('#top');
    const logo = hero?.querySelector('img[src*="fuelflow-mark"]');
    const phone = hero?.querySelector('[class*="Hero_phoneWrap"]');
    const oldPhoto = hero?.querySelector('img[src*="hero-station"], [class*="Hero_bgImage"]');
    return {
      logo: Boolean(logo),
      phone: Boolean(phone),
      oldPhoto: Boolean(oldPhoto),
      text: hero?.textContent || '',
    };
  });

  console.log('\n=== hero product stage ===');
  console.log(`  logo=${stage.logo}  phone=${stage.phone}  retiredPhoto=${stage.oldPhoto}`);
  if (!stage.logo) failures.push('the hero does not carry the approved Fuel Flow mark');
  if (!stage.phone) failures.push('the hero does not carry the product phone stage');
  if (stage.oldPhoto) failures.push('the retired station-photo hero is still present');
  if (!stage.text.includes('Fleet control') || !stage.text.includes('Ціна зафіксована')) {
    failures.push('the fleet-control product stage is incomplete');
  }
} finally {
  await browser.close();
  site.close();
}

console.log('\n[hero] reveal ladder');
for (const r of rows) console.log(r);

if (failures.length) {
  console.error('\nSee the responsive and motion rules in website/src/components/Hero.module.css.');
}
report(
  'hero',
  failures,
  `copy settles visible and unclipped across ${WIDTHS.length} widths, the CTA fits, and the approved brand/product stage is present.`,
);
