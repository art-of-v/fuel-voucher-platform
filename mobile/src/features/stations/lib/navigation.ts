/**
 * Turn-by-turn navigation to a station.
 *
 * The "build route" button offers a catalog of navigators rather than hard-wiring one: for each we
 * know an app-scheme deep link (probed with `Linking.canOpenURL`) and, where such a link exists, a
 * universal/web link that opens the app when it is installed and the browser otherwise.
 *
 * A row is shown when the navigator is installed OR it has a web link, and picking a row tries the
 * app link then the web link — the button must never dead-end. Waze is first because it is the
 * owner's preferred navigator.
 *
 * Probing is a native capability, so it under-reports rather than fails: on iOS `canOpenURL`
 * rejects every scheme missing from `LSApplicationQueriesSchemes`, on Android 11+ it rejects every
 * app missing from the manifest's `<queries>` (both are declared in app.json / the config plugin —
 * keep them in sync). That is exactly why the web link is the floor and not a bonus.
 */

export interface RouteTarget {
    lat: number;
    lng: number;
}

export type PlatformOS = 'ios' | 'android' | 'other';

export type NavigatorId = 'waze' | 'google-maps' | 'apple-maps' | 'here-wego' | 'osmand' | 'organic-maps';

export interface NavigatorDefinition {
    id: NavigatorId;
    /** Brand name as it reads on the phone — a proper noun, so not an i18n key. */
    name: string;
    /** Two-letter mark for the row chip; we ship no brand logos for navigators. */
    monogram: string;
    /** Where the navigator is offered; any other platform filters the row out. */
    platforms: readonly PlatformOS[];
    /** App-scheme deep links, probed (and opened) in order. Absent = no probeable scheme. */
    appUrls: Partial<Record<PlatformOS, readonly ((target: RouteTarget) => string)[]>>;
    /** Universal link that opens the app when installed and the browser otherwise; null = app-only. */
    webUrl: ((target: RouteTarget) => string) | null;
}

const wazeAppUrl = (t: RouteTarget) => `waze://?ll=${t.lat},${t.lng}&navigate=yes`;

export const NAVIGATORS: readonly NavigatorDefinition[] = [
    {
        id: 'waze',
        name: 'Waze',
        monogram: 'Wa',
        platforms: ['ios', 'android'],
        appUrls: { ios: [wazeAppUrl], android: [wazeAppUrl] },
        webUrl: (t) => `https://www.waze.com/ul?ll=${t.lat},${t.lng}&navigate=yes`,
    },
    {
        id: 'google-maps',
        name: 'Google Maps',
        monogram: 'GM',
        platforms: ['ios', 'android'],
        appUrls: {
            ios: [(t) => `comgooglemaps://?daddr=${t.lat},${t.lng}&directionsmode=driving`],
            android: [(t) => `google.navigation://q=${t.lat},${t.lng}&mode=d`],
        },
        webUrl: (t) => `https://www.google.com/maps/dir/?api=1&destination=${t.lat},${t.lng}`,
    },
    {
        // Apple Maps exposes no app-scheme deep link; its universal link opens the app itself when
        // installed, so the row is offered on every platform (the web part works in any browser).
        id: 'apple-maps',
        name: 'Apple Maps',
        monogram: 'AM',
        platforms: ['ios'],
        appUrls: {},
        webUrl: (t) => `https://maps.apple.com/?daddr=${t.lat},${t.lng}`,
    },
    {
        // HERE WeGo and the open-source navigators below have no reliable web route link, so they are
        // offered only when a probe confirms the app — no row, no broken tap.
        id: 'here-wego',
        name: 'HERE WeGo',
        monogram: 'HW',
        platforms: ['ios'],
        appUrls: { ios: [(t) => `here-wego://?ll=${t.lat},${t.lng}`] },
        webUrl: null,
    },
    {
        id: 'osmand',
        name: 'OsmAnd',
        monogram: 'OA',
        platforms: ['ios', 'android'],
        // Two apps share the brand: the paid one claims osmandplus://, the free one osmand://.
        appUrls: {
            ios: [(t) => `osmandplus://navigate?lat=${t.lat}&lon=${t.lng}`, (t) => `osmand://navigate?lat=${t.lat}&lon=${t.lng}`],
            android: [(t) => `osmandplus://navigate?lat=${t.lat}&lon=${t.lng}`, (t) => `osmand://navigate?lat=${t.lat}&lon=${t.lng}`],
        },
        webUrl: null,
    },
    {
        id: 'organic-maps',
        name: 'Organic Maps',
        monogram: 'OM',
        platforms: ['ios', 'android'],
        appUrls: { ios: [(t) => `organicmaps://?lat=${t.lat}&lon=${t.lng}`], android: [(t) => `organicmaps://?lat=${t.lat}&lon=${t.lng}`] },
        webUrl: null,
    },
];

export interface NavigatorOption {
    definition: NavigatorDefinition;
    /** The app link, set only when a probe confirmed the navigator is installed. */
    appUrl: string | null;
    /** The web/universal link, null for app-only navigators. */
    webUrl: string | null;
}

/** `Platform.OS` is wider than we care about — everything that is not iOS/Android is 'other'. */
export function toPlatformOS(os: string): PlatformOS {
    if (os === 'ios') return 'ios';
    if (os === 'android') return 'android';
    return 'other';
}

/**
 * The station's position, or null when it has none. Coordinates arrive as nullable strings, and a
 * missing one used to be deep-linked as 0,0 — a route to Null Island in the Atlantic. Out-of-range
 * and (0,0) values are rejected for the same reason.
 */
export function routeTarget(lat?: string | null, lng?: string | null): RouteTarget | null {
    const parsedLat = parseFloat(lat ?? '');
    const parsedLng = parseFloat(lng ?? '');
    if (!Number.isFinite(parsedLat) || !Number.isFinite(parsedLng)) return null;
    if (parsedLat < -90 || parsedLat > 90 || parsedLng < -180 || parsedLng > 180) return null;
    if (parsedLat === 0 && parsedLng === 0) return null;
    return { lat: parsedLat, lng: parsedLng };
}

/** Every link worth trying for a navigator, app links first and in catalog order. */
export function navigationUrlChain(option: NavigatorOption): string[] {
    return [option.appUrl, option.webUrl].filter((url): url is string => url != null);
}

/** The link a row leads to; null only when the navigator has neither (never in the catalog). */
export function navigationUrl(option: NavigatorOption): string | null {
    return navigationUrlChain(option)[0] ?? null;
}

/**
 * Hand a chosen navigator to the OS: the app link first, then the web link. A row is never a dead
 * end — if the app link rejects (not installed after all, or an unhandled scheme) the web link
 * still leads to the destination. Returns whether anything opened.
 */
export async function openNavigation(
    option: NavigatorOption,
    openUrl: (url: string) => Promise<unknown>,
): Promise<boolean> {
    for (const url of navigationUrlChain(option)) {
        try {
            await openUrl(url);
            return true;
        } catch {
            // Try the next link.
        }
    }
    return false;
}

export type CanOpenUrl = (url: string) => Promise<boolean>;

async function firstOpenableUrl(
    builders: readonly ((target: RouteTarget) => string)[] | undefined,
    target: RouteTarget,
    canOpenUrl: CanOpenUrl,
): Promise<string | null> {
    for (const build of builders ?? []) {
        const url = build(target);
        try {
            if (await canOpenUrl(url)) return url;
        } catch {
            // An undeclared scheme rejects rather than resolving false — same meaning here.
        }
    }
    return null;
}

/**
 * Ask the OS which catalog entries this device can actually navigate with. Returns one option per
 * row the picker should show, in catalog order: installed navigators, plus every navigator with a
 * web link (reachable through the browser). A navigator with neither is dropped.
 */
export async function probeNavigators(
    os: PlatformOS,
    target: RouteTarget,
    canOpenUrl: CanOpenUrl,
    navigators: readonly NavigatorDefinition[] = NAVIGATORS,
): Promise<NavigatorOption[]> {
    const offered = navigators.filter((definition) => definition.platforms.includes(os));
    const probed = await Promise.all(
        offered.map(async (definition): Promise<NavigatorOption> => ({
            definition,
            appUrl: await firstOpenableUrl(definition.appUrls[os], target, canOpenUrl),
            webUrl: definition.webUrl?.(target) ?? null,
        })),
    );
    return probed.filter((option) => option.appUrl != null || option.webUrl != null);
}