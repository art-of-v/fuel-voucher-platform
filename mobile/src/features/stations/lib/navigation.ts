/**
 * Deep links for turn-by-turn navigation to a station.
 *
 * Waze is the owner's preferred navigator. Its universal `https://waze.com/ul`
 * link opens the Waze app when it is installed and falls back to the Waze web
 * page otherwise — so, unlike a bare `waze://` scheme, it needs no
 * `LSApplicationQueriesSchemes` entry and ships over OTA with no native rebuild.
 */
export function wazeNavigationUrl(lat: number, lng: number): string {
  return `https://waze.com/ul?ll=${lat},${lng}&navigate=yes`;
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
