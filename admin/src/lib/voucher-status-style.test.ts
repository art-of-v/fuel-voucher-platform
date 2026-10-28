import { describe, expect, it } from "vitest";
import {
  VOUCHER_CONFIRMED_BADGE,
  VOUCHER_MARKED_BADGE,
  voucherStatusBadgeClass,
} from "./voucher-status-style";

describe("voucherStatusBadgeClass", () => {
  it("paints the two spent states on their own axis", () => {
    expect(voucherStatusBadgeClass("used")).toBe(VOUCHER_CONFIRMED_BADGE);
    expect(voucherStatusBadgeClass("pendingReconciliation")).toBe(
      VOUCHER_MARKED_BADGE,
    );
  });

  it("keeps a claim and a confirmation apart", () => {
    // They differ only by colour, so the two class strings must not collapse into one.
    expect(VOUCHER_MARKED_BADGE).not.toBe(VOUCHER_CONFIRMED_BADGE);
    expect(VOUCHER_MARKED_BADGE).toContain("voucher-marked");
    expect(VOUCHER_CONFIRMED_BADGE).toContain("voucher-confirmed");
  });

  it("leaves live stock on the theme's status tokens", () => {
    expect(voucherStatusBadgeClass("available")).toContain("success");
    expect(voucherStatusBadgeClass("assigned")).toContain("info");
  });

  it("falls back to the warning treatment for the rest", () => {
    ["sold", "deactivated", "expired", "imported"].forEach((key) =>
      expect(voucherStatusBadgeClass(key)).toContain("warning"),
    );
  });
});
