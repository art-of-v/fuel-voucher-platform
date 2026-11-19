#!/usr/bin/env node
/**
 * Renders the app screenshots the marketing site shows into public/app-*.jpg.
 *
 * Why this exists: the gallery used to ship device captures of an app that no
 * longer exists — purple accents that had turned green, a basket whose price did
 * not multiply out, an unstyled map canvas printing "API KEY REQUIRED" over
 * Kyiv. A visitor reads a stale capture as the product. The gallery is now
 * generated from the shipped design instead of remembered, and regenerating it
 * is one command.
 *
 * The reference is the app as it looks on a device: the `obsidian` theme's
 * violet, the `GridBackground` grid, and Rajdhani for digits with Cyrillic in the
 * system face (Rajdhani has no Cyrillic glyphs, so the device falls back for
 * every Ukrainian word). `scripts/assets/` carries the 14 KB subsets that make
 * that split reproducible here; the notice beside them is the SIL OFL one.
 *
 * What each screen SAYS lives in `lib/app-screens.mjs`; this file only decides
 * which browser paints it and where the result is written.
 *
 * These are marketing renders, not device captures. Everything personal is
 * invented — the station, the phone tail, the voucher id — and the QR is the
 * demo token from src/config/qr.json, which is non-routable on purpose.
 *
 * Runs before `dev` and `build` (see the predev/prebuild hooks).
 */
import { existsSync } from 'node:fs';
import { readFile, mkdir, rm } from 'node:fs/promises';
import { dirname, resolve } from 'node:path';
import { fileURLToPath } from 'node:url';
import puppeteer from 'puppeteer-core';
import { renderScreen, SCREEN_NAMES } from './lib/app-screens.mjs';

const root = resolve(dirname(fileURLToPath(import.meta.url)), '..');

const WIDTH = 588;
const HEIGHT = 1280;
const SCALE = 2;

const findChrome = () => {
  const candidates = [
    process.env.CHROME_PATH,
    '/usr/bin/google-chrome',
    '/usr/bin/chromium',
    '/Applications/Google Chrome.app/Contents/MacOS/Google Chrome',
    'C:/Program Files/Google/Chrome/Application/chrome.exe',
    'C:/Program Files (x86)/Microsoft/Edge/Application/msedge.exe',
  ].filter(Boolean);
  return candidates.find((p) => existsSync(p));
};

const dataUrl = async (file) =>
  `data:font/ttf;base64,${(await readFile(resolve(root, 'scripts/assets', file))).toString('base64')}`;

const chrome = findChrome();
if (!chrome) {
  console.error('[screens] no Chrome/Chromium found - set CHROME_PATH to render the gallery');
  process.exit(1);
}

const fonts = {
  rajdhani700: await dataUrl('rajdhani-700-subset.ttf'),
  rajdhani600: await dataUrl('rajdhani-600-subset.ttf'),
};

const qrInline = `data:image/svg+xml;base64,${(
  await readFile(resolve(root, 'public/voucher-qr.svg'), 'utf8')
).toString('base64')}`;

/* The export is a static directory; a scratch folder inside it is deleted two
 * lines later either way, and keeping it under out/ means `next build` owns the
 * cleanup of anything this leaves behind after a crash. */
const scratch = resolve(root, 'out/.screens');
await rm(scratch, { recursive: true, force: true });
await mkdir(scratch, { recursive: true });

const browser = await puppeteer.launch({
  executablePath: chrome,
  headless: 'new',
  args: ['--no-sandbox', '--disable-dev-shm-usage'],
});

try {
  const page = await browser.newPage();
  await page.setViewport({ width: WIDTH, height: HEIGHT, deviceScaleFactor: SCALE });

  for (const name of SCREEN_NAMES) {
    await page.setContent(renderScreen(name, { fonts, qrInline }), { waitUntil: 'load' });
    await page.evaluate(() => document.fonts.ready);
    await page.screenshot({
      path: resolve(root, `public/${name}.jpg`),
      type: 'jpeg',
      quality: 90,
    });
    console.log(`[screens] public/${name}.jpg`);
  }
} finally {
  await browser.close();
  await rm(scratch, { recursive: true, force: true });
}
