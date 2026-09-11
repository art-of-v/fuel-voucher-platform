export type AuthStep = 'phone' | 'code' | 'security_setup' | 'success';

export interface AuthResponse {
  accessToken: string;
  refreshToken: string;
  /**
   * One-time proof issued with this login. Lets the device register step
   * rebind a device_id that another account previously enrolled (phone
   * changed hands, reinstall on a shared device). Missing/empty = rebinding
   * unavailable this session.
   */
  deviceRegistrationNonce?: string;
}
