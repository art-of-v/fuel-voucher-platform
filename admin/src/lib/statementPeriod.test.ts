import { describe, it, expect } from "vitest";
import {
  defaultStatementPeriod,
  statementRange,
  statementQuery,
  statementPeriodLabel,
  statementYears,
  type StatementPeriod,
} from "./statementPeriod";

const oct2026: StatementPeriod = { merchant: "live", month: 10, year: 2026 };

describe("statement period defaults", () => {
  it("defaults to the previous calendar month, never the current one", () => {
    // The current month is still accruing rows; an act signed off against it disagrees tomorrow.
    const now = new Date("2026-10-15T12:00:00Z");
    expect(defaultStatementPeriod(now)).toEqual({
      merchant: "live",
      month: 9,
      year: 2026,
    });
  });

  it("steps back across a year boundary", () => {
    expect(defaultStatementPeriod(new Date("2026-01-10T00:00:00Z"))).toEqual({
      merchant: "live",
      month: 12,
      year: 2025,
    });
  });
});

describe("statement range", () => {
  it("is the half-open UTC month", () => {
    const { fromUtc, toUtc } = statementRange(oct2026);

    expect(fromUtc.toISOString()).toBe("2026-10-01T00:00:00.000Z");
    expect(toUtc.toISOString()).toBe("2026-11-01T00:00:00.000Z");
  });

  it("handles December without rolling into the wrong year", () => {
    const { fromUtc, toUtc } = statementRange({
      merchant: "live",
      month: 12,
      year: 2026,
    });

    expect(fromUtc.toISOString()).toBe("2026-12-01T00:00:00.000Z");
    expect(toUtc.toISOString()).toBe("2027-01-01T00:00:00.000Z");
  });

  it("handles February in a leap year, because a signed act is not the place to be off by a day", () => {
    const { toUtc } = statementRange({
      merchant: "live",
      month: 2,
      year: 2028,
    });
    expect(toUtc.toISOString()).toBe("2028-03-01T00:00:00.000Z");
  });
});

describe("statement query", () => {
  it("sends an explicit range so the requested period is unambiguous", () => {
    const query = new URLSearchParams(statementQuery(oct2026));

    expect(query.get("merchant")).toBe("live");
    expect(query.get("fromUtc")).toBe("2026-10-01T00:00:00.000Z");
    expect(query.get("toUtc")).toBe("2026-11-01T00:00:00.000Z");
  });

  it("differs between merchants, because a sandbox act and a live act are not the same document", () => {
    expect(statementQuery(oct2026)).not.toBe(
      statementQuery({ ...oct2026, merchant: "sandbox" }),
    );
  });
});

describe("statement labels", () => {
  it("zero-pads the month so it matches the download filename", () => {
    expect(
      statementPeriodLabel({ merchant: "live", month: 3, year: 2026 }),
    ).toBe("2026-03");
  });

  it("offers this year and the two before it", () => {
    expect(statementYears(new Date("2026-10-15T00:00:00Z"))).toEqual([
      2026, 2025, 2024,
    ]);
  });
});
