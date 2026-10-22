import { describe, it, expect, vi, beforeEach } from "vitest";
import { render, screen, waitFor } from "@testing-library/react";
import userEvent from "@testing-library/user-event";
import { QueryClient, QueryClientProvider } from "@tanstack/react-query";
import type { ReactNode } from "react";
import { ExchangeHistory } from "@/components/ExchangeHistory";
import { formatMoney } from "@/lib/money";

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

const SUPPLIER = "ФОП Стретович";

const BATCHES = {
  data: [
    {
      exchangeBatchId: "batch-1",
      atUtc: "2026-10-01T10:00:00Z",
      supplierId: "sup-1",
      supplierName: SUPPLIER,
      brands: ["OKKO"],
      fuelTypeIds: ["okko-95"],
      vouchersExchanged: 4,
      litersOut: 40,
      // The batch's whole charge. One voucher_exchanges row is one pair's quarter of this.
      surchargeUah: 100,
      costPerLiterBefore: 91.9375,
      costPerLiterAfter: 94.4375,
      recordedBy: "Operator One",
    },
  ],
  total: 1,
  page: 1,
  limit: 20,
  suppliers: [{ id: "sup-1", name: SUPPLIER }],
};

const DETAIL = {
  exchangeBatchId: "batch-1",
  atUtc: "2026-10-01T10:00:00Z",
  supplierId: "sup-1",
  supplierName: SUPPLIER,
  recordedBy: "Operator One",
  totalSurchargeUah: 100,
  totalLiters: 40,
  pairs: [
    {
      oldVoucherId: "old-1",
      oldVoucherNumber: "OLD-0001",
      oldLiters: 10,
      oldCostPerLiter: 88,
      newVoucherId: "new-1",
      newVoucherNumber: "NEW-0001",
      fuelTypeId: "okko-95",
      brand: "OKKO",
      surchargeShareUah: 25,
      newCostPerLiter: 90.5,
    },
    {
      oldVoucherId: "old-2",
      oldVoucherNumber: "OLD-0002",
      oldLiters: 10,
      oldCostPerLiter: 91.5,
      newVoucherId: "new-2",
      newVoucherNumber: "NEW-0002",
      fuelTypeId: "okko-95",
      brand: "OKKO",
      surchargeShareUah: 25,
      newCostPerLiter: 94,
    },
  ],
};

function renderHistory() {
  const client = new QueryClient({
    defaultOptions: { queries: { retry: false } },
  });
  const wrapper = ({ children }: { children: ReactNode }) => (
    <QueryClientProvider client={client}>{children}</QueryClientProvider>
  );
  return render(<ExchangeHistory />, { wrapper });
}

/**
 * Every rendered cell, whitespace stripped.
 *
 * `formatMoney` separates with a narrow no-break space (U+202F), which testing-library does not
 * normalise away — so comparing formatted strings directly fails on a character that has nothing to
 * do with the value under test.
 */
function cellTexts(): string[] {
  return Array.from(document.querySelectorAll("td, th")).map((cell) =>
    (cell.textContent ?? "").replace(/\s/g, ""),
  );
}

/** A row is loaded once its vouchers are on screen — headers render before any data arrives. */
async function waitForRows(): Promise<void> {
  await waitFor(() => expect(cellTexts()).toContain("4"));
}

const TOTAL = formatMoney(100).replace(/\s/g, "");
const SHARE = formatMoney(25).replace(/\s/g, "");

describe("ExchangeHistory", () => {
  beforeEach(() => {
    mockedApiRequest.mockReset();
  });

  it("shows a batch's whole surcharge, not one pair's share of it", async () => {
    mockedApiRequest.mockResolvedValue(BATCHES);
    renderHistory();
    await waitForRows();

    expect(screen.getByText("voucherExchange.historySurcharge")).toBeDefined();
    // The batch's 100, not the 25 that one row of voucher_exchanges holds. Reading one row as the
    // total shipped as a bug once already.
    expect(cellTexts()).toContain(TOTAL);
    expect(cellTexts()).not.toContain(SHARE);
  });

  it("lists every pair in a batch with its own cost", async () => {
    mockedApiRequest.mockImplementation((_method: string, url: string) =>
      Promise.resolve(url.includes("/batches/") ? DETAIL : BATCHES),
    );
    renderHistory();
    await waitForRows();

    await userEvent.click(screen.getAllByRole("row")[1]);
    await waitFor(() => expect(screen.getByText("OLD-0001")).toBeDefined());

    expect(screen.getByText("NEW-0001")).toBeDefined();
    expect(screen.getByText("OLD-0002")).toBeDefined();
    expect(screen.getByText("NEW-0002")).toBeDefined();
    // Each pair keeps ITS OWN cost — the reason an exchange is recorded per voucher at all.
    expect(cellTexts()).toContain("88.00→90.50");
    expect(cellTexts()).toContain("91.50→94.00");
  });

  it("puts each pair's share beside the batch total, so the two are not confused", async () => {
    mockedApiRequest.mockImplementation((_method: string, url: string) =>
      Promise.resolve(url.includes("/batches/") ? DETAIL : BATCHES),
    );
    renderHistory();
    await waitForRows();

    await userEvent.click(screen.getAllByRole("row")[1]);
    await waitFor(() => expect(screen.getByText("OLD-0001")).toBeDefined());

    // Two pairs at 25 each...
    const shares = cellTexts().filter((c) => c === SHARE);
    expect(shares).toHaveLength(2);
    // ...and the footer total, their sum, which is what the list showed.
    expect(cellTexts()).toContain(TOTAL);
  });

  it("returns from a batch to the list", async () => {
    mockedApiRequest.mockImplementation((_method: string, url: string) =>
      Promise.resolve(url.includes("/batches/") ? DETAIL : BATCHES),
    );
    renderHistory();
    await waitForRows();

    await userEvent.click(screen.getAllByRole("row")[1]);
    await waitFor(() => expect(screen.getByText("OLD-0001")).toBeDefined());

    await userEvent.click(screen.getByText("voucherExchange.historyBack"));
    // Back on the list: the drill-down's columns are gone and the list's are back.
    await waitFor(() => expect(screen.queryByText("OLD-0001")).toBeNull());
    expect(screen.getByText("voucherExchange.historySurcharge")).toBeDefined();
  });

  it("says so plainly when no exchange has been recorded", async () => {
    mockedApiRequest.mockResolvedValue({
      ...BATCHES,
      data: [],
      total: 0,
      suppliers: [],
    });
    renderHistory();

    await waitFor(() =>
      expect(screen.getByText("voucherExchange.historyEmpty")).toBeDefined(),
    );
  });

  it("reports a failed load instead of showing an empty list", async () => {
    mockedApiRequest.mockRejectedValue(new Error("boom"));
    renderHistory();

    await waitFor(() =>
      expect(
        screen.getByText("voucherExchange.historyLoadFailed"),
      ).toBeDefined(),
    );
  });
});
