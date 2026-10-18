import { describe, expect, it } from "vitest";
import { marginWarningFor, type VoucherTermFuelDto } from "./marginWarning";

const fuel = (name: string, marginPerLiterUah: number): VoucherTermFuelDto => ({
  fuelTypeId: name.toLowerCase(),
  name,
  brandId: "okko",
  marginPerLiterUah,
});

const FLOOR = 0.5;

describe("marginWarningFor", () => {
  it("says nothing when the discount leaves every fuel well above the floor", () => {
    expect(
      marginWarningFor(2, FLOOR, [fuel("ДП ЄВРО", 5), fuel("ДП", 3)]),
    ).toBeUndefined();
  });

  it("says nothing for a zero discount", () => {
    // An unpriced tier is not a discount that loses money; it is a tier nobody has set yet.
    expect(marginWarningFor(0, FLOOR, [fuel("ДП ЄВРО", 5)])).toBeUndefined();
  });

  it("says nothing when it has no catalog to judge against", () => {
    // A failed or empty catalog must not paint every tier red.
    expect(marginWarningFor(5, FLOOR, [])).toBeUndefined();
  });

  it("blocks when the discount would sell under cost", () => {
    // Shelf = cost + margin, so a discount larger than the margin lands below cost and checkout refuses the
    // line. The customer then sees a term that is mysteriously unavailable, which is why this is surfaced
    // where somebody can still change the number.
    const warning = marginWarningFor(4, FLOOR, [fuel("ДП ЄВРО", 3)]);
    expect(warning?.tone).toBe("blocked");
    expect(warning?.fuelCount).toBe(1);
    expect(warning?.worst).toContain("ДП ЄВРО");
    expect(warning?.remaining).toBeLessThan(0);
  });

  it("warns without blocking when the margin left is under the floor but positive", () => {
    // This is the sale that goes through and earns 10 kopecks. Nothing refuses it, so the only chance to
    // notice it is here.
    const warning = marginWarningFor(2.6, FLOOR, [fuel("ДП ЄВРО", 3)]);
    expect(warning?.tone).toBe("thin");
    expect(warning?.remaining).toBeCloseTo(0.4);
  });

  it("treats landing exactly on the floor as acceptable", () => {
    // Zero is not "under the floor", and the boundary is the whole point of a policy: margin exactly at the
    // floor is what a manager is aiming at, not a mistake.
    expect(marginWarningFor(2.5, FLOOR, [fuel("ДП ЄВРО", 3)])).toBeUndefined();
  });

  it("counts only the fuels the discount actually hurts", () => {
    // The ladder is one global setting while margins are per fuel, so a discount is never simply safe: it is
    // safe on the roomy fuels and unsellable on the thin ones. Reporting only a total would hide which.
    const warning = marginWarningFor(5, FLOOR, [
      fuel("ДП ЄВРО", 5), // exactly on cost
      fuel("А95", 0.2), // no margin
      fuel("UPG95", 4), // room to spare
    ]);

    expect(warning?.tone).toBe("blocked");
    expect(warning?.fuelCount).toBe(2);
    expect(warning?.totalFuels).toBe(3);
    // The worst offender is named, so the message is actionable rather than a bare count.
    expect(warning?.worst).toContain("А95");
  });

  it("never reports a zero-margin fuel as merely thin", () => {
    // A fuel with no margin at all is blocked, not thin, however small the discount is.
    expect(marginWarningFor(0.1, FLOOR, [fuel("Газ", 0)])?.tone).toBe(
      "blocked",
    );
  });

  // The regression: the stations-to-brands rename moved this field to `brandId` on the server and in
  // the C# DTO, but not here, so `worst.stationId.toUpperCase()` threw and the React error boundary
  // took the entire admin panel down instead of one label. This asserts the label names the brand.
  it("names the brand on the worst fuel", () => {
    expect(marginWarningFor(5, FLOOR, [fuel("А95", 0.2)])?.worst).toBe(
      "А95 (OKKO)",
    );
  });

  // A missing field must degrade the label, never the page. The payload is untyped JSON from the
  // API, so nothing stops the next server-side rename from arriving here the same way.
  it("still describes the warning when the brand is absent", () => {
    const nameless = {
      fuelTypeId: "x",
      name: "А95",
      marginPerLiterUah: 0.2,
    } as unknown as VoucherTermFuelDto;

    const warning = marginWarningFor(5, FLOOR, [nameless]);

    expect(warning?.tone).toBe("blocked");
    expect(warning?.worst).toBe("А95 ()");
  });
});
