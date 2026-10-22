export interface BrandFuelDto {
  id: string;
  name: string;
  // NULLABLE, and it has to stay nullable. The cost is "not stated" until an operator says what
  // we pay - the catalogue import knows the column price and nothing about that - and a blank that
  // arrives as a 0 becomes a fuel priced at its margin with no below-cost guard.
  supplierPricePerLiter: number | null;
  marginUahPerLiter: number | null;
  marginPercent: number | null;
  finalPricePerLiter: number;
  pumpPricePerLiter: number | null;
  discountPerLiter: number;
  allowBelowCost: boolean;
  /**
   * The fuel's code at the network ("A-95", "DP"). Null when it has none — and that is worth
   * seeing, because a fuel without a code is what the catalogue import can no longer match.
   */
  brandFuelCode: string | null;
  packageLiters: number[];
  /** Whether this fuel exists only to rehearse a flow. Decides whether its packages are sellable. */
  isTestData: boolean;
  /** Set only when this fuel's packages disagree; see BrandFuelPriceSpreadDto on the API. */
  priceSpread: {
    minSupplierPricePerLiter: number;
    maxSupplierPricePerLiter: number;
    minFinalPricePerLiter: number;
    maxFinalPricePerLiter: number;
  } | null;
}

export interface BrandDto {
  id: string;
  /**
   * The network's own code ("okko"). This is what the API and the importers key on; `id` is the
   * uuid the database joins on and nothing outside it needs to know it. Server-assigned on create,
   * immutable afterwards — renaming it here would re-point every import file at nothing.
   */
  slug: string;
  name: string;
  logoText: string;
  color: string;
  sortOrder: number;
  fuels: BrandFuelDto[];
  nominals: number[];
}

export interface AuditEventDto {
  id: string;
  aggregateType: string;
  aggregateId: string;
  eventType: string;
  oldValue: string | null;
  newValue: string;
  changedByUserName: string | null;
  summary: string;
  changedAtUtc: string;
}

export interface BrandFormValues {
  name: string;
  logoText: string;
  color: string;
  sortOrder: number;
}

export const DEFAULT_PROVIDER_COLOR = "#00ff80";

export function emptyBrandForm(): BrandFormValues {
  return {
    name: "",
    logoText: "",
    color: DEFAULT_PROVIDER_COLOR,
    sortOrder: 999,
  };
}
