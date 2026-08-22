import { describe, it, expect } from "vitest";
import { parseNominals, DEFAULT_NOMINALS, localizeEventSummary, csvCell } from "./utils";

function mockT(key: string, ...params: string[]): string {
  return params.reduce((acc, p, i) => acc.replace(`{${i}}`, p), `[${key}]`);
}

describe("parseNominals", () => {
  it("returns default nominals when input is empty", () => {
    expect(parseNominals("")).toEqual(DEFAULT_NOMINALS);
    expect(parseNominals("  ")).toEqual(DEFAULT_NOMINALS);
  });

  it("falls back to defaults when input has no valid numbers", () => {
    expect(parseNominals("abc, , -, 0")).toEqual(DEFAULT_NOMINALS);
  });

  it("parses comma-separated positive integers", () => {
    expect(parseNominals("10, 20, 50")).toEqual([10, 20, 50]);
  });

  it("drops invalid entries but keeps valid ones", () => {
    expect(parseNominals("5, x, 0, 10, -3")).toEqual([5, 10]);
  });

  it("uses provided fallback when input is empty", () => {
    expect(parseNominals("", [7, 8])).toEqual([7, 8]);
  });
});

describe("localizeEventSummary", () => {
  it("localizes a fuel price-change summary with old and new values", () => {
    const t = (key: string, ...params: string[]) => `${key}:${params.join("|")}`;
    const result = localizeEventSummary("OKKO / A-95: supplier 49.00 → 100.00, margin 2.00 → 0.50", t);
    expect(result).toBe(
      "OKKO / A-95: history.changeSupplier:49.00|100.00, history.changeMargin:2.00|0.50"
    );
  });

  it("localizes provider creation", () => {
    expect(localizeEventSummary("Created provider OKKO", mockT)).toBe("[history.providerCreated]");
  });

  it("localizes nominals change", () => {
    expect(localizeEventSummary("OKKO: nominals changed [2, 3, 5]", mockT)).toBe("[history.nominalsChanged]");
  });

  it("localizes fuel added with price", () => {
    expect(localizeEventSummary("OKKO / A-95: added at 51.00 UAH/L", mockT)).toBe("[history.fuelAdded]");
  });

  it("passes through unknown summaries unchanged", () => {
    expect(localizeEventSummary("something else entirely", mockT)).toBe("something else entirely");
  });

  it("localizes a provider-scoped name change", () => {
    const t = (_key: string, ...params: string[]) => params.join("|");
    expect(localizeEventSummary("OKKO: name ttt → KLO", t)).toBe("OKKO: ttt|KLO");
  });
});

describe("csvCell", () => {
  it("quotes ordinary values and doubles embedded quotes", () => {
    expect(csvCell("hello")).toBe('"hello"');
    expect(csvCell('say "hi"')).toBe('"say ""hi"""');
    expect(csvCell(null)).toBe('""');
    expect(csvCell(undefined)).toBe('""');
  });

  it("neutralises formula-leading characters so spreadsheets treat them as text", () => {
    // The reachable case: DatabaseLoggerProvider stores Request.Path verbatim, so the
    // attacker picks this string simply by requesting that path.
    expect(csvCell("=cmd|'/c calc'!A0")).toBe(`"'=cmd|'/c calc'!A0"`);
    expect(csvCell('=HYPERLINK("http://evil")')).toBe(`"'=HYPERLINK(""http://evil"")"`);
    for (const lead of ["=", "+", "-", "@", "\t", "\r"]) {
      expect(csvCell(lead + "danger")).toBe(`"'${lead}danger"`);
    }
  });

  it("does not prefix values that merely contain those characters later", () => {
    expect(csvCell("a=b")).toBe('"a=b"');
    expect(csvCell("GET /api/orders?x=1")).toBe('"GET /api/orders?x=1"');
  });
});
