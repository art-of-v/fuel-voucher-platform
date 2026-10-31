import { describe, expect, it } from "vitest";
import { ApiError } from "./api-client";
import { redemptionRefusalMessage } from "./redemption-refusal-error";

/**
 * Every refusal on the redemption screen is met by an operator with a pasted statement in hand,
 * so the panel words them rather than printing the server's English. The tests are about the
 * mapping — code to sentence — not about the prose in four locale files.
 */

// A translator that shows what was asked for, instead of pinning the wording.
const t = (key: string, ...params: (string | number)[]) =>
  params.length ? `${key}[${params.join("|")}]` : key;

describe("redemptionRefusalMessage", () => {
  it("says an empty statement in the panel's words", () => {
    const error = new ApiError(
      "The statement has no lines.",
      "statement_empty",
    );

    expect(redemptionRefusalMessage(error, t)).toBe(
      "redemption.error.statementEmpty",
    );
  });

  it("says a refusal to dismiss a line with a live claim", () => {
    const error = new ApiError(
      "Statement line 7 is attached to a pending claim: confirm or reject the claim.",
      "statement_line_has_pending_claim",
    );

    expect(redemptionRefusalMessage(error, t)).toBe(
      "redemption.error.lineHasPendingClaim",
    );
  });

  it("keeps the server's sentence for a code it has not learnt", () => {
    const error = new ApiError(
      "Something the panel does not know yet",
      "statement_line_reordered",
    );

    expect(redemptionRefusalMessage(error, t)).toBe(
      "Something the panel does not know yet",
    );
  });

  it("reads a plain error as its message", () => {
    expect(redemptionRefusalMessage(new Error("Network is down"), t)).toBe(
      "Network is down",
    );
  });
});
