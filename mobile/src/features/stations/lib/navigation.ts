/**
 * Deep links for turn-by-turn navigation to a station.
 *
 * Waze is the owner's preferred navigator. We use its app scheme
 * `waze://?ll=<lat>,<lng>&navigate=yes` so the caller can first probe
 * `Linking.canOpenURL` and route to Waze only when it is actually installed,
 * falling straight back to the platform maps app otherwise. For that probe to
 * work on iOS, `waze` must be listed in `LSApplicationQueriesSchemes`
 * (see `app.json`) — a native change, so this ships in a build, not over OTA.
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
