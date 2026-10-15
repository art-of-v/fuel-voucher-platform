import { XML } from 'expo/config-plugins';

import withNavigationQueries from './withNavigationQueries';

const SCHEMES = ['waze', 'com.google.android.apps.maps', 'here-wego', 'osmand', 'osmandplus', 'org.organicmaps'];

type Manifest = Record<string, any>;

/** The `<queries>` element React Native's template ships: one `<intent>` for http(s) links. */
const manifestWithBrowserQuery = (): Manifest => ({
  $: { 'xmlns:android': 'http://schemas.android.com/apk/res/android' },
  'uses-permission': [{ $: { 'android:name': 'android.permission.INTERNET' } }],
  queries: [
    {
      intent: [
        {
          action: [{ $: { 'android:name': 'android.intent.action.VIEW' } }],
          category: [{ $: { 'android:name': 'android.intent.category.BROWSABLE' } }],
          data: [{ $: { 'android:scheme': 'https' } }],
        },
      ],
    },
  ],
  application: [{ $: { 'android:name': '.MainApplication' } }],
});

const applyPlugin = async (manifest: Manifest): Promise<Manifest> => {
  const config: any = withNavigationQueries({ name: 'FuelFlow', slug: 'mobile-app-native' });
  const mod = config.mods.android.manifest;
  await mod({ ...config, modRequest: { nextMod: (result: any) => result }, modResults: { manifest } });
  return manifest;
};

const schemesIn = (manifest: Manifest): string[] =>
  manifest.queries.flatMap((query: any) =>
    (query.intent || [])
      .flatMap((intent: any) => (intent.data || []).map((datum: any) => datum?.$?.['android:scheme']))
      .filter(Boolean),
  );

const serialize = (manifest: Manifest): string => XML.format({ manifest });

describe('withNavigationQueries', () => {
  it('declares every navigation scheme as an ACTION_VIEW intent with a data child', async () => {
    const manifest = await applyPlugin(manifestWithBrowserQuery());

    expect(schemesIn(manifest)).toEqual(['https', ...SCHEMES]);
    for (const intent of manifest.queries[0].intent.slice(1)) {
      expect(intent.action).toEqual([{ $: { 'android:name': 'android.intent.action.VIEW' } }]);
    }
  });

  it('keeps the queries Expo already declared', async () => {
    const manifest = await applyPlugin(manifestWithBrowserQuery());

    expect(manifest.queries[0].intent[0].category).toEqual([
      { $: { 'android:name': 'android.intent.category.BROWSABLE' } },
    ]);
  });

  it('does not duplicate a scheme that is already declared', async () => {
    const manifest = manifestWithBrowserQuery();
    manifest.queries[0].intent[0].data.push({ $: { 'android:scheme': 'waze' } });

    await applyPlugin(manifest);

    expect(schemesIn(manifest).filter((scheme) => scheme === 'waze')).toHaveLength(1);
  });

  it('is idempotent across repeated runs', async () => {
    const manifest = await applyPlugin(manifestWithBrowserQuery());
    await applyPlugin(manifest);

    expect(schemesIn(manifest)).toEqual(['https', ...SCHEMES]);
  });

  it('inserts <queries> before <application> when the manifest has none', async () => {
    const manifest: Manifest = {
      $: { 'xmlns:android': 'http://schemas.android.com/apk/res/android' },
      application: [{ $: { 'android:name': '.MainApplication' } }],
    };

    await applyPlugin(manifest);

    expect(Object.keys(manifest)).toEqual(['$', 'queries', 'application']);
    expect(schemesIn(manifest)).toEqual(SCHEMES);
  });

  // The schemes all land in the ONE `<queries>` element the template already declares: handing each
  // scheme its own entry in `queries` serialises to a repeated `<queries>` element per scheme, which
  // AAPT rejects as a duplicate and which no `expo prebuild` run reports as an error.
  it('serialises to a single <queries> element holding every intent', async () => {
    const manifest = await applyPlugin(manifestWithBrowserQuery());

    const xml = serialize(manifest);
    expect(xml.match(/<queries>/g)).toHaveLength(1);
    expect(xml.match(/<intent>/g)).toHaveLength(1 + SCHEMES.length);
    for (const scheme of SCHEMES) {
      expect(xml).toContain(`<data android:scheme="${scheme}"/>`);
    }
  });
});