/**
 * Period selection for the acquirer statement act (#118, admin surface).
 *
 * The backend takes a merchant plus a month, or an explicit UTC range. The month form is the one
 * an operator actually wants - a reconciliation act is signed off per calendar month - and the
 * range form exists for the awkward cases (a merchant that went live mid-month, a re-export after
 * a correction), so both are supported rather than pretending the month form covers everything.
 *
 * Kept out of the component so the boundaries can be unit-tested: a month range that silently
 * starts an hour late is invisible on screen and quietly wrong on a document that gets signed.
 */

export const STATEMENT_MERCHANTS = ["live", "sandbox"] as const;
export type StatementMerchant = (typeof STATEMENT_MERCHANTS)[number];

export interface StatementPeriod {
  merchant: StatementMerchant;
  /** 1-12. */
  month: number;
  /** Full year, e.g. 2026. */
  year: number;
}

/**
 * Defaults to the previous calendar month. The current month is always wrong to reconcile: it is
 * still accruing statement rows, so the act would disagree with tomorrow's data and get signed off.
 */
export function defaultStatementPeriod(
  now: Date = new Date(),
): StatementPeriod {
  const previous = new Date(
    Date.UTC(now.getUTCFullYear(), now.getUTCMonth() - 1, 1),
  );
  return {
    merchant: "live",
    month: previous.getUTCMonth() + 1,
    year: previous.getUTCFullYear(),
  };
}

/**
 * The half-open UTC range covering the month: the first instant of the month to the first instant
 * of the next. Half-open because "the last millisecond of the month" is a boundary nobody wants to
 * reason about, and a closed range double-counts nothing but invites an off-by-one.
 */
export function statementRange(period: StatementPeriod): {
  fromUtc: Date;
  toUtc: Date;
} {
  const fromUtc = new Date(Date.UTC(period.year, period.month - 1, 1, 0, 0, 0));
  const toUtc = new Date(Date.UTC(period.year, period.month, 1, 0, 0, 0));
  return { fromUtc, toUtc };
}

/** The query string the API takes. Explicit range, so the act's own period is what is requested. */
export function statementQuery(period: StatementPeriod): string {
  const { fromUtc, toUtc } = statementRange(period);
  return new URLSearchParams({
    merchant: period.merchant,
    fromUtc: fromUtc.toISOString(),
    toUtc: toUtc.toISOString(),
  }).toString();
}

/** `2026-10` - the shape the download filenames use, so a label matches the file on disk. */
export function statementPeriodLabel(period: StatementPeriod): string {
  return `${period.year}-${String(period.month).padStart(2, "0")}`;
}

/** The years worth offering: the current one, plus the two before it. */
export function statementYears(now: Date = new Date()): number[] {
  const year = now.getUTCFullYear();
  return [year, year - 1, year - 2];
}
