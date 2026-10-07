import { useCartStore } from './cartStore';
import type { CartItem } from '../types';
import type { FuelPackage, Station, FuelType } from '../../../core/types/api';

/**
 * The cart is a money path with no test at all, and three of its rules are the kind
 * that lose money quietly rather than throwing:
 *
 * - a line priced for a chosen term quotes `termLinePrice`, and the totals must use
 *   that number rather than re-deriving a discount the customer never saw;
 * - a quantity of zero *removes* the line instead of pricing a free one;
 * - two lines of the same package at the same station and fuel merge into one.
 */

const station = (id: string) => ({ id, name: 'Shell ' + id }) as Station;
const fuel = (id: string) => ({ id, name: 'A95' }) as FuelType;
const pkg = (id: string, price: number) => ({ id, price }) as FuelPackage;

const item = (over: Partial<Omit<CartItem, 'id'>> = {}): Omit<CartItem, 'id'> => ({
  package: pkg('p1', 500),
  station: station('s1'),
  fuel: fuel('f1'),
  quantity: 1,
  ...over,
});

const reset = () =>
  useCartStore.setState({
    selectedStation: null,
    selectedFuel: null,
    selectedPackage: null,
    cart: [],
    promocode: '',
    discount: 0,
  });

beforeEach(reset);

describe('cart totals', () => {
  it('sums line price times quantity', () => {
    useCartStore.getState().addToCart(item({ package: pkg('p1', 500), quantity: 2 }));
    useCartStore.getState().addToCart(item({ package: pkg('p2', 300), quantity: 1 }));

    expect(useCartStore.getState().getCartTotal()).toBe(1300);
  });

  it('uses the quoted term price instead of the package price when there is one', () => {
    // Same package, but the customer was quoted 420 for the term they chose.
    useCartStore
      .getState()
      .addToCart(
        item({ package: pkg('p1', 500), termCode: '1w', termLinePrice: 420, quantity: 1 }),
      );

    // Charging 500 here would bill a discount the customer was never shown.
    expect(useCartStore.getState().getCartTotal()).toBe(420);
  });

  it('falls back to the package price when no term was chosen', () => {
    useCartStore.getState().addToCart(item({ package: pkg('p1', 500), quantity: 1 }));

    expect(useCartStore.getState().getCartTotal()).toBe(500);
  });

  it('counts quantities, not lines', () => {
    useCartStore.getState().addToCart(item({ package: pkg('p1', 500), quantity: 3 }));
    useCartStore.getState().addToCart(item({ package: pkg('p2', 300), quantity: 2 }));

    expect(useCartStore.getState().getCartItemCount()).toBe(5);
  });

  it('is zero for an empty cart', () => {
    expect(useCartStore.getState().getCartTotal()).toBe(0);
    expect(useCartStore.getState().getCartItemCount()).toBe(0);
  });
});

describe('promo codes', () => {
  it('applies the discount a code carries', () => {
    expect(useCartStore.getState().applyPromocode('FUEL10')).toBe(true);
    expect(useCartStore.getState().discount).toBe(10);
  });

  it('is case-insensitive, because the customer types it', () => {
    expect(useCartStore.getState().applyPromocode('fuel10')).toBe(true);
    expect(useCartStore.getState().promocode).toBe('FUEL10');
  });

  it('refuses an unknown code and leaves the discount alone', () => {
    useCartStore.getState().applyPromocode('FUEL10');
    const before = useCartStore.getState().getDiscountedTotal();

    expect(useCartStore.getState().applyPromocode('NOPE')).toBe(false);
    expect(useCartStore.getState().discount).toBe(10);
    expect(useCartStore.getState().getDiscountedTotal()).toBe(before);
  });

  it('discounts the term-quoted total, not the list price', () => {
    useCartStore
      .getState()
      .addToCart(
        item({ package: pkg('p1', 500), termCode: '1w', termLinePrice: 400, quantity: 1 }),
      );
    useCartStore.getState().applyPromocode('SAVE15');

    expect(useCartStore.getState().getDiscountedTotal()).toBe(340);
  });

  it('clears the code and the discount together', () => {
    useCartStore.getState().applyPromocode('POWER20');
    useCartStore.getState().clearPromocode();

    expect(useCartStore.getState().promocode).toBe('');
    expect(useCartStore.getState().discount).toBe(0);
  });
});

describe('cart lines', () => {
  it('merges the same package at the same station and fuel into one line', () => {
    useCartStore.getState().addToCart(item({ quantity: 2 }));
    useCartStore.getState().addToCart(item({ quantity: 3 }));

    const cart = useCartStore.getState().cart;
    expect(cart).toHaveLength(1);
    expect(cart[0].quantity).toBe(5);
  });

  it('keeps a different station as a separate line', () => {
    useCartStore.getState().addToCart(item({ station: station('s1'), quantity: 1 }));
    useCartStore.getState().addToCart(item({ station: station('s2'), quantity: 1 }));

    expect(useCartStore.getState().cart).toHaveLength(2);
  });

  it('removes the line when the quantity drops to zero', () => {
    useCartStore.getState().addToCart(item({ quantity: 2 }));
    const [line] = useCartStore.getState().cart;

    useCartStore.getState().updateQuantity(line.id, 0);

    // A zero-quantity line would still be summed by nothing, but it would sit in the
    // basket as a free row the customer has to think about.
    expect(useCartStore.getState().cart).toHaveLength(0);
  });

  it('removes the line when the quantity is driven negative', () => {
    useCartStore.getState().addToCart(item({ quantity: 2 }));
    const [line] = useCartStore.getState().cart;

    useCartStore.getState().updateQuantity(line.id, -1);

    expect(useCartStore.getState().cart).toHaveLength(0);
  });

  it('sets the term a line is bought for without touching its quantity', () => {
    useCartStore.getState().addToCart(item({ quantity: 3 }));
    const [line] = useCartStore.getState().cart;

    useCartStore.getState().setTerm(line.id, '1w');

    const updated = useCartStore.getState().cart[0];
    expect(updated.termCode).toBe('1w');
    expect(updated.quantity).toBe(3);
  });

  it('empties the cart and forgets the promo code together', () => {
    useCartStore.getState().addToCart(item());
    useCartStore.getState().applyPromocode('FUEL10');

    useCartStore.getState().clearCart();

    expect(useCartStore.getState().cart).toHaveLength(0);
    expect(useCartStore.getState().discount).toBe(0);
  });
});

describe('selection', () => {
  it('drops the fuel and package when the station changes', () => {
    useCartStore.getState().selectStation(station('s1'));
    useCartStore.getState().selectFuel(fuel('f1'));
    useCartStore.getState().selectPackage(pkg('p1', 500));

    useCartStore.getState().selectStation(station('s2'));

    // Fuel and package belong to the old station, so keeping them would price fuel
    // against a station that no longer applies.
    expect(useCartStore.getState().selectedFuel).toBeNull();
    expect(useCartStore.getState().selectedPackage).toBeNull();
  });

  it('drops the package when the fuel changes', () => {
    useCartStore.getState().selectStation(station('s1'));
    useCartStore.getState().selectFuel(fuel('f1'));
    useCartStore.getState().selectPackage(pkg('p1', 500));

    useCartStore.getState().selectFuel(fuel('f2'));

    expect(useCartStore.getState().selectedPackage).toBeNull();
  });
});
