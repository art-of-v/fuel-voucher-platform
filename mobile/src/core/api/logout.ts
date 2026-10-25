import { apiFetch } from './apiClient';
import { SecurityService } from './securityService';
import { parkPushToken } from '../../features/notifications/api/pushTokensApi';

export async function logout(): Promise<void> {
  // Park this device's push token FIRST: it relies on the bearer and x-device-id that
  // the steps below tear down (revokeSecurity clears the device id and tokens). Best-effort.
  //
  // Parking, not deregistering: the device stops receiving the business notifications, but it
  // stays addressable for the sign-in code - a signed-out device is where the worker signs
  // back in from, and a code that cannot reach it has to be paid for by SMS.
  await parkPushToken();

  try {
    await apiFetch('/api/auth/device/logout', { method: 'POST' });
  } catch (error) {
    console.warn('Logout server notify failed:', error);
  }
  await SecurityService.revokeSecurity();
}
