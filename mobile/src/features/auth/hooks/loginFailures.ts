import { DeviceSecurityError } from '../../../core/api/securityService';
import { reportError } from '../../../core/observability/sentry';

/**
 * The sentence the sign-in screen shows when a step fails, one function per step.
 *
 * <p>
 * Three different things can go wrong on the way in - the first-factor code, the authenticator
 * code, the device binding - and each used to be reported with whichever message the shared
 * helper happened to have. Worse, a refused code was reported as "could not confirm the
 * device": it named the phone instead of the six digits the person had just typed. These stay
 * apart so each step can only say what it knows.
 * </p>
 *
 * <p>
 * None of these ever return a raw error string: a 429 is the rate limit, a device-security
 * error has its own copy, and anything else is forwarded to Sentry behind a localized message.
 * </p>
 */

/**
 * The failure the user sees at the second factor.
 *
 * <p>
 * The second factor has its own mistakes: the code from the authenticator app can be wrong,
 * and the challenge can be spent. Both used to be reported as "could not confirm the device",
 * which pointed at a device problem instead of the code the person had just typed - and pushed
 * people to retype the push code that the first factor had already accepted.
 * </p>
 *
 * <p>
 * Only the code's own step is described here. The device binding that follows it is a separate
 * step with its own messages (see {@link describeFailure}).
 * </p>
 */
export function describeTwoFactorFailure(
  err: any,
  t: (key: string) => string,
  logs: string[],
): string {
  logs.push(`ERROR: ${err?.message}`);
  if (__DEV__) {
    // The diagnostic trail is the developer's window into the failure - and it is the only
    // place the raw cause appears at all.
    return logs.join('\n');
  }
  if (err?.status === 429) {
    return t('phoneAuth.tooManyAttempts');
  }
  if (err?.status === 401 || err?.status === 400) {
    // The server refuses a wrong code, a spent challenge and an expired one with the same
    // 401 - on purpose, so the endpoint cannot be used to tell them apart. To the person at
    // the screen the outcome is the same anyway: the code they entered was not accepted.
    return t('phoneAuth.twoFactorCodeRejected');
  }
  // Anything else is the network's or the server's fault - never the person's code.
  reportError(err);
  return t('phoneAuth.networkError');
}

/**
 * The failure the user sees at the first factor.
 *
 * <p>
 * The code that arrives by push or SMS is refused with a 401 - and it used to be reported as
 * "could not confirm the device", which names the device instead of the six digits the person
 * typed.
 * </p>
 *
 * <p>
 * The server keeps one indistinguishable 401 for a wrong, spent and expired code on purpose, so
 * the message cannot come from the wire. It comes from the clock the screen already keeps: when
 * the deadline the server published has passed, the digits were not mistyped - they were late -
 * and the message says that instead of blaming the person.
 * </p>
 */
export function describeCodeFailure(
  err: any,
  t: (key: string) => string,
  logs: string[],
  expired: boolean,
): string {
  logs.push(`ERROR: ${err?.message}`);
  if (__DEV__) {
    // The diagnostic trail is the developer's window into the failure - and it is the only
    // place the raw cause appears at all.
    return logs.join('\n');
  }
  if (err?.status === 429) {
    return t('phoneAuth.tooManyAttempts');
  }
  if (err?.status === 401 || err?.status === 400) {
    return expired ? t('phoneAuth.codeExpired') : t('phoneAuth.verifyFailed');
  }
  // Anything else is the network's or the server's fault - never the person's code.
  reportError(err);
  return t('phoneAuth.networkError');
}

/**
 * The failure the user sees.
 *
 * <p>
 * Never a raw error string: a 429 is the rate limit, a device-security error has its own
 * messages, and anything else is the network's fault - the real cause goes to Sentry.
 * </p>
 */
export function describeFailure(err: any, t: (key: string) => string, logs: string[]): string {
  logs.push(`ERROR: ${err?.message}`);
  if (__DEV__) {
    // The diagnostic trail is the developer's window into the failure - and it is the only
    // place the raw cause appears at all.
    return logs.join('\n');
  }
  if (err?.status === 429) {
    return t('phoneAuth.tooManyAttempts');
  }
  if (err instanceof DeviceSecurityError) {
    if (err.code === 'DEVICE_AUTH_UNAVAILABLE') {
      // The user can fix this themselves — tell them how, don't page Sentry.
      return t('phoneAuth.biometricsRequired');
    }
    if (err.code === 'SIGNING_CANCELLED') {
      // The user dismissed the authentication prompt; expected, not worth paging Sentry.
      return t('phoneAuth.deviceVerifyFailed');
    }
    // Unexpected Keychain/native failure (e.g. -25293, or signing still failing after the
    // passcode-fallback retry). Show a clean message; keep the raw native cause for triage
    // instead of leaking the OSStatus to the customer.
    reportError(err);
    return t('phoneAuth.deviceVerifyFailed');
  }
  // Any other failure (network/server/unexpected). Never surface its raw message: forward
  // the real cause to Sentry and show a localized fallback.
  reportError(err);
  return t('phoneAuth.deviceVerifyFailed');
}
