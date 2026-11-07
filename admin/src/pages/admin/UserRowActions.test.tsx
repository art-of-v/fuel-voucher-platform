import { QueryClient, QueryClientProvider } from "@tanstack/react-query";
import { render, screen, fireEvent, waitFor } from "@testing-library/react";
import { describe, expect, it, vi } from "vitest";
import type { ReactNode } from "react";

import { UserRowActions } from "./UserRowActions";
import type { AdminUser } from "@/lib/api-types";

vi.mock("@/lib/api-client", () => ({ apiRequest: vi.fn() }));
vi.mock("@/lib/i18n", () => ({
  useI18n: () => ({
    t: (key: string) => key,
    language: "uk",
    setLanguage: vi.fn(),
  }),
}));
vi.mock("sonner", () => ({ toast: { success: vi.fn(), error: vi.fn() } }));

// The signed-in operator, which decides which controls the row draws. `vi.mock` factories are
// hoisted above these bindings, so the holder has to be hoisted with them.
const actor = vi.hoisted(() => ({ role: "Manager" }));
vi.mock("@/lib/session-store", () => ({
  useSession: (selector: (state: { user: { role: string } }) => unknown) =>
    selector({ user: { role: actor.role } }),
}));

import { apiRequest } from "@/lib/api-client";
const mockedApiRequest = apiRequest as unknown as ReturnType<typeof vi.fn>;

/**
 * The promotion control is the panel's half of the ruling of 2026-11-06: a normal customer is not
 * served on the promotional route, and the switch that says otherwise belongs to a Manager and
 * above. The endpoint refuses everybody else whatever this file renders — what these cases guard is
 * that the operator is not shown a control the ruling never gave them, nor left without the one
 * they were given.
 */
function customer(overrides: Partial<AdminUser> = {}): AdminUser {
  return {
    id: "3fa85f64-5717-4562-b3fc-2c963f66afa6",
    phone: "+380110010203",
    email: null,
    firstName: "Petro",
    lastName: "Sakaguntchik",
    birthdate: null,
    profileImageUrl: null,
    referredBy: null,
    settlementCode: "AB12CD",
    promoAccessGrantedAt: null,
    bonusBalance: 0,
    isActive: true,
    isBanned: false,
    isDeleted: false,
    isQaAccount: false,
    hasTwoFactor: false,
    role: "User",
    createdAt: "2026-11-06T00:00:00.000Z",
    ...overrides,
  };
}

const renderRow = (row: AdminUser, actorRole: string) => {
  actor.role = actorRole;
  const queryClient = new QueryClient();
  const wrapper = ({ children }: { children: ReactNode }) => (
    <QueryClientProvider client={queryClient}>{children}</QueryClientProvider>
  );
  return render(<UserRowActions row={row} />, { wrapper });
};

describe("the promotion control of a customer row", () => {
  it("is drawn for a Manager and posts the switch", async () => {
    mockedApiRequest.mockResolvedValue({ granted: true, grantedAtUtc: "" });
    const row = customer();
    renderRow(row, "Manager");

    fireEvent.click(screen.getByText("users.grantPromo"));

    await waitFor(() => {
      expect(mockedApiRequest).toHaveBeenCalledWith(
        "POST",
        `/api/admin/users/${row.id}/promo-access`,
        { granted: true },
      );
    });
  });

  it("asks to take the access away from a customer who has it", async () => {
    mockedApiRequest.mockResolvedValue({ granted: false, grantedAtUtc: null });
    const row = customer({ promoAccessGrantedAt: "2026-11-06T10:00:00.000Z" });
    renderRow(row, "Admin");

    fireEvent.click(screen.getByText("users.revokePromo"));

    await waitFor(() => {
      expect(mockedApiRequest).toHaveBeenCalledWith(
        "POST",
        `/api/admin/users/${row.id}/promo-access`,
        { granted: false },
      );
    });
  });

  it("is not drawn for a customer actor", () => {
    mockedApiRequest.mockResolvedValue({});
    renderRow(customer(), "User");

    expect(screen.queryByText("users.grantPromo")).toBeNull();
  });

  it("is not drawn for a staff row when the actor is below the ProductOwner", () => {
    // The endpoint refuses a Manager or Admin target from these actors, and a control that is
    // guaranteed to refuse is worse than no control (the same rule the role dropdown follows).
    mockedApiRequest.mockResolvedValue({});
    renderRow(customer({ role: "Manager" }), "Manager");

    expect(screen.queryByText("users.grantPromo")).toBeNull();
  });

  it("is drawn for the ProductOwner on a staff row, which the ruling lets him put on the route", async () => {
    mockedApiRequest.mockResolvedValue({ granted: true, grantedAtUtc: "" });
    const row = customer({ role: "Manager", id: "b5b5bde2-042e-73b9-88a6-b92f6ddf2c94" });
    renderRow(row, "ProductOwner");

    fireEvent.click(screen.getByText("users.grantPromo"));

    await waitFor(() => {
      expect(mockedApiRequest).toHaveBeenCalledWith(
        "POST",
        `/api/admin/users/${row.id}/promo-access`,
        { granted: true },
      );
    });
  });

  it("is drawn on the ProductOwner's own row, because nobody above him can press it", () => {
    mockedApiRequest.mockResolvedValue({});
    renderRow(customer({ role: "ProductOwner" }), "ProductOwner");

    expect(screen.getByText("users.grantPromo")).toBeDefined();
  });

  it("still draws the controls the ruling did not change", () => {
    mockedApiRequest.mockResolvedValue({});
    renderRow(customer(), "Manager");

    expect(screen.getByText("users.deactivate")).toBeDefined();
    expect(screen.getByText("users.ban")).toBeDefined();
    expect(screen.getByText("users.delete")).toBeDefined();
  });

  it("draws nothing for an account that is deleted", () => {
    mockedApiRequest.mockResolvedValue({});
    const { container } = renderRow(customer({ isDeleted: true }), "Admin");

    expect(container).toBeEmptyDOMElement();
  });
});
