import type { ReactNode } from "react";
import { beforeEach, describe, expect, it, vi } from "vitest";
import { render, screen, waitFor } from "@testing-library/react";
import userEvent from "@testing-library/user-event";
import { QueryClient, QueryClientProvider } from "@tanstack/react-query";
import BusinessLeadsTab from "@/components/BusinessLeadsTab";
import { apiRequest } from "@/lib/api-client";
import { useI18n } from "@/lib/i18n";

vi.mock("@/lib/api-client", () => ({ apiRequest: vi.fn() }));
vi.mock("sonner", () => ({ toast: { success: vi.fn(), error: vi.fn() } }));

const mockedApiRequest = apiRequest as unknown as ReturnType<typeof vi.fn>;

const LEAD = {
  id: "lead-1",
  companyName: "ТОВ Логістик",
  contactName: "Олена Коваль",
  phone: "+380971112233",
  email: "olena@example.com",
  fleetSize: 24,
  message: "Потрібно близько 8 000 літрів на місяць",
  status: "New",
  emailSentAtUtc: "2026-11-18T10:01:00Z",
  emailSendError: null,
  createdAtUtc: "2026-11-18T10:00:00Z",
  updatedAtUtc: "2026-11-18T10:00:00Z",
};

const FACETS = {
  total: 7,
  active: 4,
  newCount: 2,
  counts: { New: 2, Contacted: 1, Qualified: 1, Converted: 2, Rejected: 1 },
};

function responseFor(method: string, url: string) {
  if (url.endsWith("/facets")) return Promise.resolve(FACETS);
  if (url === "/api/admin/business-leads/lead-1") {
    return Promise.resolve(LEAD);
  }
  if (method === "POST" && url.endsWith("/status")) {
    return Promise.resolve({ success: true, affected: 1 });
  }
  return Promise.resolve({ total: 1, items: [LEAD] });
}

function renderTab() {
  const client = new QueryClient({
    defaultOptions: {
      queries: { retry: false },
      mutations: { retry: false },
    },
  });
  const invalidate = vi.spyOn(client, "invalidateQueries");
  const wrapper = ({ children }: { children: ReactNode }) => (
    <QueryClientProvider client={client}>{children}</QueryClientProvider>
  );
  return { ...render(<BusinessLeadsTab />, { wrapper }), invalidate };
}

beforeEach(() => {
  vi.clearAllMocks();
  useI18n.getState().setLanguage("en");
  mockedApiRequest.mockImplementation(responseFor);
});

describe("BusinessLeadsTab", () => {
  it("opens on the active pipeline without sending a status filter", async () => {
    renderTab();

    await screen.findByText("ТОВ Логістик");

    const listCall = mockedApiRequest.mock.calls.find(
      ([method, url]) =>
        method === "GET" &&
        typeof url === "string" &&
        url.startsWith("/api/admin/business-leads?"),
    );
    expect(listCall).toBeTruthy();
    expect(listCall?.[1]).toContain("offset=0");
    expect(listCall?.[1]).toContain("limit=50");
    expect(listCall?.[1]).not.toContain("status=");
    expect(screen.getAllByText("Active pipeline")).toHaveLength(2);
  });

  it("localizes API statuses and opens the full lead detail", async () => {
    useI18n.getState().setLanguage("uk");
    renderTab();

    expect(await screen.findByText("Нова")).toBeInTheDocument();
    await userEvent.click(screen.getByRole("button", { name: "Деталі" }));

    expect(screen.getAllByText("+380971112233")).toHaveLength(2);
    expect(
      screen.getAllByText("Потрібно близько 8 000 літрів на місяць"),
    ).toHaveLength(2);
    expect(mockedApiRequest).toHaveBeenCalledWith(
      "GET",
      "/api/admin/business-leads/lead-1",
    );
  });

  it("moves selected leads and invalidates every business-lead query", async () => {
    const { invalidate } = renderTab();
    await screen.findByText("ТОВ Логістик");

    await userEvent.click(
      screen.getByRole("checkbox", { name: "Select ТОВ Логістик" }),
    );
    await userEvent.click(screen.getByRole("button", { name: "Apply" }));

    await waitFor(() =>
      expect(mockedApiRequest).toHaveBeenCalledWith(
        "POST",
        "/api/admin/business-leads/status",
        { ids: ["lead-1"], status: "Contacted" },
      ),
    );
    expect(invalidate).toHaveBeenCalledWith({
      queryKey: ["admin", "businessLeads"],
    });
  });

  it("shows a retryable error when the list cannot be loaded", async () => {
    mockedApiRequest.mockImplementation((method: string, url: string) => {
      if (url.endsWith("/facets")) return Promise.resolve(FACETS);
      if (method === "GET") return Promise.reject(new Error("offline"));
      return Promise.resolve({ success: true, affected: 0 });
    });

    renderTab();

    expect(
      await screen.findByText("Could not load business leads."),
    ).toBeInTheDocument();
    expect(screen.getByRole("button", { name: "Try again" })).toBeEnabled();
  });

  it("shows the empty active pipeline honestly", async () => {
    mockedApiRequest.mockImplementation((_method: string, url: string) =>
      url.endsWith("/facets")
        ? Promise.resolve(FACETS)
        : Promise.resolve({ total: 0, items: [] }),
    );

    renderTab();

    expect(
      await screen.findByText("No business leads match this view."),
    ).toBeInTheDocument();
  });
});
