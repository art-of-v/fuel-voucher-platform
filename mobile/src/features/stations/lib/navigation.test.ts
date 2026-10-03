import {
    NAVIGATORS,
    navigationUrl,
    navigationUrlChain,
    openNavigation,
    probeNavigators,
    routeTarget,
    toPlatformOS,
    type NavigatorDefinition,
} from './navigation';

const KYIV = { lat: 50.4501, lng: 30.5234 };

const never = async () => false;
const always = async () => true;

describe('routeTarget', () => {
    it('parses the station coordinates', () => {
        expect(routeTarget('50.4501', '30.5234')).toEqual({ lat: 50.4501, lng: 30.5234 });
    });

    it('rejects missing coordinates instead of routing to Null Island', () => {
        expect(routeTarget(null, null)).toBeNull();
        expect(routeTarget(undefined, undefined)).toBeNull();
        expect(routeTarget('', '')).toBeNull();
        expect(routeTarget('not-a-number', '30.5234')).toBeNull();
    });

    it('rejects a literal zero pair but keeps a single zero axis', () => {
        expect(routeTarget('0', '0')).toBeNull();
        expect(routeTarget('0', '30.5234')).toEqual({ lat: 0, lng: 30.5234 });
        expect(routeTarget('-33.86', '0')).toEqual({ lat: -33.86, lng: 0 });
    });

    it('rejects out-of-range coordinates', () => {
        expect(routeTarget('91', '30')).toBeNull();
        expect(routeTarget('-91', '30')).toBeNull();
        expect(routeTarget('50', '181')).toBeNull();
    });
});

describe('toPlatformOS', () => {
    it('keeps the two mobile platforms and folds the rest', () => {
        expect(toPlatformOS('ios')).toBe('ios');
        expect(toPlatformOS('android')).toBe('android');
        expect(toPlatformOS('web')).toBe('other');
        expect(toPlatformOS('macos')).toBe('other');
    });
});

describe('NAVIGATORS', () => {
    it('puts Waze first — the preferred navigator', () => {
        expect(NAVIGATORS[0].id).toBe('waze');
    });

    it('has unique ids, a name and a monogram for every entry', () => {
        const ids = NAVIGATORS.map((n) => n.id);
        expect(new Set(ids).size).toBe(ids.length);
        for (const n of NAVIGATORS) {
            expect(n.name.length).toBeGreaterThan(0);
            expect(n.monogram.length).toBeGreaterThan(0);
            expect(n.platforms.length).toBeGreaterThan(0);
        }
    });

    it('builds every link with the destination coordinates', () => {
        for (const definition of NAVIGATORS) {
            const urls = [
                ...(definition.appUrls.ios ?? []),
                ...(definition.appUrls.android ?? []),
                ...(definition.webUrl ? [definition.webUrl] : []),
            ].map((build) => build(KYIV));

            expect(urls.length).toBeGreaterThan(0);
            for (const url of urls) {
                expect(url).toContain(String(KYIV.lat));
                expect(url).toContain(String(KYIV.lng));
            }
        }
    });

    it('keeps negative coordinates intact', () => {
        const waze = NAVIGATORS[0];
        expect(waze.appUrls.ios?.[0]({ lat: -33.86, lng: 0 })).toBe(
            'waze://?ll=-33.86,0&navigate=yes',
        );
    });
});

describe('probeNavigators', () => {
    it('shows installed navigators with their app link', async () => {
        const options = await probeNavigators('ios', KYIV, (url) => (url.startsWith('waze://') ? always() : never()));

        const waze = options.find((o) => o.definition.id === 'waze');
        expect(waze?.appUrl).toBe('waze://?ll=50.4501,30.5234&navigate=yes');
        expect(waze?.webUrl).toBe('https://www.waze.com/ul?ll=50.4501,30.5234&navigate=yes');
    });

    it('drops app-only navigators that are not installed, keeping the web-backed ones', async () => {
        const options = await probeNavigators('android', KYIV, never);
        const ids = options.map((o) => o.definition.id);

        expect(ids).not.toContain('osmand');
        expect(ids).not.toContain('organic-maps');
        expect(ids).toContain('waze');
        expect(ids).toContain('google-maps');
        expect(ids).toContain('yandex-maps');
    });

    it('picks the first scheme that resolves — OsmAnd+ vs OsmAnd', async () => {
        const options = await probeNavigators('android', KYIV, (url) => (url.startsWith('osmand://') ? always() : never()));
        const osmand = options.find((o) => o.definition.id === 'osmand');

        expect(osmand?.appUrl).toBe('osmand://navigate?lat=50.4501&lon=30.5234');
        expect(osmand?.webUrl).toBeNull();
    });

    it('treats a rejecting probe as "not installed"', async () => {
        const rejecting = async () => {
            throw new Error('unavailable scheme');
        };
        const options = await probeNavigators('ios', KYIV, rejecting);
        const ids = options.map((o) => o.definition.id);

        // Only the web-backed rows survive a probe that rejects everything (undeclared schemes).
        expect(ids).toEqual(['waze', 'google-maps', 'yandex-maps', 'apple-maps']);
    });

    it('offers Apple Maps only on iOS, and only as a web link', async () => {
        const ios = await probeNavigators('ios', KYIV, never);
        const android = await probeNavigators('android', KYIV, never);

        const apple = ios.find((o) => o.definition.id === 'apple-maps');
        expect(apple?.appUrl).toBeNull();
        expect(apple?.webUrl).toBe('https://maps.apple.com/?daddr=50.4501,30.5234');
        expect(android.map((o) => o.definition.id)).not.toContain('apple-maps');
    });

    it('returns nothing when no navigator is reachable at all', async () => {
        const options = await probeNavigators('other', KYIV, always);
        expect(options).toEqual([]);
    });

    it('probes the navigator definitions it is given', async () => {
        const custom: NavigatorDefinition = {
            id: 'organic-maps',
            name: 'Only',
            monogram: 'On',
            platforms: ['android'],
            appUrls: { android: [() => 'only://go'] },
            webUrl: null,
        };

        const options = await probeNavigators('android', KYIV, always, [custom]);

        expect(options).toHaveLength(1);
        expect(options[0].appUrl).toBe('only://go');
    });
});

describe('openNavigation', () => {
    const installed = { definition: NAVIGATORS[0], appUrl: 'waze://go', webUrl: 'https://www.waze.com/ul' };
    const webOnly = { definition: NAVIGATORS[0], appUrl: null, webUrl: 'https://www.waze.com/ul' };

    it('opens the app link of an installed navigator', async () => {
        const open = jest.fn(async () => undefined);
        await expect(openNavigation(installed, open)).resolves.toBe(true);
        expect(open).toHaveBeenCalledWith('waze://go');
    });

    it('falls back to the web link when the app link cannot be opened', async () => {
        const open = jest.fn(async (url: string) => {
            if (url.startsWith('waze://')) throw new Error('ActivityNotFoundException');
        });

        await expect(openNavigation(installed, open)).resolves.toBe(true);
        expect(open.mock.calls.map(([url]) => url)).toEqual(['waze://go', 'https://www.waze.com/ul']);
    });

    it('opens the web link when the app is missing', async () => {
        const open = jest.fn(async () => undefined);
        await expect(openNavigation(webOnly, open)).resolves.toBe(true);
        expect(open).toHaveBeenCalledWith('https://www.waze.com/ul');
    });

    it('reports failure when nothing could be opened', async () => {
        const open = jest.fn(async () => {
            throw new Error('nope');
        });
        await expect(openNavigation(installed, open)).resolves.toBe(false);
        expect(open).toHaveBeenCalledTimes(2);
    });

    it('has nothing to open for a row without links', async () => {
        const open = jest.fn(async () => undefined);
        await expect(openNavigation({ definition: NAVIGATORS[0], appUrl: null, webUrl: null }, open)).resolves.toBe(false);
        expect(open).not.toHaveBeenCalled();
    });
});

describe('navigationUrl helpers', () => {
    it('prefers the app link and lists the whole chain', () => {
        const option = { definition: NAVIGATORS[0], appUrl: 'waze://go', webUrl: 'https://www.waze.com/ul' };

        expect(navigationUrl(option)).toBe('waze://go');
        expect(navigationUrlChain(option)).toEqual(['waze://go', 'https://www.waze.com/ul']);
    });

    it('falls back to the web link and to null', () => {
        expect(navigationUrl({ definition: NAVIGATORS[0], appUrl: null, webUrl: 'https://waze' })).toBe('https://waze');
        expect(navigationUrl({ definition: NAVIGATORS[0], appUrl: null, webUrl: null })).toBeNull();
    });
});