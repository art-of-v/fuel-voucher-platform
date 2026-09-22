import { describe, it, expect, vi, beforeEach, afterEach } from "vitest";
import * as Sentry from "@sentry/react";
import { initSentry } from "./sentry";

// Guards the "inert without a DSN" contract: with VITE_SENTRY_DSN unset the SDK must
// never initialise, so a build without the env var (local dev, tests, or a deploy that
// left it blank) ships nothing to Sentry. Mirrors backend SentryOptionsTests.cs.
vi.mock("@sentry/react", () => ({
  init: vi.fn(),
}));

describe("initSentry", () => {
  beforeEach(() => {
    vi.mocked(Sentry.init).mockReset();
  });

  afterEach(() => {
    vi.unstubAllEnvs();
  });

  it("does not initialise Sentry when the DSN is unset", () => {
    vi.stubEnv("VITE_SENTRY_DSN", undefined);

    expect(initSentry()).toBe(false);
    expect(Sentry.init).not.toHaveBeenCalled();
  });

  it("does not initialise Sentry when the DSN is an empty string", () => {
    vi.stubEnv("VITE_SENTRY_DSN", "");

    expect(initSentry()).toBe(false);
    expect(Sentry.init).not.toHaveBeenCalled();
  });

  it("initialises Sentry when a DSN is present", () => {
    vi.stubEnv("VITE_SENTRY_DSN", "https://key@o1.ingest.sentry.io/42");

    expect(initSentry()).toBe(true);
    expect(Sentry.init).toHaveBeenCalledOnce();
  });

  it("keeps PII off and tracing disabled (errors only)", () => {
    vi.stubEnv("VITE_SENTRY_DSN", "https://key@o1.ingest.sentry.io/42");

    initSentry();

    expect(Sentry.init).toHaveBeenCalledWith(
      expect.objectContaining({
        dsn: "https://key@o1.ingest.sentry.io/42",
        sendDefaultPii: false,
        tracesSampleRate: 0,
      }),
    );
  });
});
