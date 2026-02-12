
import { create } from 'zustand';
import { createJSONStorage, persist } from 'zustand/middleware';
import AsyncStorage from '@react-native-async-storage/async-storage';
import { FuelPackage, Station, FuelType } from './mock-data';

export interface CartItem {
  id: string;
  package: FuelPackage;
  station: Station;
  fuel: FuelType;
  quantity: number;
}

interface AppState {
  // Selection flow
  selectedStation: Station | null;
  selectedFuel: FuelType | null;
  selectedPackage: FuelPackage | null;

  // Cart
  cart: CartItem[];
  promocode: string;
  discount: number;

  // Selection actions
  selectStation: (station: Station | null) => void;
  selectFuel: (fuel: FuelType | null) => void;
  selectPackage: (pkg: FuelPackage | null) => void;
  resetSelection: () => void;

  // Cart actions
  addToCart: (item: Omit<CartItem, 'id'>) => void;
  updateQuantity: (itemId: string, quantity: number) => void;
  removeFromCart: (itemId: string) => void;
  clearCart: () => void;
  applyPromocode: (code: string) => boolean;
  clearPromocode: () => void;

  // Computed
  getCartTotal: () => number;
  getCartItemCount: () => number;
  getDiscountedTotal: () => number;
}

const PROMOCODES: Record<string, number> = {
  'FUEL10': 10,
  'SAVE15': 15,
  'POWER20': 20,
  'LEMBERG25': 25,
};

export const useStore = create<AppState>()(
  persist(
    (set, get) => ({
      selectedStation: null,
      selectedFuel: null,
      selectedPackage: null,
      cart: [],
      promocode: '',
      discount: 0,

      selectStation: (station) => set({ selectedStation: station }),
      selectFuel: (fuel) => set({ selectedFuel: fuel }),
      selectPackage: (pkg) => set({ selectedPackage: pkg }),

      resetSelection: () => set({
        selectedStation: null,
        selectedFuel: null,
        selectedPackage: null
      }),

      addToCart: (item) => set((state) => {
        const existingIndex = state.cart.findIndex(
          (c) => c.package.id === item.package.id
        );

        if (existingIndex >= 0) {
          const newCart = [...state.cart];
          newCart[existingIndex].quantity += item.quantity;
          return { cart: newCart };
        }

        return {
          cart: [...state.cart, { ...item, id: `cart-${Date.now()}-${Math.random()}` }]
        };
      }),

      updateQuantity: (itemId, quantity) => set((state) => {
        if (quantity <= 0) {
          return { cart: state.cart.filter((c) => c.id !== itemId) };
        }
        return {
          cart: state.cart.map((c) =>
            c.id === itemId ? { ...c, quantity } : c
          )
        };
      }),

      removeFromCart: (itemId) => set((state) => ({
        cart: state.cart.filter((c) => c.id !== itemId)
      })),

      clearCart: () => set({ cart: [], promocode: '', discount: 0 }),

      applyPromocode: (code) => {
        const upperCode = code.toUpperCase();
        const discountPercent = PROMOCODES[upperCode];
        if (discountPercent) {
          set({ promocode: upperCode, discount: discountPercent });
          return true;
        }
        return false;
      },

      clearPromocode: () => set({ promocode: '', discount: 0 }),

      getCartTotal: () => {
        const { cart } = get();
        return cart.reduce((sum, item) => sum + (item?.package?.price ?? 0) * (item?.quantity ?? 0), 0);
      },

      getCartItemCount: () => {
        const { cart } = get();
        return cart.reduce((sum, item) => sum + (item?.quantity ?? 0), 0);
      },

      getDiscountedTotal: () => {
        const { cart, discount } = get();
        const total = cart.reduce((sum, item) => sum + (item?.package?.price ?? 0) * (item?.quantity ?? 0), 0);
        return Math.round(total * (1 - discount / 100));
      },
    }),
    {
      name: 'fuel-app-cart',
      storage: createJSONStorage(() => AsyncStorage),
    }
  )
);
