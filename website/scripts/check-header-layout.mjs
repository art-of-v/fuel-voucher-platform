#!/usr/bin/env node
/**
 * Gate: the header must never overlap itself, and the primary nav must stay
 * visible on the viewports people actually use.
 *
 * The nav is absolutely centred on the viewport while the logo and the right
 * cluster are pinned to the gutters, so the three only coexist while the
 * viewport is at least nav + logo + right + 2*gutter. When that stops being
 * true nothing looks broken in code review - the header is still centred, the
 * links are still all there - and the result is that "Підтримка" paints on top
 * of the download CTA and, because the nav is pointer-events:auto once the
 * header turns solid, takes its clicks. That is what shipped: an 18px overlap
 * at 1440 and 94px at 1280, on the two most common laptop widths.
 *
 * Two things are asserted, because either one alone is easy to "fix" wrongly:
 *
 *   1. No overlap at any width. Catches the regression.
 *   2. The nav is VISIBLE from 1160px up. Catches the tempting non-fix of
 *      hiding the nav at 1280 to make the arithmetic work, which trades a
 *      cosmetic bug for a real conversion loss on every laptop.
 *
 * Runs against the built export rather than the dev server, because the thing
 * that regresses is the shipped CSS.
 *
 * Two phases. The first is the desktop ladder; the second is the phone menu,
 * which failed separately and just as badly: the burger is the only shrinkable
 * child of the header, so on any phone that ran out of room it absorbed the
 * whole deficit (measured 2px wide from 320px to 430px) and the navigation
 * became unreachable. Nothing about that is visible in review either.
 */

import { createServer } from 'node:http';
import { existsSync, readFileSync, statSync } from 'node:fs';
import path from 'node:path';
import { fileURLToPath } from 'node:url';

const root = path.resolve(path.dirname(fileURLToPath(import.meta.url)), '..');
const outDir = path.join(root, 'out');

/** Nav must be visible at and above this. Keep in sync with Header.module.css. */
const NAV_FLOOR = 1180;
/** The phone number only earns its space at and above this. */
const PHONE_FLOOR = 1500;

/* Boundaries matter more than round numbers: that is where a media query flips. */
const WIDTHS = [
  1920, 1600, 1536, 1501, 1500, 1499, 1440, 1366, 1301, 1300, 1299, 1280, 1200,
  1181, 1180, 1179, 1152, 1120, 1024, 900, 768, 430, 390, 360, 320,
];

/** Real devices plus the two awkward cases: a very short phone and a landscape one. */
const PHONES = [
  [320, 480], [320, 568], [360, 740], [375, 667], [390, 844], [393, 852],
  [412, 915], [430, 932], [844, 390], [740, 360],
];

const MIME = {
  '.html': 'text/html; charset=utf-8', '.js': 'text/javascript', '.css': 'text/css',
  '.svg': 'image/svg+xml', '.jpg': 'image/jpeg', '.jpeg': 'image/png', '.png': 'image/png',
  '.ico': 'image/x-icon', '.json': 'application/json', '.woff2': 'font/woff2',
  '.webp': 'image/webp',
};

function findBrowser() {
  if (process.env.CHROME_PATH && existsSync(process.env.CHROME_PATH)) return process.env.CHROME_PATH;
  const candidates = [
    // Linux (GitHub runners, containers)
    '/usr/bin/google-chrome', '/usr/bin/google-chrome-stable', '/usr/bin/chromium',
    '/usr/bin/chromium-browser', '/snap/bin/chromium',
    // macOS
    '/Applications/Google Chrome.app/Contents/MacOS/Google Chrome',
    '/Applications/Chromium.app/Contents/MacOS/Chromium',
    // Windows
    'C:/Program Files/Google/Chrome/Application/chrome.exe',
    'C:/Program Files (x86)/Google/Chrome/Application/chrome.exe',
    'C:/Program Files (x86)/Microsoft/Edge/Application/msedge.exe',
    'C:/Program Files/Microsoft/Edge/Application/msedge.exe',
  ];
  return candidates.find((p) => existsSync(p));
}

if (!existsSync(outDir)) {
  console.error('[header] out/ not found - run "npm run build" first.');
  process.exit(1);
}

const executablePath = findBrowser();
if (!executablePath) {
  // Failing here would make the gate unrunnable on a bare dev box; failing it
  // in CI is the point, because that is where a silent regression ships from.
  const msg = '[header] no Chrome/Chromium found - set CHROME_PATH to run this check';
  if (process.env.CI) {
    console.error(msg);
    process.exit(1);
  }
  console.warn(`${msg} (skipped)`);
  process.exit(0);
}

const { default: puppeteer } = await import('puppeteer-core');

const server = createServer((req, res) => {
  let pathname = decodeURIComponent(req.url.split('?')[0].split('#')[0]);
  if (pathname.endsWith('/')) pathname += 'index.html';
  const file = path.join(outDir, pathname);
  if (!file.startsWith(outDir) || !existsSync(file) || !statSync(file).isFile()) {
    res.writeHead(404).end('not found');
    return;
  }
  res.writeHead(200, { 'content-type': MIME[path.extname(file)] ?? 'application/octet-stream' });
  res.end(readFileSync(file));
});
await new Promise((r) => server.listen(0, '127.0.0.1', r));

const browser = await puppeteer.launch({
  executablePath,
  headless: true,
  args: ['--disable-gpu', '--disable-extensions', '--no-first-run', '--hide-scrollbars'],
});

const failures = [];
const rows = [];

try {
  const page = await browser.newPage();
  for (const width of WIDTHS) {
    await page.setViewport({ width, height: 900 });
    await page.goto(`http://127.0.0.1:${server.address().port}/`, { waitUntil: 'networkidle0' });
    // The nav only becomes visible after 24px of scroll, so measure it the way
    // a user sees it rather than in its hidden initial state.
    await page.evaluate(() => window.scrollTo(0, 300));
    await page.evaluate(() => document.fonts.ready);
    await new Promise((r) => setTimeout(r, 250));

    const m = await page.evaluate(() => {
      const box = (el) => {
        if (!el) return null;
        const r = el.getBoundingClientRect();
        return { left: r.left, right: r.right };
      };
      const shown = (el) => !!el && getComputedStyle(el).display !== 'none';
      return {
        nav: shown(document.querySelector('header nav'))
          ? box(document.querySelector('header nav')) : null,
        logo: box(document.querySelector('header a[aria-label]')),
        right: shown(document.querySelector('header div[class*="right"]'))
          ? box(document.querySelector('header div[class*="right"]')) : null,
        phone: shown(document.querySelector('header a[href^="tel:"]')),
      };
    });

    // The logo sits left of the nav, so that pair collides on logo.right vs nav.left.
    const overlapLogo = m.nav && m.logo ? Math.max(0, m.logo.right - m.nav.left) : 0;
    const overlapRight = m.nav && m.right ? Math.max(0, m.nav.right - m.right.left) : 0;
    const overlap = Math.max(overlapLogo, overlapRight);

    if (overlap > 0) {
      failures.push(`${width}px: nav overlaps by ${Math.round(overlap)}px`);
    }
    if (width >= NAV_FLOOR && !m.nav) {
      failures.push(`${width}px: primary nav is hidden at or above the ${NAV_FLOOR}px floor`);
    }
    if (width >= PHONE_FLOOR && !m.phone) {
      failures.push(`${width}px: phone number is hidden at or above ${PHONE_FLOOR}px`);
    }
    if (width < PHONE_FLOOR && m.phone) {
      failures.push(`${width}px: phone number is shown below ${PHONE_FLOOR}px, so the nav cannot clear it`);
    }

    rows.push(
      `  ${String(width).padStart(4)}px  nav=${m.nav ? 'shown' : 'hidden'}  phone=${m.phone ? 'shown' : 'hidden'}` +
      (overlap ? `  OVERLAP ${Math.round(overlap)}px` : ''),
    );
  }

  /* ── Phase 2: the phone menu ── */
  console.log('\n=== phone: burger and menu ===');
  for (const [width, height] of PHONES) {
    await page.setViewport({ width, height, isMobile: true, hasTouch: true, deviceScaleFactor: 2 });
    await page.goto(`http://127.0.0.1:${server.address().port}/`, { waitUntil: 'networkidle0' });
    await page.evaluate(() => document.fonts.ready);
    await new Promise((r) => setTimeout(r, 200));

    const burger = await page.evaluate(() => {
      const el = document.querySelector('button[aria-expanded]');
      const b = el.getBoundingClientRect();
      return {
        w: Math.round(b.width), h: Math.round(b.height),
        left: Math.round(b.left), right: Math.round(b.right), top: Math.round(b.top),
        vw: document.documentElement.clientWidth,
      };
    });
    if (burger.w < 44 || burger.h < 44) {
      failures.push(
        `${width}x${height}: burger is ${burger.w}x${burger.h}, under the 44px minimum tap target - it has been squeezed by flex`,
      );
    }
    if (burger.right > burger.vw + 1 || burger.left < -1) {
      failures.push(`${width}x${height}: burger is outside the viewport (${burger.left}..${burger.right} in ${burger.vw}) - it cannot be tapped`);
    }

    // Open it the way a finger would, then confirm every link is reachable.
    await page.touchscreen.tap(burger.left + burger.w / 2, burger.top + burger.h / 2);
    await new Promise((r) => setTimeout(r, 500));

    const menu = await page.evaluate(async () => {
      const panel = document.querySelector('div[class*="mobile"]');
      panel.scrollTop = panel.scrollHeight;
      await new Promise((r) => requestAnimationFrame(r));
      const links = [...panel.querySelectorAll('a')];
      const foot = panel.querySelector('div[class*="mobileFoot"]');
      return {
        open: document.querySelector('button[aria-expanded]').getAttribute('aria-expanded') === 'true',
        scrollable: panel.scrollHeight > panel.clientHeight,
        lastLinkBottom: Math.round(links[links.length - 1].getBoundingClientRect().bottom),
        footBottom: Math.round(foot.getBoundingClientRect().bottom),
        vh: window.innerHeight,
        smallestTarget: Math.min(...links.map((a) => Math.round(a.getBoundingClientRect().height))),
      };
    });

    if (!menu.open) {
      failures.push(`${width}x${height}: tapping the burger did not open the menu`);
    }
    if (menu.lastLinkBottom > menu.vh + 1 || menu.footBottom > menu.vh + 1) {
      failures.push(
        `${width}x${height}: menu content is cut off (last link ${menu.lastLinkBottom}, footer ${menu.footBottom}, viewport ${menu.vh}` +
        `${menu.scrollable ? '' : ' and the panel does not scroll'})`,
      );
    }
    if (menu.smallestTarget < 44) {
      failures.push(`${width}x${height}: a menu link is only ${menu.smallestTarget}px tall`);
    }

    rows.push(
      `  ${String(`${width}x${height}`).padEnd(9)} burger=${burger.w}x${burger.h}` +
      `  menu=${menu.open ? 'opens' : 'DEAD'}` +
      `  fits=${menu.lastLinkBottom <= menu.vh + 1 ? 'yes' : 'scrolls'}` +
      `  minTarget=${menu.smallestTarget}`,
    );
  }
} finally {
  await browser.close();
  server.close();
}

console.log('[header] responsive ladder');
for (const r of rows) console.log(r);

if (failures.length) {
  console.error(`\n[header] FAILED (${failures.length})`);
  for (const f of failures) console.error(`  - ${f}`);
  console.error('\nSee the "Responsive" and "Mobile menu" blocks in website/src/components/Header.module.css.');
  process.exit(1);
}

console.log(
  `\n[header] OK - nav holds to ${NAV_FLOOR}px across ${WIDTHS.length} widths, ` +
  `and the menu is reachable on all ${PHONES.length} phone viewports.`,
);
