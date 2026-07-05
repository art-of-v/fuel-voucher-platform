# Plan: Speed Up Vouchers Import (Fast, Robust, Gemini-Economy)

## Current State Summary

| Layer | Current behavior | Bottlenecks |
|-------|------------------|-------------|
| **Job** | One job at a time; files processed **sequentially** | No parallelism across files |
| **PDF path** | 1 Gemini request per PDF (good), then **sequential** QR scan, then **sequential** persist per voucher | QR scan and persist are serial; many DB/event round-trips |
| **Fallback** | Per-page: 1 Gemini request per page, then persist per voucher | N pages = N Gemini calls; expensive and slow |
| **Per voucher** | `ensureMasterData` + `createVoucher` + `publishEvent` (×2: DB outbox + Redis) | Repeated master lookups; event per voucher |
| **Gemini** | Multiple models tried on failure; long prompts; PDF schema asks for `qrCodeData` (then we overwrite with local scan) | Extra tokens; cascade retries cost more |

---

## Goals

1. **Speed**: Minimize wall-clock time (parallelism, fewer round-trips, less redundant work).
2. **Robust**: Keep PDF-first + page fallback; retries; validation; idempotent writes.
3. **Gemini economy**: Fewer requests, fewer tokens, single cheap model first, batch where possible.

---

## Phase 1: Gemini Request Economy (Fewer Calls & Tokens)

### 1.1 Prefer one model and shorter prompt

- **Change**: Use **one primary model** (e.g. `gemini-1.5-flash`) for both PDF and image analysis. Only try next model on **hard failure**, not on rate limit (rate limit → backoff + retry same model).
- **Reason**: Cascade through 4–5 models burns through quota; one model + retry is cheaper and more predictable.
- **Files**: `gemini_pdf_analysis.ts`, `gemini_analysis.ts`.

### 1.2 Reduce tokens per request

- **PDF**: Shorten `systemInstruction` and `prompt` to the minimal rules (externalId digits, fuelType Cyrillic, one line on qrCodeData). Drop lengthy “CRITICAL RULES” paragraphs if a single short paragraph achieves the same.
- **Image (fallback)**: Shorten prompt to: extract list of vouchers with provider, fuelType, amount, expirationDate, externalId; “Return JSON array only.”
- **Structured output**: Keep `responseMimeType: "application/json"` and schema for PDF to avoid parsing flakiness and extra tokens.

### 1.3 Don’t ask Gemini for `qrCodeData` in PDF path

- **Change**: Remove `qrCodeData` from the PDF response schema; we always overwrite with local QR scan. Let Gemini return only: provider, fuelType, amount, expirationDate, externalId.
- **Reason**: Saves output tokens and reduces OCR errors on QR content; single source of truth (local scan) is better.

### 1.4 Batch images in fallback (optional, API permitting)

- **Change**: When falling back to page-by-page, send **multiple page images in one request** (e.g. 2–4 pages) with prompt: “Extract all vouchers from all attached images. Return one JSON array for all.”
- **Reason**: Cuts Gemini calls from N to N/2 or N/4; check Gemini docs for multi-image request limits and token caps.
- **Files**: `import.service.ts` (collect page buffers), `gemini_analysis.ts` or new `gemini_batch_analysis.ts`.

---

## Phase 2: Parallelism and Fewer Round-Trips

### 2.1 Run Gemini PDF and QR scan in parallel

- **Change**: For each PDF, call `analyzePdfWithGemini(pdfBuffer)` and `scanQrsFromPdf(pdfBuffer, provider)` in parallel (e.g. `Promise.all`). Then run matching and persist as today.
- **Reason**: QR scan is CPU-bound and independent of Gemini; overlapping saves wall time.
- **File**: `import.service.ts` in `processPdf` (before matching).

### 2.2 Process multiple files concurrently (with cap)

- **Change**: Process files in **batches of 2–4** (configurable, e.g. `IMPORT_CONCURRENCY=3`). For each file, run PDF-or-image pipeline; collect results; then persist.
- **Reason**: Hides latency of Gemini/QR when multiple files are present; cap avoids rate limits and memory spikes.
- **File**: `import.service.ts` in `executeJob`: replace `for (const file of files)` with `pLimit` or a simple pool (e.g. 3 at a time).

### 2.3 Batch persist and single event per file/batch

- **Master data**: For each file (or batch of vouchers), collect unique `(provider, fuelType, liters)`. Call `ensureMasterData` once per unique triple (in parallel if desired), then map to canonical fuel name for all vouchers of that triple.
- **Vouchers**: Use a **bulk insert** (e.g. `vouchersRepository.createVouchers(vouchers[])`) if the repo supports it; otherwise keep sequential insert but avoid redundant work.
- **Events**: Emit **one** `VOUCHERS_IMPORTED` per (provider, fuelType, liters) with `count` = number of vouchers of that type, instead of one event per voucher.
- **Reason**: Fewer DB round-trips and fewer event publishes; fulfillment logic already supports `count`.
- **Files**: `import.service.ts`, `vouchers.repository.ts` (add bulk create if missing), `fulfillment.service.ts` (already handles count).

### 2.4 Checkpoint less often

- **Change**: Update `importRepository.updateImportJob` every **N files** (e.g. 5) or every **N vouchers** (e.g. 50), not after every file.
- **Reason**: Fewer DB writes during import; UI can still poll every 2s.
- **File**: `import.service.ts` in `executeJob`.

---

## Phase 3: Robustness and Validation

### 3.1 Validate Gemini output before persist

- **Change**: After parsing JSON from Gemini, validate each item: `externalId` non-empty and numeric part present, `amount` number, `expirationDate` parseable or null. Log and skip invalid rows; don’t fail whole file.
- **Files**: `gemini_pdf_analysis.ts`, `gemini_analysis.ts` (or a shared `validateVoucherRow` in import.service).

### 3.2 Idempotent master data

- **Change**: Ensure `ensureMasterData` remains idempotent (find-or-create). Consider caching in-memory per job: “already ensured” set for (provider, fuelType, liters) to avoid repeated DB lookups within same import.
- **File**: `auto-master-data.service.ts` and/or `import.service.ts` (cache Set per job).

### 3.3 Retry and backoff

- **Keep**: Existing retry with exponential backoff for 429 / RESOURCE_EXHAUSTED on Gemini.
- **Optional**: On “all models failed” for PDF, retry the **first** model once after 30s before falling back to page-by-page (avoids transient failures burning fallback path).

---

## Phase 4: Optional / Later

- **Caching**: If the same PDF is re-imported (e.g. retry), consider a short-lived cache keyed by hash of first 64KB to skip re-analysis (optional, only if duplicates are common).
- **Streaming status**: WebSocket or SSE for import-status instead of polling (better UX, not required for “speed” of import itself).
- **Background worker**: Move import to a separate process/worker to avoid blocking the API (already “fire and forget” via queue; ensure worker has enough CPU for QR scan).

---

## Implementation Order

| # | Task | Impact | Effort |
|---|------|--------|--------|
| 1 | Single-model-first + backoff (no cascade) | High (economy) | Low |
| 2 | Shorten prompts + drop qrCodeData from PDF schema | High (economy) | Low |
| 3 | Parallel Gemini PDF + QR scan | High (speed) | Low |
| 4 | File-level concurrency (cap 2–4) | High (speed) | Medium |
| 5 | Batch ensureMasterData + one event per (provider, fuelType, liters) | Medium (speed + robustness) | Medium |
| 6 | Checkpoint every N files | Low (speed) | Low |
| 7 | Validate Gemini output before persist | Medium (robustness) | Low |
| 8 | Optional: batch multiple page images in one Gemini call (fallback) | Medium (economy) | Medium |

---

## Success Metrics

- **Gemini**: Fewer requests per 100 vouchers (target: 1 request per PDF when PDF path succeeds; in fallback, aim for 1 request per 2–4 pages if batching).
- **Time**: Lower end-to-end time for a fixed set of files (e.g. 10 PDFs) due to parallelism and fewer round-trips.
- **Robustness**: No increase in failed imports; duplicate and invalid rows skipped with clear logs.

This plan keeps the current architecture (PDF-first, then page fallback, local QR as source of truth) while making imports faster and cheaper.
