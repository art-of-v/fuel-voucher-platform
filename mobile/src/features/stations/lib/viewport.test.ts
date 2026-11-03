import {
  MARKER_POOL_SIZE,
  MAX_MOUNTED_MARKERS,
  VIEWPORT_PADDING,
  assignToSlots,
  isPointInRegion,
  isRegionCoveredByBox,
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

  it('keeps the pins it already had when the same zoom moves', () => {
    // The churn the interop crash feeds on. Anchored to the region, a new centre moves every cell
    // boundary, every cell changes its winner, and a pan that revealed nothing new re-creates all
    // ~120 native trees in the frame right after the gesture. Anchored to the ground, the АЗК
    // nearest a cell's centre is whoever asks — so any pin of the old set that sits well inside
    // both boxes has to still be there. The shift is a quarter of the screen, which is a pan.
    const all = spread(1000);
    const shifted: MapRegion = {
      ...whole,
      latitude: whole.latitude + 0.6,
      longitude: whole.longitude - 0.5,
    };
    const before = markersForRegion(all, whole);
    const after = markersForRegion(all, shifted);

    // "Well inside" is half the box on each side, so the cells those pins sit in are wholly inside
    // both boxes and the candidate set that chose them did not change.
    const stays = before.filter(
      (point) => isPointInRegion(point, whole, -0.5) && isPointInRegion(point, shifted, -0.5),
    );
    const keptIds = new Set(after.map((point) => point.id));

    expect(stays.length).toBeGreaterThan(5);
    expect(stays.filter((point) => keptIds.has(point.id))).toHaveLength(stays.length);
  });
});

describe('isRegionCoveredByBox', () => {
  it('holds for the region the set was computed from', () => {
    expect(isRegionCoveredByBox(KYIV, KYIV)).toBe(true);
  });

  it('holds while the screen stays inside the margin already mounted', () => {
    // The set covers the box plus VIEWPORT_PADDING on each side, so a pan of up to a sixth of the
    // screen (0.015° of 0.15°) reveals nothing that was not already a pin.
    expect(isRegionCoveredByBox({ ...KYIV, latitude: KYIV.latitude + 0.014 }, KYIV)).toBe(true);
  });

  it('asks for a recomputation past the padded edge', () => {
    expect(isRegionCoveredByBox({ ...KYIV, latitude: KYIV.latitude + 0.05 }, KYIV)).toBe(false);
    expect(isRegionCoveredByBox({ ...KYIV, longitude: KYIV.longitude - 0.05 }, KYIV)).toBe(false);
  });

  it('asks for one when the screen outgrows the box', () => {
    expect(isRegionCoveredByBox({ ...KYIV, latitudeDelta: 0.3, longitudeDelta: 0.3 }, KYIV)).toBe(
      false,
    );
  });

  it('holds while the box is still the right scale for the screen', () => {
    // Zooming in a little keeps the pins; the box may not stay many times wider than the screen,
    // or the street the user opened would be served by a set thinned for a city.
    expect(isRegionCoveredByBox({ ...KYIV, latitudeDelta: 0.13, longitudeDelta: 0.13 }, KYIV)).toBe(
      true,
    );
    expect(isRegionCoveredByBox({ ...KYIV, latitudeDelta: 0.01, longitudeDelta: 0.01 }, KYIV)).toBe(
      false,
    );
  });
});

describe('assignToSlots', () => {
  const WHOLE: MapRegion = { latitude: 50, longitude: 31, latitudeDelta: 4, longitudeDelta: 4 };

  function lattice(count: number) {
    const side = Math.ceil(Math.sqrt(count));
    return Array.from({ length: count }, (_, i) =>
      at(48.2 + Math.floor(i / side) * (3.4 / side), 29.5 + (i % side) * (3.4 / side)),
    );
  }

  const shown = (slots: ({ id: string } | null)[]) =>
    slots.filter((point): point is { id: string } => point != null);

  it('mounts the pool and nothing else', () => {
    // The length is the whole point. `Marker` is hosted by a legacy view manager, and the only way
    // the map's own child array stops being rewritten mid-gesture — which is what kills the app, in
    // Objective-C, twice over — is for the screen to ask for the same number of markers every time.
    expect(assignToSlots([], KYIV, [])).toHaveLength(MARKER_POOL_SIZE);
    expect(assignToSlots(lattice(1000), WHOLE, [])).toHaveLength(MARKER_POOL_SIZE);
  });

  it('shows every pin the region wants and leaves the rest of the pool empty', () => {
    const all = lattice(1000);
    const wanted = markersForRegion(all, WHOLE);
    const slots = assignToSlots(all, WHOLE, []);

    expect(
      shown(slots)
        .map((p) => p.id)
        .sort(),
    ).toEqual(wanted.map((p) => p.id).sort());
    expect(slots.length - shown(slots).length).toBe(MARKER_POOL_SIZE - wanted.length);
  });

  it('keeps a station in the slot it was already given', () => {
    const near = [at(50.45, 30.52), at(50.46, 30.55), at(50.44, 30.5)];
    const first = assignToSlots(near, KYIV, []);
    const second = assignToSlots(near, { ...KYIV, latitude: 50.455, longitude: 30.51 }, first);

    // The same pins in the same markers: a pan that still wants a pin must not hand it to another
    // slot, because the press handler, the coordinates and the rasterised disc ride on it.
    expect(second).toEqual(first);
  });

  it('takes a fresh pin into an empty slot instead of displacing one that stayed', () => {
    const a = at(50.45, 30.52);
    const b = at(50.46, 30.55);
    const first = assignToSlots([a], KYIV, []);
    const second = assignToSlots([a, b], KYIV, first);

    expect(shown(second).map((point) => point.id)).toEqual(['50.45,30.52', '50.46,30.55']);
    // Enough empty slots always exist — the shortlist is capped at the pool — so this never has to
    // move a wanted pin out of the way to make room.
    expect(second.length - shown(second).length).toBe(MARKER_POOL_SIZE - 2);
  });

  it('empties the slot of a station that left the region', () => {
    const a = at(50.45, 30.52);
    const lviv = at(49.8397, 24.0297);
    const first = assignToSlots([a, lviv], { ...KYIV, longitudeDelta: 13, latitudeDelta: 13 }, []);
    const second = assignToSlots([a, lviv], KYIV, first);

    // A slot showing nothing is what the pool is for: the marker stays, the pin goes.
    expect(shown(second).map((point) => point.id)).toEqual(['50.45,30.52']);
    expect(second.every((p) => p == null || isPointInRegion(p, KYIV))).toBe(true);
  });

  it('keeps the slot when the station comes back as a new object', () => {
    // react-query hands the screen fresh instances after a refetch. Matching by identity would move
    // every pin to a different marker on the first refresh after a pan.
    const a = at(50.45, 30.52);
    const first = assignToSlots([a], KYIV, []);
    const second = assignToSlots([{ ...a }], KYIV, first);

    expect(second[0]?.id).toBe(a.id);
    expect(second[0]).not.toBe(first[0]);
  });
});
