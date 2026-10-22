import { describe, it, expect } from "vitest";

import { feedbackBotTone } from "./SettingsTab";

/**
 * The tone is the only thing telling an operator whether the channel is healthy.
 *
 * The first version computed it inline and ended in `"ok"` — not a tone `StateBanner` has. It does not
 * crash: the component's ternary falls through to the warning colour, so an **enabled** channel
 * rendered in amber next to genuinely broken ones, and "on" was indistinguishable from "needs
 * attention". TypeScript rejects `"ok"` but `vite build` does not type-check, so nothing did.
 *
 * This test is the cheap guard for that specific slip; the `typecheck` script added to the admin CI
 * job is the one that catches the whole class.
 */
describe("feedbackBotTone", () => {
  it("reports an enabled, configured channel as on", () => {
    expect(feedbackBotTone(true, true)).toBe("on");
  });

  it("reports a deliberately switched-off channel as the neutral tone, not a fault", () => {
    expect(feedbackBotTone(true, false)).toBe("off");
  });

  it("outranks the toggle when Telegram is not configured at all", () => {
    // The order matters: the toggle is honoured while the credentials are missing (an operator can
    // stage the decision), but the card must still say "this cannot work" rather than "on".
    expect(feedbackBotTone(false, true)).toBe("danger");
    expect(feedbackBotTone(false, false)).toBe("danger");
  });

  it("only ever returns a tone StateBanner actually accepts", () => {
    const allowed = new Set(["on", "off", "danger"]);

    for (const configured of [true, false]) {
      for (const enabled of [true, false]) {
        expect(allowed.has(feedbackBotTone(configured, enabled))).toBe(true);
      }
    }
  });
});
