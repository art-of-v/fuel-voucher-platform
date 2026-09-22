import AsyncStorage from '@react-native-async-storage/async-storage';
import { SecurityService } from './securityService';
import { ensureFreshInstallReset } from './firstLaunchReset';

jest.mock('@react-native-async-storage/async-storage', () => ({
  __esModule: true,
  default: {
    getItem: jest.fn(),
    getAllKeys: jest.fn(),
    setItem: jest.fn(),
  },
}));

jest.mock('./securityService', () => ({
  SecurityService: { revokeSecurity: jest.fn() },
}));

const getItem = AsyncStorage.getItem as jest.Mock;
const getAllKeys = AsyncStorage.getAllKeys as jest.Mock;
const setItem = AsyncStorage.setItem as jest.Mock;
const revokeSecurity = SecurityService.revokeSecurity as jest.Mock;

describe('ensureFreshInstallReset', () => {
  it('does nothing when the install flag is already set (normal launch)', async () => {
    getItem.mockResolvedValue('1');

    await ensureFreshInstallReset();

    expect(revokeSecurity).not.toHaveBeenCalled();
    expect(setItem).not.toHaveBeenCalled();
  });

  it('wipes Keychain credentials on a fresh reinstall (flag absent, no prior app state)', async () => {
    getItem.mockResolvedValue(null);
    getAllKeys.mockResolvedValue([]);

    await ensureFreshInstallReset();

    // The stale session that survived in the Keychain must be cleared, then the
    // install is marked so later launches skip the wipe.
    expect(revokeSecurity).toHaveBeenCalledTimes(1);
    expect(setItem).toHaveBeenCalledWith('install_initialized', '1');
  });

  it('does NOT wipe on a version upgrade (flag absent but prior app state present)', async () => {
    getItem.mockResolvedValue(null);
    // AsyncStorage survived the upgrade, so an existing user must stay logged in.
    getAllKeys.mockResolvedValue(['fuel-app-state', 'lemberg-language']);

    await ensureFreshInstallReset();

    expect(revokeSecurity).not.toHaveBeenCalled();
    expect(setItem).toHaveBeenCalledWith('install_initialized', '1');
  });
});
