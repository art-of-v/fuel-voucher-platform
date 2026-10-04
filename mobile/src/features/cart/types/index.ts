import type { FuelPackage, Station, FuelType } from '../../../core/types/api';

export interface CartItem {
  id: string;
  package: FuelPackage;
  station: Station;
  fuel: FuelType;
  quantity: number;
  /**
   * The validity term this fuel is bought for ('1w'...'6m'). Undefined means the
   * voucher's full remaining term at the undiscounted price - the behaviour when
   * short-term selling is off. Set by the term picker at checkout.
   */
  termCode?: string;
}

export const PROMO_CODES: Record<string, number> = {
  FUEL10: 10,
  SAVE15: 15,
  POWER20: 20,
  LEMBERG25: 25,
};
