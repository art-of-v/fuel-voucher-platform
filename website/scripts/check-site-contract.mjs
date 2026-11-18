#!/usr/bin/env node

import { existsSync, readFileSync } from 'node:fs';
import path from 'node:path';
import { launchBrowser, report, requireBrowser, root, serveOut } from './lib/harness.mjs';

const SAFE_SCREENS = [
  'design-1.jpg',
  'design-2.jpg',
  'design-3.jpg',
  'design-6.jpg',
  'design-9.jpg',
  'design-10.jpg',
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
