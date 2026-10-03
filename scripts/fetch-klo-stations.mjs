#!/usr/bin/env node
// Builds an АЗК import file for the `klo` brand out of KLO's public network map.
//
// Source: https://www.klo.ua/ — the homepage carries a Google Map rendered by the
// `map-multi-marker` WordPress plugin, which inlines the pin list as a JS array
// (`var mmm_maps = [...]`) holding only `{id, lat, lng, icon}`. The human-readable half of each
// pin — title, address, phone — is not in that array: the plugin fetches it per pin from
// `admin-ajax.php?action=mmm_async_content_marker&id=<id>` when a marker is clicked, and returns
// a small HTML fragment. So this script walks the pins and pulls that fragment for each one.
//
// Output is the CSV shape the admin importer already accepts:
//
//   id, stationId, name, address, phone, city, stationType, lat, lng
//
// (POST /api/admin/station-nodes/import — see StationNodeImportParser.)
//
// Usage:
//   node scripts/fetch-klo-stations.mjs                        # fetch live, write ./klo-stations.csv
//   node scripts/fetch-klo-stations.mjs --out path.csv
//   node scripts/fetch-klo-stations.mjs --offline             # reuse a cached pin/details dump
//
// Id scheme: `klo-<KLO marker id>` rather than the importer's coordinate-derived fallback, so a
// pin that shifts a few metres is updated in place instead of leaving a ghost behind — same as
// the OKKO/WOG/UPG imports, which are all keyed by the source's own site number.

import { readFileSync, writeFileSync } from "node:fs";
import { collapse, writeStationCsv } from "./lib/station-csv.mjs";

const DEFAULT_SOURCE = "https://www.klo.ua/";
const DEFAULT_OUT = "klo-stations.csv";
const STATION_ID = "klo";
const CACHE_FILE = "klo-details.json";
const UA = "Mozilla/5.0 (compatible; FuelFlow station import)";

function parseArgs(argv) {
  const args = { source: DEFAULT_SOURCE, out: DEFAULT_OUT, offline: false };
  for (let i = 0; i < argv.length; i++) {
    const arg = argv[i];
    if (arg === "--out") args.out = argv[++i];
    else if (arg === "--source") args.source = argv[++i];
    else if (arg === "--offline") args.offline = true;
    else if (arg === "--help" || arg === "-h") args.help = true;
    else throw new Error(`Unknown argument: ${arg}`);
  }
  return args;
}

const decodeEntities = (html) =>
  html
    .replace(/&#0?39;/g, "'")
    .replace(/&quot;/g, '"')
    .replace(/&nbsp;/g, " ")
    .replace(/&amp;/g, "&");

/** The plugin's info-window fragment is HTML; flatten one `<li>`'s text. */
function liText(html, className) {
  const block = html.match(new RegExp(`class="${className}"[\\s\\S]*?</li>`, "i"));
  if (!block) return "";
  const text = block[0]
    .replace(/<[^>]+>/g, " ")
    .replace(/\s+/g, " ")
    .trim();
  // The match starts at the class attribute, so the opening tag's own ">" is left orphaned once
  // the tags are stripped — consume it along with the attribute.
  return text.replace(new RegExp(`^class="${className}"\\s*>?\\s*`, "i"), "").trim();
}

/** Matches `var mmm_maps = [...]` by bracket counting, honouring JSON string escapes. */
function extractMarkerArray(html, name) {
  const marker = `var ${name} = `;
  const start = html.indexOf(marker);
  if (start < 0) throw new Error(`Marker '${marker}' not found — the page layout changed`);

  const from = start + marker.length;
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
    else if (c === "[") depth++;
    else if (c === "]" && --depth === 0) return JSON.parse(html.slice(from, i + 1));
  }
  throw new Error(`Unterminated '${name}' literal`);
}

// KLO's addresses are the messiest of any brand we import, and they are not localised: street
// names appear in both Ukrainian ("Броварський проспект") and Russian ("Дворянская улица"), the
// country is spelled "Украина" on 60 of 65 pins, and every urban pin ends "..., <city>, <oblast>,
// <country>, <postcode>". The short form the rest of the catalog stores is "{city}, {street}", so
// the tail is administrative scaffolding and is dropped.

const COUNTRY_SEGMENT = /^(?:укра[їі]на|украина)$/i;
const POSTCODE_SEGMENT = /^\d{5}$/;
/** «Київська обл.», «Житомирська область», «Kiev oblast», and the Russian «Киевская». */
const OBLAST_SEGMENT =
  /(?:^|\s)обл\.?$|(?:^|\s)облас[ьт][ьи]\.?$|(?:^|\s)oblast$|^киевская$/i;
const RAION_SEGMENT = /(?:^|\s)район(?=\s|$)|(?:^|\s)р-н\.?$/i;
/** A settlement marker with its name: «м. Борислав», «с.Юрів», «пгт. Микуличи», «село Х». */
const SETTLEMENT_PREFIX = /^(?:м|с|смт|пгт|с-ще|сел(?:о|ище)|пос)(?:\.\s*|\s+)/i;
/** A settlement marker with no name of its own — «пгт, вулиця Вокзальна, 2». */
const BARE_SETTLEMENT = /^(?:м|с|смт|пгт|с-ще)\.?$/i;
/**
 * Street words that must never be mistaken for a place name. The lookahead is a negative
 * `\p{L}` rather than `\b`: JavaScript's `\b` is defined over ASCII `\w`, so it never fires
 * between two Cyrillic letters and would silently fail to anchor here.
 */
const STREET_WORD =
  /^(?:вул|улиця|улица|ул|просп|проспект|пров|провулок|бул|бульвар|бульв|набережна|набережная|шоссе|шосе|траса|траси|дорога|дороги|а\/д|автодорога|автомобільна|аеропорт|термінал|terminal|км)(?!\p{L})/iu;
/** A city/settlement name: capitalised, no digits, not a street word, at most three words. */
const PLACE_NAME = /^[\p{Lu}][\p{L}'’.-]*(?:\s+[\p{Lu}][\p{L}'’.-]*){0,2}$/u;

/**
 * Splits a KLO address into the pair the catalog stores: `city` the settlement, `address` only
 * the street. Returns the address untouched when no city can be identified, so nothing is lost.
 */
function splitAddress(rawAddress) {
  const segments = decodeEntities(rawAddress)
    .split(",")
    .map((s) => collapse(s))
    .filter(Boolean)
    .filter(
      (s) =>
        !COUNTRY_SEGMENT.test(s) &&
        !POSTCODE_SEGMENT.test(s) &&
        !OBLAST_SEGMENT.test(s) &&
        !RAION_SEGMENT.test(s) &&
        !BARE_SETTLEMENT.test(s),
    );
  if (segments.length === 0) return { address: "", city: "" };

  // A city named outright wins: «с. Тепловка», «пгт. Микуличи», «с.Юрів».
  const settled = segments.findIndex((s) => SETTLEMENT_PREFIX.test(s));
  if (settled >= 0) {
    const city = collapse(segments[settled].replace(SETTLEMENT_PREFIX, ""));
    const address = segments.filter((_, i) => i !== settled).join(", ");
    if (city && address) return { address, city };
  }

  // Otherwise the settlement is the trailing segment — «..., Київ», «..., Ірпінь».
  const last = segments.length - 1;
  const tail = segments[last];
  if (tail !== undefined && !/\d/.test(tail) && !STREET_WORD.test(tail) && PLACE_NAME.test(tail)) {
    const address = segments.slice(0, last).join(", ");
    if (address) return { address, city: tail };
  }

  // «Вишгород ул. Набережна 36» — the settlement runs into a street word inside one segment.
  if (segments.length === 1) {
    const inline = segments[0].match(
      /^(.+?)\s+(?:вул|вулиця|улица|ул|просп|проспект|провулок|бул|бульвар)(?!\p{L})\.?\s*(.*)$/iu,
    );
    if (inline) {
      const head = collapse(inline[1]);
      if (head && !/\d/.test(head) && PLACE_NAME.test(head)) {
        return { address: collapse(inline[2]), city: head };
      }
    }
  }

  return { address: segments.join(", "), city: "" };
}

/**
 * KLO titles are near-identical («АЗС КЛО» on 62 of 65 pins), which would make the map list a
 * wall of duplicates. The brand plus the town reads like WOG's «WOG Івано-Франківків» and is
 * derived from published data, not invented.
 */
function toName(title, city) {
  // «АЗС КЛО на воде» is the moored bunker, not a road pump — keep the distinction, but read it
  // as a suffix so the town still sits in the middle: «KLO Київ на воде».
  const floating = /вод/i.test(title) ? " на воде" : "";
  return city ? `KLO ${city}${floating}` : `KLO${floating}`;
}

async function fetchDetails({ source, offline }) {
  if (offline) return JSON.parse(readFileSync(CACHE_FILE, "utf8"));

  const response = await fetch(source, { headers: { "User-Agent": UA, "Accept-Language": "uk,en;q=0.8" } });
  if (!response.ok) throw new Error(`GET ${source} -> HTTP ${response.status}`);
  const html = await response.text();

  const maps = extractMarkerArray(html, "mmm_maps");
  const localizeRaw = html.match(/var mmm_localize\s*=\s*(\{.*?\});/s)?.[1];
  if (!localizeRaw) throw new Error("Marker 'var mmm_localize' not found — the plugin markup changed");
  const localize = JSON.parse(localizeRaw);
  const markers = maps.flatMap((m) => m.markers ?? []);
  if (markers.length === 0) throw new Error("no markers in mmm_maps — the plugin markup changed");

  const details = [];
  for (const marker of markers) {
    const res = await fetch(localize.ajax_url, {
      method: "POST",
      headers: { "User-Agent": UA, "Content-Type": "application/x-www-form-urlencoded; charset=UTF-8" },
      body: `action=mmm_async_content_marker&id=${marker.id}`,
    });
    const fragment = res.ok ? await res.text() : "";
    details.push({
      id: marker.id,
      lat: marker.lat,
      lng: marker.lng,
      title: collapse(decodeEntities((fragment.match(/<h2>([\s\S]*?)<\/h2>/) ?? [, ""])[1])),
      address: collapse(decodeEntities(liText(fragment, "adresse"))),
      telephone: collapse(decodeEntities(liText(fragment, "telephone"))),
    });
    // The marker fragment is one request per pin; pace them rather than firing 65 at once.
    await new Promise((resolve) => setTimeout(resolve, 120));
  }

  writeFileSync(CACHE_FILE, JSON.stringify(details, null, 2), "utf8");
  return details;
}

function toCsvCells(pin) {
  const { address, city } = splitAddress(pin.address);
  return [
    `klo-${pin.id}`,
    STATION_ID,
    toName(pin.title, city),
    address,
    pin.telephone, // real, published — KLO is the one brand that gives a number per site
    city,
    "", // stationType — KLO has no АЗК type; left blank hides the tag
    pin.lat,
    pin.lng,
  ];
}

async function main() {
  const args = parseArgs(process.argv.slice(2));
  if (args.help) {
    console.log(
      "Usage: node scripts/fetch-klo-stations.mjs [--out <file>] [--source <url>] [--offline]",
    );
    return;
  }

  const pins = await fetchDetails(args);
  const skipped = [];
  const seenIds = new Set();
  const rows = [];

  for (const pin of pins) {
    const lat = Number.parseFloat(pin.lat);
    const lng = Number.parseFloat(pin.lng);
    const { address } = splitAddress(pin.address);

    if (pin.id == null) skipped.push({ id: "<none>", reason: "no KLO marker id" });
    else if (seenIds.has(pin.id)) skipped.push({ id: pin.id, reason: "duplicate id" });
    else if (!Number.isFinite(lat) || lat < -90 || lat > 90 ||
             !Number.isFinite(lng) || lng < -180 || lng > 180) {
      skipped.push({ id: pin.id, reason: `bad coordinates ${pin.lat}/${pin.lng}` });
    } else if (lat === 0 && lng === 0) {
      skipped.push({ id: pin.id, reason: "null island coordinates" });
    } else if (!address) {
      skipped.push({ id: pin.id, reason: `no usable address in "${pin.address}"` });
    } else {
      seenIds.add(pin.id);
      rows.push(toCsvCells(pin));
    }
  }

  rows.sort((a, b) => Number(a[0].slice(4)) - Number(b[0].slice(4)));
  writeStationCsv(rows, args.out);

  console.error(`wrote ${args.out}: ${rows.length} stations of ${pins.length} pins`);
  const withoutCity = rows.filter((r) => !r[5]).length;
  if (withoutCity) console.error(`note: ${withoutCity} site(s) have no city in the address — city left blank`);
  for (const { id, reason } of skipped) console.error(`skipped #${id}: ${reason}`);
}

try {
  await main();
} catch (error) {
  // The plugin markup is third-party; a change there should read as one clear line, not a
  // stack trace that buries the reason.
  console.error(`fetch-klo-stations: ${error instanceof Error ? error.message : error}`);
  process.exit(1);
}
