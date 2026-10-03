/**
 * Deep links for turn-by-turn navigation to a station.
 *
 * Waze is the owner's preferred navigator. The caller probes
 * `Linking.canOpenURL` with this app-scheme link and routes to Waze only when it
 * is installed, falling back to the platform maps app otherwise. For that probe
 * to work on iOS, `waze` must be listed in `LSApplicationQueriesSchemes`
 * (see `app.json`) — a native change, so it takes effect in a build, not over
 * OTA; on a build without the entry the probe rejects and the caller opens the
 * platform maps app.
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
