import { apiFetch } from './apiClient';
import { SecurityService } from './securityService';

export async function logout(): Promise<void> {
  try {
    await apiFetch('/api/auth/device/logout', { method: 'POST' });
  } catch (error) {
    console.warn('Logout server notify failed:', error);
  }
  await SecurityService.revokeSecurity();
}
