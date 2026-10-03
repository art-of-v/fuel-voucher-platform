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

import { writeFileSync, readFileSync } from "node:fs";

const DEFAULT_SOURCE = "https://upg.ua/merezha_azk/";
const DEFAULT_OUT = "upg-stations.csv";
const STATION_ID = "upg";

const CSV_COLUMNS = [
  "id", "stationId", "name", "address", "phone", "city", "stationType", "lat", "lng",
];

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

const collapse = (value) => (typeof value === "string" ? value.replace(/\s+/g, " ").trim() : "");

/**
 * The city for the admin column and map search. UPG publishes an oblast (`Region`) rather
 * than a city, so the city comes from the address: the «м. Буча» / «село Фонтанка» segment.
 * Roadside sites («автошлях Київ-Харків, 91 км») have none — those keep an empty city and the
 * mobile falls back to deriving it from the address itself.
 */
function deriveCity(address) {
  const segments = address.split(",").map((s) => s.trim());
  for (const segment of segments) {
    if (SETTLEMENT_PREFIX.test(segment)) return segment.replace(SETTLEMENT_PREFIX, "").trim();
  }
  return "";
}

/** RFC 4180 quoting — the importer's splitter honours quotes and "" escapes. */
function csvField(value) {
  if (value == null || value === "") return "";
  // Coordinates arrive as numbers, text fields as strings — both must survive quoting.
  const text = collapse(String(value));
  return /[",\r\n]/.test(text) ? `"${text.replace(/"/g, '""')}"` : text;
}

function toCsvRow(station) {
  const cells = [
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
  return cells.map(csvField).join(",");
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
      const address = collapse(raw.Address);
      rows.push({ id, name, address, city: deriveCity(address), lat, lng });
    }
  }

  rows.sort((a, b) => a.id - b.id);

  const csv = [
    CSV_COLUMNS.join(","),
    ...rows.map(toCsvRow),
  ].join("\r\n") + "\r\n";

  writeFileSync(args.out, csv, "utf8");

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