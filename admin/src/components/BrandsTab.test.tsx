import { beforeEach, describe, expect, it, vi } from "vitest";
import { render, screen, waitFor, within } from "@testing-library/react";
import userEvent from "@testing-library/user-event";
import { QueryClient, QueryClientProvider } from "@tanstack/react-query";
import type { ReactNode } from "react";

import BrandsTab from "@/components/BrandsTab";
import { defaultQueryFn } from "@/lib/query-defaults";
import { useI18n } from "@/lib/i18n";

const fetchMock = vi.fn();
vi.stubGlobal("fetch", fetchMock);

const FUEL = {
  id: "f-95",
  name: "A-95",
  supplierPricePerLiter: 50,
  marginUahPerLiter: 2,
  marginPercent: null,
  finalPricePerLiter: 52,
  pumpPricePerLiter: 52.5,
  discountPerLiter: 0,
  allowBelowCost: false,
  brandFuelCode: "A95",
  packageLiters: [1, 5, 20],
  isTestData: false,
  priceSpread: null,
};

const BRANDS = [
  {
    id: "b-okko",
    slug: "okko",
    name: "OKKO",
    logoText: "OK",
    color: "#00ff80",
    sortOrder: 1,
    fuels: [FUEL],
    nominals: [2, 5, 10],
  },
];

function jsonResponse(body: unknown, status = 200): Response {
  return {
    ok: status >= 200 && status < 300,
    status,
    headers: new Headers(),
    text: async () => (body === undefined ? "" : JSON.stringify(body)),
  } as unknown as Response;
}

function renderTab() {
  const client = new QueryClient({
    defaultOptions: { queries: { retry: false, queryFn: defaultQueryFn } },
  });
  const wrapper = ({ children }: { children: ReactNode }) => (
    <QueryClientProvider client={client}>{children}</QueryClientProvider>
  );
  return render(<BrandsTab />, { wrapper });
}

const t = (key: string, ...params: (string | number)[]) =>
  useI18n.getState().t(key, ...params);

function callsTo(method: string, suffix: string) {
  return fetchMock.mock.calls.filter(
    ([input, init]) =>
      String(input).endsWith(suffix) &&
      (init as RequestInit | undefined)?.method === method,
  );
}

beforeEach(() => {
  fetchMock.mockReset();
  fetchMock.mockImplementation(
    async (input: RequestInfo | URL, init?: RequestInit) => {
      const url = String(input);
      const method = init?.method ?? "GET";
      if (method === "GET" && url.endsWith("/api/admin/brands")) {
        return jsonResponse(BRANDS);
      }
      return jsonResponse(undefined, 204);
    },
  );
  useI18n.setState({ language: "en" });
});

describe("BrandsTab", () => {
  it("shows the loading state, then the brand and its fuel", async () => {
    renderTab();

    expect(screen.getByText(t("common.loading"))).toBeInTheDocument();

    await screen.findByText("OKKO");
    expect(
      screen.getByText(new RegExp(t("brands.nominalsCount", 3))),
    ).toBeInTheDocument();

    const user = userEvent.setup();
    await user.click(screen.getByText("OKKO"));

    expect(await screen.findByText("A-95")).toBeInTheDocument();
  });

  it("opens the margin editor and saves the fuel with a PUT", async () => {
    const user = userEvent.setup();
    renderTab();

    await user.click(await screen.findByText("OKKO"));
    const row = (await screen.findByText("A-95")).closest("tr");
    expect(row).not.toBeNull();

    await user.click(within(row!).getAllByRole("button")[1]);

    const margin = screen.getByLabelText(`${t("price.margin")} — A-95`);
    expect(margin).toBeInTheDocument();

    await user.click(margin);
    await user.type(margin, "3.5");
    await user.tab();

    await user.click(
      within(row!).getByRole("button", { name: t("common.save") }),
    );

    await waitFor(() =>
      expect(callsTo("PUT", `/api/admin/brand-fuels/${FUEL.id}`)).toHaveLength(
        1,
      ),
    );

    const [[url, init]] = callsTo(
      "PUT",
      `/api/admin/brand-fuels/${FUEL.id}`,
    ) as [[string, RequestInit]];
    expect(url).toBe(`/api/admin/brand-fuels/${FUEL.id}`);
    expect(init.method).toBe("PUT");
    expect(JSON.parse(String(init.body))).toMatchObject({
      id: FUEL.id,
      name: "A-95",
      supplierPricePerLiter: 50,
      marginUahPerLiter: 3.5,
      finalPricePerLiter: 53.5,
      pumpPricePerLiter: 52.5,
      brandFuelCode: "A95",
      allowBelowCost: false,
      isTestData: false,
    });
  });

  it("opens the add-brand panel and creates the brand with a POST", async () => {
    const user = userEvent.setup();
    renderTab();

    await screen.findByText("OKKO");
    await user.click(
      screen.getByRole("button", { name: t("brands.addBrand") }),
    );

    await user.type(
      await screen.findByPlaceholderText(t("brands.namePlaceholder")),
      "WOG",
    );
    await user.click(screen.getByRole("button", { name: t("common.create") }));

    await waitFor(() =>
      expect(callsTo("POST", "/api/admin/brands")).toHaveLength(1),
    );

    const [[url, init]] = callsTo("POST", "/api/admin/brands") as [
      [string, RequestInit],
    ];
    expect(url).toBe("/api/admin/brands");
    expect(init.method).toBe("POST");
    expect(JSON.parse(String(init.body))).toEqual({
      slug: "wog",
      name: "WOG",
      logoText: "",
      color: "#00ff80",
      sortOrder: 999,
    });
  });
});
