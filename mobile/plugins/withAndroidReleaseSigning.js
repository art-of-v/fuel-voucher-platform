const { withAppBuildGradle } = require('expo/config-plugins');

/**
 * Sign release builds with a real key instead of the debug keystore that Expo's template copies in.
 *
 * The debug keystore is regenerated on every machine, so a release signed with it cannot be updated
 * in place anywhere else: Android refuses an APK whose signing certificate changed, and the tester
 * has to uninstall the app first, losing the signed-in session and the OTA update history.
 *
 * The key and its passwords live in `mobile/keys/` (gitignored, never committed). That directory is
 * in the primary checkout, so a build started from a git worktree finds no key next to it: set
 * `FF_KEYSTORE_DIR` to the primary checkout's `mobile/keys` before running Gradle there.
 */
const INJECTION = `
def ffKeystoreDir = System.getenv('FF_KEYSTORE_DIR') ? new File(System.getenv('FF_KEYSTORE_DIR')) : new File(rootProject.projectDir.parentFile, 'keys')

def ffReleaseProps = new Properties()
def ffReleasePropsFile = new File(ffKeystoreDir, 'release.keystore.properties')
if (ffReleasePropsFile.exists()) {
    ffReleasePropsFile.withInputStream { ffReleaseProps.load(it) }
}

android {
    signingConfigs {
        release {
            storeFile new File(ffKeystoreDir, 'release.keystore')
            storePassword ffReleaseProps.getProperty('storePassword')
            keyAlias ffReleaseProps.getProperty('keyAlias')
            keyPassword ffReleaseProps.getProperty('keyPassword')
        }
    }
    buildTypes {
        release {
            signingConfig signingConfigs.release
        }
    }
}
`.trim();

module.exports = function withAndroidReleaseSigning(config) {
  return withAppBuildGradle(config, (config) => {
    if (config.modResults.language !== 'groovy') {
      throw new Error('withAndroidReleaseSigning only supports a Groovy app build.gradle');
    }
    if (config.modResults.contents.includes('signingConfigs.release')) return config;
    config.modResults.contents = `${config.modResults.contents.trimEnd()}\n\n${INJECTION}\n`;
    return config;
  });
};
