import { renderHook, waitFor } from '@testing-library/react-native';
import { createElement, useState, type PropsWithChildren } from 'react';
import { QueryClient, QueryClientProvider } from '@tanstack/react-query';
import { getPackages } from '../api/getPackages';
import { usePackages } from './usePackages';
import type { FuelPackage } from '../../../core/types/api';

jest.mock('../api/getPackages', () => ({ getPackages: jest.fn() }));
jest.mock('../../../core/hooks/useAppStateActive', () => ({ useAppStateActive: () => false }));

const packageRow = (
  id: string,
  fuelTypeId: string,
  fuelName: string,
  liters: number,
  isAvailable: boolean,
  brandId = 'okko',
): FuelPackage => ({
  id,
  brandId,
  brandSlug: brandId,
  fuelTypeId,
  fuelName,
  liters,
  price: 862,
  originalPrice: 929,
  isAvailable,
});

function Wrapper({ children }: PropsWithChildren) {
  const [client] = useState(
    () =>
      new QueryClient({
        defaultOptions: { queries: { retry: false, gcTime: 0 } },
      }),
  );
  return createElement(QueryClientProvider, { client }, children);
}

beforeEach(() => {
  jest.mocked(getPackages).mockReset();
  jest
    .mocked(getPackages)
    .mockResolvedValue([
      packageRow('a95-20', 'regular', 'А-95', 20, false),
      packageRow('pulse-10', 'premium', 'Пульс 95', 10, false),
      packageRow('a95-10', 'regular', 'А-95', 10, true),
      packageRow('same-name', 'another-fuel', 'А-95', 10, true),
      packageRow('other-brand', 'regular', 'А-95', 10, true, 'wog'),
    ]);
});

it('selects only the exact fuel and brand, retaining its unavailable denominations in order', async () => {
  const { result } = renderHook(() => usePackages('okko', 'regular'), { wrapper: Wrapper });
  await waitFor(() => expect(result.current.isSuccess).toBe(true));
  expect(result.current.data?.map((p) => [p.id, p.isAvailable])).toEqual([
    ['a95-10', true],
    ['a95-20', false],
  ]);
});

it('changes the cached selection when the fuel ID changes', async () => {
  const { result, rerender } = renderHook(({ fuel }) => usePackages('okko', fuel), {
    initialProps: { fuel: 'regular' },
    wrapper: Wrapper,
  });
  await waitFor(() => expect(result.current.isSuccess).toBe(true));
  rerender({ fuel: 'premium' });
  await waitFor(() => expect(result.current.data?.map((p) => p.id)).toEqual(['pulse-10']));
});

it.each([
  [undefined, 'regular'],
  ['okko', undefined],
])('does not fetch without both selected identities (%s, %s)', (brandId, fuelTypeId) => {
  const { result } = renderHook(() => usePackages(brandId, fuelTypeId), { wrapper: Wrapper });
  expect(result.current.fetchStatus).toBe('idle');
  expect(getPackages).not.toHaveBeenCalled();
});
