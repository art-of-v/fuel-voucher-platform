import { QueryClient, QueryClientProvider } from "@tanstack/react-query";
import { render, screen, fireEvent, waitFor } from "@testing-library/react";
import { describe, expect, it, vi, beforeEach } from "vitest";
import type { ReactNode } from "react";

import {
  BatchCostTimeline,
  type BatchCostTimelineResponse,
} from "./BatchCostTimeline";

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

const RESPONSE: BatchCostTimelineResponse = {
  importId: "import-1",
  entries: [
    {
      exchangeBatchId: "xb-1",
      atUtc: "2026-10-07T12:00:00Z",
      fuelTypeId: "okko-dp",
      brand: "okko",
      vouchersExchangedOut: 5,
      litersExchangedOut: 250,
      surchargeUah: 300,
      costPerLiterBefore: 40,
      costPerLiterAfter: 45,
    },
  ],
  currentCosts: [
    {
      fuelTypeId: "okko-dp",
      fuelTypeName: "Дизель",
      brand: "okko",
      voucherCount: 10,
      totalLiters: 500,
      costPerLiter: 45,
      blendedCostPerLiter: 46.2,
    },
  ],
};

function renderTimeline(
  props: { importId: string | null; enabled?: boolean } = {
    importId: "import-1",
  },
) {
  const client = new QueryClient({
    defaultOptions: { queries: { retry: false } },
  });
  const wrap = ({ children }: { children: ReactNode }) => (
    <QueryClientProvider client={client}>{children}</QueryClientProvider>
  );
  return render(
    <BatchCostTimeline
      importId={props.importId}
      enabled={props.enabled ?? true}
    />,
    { wrapper: wrap },
  );
}

describe("BatchCostTimeline", () => {
  beforeEach(() => vi.clearAllMocks());

  // The panel above edits a cost. This is the audit view - an operator has to be able to see where
  // that number came from without opening another screen.
  it("shows the surcharge and the cost it moved the batch from to", async () => {
    mockedApiRequest.mockResolvedValue(RESPONSE);
    renderTimeline();

    fireEvent.click(screen.getByText("imports.costTimeline"));

    await waitFor(() => expect(screen.getByText(/5 · 250 L/)).toBeTruthy());
    expect(screen.getByText("2026-10-07")).toBeTruthy();
    expect(screen.getByText("40.00 ₴ → 45.00 ₴")).toBeTruthy();
  });

  // Lazy by design: an operator reading batch costs should not pay for an audit view they never open.
  it("does not call the API until it is expanded", () => {
    mockedApiRequest.mockResolvedValue(RESPONSE);
    renderTimeline();

    expect(mockedApiRequest).not.toHaveBeenCalled();
  });

  it("says so plainly when no exchange has touched the batch", async () => {
    mockedApiRequest.mockResolvedValue({ ...RESPONSE, entries: [] });
    renderTimeline();

    fireEvent.click(screen.getByText("imports.costTimeline"));

    await waitFor(() =>
      expect(screen.getByText("imports.costTimelineEmpty")).toBeTruthy(),
    );
  });

  // An uncosted voucher has no "before" to show. A literal 0.00 would read as "this batch cost
  // nothing", which is a different and wrong statement.
  it("renders a missing cost as a dash rather than zero", async () => {
    mockedApiRequest.mockResolvedValue({
      ...RESPONSE,
      entries: [{ ...RESPONSE.entries[0], costPerLiterBefore: null }],
    });
    renderTimeline();

    fireEvent.click(screen.getByText("imports.costTimeline"));

    await waitFor(() => expect(screen.getByText("— → 45.00 ₴")).toBeTruthy());
  });

  it("does not fetch when the tab is not open", () => {
    mockedApiRequest.mockResolvedValue(RESPONSE);
    renderTimeline({ importId: "import-1", enabled: false });

    fireEvent.click(screen.getByText("imports.costTimeline"));

    expect(mockedApiRequest).not.toHaveBeenCalled();
  });
});
