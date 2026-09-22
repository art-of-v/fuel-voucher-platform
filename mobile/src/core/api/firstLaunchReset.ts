import AsyncStorage from '@react-native-async-storage/async-storage';
import { SecurityService } from './securityService';

/**
 * iOS Keychain (and Android Keystore) entries written by expo-secure-store and
 * react-native-biometrics SURVIVE app deletion. AsyncStorage does not — it lives in
 * the app sandbox that the OS wipes on uninstall. That asymmetry means a freshly
 * reinstalled app still finds the previous install's access/refresh tokens and device
 * keypair in the Keychain and silently restores the session (verified live: reinstall
 * → refresh → 200, no re-auth), violating "reinstall must re-authenticate".
 *
 * On the first launch of an install we therefore wipe the persisted auth material so a
 * real login is forced. A fresh install is distinguished from a version UPGRADE (where
 * AsyncStorage is retained and users must stay logged in) by the absence of any prior
 * app-state key: on a reinstall the Keychain has tokens but AsyncStorage is empty; on an
 * upgrade AsyncStorage still holds the app's persisted state.
 */
const INSTALL_FLAG = 'install_initialized';

// Keys the app persists via AsyncStorage for any user who has used it before. Their
// presence proves AsyncStorage was NOT wiped, i.e. this is an upgrade, not a reinstall.
const PRIOR_APP_STATE_KEYS = ['fuel-app-state', 'lemberg-language'];

export async function ensureFreshInstallReset(): Promise<void> {
  const alreadyInitialized = await AsyncStorage.getItem(INSTALL_FLAG);
  if (alreadyInitialized) {
    return;
  }

  const keys = await AsyncStorage.getAllKeys();
  const isUpgradeOfExistingInstall = keys.some((k) => PRIOR_APP_STATE_KEYS.includes(k));

  if (!isUpgradeOfExistingInstall) {
    // Fresh install (or genuine first-ever launch, where this is a harmless no-op):
    // clear every Keychain-persisted credential so a stale session can't be restored.
    await SecurityService.revokeSecurity();
  }

  await AsyncStorage.setItem(INSTALL_FLAG, '1');
}
