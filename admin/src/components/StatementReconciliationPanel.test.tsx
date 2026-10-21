import { describe, it, expect, vi, beforeEach } from "vitest";
import { render, screen, waitFor } from "@testing-library/react";
import userEvent from "@testing-library/user-event";
import { QueryClient, QueryClientProvider } from "@tanstack/react-query";
import type { ReactNode } from "react";
import { StatementReconciliationPanel } from "@/components/StatementReconciliationPanel";

vi.mock("@/lib/api-client", () => ({ apiRequest: vi.fn(), apiBlob: vi.fn() }));
vi.mock("@/lib/i18n", () => ({
  useI18n: () => ({ t: (key: string) => key, language: "uk", setLanguage: vi.fn() }),
}));
vi.mock("sonner", () => ({ toast: { error: vi.fn(), success: vi.fn() } }));

import { apiRequest, apiBlob } from "@/lib/api-client";

const mockedApiRequest = apiRequest as unknown as ReturnType<typeof vi.fn>;
const mockedApiBlob = apiBlob as unknown as ReturnType<typeof vi.fn>;

const ACT = {
  merchant: "Live",
  fromUtc: "2026-09-01T00:00:00Z",
  toUtc: "2026-10-01T00:00:00Z",
  statementRowCount: 2,
  summary: {
    totalRows: 2,
    matchedCount: 1,
    mismatchCount: 1,
    unmatchedCount: 0,
    refundMatchCount: 0,
    matchedTotalUah: 500,
    mismatchDeltaUah: 1.43,
    unmatchedTotalUah: 0,
    issuedRefundCount: 1,
    issuedRefundTotalUah: 478.57,
  },
  rows: [
    {
      statementId: "s-1",
      status: "matched",
      invoiceId: "inv-1",
      reference: null,
      acquirerAmountKopecks: 50000,
      acquirerAmount: 500,
      orderId: "o-1",
      localAmount: 500,
      orderReference: null,
      orderStatus: "Fulfilled",
      acquirerCreatedAtUtc: "2026-09-03T10:15:00Z",
      note: null,
    },
    {
      statementId: "s-2",
      status: "amountMismatch",
      invoiceId: "inv-2",
      reference: null,
      acquirerAmountKopecks: 97857,
      acquirerAmount: 978.57,
      orderId: "o-2",
      localAmount: 979,
      orderReference: null,
      orderStatus: "Fulfilled",
      acquirerCreatedAtUtc: "2026-09-04T11:00:00Z",
      note: "delta",
    },
  ],
  missingFromStatement: [],
  refundsIssued: [
    {
      refundId: "r-1",
      orderId: "o-2",
      invoiceId: "inv-2",
      amount: 478.57,
      issuedAtUtc: "2026-09-20T12:00:00Z",
      reversedPaymentAtUtc: "2026-09-03T10:15:00Z",
      reversesEarlierPeriodPayment: false,
      note: null,
    },
  ],
};

function renderPanel() {
  const client = new QueryClient({ defaultOptions: { queries: { retry: false } } });
  const wrapper = ({ children }: { children: ReactNode }) => (
    <QueryClientProvider client={client}>{children}</QueryClientProvider>
  );
  return render(<StatementReconciliationPanel />, { wrapper });
}

describe("StatementReconciliationPanel", () => {
  beforeEach(() => {
    mockedApiRequest.mockReset();
    mockedApiBlob.mockReset();
  });

  it("asks for nothing until the act is built, so opening reports does not hammer the API", async () => {
    renderPanel();

    expect(mockedApiRequest).not.toHaveBeenCalled();
  });

  it("builds the act for the previous calendar month and shows both verdicts", async () => {
    mockedApiRequest.mockResolvedValue(ACT);
    renderPanel();

    await userEvent.click(screen.getByText("statementAct.build"));

    await waitFor(() => expect(mockedApiRequest).toHaveBeenCalledTimes(1));
    const url = mockedApiRequest.mock.calls[0][1] as string;
    expect(url).toContain("merchant=live");
    // Default is the previous month, never the running one.
    const from = new URL(url, "http://x").searchParams.get("fromUtc")!;
    const to = new URL(url, "http://x").searchParams.get("toUtc")!;
    expect(new Date(to).getTime() - new Date(from).getTime()).toBeGreaterThanOrEqual(27 * 86400000);

    expect(await screen.findByText("statementAct.status.matched")).toBeDefined();
    expect(screen.getByText("statementAct.status.amountMismatch")).toBeDefined();
  });

  it("surfaces a mismatch count rather than burying it in the table", async () => {
    mockedApiRequest.mockResolvedValue(ACT);
    renderPanel();

    await userEvent.click(screen.getByText("statementAct.build"));

    expect(await screen.findByText("statementAct.mismatches")).toBeDefined();
    expect(screen.getByText("statementAct.matched")).toBeDefined();
  });

  // The acquirer nests a reversal under the payment it reverses and files no row for it, so the
  // statement alone cannot show money going back. The act reports the refunds from our side, with
  // their own figure - and the reader can see which payment each one reverses.
  it("shows the refunds we issued in the period, with the payment each one reverses", async () => {
    mockedApiRequest.mockResolvedValue(ACT);
    renderPanel();

    await userEvent.click(screen.getByText("statementAct.build"));

    expect(await screen.findByText("statementAct.refundsIssued")).toBeDefined();
    expect(screen.getByText("statementAct.refundsTitle")).toBeDefined();
    // The invoice appears in the statement rows too, so it is the count that carries the
    // assertion: the refund names the payment it reverses.
    expect(screen.getAllByText("inv-2").length).toBeGreaterThanOrEqual(2);
    // The figure is in the tile and in the refund's own row: the same money, reported twice.
    expect(screen.getAllByText("478.57 ₴").length).toBe(2);
  });

  it("downloads the period's CSV through the blob client, not a JSON request", async () => {
    mockedApiRequest.mockResolvedValue(ACT);
    mockedApiBlob.mockResolvedValue({ blob: new Blob(["a,b"]), fileName: "reconciliation-live-2026-09.csv" });
    renderPanel();

    await userEvent.click(screen.getByText("statementAct.exportCsv"));

    await waitFor(() => expect(mockedApiBlob).toHaveBeenCalledTimes(1));
    const [method, url] = mockedApiBlob.mock.calls[0];
    expect(method).toBe("GET");
    expect(String(url)).toContain("/api/admin/reconciliation/statement/csv?");
    expect(mockedApiRequest).not.toHaveBeenCalledWith("GET", expect.stringContaining("/csv"));
  });

  it("says so when a period has no statement rows, instead of showing an empty table", async () => {
    mockedApiRequest.mockResolvedValue({ ...ACT, rows: [], statementRowCount: 0 });
    renderPanel();

    await userEvent.click(screen.getByText("statementAct.build"));

    expect(await screen.findByText("statementAct.noRows")).toBeDefined();
  });

  it("reports a failed load rather than pretending the period is clean", async () => {
    mockedApiRequest.mockRejectedValue(new Error("boom"));
    renderPanel();

    await userEvent.click(screen.getByText("statementAct.build"));

    expect(await screen.findByText("statementAct.loadFailed")).toBeDefined();
  });
});
