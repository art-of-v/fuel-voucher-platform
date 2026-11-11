/**
 * How long a map press waits before it closes the open station card.
 *
 * The press and the pin's own selection are the same touch, but they do not arrive together. A
 * marker reports its tap at touch-up; the map reports the press only after its double-tap
 * recogniser gives up, and on a cold start the JS thread is slow enough that the report lands a
 * good half-second later. That delay is not a constant anyone can time against — it shrinks as
 * the app warms up — so the close waits for the press to settle and then asks the map whether
 * anything on it is still selected. A pin press cancels the wait outright, which makes replacing
 * one station with another a single state change.
 */
export const MAP_TAP_SETTLE_MS = 250;

/**
 * How close together two reports of one touch have to be to count as one tap.
 *
 * A pin tap is delivered twice on both platforms: the marker's own press and its selection
 * report it together, at touch-up. Only the second report of the same touch is dropped; a
 * deliberate re-tap of the same pin arrives later than this.
 */
export const MARKER_TAP_DUPLICATE_MS = 350;
