#!/usr/bin/env node
// Builds a pump-price (колонка) import file for the `okko` brand from OKKO's public fuel page.
//
// Source: https://www.okko.ua/fuels — a Nuxt page that server-renders its whole state into
// `window.__NUXT__`. The prices live in the `Global_BulletsFuel` component's `bullets.items`, one
// entry per fuel, each `{title, price, fuel_code, type}`. That state is the only place the price
// appears: there is no public JSON endpoint behind the page, and no prices in the rendered markup
// outside the state blob.
//
// The site publishes `fuel_code` beside each price ("A-95", "Pulls Diesel", "SPBT"); that code is
// non-localized, whereas `title` is translated per request. We emit the code, and the backend maps
// it onto the catalog with OkkoFuelClassifier.CategoryFromSiteFuelCode.
//
// Output is the CSV shape the admin importer accepts:
//
//   fuelCode, pricePerLiter
//
// (POST /api/admin/providers/okko/pump-prices/import — see OkkoPriceSheetParser.)
//
// Usage:
//   node scripts/fetch-okko-prices.mjs                        # fetch live, write ./okko-prices.csv
//   node scripts/fetch-okko-prices.mjs --out path.csv
//   node scripts/fetch-okko-prices.mjs --from-file page.html  # offline, re-parse a saved page
//
// Note: `Pulls 100` (aviation jet fuel) and `AdBlue` (an additive, not a fuel grade) are scraped
// but have no counterpart in our catalog — the backend reports them as unmatched rather than
// guessing a fuel, so they are listed here for visibility and left in the file.

import { readFileSync } from "node:fs";
import { collapse, writePriceCsv } from "./lib/price-csv.mjs";

const DEFAULT_SOURCE = "https://www.okko.ua/fuels";
const DEFAULT_OUT = "okko-prices.csv";

function parseArgs(argv) {
  const args = { source: DEFAULT_SOURCE, out: DEFAULT_OUT, fromFile: null };
  for (let i = 0; i < argv.length; i++) {
    const arg = argv[i];
    if (arg === "--out") args.out = argv[++i];
    else if (arg === "--source") args.source = argv[++i];
    else if (arg === "--from-file") args.fromFile = argv[++i];
    else if (arg === "--help" || arg === "-h") args.help = true;
    else throw new Error(`Unknown argument: ${arg}`);
  }
  return args;
}

/**
 * Reads `window.__NUXT__` out of the page and returns it as a plain object.
 *
 * Nuxt emits the state as an IIFE over hoisted parameters — `(function(a,b,c){…return {…}}(v1,v2,…))` —
 * to keep the payload small, so it is not JSON and cannot be `JSON.parse`d. Evaluating it is safe
 * here because we fetched the page ourselves; it is the only way to read the shape.
 */
function extractNuxtState(html) {
  const marker = "window.__NUXT__=";
  const start = html.indexOf(marker);
  if (start < 0) throw new Error(`Marker '${marker}' not found — the page layout changed`);

  const tail = html.slice(start + marker.length);
  const end = tail.indexOf("</script>");
  const expr = tail.slice(0, end < 0 ? undefined : end).replace(/;\s*$/, "");

  // eslint-disable-next-line no-new-func
  const state = new Function(`return (${expr})`)();
  if (!state || typeof state !== "object") throw new Error("__NUXT__ did not evaluate to an object");
  return state;
}

/**
 * Finds the fuel price rows without hard-coding the component path.
 *
 * The payload nests page components under `data[].data[]`, each tagged with a `componentName`, and
 * the price list is the one whose `bullets.items` carry a `fuel_code`. Walking for that shape means a
 * Nuxt upgrade that renames or reorders the tree does not break the scrape — only a change that moves
 * prices out of the SSR state would, and that fails loudly with no rows.
 */
function findFuelRows(node, found = []) {
  if (node === null || typeof node !== "object") return found;

  if (Array.isArray(node)) {
    for (const item of node) findFuelRows(item, found);
    return found;
  }

  const items = node.bullets?.items;
  if (Array.isArray(items)) {
    for (const item of items) {
      if (item && typeof item === "object" && "fuel_code" in item && "price" in item) found.push(item);
    }
  }

  for (const value of Object.values(node)) findFuelRows(value, found);
  return found;
}

async function loadPage({ source, fromFile }) {
  if (fromFile) return readFileSync(fromFile, "utf8");
  const response = await fetch(source, {
    headers: {
      "User-Agent": "Mozilla/5.0 (compatible; FuelFlow price import)",
      "Accept-Language": "uk,en;q=0.8",
    },
  });
  if (!response.ok) throw new Error(`GET ${source} -> HTTP ${response.status}`);
  return response.text();
}

async function main() {
  const args = parseArgs(process.argv.slice(2));
  if (args.help) {
    console.error(
      "Usage: node scripts/fetch-okko-prices.mjs [--out <file>] [--source <url>] [--from-file <html>]",
    );
    return;
  }

  const raw = findFuelRows(extractNuxtState(await loadPage(args)));
  if (raw.length === 0) {
    throw new Error("no fuel price rows in __NUXT__ — the page layout changed");
  }

  const skipped = [];
  const seen = new Set();
  const rows = [];

  for (const item of raw) {
    const code = collapse(item.fuel_code);
    // Prices are published as strings ("92.90"); a dot is the only separator, never a comma.
    const price = Number.parseFloat(collapse(item.price));

    if (!code) skipped.push({ code: "<none>", reason: "no fuel_code" });
    else if (seen.has(code)) skipped.push({ code, reason: "duplicate fuel_code" });
    else if (!Number.isFinite(price)) skipped.push({ code, reason: `unparseable price "${item.price}"` });
    else if (price <= 0) skipped.push({ code, reason: `non-positive price ${price}` });
    else {
      seen.add(code);
      rows.push([code, price.toFixed(2)]);
    }
  }

  if (rows.length === 0) {
    throw new Error(`every scraped row was unusable: ${skipped.map((s) => `${s.code} (${s.reason})`).join(", ")}`);
  }

  writePriceCsv(rows, args.out);

  console.error(`wrote ${args.out}: ${rows.length} prices from ${raw.length} published rows`);
  console.error("  " + rows.map(([code, price]) => `${code}=${price}`).join("  "));
  for (const { code, reason } of skipped) console.error(`skipped ${code}: ${reason}`);
  console.error(
    "preview with: POST /api/admin/providers/okko/pump-prices/import?dryRun=true (file above)",
  );
}

try {
  await main();
} catch (error) {
  // OKKO's page is third-party; a layout change should read as one clear line, not a stack trace
  // that buries the reason.
  console.error(`fetch-okko-prices: ${error instanceof Error ? error.message : error}`);
  process.exit(1);
}