#!/usr/bin/env node
// Render the admin shell against the *built* CSS and screenshot it.
//
// Theming here is token-driven and mostly invisible in code review: a change to
// index.css can square every corner or flatten every bevel in the app, and the
// only way to know it looks right is to look at it. This exists so that check is
// one command instead of a hand-assembled HTML file that has to be rewritten
// every time a hashed asset name changes.
//
//   npm run build && npm run preview:theme -- --theme mercury
//
// Flags: --theme <id>  --out <file.png>  --width/--height/--scale <n>
//        --browser <path to chrome|edge>  --keep (leave the page in dist/)
//
// --forge is inferred from themes.ts, because the CSS keys the machined treatment
// off <html data-forge> rather than off data-theme: setting the flag on a
// non-forge theme would render it square and beveled, i.e. wrong.

import { execFileSync } from "node:child_process";
import { existsSync, readFileSync, readdirSync, rmSync, writeFileSync } from "node:fs";
import path from "node:path";
import { fileURLToPath } from "node:url";

const admin = path.resolve(path.dirname(fileURLToPath(import.meta.url)), "..");
const dist = path.join(admin, "dist");
const assets = path.join(dist, "assets");

const args = process.argv.slice(2);
const flag = (name, fallback) => {
    const i = args.indexOf(`--${name}`);
    return i === -1 ? fallback : args[i + 1];
};
const has = (name) => args.includes(`--${name}`);

const theme = flag("theme", "mercury");
const scale = Number(flag("scale", "2"));
const width = Number(flag("width", "1440"));
const height = Number(flag("height", "900"));
const out = flag("out", path.join(admin, `preview-${theme}.png`));

// Which themes carry the machined treatment, read from the catalogue so this
// script cannot drift from themes.ts.
function forgeThemes() {
    const src = readFileSync(path.join(admin, "src/lib/themes.ts"), "utf8");
    return new Set(
        [...src.matchAll(/'?([a-z-]+)'?:\s*\{\s*isDark:\s*(?:true|false),\s*forge:\s*true/g)].map((m) => m[1]),
    );
}

function builtAsset(pattern) {
    const hit = readdirSync(assets).find((f) => pattern.test(f));
    if (!hit) throw new Error(`no ${pattern} in ${assets} — run "npm run build" first`);
    return hit;
}

function findBrowser() {
    const explicit = flag("browser", process.env.CHROME_PATH);
    if (explicit) return explicit;
    const candidates = [
        "C:/Program Files/Google/Chrome/Application/chrome.exe",
        "C:/Program Files (x86)/Google/Chrome/Application/chrome.exe",
        "C:/Program Files (x86)/Microsoft/Edge/Application/msedge.exe",
        "C:/Program Files/Microsoft/Edge/Application/msedge.exe",
        "/Applications/Google Chrome.app/Contents/MacOS/Google Chrome",
        "/usr/bin/google-chrome",
        "/usr/bin/chromium",
    ];
    const hit = candidates.find((p) => existsSync(p));
    if (!hit) throw new Error("no Chrome/Edge found — pass --browser <path> or set CHROME_PATH");
    return hit;
}

const css = builtAsset(/^index-.*\.css$/);
const lion = builtAsset(/^lion-.*\.png$/);
const forge = forgeThemes().has(theme);

// Deliberately the real class names from the app, so the preview fails loudly if
// a theme override stops matching what components actually render.
const html = `<!doctype html>
<html lang="uk" data-theme="${theme}"${forge ? ' data-forge=""' : ""}>
<head><meta charset="utf-8" /><link rel="stylesheet" href="./assets/${css}" /></head>
<body>
<div class="flex h-screen w-screen overflow-hidden bg-background text-foreground relative">
  ${forge ? '<div class="aurora-bg"><div class="aurora-blob aurora-blob--green"></div></div>' : ""}
  <aside class="w-64 glass-chrome rounded-2xl flex flex-col h-full shrink-0 overflow-hidden relative z-10 my-3 ml-3">
    <div class="h-24 px-5 flex items-center border-b border-border">
        <div class="flex items-center gap-4 min-w-0">
        ${forge
          ? `<div class="shrink-0 bg-card border border-border p-1.5"><img src="./assets/${lion}" width="72" height="72" /></div>`
          : '<div class="w-2.5 h-10 bg-primary shadow-glow shrink-0"></div>'}
        <div class="min-w-0">
          <h1 class="text-xl font-bold tracking-tight leading-none text-foreground truncate">
            <span class="glass-text-gradient">FUEL FLOW</span>
          </h1>
          <div class="flex flex-col gap-1.5 mt-2">
            <p class="wordmark-sub leading-none">MANAGEMENT</p>
            <span class="h-[3px] w-12 bg-primary" aria-hidden="true"></span>
          </div>
        </div>
      </div>
    </div>
    <nav class="flex-1 p-4 space-y-1">
      <div class="px-2 mb-2"><span class="text-xs font-semibold text-muted-foreground uppercase tracking-wider">Управління</span></div>
      <button class="w-full flex items-center gap-3 px-3 py-2 text-sm font-medium rounded-lg bg-primary/15 text-primary glass-glow border border-primary/25"><span class="w-4 h-4"></span>Постачальники</button>
      <button class="w-full flex items-center gap-3 px-3 py-2 text-sm font-medium rounded-lg border border-transparent text-muted-foreground hover:bg-foreground/5 hover:text-foreground"><span class="w-4 h-4"></span>Закупівлі<span class="ml-auto min-w-[1.25rem] px-1.5 py-0.5 text-[10px] font-bold leading-none text-center rounded-full bg-destructive text-destructive-foreground">12</span></button>
      <button class="w-full flex items-center gap-3 px-3 py-2 text-sm font-medium rounded-lg border border-transparent text-muted-foreground hover:bg-foreground/5 hover:text-foreground"><span class="w-4 h-4"></span>Користувачі</button>
      <button class="w-full flex items-center gap-3 px-3 py-2 text-sm font-medium rounded-lg border border-transparent text-muted-foreground hover:bg-foreground/5 hover:text-foreground"><span class="w-4 h-4"></span>Журнал помилок</button>
    </nav>
    <div class="p-4 border-t border-border mt-auto">
      <div class="flex items-center gap-3 px-3 py-2">
        <div class="w-8 h-8 rounded-full bg-primary/20 flex items-center justify-center border border-border"><span class="text-xs font-bold text-foreground">AA</span></div>
        <div class="overflow-hidden flex-1"><p class="text-sm font-medium text-foreground truncate">Андрей Артем</p><p class="text-xs text-muted-foreground truncate">SuperAdmin</p></div>
      </div>
    </div>
  </aside>

  <div class="flex flex-1 flex-col overflow-hidden min-w-0 relative z-10 md:py-3 md:pr-3 md:pl-0">
    <header class="h-16 glass-chrome md:rounded-2xl flex items-center justify-between px-4 md:px-6 shrink-0">
      <span class="text-sm font-medium text-muted-foreground">/ Панель / providers</span>
      <div class="flex items-center gap-4"><span class="text-sm font-medium text-muted-foreground">Документація</span><span class="text-sm font-medium text-muted-foreground">Підтримка</span></div>
    </header>

    <main class="flex-1 overflow-y-auto p-4 md:p-6 relative z-10">
      <div class="flex flex-wrap justify-between items-center glass-panel p-4 gap-4 mb-4">
        <h2 class="text-lg">Постачальники</h2>
        <div class="flex items-center gap-2">
          <input class="glass-input rounded-lg px-3 py-2 text-sm text-foreground w-64" placeholder="Пошук…" />
          <button class="inline-flex items-center justify-center gap-2 min-h-9 px-4 py-2 rounded-lg text-sm font-medium bg-primary text-primary-foreground border border-primary/60">Додати</button>
          <button class="inline-flex items-center justify-center gap-2 min-h-9 px-4 py-2 rounded-lg text-sm font-medium border border-border bg-foreground/8 text-foreground">Експорт</button>
          <button class="inline-flex items-center justify-center gap-2 min-h-9 px-4 py-2 rounded-lg text-sm font-medium bg-destructive text-destructive-foreground">Очистити</button>
        </div>
      </div>

      <div class="flex items-start gap-2 mb-4 px-4 py-3 rounded-lg bg-destructive/10 border border-destructive/20 text-sm text-destructive"><span>Строк-лимит оновлення тарифів минув.</span></div>
      <div class="flex items-start gap-2 mb-4 px-4 py-3 rounded-lg bg-warning/10 border border-warning/20 text-sm text-warning"><span>Частина покупок не синхронізована.</span></div>

      <div class="glass-panel overflow-x-auto mb-4">
        <table class="w-full">
          <thead class="bg-muted text-muted-foreground uppercase text-xs backdrop-blur-sm">
            <tr><th class="px-4 py-3 text-left">Провайдер</th><th class="px-4 py-3 text-left">Паливо</th><th class="px-4 py-3 text-left">Ціна</th><th class="px-4 py-3 text-left">Маржа</th><th class="px-4 py-3 text-left">Статус</th></tr>
          </thead>
          <tbody>
            <tr><td class="px-4 py-3 text-foreground">OKKO</td><td class="px-4 py-3 text-muted-foreground">A-95</td><td class="px-4 py-3 text-foreground tabular-nums">54.20</td><td class="px-4 py-3 text-success tabular-nums">2.40</td><td class="px-4 py-3"><span class="px-2 py-1 rounded-md text-xs font-bold uppercase border backdrop-blur-md bg-success/10 text-success border-success/20">Доступний</span></td></tr>
            <tr><td class="px-4 py-3 text-foreground">KLO</td><td class="px-4 py-3 text-muted-foreground">Дизель</td><td class="px-4 py-3 text-foreground tabular-nums">58.90</td><td class="px-4 py-3 text-warning tabular-nums">0.10</td><td class="px-4 py-3"><span class="px-2 py-1 rounded-md text-xs font-bold uppercase border backdrop-blur-md bg-warning/10 text-warning border-warning/20">Дефіцит</span></td></tr>
            <tr><td class="px-4 py-3 text-foreground">Shell</td><td class="px-4 py-3 text-muted-foreground">A-98</td><td class="px-4 py-3 text-foreground tabular-nums">61.00</td><td class="px-4 py-3 text-destructive tabular-nums">-0.80</td><td class="px-4 py-3"><span class="px-2 py-1 rounded-md text-xs font-bold uppercase border backdrop-blur-md bg-destructive/10 text-destructive border-destructive/20">Відключено</span></td></tr>
          </tbody>
        </table>
      </div>

      <div class="bg-card border border-border rounded-xl p-4 flex items-center justify-between">
        <div><p class="text-sm font-medium text-foreground">Автоматичний рефанд</p><p class="text-xs text-muted-foreground mt-1">Повертати кошти при скасуванні</p></div>
        <button class="relative w-11 h-6 rounded-full transition-colors shrink-0 bg-primary"><span class="absolute top-0.5 left-0.5 w-5 h-5 rounded-full shadow-sm transition-transform translate-x-5 bg-primary-foreground"></span></button>
      </div>
    </main>
  </div>
</div>
</body></html>`;

const page = path.join(dist, "__theme-preview.html");
writeFileSync(page, html, "utf8");

const url = `file:///${page.replace(/\\/g, "/")}`;
execFileSync(
    findBrowser(),
    [
        "--headless=new",
        "--disable-gpu",
        "--hide-scrollbars",
        `--force-device-scale-factor=${scale}`,
        `--window-size=${width},${height}`,
        `--screenshot=${out}`,
        url,
    ],
    { stdio: "ignore" },
);

if (!has("keep")) rmSync(page, { force: true });
console.log(`${out}  theme=${theme}${forge ? " (forge)" : ""}`);