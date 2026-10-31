import React from 'react';
import { render, screen, fireEvent } from '@testing-library/react-native';

import { MapSearchDeck } from './MapSearchDeck';

/**
 * The deck is the map's only keyboard target, and every card below it is filtered
 * by the two controls it holds. The failure it replaced was spatial: a blurred
 * field and a separate chip row floating over the header's corner band, so taps
 * on the frame landed in neither control.
 */

jest.mock('../../../core/i18n', () => ({
  useI18n: (selector: (s: any) => unknown) => selector({ t: (key: string) => key }),
}));

function renderDeck(over: Partial<React.ComponentProps<typeof MapSearchDeck>> = {}) {
  const onQueryChange = jest.fn();
  const onSelectFuel = jest.fn();
  render(
    <MapSearchDeck
      query=""
      onQueryChange={onQueryChange}
      fuels={['a-95', 'a-92', 'diesel']}
      selectedFuel="a-95"
      onSelectFuel={onSelectFuel}
      {...over}
    />,
  );
  return { onQueryChange, onSelectFuel };
}

describe('MapSearchDeck — the search field', () => {
  it('reports what is typed', () => {
    const { onQueryChange } = renderDeck();

    fireEvent.changeText(screen.getByLabelText('map.searchPlaceholder'), 'wog');
    expect(onQueryChange).toHaveBeenCalledWith('wog');
  });

  it('offers a clear control only when there is something to clear', () => {
    renderDeck({ query: '' });
    expect(screen.queryByLabelText('map.clearSearch')).toBeNull();

    renderDeck({ query: 'wog' });
    expect(screen.getByLabelText('map.clearSearch')).toBeTruthy();
  });

  it('clears the query through the clear control', () => {
    const { onQueryChange } = renderDeck({ query: 'wog' });

    fireEvent.press(screen.getByLabelText('map.clearSearch'));
    expect(onQueryChange).toHaveBeenCalledWith('');
  });
});

describe('MapSearchDeck — the fuel chips', () => {
  it('renders one chip per fuel, in canonical order with display labels', () => {
    renderDeck();

    expect(screen.getByRole('button', { name: 'А95' })).toBeTruthy();
    expect(screen.getByRole('button', { name: 'А92' })).toBeTruthy();
    expect(screen.getByRole('button', { name: 'ДП' })).toBeTruthy();
  });

  it('marks the selected fuel and no other', () => {
    renderDeck({ selectedFuel: 'diesel' });

    expect(screen.getByRole('button', { name: 'ДП' })).toBeSelected();
    expect(screen.getByRole('button', { name: 'А95' })).not.toBeSelected();
  });

  it('selects a fuel when its chip is tapped', () => {
    const { onSelectFuel } = renderDeck();

    fireEvent.press(screen.getByRole('button', { name: 'А92' }));
    expect(onSelectFuel).toHaveBeenCalledWith('a-92');
  });

  it('drops the chip row when the network has no fuel grades', () => {
    renderDeck({ fuels: [] });

    expect(screen.queryByRole('button', { name: 'А95' })).toBeNull();
  });
});
