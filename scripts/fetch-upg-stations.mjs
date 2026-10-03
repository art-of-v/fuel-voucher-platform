#!/usr/bin/env node
// Builds an АЗК import file for the `upg` brand out of UPG's public network map.
//
// Source: https://upg.ua/merezha_azk/ — the page inlines its station list as a JS object
// literal (`var objmap = {...}`) with one entry per АЗК: id, coordinates, name, address,
// region, services and live fuel prices. We take the location half of it and emit the CSV
// shape the admin importer already accepts:
//
//   id, stationId, name, address, phone, city, stationType, lat, lng
//
// (POST /api/admin/station-nodes/import — see StationNodeImportParser).
//
// Usage:
//   node scripts/fetch-upg-stations.mjs                        # fetch live, write ./upg-stations.csv
//   node scripts/fetch-upg-stations.mjs --out path.csv
//   node scripts/fetch-upg-stations.mjs --from-file page.html  # offline, re-parse a saved page
//
// Id scheme: `upg-<UPG id>` rather than the importer's coordinate-derived fallback. UPG's own
// id is stable across re-runs, so a station whose pin moves a few metres is still updated in
// place; deriving from coordinates would mint a second row and leave a ghost behind.

import { readFileSync } from "node:fs";
import { collapse, writeStationCsv } from "./lib/station-csv.mjs";


const DEFAULT_SOURCE = "https://upg.ua/merezha_azk/";
const DEFAULT_OUT = "upg-stations.csv";
const STATION_ID = "upg";

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

/** The page inlines the data as an object literal, not JSONP, so match the braces by hand. */
function extractObjmap(html) {
  const marker = "var objmap = ";
  const start = html.indexOf(marker);
  if (start < 0) throw new Error(`Marker '${marker}' not found — the page layout changed`);

  const from = start + marker.length;
  if (html[from] !== "{") throw new Error("Expected '{' right after the objmap marker");

  let depth = 0;
  let inString = false;
  for (let i = from; i < html.length; i++) {
    const c = html[i];
    if (inString) {
      if (c === "\\") i++;
      else if (c === '"') inString = false;
      continue;
    }
    if (c === '"') inString = true;
    else if (c === "{") depth++;
    else if (c === "}" && --depth === 0) {
      return JSON.parse(html.slice(from, i + 1));
    }
  }
  throw new Error("Unterminated objmap literal");
}

/** Settlement marker + optional dot, mirroring the mobile's SETTLEMENT_PREFIX. */
const SETTLEMENT_PREFIX = /^(?:м|с|смт|с-ще|сел(?:о|ище)|пос)(?:\.\s*|\s+)/i;
// Administrative segments the short address drops: oblast (incl. the abbreviated «обл.»),
// raion (all three spellings) and the country — the mobile's ADDRESS_OBLAST /
// ADDRESS_ADMIN_SEGMENT, plus the «... р.» abbreviation UPG uses on 12 sites.
const OBLAST_SEGMENT = /(?:^|\s)обл(?:асть|асті|астю|сть|\.)?$/i;
const ADMIN_SEGMENT = /(?:^|\s)район(?=\s|$)|(?:^|\s)р-н(?=\s|$)|(?:^|\s)р\.$|^україна$/i;

/**
 * Splits a UPG address into the pair the rest of the platform stores: `address` keeps only the
 * street, `city` the settlement. UPG publishes the administrative form —
 * «Запорізька обл., м. Запоріжжя, вул. Авраменка, 23» — and so does WOG, but OKKO is stored short
 * and the radar's address formatter is built around that shape, so UPG is imported short too.
 *
 * Dropping oblast/raion is safe for display: `formatShortAddress` rebuilds the
 * «{city}, {street}» form from `city` + the remainder, so both stored shapes render the same.
 */
function splitAddress(rawAddress) {
  const segments = collapse(rawAddress).split(",").map((s) => s.trim()).filter(Boolean);
  const kept = segments.filter((s) => !OBLAST_SEGMENT.test(s) && !ADMIN_SEGMENT.test(s));

  const settlementAt = kept.findIndex((s) => SETTLEMENT_PREFIX.test(s));
  // Only a settlement with a segment after it is the city. Seven sites glue the street onto it
  // without a comma — «м. Корсунь-Шевченківський вул. Гіфхорнська 25» — and there the single
  // segment is the whole address, so consuming it as a city would leave nothing behind.
  if (settlementAt < 0 || settlementAt === kept.length - 1) {
    return { address: kept.join(", "), city: "" };
  }

  const city = kept[settlementAt].replace(SETTLEMENT_PREFIX, "").trim();
  const address = kept
    .filter((s, i) => i !== settlementAt && s.toLowerCase() !== city.toLowerCase())
    .join(", ");
  return { address, city };
}

function toCsvCells(station) {
  return [
    `upg-${station.id}`,
    STATION_ID,
    station.name,
    station.address,
    "", // phone — UPG does not publish it per site
    station.city,
    "", // stationType — UPG publishes services, not an АЗК type; left blank hides the tag
    station.lat,
    station.lng,
  ];
}

async function loadPage({ source, fromFile }) {
  if (fromFile) return readFileSync(fromFile, "utf8");
  const response = await fetch(source, {
    headers: {
      "User-Agent": "Mozilla/5.0 (compatible; FuelFlow station import)",
      "Accept-Language": "uk,en;q=0.8",
    },
  });
  if (!response.ok) throw new Error(`GET ${source} -> HTTP ${response.status}`);
  return response.text();
}

async function main() {
  const args = parseArgs(process.argv.slice(2));
  if (args.help) {
    console.log(
      "Usage: node scripts/fetch-upg-stations.mjs [--out <file>] [--source <url>] [--from-file <html>]",
    );
    return;
  }

  const objmap = extractObjmap(await loadPage(args));
  const stations = objmap.data ?? [];
  if (!Array.isArray(stations) || stations.length === 0) {
    throw new Error("objmap.data is empty — the page layout changed");
  }

  const skipped = [];
  const seenIds = new Set();
  const rows = [];

  for (const raw of stations) {
    const lat = Number.parseFloat(raw.Latitude);
    const lng = Number.parseFloat(raw.Longitude);
    const name = collapse(raw.FullName);
    const id = raw.id;

    if (id == null) skipped.push({ id: "<none>", reason: "no UPG id" });
    else if (seenIds.has(id)) skipped.push({ id, reason: "duplicate id" });
    else if (!name) skipped.push({ id, reason: "empty name" });
    else if (!Number.isFinite(lat) || lat < -90 || lat > 90 ||
             !Number.isFinite(lng) || lng < -180 || lng > 180) {
      skipped.push({ id, reason: `bad coordinates ${raw.Latitude}/${raw.Longitude}` });
    } else if (lat === 0 && lng === 0) {
      skipped.push({ id, reason: "null island coordinates" });
    } else {
      seenIds.add(id);
      const { address, city } = splitAddress(raw.Address);
      rows.push({ id, name, address, city, lat, lng });
    }
  }

  rows.sort((a, b) => a.id - b.id);
  writeStationCsv(rows.map(toCsvCells), args.out);

  const inactive = stations.filter((s) => s.Active === false).length;
  const withoutCity = rows.filter((r) => !r.city).length;
  console.error(`wrote ${args.out}: ${rows.length} stations (page reported ${objmap.countData})`);
  if (inactive) console.error(`note: ${inactive} station(s) are flagged inactive on UPG's side`);
  if (withoutCity) console.error(`note: ${withoutCity} site(s) have no settlement in the address — city left blank`);
  for (const { id, reason } of skipped) console.error(`skipped #${id}: ${reason}`);
}

try {
  await main();
} catch (error) {
  // UPG rebuilds the page from time to time; a layout change should read as one clear line,
  // not a stack trace that buries the reason.
  console.error(`fetch-upg-stations: ${error instanceof Error ? error.message : error}`);
  process.exit(1);
}