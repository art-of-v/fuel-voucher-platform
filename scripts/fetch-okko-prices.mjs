#!/usr/bin/env node
// Builds a pump-price (колонка) import file for the `okko` brand out of OKKO's public price API.
//
// Source: https://www.okko.ua/api/uk/fuels — the page API behind okko.ua/fuels. It returns the same
// component tree the site renders, with the national price list in `bullets.items[]`, one entry per
// fuel: `{title, price, fuel_code, type}`.
//
// Why the API and not the page: the rendered HTML carries no prices at all outside a JavaScript state
// blob (window.__NUXT__), which is not JSON — it is an IIFE whose values live in the function's
// parameters. Reading it meant evaluating JavaScript. The API returns plain JSON, so this script and
// the backend's OkkoPriceClient (which runs this on a schedule) read the same thing the same way.
//
// Rows are found by looking for objects carrying both `fuel_code` and `price` anywhere in the tree,
// not by a fixed path: OKKO nests the tree differently per locale and revises it between page
// versions. A hard-coded path would read as "no prices" rather than as a failure.
//
// The site publishes a non-localized `fuel_code` beside each price ("A-95", "Pulls Diesel", "SPBT");
// the `title` is translated per request. We emit the code, and the backend maps it onto the catalog
// with OkkoFuelClassifier.CategoryFromSiteFuelCode.
//
// The title is dropped HERE only because this script's output is a CSV for the retired manual upload
// endpoint (planning #289, disabled 2026-10-12). The backend's own fetch — OkkoPriceClient, on a
// 15-minute schedule — reads the title and uses it to NAME a fuel the catalogue does not carry yet
// (ImportOkkoCatalogueCommandHandler). A CSV produced by this script therefore carries codes without
// labels, and the catalogue import cannot create anything from it; run the scheduled job, not this.
//
// Output is the CSV shape the retired manual upload endpoint accepted:
//
//   fuelCode, pricePerLiter
//
// (POST /api/admin/brands/okko/pump-prices/import - deleted 2026-10-21, planning #289. The
// scheduled job does this now; see OkkoPriceSyncService.)
//
// Usage:
//   node scripts/fetch-okko-prices.mjs                        # fetch live, write ./okko-prices.csv
//   node scripts/fetch-okko-prices.mjs --out path.csv
//   node scripts/fetch-okko-prices.mjs --from-file api.json   # offline, re-parse a saved response
//
// Note: `Pulls 100` (aviation jet fuel) and `AdBlue` (an additive, not a fuel grade) are fetched but
// have no counterpart in our catalog — the backend reports them as unmatched rather than guessing a
// fuel, so they are listed here for visibility and left in the file.

import { readFileSync } from "node:fs";
import { collapse, writePriceCsv } from "./lib/price-csv.mjs";

const DEFAULT_SOURCE = "https://www.okko.ua/api/uk/fuels";
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

/** Walks the payload collecting every object that carries both a fuel_code and a price. */
function findPriceRows(node, found = []) {
  if (node === null || typeof node !== "object") return found;

  if (Array.isArray(node)) {
    for (const item of node) findPriceRows(item, found);
    return found;
  }

  if ("fuel_code" in node && "price" in node) found.push(node);
  else for (const value of Object.values(node)) findPriceRows(value, found);

  return found;
}

async function loadPayload({ source, fromFile }) {
  if (fromFile) return readFileSync(fromFile, "utf8");
  const response = await fetch(source, {
    headers: {
      // Set to something contactable so OKKO can see who is polling.
      "User-Agent": "FuelFlow price import (https://github.com/art-of-v/fuel-voucher-platform)",
      Accept: "application/json",
    },
  });
  if (!response.ok) throw new Error(`GET ${source} -> HTTP ${response.status}`);
  return response.text();
}

async function main() {
  const args = parseArgs(process.argv.slice(2));
  if (args.help) {
    console.error("Usage: node scripts/fetch-okko-prices.mjs [--out <file>] [--source <url>] [--from-file <json>]");
    return;
  }

  const raw = findPriceRows(JSON.parse(await loadPayload(args)));
  if (raw.length === 0) {
    throw new Error("no rows with both fuel_code and price — the payload shape changed");
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
    throw new Error(`every fetched row was unusable: ${skipped.map((s) => `${s.code} (${s.reason})`).join(", ")}`);
  }

  writePriceCsv(rows, args.out);

  console.error(`wrote ${args.out}: ${rows.length} prices from ${raw.length} published rows`);
  console.error("  " + rows.map(([code, price]) => `${code}=${price}`).join("  "));
  for (const { code, reason } of skipped) console.error(`skipped ${code}: ${reason}`);
  console.error(
    "the manual upload endpoint is gone (planning #289, deleted 2026-10-21): the scheduled " +
      "OkkoPriceSyncService does this every 15 minutes - run the job, not this file",
  );
}

try {
  await main();
} catch (error) {
  // OKKO's API is third-party; a shape change should read as one clear line, not a stack trace
  // that buries the reason.
  console.error(`fetch-okko-prices: ${error instanceof Error ? error.message : error}`);
  process.exit(1);
}