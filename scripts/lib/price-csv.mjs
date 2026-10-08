// Shared CSV contract for the pump-price fetchers in scripts/.
//
// The column set and the quoting rules here must stay in lockstep with the backend importer
// (OkkoPriceSheetParser in backend/src/FuelFlow.API/Features/Pricing/ImportOkkoPumpPrices) — that
// parser is the only thing standing between a scraper and a silently mangled import, so the
// contract lives in one place rather than being copied into every fetcher.

import { writeFileSync } from "node:fs";

export const PRICE_CSV_COLUMNS = ["fuelCode", "pricePerLiter"];

/** Collapse whitespace runs and trim — price labels arrive padded when read out of the SSR payload. */
export const collapse = (value) =>
  typeof value === "string" ? value.replace(/\s+/g, " ").trim() : "";

/** RFC 4180 quoting — the importer's splitter honours quotes and "" escapes. */
export function csvField(value) {
  if (value == null || value === "") return "";
  const text = collapse(String(value));
  return /[",\r\n]/.test(text) ? `"${text.replace(/"/g, '""')}"` : text;
}

/** Serialises one row given in {@link PRICE_CSV_COLUMNS} order. */
export const csvRow = (cells) => cells.map(csvField).join(",");

/** Writes a header plus rows, CRLF-terminated as the importer's line splitter expects. */
export function writePriceCsv(rows, outFile) {
  const csv = [PRICE_CSV_COLUMNS.join(","), ...rows.map(csvRow)].join("\r\n") + "\r\n";
  writeFileSync(outFile, csv, "utf8");
}