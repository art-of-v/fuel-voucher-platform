const { withProjectBuildGradle, withAppBuildGradle } = require('expo/config-plugins');

/**
 * Force the C++ standard library onto every native module's link line.
 *
 * Every module that compiles C++ (expo-modules-core, react-native-screens,
 * react-native-worklets, @sentry/react-native, :app) fails to link on this
 * machine with `ld.lld: undefined symbol: operator new(unsigned long)`,
 * `__cxa_throw`, `std::bad_alloc` and friends. The cause is visible in each
 * module's `CMakeCache.txt`:
 *
 *   CMAKE_CXX_STANDARD_LIBRARIES:STRING=-latomic -lm
 *
 * The C++ runtime is simply absent from that list, so the generated link line
 * carries `-landroid -latomic -lm` and no `-lc++_shared`, while the objects were
 * compiled against libc++. `ANDROID_STL=c++_shared` is passed by every module's
 * own Gradle config and is honoured by the compiler, but it never reaches
 * `CMAKE_CXX_STANDARD_LIBRARIES`. Reproduced with CMake 3.22.1 and 3.31.6 and
 * with NDK 27.1.12297006 and 28.2.13676358, from both Gradle and Android Studio.
 *
 * Passing the variable explicitly on the CMake command line wins over the cache
 * entry the toolchain computed, which is why this fixes it: `-lc++_shared` is
 * prepended to the `-latomic -lm` that was already there, leaving the existing
 * default libraries in place. The value is identical for every module and every
 * ABI, so it needs no per-module configuration.
 */
const CXX_STANDARD_LIBRARIES = '-DCMAKE_CXX_STANDARD_LIBRARIES=-lc++_shared -latomic -lm';

const INJECTION = `
subprojects { subproject ->
    subproject.plugins.withId('com.android.library') {
        subproject.android.defaultConfig.externalNativeBuild.cmake.arguments.add('${CXX_STANDARD_LIBRARIES}')
    }
    subproject.plugins.withId('com.android.application') {
        subproject.android.defaultConfig.externalNativeBuild.cmake.arguments.add('${CXX_STANDARD_LIBRARIES}')
    }
}
`.trim();

const APP_INJECTION = `
android.defaultConfig.externalNativeBuild.cmake {
    arguments.add('${CXX_STANDARD_LIBRARIES}')
}
`.trim();

module.exports = function withAndroidCxxStl(config) {
  config = withProjectBuildGradle(config, (config) => {
    if (config.modResults.language !== 'groovy') {
      throw new Error('withAndroidCxxStl only supports a Groovy root build.gradle');
    }
    if (config.modResults.contents.includes(CXX_STANDARD_LIBRARIES)) return config;
    config.modResults.contents = `${config.modResults.contents.trimEnd()}\n\n${INJECTION}\n`;
    return config;
  });

  // `:app` is not covered by the subprojects block above: its own `defaultConfig` block replaces the
  // `arguments` list when it is evaluated, so the value has to be appended after that block instead.
  return withAppBuildGradle(config, (config) => {
    if (config.modResults.language !== 'groovy') {
      throw new Error('withAndroidCxxStl only supports a Groovy app build.gradle');
    }
    if (config.modResults.contents.includes(APP_INJECTION)) return config;
    config.modResults.contents = `${config.modResults.contents.trimEnd()}\n\n${APP_INJECTION}\n`;
    return config;
  });
};