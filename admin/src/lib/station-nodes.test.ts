import { describe, it, expect } from "vitest";
import { isValidLat, isValidLng, isValidCoordinate, deriveNodeId } from "./station-nodes";

describe("station-nodes WGS84 validation", () => {
  it("accepts in-range latitudes and rejects out-of-range", () => {
    expect(isValidLat(50.4501)).toBe(true);
    expect(isValidLat(-90)).toBe(true);
    expect(isValidLat(90)).toBe(true);
    expect(isValidLat(90.0001)).toBe(false);
    expect(isValidLat(-90.0001)).toBe(false);
    expect(isValidLat(NaN)).toBe(false);
    expect(isValidLat(Infinity)).toBe(false);
  });

  it("accepts in-range longitudes and rejects out-of-range", () => {
    expect(isValidLng(30.5234)).toBe(true);
    expect(isValidLng(-180)).toBe(true);
    expect(isValidLng(180)).toBe(true);
    expect(isValidLng(180.0001)).toBe(false);
    expect(isValidLng(-180.0001)).toBe(false);
    expect(isValidLng(NaN)).toBe(false);
  });

  it("requires both coordinates valid", () => {
    expect(isValidCoordinate(50.45, 30.52)).toBe(true);
    expect(isValidCoordinate(91, 30.52)).toBe(false);
    expect(isValidCoordinate(50.45, 181)).toBe(false);
  });
});

describe("deriveNodeId — parity with backend {stationId}-{lat:F5}-{lng:F5}", () => {
  it("formats coordinates to 5 decimals", () => {
    expect(deriveNodeId("wog", 50.4501, 30.5234)).toBe("wog-50.45010-30.52340");
  });

  it("pads and rounds to exactly 5 decimals", () => {
    expect(deriveNodeId("okko", 49.8, -24.123456)).toBe("okko-49.80000--24.12346");
  });
});
