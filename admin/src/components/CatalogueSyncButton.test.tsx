import { QueryClient, QueryClientProvider } from "@tanstack/react-query";
import { render, screen, fireEvent, waitFor } from "@testing-library/react";
import { describe, expect, it, vi } from "vitest";
import type { ReactNode } from "react";

import CatalogueSyncButton from "./CatalogueSyncButton";

vi.mock("@/lib/api-client", () => ({ apiRequest: vi.fn() }));
vi.mock("@/lib/i18n", () => ({
  useI18n: () => ({ t: (key: string) => key, language: "uk", setLanguage: vi.fn() }),
}));
vi.mock("sonner", () => ({ toast: { success: vi.fn(), error: vi.fn() } }));

import { apiRequest } from "@/lib/api-client";
const mockedApiRequest = apiRequest as unknown as ReturnType<typeof vi.fn>;

const renderButton = () => {
  const queryClient = new QueryClient();
  const wrapper = ({ children }: { children: ReactNode }) => (
    <QueryClientProvider client={queryClient}>{children}</QueryClientProvider>
  );
  return render(<CatalogueSyncButton />, { wrapper });
};

describe("CatalogueSyncButton", () => {
  it("asks the server to start the catalogue check", async () => {
    mockedApiRequest.mockResolvedValue({});
    renderButton();

    fireEvent.click(screen.getByText("brands.syncCatalogue"));

    await waitFor(() => {
      expect(mockedApiRequest).toHaveBeenCalledWith(
        "POST",
        "/api/admin/catalogue/sync",
      );
    });
  });

  it("does not ask twice while a check is already running", async () => {
    mockedApiRequest.mockResolvedValue({});
    renderButton();

    const button = screen.getByText("brands.syncCatalogue");
    fireEvent.click(button);
    fireEvent.click(button);

    await waitFor(() => {
      expect(mockedApiRequest).toHaveBeenCalledTimes(1);
    });
  });
});
