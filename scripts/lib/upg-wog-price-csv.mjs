// Shared CSV contract for the UPG/WOG fuel-price fetcher in scripts/.
//
// The column set and the quoting rules here must stay in lockstep with the backend importer
// (OkkoPriceSheetParser in backend/src/FuelFlow.API/Features/Pricing/ImportOkkoPumpPrices) if this
// ever feeds the CSV upload endpoint. That endpoint is currently disabled (planning #289, retired
// 2026-10-12), and the OKKO fetcher keeps its own copy of these rules for exactly that reason - so
// the duplication is deliberate rather than an oversight, and it disappears if #289 is reversed in
// favour of resolving fuels by name.

import { writeFileSync } from "node:fs";

export const PRICE_CSV_COLUMNS = ["fuelCode", "pricePerLiter"];

/** Collapse whitespace runs and trim - price labels arrive padded when read out of the SSR payload. */
export const collapse = (value) =>
  typeof value === "string" ? value.replace(/\s+/g, " ").trim() : "";

/** RFC 4180 quoting - the importer's splitter honours quotes and "" escapes. */
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
