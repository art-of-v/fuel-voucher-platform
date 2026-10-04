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

  it("keeps tracing disabled (errors only)", () => {
    vi.stubEnv("VITE_SENTRY_DSN", "https://key@o1.ingest.sentry.io/42");

    initSentry();

    expect(Sentry.init).toHaveBeenCalledWith(
      expect.objectContaining({
        dsn: "https://key@o1.ingest.sentry.io/42",
        tracesSampleRate: 0,
      }),
    );
  });

  it("passes no option that Sentry v11 no longer accepts", () => {
    // v11 removed `sendDefaultPii` outright; passing it is a type error and a
    // silent runtime no-op. Regression guard for the upgrade, and for anyone
    // copying options between the backend and admin SDKs - they are different
    // major versions and the browser SDK does not take the server SDK's options.
    vi.stubEnv("VITE_SENTRY_DSN", "https://key@o1.ingest.sentry.io/42");

    initSentry();

    const options = vi.mocked(Sentry.init).mock.calls[0][0] as Record<string, unknown>;
    expect(options).not.toHaveProperty("sendDefaultPii");
    expect(options).not.toHaveProperty("replaysSessionSampleRate");
  });
});
