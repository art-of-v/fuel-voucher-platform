import { apiFetch } from './apiClient';
import { SecurityService } from './securityService';
import { deregisterPushToken } from '../../features/notifications/api/pushTokensApi';

export async function logout(): Promise<void> {
  // Deregister this device's push token FIRST: it relies on the bearer and x-device-id that
  // the steps below tear down (revokeSecurity clears the device id and tokens). Best-effort.
  await deregisterPushToken();

  try {
    await apiFetch('/api/auth/device/logout', { method: 'POST' });
  } catch (error) {
    console.warn('Logout server notify failed:', error);
  }
  await SecurityService.revokeSecurity();
}
