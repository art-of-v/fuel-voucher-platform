/**
 * On iOS a tap on a marker reaches the map view's own press handler as well: react-native-maps
 * installs both recognisers with `cancelsTouchesInView = NO`, so the same tap that selects a
 * station also fires `MapView onPress`, and clearing the selection there closed the station panel
 * in the frame it opened (planning #756). The marker stamps the moment it handled the tap; a map
 * press inside this window is that same tap, not a new one.
 */
export const MARKER_PRESS_WINDOW_MS = 350;

/**
 * True when a map press arrives close enough after a marker press to be the marker's echo.
 * A press that arrives *before* the marker's (the map handler ran first) is not an echo - the
 * selection it cleared is written again by the marker in the same tap.
 */
export function isMarkerPressEcho(
  lastMarkerPressAt: number | null,
  now: number,
  windowMs: number = MARKER_PRESS_WINDOW_MS,
): boolean {
  if (lastMarkerPressAt === null) return false;
  const elapsed = now - lastMarkerPressAt;
  return elapsed >= 0 && elapsed <= windowMs;
}
