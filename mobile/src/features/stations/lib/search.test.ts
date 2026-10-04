import { filterStationsByQuery, matchesQuery, BRAND_SEARCH_ALIASES } from './search';
import type { StationNode } from '../../../core/types/api';

/**
 * `app/map.tsx` filtered the station list inline, in a 1,100-line screen, with no
 * test. This suite is the reason a change to the filter cannot now break search
 * silently.
 *
 * The brand aliases are the part worth protecting: a customer types "OKKO" in Latin
 * and the API returns "ОККО" in Cyrillic. Drop one alias and that network vanishes
 * from search with no error and no hint that anything is wrong.
 */

function station(overrides: Partial<StationNode> = {}): StationNode {
  return {
    id: overrides.id ?? 'st-1',
    name: overrides.name ?? 'Some Station',
    lat: overrides.lat ?? '50.45',
    lng: overrides.lng ?? '30.52',
    ...overrides,
  } as StationNode;
}

function named(name: string, extra: Partial<StationNode> = {}) {
  return station({ name, ...extra });
}

describe('BRAND_SEARCH_ALIASES', () => {
  it('covers the four networks whose names differ by alphabet', () => {
    expect(Object.keys(BRAND_SEARCH_ALIASES).sort()).toEqual(['klo', 'okko', 'upg', 'wog']);
  });

  it('maps every alias to a Cyrillic spelling, so the two are never equal', () => {
    // An alias equal to its key would be dead configuration: the plain substring
    // match already covers it, and the intent would be lost.
    for (const [latin, cyrillic] of Object.entries(BRAND_SEARCH_ALIASES)) {
      expect(cyrillic).not.toBe(latin);
      expect(cyrillic).toMatch(/^[а-яіїєґ]+$/i);
    }
  });
});

describe('matchesQuery', () => {
  it('matches everything on an empty query', () => {
    expect(matchesQuery(named('ВОГ'), '')).toBe(true);
    expect(matchesQuery(named('ВОГ'), '   ')).toBe(true);
  });

  it('ignores case in both the query and the station name', () => {
    expect(matchesQuery(named('Shell'), 'SHELL')).toBe(true);
    expect(matchesQuery(named('SHELL'), 'shell')).toBe(true);
  });

  it('trims surrounding whitespace off the query', () => {
    expect(matchesQuery(named('Shell'), '  shell  ')).toBe(true);
  });

  it('matches a station name, an address or a city', () => {
    expect(matchesQuery(named('Shell', { address: 'Хрещатик 1' }), 'хрещатик')).toBe(true);
    expect(matchesQuery(named('Shell', { city: 'Львів' }), 'ьвів')).toBe(true);
  });

  describe('Latin query, Cyrillic network name', () => {
    const cases: [string, string][] = [
      ['okko', 'ОККО №1'],
      ['wog', 'ВОГ на Львівському'],
      ['klo', 'КЛО Солом’янський'],
      ['upg', 'ЮПІ_fill'],
    ];

    it.each(cases)('"%s" finds "%s"', (query, name) => {
      expect(matchesQuery(named(name), query)).toBe(true);
    });

    it('works with the query typed in any case', () => {
      expect(matchesQuery(named('ОККО №1'), 'OKKO')).toBe(true);
      expect(matchesQuery(named('ОККО №1'), 'OkKo')).toBe(true);
    });
  });

  it('uses the alias only on an exact whole-query match, not a substring', () => {
    // The alias table is looked up by equality. If it were ever looked up by
    // "does the query contain a brand name", a longer query like "wogx" would
    // silently match every WOG station.
    expect(matchesQuery(named('ВОГ'), 'wog')).toBe(true);
    expect(matchesQuery(named('ВОГ'), 'wogx')).toBe(false);
    expect(matchesQuery(named('КЛО Солом’янський'), 'клок')).toBe(false);
  });

  it('matches a station whose address merely contains the Cyrillic brand name', () => {
    // The alias is a substring test against name/address/city, so a station on
    // Вогданова (which genuinely contains "вог") is findable by typing "wog".
    expect(matchesQuery(named('Shell', { address: 'вул. Вогданова 12' }), 'wog')).toBe(true);
  });

  it('returns nothing for a query that matches nothing', () => {
    expect(matchesQuery(named('Shell'), 'zzzz')).toBe(false);
  });
});

describe('filterStationsByQuery', () => {
  const list = [
    named('ОККО №1', { id: 'a', address: 'вул. Соборна 1' }),
    named('Shell', { id: 'b', city: 'Київ' }),
    named('ВОГ на Стрильницях', { id: 'c' }),
  ];

  it('returns every usable station for an empty query', () => {
    expect(filterStationsByQuery(list, '')).toHaveLength(3);
  });

  it('narrows to the matching station', () => {
    expect(filterStationsByQuery(list, 'shell').map((s) => s.id)).toEqual(['b']);
  });

  it('finds a Cyrillic-named station by its Latin brand name', () => {
    expect(filterStationsByQuery(list, 'wog').map((s) => s.id)).toEqual(['c']);
    expect(filterStationsByQuery(list, 'okko').map((s) => s.id)).toEqual(['a']);
  });

  it('matches on address as well as name', () => {
    expect(filterStationsByQuery(list, 'соборна').map((s) => s.id)).toEqual(['a']);
  });

  describe('drops stations that cannot be shown on the map', () => {
    it('drops a station with no coordinates at all', () => {
      const withNoCoords = [station({ name: 'Ghost', lat: undefined, lng: undefined })];
      expect(filterStationsByQuery(withNoCoords, '')).toHaveLength(0);
    });

    it('drops a station whose coordinates are zero', () => {
      // Null Island is in the Gulf of Guinea. It is never a real АЗК, and keeping
      // it would make the results count disagree with the pins on screen.
      const atOrigin = [station({ name: 'Null Island', lat: '0', lng: '0' })];
      expect(filterStationsByQuery(atOrigin, '')).toHaveLength(0);
    });

    it('drops a station missing only one of the two coordinates', () => {
      const halfCoords = [station({ name: 'Half', lat: '50.45', lng: '0' })];
      expect(filterStationsByQuery(halfCoords, '')).toHaveLength(0);
    });

    it('drops them even when the query would have matched them', () => {
      const ghost = [station({ name: 'ОККО Призрак', lat: '', lng: '' })];
      expect(filterStationsByQuery(ghost, 'okko')).toHaveLength(0);
    });

    it('keeps the usable stations and drops only the broken ones', () => {
      const mixed = [...list, station({ id: 'd', name: 'Ghost', lat: '0', lng: '0' })];
      expect(filterStationsByQuery(mixed, '').map((s) => s.id)).toEqual(['a', 'b', 'c']);
    });
  });

  it('handles absent input rather than throwing', () => {
    expect(filterStationsByQuery(undefined, '')).toEqual([]);
    expect(filterStationsByQuery(null, 'shell')).toEqual([]);
  });

  it('does not mutate the list it was given', () => {
    const original = [...list];
    filterStationsByQuery(list, 'shell');
    expect(list).toEqual(original);
  });
});
