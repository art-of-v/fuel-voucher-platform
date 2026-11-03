import React from 'react';
import { render, screen, fireEvent, act } from '@testing-library/react-native';

import { StationMarker, BrandLogoChip } from './StationMarker';

/**
 * The two map-pin components, extracted from `app/map.tsx`.
 *
 * Four behaviours must survive any edit here. The first is the `tracksViewChanges` lifecycle,
 * which belongs to the Android marker manager (iOS MapKit has no such prop): a custom-view marker
 * is rasterised once there, so a logo pin has to keep tracking until the image paints (`onLoad`),
 * and a colour-only pin -- which is ready at mount -- freezes on the next tick. Freezing a logo
 * pin too early leaves blank discs; never freezing re-rasters every one every frame.
 *
 * The second is that the pin carries no `Callout`, and so keeps a legacy view manager out of the
 * marker's own. That is a crash, not a style rule -- see the describe below.
 *
 * The third is what an empty slot does. The map mounts a fixed pool of markers and hands each one a
 * station, so a marker showing nothing is a normal state, and the two things it must do are stay
 * invisible and stay non-empty -- a marker with no child at all is MapKit's stock red pin.
 *
 * The fourth is what happens when a slot is handed a *different* station: the native marker is
 * reused, so everything that has to be rebuilt has to be rebuilt on purpose.
 */

const markerProps: any[] = [];

jest.mock('react-native-maps', () => {
  const R = jest.requireActual<typeof import('react')>('react');
  const { View } = jest.requireActual<typeof import('react-native')>('react-native');
  return {
    __esModule: true,
    // Records the live props on every render so the lifecycle and the empty-slot state can both be
    // observed, and exposes the press target.
    Marker: (props: any) => {
      markerProps.push(props);
      return R.createElement(
        View,
        { testID: 'marker', onPress: props.onPress } as any,
        props.children,
      );
    },
    // Still mocked after the component stopped using it, so a reintroduced callout renders
    // somewhere a test can see it instead of crashing the module mock.
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

const SHELL = {
  id: 'n-1',
  name: 'Shell Darnytsia',
  address: 'Kyiv, Darnytsia',
  lat: 50.41,
  lng: 30.65,
};

function renderMarker(over: Record<string, unknown> = {}) {
  const onPress = jest.fn();
  render(<StationMarker point={SHELL as any} brandColor="#ff0000" onPress={onPress} {...over} />);
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

describe('StationMarker - what the pin carries', () => {
  it('mounts no callout inside the marker', () => {
    // `Callout` is a second legacy view manager nested inside the marker's. Under the New
    // Architecture the interop layer resolves a pending child through that child's `contentView`,
    // which is nil until the child's own `finalizeUpdates` has run, and hands the result straight
    // to `insertReactSubview:atIndex:` -- so a pin that mounts with a callout in the same
    // transaction sometimes inserts nil into an NSMutableArray and dies fatally, with no JS error.
    // The map mounts a whole pool of markers at once, so "sometimes" would be every launch.
    renderMarker({ logo: undefined });

    expect(screen.queryByTestId('callout')).toBeNull();
  });

  it('names the station and its address on the pin itself', () => {
    // The callout used to be where the name and address were drawn. Now that the card is a
    // separate surface, the label is what keeps a pin from being an anonymous disc.
    renderMarker({ logo: undefined });

    expect(markerProps[0].accessibilityLabel).toBe('Shell Darnytsia, Kyiv, Darnytsia');
  });

  it('falls back to the no-address copy when the node carries none', () => {
    renderMarker({ logo: undefined, point: { name: 'Shell', address: '' } as any });

    expect(markerProps[0].accessibilityLabel).toBe('Shell, map.noAddress');
  });
});

describe('StationMarker - an empty slot', () => {
  it('is transparent and parked where no region of this network reaches', () => {
    renderMarker({ point: null });

    expect(markerProps[0].opacity).toBe(0);
    expect(markerProps[0].coordinate).toEqual({ latitude: 0, longitude: 0 });
    expect(markerProps[0].accessibilityLabel).toBeUndefined();
  });

  it('still hands the marker a child view, because a childless marker draws the stock MapKit pin', () => {
    // `AIRMapMarker.shouldUsePinView` is `reactSubviews.count == 0 && !imageSrc`. Empty the marker
    // of children and the pool stops being 120 invisible markers and becomes 120 red Apple pins --
    // so the container is what carries the emptiness, and it never leaves.
    renderMarker({ point: null });

    const child: any = markerProps[0].children;
    expect(React.isValidElement(child) && child.type).toBe(View);
    // The emptiness is what goes inside that view -- the view itself never leaves, or the marker has
    // no react subviews and `shouldUsePinView` hands it the platform pin instead.
    expect(child.props.children).toBeFalsy();
  });

  it('draws the station at full opacity, on its own coordinates', () => {
    renderMarker();

    expect(markerProps[0].opacity).toBe(1);
    expect(markerProps[0].coordinate).toEqual({ latitude: 50.41, longitude: 30.65 });
    expect(React.isValidElement(markerProps[0].children)).toBe(true);
    expect(markerProps[0].children.props.children).toBeTruthy();
  });
});

describe('StationMarker - a recycled slot', () => {
  it('rebuilds the pin and tracks it again when the marker is handed another station', () => {
    // The native marker is reused, so nothing about the new АЗК arrives by remounting it: the pin
    // subtree has to be keyed to the station and the raster redone, or a recycled slot keeps the
    // previous brand's disc. Rendered as one component re-rendered in place, because that is the
    // only case where a fresh mount would hide the bug.
    const { rerender } = render(
      <StationMarker point={SHELL as any} brandColor="#ff0000" logo={1 as any} />,
    );

    act(() => {
      screen.UNSAFE_getAllByType(Image)[0].props.onLoad();
    });
    expect(markerProps[markerProps.length - 1].tracksViewChanges).toBe(false);

    rerender(
      <StationMarker
        point={{ ...SHELL, id: 'n-2', name: 'KLO Brest', address: 'Brest, Mira, 1' } as any}
        brandColor="#00ff00"
        logo={1 as any}
      />,
    );

    expect(markerProps[markerProps.length - 1].tracksViewChanges).toBe(true);
    expect(markerProps[markerProps.length - 1].accessibilityLabel).toBe(
      'KLO Brest, Brest, Mira, 1',
    );
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
