import {
  MAX_MOUNTED_MARKERS,
  VIEWPORT_PADDING,
  isPointInRegion,
  markersForRegion,
  pointsInRegion,
  type MapRegion,
} from './viewport';

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

describe('markersForRegion', () => {
  const whole: MapRegion = { latitude: 50, longitude: 31, latitudeDelta: 4, longitudeDelta: 4 };

  /**
   * `count` stations on an even lattice across the box the region shows. Even, because the
   * thing under test is what happens to a *spread* of stations, and a random scatter would
   * make the assertions about coverage flaky.
   */
  function spread(count: number) {
    const side = Math.ceil(Math.sqrt(count));
    return Array.from({ length: count }, (_, i) =>
      at(48.2 + Math.floor(i / side) * (3.4 / side), 29.5 + (i % side) * (3.4 / side)),
    );
  }

  it('leaves the set alone while it fits under the cap', () => {
    // A city at the map's opening zoom holds ~72 of the network's 1322 АЗК. Those pins are
    // countable one by one, so thinning them would be a loss, not a saving.
    const city = Array.from({ length: 72 }, (_, i) =>
      at(
        KYIV.latitude - 0.07 + Math.floor(i / 12) * 0.012,
        KYIV.longitude - 0.07 + (i % 12) * 0.012,
      ),
    );

    expect(markersForRegion(city, KYIV)).toHaveLength(72);
    expect(markersForRegion(city, KYIV)).toEqual(pointsInRegion(city, KYIV));
  });

  it('never mounts more than the cap', () => {
    expect(markersForRegion(spread(1000), whole).length).toBeLessThanOrEqual(MAX_MOUNTED_MARKERS);
  });

  it('thins across the whole area rather than keeping the first ones listed', () => {
    // The obvious implementation is `inView.slice(0, limit)`, and it is wrong: the API lists
    // stations brand by brand, so a prefix keeps one corner of the country and leaves the
    // user's own neighbourhood empty. The lattice runs 48.2…51.5 north, and a prefix of 120 of
    // it stops at 48.5 — so a pin surviving up near 51 is what says the set was spread and not
    // truncated. Each cell is about 0.4° tall, so the survivor is the АЗК nearest the cell's
    // centre rather than the extreme point itself.
    const kept = markersForRegion(spread(1000), whole);
    const lats = kept.map((p) => p.lat as number);
    const lngs = kept.map((p) => p.lng as number);

    expect(Math.min(...lats)).toBeLessThan(48.5);
    expect(Math.max(...lats)).toBeGreaterThan(51);
    expect(Math.min(...lngs)).toBeLessThan(29.9);
    expect(Math.max(...lngs)).toBeGreaterThan(32.3);
  });

  it('gives the same pins for the same region', () => {
    // A pan that re-shuffles which of the visible stations keep their pin looks like the map
    // is redrawn at random, so the choice has to be a function of position alone.
    const all = spread(1000);

    expect(markersForRegion(all, whole).map((p) => p.id)).toEqual(
      markersForRegion(all, whole).map((p) => p.id),
    );
  });

  it('only ever picks from inside the padded region', () => {
    const kept = markersForRegion(spread(1000), whole);

    expect(kept.length).toBeGreaterThan(0);
    expect(kept.every((point) => isPointInRegion(point, whole))).toBe(true);
  });
});
