#!/usr/bin/env node
// Builds an АЗК import file for the `upg` brand out of UPG's public network map.
//
// Source: https://upg.ua/merezha_azk/ — the page inlines its station list as a JS object
// literal (`var objmap = {...}`) with one entry per АЗК: id, coordinates, name, address,
// region, services and live fuel prices. We take the location half of it and emit the CSV
// shape the admin importer already accepts:
//
//   externalId, brandId (the network's SLUG), name, address, phone, city, stationType, lat, lng
//
// (POST /api/admin/brand-stations/import — see ImportBrandStationsCommandHandler). The importer
// resolves the slug to the brand's uuid; the row is keyed by externalId within that brand.
//
// Usage:
//   node scripts/fetch-upg-stations.mjs                        # fetch live, write ./upg-stations.csv
//   node scripts/fetch-upg-stations.mjs --out path.csv
//   node scripts/fetch-upg-stations.mjs --from-file page.html  # offline, re-parse a saved page
//
// Id scheme: `upg-<UPG id>` rather than the importer's coordinate-derived fallback. UPG's own
// id is stable across re-runs, so a station whose pin moves a few metres is still updated in
// place; deriving from coordinates would mint a second row and leave a ghost behind.
//
// Name scheme: UPG's `FullName` is not used verbatim — see {@link toName}.

import { readFileSync } from "node:fs";
import { resolve } from "node:path";
import { fileURLToPath } from "node:url";
import { collapse, writeStationCsv } from "./lib/station-csv.mjs";


const DEFAULT_SOURCE = "https://upg.ua/merezha_azk/";
const DEFAULT_OUT = "upg-stations.csv";
// The importer takes the network's slug, not its uuid - a supplier file says "upg", never a
// uuid, and the slug is what it resolves.
const BRAND_SLUG = "upg";

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

// A settlement marker anywhere in a segment, not only at its start: UPG routinely glues the
// raion onto the same comma-less chunk — «Володимирський р-н. м. Нововолинськ». The lookahead
// demands a dot or a space after the marker, which is what keeps «с» out of «с/рада Кисорицька».
// Longest alternative first, so «селище» is not read as «с».
//
// Both spellings of the settlement word are listed on purpose. «селище» with U+0449 (щ) is the
// standard one; UPG writes «селище» with U+0448 (ш) on two live stations, and a parser that knows
// only the correct spelling leaves those two with an empty city while looking otherwise complete.
// The optional tail is what lets bare «сел.» match, which is the common abbreviated form.
const SETTLEMENT_MARKER = /(?:^|[\s.,])(смт|с-ще|сел(?:ище|ише|о)?|м|с|пос)(?=[.\s])/gi;
/**
 * The first word of a street, matched against ONE word and anchored with `$` rather than `\b`.
 *
 * `\b` is a trap here: JavaScript's `\w` is `[A-Za-z0-9_]` only, so a Cyrillic word contains no
 * word-characters at all and the boundary never exists. `/^вул\b/.test("вул")` is FALSE. A `\b`
 * version of this pattern therefore matches nothing at all, silently, and street detection just
 * stops working while the parser still returns plausible output.
 */
const STREET_WORD = /^(?:вул(?:иця|\.)?|просп|проспект|пр-т|бул(?:ьвар)?|пров|провулок|шосе|площа|майдан|набережна|проїзд|переулок|пр-д)$/i;
/** A street-type noun owns the word before it — «Хмельницьке шосе» — so that word is not the settlement. */
const STREET_NOUN = /^(?:шосе|проспект|бульвар|провулок|площа|майдан|набережна|вулиця|проїзд|переулок)$/i;
/** One word of a settlement name: letters, plus the hyphen and apostrophe inside compound names. */
const NAME_WORD = /^[\p{L}\p{M}]+(?:[-'’ʼ][\p{L}\p{M}]+)*$/u;
// Administrative segments the short address drops: oblast (incl. the abbreviated «обл.»),
// raion (all three spellings) and the country — the mobile's ADDRESS_OBLAST /
// ADDRESS_ADMIN_SEGMENT, plus the «... р.» abbreviation UPG uses on 12 sites.
//
// Every alternative is anchored to the END of the segment. They must be: an unanchored
// `р-н\.?(?=\s|$)` also matches «Олександрівський р-н. вул. Українська» and would delete a whole
// chunk that still holds the street. The old `р-н(?=\s|$)` never matched «... р-н.» because of the
// dot, which is why the raion survived into the address; anchoring with `\.?$` fixes that without
// widening the deletion.
const OBLAST_SEGMENT = /(?:^|\s)обл(?:асть|асті|астю|сть|\.)?$/i;
const ADMIN_SEGMENT = /(?:^|\s)район\s*$|(?:^|\s)р-н\.?$|(?:^|\s)р\.$|^україна$/i;
/**
 * A raion glued to the front of the street, in the same comma-less chunk: «Хортицький р-н вул.
 * Запорізького казацтва». Without this the raion is stripped when it is a chunk of its own and kept
 * when it shares one, which is the inconsistency a reader would file as a bug.
 *
 * The word it matches (район / р-н / р.) is specific enough that eating it cannot lose a street.
 */
const ADMIN_PREFIX = /^[\p{L}][\p{L}-]*(?:[-'][\p{L}]+)?\s+(?:район|р-н\.?|р\.)\s*/iu;

/**
 * Pulls the settlement out of one comma-separated chunk, reporting the text before it (`prefix`)
 * and after it (`suffix`) so the caller can keep whichever half is real.
 *
 * The name runs from the marker up to the first word that cannot belong to it: a street, a digit,
 * or any punctuation («с/рада.», «а/д»). Words that turn out to name a street for a following
 * STREET_NOUN are given back — «с. Зарванці Хмельницьке шоссе 14» is the village Зарванці, not
 * «Зарванці Хмельницьке».
 *
 * Returns null when the chunk carries no settlement, which is the common case for a street-only
 * chunk and for the council forms («с/рада Кисорицька») that are not a town.
 *
 * The suffix matters as much as the city: «м. Хмельницький вул. Прибузька 11\3» is one comma-less
 * chunk, so a parser that only cut the prefix would import the station with an EMPTY address.
 */
function takeSettlement(segment) {
  for (const match of segment.matchAll(SETTLEMENT_MARKER)) {
    // «вул. С. Бандери», «вул. М. Грушевського» — an initial that follows a street word is a
    // person's name, not a settlement. Without this the city becomes «С» and the address «вул., 2а».
    const before = segment.slice(0, match.index).replace(/[\s.,]+$/, "");
    if (before && STREET_WORD.test(before.split(/\s+/).pop())) continue;

    const rest = segment.slice(match.index + match[0].length).replace(/^[.\s]+/, "");
    const words = rest.split(/\s+/);
    const taken = [];
    for (const word of words) {
      if (!NAME_WORD.test(word) || STREET_WORD.test(word)) break;
      taken.push(word);
    }
    while (taken.length > 0 && STREET_NOUN.test(words[taken.length])) taken.pop();

    const city = taken.join(" ");
    if (!city) continue;
    const suffix = words.slice(taken.length).join(" ");
    return { city, prefix: segment.slice(0, match.index), suffix };
  }
  return null;
}

/**
 * Splits a UPG address into the pair the rest of the platform stores: `address` keeps only the
 * street, `city` the settlement. UPG publishes the administrative form —
 * «Запорізька обл., м. Запоріжжя, вул. Авраменка, 23» — and so does WOG, but OKKO is stored short
 * and the radar's address formatter is built around that shape, so UPG is imported short too.
 *
 * Dropping oblast/raion is safe for display: `formatShortAddress` rebuilds the
 * «{city}, {street}» form from `city` + the remainder, so both stored shapes render the same.
 *
 * The settlement is cut out of the chunk it sits in rather than requiring a chunk of its own,
 * because UPG usually writes it glued to its raion («Прилуцький р-н. с. Заїзд») or to the street
 * («м. Шепетівка вул. Старокостянтинівське шосе 70»). Requiring a dedicated chunk is what left 56
 * of 500 stations with an empty city.
 */
export function splitAddress(rawAddress) {
  const segments = collapse(rawAddress).split(",").map((s) => s.trim()).filter(Boolean);

  let city = "";
  const kept = [];
  for (const segment of segments) {
    const found = takeSettlement(segment);
    // Both halves are filtered independently: a lone «Володимирський р-н.» is administrative and
    // falls out, while the street after the settlement stays.
    for (const piece of found ? [found.prefix, found.suffix] : [segment]) {
      const text = collapse(piece).replace(ADMIN_PREFIX, "").trim();
      if (text && !OBLAST_SEGMENT.test(text) && !ADMIN_SEGMENT.test(text)) kept.push(text);
    }
    if (found && !city) city = found.city;
  }

  return { address: collapse(kept.join(", ")), city };
}

/**
 * UPG's own `FullName` is unusable as a station name: it is `АЗК №100 цілодобово` /
 * `АЗС №150 (🍽 07:00 – 21:00)` — 494 of 500 sites end in the bare word «цілодобово» and the
 * other 6 carry their café's opening hours. Neither tail says anything the driver cannot already
 * see, so 500 rows of «АЗК №NNN цілодобово» was a wall of near-identical text in the admin list
 * and the useless title on every map callout.
 *
 * The site number IS worth keeping: it is what UPG calls the place, it is the one part of the
 * string that differs, and all 500 numbers are unique. So the name is rebuilt as brand + number —
 * `UPG №100` — which also drops UPG's own АЗК/АЗС inconsistency (82 sites say АЗК, 418 say АЗС).
 *
 * The café-hours tail is dropped, not relocated: hours go stale and nothing would ever re-import
 * them, so a stale opening time is worse than no opening time.
 *
 * Returns "" when the number cannot be found, which {@link main} reports as a skipped row rather
 * than importing a nameless point.
 */
export function toName(fullName) {
  const match = /^АЗ[СК]\s*№?\s*(\d+)/i.exec(collapse(fullName));
  return match ? `UPG №${match[1]}` : "";
}

function toCsvCells(station) {
  return [
    `upg-${station.id}`,
    BRAND_SLUG,
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
    const name = toName(raw.FullName);
    const id = raw.id;

    if (id == null) skipped.push({ id: "<none>", reason: "no UPG id" });
    else if (seenIds.has(id)) skipped.push({ id, reason: "duplicate id" });
    else if (!name) skipped.push({ id, reason: `no site number in "${collapse(raw.FullName)}"` });
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

// Only when invoked directly: fetch-upg-stations.test.mjs imports {@link toName}, and an
// unconditional `main()` would fire a live network fetch and drop a CSV on the test run.
if (process.argv[1] && resolve(process.argv[1]) === fileURLToPath(import.meta.url)) {
  try {
    await main();
  } catch (error) {
    // UPG rebuilds the page from time to time; a layout change should read as one clear line,
    // not a stack trace that buries the reason.
    console.error(`fetch-upg-stations: ${error instanceof Error ? error.message : error}`);
    process.exit(1);
  }
}
