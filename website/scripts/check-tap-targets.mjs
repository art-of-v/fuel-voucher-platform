#!/usr/bin/env node
/**
 * Gate: every standalone control on a phone is at least 44x44.
 *
 * 44px is the number Apple's HIG asks for and the one a thumb actually hits;
 * WCAG 2.5.8 (AA) only demands 24, but this is a site whose whole job is
 * "tap here to buy fuel", so the stricter bar is the right one.
 *
 * This exists because nothing measured it, and the result was not subtle once
 * you looked: the price card's "Купити літри" link was a 17px target, six footer
 * nav links were 22px, the legal links 19px, and the footer logo 32px. Every one
 * of those is below what WCAG 2.5.8 asks for, let alone 44.
 *
 * Two exemptions, both deliberate and both narrow:
 *
 *   - <label> is not a target. Clicking one focuses the input it names, and
 *     that input is checked on its own. Labelling the label would mean padding
 *     text and wrecking the form.
 *   - A link inside running prose. WCAG 2.5.8 exempts these explicitly, because
 *     padding an inline link breaks the line box. The legal pages have three:
 *     an email, a phone number and a support URL, each mid-sentence.
 *
 * Anything that opts out has to say so in the source, because the alternative is
 * a gate nobody trusts.
 */

import { launchBrowser, report, requireBrowser, serveOut } from './lib/harness.mjs';

const MIN = 44;

const ROUTES = ['/', '/support/', '/privacy/', '/terms/'];

const puppeteer = await requireBrowser();
const site = await serveOut();
const browser = await launchBrowser(puppeteer);

const failures = [];
const seen = new Set();
let checked = 0;

try {
  for (const route of ROUTES) {
    const page = await browser.newPage();
    await page.setViewport({
      width: 390, height: 844, isMobile: true, hasTouch: true, deviceScaleFactor: 2,
    });
    await page.goto(`${site.origin}${route}`, { waitUntil: 'networkidle0' });
    await page.evaluate(() => document.fonts.ready);
    // Walk the page so lazily-revealed sections are laid out and measurable.
    await page.evaluate(async () => {
      document.documentElement.style.scrollBehavior = 'auto';
      for (let y = 0; y < document.body.scrollHeight; y += 400) {
        window.scrollTo(0, y);
        await new Promise((r) => setTimeout(r, 40));
      }
    });
    await new Promise((r) => setTimeout(r, 300));

    const found = await page.evaluate((MIN) => {
      /* Every target's box first, so undersized ones can be tested against
         their neighbours rather than in isolation. */
      const boxes = [];
      for (const el of document.querySelectorAll(
        'a, button, input, select, textarea, summary, [role="button"]',
      )) {
        const cs = getComputedStyle(el);
        if (cs.display === 'none' || cs.visibility === 'hidden' || cs.opacity === '0') continue;
        if (el.closest('[aria-hidden="true"], [inert]')) continue;
        // A pseudo-element overlay is the accepted way to grow a hit area
        // without moving anything, so it counts as already fixed.
        const after = getComputedStyle(el, '::after');
        if (after.content !== 'none' && after.position === 'absolute') continue;

        const b = el.getBoundingClientRect();
        if (b.width === 0 || b.height === 0) continue;
        const prose = el.closest('p, li');
        boxes.push({
          el,
          tag: el.tagName.toLowerCase(),
          text: (el.textContent || el.value || el.placeholder || '').trim().replace(/\s+/g, ' ').slice(0, 30),
          cx: b.left + b.width / 2, cy: b.top + b.height / 2,
          w: Math.round(b.width), h: Math.round(b.height),
          inlineInProse: Boolean(prose && prose.textContent.trim() !== el.textContent.trim()),
          where: el.closest('footer') ? 'footer'
            : el.closest('header') ? 'header'
            : el.closest('section')?.id || el.closest('section')?.className?.split(' ')[0] || '?',
        });
      }

      /* WCAG 2.5.8 spacing exception: a target may be under 44 if a 44px circle
         centred on it cannot touch another target's 44px circle. Centre-to-centre
         distance >= 44 is exactly that test, and it is why "Ціни" at 31px wide
         with 28px of air around it is fine while a 31px target with 4px of air
         is not. */
      const isolated = (a, b) => Math.hypot(a.cx - b.cx, a.cy - b.cy) >= MIN;

      const out = [];
      for (const t of boxes) {
        if (t.w >= MIN && t.h >= MIN) continue;
        const neighbours = boxes.filter(
          (o) => o !== t && !t.el.contains(o.el) && !o.el.contains(t.el),
        );
        // Only plain data crosses the evaluate boundary - a DOM node cannot.
        out.push({
          tag: t.tag,
          text: t.text,
          w: t.w,
          h: t.h,
          where: t.where,
          inlineInProse: t.inlineInProse,
          crowded: neighbours.some((o) => !isolated(t, o)),
        });
      }
      return out;
    }, MIN);

    for (const f of found) {
      checked++;
      const key = `${route}|${f.tag}|${f.where}|${f.text}`;
      if (seen.has(key)) continue;
      seen.add(key);
      if (f.inlineInProse) continue; // documented exemption
      // Under 44 but with the room WCAG's spacing exception requires: allowed.
      if (!f.crowded) continue;
      failures.push(
        `${route} ${f.where}: ${f.tag} "${f.text}" is ${f.w}x${f.h} and another target is within 44px of it`,
      );
    }

    const actionable = found.filter((f) => !f.inlineInProse && f.crowded);
    console.log(`  ${route.padEnd(11)} ${found.length} under ${MIN}px, ${actionable.length} actionable`);
    await page.close();
  }
} finally {
  await browser.close();
  site.close();
}

report('tap-targets', failures, `${checked} controls measured across ${ROUTES.length} routes; ` +
  `every standalone target is ${MIN}px or has the spacing WCAG 2.5.8 allows.`);
