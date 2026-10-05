/**
 * What a term discount would cost the business, judged against every fuel's thinnest margin.
 *
 * This lives apart from the settings screen because it is a pricing rule, not a rendering detail: it is the
 * difference between a ladder that looks right and one that quietly sells nothing. The screen shows the
 * verdict; this decides it.
 */

export interface VoucherTermFuelDto {
  fuelTypeId: string;
  name: string;
  stationId: string;
  /** Least margin across this fuel's packages, UAH per litre. Zero means it has none. */
  marginPerLiterUah: number;
}

export type MarginWarning = {
  /** "blocked" is refused at checkout; "thin" goes through and earns almost nothing. */
  tone: "blocked" | "thin";
  fuelCount: number;
  totalFuels: number;
  /** The fuel that comes off worst, so the message is actionable rather than a bare count. */
  worst: string;
  /** What that fuel would be left with, UAH per litre. Negative when blocked. */
  remaining: number;
};

/**
 * A discount eats margin one-for-one: the shelf price is cost plus margin, so what is left after a discount
 * of `d` is `margin - d`.
 *
 * Below zero the checkout guard refuses the sale outright, which the customer experiences as a term that is
 * mysteriously unavailable rather than as a pricing decision — so it is worth catching here, where somebody
 * can still change the number.
 *
 * Under the floor but positive is the quieter case, and the reason this exists: the sale goes through and
 * earns 10 kopecks. Nothing refuses it, so without a warning here it would be sold indefinitely.
 *
 * Fuels are judged on their *thinnest* package, because a discount is a per-litre figure and the package with
 * the least room decides whether the fuel can carry it. Averaging would hide the fuel that cannot be sold.
 */
export function marginWarningFor(
  discountPerLiter: number,
  floorUah: number,
  fuels: VoucherTermFuelDto[],
): MarginWarning | undefined {
  if (!fuels.length || discountPerLiter <= 0) return undefined;

  const blocked = fuels.filter(
    (fuel) => fuel.marginPerLiterUah - discountPerLiter < 0,
  );
  if (blocked.length) {
    return describe("blocked", blocked, discountPerLiter, fuels.length);
  }

  const thin = fuels.filter(
    (fuel) =>
      fuel.marginPerLiterUah - discountPerLiter < floorUah &&
      fuel.marginPerLiterUah > 0,
  );
  if (!thin.length) return undefined;

  return describe("thin", thin, discountPerLiter, fuels.length);
}

function describe(
  tone: MarginWarning["tone"],
  affected: VoucherTermFuelDto[],
  discountPerLiter: number,
  totalFuels: number,
): MarginWarning {
  const worst = affected.reduce((a, b) =>
    b.marginPerLiterUah < a.marginPerLiterUah ? b : a,
  );

  return {
    tone,
    fuelCount: affected.length,
    totalFuels,
    worst: `${worst.name} (${worst.stationId.toUpperCase()})`,
    remaining: worst.marginPerLiterUah - discountPerLiter,
  };
}
