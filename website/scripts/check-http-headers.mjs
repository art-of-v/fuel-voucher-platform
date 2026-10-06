#!/usr/bin/env node
/**
 * Gate: the caching nginx.conf promises must actually be the caching that ships.
 *
 * The config has always said the right thing in its comments and never done it,
 * and nothing checked, so neither half was ever noticed:
 *
 *   1. Hashed assets were never immutable. `location /_next/static/` looks like
 *      the right block, but nginx remembers the longest PREFIX match and then
 *      lets a regex location win over it. `~* \.(js|css|...)$` matches every
 *      hashed .css and .js, so it took precedence and the immutable header was
 *      unreachable dead code. Every build asset was revalidated on every single
 *      page load - the opposite of what the comment two lines above promises.
 *
 *   2. HTML had no Cache-Control at all. `.html` was not in the regex list, so
 *      `/`, `/support/` and `/404.html` fell through to `location /`, which has
 *      no add_header. nginx emitted only Last-Modified and ETag - exactly the
 *      pair that makes browsers apply heuristic caching. This is why a deploy
 *      could land and a phone could still be showing the previous build.
 *
 * Both are one-line classes of fix and neither is visible in review, so this
 * runs the real nginx with the real website/nginx.conf against the real built
 * export and reads the headers off the wire. Asserting on the text of the config
 * instead would pass happily on a config nginx cannot even parse.
 *
 * It also closes a hole that is not about caching at all: `docker build` never
 * starts nginx, so a syntactically invalid nginx.conf passes CI and only fails
 * once the deploy restarts the container. Starting it here is the check.
 *
 * Missing Docker degrades to a warning locally and is a hard failure in CI, the
 * same policy as a missing browser in check-hero.mjs - an unrunnable gate on a
 * bare dev box is worse than no gate, but a silent skip in CI is worse still.
 */

import { spawnSync } from 'node:child_process';
import { existsSync, readdirSync } from 'node:fs';
import path from 'node:path';

import { outDir, report, root } from './lib/harness.mjs';

const IMAGE = 'nginx:alpine';
const CONTAINER = 'fuelflow-headers-check';

/** Must be true for these paths, described by what each one actually is. */
const EXPECTATIONS = [
  {
    what: 'the HTML document',
    kind: 'html',
    match: 'must-revalidate',
    why: 'it is not content-addressed, so a redeploy has to be able to reach a visitor',
  },
  {
    what: 'a directory-style HTML page',
    path: '/support/',
    match: 'must-revalidate',
    why: 'trailingSlash emits these, and they are still just HTML',
  },
  {
    what: 'the 404 document',
    path: '/404.html',
    match: 'must-revalidate',
    why: 'it is HTML reached directly, not only through the error handler',
  },
  {
    what: 'a 404 response',
    path: '/no-such-page-at-all',
    status: 404,
    match: 'must-revalidate',
    why: 'error responses need the header too, which is what `always` is for',
  },
  {
    what: 'a hashed build asset',
    kind: 'next-asset',
    match: 'immutable',
    why: 'its filename is its content hash, so it can never go stale',
  },
  {
    what: 'an image',
    kind: 'image',
    match: 'must-revalidate',
    why: 'the name is stable, so a redeploy can replace the bytes behind it',
  },
];

function docker(...args) {
  /* Bounded too: a wedged docker CLI would otherwise block the event loop
   * outright, which no timeout on the fetches could ever recover from. */
  return spawnSync('docker', args, { encoding: 'utf8', timeout: 60_000 });
}

if (!existsSync(path.join(outDir, 'index.html'))) {
  console.error('[headers] out/index.html not found - run "npm run build" first.');
  process.exit(1);
}
if (docker('version', '--format', '{{.Server.Version}}').status !== 0) {
  const msg = '[headers] Docker is not available - cannot verify nginx.conf against a real nginx';
  if (process.env.CI) {
    console.error(msg);
    process.exit(1);
  }
  console.warn(`${msg} (skipped)`);
  process.exit(0);
}

/* Pick real files out of the build so the paths below exist for real. */
const nextAssets = [];
const walk = (dir) => {
  for (const entry of readdirSync(dir, { withFileTypes: true })) {
    const full = path.join(dir, entry.name);
    if (entry.isDirectory()) walk(full);
    else if (/\.(css|js)$/.test(entry.name) && entry.name.includes('.')) nextAssets.push('/' + path.relative(outDir, full).split(path.sep).join('/'));
  }
};
walk(path.join(outDir, '_next', 'static'));

const image = readdirSync(outDir).find((f) => /\.(jpg|jpeg|png|svg|webp)$/i.test(f));
const target = (e) => e.path ?? ({
  html: '/',
  'next-asset': nextAssets[0],
  image: image ? `/${image}` : null,
}[e.kind]);

const missing = [...new Set(EXPECTATIONS.map(target).filter((p) => p === undefined || p === null))];
if (missing.length) {
  console.error(`[headers] the build has no ${missing.length === 1 ? 'file' : 'files'} to test: ${missing.length}`);
  process.exit(1);
}

/* ── Run the real thing ── */
docker('rm', '-f', CONTAINER);
/* Deliberately not --rm: if nginx refuses the config the container dies
 * immediately, and a persisted one is the only way to read why. */
const up = docker(
  'run', '-d',
  '--name', CONTAINER,
  '-p', '127.0.0.1::5001',
  '-v', `${path.join(root, 'nginx.conf')}:/etc/nginx/conf.d/default.conf:ro`,
  '-v', `${outDir}:/usr/share/nginx/html:ro`,
  IMAGE,
);
if (up.status !== 0) {
  console.error(`[headers] could not start nginx:\n${up.stderr}`);
  process.exit(1);
}

const port = (docker('port', CONTAINER, '5001/tcp').stdout || '').trim().split('\n')[0].split(':').pop();
if (!port || !/^\d+$/.test(port)) {
  console.error(`[headers] could not work out the published port for ${CONTAINER} - "docker port" said nothing.`);
  console.error(`[headers] docker port output: ${JSON.stringify(docker('port', CONTAINER, '5001/tcp').stdout)}`);
  docker('rm', '-f', CONTAINER);
  process.exit(1);
}
const origin = `http://127.0.0.1:${port}`;

const failures = [];
const rows = [];

/* Every wait in here is bounded. That is not defensive decoration: this gate
 * once wedged a CI job to a wall-clock timeout and exited 13 with no output at
 * all, because an unbounded fetch against a container that had died between the
 * readiness probe and the first assertion sat waiting for a port nobody would
 * ever answer on. A check that can hang instead of fail is worse than no check,
 * so the fetches time out, the readiness loop is bounded by the clock rather
 * than by an iteration count, and every row is printed as it completes so a
 * future failure is diagnosable from the log even if something else goes wrong. */
const FETCH_TIMEOUT = 5000;
const get = (url) => fetch(url, { redirect: 'manual', signal: AbortSignal.timeout(FETCH_TIMEOUT) });

console.log(`\n[headers] nginx on ${origin} (container ${CONTAINER})`);

try {
  /* nginx refuses to start on a bad config. Surface that as a real failure with
   * its own output, because today this only appears after a deploy. */
  const deadline = Date.now() + 20_000;
  let ready = false;
  while (Date.now() < deadline && !ready) {
    await new Promise((r) => setTimeout(r, 200));
    try {
      ready = (await get(`${origin}/`)).status > 0;
    } catch {
      /* not up yet, or not answering - the clock is what ends this */
    }
  }
  if (!ready) {
    const logs = docker('logs', '--tail', '10', CONTAINER);
    const detail = (logs.stdout + logs.stderr).trim() || up.stderr.trim() || 'no output';
    failures.push(
      `nginx did not answer on ${origin} within 20s - ${detail.split('\n').slice(-3).join(' | ')}. ` +
      'This config is only read when the container starts, so without this check a broken one passes',
    );
  } else {
    for (const { what, match, why, status, ...rest } of EXPECTATIONS) {
      const p = target(rest);
      let res;
      try {
        res = await get(`${origin}${p}`);
      } catch (e) {
        failures.push(`${what} (${p}) never answered within ${FETCH_TIMEOUT}ms - ${e.message}`);
        rows.push(`  ${String(p).padEnd(42)} ${'TIMEOUT'.padEnd(4)} -`);
        continue;
      }
      const cc = res.headers.get('cache-control') || '<absent>';
      const statusOk = status === undefined || res.status === status;
      if (!statusOk || !cc.includes(match)) {
        failures.push(
          `${what} (${p}) serves Cache-Control: ${cc} - it must contain "${match}", because ${why}.` +
          (statusOk ? '' : ` (expected status ${status}, got ${res.status})`),
        );
      }
      rows.push(`  ${String(p).padEnd(42)} ${String(res.status).padEnd(4)} ${cc}`);
      console.log(rows[rows.length - 1]);
    }
  }
} finally {
  docker('rm', '-f', CONTAINER);
}

if (failures.length) {
  console.error('\nSee website/nginx.conf. A regex location beats a plain prefix location, so /_next/static/');
  console.error('needs `location ^~` to keep its header from being overridden.');
}
report(
  'headers',
  failures,
  'HTML and images revalidate, hashed build assets are immutable, and nginx starts with the shipped config.',
);