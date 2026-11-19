/**
 * The six screens the marketing gallery paints, and the CSS that dresses them.
 *
 * Split from `generate-app-screens.mjs` so the file that decides *what a screen
 * says* is not the file that decides *which browser renders it* — and so neither
 * crosses the repository's 500-line review target.
 *
 * Everything here mirrors the app: the `obsidian` theme's colours, the
 * `GridBackground` grid, Rajdhani digits with Ukrainian in the system face, and
 * the copy the screens actually carry. It is presentation only — no file is
 * read, nothing is written, and the caller supplies the fonts and the demo QR.
 */

/** mobile/src/core/design/themes.ts → themes.obsidian. */
const c = {
  background: '#020203',
  surface: '#0D0D10',
  surfaceElevated: '#17171C',
  text: '#F8FAFC',
  textSecondary: 'rgba(248,250,252,0.72)',
  textMuted: 'rgba(248,250,252,0.55)',
  textDim: 'rgba(248,250,252,0.35)',
  border: 'rgba(255,255,255,0.12)',
  borderSubtle: 'rgba(255,255,255,0.07)',
  primary: '#8B5CF6',
  primarySubtle: 'rgba(139,92,246,0.16)',
  primaryBorder: 'rgba(139,92,246,0.48)',
  onPrimary: '#FFFFFF',
  success: '#34D399',
  warning: '#FBBF24',
  danger: '#FF5D5D',
};

/** mobile/src/core/design/palette.ts → BRAND_COLORS. */
const brand = { okko: '#16FF00', wog: '#008B45', upg: '#00C853', klo: '#FFCE00' };

/** Device geometry the site's manifest expects (AppShowcase.tsx width/height). */
const WIDTH = 588;
const HEIGHT = 1280;

const icons = {
  home: '<path d="M3 10.5 12 3.2l9 7.3V21h-6.2v-6.1H9.2V21H3z"/>',
  pin: '<path d="M12 21s6.6-6.1 6.6-10.4A6.6 6.6 0 1 0 5.4 10.6C5.4 14.9 12 21 12 21z"/><circle cx="12" cy="10.2" r="2.4" fill="#020203"/>',
  cart: '<path d="M3.5 4.5h2l2.3 10a2 2 0 0 0 2 1.6h7a2 2 0 0 0 1.9-1.5L20.5 7.6H6.6"/><circle cx="10" cy="19.6" r="1.4"/><circle cx="17.6" cy="19.6" r="1.4"/>',
  codes:
    '<rect x="3.5" y="3.5" width="7" height="7" rx="1.4"/><rect x="13.5" y="3.5" width="7" height="7" rx="1.4"/><rect x="3.5" y="13.5" width="7" height="7" rx="1.4"/><rect x="14.5" y="14.5" width="2.6" height="2.6"/><rect x="18.4" y="18.4" width="2.1" height="2.1"/>',
  chat: '<path d="M4 5.5h16v10.2H9.4L4 20z"/>',
  user: '<circle cx="12" cy="8" r="3.5"/><path d="M4.8 20.5c1.1-3.7 3.8-5.6 7.2-5.6s6.1 1.9 7.2 5.6"/>',
};


const tabBar = (active) => `
  <nav class="tabs">
    ${['home', 'pin', 'cart', 'codes', 'chat', 'user']
      .map(
        (key) => `<span class="tab ${key === active ? 'on' : ''}">
          <svg viewBox="0 0 24 24" fill="currentColor">${icons[key]}</svg>
          ${key === 'cart' ? '' : ''}
        </span>`,
      )
      .join('')}
  </nav>`;

const shell = ({ status = '12:42', body, active = 'home' }) => `
<section class="screen">
  <header class="statusbar">
    <span class="clock">${status}</span>
    <span class="sb-icons">
      <svg viewBox="0 0 18 12" width="19"><path d="M1 9h2v3H1zm4-3h2v6H5zm4-3h2v9H9zm4-4h2v13h-2z" fill="#fff"/></svg>
      <svg viewBox="0 0 16 12" width="17"><path d="M8 11.2 1 4.2A10 10 0 0 1 15 4.2z" fill="#fff"/></svg>
      <span class="battery"><i></i></span>
    </span>
  </header>
  <div class="body">${body}</div>
  ${tabBar(active)}
</section>`;

const G = (v) => `<span class="hryvnia">${v}</span>`;

const baseCss = (fonts) => `
  @font-face { font-family: 'Rajdhani'; font-weight: 700; src: url('${fonts.rajdhani700}') format('truetype'); font-display: block; }
  @font-face { font-family: 'Rajdhani'; font-weight: 600; src: url('${fonts.rajdhani600}') format('truetype'); font-display: block; }
  * { box-sizing: border-box; margin: 0; padding: 0; }
  html, body { width: ${WIDTH}px; height: ${HEIGHT}px; background: ${c.background}; }
  body {
    font-family: -apple-system, 'Segoe UI', Inter, system-ui, sans-serif;
    color: ${c.text};
    -webkit-font-smoothing: antialiased;
  }
  .screen {
    position: relative; width: ${WIDTH}px; height: ${HEIGHT}px;
    display: flex; flex-direction: column; overflow: hidden;
    background:
      linear-gradient(to right, rgba(139,92,246,0.05) 1px, transparent 1px) 0 0 / 40px 40px,
      linear-gradient(to bottom, rgba(139,92,246,0.05) 1px, transparent 1px) 0 0 / 40px 40px,
      radial-gradient(120% 45% at 50% -6%, rgba(139,92,246,0.10), transparent 62%),
      radial-gradient(110% 40% at 50% 104%, rgba(139,92,246,0.12), transparent 60%),
      ${c.background};
  }
  .num { font-family: 'Rajdhani', sans-serif; font-weight: 700; font-variant-numeric: tabular-nums; letter-spacing: -0.01em; }
  .statusbar { display: flex; align-items: center; justify-content: space-between; padding: 24px 32px 6px; }
  .clock { font-family: 'Rajdhani', sans-serif; font-weight: 700; font-size: 26px; letter-spacing: 0.01em; }
  .sb-icons { display: flex; align-items: center; gap: 8px; }
  .battery { position: relative; width: 27px; height: 13px; border: 1.5px solid rgba(255,255,255,0.6); border-radius: 4px; display: inline-block; }
  .battery::after { content: ''; position: absolute; right: -4px; top: 3.5px; width: 2.5px; height: 5px; border-radius: 0 2px 2px 0; background: rgba(255,255,255,0.6); }
  .battery i { position: absolute; inset: 2px 7px 2px 2px; background: #fff; border-radius: 2px; }
  .body { flex: 1; padding: 16px 26px 0; display: flex; flex-direction: column; min-height: 0; }
  .tabs {
    height: 100px; display: flex; align-items: center; justify-content: space-around;
    border-top: 1px solid ${c.borderSubtle}; background: rgba(2,2,3,0.86); padding-bottom: 10px;
  }
  .tab { width: 44px; height: 44px; color: ${c.textMuted}; display: grid; place-items: center; }
  .tab svg { width: 27px; height: 27px; }
  .tab.on { color: ${c.primary}; }
  .screen-title { font-size: 40px; font-weight: 800; letter-spacing: -0.01em; }
  .label { font-size: 13px; font-weight: 700; letter-spacing: 0.14em; color: ${c.textMuted}; text-transform: uppercase; }
  .hryvnia { color: ${c.primary}; font-family: -apple-system, 'Segoe UI', Inter, system-ui, sans-serif; font-weight: 700; }
  .card { background: ${c.surface}; border: 1px solid ${c.borderSubtle}; border-radius: 26px; }
  .cta { height: 86px; border-radius: 22px; background: ${c.primary}; color: ${c.onPrimary}; display: grid; place-items: center;
         font-size: 20px; font-weight: 800; letter-spacing: 0.02em; }
  .ghost { height: 92px; border-radius: 6px; border: 1px solid ${c.border}; display: grid; place-items: center;
           font-size: 19px; font-weight: 800; letter-spacing: 0.06em; background: transparent; color: ${c.text}; }
`;

const screens = {
  'app-networks': ({ qrInline }) => shell({
    body: `
      <h1 class="screen-title" style="margin: 26px 0 34px; font-size: 46px">
        ОБЕРІТЬ<br><span style="color:${c.primary}">МЕРЕЖУ</span>
      </h1>
      ${[
        { slug: 'okko', name: 'OKKO', bolt: c.success },
        { slug: 'wog', name: 'WOG', bolt: c.success },
        { slug: 'upg', name: 'UPG', bolt: c.success },
        { slug: 'klo', name: 'KLO', bolt: c.warning },
      ]
        .map(
          (n) => `
        <div class="card" style="position:relative;display:flex;align-items:center;justify-content:space-between;
          padding: 34px 30px; margin-bottom: 26px; overflow:hidden">
          <span style="position:absolute;left:0;top:14px;bottom:14px;width:5px;border-radius:0 4px 4px 0;background:${brand[n.slug]}"></span>
          <div>
            <div style="display:flex;align-items:center;gap:10px">
              <span class="num" style="font-size:38px;letter-spacing:0.01em">${n.name}</span>
              <svg viewBox="0 0 24 24" width="20" height="20" fill="${n.bolt}"><path d="M13 2 4 14h6l-1 8 9-12h-6z"/></svg>
            </div>
            <div style="display:flex;align-items:center;gap:8px;margin-top:8px">
              <span style="width:7px;height:7px;border-radius:4px;background:${n.bolt}"></span>
              <span class="label" style="letter-spacing:0.12em">ONLINE · READY</span>
            </div>
          </div>
          <span style="width:66px;height:66px;border-radius:18px;border:1px solid ${c.primaryBorder};
            background:${c.primarySubtle};display:grid;place-items:center">
            <svg viewBox="0 0 24 24" width="26" height="26" fill="none" stroke="${c.primary}" stroke-width="2.2" stroke-linecap="round">
              <path d="M4 12h15"/><path d="m13 6 6 6-6 6"/>
            </svg>
          </span>
        </div>`,
        )
        .join('')}
    `,
  }),

  'app-fuels': ({ qrInline }) => shell({
    body: `
      <div style="display:flex;align-items:center;gap:18px;margin:22px 0 30px">
        <span style="font-size:30px;color:${c.textMuted}">‹</span>
        <span class="num" style="font-size:34px">OKKO</span>
      </div>
      ${[
        { name: 'ДП', price: '103,90' },
        { name: 'пульс ДП', price: '106,90' },
        { name: 'А-95', price: '90,90' },
        { name: 'ПРОПАН', price: '64,90', out: true },
        { name: 'пульс 95', price: '93,90' },
      ]
        .map(
          (f) => `
        <div class="card" style="display:flex;align-items:center;justify-content:space-between;padding:26px 26px;margin-bottom:22px">
          <div style="display:flex;align-items:center;gap:20px">
            <span style="width:62px;height:62px;border-radius:16px;background:${c.surfaceElevated};display:grid;place-items:center">
              <svg viewBox="0 0 24 24" width="26" height="26" fill="none" stroke="${c.textMuted}" stroke-width="1.8">
                <path d="M7 3h7v17H7z"/><path d="M14 8h3l3 4v6a2 2 0 0 1-4 0"/>
              </svg>
            </span>
            <div>
              <div class="label" style="letter-spacing:0.1em">${f.name}</div>
              <div class="num" style="font-size:34px;margin-top:2px">${f.price}${G('₴')}</div>
            </div>
          </div>
          ${
            f.out
              ? `<span style="padding:11px 16px;border-radius:999px;background:rgba(255,255,255,0.05);
                  border:1px solid ${c.borderSubtle};font-size:12px;font-weight:700;letter-spacing:0.08em;color:${c.textMuted}">
                  НЕМАЄ НА СКЛАДІ
                </span>`
              : `<span style="width:58px;height:58px;border-radius:16px;border:1px solid ${c.primaryBorder};
                  background:${c.primarySubtle};display:grid;place-items:center">
                  <svg viewBox="0 0 24 24" width="24" height="24" fill="none" stroke="${c.primary}" stroke-width="2.2" stroke-linecap="round">
                    <path d="m9 5 7 7-7 7"/>
                  </svg>
                </span>`
          }
        </div>`,
        )
        .join('')}
    `,
  }),

  'app-packages': ({ qrInline }) => shell({
    body: `
      <div style="display:flex;align-items:center;justify-content:space-between;margin:20px 0 26px">
        <div style="display:flex;align-items:center;gap:18px">
          <span style="font-size:30px;color:${c.textMuted}">‹</span>
          <div>
            <div class="screen-title" style="font-size:38px">ДП</div>
            <div class="label" style="margin-top:4px">ОБЕРІТЬ ТАЛОНИ</div>
          </div>
        </div>
        <span style="width:74px;height:74px;border-radius:20px;border:1px solid ${c.border};display:grid;place-items:center">
          <svg viewBox="0 0 24 24" width="28" height="28" fill="none" stroke="${c.text}" stroke-width="1.9">
            <path d="M3.5 4.5h2l2.3 10a2 2 0 0 0 2 1.6h7a2 2 0 0 0 1.9-1.5L20.5 7.6H6.6"/>
            <circle cx="10" cy="19.6" r="1.3"/><circle cx="17.6" cy="19.6" r="1.3"/>
          </svg>
        </span>
      </div>
      ${[
        { l: '10', price: '1039' },
        { l: '20', price: '2078' },
      ]
        .map(
          (p) => `
        <div class="card" style="padding:24px;margin-bottom:20px">
          <div style="display:flex;align-items:center;gap:24px">
            <span style="width:104px;height:104px;border-radius:22px;border:1px solid ${c.primaryBorder};
              background:${c.primarySubtle};display:flex;flex-direction:column;align-items:center;justify-content:center">
              <span class="num" style="font-size:40px;line-height:1">${p.l}</span>
              <span style="font-size:11px;font-weight:700;letter-spacing:0.1em;color:${c.textSecondary};margin-top:2px">ЛІТРІВ</span>
            </span>
            <span class="num" style="font-size:40px">${p.price}${G('₴')}</span>
          </div>
          <div style="border-top:1px solid ${c.borderSubtle};margin-top:22px;padding-top:16px;text-align:right">
            <span class="label">РАЗОМ</span>
            <div class="num" style="font-size:44px;margin-top:2px">${p.price}${G('₴')}</div>
          </div>
          <div class="cta" style="margin-top:18px">
            <span style="display:flex;align-items:center;gap:12px">
              <svg viewBox="0 0 24 24" width="23" height="23" fill="none" stroke="${c.onPrimary}" stroke-width="2" stroke-linecap="round">
                <path d="M3.5 4.5h2l2.3 10a2 2 0 0 0 2 1.6h7a2 2 0 0 0 1.9-1.5L20.5 7.6H6.6"/>
              </svg>
              ДОДАТИ В КОШИК
            </span>
          </div>
        </div>`,
        )
        .join('')}
    `,
    active: 'cart',
  }),

  'app-wallet': ({ qrInline }) => shell({
    body: `
      <h1 class="screen-title" style="margin: 20px 0 24px">МОЇ ТАЛОНИ</h1>
      <div style="display:flex;gap:14px;margin-bottom:26px">
        ${[
          { v: '3', k: 'ЗАМОВЛЕННЯ', color: c.text },
          { v: '3', k: 'ВИКОНАНО', color: c.primary },
          { v: '0', k: 'В ОБРОБЦІ', color: c.warning },
        ]
          .map(
            (s) => `<div class="card" style="flex:1;padding:20px 10px;text-align:center">
              <div class="num" style="font-size:34px;color:${s.color}">${s.v}</div>
              <div class="label" style="font-size:11px;margin-top:6px">${s.k}</div>
            </div>`,
          )
          .join('')}
      </div>
      <div class="card" style="padding:24px;background:linear-gradient(180deg, rgba(139,92,246,0.07), ${c.surface} 45%)">
        <div style="display:flex;justify-content:space-between;align-items:flex-start">
          <div>
            <div class="label" style="letter-spacing:0.12em;color:${c.textSecondary}">OKKO</div>
            <div class="screen-title" style="font-size:40px;margin-top:6px">A-95</div>
            <div class="num" style="font-size:26px;color:${c.textSecondary};margin-top:6px">10 <span style="font-size:18px">л</span></div>
            <div style="font-size:14px;color:${c.textDim};margin-top:14px">Термін: 07.11.2026</div>
          </div>
          <span style="padding:9px 16px;border-radius:999px;background:rgba(255,255,255,0.06);
            border:1px solid ${c.borderSubtle};font-size:12px;font-weight:700;letter-spacing:0.1em;color:${c.textMuted}">
            ● REDEEMED
          </span>
        </div>
        <div style="width:270px;margin:20px auto 0;padding:14px;background:rgba(255,255,255,0.9);border-radius:4px">
          <img src="${qrInline}" style="width:100%;display:block" alt="">
        </div>
        <div class="ghost" style="margin-top:22px;height:80px;font-size:18px">ВІДНОВИТИ ТАЛОН</div>
        <div class="ghost" style="margin-top:16px;height:80px;color:${c.textSecondary}">
          <span style="display:flex;align-items:center;gap:12px">
            <svg viewBox="0 0 24 24" width="21" height="21" fill="none" stroke="${c.textMuted}" stroke-width="1.8">
              <circle cx="12" cy="12" r="9"/><path d="M12 7.5v5l3.4 2"/>
            </svg>
            ІСТОРІЯ
            <span style="color:${c.textDim};font-size:16px">›</span>
          </span>
        </div>
        <div style="margin-top:18px;font-size:14px;color:${c.textDim}">ID: 99999600000024588842</div>
      </div>
    `,
    active: 'codes',
  }),

  'app-map': ({ qrInline }) => shell({
    status: '12:43',
    body: `
      <div style="position:absolute;inset:104px 0 330px;z-index:0">
        <svg viewBox="0 0 600 700" width="100%" height="100%" fill="none">
          <g stroke="rgba(255,255,255,0.16)" stroke-width="9" stroke-linecap="round">
            <path d="M-30 190 C 150 150, 250 240, 420 200 S 640 230, 700 190"/>
            <path d="M-30 420 C 160 390, 270 470, 430 430 S 640 470, 700 420"/>
            <path d="M150 -30 C 190 150, 120 330, 180 520 S 220 680, 200 760"/>
            <path d="M400 -30 C 370 160, 450 320, 400 530 S 350 690, 380 760"/>
          </g>
          <g stroke="rgba(255,255,255,0.07)" stroke-width="5">
            <path d="M-30 300 H 700"/><path d="M-30 560 H 700"/><path d="M280 -30 V 760"/><path d="M540 -30 V 760"/>
          </g>
        </svg>
      </div>
      <div style="position:relative;z-index:1">
        <h1 class="screen-title" style="font-size:40px">Карта мережі</h1>
        <div style="font-size:15px;color:${c.textDim};margin-top:6px">1322 АЗК</div>
        <div style="margin-top:22px;border-radius:22px;background:rgba(20,20,26,0.94);border:1px solid ${c.borderSubtle};
          padding:22px 24px;display:flex;align-items:center;gap:16px">
          <svg viewBox="0 0 24 24" width="24" height="24" fill="none" stroke="${c.textMuted}" stroke-width="2">
            <circle cx="11" cy="11" r="7"/><path d="m20 20-4-4"/>
          </svg>
          <span style="font-size:21px;color:${c.textMuted}">Пошук АЗС…</span>
        </div>
        <div style="display:flex;gap:10px;margin-top:14px;overflow:hidden">
          ${['А92', 'А95', 'А95 преміум', '100', '100 преміум', 'ДП']
            .map(
              (f, i) => `<span style="padding:9px 15px;border-radius:12px;white-space:nowrap;font-size:16px;font-weight:600;
                color:${i === 1 ? c.text : c.textMuted};background:rgba(20,20,26,0.9);border:1px solid ${c.borderSubtle}">${f}</span>`,
            )
            .join('')}
        </div>
      </div>

      <div style="position:relative;flex:1;min-height:220px">
        ${[
          { x: 24, y: 22, s: 'okko', n: 'OKKO' },
          { x: 62, y: 30, s: 'wog', n: 'WOG' },
          { x: 48, y: 52, s: 'okko', n: 'OKKO' },
          { x: 22, y: 74, s: 'upg', n: 'UPG' },
          { x: 68, y: 78, s: 'wog', n: 'WOG' },
        ]
          .map(
            (p) => `<div style="position:absolute;left:${p.x}%;top:${p.y}%;transform:translate(-50%,-100%)">
              <span style="width:60px;height:60px;border-radius:30px;background:#fff;color:#000;display:grid;place-items:center;
                font-weight:900;font-size:12px;box-shadow:0 0 0 6px rgba(139,92,246,0.16)">${p.n}</span>
            </div>`,
          )
          .join('')}
        <span style="position:absolute;left:46%;top:60%;width:26px;height:26px;border-radius:14px;background:${c.primary};
          box-shadow:0 0 0 8px rgba(139,92,246,0.25)"></span>
      </div>

      <div style="position:relative;z-index:2;background:rgba(16,16,22,0.97);border:1px solid ${c.borderSubtle};
        border-radius:26px;padding:26px;margin-bottom:20px">
        <div style="display:flex;align-items:center;gap:16px">
          <span style="width:56px;height:56px;border-radius:28px;background:#fff;display:grid;place-items:center;
            font-weight:900;font-size:13px;color:#000">UPG</span>
          <div style="flex:1">
            <div style="font-size:22px;font-weight:800">UPG №142</div>
            <div style="font-size:15px;color:${c.textMuted};margin-top:5px">Сокільники, вул. Зубрівська, 8</div>
          </div>
          <span style="font-size:22px;color:${c.textDim}">×</span>
        </div>
        <div style="margin-top:22px;border-radius:18px;background:${c.background};border:1px solid ${c.borderSubtle};padding:20px 22px">
          <div class="label" style="letter-spacing:0.12em">ЦІНА ВАУЧЕРА · А95</div>
          <div style="display:flex;align-items:baseline;gap:12px;margin-top:6px">
            <span class="num" style="font-size:52px;color:${c.primary}">86,90</span>
            <span class="num" style="font-size:30px;color:${c.primary}">₴</span>
            <span style="font-size:19px;color:${c.textMuted}">/ літр</span>
          </div>
        </div>
        <div style="display:flex;justify-content:space-between;align-items:center;margin-top:16px">
          <span style="font-size:15px;color:${c.textDim}">Від вас · 2,4 км</span>
        </div>
        <div class="cta" style="margin-top:16px">
          <span style="display:flex;align-items:center;gap:14px">
            <svg viewBox="0 0 24 24" width="24" height="24" fill="${c.onPrimary}"><path d="M21 3 3 10.6l7.4 2.6L13 21z"/></svg>
            ПРОКЛАСТИ МАРШРУТ
          </span>
        </div>
      </div>
    `,
    active: 'pin',
  }),

  'app-economy': ({ qrInline }) => shell({
    status: '12:44',
    body: `
      <div style="display:flex;align-items:center;gap:16px;margin:20px 0 22px">
        <span style="font-size:30px;color:${c.textMuted}">‹</span>
        <h1 class="screen-title" style="font-size:38px">МОЯ ЕКОНОМІЯ</h1>
      </div>
      <div style="display:flex;flex-wrap:wrap;gap:12px;margin-bottom:26px">
        ${['ЗА ВЕСЬ ЧАС', 'РІК', '3 МІСЯЦІ', 'МІСЯЦЬ']
          .map(
            (f, i) => `<span style="padding:14px 22px;border-radius:16px;font-size:15px;font-weight:700;letter-spacing:0.06em;
              ${i === 0
                ? `background:${c.primarySubtle};border:1px solid ${c.primaryBorder};color:${c.primary}`
                : `border:1px solid ${c.border};color:${c.textSecondary}`}">${f}</span>`,
          )
          .join('')}
      </div>
      <div style="display:grid;grid-template-columns:1fr 1fr;gap:16px">
        ${[
          { icon: '<rect x="2.5" y="6" width="19" height="12" rx="3"/><circle cx="12" cy="12" r="3"/>', v: '5172', u: '₴', k: 'СПЛАЧЕНО', color: c.primary },
          { icon: '<path d="M4 17 10 11l4 4 6-7"/><path d="M20 8h-5m5 0v5"/>', v: '402', u: '₴', k: 'ЗАОЩАДЖЕНО VS КОЛОНКА', color: c.success },
          { icon: '<path d="M7 3h7v17H7z"/><path d="M14 8h3l3 4v6a2 2 0 0 1-4 0"/>', v: '0', u: 'л', k: 'ЗАЛИШОК ЛІТРІВ', color: c.textSecondary },
          { icon: '<rect x="3" y="4" width="18" height="16" rx="3"/><path d="M7 9h10M7 14h6"/>', v: '0', u: '', k: 'ЗАЛИШОК КОДІВ', color: c.textSecondary },
        ]
          .map(
            (t) => `<div class="card" style="padding:24px 18px;text-align:center">
              <svg viewBox="0 0 24 24" width="26" height="26" fill="none" stroke="${t.color}" stroke-width="1.8" stroke-linecap="round">${t.icon}</svg>
              <div class="num" style="font-size:40px;margin-top:8px">${t.v}<span style="font-size:22px">${t.u}</span></div>
              <div class="label" style="font-size:11px;margin-top:8px;line-height:1.5">${t.k}</div>
            </div>`,
          )
          .join('')}
      </div>
      <div class="card" style="display:flex;align-items:center;justify-content:space-between;padding:26px;margin-top:18px">
        <span class="label" style="letter-spacing:0.14em">ПРИДБАНО ЛІТРІВ</span>
        <span class="num" style="font-size:32px">60<span style="font-size:20px">л</span></span>
      </div>
      <div style="margin-top:18px;border-radius:20px;border:1px solid ${c.borderSubtle};background:${c.surface};padding:24px">
        <div class="label" style="color:${c.primary};letter-spacing:0.2em">ЕКОНОМІЯ ПО МІСЯЦЯХ</div>
        ${[
          { m: '2026-10', plus: '+268 ₴', spent: '3448 ₴', l: '40л' },
          { m: '2026-11', plus: '+134 ₴', spent: '1724 ₴', l: '20л' },
        ]
          .map(
            (r) => `<div style="display:flex;align-items:center;justify-content:space-between;margin-top:20px">
              <span class="num" style="font-size:22px">${r.m}</span>
              <span style="display:flex;align-items:center;gap:16px">
                <span class="num" style="font-size:24px;color:${c.success}">${r.plus}</span>
                <span class="num" style="font-size:20px;color:${c.textMuted}">${r.spent}</span>
                <span class="num" style="font-size:20px;color:${c.primary}">${r.l}</span>
              </span>
            </div>`,
          )
          .join('')}
      </div>
      <p style="margin-top:22px;font-size:14px;line-height:1.6;color:${c.textDim}">
        Заощадження рахуються відносно ціни на колонці на момент покупки
      </p>
    `,
    active: 'codes',
  }),
};


/**
 * Builds one screen's complete HTML document.
 *
 * `fonts` carries the two Rajdhani subsets as data URLs and `qrInline` the demo
 * voucher code; both are loaded by the caller, so this module reads no file.
 */
export function renderScreen(name, { fonts, qrInline }) {
  const build = screens[name];
  if (!build) throw new Error(`unknown screen: ${name}`);
  return `<!doctype html><html lang="uk"><head><meta charset="utf-8">
    <style>${baseCss(fonts)}</style></head><body>${build({ qrInline })}</body></html>`;
}

export const SCREEN_NAMES = Object.keys(screens);
