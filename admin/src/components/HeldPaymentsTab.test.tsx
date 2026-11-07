import { describe, it, expect, vi, beforeEach } from "vitest";
import { render, screen } from "@testing-library/react";
import userEvent from "@testing-library/user-event";
import { QueryClient, QueryClientProvider } from "@tanstack/react-query";
import type { ReactNode } from "react";

import HeldPaymentsTab from "@/components/HeldPaymentsTab";

vi.mock("@/lib/api-client", () => ({ apiRequest: vi.fn() }));
vi.mock("@/lib/i18n", () => ({
  useI18n: () => ({
    t: (key: string, ...p: (string | number)[]) =>
      p.length ? `${key}:${p[0]}` : key,
    language: "uk",
    setLanguage: vi.fn(),
  }),
}));
vi.mock("sonner", () => ({ toast: { success: vi.fn(), error: vi.fn() } }));

import { apiRequest } from "@/lib/api-client";
const mockedApiRequest = apiRequest as unknown as ReturnType<typeof vi.fn>;

/**
 * The worklist, read against the balance model.
 *
 * <p>Declared money is a balance and a balance is spent across orders, so a row can name a transfer
 * that has already paid for part of somebody's sale. The row that printed only the amount printed a
 * number the operator could not act on — attaching moves the WHOLE amount onto one order, and the
 * database refuses that write once any of the money is drawn. These tests pin the two figures the row
 * now carries and the one chip that must stay quiet about money the server will not place at all.
 * </p>
 */
const WHOLE = {
  id: "p-whole",
  amountUah: 500,
  freeUah: 500,
  currency: "UAH",
  paidAtUtc: "2026-11-05T09:00:00Z",
  provider: "manual",
  userId: "u-1",
  userPhone: "+380999000001",
  userName: "Ostap",
  declaredCode: "K7P2M4XB",
  declaredText: "500 за замовленням",
  declaredBy: "Finance",
};

const PARTLY_SPENT = {
  id: "p-partly",
  amountUah: 2014,
  freeUah: 514,
  currency: "UAH",
  paidAtUtc: "2026-11-04T09:00:00Z",
  provider: "manual",
  userId: "u-2",
  userPhone: "+380999000002",
  userName: "Gertsyk",
  declaredCode: "R2Q8W6TD",
  declaredText: "2014",
  declaredBy: "Finance",
};

const PAYLOAD = {
  data: [WHOLE, PARTLY_SPENT],
  total: 2,
  openOrders: [
    {
      id: "o-1",
      userId: "u-1",
      price: 900,
      createdAtUtc: "2026-11-05T10:00:00Z",
    },
    {
      id: "o-2",
      userId: "u-2",
      price: 400,
      createdAtUtc: "2026-11-04T10:00:00Z",
    },
  ],
};

function renderTab() {
  const client = new QueryClient({
    defaultOptions: { queries: { retry: false } },
  });
  const wrapper = ({ children }: { children: ReactNode }) => (
    <QueryClientProvider client={client}>{children}</QueryClientProvider>
  );
  return render(<HeldPaymentsTab />, { wrapper });
}

beforeEach(() => {
  mockedApiRequest.mockImplementation((_body: unknown, path: string) =>
    path.includes("/unallocated")
      ? Promise.resolve(PAYLOAD)
      : Promise.resolve(undefined),
  );
});

describe("the held-money worklist", () => {
  it("says what is still free of a transfer that has already paid", async () => {
    renderTab();
    await screen.findByText("heldPayments.freeOf:514.00");

    // One row has drawn money and one does not, so exactly one free figure is printed — and the
    // amount column still quotes the full transfer, because the message finance declared is the
    // evidence behind it.
    expect(screen.queryAllByText(/^heldPayments\.freeOf/)).toHaveLength(1);
    expect(screen.getByText("2014.00 UAH")).toBeInTheDocument();
    expect(screen.getByText("500.00 UAH")).toBeInTheDocument();
  });

  it("does not blame the price for money the database will not let it place", async () => {
    const { container } = renderTab();
    await screen.findByText("heldPayments.freeOf:514.00");

    const pickers = container.querySelectorAll("select");
    await userEvent.selectOptions(pickers[1], "o-2");

    // Partly drawn: the press is refused because of the draw, and the refusal names the figures.
    // Saying "different amount" here would send the operator to fix the wrong thing.
    expect(
      screen.queryByText("heldPayments.amountDiffers"),
    ).not.toBeInTheDocument();

    await userEvent.selectOptions(pickers[0], "o-1");

    // Whole money on an order of another price is still worth warning about: that attach succeeds
    // and counts all 500 on a 900 order.
    expect(screen.getByText("heldPayments.amountDiffers")).toBeInTheDocument();
  });

  it("takes a row off the queue only with a reason, and sends that reason", async () => {
    renderTab();
    await screen.findByText("heldPayments.freeOf:514.00");

    await userEvent.click(
      screen.getAllByRole("button", { name: "heldPayments.dismiss" })[0],
    );

    // The act is answerable later: the server refuses a dismissal nobody can explain, so the
    // confirm press is dead until the operator types one.
    const confirm = screen.getByRole("button", {
      name: "heldPayments.dismissConfirm",
    });
    expect(confirm).toBeDisabled();

    await userEvent.type(
      screen.getByPlaceholderText("heldPayments.dismissPlaceholder"),
      "1 ₴ test capture",
    );
    expect(confirm).toBeEnabled();
    await userEvent.click(confirm);

    expect(mockedApiRequest).toHaveBeenCalledWith(
      "POST",
      "/api/admin/payments/p-whole/dismiss",
      { reason: "1 ₴ test capture" },
    );
  });
});
