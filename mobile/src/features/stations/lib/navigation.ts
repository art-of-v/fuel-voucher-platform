/**
 * Deep links for turn-by-turn navigation to a station.
 *
 * Waze is the owner's preferred navigator. The caller opens this app-scheme link
 * directly with `Linking.openURL` — which, unlike `canOpenURL`, needs no `waze`
 * entry in `LSApplicationQueriesSchemes`, so it prefers Waze when installed even
 * over OTA. If Waze isn't installed `openURL` rejects and the caller falls back
 * to the platform maps app.
 */
export function wazeNavigationUrl(lat: number, lng: number): string {
  return `waze://?ll=${lat},${lng}&navigate=yes`;
}

/**
 * The platform's own maps app — used only as a last resort when the Waze link
 * cannot be opened at all, so the "build route" button is never dead.
 */
export function platformMapsUrl(lat: number, lng: number, os: string): string {
  return os === 'ios'
    ? `https://maps.apple.com/?daddr=${lat},${lng}`
    : `geo:0,0?q=${lat},${lng}`;
}
