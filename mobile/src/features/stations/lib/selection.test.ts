import { isMarkerPressEcho, MARKER_PRESS_WINDOW_MS } from './selection';

describe('isMarkerPressEcho', () => {
  it('lets a map press clear the selection when no marker was pressed', () => {
    expect(isMarkerPressEcho(null, 1_000)).toBe(false);
  });

  it('treats the map press that follows a marker tap as the same tap', () => {
    expect(isMarkerPressEcho(1_000, 1_000)).toBe(true);
    expect(isMarkerPressEcho(1_000, 1_120)).toBe(true);
    expect(isMarkerPressEcho(1_000, 1_000 + MARKER_PRESS_WINDOW_MS)).toBe(true);
  });

  it('lets a later map press clear the selection again', () => {
    expect(isMarkerPressEcho(1_000, 1_001 + MARKER_PRESS_WINDOW_MS)).toBe(false);
  });

  it('does not treat a press that arrived before the marker as an echo', () => {
    expect(isMarkerPressEcho(1_000, 999)).toBe(false);
  });

  it('honours a caller-supplied window', () => {
    expect(isMarkerPressEcho(1_000, 1_200, 100)).toBe(false);
    expect(isMarkerPressEcho(1_000, 1_050, 100)).toBe(true);
  });
});
