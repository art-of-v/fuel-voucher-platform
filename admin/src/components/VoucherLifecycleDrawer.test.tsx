import { describe, it, expect, vi, beforeEach } from "vitest";
import { render, screen, waitFor } from "@testing-library/react";
import userEvent from "@testing-library/user-event";
import { QueryClient, QueryClientProvider } from "@tanstack/react-query";
import type { ReactNode } from "react";

import { VoucherLifecycleDrawer } from "./VoucherLifecycleDrawer";
import { apiRequest } from "@/lib/api-client";

vi.mock("@/lib/api-client", () => ({ apiRequest: vi.fn() }));
vi.mock("@/lib/i18n", () => ({
  useI18n: () => ({
    t: (key: string, ...p: string[]) => (p.length ? `${key}:${p[0]}` : key),
    language: "uk",
    setLanguage: vi.fn(),
  }),
}));

const mockedApiRequest = vi.mocked(apiRequest);

function renderDrawer(props: {
  voucherId: string | null;
  onOpenChange?: (o: boolean) => void;
}) {
  const client = new QueryClient({
    defaultOptions: { queries: { retry: false } },
  });
  const wrapper = ({ children }: { children: ReactNode }) => (
    <QueryClientProvider client={client}>{children}</QueryClientProvider>
  );
  // The caller owns the open state, so the callback is part of the contract rather than optional -
  // a drawer that cannot be told to close is a drawer nobody uses.
  const onOpenChange = props.onOpenChange ?? vi.fn();
  return render(
    <VoucherLifecycleDrawer {...props} onOpenChange={onOpenChange} />,
    { wrapper },
  );
}

const FULL = {
  voucherId: "v1",
  voucherNumber: "OKKO-1",
  status: "Assigned",
  liters: 50,
  brand: "OKKO",
  isArchived: false,
  supplierId: "s1",
  supplierName: "Supplier A",
  costPerLiter: 40,
  supplierExpirationDate: "2027-01-01",
  customerExpirationDate: "2027-01-01",
  importJobId: "i1",
  importFileName: "batch.pdf",
  importedAtUtc: "2026-05-01T09:00:00Z",
  rootVoucherId: "v1",
  lineage: ["v1"],
  entries: [
    {
      source: "movement",
      type: "issued",
      occurredAtUtc: "2026-05-01T10:00:00Z",
      from: "pool",
      to: "customer",
      previousCustomerExpiration: null,
      newCustomerExpiration: null,
      termCode: null,
      amountUah: null,
      costDeltaUah: null,
      actorKind: "user",
      actorUserId: "u1",
      actorName: "Olena K",
      reason: null,
      externalRef: null,
      orderId: "o1",
    },
    {
      source: "event",
      type: "StatusChanged",
      occurredAtUtc: "2026-05-01T11:00:00Z",
      from: "Assigned",
      to: "Used",
      previousCustomerExpiration: null,
      newCustomerExpiration: null,
      termCode: null,
      amountUah: null,
      costDeltaUah: null,
      actorKind: "system",
      actorUserId: null,
      actorName: null,
      reason: null,
      externalRef: null,
      orderId: null,
    },
  ],
};

beforeEach(() => {
  mockedApiRequest.mockReset();
});

describe("VoucherLifecycleDrawer", () => {
  it("does not call the API while closed", () => {
    renderDrawer({ voucherId: null });

    // A closed panel must cost nothing. Opening it is the thing that fetches.
    expect(mockedApiRequest).not.toHaveBeenCalled();
  });

  it("renders the three sources with their own vocabularies, not one merged noun", async () => {
    mockedApiRequest.mockResolvedValue(FULL);
    renderDrawer({ voucherId: "v1" });

    await waitFor(() => expect(mockedApiRequest).toHaveBeenCalled());
    expect(mockedApiRequest).toHaveBeenCalledWith(
      "GET",
      "/api/admin/vouchers/v1/lifecycle",
    );

    // The key IS the word, so an unmapped term still reads as itself.
    await waitFor(() =>
      expect(screen.getByText("voucherLifecycle.kind.issued")).toBeTruthy(),
    );
    expect(
      screen.getByText("voucherLifecycle.event.StatusChanged"),
    ).toBeTruthy();
  });

  it("translates the words either side of the arrow through their own source", async () => {
    // A header status the timeline does not repeat, so each assertion names exactly one element.
    mockedApiRequest.mockResolvedValue({ ...FULL, status: "Available" });
    renderDrawer({ voucherId: "v1" });

    // A movement's ends are custody locations, not movement kinds: asking the locales for
    // `kind.pool` printed the key itself next to a translated neighbour.
    await waitFor(() =>
      expect(screen.getByText("voucherLifecycle.location.pool")).toBeTruthy(),
    );
    expect(screen.getByText("voucherLifecycle.location.customer")).toBeTruthy();

    // An event's ends are voucher states, so they read from the same keys the status badges use.
    expect(screen.getAllByText("status.assigned")).toHaveLength(1);
    expect(screen.getAllByText("status.used")).toHaveLength(1);
    expect(screen.queryByText("voucherLifecycle.event.Assigned")).toBeNull();
    expect(screen.queryByText("voucherLifecycle.event.Used")).toBeNull();
  });

  it("translates the voucher's own status in the header", async () => {
    // An empty timeline, so the only status on screen is the voucher's own.
    mockedApiRequest.mockResolvedValue({ ...FULL, entries: [] });
    renderDrawer({ voucherId: "v1" });

    // The API spells it `Assigned`; the header used to print that English word inside a Ukrainian UI.
    await waitFor(() =>
      expect(screen.getByText("status.assigned")).toBeTruthy(),
    );
    expect(screen.queryByText("Assigned")).toBeNull();
  });

  it("keys a renewal's arrow by what the renewal did", async () => {
    mockedApiRequest.mockResolvedValue({
      ...FULL,
      entries: [
        {
          ...FULL.entries[0],
          source: "money",
          type: "Renewal",
          from: "replaced",
          to: null,
          actorKind: "system",
          actorUserId: null,
          actorName: null,
        },
      ],
    });
    renderDrawer({ voucherId: "v1" });

    await waitFor(() =>
      expect(screen.getByText("voucherLifecycle.renewal.Renewal")).toBeTruthy(),
    );
    expect(screen.getByText("voucherLifecycle.renewal.replaced")).toBeTruthy();
  });

  it("shows the resolved author's name and says 'the system' when there is none", async () => {
    mockedApiRequest.mockResolvedValue(FULL);
    renderDrawer({ voucherId: "v1" });

    // actor_user_id is FK-free, so a lookup may come back empty. A scheduler is named as the
    // scheduler rather than given an invented person.
    await waitFor(() => expect(screen.getByText("Olena K")).toBeTruthy());
    expect(screen.getByText("voucherLifecycle.system")).toBeTruthy();
  });

  it("falls back to the raw id when an author has no resolvable name", async () => {
    mockedApiRequest.mockResolvedValue({
      ...FULL,
      entries: [{ ...FULL.entries[0], actorName: null }],
    });
    renderDrawer({ voucherId: "v1" });

    // The id is still answerable, so it is shown rather than replaced by a placeholder.
    await waitFor(() =>
      expect(screen.getByText("voucherLifecycle.actorById:u1")).toBeTruthy(),
    );
  });

  it("says an error rather than an empty timeline", async () => {
    mockedApiRequest.mockRejectedValue(new Error("boom"));
    renderDrawer({ voucherId: "v1" });

    // "Nothing happened" and "we could not read it" are different answers and must not look alike.
    await waitFor(() =>
      expect(screen.getByText("voucherLifecycle.loadFailed")).toBeTruthy(),
    );
    expect(screen.queryByText("voucherLifecycle.empty")).toBeNull();
  });

  it("says so when nothing has been recorded", async () => {
    mockedApiRequest.mockResolvedValue({ ...FULL, entries: [] });
    renderDrawer({ voucherId: "v1" });

    await waitFor(() =>
      expect(screen.getByText("voucherLifecycle.empty")).toBeTruthy(),
    );
  });

  it("marks an archived voucher instead of pretending it is live", async () => {
    mockedApiRequest.mockResolvedValue({ ...FULL, isArchived: true });
    renderDrawer({ voucherId: "v1" });

    await waitFor(() =>
      expect(screen.getByText("voucherLifecycle.archivedNote")).toBeTruthy(),
    );
  });

  it("closes on Escape", async () => {
    const onOpenChange = vi.fn();
    mockedApiRequest.mockResolvedValue(FULL);
    const user = userEvent.setup();
    renderDrawer({ voucherId: "v1", onOpenChange });

    await waitFor(() => expect(screen.getByText("OKKO-1")).toBeTruthy());

    // The hand-rolled overlays in this app have no Escape handling and no focus trap. This one is on
    // Radix, and the assertion is here so that stays true.
    await user.keyboard("{Escape}");
    expect(onOpenChange).toHaveBeenCalledWith(false);
  });
});
