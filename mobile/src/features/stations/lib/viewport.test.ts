import { isPointInRegion, pointsInRegion, VIEWPORT_PADDING, type MapRegion } from './viewport';

const KYIV: MapRegion = {
  latitude: 50.4501,
  longitude: 30.5234,
  latitudeDelta: 0.15,
  longitudeDelta: 0.15,
};

const at = (lat: number, lng: number) => ({
  id: `${lat},${lng}`,
  lat,
  lng,
});

describe('isPointInRegion', () => {
  it('accepts a point inside the region', () => {
    expect(isPointInRegion(at(50.45, 30.52), KYIV)).toBe(true);
  });

  it('rejects a point on the far side of the country', () => {
    expect(isPointInRegion(at(49.8397, 24.0297), KYIV)).toBe(false);
  });

  it('keeps points just outside the edge, inside the padding', () => {
    // Half the latitude span is 0.075; 0.081 is past the visible edge but inside the 20% margin.
    const justOutside = KYIV.latitude + KYIV.latitudeDelta / 2 + 0.005;

    expect(isPointInRegion(at(justOutside, KYIV.longitude), KYIV)).toBe(true);
    expect(isPointInRegion(at(justOutside, KYIV.longitude), KYIV, 0)).toBe(false);
  });

  it('rejects points past the padded edge', () => {
    const farOutside = KYIV.latitude + (KYIV.latitudeDelta / 2) * (1 + VIEWPORT_PADDING) + 0.01;

    expect(isPointInRegion(at(farOutside, KYIV.longitude), KYIV)).toBe(false);
  });

  it('drops coordinates it cannot read rather than mounting a broken marker', () => {
    expect(isPointInRegion({ lat: null, lng: 30.52 }, KYIV)).toBe(false);
    expect(isPointInRegion({ lat: 'not-a-number', lng: 30.52 }, KYIV)).toBe(false);
  });
});

describe('pointsInRegion', () => {
  it('returns only the stations inside the viewport', () => {
    const all = [at(50.4501, 30.5234), at(49.8397, 24.0297), at(50.46, 30.55)];

    expect(pointsInRegion(all, KYIV).map((p) => p.id)).toEqual(['50.4501,30.5234', '50.46,30.55']);
  });

  it('keeps the set small when zoomed in on one street', () => {
    const all = Array.from({ length: 1300 }, (_, i) => at(50 + i * 0.01, 30 + i * 0.01));
    const narrow: MapRegion = { ...KYIV, latitudeDelta: 0.002, longitudeDelta: 0.002 };

    expect(pointsInRegion(all, narrow).length).toBeLessThan(10);
  });

  it('returns everything when the whole country is on screen', () => {
    const all = Array.from({ length: 50 }, (_, i) => at(49 + i * 0.05, 30 + i * 0.05));
    const whole: MapRegion = { latitude: 50, longitude: 31, latitudeDelta: 4, longitudeDelta: 4 };

    expect(pointsInRegion(all, whole)).toHaveLength(all.length);
  });
});
