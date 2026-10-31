import React from 'react';
import { render, screen, fireEvent, act } from '@testing-library/react-native';

import { StationMarker, BrandLogoChip } from './StationMarker';

/**
 * The two map-pin components, extracted from `app/map.tsx`.
 *
 * The behaviour that must survive the move is the marker's `tracksViewChanges`
 * lifecycle: react-native-maps rasterises a custom-view marker once, so a logo pin
 * has to keep tracking until the image paints (`onLoad`), and a colour-only pin --
 * which is ready at mount -- freezes on the next tick. Freezing a logo pin too early
 * leaves 700+ blank markers; never freezing re-rasters every one every frame.
 */

const markerProps: any[] = [];

jest.mock('react-native-maps', () => {
  const R = jest.requireActual<typeof import('react')>('react');
  const { View } = jest.requireActual<typeof import('react-native')>('react-native');
  return {
    __esModule: true,
    // Records the live `tracksViewChanges` on every render so the lifecycle can be
    // observed, and exposes the press target.
    Marker: ({ children, tracksViewChanges, onPress }: any) => {
      markerProps.push({ tracksViewChanges, onPress });
      return R.createElement(View, { testID: 'marker', onPress } as any, children);
    },
    Callout: ({ children }: any) => R.createElement(View, { testID: 'callout' }, children),
  };
});

jest.mock('../../../core/hooks/useTheme', () => {
  const { getTokens } = jest.requireActual<typeof import('../../../core/design/tokens')>(
    '../../../core/design/tokens',
  );
  return { useDesignTokens: () => getTokens() };
});

// Keys are echoed back, so the assertions below name the copy rather than its
// Ukrainian value.
jest.mock('../../../core/i18n', () => ({
  useI18n: (selector: (s: any) => unknown) => selector({ t: (key: string) => key }),
}));

jest.mock('../lib/radar', () => ({
  formatShortAddress: (s: any) => s.address ?? '',
}));

const { Image, View } = jest.requireActual<typeof import('react-native')>('react-native');

function renderMarker(over: Record<string, unknown> = {}) {
  const onPress = jest.fn();
  render(
    <StationMarker
      point={{ name: 'Shell Darnytsia', address: 'Kyiv, Darnytsia' } as any}
      coordinate={{ latitude: 50, longitude: 30 }}
      brandColor="#ff0000"
      price={null}
      onPress={onPress}
      {...over}
    />,
  );
  return { onPress };
}

beforeEach(() => {
  markerProps.length = 0;
  // The colour-pin freeze is a setTimeout, so every test runs on fake timers.
  jest.useFakeTimers();
});

// Every marker schedules that freeze, so drain it inside act -- otherwise the
// stray state update lands in the *next* test and jest.setup.js escalates the
// act warning into a failure there.
afterEach(() => {
  act(() => {
    jest.runOnlyPendingTimers();
  });
  jest.useRealTimers();
});

describe('StationMarker - the tracksViewChanges lifecycle', () => {
  it('keeps tracking a logo pin until the image paints', () => {
    renderMarker({ logo: 1 });

    // First frame: tracking on, so the logo is not rasterised blank before it loads.
    expect(markerProps[0].tracksViewChanges).toBe(true);
  });

  it('freezes a logo pin once the image reports onLoad', () => {
    renderMarker({ logo: 1 });

    act(() => {
      screen.UNSAFE_getAllByType(Image)[0].props.onLoad();
    });

    expect(markerProps[markerProps.length - 1].tracksViewChanges).toBe(false);
  });

  it('freezes a colour-only pin on the next tick', () => {
    renderMarker({ logo: undefined });
    expect(markerProps[0].tracksViewChanges).toBe(true);

    act(() => {
      jest.runOnlyPendingTimers();
    });

    // A colour pin is ready at mount, so it stops re-rastering immediately.
    expect(markerProps[markerProps.length - 1].tracksViewChanges).toBe(false);
  });

  it('keeps tracking the selected pin, whose ring and scale change after the image loads', () => {
    // The raster freezes on load; a selected pin is redrawn afterwards (3px ring,
    // 1.12 scale) and would otherwise keep the unselected drawing. Only one pin is
    // ever selected, so the extra rasters cost nothing.
    renderMarker({ logo: 1, selected: true });

    act(() => {
      screen.UNSAFE_getAllByType(Image)[0].props.onLoad();
    });

    expect(markerProps[markerProps.length - 1].tracksViewChanges).toBe(true);
  });
});

describe('StationMarker - the callout', () => {
  it('names the station and its address', () => {
    renderMarker({ logo: undefined });

    expect(screen.getByText('Shell Darnytsia')).toBeTruthy();
    expect(screen.getByText('Kyiv, Darnytsia')).toBeTruthy();
  });

  it('falls back to a no-address label when there is none', () => {
    renderMarker({ logo: undefined, point: { name: 'Shell', address: '' } as any });

    expect(screen.getByText('map.noAddress')).toBeTruthy();
  });

  it('shows the voucher price and the saving when there is one', () => {
    renderMarker({
      logo: undefined,
      price: { voucherPerLiter: 52.3, pumpPerLiter: 56, savingsPerLiter: 3.7 } as any,
    });

    // Money uses a decimal comma everywhere else in the app, so a dot here would
    // read as a different number to the customer.
    expect(screen.getByText('52,30')).toBeTruthy();
    expect(screen.getByText(/3,70/)).toBeTruthy();
  });

  it('omits the saving when there is none', () => {
    renderMarker({
      logo: undefined,
      price: { voucherPerLiter: 52.3, pumpPerLiter: 52.3, savingsPerLiter: 0 } as any,
    });

    expect(screen.getByText('52,30')).toBeTruthy();
    expect(screen.queryByText(/map.vsPump/)).toBeNull();
  });

  it('shows no price block when the station has no price', () => {
    renderMarker({ logo: undefined, price: null });

    expect(screen.queryByText(/map.perLiter/)).toBeNull();
  });
});

describe('StationMarker - pressing', () => {
  it('hands the pin the tap handler it was given', () => {
    const { onPress } = renderMarker({ logo: undefined });

    // Asserted on the prop, not just via fireEvent: RNTL's press bubbles up to an
    // ancestor's handler, so a press can reach the callback even when the pin
    // itself was rendered without one.
    expect(markerProps[markerProps.length - 1].onPress).toBe(onPress);
  });

  it('calls back when the pin is tapped', () => {
    const { onPress } = renderMarker({ logo: undefined });

    fireEvent.press(screen.getByTestId('marker'));
    expect(onPress).toHaveBeenCalled();
  });
});

describe('BrandLogoChip', () => {
  it('renders the brand image sized to the chip', () => {
    render(<BrandLogoChip logo={1 as any} size={30} />);

    // The glyph is sized relative to the chip, so the badge scales as one piece.
    expect(screen.UNSAFE_getAllByType(Image)[0].props.style).toEqual({
      width: 30 * 0.74,
      height: 30 * 0.74,
    });
  });

  it('is a round badge of the requested size', () => {
    render(<BrandLogoChip logo={1 as any} size={30} />);

    const flat = Object.assign({}, ...[screen.UNSAFE_getByType(View).props.style].flat());
    expect(flat).toMatchObject({ width: 30, height: 30, borderRadius: 15 });
  });
});
