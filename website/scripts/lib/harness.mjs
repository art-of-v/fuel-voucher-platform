/**
 * Shared harness for the website's browser-backed checks.
 *
 * Every gate here measures the BUILT export rather than the dev server, because
 * the things that break - hashed CSS, media queries, flex overflow - are
 * properties of the shipped output. So all of them need the same three things:
 * a static server over out/, a browser, and a missing-browser policy.
 */

import { createServer } from 'node:http';
import { existsSync, readFileSync, statSync } from 'node:fs';
import path from 'node:path';
import { fileURLToPath } from 'node:url';

export const root = path.resolve(path.dirname(fileURLToPath(import.meta.url)), '..', '..');
export const outDir = path.join(root, 'out');

const MIME = {
  '.html': 'text/html; charset=utf-8', '.js': 'text/javascript', '.css': 'text/css',
  '.svg': 'image/svg+xml', '.jpg': 'image/jpeg', '.jpeg': 'image/jpeg', '.png': 'image/png',
  '.ico': 'image/x-icon', '.json': 'application/json', '.woff2': 'font/woff2',
  '.webp': 'image/webp',
};

export function findBrowser() {
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

/**
 * Resolve puppeteer-core, or explain why the check cannot run.
 *
 * A missing browser downgrades to a warning locally, because an unrunnable gate
 * on a bare dev box is worse than no gate. In CI it is a hard failure: that is
 * where a silent skip would let a regression ship.
 */
export async function requireBrowser() {
  if (!existsSync(path.join(outDir, 'index.html'))) {
    console.error('[check] out/index.html not found - run "npm run build" first.');
    process.exit(1);
  }
  const executablePath = findBrowser();
  if (!executablePath) {
    const msg = '[check] no Chrome/Chromium found - set CHROME_PATH to run this check';
    if (process.env.CI) {
      console.error(msg);
      process.exit(1);
    }
    console.warn(`${msg} (skipped)`);
    process.exit(0);
  }
  return (await import('puppeteer-core')).default;
}

/** Serve the built export on an ephemeral port. */
export async function serveOut() {
  const server = createServer((req, res) => {
    let pathname = decodeURIComponent(req.url.split('?')[0].split('#')[0]);
    if (pathname.endsWith('/')) pathname += 'index.html';
    const file = path.join(outDir, pathname);
    if (!file.startsWith(outDir) || !existsSync(file) || !statSync(file).isFile()) {
      res.writeHead(404).end('not found');
      return;
    }
    res.writeHead(200, {
      'content-type': MIME[path.extname(file)] ?? 'application/octet-stream',
      'cache-control': 'no-store',
    });
    res.end(readFileSync(file));
  });
  await new Promise((r) => server.listen(0, '127.0.0.1', r));
  return {
    origin: `http://127.0.0.1:${server.address().port}`,
    close: () => server.close(),
  };
}

export async function launchBrowser(puppeteer) {
  return puppeteer.launch({
    executablePath: findBrowser(),
    headless: true,
    args: ['--disable-gpu', '--disable-extensions', '--no-first-run', '--hide-scrollbars'],
  });
}

/** Print failures and exit non-zero, or print the pass line. */
export function report(name, failures, okLine) {
  if (failures.length) {
    console.error(`\n[${name}] FAILED (${failures.length})`);
    for (const f of failures) console.error(`  - ${f}`);
    process.exit(1);
  }
  console.log(`\n[${name}] OK - ${okLine}`);
}
