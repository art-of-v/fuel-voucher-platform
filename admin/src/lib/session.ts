import {
  REFRESH_RATE_LIMIT_FALLBACK_SECONDS,
  clearTokens,
  fetchCurrentUser,
  getStoredAccessToken,
  refreshAccessToken,
  sendCode,
  verifyCode,
  verifyTwoFactor,
  logout as revokeSessionOnServer,
  type CurrentUser,
  type RefreshResult,
} from "./admin-auth";
import { hasSessionMarker, useSession } from "./session-store";

/**
 * Orchestration for the session lifecycle.
 *
 * <p>
 * The rule this file exists to enforce: <strong>the client spends a refresh from
 * the server's budget only when there is a reason to believe a session might
 * exist.</strong> Everything else - the login screen, a 401 on a request that had
 * no token to begin with, a retry button - must be answerable without touching
 * `/api/auth/refresh`.
 * </p>
 */

/** Applies a refresh outcome to session state. Shared by boot and 401 recovery. */
async function applyRefresh({
  status,
  retryAfterSeconds,
}: RefreshResult): Promise<void> {
  const store = useSession.getState();

  switch (status) {
    case "refreshed": {
      try {
        const user = await fetchCurrentUser();
        store.authenticate(user);
      } catch (err) {
        // The token is live but /user/me failed. A 401 here means the refresh minted a token the
        // API will not accept, which is a real sign-out. Anything else is the backend's problem
        // and must not cost the operator their session.
        if (isRejected(err)) {
          store.signOut();
        } else {
          store.block({ reason: "api_unreachable" });
        }
      }
      return;
    }

    case "expired":
      store.signOut();
      return;

    case "rate_limited":
      store.block({
        reason: "rate_limited",
        retryAt:
          Date.now() +
          (retryAfterSeconds || REFRESH_RATE_LIMIT_FALLBACK_SECONDS) * 1000,
      });
      return;

    case "unavailable":
      store.block({ reason: "api_unreachable" });
      return;
  }
}

function isRejected(err: unknown): boolean {
  return (
    typeof err === "object" &&
    err !== null &&
    (err as { status?: number }).status === 401
  );
}

/**
 * Decide what to do on page load.
 *
 * <p>
 * With no session marker this resolves to "anonymous" <em>without any network
 * call</em>. That is the entire fix for the lockout: signing in no longer has to
 * compete with page loads by people who were never signed in, so it always works.
 * </p>
 */
export async function bootSession(): Promise<void> {
  const store = useSession.getState();

  if (!hasSessionMarker()) {
    store.signOut();
    return;
  }

  await applyRefresh(await refreshAccessToken());
}

/**
 * The one place allowed to turn a 401 into a refresh.
 *
 * <p>
 * Returns the token to retry with, or `null` when there is no session to retry
 * with. Two guards keep this from becoming a storm:
 * </p>
 * <ul>
 *   <li>If the session is already known to be signed out, return immediately. A second 401
 *       after a sign-out carries no new information, and refreshing on it is what turned a
 *       dead session into an endless loop that also burned the rate-limit budget.</li>
 *   <li>`refreshAccessToken` is single-flight, so N concurrent 401s produce exactly one
 *       request.</li>
 * </ul>
 */
export async function ensureAccessToken(): Promise<string | null> {
  const { status } = useSession.getState();
  if (status === "anonymous") return null;

  await applyRefresh(await refreshAccessToken());
  return useSession.getState().status === "authenticated"
    ? getStoredAccessToken()
    : null;
}

/**
 * What a staff sign-in produced.
 *
 * <p>
 * `signed_in` - the session is up.
 * `two_factor` - the first factor was proven and the second one is outstanding: the backend
 * answered with a challenge instead of a token, and the sign-in continues at the challenge.
 * </p>
 */
export type SignInOutcome =
  | { status: "signed_in"; user: CurrentUser }
  | {
      status: "two_factor";
      challenge: string;
      /** True when the account has no secret yet and one has to be enrolled. */
      needsEnrollment: boolean;
      enrollmentSecret?: string | null;
      provisioningUri?: string | null;
    };

/** Verify an OTP and adopt the resulting session. Returns the user so the caller can apply its
 *  own role gate without a second round trip. */
export async function signInWithCode(
  phoneNumber: string,
  code: string,
): Promise<SignInOutcome> {
  const result = await verifyCode(phoneNumber, code);

  // A staff account is not signed in by the first factor alone: the backend answers with a
  // challenge, and there is no token to adopt yet.
  if (result.twoFactor) {
    return {
      status: "two_factor",
      challenge: result.twoFactor.challenge,
      needsEnrollment: result.twoFactor.status === "enroll",
      enrollmentSecret: result.twoFactor.enrollmentSecret,
      provisioningUri: result.twoFactor.provisioningUri,
    };
  }

  const user = await fetchCurrentUser();
  useSession.getState().authenticate(user);
  return { status: "signed_in", user };
}

/** The second factor of a staff sign-in: the code from the authenticator app. */
export async function signInWithTwoFactor(
  challenge: string,
  code: string,
): Promise<CurrentUser> {
  await verifyTwoFactor(challenge, code);
  const user = await fetchCurrentUser();
  useSession.getState().authenticate(user);
  return user;
}

export { sendCode };

/**
 * Sign out locally and revoke server-side.
 *
 * <p>
 * Deliberately does <em>not</em> navigate. The old `endSession()` assigned
 * `window.location.href`, which reloaded the SPA, which re-ran the boot check,
 * which issued another refresh - burning another unit of rate-limit budget on
 * the way out of the very screen that caused the lockout.
 * </p>
 */
export async function signOut(): Promise<void> {
  const store = useSession.getState();
  try {
    await revokeSessionOnServer();
  } finally {
    clearTokens();
    store.signOut();
  }
}
