import { describe, expect, it } from "vitest";
import fs from "node:fs";
import path from "node:path";
import { fileURLToPath } from "node:url";
import { translations } from "./i18n";

/**
 * The direction `i18n-parity.test.ts` cannot see: a key the code asks for that no locale defines.
 *
 * <p>
 * Parity compares the four locales to each other, so a sentence the screens name but nobody added
 * is missing from all four at once and every assertion still passes. `t()` then prints the raw
 * key, and a live-stock refusal reached the operator as "fuel.error.liveStockNeedsMargin".
 *
 * <p>
 * The test scans the admin's sources for literal `t("…")` calls - the one form this codebase
 * uses - and checks each key against the English block, the baseline the other three are checked
 * against. Dynamic keys are invisible here by construction; the point is that a hand-typed key,
 * which is how every refusal sentence is wired, cannot be a typo that ships.
 */

const adminSrc = path.resolve(
  path.dirname(fileURLToPath(import.meta.url)),
  "..",
);

function sourceFiles(dir: string): string[] {
  return fs.readdirSync(dir, { withFileTypes: true }).flatMap((entry) => {
    const full = path.join(dir, entry.name);
    if (entry.isDirectory()) {
      return entry.name === "node_modules" ? [] : sourceFiles(full);
    }
    if (!/\.(ts|tsx)$/.test(entry.name) || /\.test\.tsx?$/.test(entry.name)) {
      return [];
    }
    return [full];
  });
}

const CALL = /\bt\(\s*(?:"([^"\\\n]+)"|'([^'\\\n]+)')\s*[,)]/g;

describe("translation references", () => {
  it("names only keys every locale defines", () => {
    const offenders: { key: string; file: string }[] = [];

    for (const file of sourceFiles(adminSrc)) {
      const text = fs.readFileSync(file, "utf8");
      for (const match of text.matchAll(CALL)) {
        const key = match[1] ?? match[2];
        if (translations.en[key] === undefined) {
          offenders.push({
            key,
            file: path.relative(adminSrc, file).replaceAll("\\", "/"),
          });
        }
      }
    }

    expect(offenders).toEqual([]);
  });
});
