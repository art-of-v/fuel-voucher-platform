export type AuthStep = 'phone' | 'code' | 'security_setup' | 'success';

export interface AuthResponse {
  accessToken: string;
  refreshToken: string;
}
