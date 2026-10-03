// Shared CSV contract for the station-network fetchers in scripts/.
//
// The column set and the quoting rules here must stay in lockstep with the backend importer
// (StationNodeImportParser in backend/src/FuelFlow.API/Features/Stations/ImportStationNodes) —
// that parser is the only thing standing between a scraper and a silently mangled import, so the
// contract lives in one place rather than being copied into every fetcher.

import { writeFileSync } from "node:fs";

export const CSV_COLUMNS = [
  "id", "stationId", "name", "address", "phone", "city", "stationType", "lat", "lng",
];

/** Collapse whitespace runs and trim — UPG and KLO both ship padding and doubled spaces. */
export const collapse = (value) =>
  typeof value === "string" ? value.replace(/\s+/g, " ").trim() : "";

/** RFC 4180 quoting — the importer's splitter honours quotes and "" escapes. */
export function csvField(value) {
  if (value == null || value === "") return "";
  // Coordinates arrive as numbers, text fields as strings — both must survive quoting.
  const text = collapse(String(value));
  return /[",\r\n]/.test(text) ? `"${text.replace(/"/g, '""')}"` : text;
}

/** Serialises one row given in {@link CSV_COLUMNS} order. */
export const csvRow = (cells) => cells.map(csvField).join(",");

/** Writes a header plus rows, CRLF-terminated as the importer's line splitter expects. */
export function writeStationCsv(rows, outFile) {
  const csv = [CSV_COLUMNS.join(","), ...rows.map(csvRow)].join("\r\n") + "\r\n";
  writeFileSync(outFile, csv, "utf8");
}
