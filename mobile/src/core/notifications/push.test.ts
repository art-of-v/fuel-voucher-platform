import * as Notifications from 'expo-notifications';
import Constants from 'expo-constants';
import { Platform } from 'react-native';
import { registerForPushNotifications } from './push';
import { registerPushToken } from '../../features/notifications/api/pushTokensApi';

// The native module cannot load under Jest, so mock the SDK surface we call.
// Mirrors the mocking style of sentry.test.ts.
jest.mock('expo-notifications', () => ({
  getPermissionsAsync: jest.fn(),
  requestPermissionsAsync: jest.fn(),
  getExpoPushTokenAsync: jest.fn(),
}));

// Mutable mock: tests clear extra.eas.projectId to exercise the missing-id path,
// same shape as app.json (expo.extra.eas.projectId).
jest.mock('expo-constants', () => ({
  __esModule: true,
  default: { expoConfig: { extra: { eas: { projectId: 'test-project-id' } } } },
}));

jest.mock('../../features/notifications/api/pushTokensApi', () => ({
  registerPushToken: jest.fn(),
}));

const mockedExtra = (
  Constants as unknown as { expoConfig: { extra: { eas: { projectId: string } } } }
).expoConfig.extra;

const getPermissions = Notifications.getPermissionsAsync as jest.Mock;
const requestPermissions = Notifications.requestPermissionsAsync as jest.Mock;
const getToken = Notifications.getExpoPushTokenAsync as jest.Mock;
const backendRegister = registerPushToken as jest.Mock;

const TOKEN = 'ExponentPushToken[abc123]';

describe('registerForPushNotifications', () => {
  beforeEach(() => {
    jest.clearAllMocks();
    mockedExtra.eas.projectId = 'test-project-id';
    getPermissions.mockResolvedValue({ status: 'granted' });
    requestPermissions.mockResolvedValue({ status: 'granted' });
    getToken.mockResolvedValue({ data: TOKEN });
    backendRegister.mockResolvedValue(undefined);
  });

  it('mints the token and registers it with the backend when already granted', async () => {
    const result = await registerForPushNotifications();

    expect(result).toBe(TOKEN);
    expect(requestPermissions).not.toHaveBeenCalled();
    expect(getToken).toHaveBeenCalledWith({ projectId: 'test-project-id' });
    expect(backendRegister).toHaveBeenCalledWith(TOKEN, Platform.OS);
  });

  it('requests permission when it is not already granted', async () => {
    getPermissions.mockResolvedValue({ status: 'undetermined' });

    const result = await registerForPushNotifications();

    expect(requestPermissions).toHaveBeenCalledTimes(1);
    expect(result).toBe(TOKEN);
  });

  it('returns null and never mints a token when permission is denied', async () => {
    getPermissions.mockResolvedValue({ status: 'denied' });
    requestPermissions.mockResolvedValue({ status: 'denied' });

    const result = await registerForPushNotifications();

    expect(result).toBeNull();
    expect(getToken).not.toHaveBeenCalled();
    expect(backendRegister).not.toHaveBeenCalled();
  });

  it('returns null when no EAS projectId is configured', async () => {
    mockedExtra.eas.projectId = '';

    const result = await registerForPushNotifications();

    expect(result).toBeNull();
    expect(getToken).not.toHaveBeenCalled();
  });

  it('swallows a native minting failure (e.g. simulator) and returns null', async () => {
    getToken.mockRejectedValue(new Error('no APNs token on simulator'));

    const result = await registerForPushNotifications();

    expect(result).toBeNull();
    expect(backendRegister).not.toHaveBeenCalled();
  });

  it('swallows a backend failure and returns null', async () => {
    backendRegister.mockRejectedValue(new Error('backend down'));

    const result = await registerForPushNotifications();

    expect(result).toBeNull();
  });

  it('de-dupes concurrent calls into a single permission prompt', async () => {
    const [a, b] = await Promise.all([
      registerForPushNotifications(),
      registerForPushNotifications(),
    ]);

    expect(a).toBe(TOKEN);
    expect(b).toBe(TOKEN);
    expect(getPermissions).toHaveBeenCalledTimes(1);
  });
});
