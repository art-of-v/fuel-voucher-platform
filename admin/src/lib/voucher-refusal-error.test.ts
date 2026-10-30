import { describe, expect, it } from "vitest";
import { ApiError } from "./api-client";
import { voucherRefusalMessage } from "./voucher-refusal-error";

/**
 * Archiving a paid voucher is refused on purpose, and every operator meets that refusal. The
 * tests are about the mapping — which numbers and names reach the sentence — not about the wording
 * of four locale files.
 */

// A translator that shows what was asked for and with what, instead of pinning the prose.
const t = (key: string, ...params: (string | number)[]) =>
  params.length ? `${key}[${params.join("|")}]` : key;

describe("voucherRefusalMessage", () => {
  it("names the voucher that stopped the archive and what is wrong with it", () => {
    const error = new ApiError(
      "Voucher V-123 cannot be deleted: it has already been used.",
      "vouchers_not_deletable",
      { refusedCount: 3, totalCount: 50 },
      undefined,
      { voucherNumber: "V-123", gap: "used" },
    );

    expect(voucherRefusalMessage(error, t)).toBe(
      "vouchers.error.notDeletable[3|50|V-123|vouchers.error.gapUsed]",
    );
  });

  it("says what a 'delete all' did and what it kept", () => {
    const error = new ApiError(
      "Archived 47 voucher(s), kept 3.",
      "voucher_archive_partial",
      { archivedCount: 47, keptCount: 3 },
    );

    expect(voucherRefusalMessage(error, t)).toBe(
      "vouchers.error.archivePartial[47|3]",
    );
  });

  it("says the manual-assign refusal in its own words", () => {
    const error = new ApiError(
      "Manual assign is disabled",
      "manual_assign_disabled",
    );

    expect(voucherRefusalMessage(error, t)).toBe(
      "vouchers.error.assignDisabled",
    );
  });

  it("keeps the server's sentence when the voucher or its gap did not arrive", () => {
    // An older backend sends the code and the English sentence and nothing between them. A
    // localised sentence with a hole where the voucher's number should be reads worse.
    const noExtra = new ApiError(
      "Voucher V-123 cannot be deleted",
      "vouchers_not_deletable",
      { refusedCount: 3, totalCount: 50 },
    );
    const noCounts = new ApiError(
      "Voucher V-123 cannot be deleted",
      "vouchers_not_deletable",
      undefined,
      undefined,
      { voucherNumber: "V-123", gap: "used" },
    );

    expect(voucherRefusalMessage(noExtra, t)).toBe(
      "Voucher V-123 cannot be deleted",
    );
    expect(voucherRefusalMessage(noCounts, t)).toBe(
      "Voucher V-123 cannot be deleted",
    );
  });

  it("does not invent a clause for a gap token it has not learnt", () => {
    const error = new ApiError(
      "Voucher V-123 cannot be deleted",
      "vouchers_not_deletable",
      { refusedCount: 1, totalCount: 1 },
      undefined,
      { voucherNumber: "V-123", gap: "something_new_upstream" },
    );

    expect(voucherRefusalMessage(error, t)).toBe(
      "Voucher V-123 cannot be deleted",
    );
  });

  it("keeps the server's sentence for a code it has not learnt", () => {
    const error = new ApiError("Some other refusal", "not_a_code_we_know");

    expect(voucherRefusalMessage(error, t)).toBe("Some other refusal");
  });

  it("reads a plain error as its message", () => {
    expect(voucherRefusalMessage(new Error("Network is down"), t)).toBe(
      "Network is down",
    );
  });
});
