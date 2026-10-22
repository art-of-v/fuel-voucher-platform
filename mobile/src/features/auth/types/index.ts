export type AuthStep = 'phone' | 'code' | 'two_factor' | 'security_setup' | 'success';

export interface AuthResponse {
  /**
   * The session's tokens. They are null when the sign-in is not finished: a staff account
   * has to prove a second factor, and the first factor alone mints nothing.
   */
  accessToken: string | null;
  refreshToken: string | null;
  /**
   * One-time proof issued with this login. Lets the device register step
   * rebind a device_id that another account previously enrolled (phone
   * changed hands, reinstall on a shared device). Missing/empty = rebinding
   * unavailable this session.
   */
  deviceRegistrationNonce?: string;
  /**
   * The outstanding second factor, when the account is a staff one. The challenge is what
   * turns the code from the authenticator app into a session; the secret is what the
   * account has to enrol when it has none yet.
   */
  twoFactor?: {
    status: 'required' | 'enroll';
    challenge: string;
    enrollmentSecret?: string | null;
    provisioningUri?: string | null;
  } | null;
}
