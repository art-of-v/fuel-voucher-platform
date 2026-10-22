import { QueryClient, QueryClientProvider } from "@tanstack/react-query";
import { render, screen, fireEvent, waitFor } from "@testing-library/react";
import { describe, expect, it, vi } from "vitest";
import type { ReactNode } from "react";

import {
  FuelPriceHistory,
  type FuelPriceAt,
  type FuelPriceHistoryEntry,
} from "./FuelPriceHistory";

vi.mock("@/lib/api-client", () => ({ apiRequest: vi.fn() }));
vi.mock("@/lib/i18n", () => ({
  useI18n: () => ({
    t: (key: string) => key,
    language: "uk",
    setLanguage: vi.fn(),
  }),
}));

import { apiRequest } from "@/lib/api-client";
const mockedApiRequest = apiRequest as unknown as ReturnType<typeof vi.fn>;

const LOG: FuelPriceHistoryEntry[] = [
  {
    id: "row-2",
    offerId: "okko-95",
    dataKind: "real",
    supplierPricePerLiter: 48.9,
    marginUahPerLiter: 2,
    marginPercent: null,
    finalPricePerLiter: 50.9,
    pumpPricePerLiter: 51,
    minDiscountPerLiter: 0.5,
    effectiveFromUtc: "2026-10-09T08:00:00Z",
    changedByUserId: null,
    reason: "pump-import",
  },
  {
    id: "row-1",
    offerId: "okko-95",
    dataKind: "real",
    supplierPricePerLiter: 49.5,
    marginUahPerLiter: 3,
    marginPercent: null,
    finalPricePerLiter: 51.5,
    pumpPricePerLiter: 52.3,
    minDiscountPerLiter: 0.5,
    effectiveFromUtc: "2026-10-01T08:00:00Z",
    changedByUserId: null,
    reason: "operator",
  },
];

function renderHistory() {
  const client = new QueryClient({
    defaultOptions: { queries: { retry: false } },
  });
  const wrap = ({ children }: { children: ReactNode }) => (
    <QueryClientProvider client={client}>{children}</QueryClientProvider>
  );
  return render(<FuelPriceHistory fuelId="fuel-1" />, { wrapper: wrap });
}

/** Answers both endpoints; the as-of one only when asked. */
function mockApi(priceAt: FuelPriceAt | null = null) {
  mockedApiRequest.mockImplementation(async (_method: string, url: string) => {
    if (url.includes("price-at")) return priceAt;
    return LOG;
  });
}

function urls(): string[] {
  return mockedApiRequest.mock.calls.map((call) => String(call[1]));
}

describe("FuelPriceHistory", () => {
  it("reads the log without asking for a date the operator did not give", async () => {
    mockApi();
    renderHistory();

    await waitFor(() => expect(screen.getByText("50,90")).toBeTruthy());

    expect(urls().every((u) => u.includes("price-history"))).toBe(true);
    // An empty date box is not a question, and asking anyway would be a request nobody made.
    expect(urls().some((u) => u.includes("price-at"))).toBe(false);
  });

  it("shows every change with the reason that produced it", async () => {
    mockApi();
    renderHistory();

    await waitFor(() =>
      expect(screen.getByText("priceHistory.reasonOperator")).toBeTruthy(),
    );
    expect(screen.getByText("priceHistory.reasonPumpImport")).toBeTruthy();
    // Buy, margin and pump are all shown, because a price nobody can explain is not trusted.
    expect(screen.getByText("48,90")).toBeTruthy();
    expect(screen.getByText("2,00")).toBeTruthy();
  });

  it("asks what the price was on a date the operator picks", async () => {
    const at: FuelPriceAt = {
      ...LOG[1],
      effectiveFromUtc: "2026-10-01T08:00:00Z",
      supersededAtUtc: "2026-10-09T08:00:00Z",
    };
    mockApi(at);
    renderHistory();

    await waitFor(() => expect(screen.getByText("51,50")).toBeTruthy());
    fireEvent.change(screen.getByLabelText("priceHistory.atTitle"), {
      target: { value: "2026-10-03" },
    });

    await waitFor(() => expect(screen.getByText(/stoodFrom/)).toBeTruthy());
    expect(
      urls().some((u) => u.includes("price-at") && u.includes("2026-10-03")),
    ).toBe(true);
    expect(screen.getByText(/2026-10-01/)).toBeTruthy();
  });

  it("says so when no price was in effect on that date", async () => {
    mockApi(null);
    renderHistory();

    await waitFor(() => expect(screen.getByText("51,50")).toBeTruthy());
    fireEvent.change(screen.getByLabelText("priceHistory.atTitle"), {
      target: { value: "2026-09-01" },
    });

    // Not an error and not an empty table: "the fuel was not priced yet" is the answer.
    await waitFor(() =>
      expect(screen.getByText("priceHistory.atNone")).toBeTruthy(),
    );
  });

  it("marks a rehearsal row so it is never read as a live price", async () => {
    mockedApiRequest.mockImplementation(
      async (_method: string, url: string) => {
        if (url.includes("price-at")) return null;
        return [{ ...LOG[0], dataKind: "qa" }];
      },
    );
    renderHistory();

    await waitFor(() => expect(screen.getByText("qa")).toBeTruthy());
  });

  it("says when there is nothing recorded", async () => {
    mockedApiRequest.mockImplementation(async (_method: string, url: string) =>
      url.includes("price-at") ? null : [],
    );
    renderHistory();

    await waitFor(() =>
      expect(screen.getByText("priceHistory.empty")).toBeTruthy(),
    );
  });
});
