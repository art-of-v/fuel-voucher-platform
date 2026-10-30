import { describe, expect, it } from "vitest";
import { ApiError } from "./api-client";
import { codedErrorMessage, type CodedSentence } from "./coded-error-message";

/**
 * The one rule every screen with codes leans on: a code it has learnt is written in the panel's
 * language, and everything else keeps the server's sentence. The fallback is the half that matters
 * — it is what makes adding a refusal on the backend safe for a client that has not shipped yet.
 */

// A translator that shows what was asked for, instead of pinning four locale files to a unit test.
const t = (key: string, ...params: (string | number)[]) =>
  params.length ? `${key}[${params.join("|")}]` : key;

const sentences: Record<string, CodedSentence> = {
  known: (error, translate) =>
    translate("some.sentence", "x", error.data?.n ?? -1),
  payload_missing: () => undefined,
};

describe("codedErrorMessage", () => {
  it("builds the sentence of a code it has learnt", () => {
    const error = new ApiError("server words", "known", { n: 3 });

    expect(codedErrorMessage(error, t, sentences)).toBe("some.sentence[x|3]");
  });

  it("keeps the server's sentence for a code it has not learnt", () => {
    const error = new ApiError("server words", "nobody_has_taught_this_one");

    expect(codedErrorMessage(error, t, sentences)).toBe("server words");
  });

  it("keeps the server's sentence when the payload a sentence needs did not arrive", () => {
    const error = new ApiError("server words", "payload_missing");

    expect(codedErrorMessage(error, t, sentences)).toBe("server words");
  });

  it("keeps the server's sentence when the failure carries no code at all", () => {
    const error = new ApiError("server words");

    expect(codedErrorMessage(error, t, sentences)).toBe("server words");
  });

  it("reads a failure that is not an ApiError as its message", () => {
    expect(codedErrorMessage(new Error("Network is down"), t, sentences)).toBe(
      "Network is down",
    );
    expect(codedErrorMessage("a thrown string", t, sentences)).toBe(
      "a thrown string",
    );
  });
});
