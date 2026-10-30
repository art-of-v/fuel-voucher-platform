import { ApiError } from "@/lib/api-client";
import { errorMessage } from "@/lib/error-message";

type Translator = (key: string, ...params: (string | number)[]) => string;

/**
 * Writes the sentence for one code, in the panel's language.
 *
 * Returns `undefined` when the payload the sentence needs did not arrive — an older backend sends
 * the code and the English sentence and nothing between them. A localised sentence with a hole in
 * it reads worse than the server's true one.
 */
export type CodedSentence = (
  error: ApiError,
  t: Translator,
) => string | undefined;

/**
 * The sentence for a refused action, built from the code the API sent.
 *
 * The API answers every failure in the same envelope, and a refusal that a screen can act on also
 * carries a stable machine code beside its English sentence (`detail`). The panel is localized into
 * four languages, so each screen registers the codes it can say in its own words; anything it has
 * not learnt — a new refusal, an upstream proxy's body, a plain `Error` — falls back to the text
 * the server sent, which is what every screen showed before this existed.
 *
 * The registration is a parameter rather than a switch inside this file so a screen owns the
 * sentences it can word and the next added code cannot silently print English on a Ukrainian panel
 * without someone writing a key for it.
 */
export function codedErrorMessage(
  error: unknown,
  t: Translator,
  sentences: Record<string, CodedSentence>,
): string {
  if (!(error instanceof ApiError) || !error.code) return errorMessage(error);

  const sentence = sentences[error.code];
  if (!sentence) return errorMessage(error);

  return sentence(error, t) ?? errorMessage(error);
}
