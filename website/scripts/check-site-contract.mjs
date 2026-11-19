#!/usr/bin/env node

import { existsSync, readFileSync } from 'node:fs';
import path from 'node:path';
import { launchBrowser, report, requireBrowser, root, serveOut } from './lib/harness.mjs';

const SAFE_SCREENS = [
  'app-networks.jpg',
  'app-packages.jpg',
  'app-basket.jpg',
  'app-codes.jpg',
  'app-company.jpg',
  'app-map.jpg',
];
const FORBIDDEN_SCREENS = ['design-4.jpg', 'design-5.jpg', 'design-7.jpg', 'design-8.jpg'];
const LEGACY_ANCHORS = ['numbers', 'prices', 'networks', 'contact'];

const failures = [];
const showcaseSource = readFileSync(path.join(root, 'src/components/AppShowcase.tsx'), 'utf8');

for (const name of SAFE_SCREENS) {
  if (!showcaseSource.includes(`/${name}`)) failures.push(`safe screenshot ${name} is missing from the manifest`);
  if (!existsSync(path.join(root, 'public', name))) failures.push(`safe screenshot ${name} is missing from public/`);
}
for (const name of FORBIDDEN_SCREENS) {
  if (showcaseSource.includes(`/${name}`)) failures.push(`sensitive screenshot ${name} is still referenced`);
  if (existsSync(path.join(root, 'public', name))) failures.push(`sensitive screenshot ${name} is still publicly exported`);
}

const puppeteer = await requireBrowser();
const site = await serveOut();
const browser = await launchBrowser(puppeteer);
try {
  const page = await browser.newPage();
  await page.setViewport({ width: 390, height: 844, isMobile: true, hasTouch: true });
  await page.goto(`${site.origin}/`, { waitUntil: 'networkidle0' });

  const contract = await page.evaluate((anchors) => {
    const srcs = [...document.querySelectorAll('#app img')]
      .map((image) => new URL(image.currentSrc || image.src).pathname.split('/').pop())
      .filter(Boolean);
    return {
      anchors: Object.fromEntries(anchors.map((id) => [id, Boolean(document.getElementById(id))])),
      lead: Boolean(document.getElementById('business-lead')),
      uniqueScreens: [...new Set(srcs)],
      overflow: document.documentElement.scrollWidth > document.documentElement.clientWidth,
    };
  }, LEGACY_ANCHORS);

  for (const [anchor, exists] of Object.entries(contract.anchors)) {
    if (!exists) failures.push(`legacy #${anchor} anchor is missing`);
  }
  if (!contract.lead) failures.push('primary #business-lead anchor is missing');
  if (contract.overflow) failures.push('390px document scrolls horizontally');
  if (
    contract.uniqueScreens.length !== SAFE_SCREENS.length ||
    SAFE_SCREENS.some((name) => !contract.uniqueScreens.includes(name))
  ) {
    failures.push(`gallery exports the wrong screenshot set: ${contract.uniqueScreens.join(', ')}`);
  }

  for (const route of ['/privacy/', '/terms/']) {
    await page.goto(`${site.origin}${route}`, { waitUntil: 'networkidle0' });
    const text = await page.$eval('main', (element) => element.textContent || '');
    if (route === '/privacy/' && !text.includes('365 днів')) {
      failures.push('privacy page does not disclose terminal lead retention');
    }
    if (route === '/terms/' && !text.includes('не укладає договір')) {
      failures.push('terms page does not clarify that a lead form is not a contract');
    }
  }

  /* ── The "scan to install" card must never sit on the phone mock ── */
  /* It used to be absolutely positioned over the phone's lower half, so its own
   * copy landed on the mock's white QR plate and went unreadable, and the page
   * showed two QR codes fighting in one square. Hiding it below 1050px did not
   * work either: `.qr { display: none }` lost to StoreQrCard's own
   * `.card { display: flex }` - one class each, and the card's module came
   * later in the bundle - so the overlap survived at 750px as well. Neither
   * failure is visible in the DOM; both are geometry, so geometry is asserted. */
  const CARD_WIDTHS = [1920, 1440, 1100, 1000, 900, 820, 750, 390];
  for (const width of CARD_WIDTHS) {
    await page.setViewport({ width, height: 900 });
    await page.goto(`${site.origin}/`, { waitUntil: 'networkidle0' });
    const geometry = await page.evaluate(() => {
      const section = document.getElementById('download');
      if (!section) return { error: 'the download section is missing' };
      /* The phone mock, not the first aria-hidden node: the section's own scrim
       * carries that attribute too and covers everything. */
      const phone = section.querySelector('[class*="PhoneMock"]');
      if (!phone) return { error: 'the download section has no phone mock' };
      const plate = section.querySelector('img[alt*="QR-код"]');
      /* card > QR plate > image, so the card is the plate's parent - the class
       * name is hashed per build and this check must not care. */
      const card = plate ? plate.closest('div')?.parentElement : null;
      /* An ancestor's `display: none` does not change this element's computed
       * display, so "is it rendered" is asked of the box tree instead. */
      if (!card || card.getClientRects().length === 0) return { visible: false, overlap: 0 };
      const a = phone.getBoundingClientRect();
      const b = card.getBoundingClientRect();
      const overlap = Math.max(0, Math.min(a.right, b.right) - Math.max(a.left, b.left))
        * Math.max(0, Math.min(a.bottom, b.bottom) - Math.max(a.top, b.top));
      return {
        visible: true,
        overlap: Math.round(overlap),
        card: [Math.round(b.left), Math.round(b.top), Math.round(b.width), Math.round(b.height)],
      };
    });
    if (geometry.error) {
      failures.push(`${width}px: ${geometry.error}`);
    } else if (geometry.overlap > 0) {
      failures.push(
        `${width}px: the "scan to install" card overlaps the phone mock by ${geometry.overlap}px²` +
        ` (card at ${geometry.card.join(',')}) - its copy is unreadable over the mock's own QR plate`,
      );
    }
    console.log(
      `  ${String(width).padStart(4)}px  install card ${geometry.visible ? 'visible' : 'hidden'}` +
      `${geometry.overlap ? `  overlap=${geometry.overlap}px²` : ''}`,
    );
  }

  await page.close();
} finally {
  await browser.close();
  site.close();
}

report(
  'site-contract',
  failures,
  `${SAFE_SCREENS.length} reviewed screenshots, ${LEGACY_ANCHORS.length} legacy anchors and both legal disclosures are intact.`,
);
