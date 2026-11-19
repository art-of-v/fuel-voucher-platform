#!/usr/bin/env node
/**
 * Renders the app screenshots the marketing site shows into public/app-*.jpg.
 *
 * Why this exists: the gallery used to ship device captures of an app that no
 * longer exists — purple accents, an unstyled map canvas printing "API KEY
 * REQUIRED" over Kyiv, a basket whose price did not multiply out. A visitor
 * reads a stale capture as the product, so the gallery is now generated from
 * the same design tokens and the same Ukrainian copy the app ships today
 * (`mobile/src/core/design/themes.ts`, `mobile/src/core/i18n/.../uk.ts`), and
 * regenerating it is one command instead of a screenshot ritual nobody repeats.
 *
 * These are marketing renders, not device captures — say so where it matters,
 * and never put real customer data in one: the avatar, the station and the
 * voucher code are invented, and the QR is the demo token from
 * src/config/qr.json (non-routable on purpose).
 *
 * Runs before `dev` and `build` (see the predev/prebuild hooks) so the gallery
 * cannot drift from the tokens the app reads.
 */
import { existsSync } from 'node:fs';
import { readFile, mkdir, rm } from 'node:fs/promises';
import { dirname, resolve } from 'node:path';
import { fileURLToPath } from 'node:url';
import puppeteer from 'puppeteer-core';

const root = resolve(dirname(fileURLToPath(import.meta.url)), '..');

/** The `lemberg` theme in mobile/src/core/design/themes.ts. */
const t = {
  background: '#000000',
  surface: '#0C0C0C',
  surfaceElevated: '#161616',
  surfaceSunken: '#050505',
  text: '#FFFFFF',
  textSecondary: 'rgba(255,255,255,0.72)',
  textMuted: 'rgba(255,255,255,0.56)',
  textDim: 'rgba(255,255,255,0.4)',
  border: 'rgba(255,255,255,0.14)',
  borderSubtle: 'rgba(255,255,255,0.08)',
  primary: '#00E85F',
  primarySubtle: 'rgba(0,232,95,0.14)',
  onPrimary: '#04170B',
  error: '#FF5D5D',
};

/** mobile/src/core/design/palette.ts → BRAND_COLORS. */
const brand = { okko: '#16FF00', wog: '#008B45', upg: '#00C853', klo: '#FFCE00' };

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

const QR = await readFile(resolve(root, 'public/voucher-qr.svg'), 'utf8');
const qrInline = `data:image/svg+xml;base64,${Buffer.from(QR, 'utf8').toString('base64')}`;

const icons = {
  home: '<path d="M3 10.5 12 3l9 7.5V21h-6v-6H9v6H3z"/>',
  map: '<path d="M12 21s7-6.4 7-11a7 7 0 1 0-14 0c0 4.6 7 11 7 11z"/><circle cx="12" cy="10" r="2.6" fill="#000"/>',
  cart: '<path d="M4 5h2l2.4 10.2A2 2 0 0 0 10.4 17h7.2a2 2 0 0 0 2-1.6L21 8H7"/><circle cx="10" cy="20" r="1.4"/><circle cx="18" cy="20" r="1.4"/>',
  user: '<circle cx="12" cy="8" r="3.6"/><path d="M4.5 21c1.2-4 4-6 7.5-6s6.3 2 7.5 6"/>',
};

const tabBar = (active) => `
  <nav class="tabs">
    ${['home', 'map', 'cart', 'user']
      .map(
        (key) => `<span class="tab ${key === active ? 'on' : ''}">
          <svg viewBox="0 0 24 24" fill="currentColor">${icons[key]}</svg>
          ${key === 'cart' ? '<i class="dot">2</i>' : ''}
        </span>`,
      )
      .join('')}
  </nav>`;

const shell = ({ status = '18:14', body, active = 'home' }) => `
<section class="screen">
  <header class="statusbar">
    <span>${status}</span>
    <span class="sb-icons">
      <svg viewBox="0 0 18 12" width="18"><path d="M1 9h2v3H1zm4-3h2v6H5zm4-3h2v9H9zm4-4h2v13h-2z" fill="#fff"/></svg>
      <svg viewBox="0 0 16 12" width="16"><path d="M8 11.2 1 4.2A10 10 0 0 1 15 4.2z" fill="#fff"/></svg>
      <svg viewBox="0 0 26 12" width="26"><rect x="1" y="1.5" width="20" height="9" rx="2.6" fill="none" stroke="#fff" stroke-opacity=".5"/><rect x="3" y="3.5" width="14" height="5" rx="1.4" fill="#fff"/><path d="M23 5v3a2 2 0 0 0 0-3z" fill="#fff" fill-opacity=".5"/></svg>
    </span>
  </header>
  <div class="body">${body}</div>
  ${tabBar(active)}
</section>`;

const baseCss = `
  @font-face {
    font-family: 'Inter';
    src: url('file://${resolve(root, 'public/fonts/Inter-cyrillic.woff2').replace(/\\/g, '/')}') format('woff2');
    font-weight: 100 900;
    font-display: block;
  }
  * { box-sizing: border-box; margin: 0; padding: 0; }
  html, body { width: ${WIDTH}px; height: ${HEIGHT}px; background: ${t.background}; }
  body {
    font-family: 'Inter', system-ui, sans-serif;
    color: ${t.text};
    -webkit-font-smoothing: antialiased;
    font-feature-settings: 'cv11', 'ss01';
  }
  .screen {
    position: relative;
    width: ${WIDTH}px;
    height: ${HEIGHT}px;
    display: flex;
    flex-direction: column;
    background:
      radial-gradient(120% 55% at 50% -10%, rgba(0,232,95,0.10), transparent 60%),
      ${t.background};
  }
  .statusbar {
    display: flex; align-items: center; justify-content: space-between;
    padding: 22px 32px 8px; font-size: 22px; font-weight: 650;
  }
  .sb-icons { display: flex; align-items: center; gap: 9px; }
  .body { flex: 1; padding: 10px 26px 0; display: flex; flex-direction: column; min-height: 0; }
  .tabs {
    height: 96px; display: flex; align-items: center; justify-content: space-around;
    border-top: 1px solid ${t.borderSubtle}; background: rgba(0,0,0,0.72);
    padding-bottom: 12px;
  }
  .tab { position: relative; width: 44px; height: 44px; color: ${t.textDim}; }
  .tab svg { width: 26px; height: 26px; margin: auto; }
  .tab.on { color: ${t.primary}; }
  .tab .dot {
    position: absolute; top: -4px; right: -6px; width: 18px; height: 18px; border-radius: 9px;
    background: ${t.primary}; color: ${t.onPrimary}; font-size: 11px; font-weight: 800;
    display: grid; place-items: center; font-style: normal;
  }
  .eyebrow { font-size: 15px; font-weight: 700; letter-spacing: 0.10em; color: ${t.textMuted}; }
  .title { font-size: 40px; font-weight: 800; letter-spacing: -0.02em; line-height: 1.05; }
  .sub { font-size: 17px; color: ${t.textMuted}; }
  .card {
    background: ${t.surface}; border: 1px solid ${t.borderSubtle}; border-radius: 22px;
  }
  .row { display: flex; align-items: center; }
  .pill {
    display: inline-flex; align-items: center; gap: 7px; height: 34px; padding: 0 14px;
    border-radius: 999px; font-size: 14px; font-weight: 700;
  }
  .cta {
    height: 84px; border-radius: 20px; background: ${t.primary}; color: ${t.onPrimary};
    display: grid; place-items: center; font-size: 19px; font-weight: 800; letter-spacing: 0.03em;
  }
`;

const screens = {
  'app-networks': shell({
    body: `
      <p class="eyebrow">4 МЕРЕЖІ ПОБЛИЗУ</p>
      <h1 class="title" style="margin: 10px 0 6px">Карта мережі</h1>
      <p class="sub" style="margin-bottom: 22px">Знайдено 4 станції на відстані до 5 км</p>
      <div class="card" style="height: 84px; display:flex; align-items:center; gap:14px; padding: 0 24px; margin-bottom: 26px">
        <svg viewBox="0 0 24 24" width="22" height="22" fill="none" stroke="${t.textMuted}" stroke-width="2"><circle cx="11" cy="11" r="7"/><path d="m20 20-4-4"/></svg>
        <span style="font-size:19px; color:${t.textMuted}">Пошук АЗС…</span>
      </div>
      ${[
        { slug: 'okko', name: 'OKKO', addr: 'вул. Городоцька, 128а', d: '0,8 км', from: '55,00' },
        { slug: 'wog', name: 'WOG', addr: 'просп. Свободи, 21', d: '1,9 км', from: '56,40' },
        { slug: 'upg', name: 'UPG', addr: 'вул. Кульпарківська, 93', d: '3,4 км', from: '54,80' },
        { slug: 'klo', name: 'KLO', addr: 'вул. Шевченка, 44', d: '4,1 км', from: '56,00' },
      ]
        .map(
          (s) => `
        <div class="card" style="padding: 24px; margin-bottom: 18px; display:flex; align-items:center; gap:20px">
          <span style="width:72px;height:72px;border-radius:20px;background:${brand[s.slug]};color:#000;
            display:grid;place-items:center;font-weight:900;font-size:14px;letter-spacing:-0.02em">${s.name}</span>
          <div style="flex:1">
            <div style="font-size:25px;font-weight:750;letter-spacing:-0.01em">${s.name}</div>
            <div style="font-size:16px;color:${t.textMuted};margin-top:5px">${s.addr} · ${s.d}</div>
          </div>
          <div style="text-align:right">
            <div style="font-size:14px;color:${t.textDim}">від</div>
            <div style="font-size:24px;font-weight:800">${s.from} ₴</div>
            <div style="font-size:13px;color:${t.textDim}">грн/л</div>
          </div>
        </div>`,
        )
        .join('')}
    `,
  }),

  'app-packages': shell({
    status: '18:15',
    body: `
      <div class="row" style="gap:16px; margin-bottom: 20px">
        <span style="width:44px;height:44px;border-radius:14px;background:${t.surface};display:grid;place-items:center;font-size:22px">‹</span>
        <div>
          <div class="eyebrow">OKKO · КУЛЬПАРКІВСЬКА 93</div>
          <h1 class="title" style="font-size:34px;margin-top:4px">Оберіть пакет</h1>
        </div>
      </div>
      <div class="row" style="gap:12px; margin-bottom: 24px">
        ${['ДП ЄВРО', 'А-95', 'PULLS 95']
          .map(
            (f, i) => `<span class="pill" style="height:44px;font-size:15px;
              background:${i === 0 ? t.primarySubtle : t.surface};
              border:1px solid ${i === 0 ? 'rgba(0,232,95,0.45)' : t.border};
              color:${i === 0 ? t.primary : t.textSecondary}">${f}</span>`,
          )
          .join('')}
      </div>
      ${[
        { l: '10', price: '580,00', per: '58,00', save: '−3,00' },
        { l: '20', price: '1 100,00', per: '55,00', save: '−6,00' },
        { l: '50', price: '2 650,00', per: '53,00', save: '−8,00' },
      ]
        .map(
          (p) => `
        <div class="card" style="padding: 26px; margin-bottom: 18px; border-color:${p.l === '20' ? 'rgba(0,232,95,0.45)' : t.borderSubtle}">
          <div class="row" style="justify-content:space-between;align-items:flex-start">
            <div class="row" style="gap:16px">
              <span style="width:72px;height:72px;border-radius:20px;background:${t.surfaceElevated};
                display:grid;place-items:center;font-size:26px;font-weight:800">${p.l}</span>
              <div>
                <div style="font-size:15px;color:${t.textMuted};letter-spacing:0.08em">ЛІТРІВ</div>
                <div style="font-size:27px;font-weight:800;margin-top:4px">${p.price} ₴</div>
                <div style="font-size:15px;color:${t.textMuted};margin-top:3px">${p.per} ₴ за літр</div>
              </div>
            </div>
            <span class="pill" style="background:${t.primarySubtle};color:${t.primary};border:1px solid rgba(0,232,95,0.4)">
              ЗНИЖКА ${p.save} ₴/л
            </span>
          </div>
          <div class="cta" style="height:66px;margin-top:22px;font-size:17px">ДОДАТИ В КОШИК</div>
        </div>`,
        )
        .join('')}
    `,
    active: 'cart',
  }),

  'app-basket': shell({
    status: '18:16',
    body: `
      <p class="eyebrow">1 ПОЗИЦІЯ</p>
      <h1 class="title" style="margin: 10px 0 24px">Ваш кошик</h1>
      <div class="card" style="padding: 26px; margin-bottom: 20px">
        <div class="row" style="justify-content:space-between;align-items:flex-start">
          <div class="row" style="gap:16px">
            <span style="width:60px;height:60px;border-radius:16px;background:${brand.okko};color:#000;display:grid;place-items:center;font-weight:900">OK</span>
            <div>
              <div style="font-size:22px;font-weight:750">ОККО · ДП ЄВРО</div>
              <div style="font-size:16px;color:${t.textMuted};margin-top:6px">20 літрів · 55,00 ₴ за літр</div>
            </div>
          </div>
          <div style="font-size:22px;font-weight:800">1 100,00 ₴</div>
        </div>
      </div>
      <div class="card" style="height:82px;display:flex;align-items:center;gap:14px;padding:0 22px;margin-bottom:auto">
        <span style="font-size:14px;font-weight:700;color:${t.textMuted};letter-spacing:0.06em">ПРОМОКОД</span>
        <span style="flex:1;font-size:18px;color:${t.textDim}">Введіть код</span>
        <span class="pill" style="background:${t.surfaceElevated};color:${t.textSecondary};border:1px solid ${t.border}">ЗАСТОСУВАТИ</span>
      </div>
      <div style="padding: 26px 0 10px">
        <div class="row" style="justify-content:space-between;font-size:17px;color:${t.textMuted}">
          <span>Підсумок</span><span>1 100,00 ₴</span>
        </div>
        <div class="row" style="justify-content:space-between;align-items:baseline;margin-top:14px">
          <span style="font-size:16px;letter-spacing:0.06em;color:${t.textSecondary};font-weight:700">ДО СПЛАТИ</span>
          <span style="font-size:38px;font-weight:850;letter-spacing:-0.02em">1 100,00 ₴</span>
        </div>
        <div class="cta" style="margin-top:24px">ОФОРМИТИ ЗАМОВЛЕННЯ</div>
      </div>
    `,
    active: 'cart',
  }),

  'app-codes': shell({
    status: '18:17',
    body: `
      <p class="eyebrow">АКТИВНІ ТАЛОНИ · 1</p>
      <h1 class="title" style="margin: 10px 0 22px">Мої талони</h1>
      <div class="card" style="padding: 26px; border-color: rgba(0,232,95,0.4); background: linear-gradient(180deg, rgba(0,232,95,0.08), ${t.surface} 55%)">
        <div class="row" style="justify-content:space-between">
          <div class="row" style="gap:14px">
            <span style="width:52px;height:52px;border-radius:14px;background:${brand.okko};color:#000;display:grid;place-items:center;font-weight:900;font-size:15px">OK</span>
            <div>
              <div style="font-size:23px;font-weight:780">ДП ЄВРО · 20 л</div>
              <div style="font-size:15px;color:${t.textMuted};margin-top:4px">ОККО · Городоцька, 128а</div>
            </div>
          </div>
          <span class="pill" style="background:${t.primarySubtle};color:${t.primary};border:1px solid rgba(0,232,95,0.4)">АКТИВНИЙ</span>
        </div>
        <div style="margin: 22px auto 0; width: 320px; padding: 16px; background:#fff; border-radius: 20px">
          <img src="${qrInline}" style="width:100%;display:block" alt="">
        </div>
        <div class="cta" style="margin-top: 22px; height: 74px; font-size: 18px">ПОКАЗАТИ НА КОЛОНЦІ</div>
      </div>
      <div class="row" style="justify-content:space-between;padding: 24px 4px 0;font-size:16px;color:${t.textMuted}">
        <span>Дійсний до 14.12.2026</span>
        <span>Залишок 2 650,00 ₴</span>
      </div>
    `,
    active: 'user',
  }),

  'app-company': shell({
    status: '18:18',
    body: `
      <p class="eyebrow">КОМПАНІЯ</p>
      <h1 class="title" style="margin: 10px 0 22px">Автопарк «Захід»</h1>
      <div class="row" style="gap: 14px; margin-bottom: 26px">
        ${[
          { k: 'Учасники', v: '12' },
          { k: 'Видано', v: '34' },
          { k: 'Очікують', v: '2' },
        ]
          .map(
            (s) => `<div class="card" style="flex:1;padding: 20px">
              <div style="font-size:34px;font-weight:850;letter-spacing:-0.02em">${s.v}</div>
              <div style="font-size:14px;color:${t.textMuted};margin-top:4px">${s.k}</div>
            </div>`,
          )
          .join('')}
      </div>
      <div class="row" style="justify-content:space-between;margin-bottom:14px">
        <span class="eyebrow">ПРАЦІВНИКИ</span>
        <span style="font-size:15px;color:${t.primary}">Запросити</span>
      </div>
      ${[
        { n: 'Іван Петренко', p: '+380 67 ••• 41 12', g: 'видано 6', s: t.primary },
        { n: 'Олег Гриценко', p: '+380 63 ••• 09 87', g: 'видано 4', s: t.primary },
        { n: 'Марія Дзюба', p: '+380 50 ••• 77 20', g: 'на паузі', s: 'rgba(251,191,36,0.9)' },
        { n: 'Тарас Білий', p: '+380 93 ••• 12 04', g: 'видано 2', s: t.primary },
      ]
        .map(
          (w) => `<div class="card" style="padding: 20px 22px;margin-bottom:12px;display:flex;align-items:center;gap:16px">
            <span style="width:48px;height:48px;border-radius:24px;background:${t.surfaceElevated};display:grid;place-items:center;font-weight:800">${w.n[0]}</span>
            <div style="flex:1">
              <div style="font-size:19px;font-weight:700">${w.n}</div>
              <div style="font-size:14px;color:${t.textDim};margin-top:3px">${w.p}</div>
            </div>
            <span style="font-size:14px;font-weight:700;color:${w.s}">${w.g}</span>
          </div>`,
        )
        .join('')}
    `,
    active: 'user',
  }),

  'app-map': shell({
    status: '18:19',
    body: `
      <div style="position:absolute;inset:0;background:
        radial-gradient(70% 40% at 30% 30%, rgba(0,232,95,0.06), transparent 60%),
        linear-gradient(180deg, #06090A, #04060A 60%, #000);
        z-index:-1"></div>
      <div style="position:absolute;inset:150px -140px 260px;z-index:0;opacity:.85;pointer-events:none">
        <svg viewBox="0 0 600 800" width="100%" height="100%" fill="none">
          <g stroke="rgba(255,255,255,0.22)" stroke-width="18" stroke-linecap="round">
            <path d="M-40 250 C 140 200, 240 320, 420 260 S 620 300, 700 240"/>
            <path d="M-40 470 C 160 430, 260 520, 430 470 S 640 520, 700 460"/>
            <path d="M120 -40 C 160 180, 90 340, 150 520 S 200 720, 170 840"/>
            <path d="M380 -40 C 350 160, 430 320, 380 520 S 330 720, 360 840"/>
          </g>
          <g stroke="rgba(255,255,255,0.12)" stroke-width="8" stroke-linecap="round">
            <path d="M-40 360 H 700"/><path d="M-40 600 H 700"/><path d="M260 -40 V 840"/><path d="M520 -40 V 840"/>
          </g>
        </svg>
      </div>
      <div class="row" style="gap:14px;margin-bottom:18px">
        <div class="card" style="flex:1;height:80px;display:flex;align-items:center;gap:14px;padding:0 22px;background:rgba(12,12,12,0.92)">
          <svg viewBox="0 0 24 24" width="22" height="22" fill="none" stroke="${t.textMuted}" stroke-width="2"><circle cx="11" cy="11" r="7"/><path d="m20 20-4-4"/></svg>
          <span style="font-size:19px;color:${t.textMuted}">Пошук АЗС…</span>
        </div>
        <span class="card" style="width:80px;height:80px;display:grid;place-items:center;background:${t.surfaceElevated};font-size:22px">◎</span>
      </div>

      <div style="position:relative;height:600px;margin:0 -26px">
        ${[
          { x: 26, y: 20, s: 'okko', n: 'OKKO', p: '55,00' },
          { x: 62, y: 34, s: 'wog', n: 'WOG', p: '56,40' },
          { x: 40, y: 56, s: 'upg', n: 'UPG', p: '54,80' },
          { x: 68, y: 68, s: 'klo', n: 'KLO', p: '56,00' },
        ]
          .map(
            (pin) => `<div style="position:absolute;left:${pin.x}%;top:${pin.y}%;transform:translate(-50%,-100%);
              display:flex;flex-direction:column;align-items:center;gap:6px">
              <span style="padding:6px 10px;border-radius:10px;background:${t.surfaceElevated};
                border:1px solid ${t.border};font-size:14px;font-weight:800;white-space:nowrap">${pin.p} ₴</span>
              <span style="width:52px;height:52px;border-radius:26px;background:${brand[pin.s]};color:#000;
                display:grid;place-items:center;font-weight:900;font-size:12px;letter-spacing:-0.02em;
                box-shadow:0 0 0 10px rgba(0,232,95,0.10)">${pin.n}</span>
            </div>`,
          )
          .join('')}
        <span style="position:absolute;left:48%;top:44%;width:20px;height:20px;border-radius:10px;
          background:${t.primary};box-shadow:0 0 0 12px rgba(0,232,95,0.18)"></span>
      </div>

      <div class="card" style="padding:22px;background:rgba(12,12,12,0.96)">
        <div class="row" style="justify-content:space-between;align-items:flex-start">
          <div class="row" style="gap:14px">
            <span style="width:52px;height:52px;border-radius:14px;background:${brand.okko};color:#000;display:grid;place-items:center;font-weight:900;font-size:15px">OK</span>
            <div>
              <div style="font-size:22px;font-weight:780">OKKO</div>
              <div style="font-size:15px;color:${t.textMuted};margin-top:4px">вул. Городоцька, 128а · 0,8 км</div>
            </div>
          </div>
          <span style="font-size:15px;font-weight:700;color:${t.primary}">від 55,00 ₴</span>
        </div>
        <div class="cta" style="height:64px;margin-top:20px;font-size:17px">ПРОКЛАСТИ МАРШРУТ</div>
      </div>
    `,
    active: 'map',
  }),
};

const chrome = findChrome();
if (!chrome) {
  console.error('[screens] no Chrome/Chromium found - set CHROME_PATH to render the gallery');
  process.exit(1);
}

const siteTmp = resolve(root, 'out/.screens');
await rm(siteTmp, { recursive: true, force: true });
await mkdir(siteTmp, { recursive: true });

const browser = await puppeteer.launch({
  executablePath: chrome,
  headless: 'new',
  args: ['--no-sandbox', '--disable-dev-shm-usage', '--allow-file-access-from-files'],
});

try {
  const page = await browser.newPage();
  await page.setViewport({ width: WIDTH, height: HEIGHT, deviceScaleFactor: SCALE });

  for (const [name, markup] of Object.entries(screens)) {
    const html = `<!doctype html><html lang="uk"><head><meta charset="utf-8">
      <style>${baseCss}</style></head><body>${markup}</body></html>`;
    await page.setContent(html, { waitUntil: 'load' });
    await page.evaluate(() => document.fonts.ready);
    await page.screenshot({
      path: resolve(root, `public/${name}.jpg`),
      type: 'jpeg',
      quality: 88,
    });
    console.log(`[screens] public/${name}.jpg`);
  }
} finally {
  await browser.close();
  await rm(siteTmp, { recursive: true, force: true });
}
