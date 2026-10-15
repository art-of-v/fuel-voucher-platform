import { XML } from 'expo/config-plugins';

import withAndroidCxxStl from './withAndroidCxxStl';

const ARGUMENT = '-DCMAKE_CXX_STANDARD_LIBRARIES=-lc++_shared -latomic -lm';

const applyPlugin = (contents: string): string => {
  const config: any = withAndroidCxxStl({ name: 'FuelFlow', slug: 'mobile-app-native' });
  const mod = config.mods.android.projectBuildGradle;
  const results: any = { contents, language: 'groovy', path: 'android/build.gradle' };
  mod({ ...config, modRequest: { nextMod: () => results }, modResults: results, modResultsPath: 'android/build.gradle' });
  return results.contents;
};

describe('withAndroidCxxStl', () => {
  it('adds the C++ standard library to every native module', () => {
    const contents = applyPlugin('// root build file\n');

    expect(contents).toContain(ARGUMENT);
    expect(contents).toContain('subprojects');
  });

  it('keeps the root build file it was given', () => {
    const contents = applyPlugin('// root build file\napply plugin: "expo-root-project"');

    expect(contents).toContain('apply plugin: "expo-root-project"');
  });

  it('does not append twice when prebuild runs again over an existing file', () => {
    const once = applyPlugin('// root build file\n');
    const twice = applyPlugin(once);

    expect(twice.match(/subprojects \{/g)).toHaveLength(1);
    expect(twice).toContain('// root build file');
  });

  it('refuses a Kotlin root build file instead of writing Groovy into it', async () => {
    const config: any = withAndroidCxxStl({ name: 'FuelFlow', slug: 'mobile-app-native' });
    const mod = config.mods.android.projectBuildGradle;
    const results: any = { contents: '', language: 'kotlin', path: 'android/build.gradle.kts' };

    await expect(
      mod({ ...config, modRequest: { nextMod: () => results }, modResults: results, modResultsPath: results.path }),
    ).rejects.toThrow(/Groovy/);
  });
});

// The plugin only touches Gradle, so the shared XML import above is what keeps the
// config-plugins entry point in the module graph for the manifest test next door.
describe('config-plugins surface', () => {
  it('is the same build that parses Android XML', () => {
    expect(typeof XML.format).toBe('function');
  });
});