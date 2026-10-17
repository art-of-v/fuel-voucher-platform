#!/usr/bin/env node
// Reads the fuel list UPG and WOG publish, as one CSV of (code, price) per brand.
//
// WHY ONE SCRIPT FOR TWO BRANDS. Both sites carried the SAME blob when this was written on
// 2026-10-17 - WOG's https://www.wog.ua/ua/fuels/ and UPG's https://upg.ua/ each embedded an
// identical `let averagePrices = {...}` and an identical `orderFuel` array. WOG grew out of UPG's
// network and both run on the same platform, so their fuel vocabulary is one vocabulary. Two scripts
// would be two copies of the same parser, which is how one of them starts disagreeing with the page.
//
// ...BUT WOG's COPY IS NOT RELIABLE, and this is the thing to know before wiring it anywhere:
// on 2026-10-17 the blob was present on WOG's page for the first request of the session and absent
// from every request after it (six in a row, page length dropping from 114850 to 107477 bytes), while
// UPG's carried it on every request including the ones in between. Either WOG serves that block
// conditionally or it deployed mid-session; nothing here can tell those apart from outside. So: the
// parser is the same and the shape is the same, but `wog` is unproven as an unattended source and
// will refuse loudly rather than write an empty file. Treat WOG as "read half only, needs a look at
// why" until it answers on consecutive days.
//
// THE SOURCE. Not a JSON API like OKKO's `/api/uk/fuels`: these two publish the prices inside the
// HTML, as a JavaScript literal the page reads to draw its price list. The blob is found by walking
// the document for the assignment rather than at a fixed path, the same reason the OKKO fetcher walks
// its JSON tree - these sites reorganise their markup between page versions, and a hard-coded path
// reads as "no prices" rather than as a failure.
//
// WHAT THE BLOB CARRIES. `averagePrices.data[]` is `{Id, Title, IconUrl, Price}` per grade, where
// `Title` is the brand's own slug for the grade - `upg95`, `upg100`, `upgDIESEL`, `EURO DIESEL`,
// `A-95`, `Газ` - and `Price` is published as a JSON number carrying float noise (`49.899999999999999`
// for 49.90). The slug is the code: it is what the order widget's `orderFuel` array keys on, so it is
// the same string the brand's own systems use, and it is what belongs in `fuel_types.brand_fuel_code`.
//
// WHAT IT DOES NOT CARRY. A display label. `Title` is both the code and the only name on offer, and
// `orderFuel`'s `title` equals `name` for every grade except A-95 - so an import that needed a
// customer-facing name has nothing better than the slug here, which is why the catalogue import
// reports such grades instead of naming a fuel "upg95" (see ImportOkkoCatalogueCommandHandler).
// These two brands are therefore good for PRICE matching and not yet good for catalogue creation.
//
// Output is the same two-column CSV the OKKO fetcher emits:
//
//   fuelCode, pricePerLiter
//
// Usage:
//   node scripts/fetch-upg-wog-prices.mjs --brand upg
//   node scripts/fetch-upg-wog-prices.mjs --brand wog --out wog-prices.csv
//   node scripts/fetch-upg-wog-prices.mjs --brand upg --from-file page.html    (offline)
import { readFileSync } from "node:fs";
import { resolve } from "node:path";
import { fileURLToPath } from "node:url";
import { collapse, writePriceCsv } from "./lib/upg-wog-price-csv.mjs";

const SOURCES = {
  upg: "https://upg.ua/",
  wog: "https://www.wog.ua/ua/fuels/",
};

const DEFAULT_OUT = "upg-wog-prices.csv";

function parseArgs(argv) {
  const args = { brand: null, source: null, out: DEFAULT_OUT, fromFile: null, help: false };
  for (let i = 0; i < argv.length; i++) {
    const arg = argv[i];
    if (arg === "--out") args.out = argv[++i];
    else if (arg === "--source") args.source = argv[++i];
    else if (arg === "--brand") args.brand = argv[++i];
    else if (arg === "--from-file") args.fromFile = argv[++i];
    else if (arg === "--help") args.help = true;
    else throw new Error(`Unknown argument: ${arg}`);
  }
  return args;
}

/**
 * Pulls `averagePrices` out of the page.
 *
 * The assignment is located by its `let averagePrices = ` prefix rather than by any path, and the
 * object is balanced by brace counting so a `}` inside a string does not end it early. Returns null
 * when the page carries no blob at all, which the caller turns into an error rather than an empty CSV.
 */
export function extractAveragePrices(html) {
  const marker = /averagePrices\s*=\s*\{/;
  const match = marker.exec(html);
  if (!match) return null;

  const start = match.index + match[0].length - 1;
  let depth = 0;
  let inString = false;
  let escaped = false;

  for (let i = start; i < html.length; i++) {
    const c = html[i];

    if (inString) {
      if (escaped) escaped = false;
      else if (c === "\\") escaped = true;
      else if (c === '"') inString = false;
      continue;
    }

    if (c === '"') inString = true;
    else if (c === "{") depth++;
    else if (c === "}") {
      depth--;
      if (depth === 0) return html.slice(start, i + 1);
    }
  }

  return null;
}

/**
 * Turns one blob row into a CSV row, or null with a reason.
 *
 * `Price` arrives as a JSON number with float noise, so it is rounded here rather than stringified
 * raw: `49.899999999999999` written into a price column is a number every downstream reader has to
 * know to round.
 */
export function toRow(item) {
  const code = collapse(item?.Title);

  if (!code) return { skip: { code: "<none>", reason: "no Title" } };

  const price = typeof item?.Price === "number" ? item.Price : Number.parseFloat(collapse(item?.Price));
  if (!Number.isFinite(price)) return { skip: { code, reason: `unparseable Price "${item?.Price}"` } };
  if (price <= 0) return { skip: { code, reason: `non-positive price ${price}` } };

  return { row: [code, price.toFixed(2)] };
}

/** Every usable row of a page, with what was dropped and why. */
export function parsePrices(html) {
  const blob = extractAveragePrices(html);
  if (blob === null) return { rows: [], skipped: [], missing: true };

  let data;
  try {
    data = JSON.parse(blob);
  } catch (error) {
    return { rows: [], skipped: [], missing: true, parseError: String(error) };
  }

  const items = Array.isArray(data?.data) ? data.data : [];
  const rows = [];
  const skipped = [];
  const seen = new Set();

  for (const item of items) {
    const parsed = toRow(item);
    if (parsed.skip) {
      // A duplicate Title is the page repeating the component, not a second grade. Kept, because a
      // duplicate with a DIFFERENT price is the case worth saying out loud.
      if (seen.has(collapse(item?.Title)))
        skipped.push({ code: collapse(item?.Title), reason: `duplicate title, price ${item?.Price}` });
      else skipped.push(parsed.skip);
      continue;
    }

    const code = parsed.row[0];
    if (seen.has(code)) {
      skipped.push({ code, reason: "duplicate title" });
      continue;
    }

    seen.add(code);
    rows.push(parsed.row);
  }

  return { rows, skipped, missing: false };
}

async function loadPage({ source, fromFile }) {
  if (fromFile) return readFileSync(fromFile, "utf8");
  const response = await fetch(source, {
    headers: {
      "User-Agent": "FuelFlow price import (https://github.com/art-of-v/fuel-voucher-platform)",
      Accept: "text/html",
    },
  });
  if (!response.ok) throw new Error(`GET ${source} -> HTTP ${response.status}`);
  return response.text();
}

async function main() {
  const args = parseArgs(process.argv.slice(2));
  if (args.help) {
    console.error("Usage: node scripts/fetch-upg-wog-prices.mjs --brand <upg|wog> [--out <file>] [--source <url>] [--from-file <html>]");
    return;
  }

  if (!args.brand) throw new Error("--brand <upg|wog> is required: the two brands publish the same blob and the file is named after one run");
  if (!SOURCES[args.brand] && !args.source) throw new Error(`Unknown brand '${args.brand}'; expected upg or wog`);

  const source = args.source ?? SOURCES[args.brand];
  const { rows, skipped, missing, parseError } = parsePrices(await loadPage({ source, fromFile: args.fromFile }));

  if (missing) {
    // Deliberately an error and not an empty CSV: this file's whole job is to say what the brand
    // publishes, and a page that stopped carrying the blob must not be reported as "no prices today".
    throw new Error(
      `no averagePrices blob in the page${parseError ? ` (${parseError})` : ""} - the page shape changed, not the catalogue`,
    );
  }

  if (rows.length === 0) {
    throw new Error(`every published row was unusable: ${skipped.map((s) => `${s.code} (${s.reason})`).join(", ")}`);
  }

  writePriceCsv(rows, args.out);

  console.error(`wrote ${args.out}: ${rows.length} ${args.brand} price(s) from ${source}`);
  if (skipped.length > 0) {
    console.error(`skipped ${skipped.length}: ${skipped.map((s) => `${s.code} (${s.reason})`).join(", ")}`);
  }
  console.error("note: these brands publish no display label, so these codes can price an existing fuel but cannot name a new one");
}

// Only when invoked directly: fetch-upg-wog-prices.test.mjs imports {@link parsePrices}, and an
// unconditional `main()` would fire a live network fetch and drop a CSV on every test run. Compared
// by resolved path rather than by the raw `process.argv[1]` string, because on Windows the two
// differ in slash direction and the guard would simply never fire.
if (process.argv[1] && resolve(process.argv[1]) === fileURLToPath(import.meta.url)) {
  try {
    await main();
  } catch (error) {
    // These pages are third-party; a shape change should read as one clear line, not a stack trace
    // that buries the reason.
    console.error(`fetch-upg-wog-prices: ${error instanceof Error ? error.message : error}`);
    process.exit(1);
  }
}
