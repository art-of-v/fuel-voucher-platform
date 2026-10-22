/**
 * Reads a message out of a rejected promise.
 *
 * <p>
 * `catch (e: any)` was the admin's way of getting at `e.message` - and `any` there is exactly the
 * hole this file closes: an `any` in a catch block is unchecked, so a rejection that was not an
 * `Error` (a string, an `ApiError` with a code, a thrown object) read as `undefined` and the
 * screen said "Failed" with no reason. `unknown` forces the check.
 */
export function errorMessage(error: unknown): string {
  if (error instanceof Error) return error.message;
  if (typeof error === "string") return error;
  return String(error);
}
