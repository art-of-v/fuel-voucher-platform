import { wazeNavigationUrl, platformMapsUrl } from './navigation';

describe('wazeNavigationUrl', () => {
  it('builds a navigate universal link for the coordinates', () => {
    expect(wazeNavigationUrl(49.79, 23.99)).toBe('https://waze.com/ul?ll=49.79,23.99&navigate=yes');
  });

  it('keeps negative and zero coordinates intact', () => {
    expect(wazeNavigationUrl(-33.86, 0)).toBe('https://waze.com/ul?ll=-33.86,0&navigate=yes');
  });
});

describe('platformMapsUrl', () => {
  it('uses Apple Maps on iOS', () => {
    expect(platformMapsUrl(49.79, 23.99, 'ios')).toBe('https://maps.apple.com/?daddr=49.79,23.99');
  });

  it('uses a geo: intent elsewhere', () => {
    expect(platformMapsUrl(49.79, 23.99, 'android')).toBe('geo:0,0?q=49.79,23.99');
  });
});
