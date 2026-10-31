import { describe, it, expect, vi, beforeAll, beforeEach } from "vitest";
import { render, screen, waitFor } from "@testing-library/react";
import userEvent from "@testing-library/user-event";
import { QueryClient, QueryClientProvider } from "@tanstack/react-query";
import type { ReactNode } from "react";

import ContractsTab from "@/components/ContractsTab";
import { defaultQueryFn } from "@/lib/query-defaults";

vi.mock("@/lib/api-client", () => {
  // The refusal tests need the same class `codedErrorMessage` narrows on, so the mock ships it
  // rather than leaving the module's real reader undefined.
  class ApiError extends Error {
    readonly code?: string;
    readonly data?: Record<string, number>;
    readonly extra?: Record<string, unknown>;

    constructor(
      message: string,
      code?: string,
      data?: Record<string, number>,
      extra?: Record<string, unknown>,
    ) {
      super(message);
      this.name = "ApiError";
      this.code = code;
      this.data = data;
      this.extra = extra;
    }
  }

  return { apiRequest: vi.fn(), ApiError };
});
vi.mock("@/lib/i18n", () => ({
  useI18n: () => ({
    t: (key: string, ...p: string[]) => (p.length ? `${key}:${p[0]}` : key),
    language: "uk",
    setLanguage: vi.fn(),
  }),
}));
// The signed contracts are behind a capability on the server (AuthPolicies.SignaturePayload):
// the Admin tier holds it, and the button is hidden for anybody else - so the tab is rendered
// as an Admin here.
vi.mock("@/lib/session-store", () => ({
  useSession: (
    selector: (state: { user: { role: string } | null }) => unknown,
  ) => selector({ user: { role: "Admin" } }),
}));
vi.mock("sonner", () => ({ toast: { success: vi.fn(), error: vi.fn() } }));

import { apiRequest, ApiError } from "@/lib/api-client";
const mockedApiRequest = apiRequest as unknown as ReturnType<typeof vi.fn>;
import { toast } from "sonner";

/**
 * The contracts tab, read against what the server actually returns.
 *
 * <p>
 * The tab used to read five fields the server had never returned - <c>title</c>,
 * <c>version</c>, <c>status</c>, <c>userName</c>, <c>companyName</c>, <c>contractTitle</c> -
 * and it called <c>contract.status.toLowerCase()</c> on the way to rendering, so the first real
 * contract threw the whole tab over. The rows below carry the shape the endpoint answers with,
 * and they are deliberately missing the fields that never existed.
 * </p>
 */
const CONTRACT = {
  id: "c-1",
  brandId: "b-okko",
  brandName: "OKKO",
  legalEntityId: "le-1",
  companyName: "ACME LLC",
  userId: "u-1",
  customerName: "+380991234567",
  createdAtUtc: "2026-10-01T10:00:00Z",
  expiresAtUtc: "2026-12-31T00:00:00Z",
  expired: false,
  termsVersion: 2,
  signed: false,
  signedAtUtc: null,
};

/**
 * A signature, as the LIST answers with it: the row, and no image.
 *
 * <p>
 * The image is not on the list - a handwritten signature is a capability of its own, served per
 * record, and opening it is recorded (planning #622, S-13). The fixture deliberately does not
 * carry a <c>signatureData</c> field, so the test cannot pass by reading one.
 */
const SIGNED = {
  id: "uc-1",
  contractId: "c-1",
  brandId: "b-okko",
  brandName: "OKKO",
  companyId: "le-1",
  companyName: "ACME LLC",
  userId: "u-1",
  customerName: "+380991234567",
  signedAtUtc: "2026-10-02T11:00:00Z",
  expiresAtUtc: "2026-12-31T00:00:00Z",
  expired: false,
  termsVersion: 2,
};

/** The image, as the DETAIL answers with it. */
const SIGNATURE = {
  ...SIGNED,
  signatureData: '["M0,0 L10,10"]',
};

/** The list is a page, so the list endpoint answers with a page. */
const SIGNED_PAGE = {
  items: [SIGNED],
  totalCount: 1,
  page: 1,
  pageSize: 50,
  totalPages: 1,
  hasPreviousPage: false,
  hasNextPage: false,
};

const BRANDS = [
  { id: "b-okko", name: "OKKO" },
  { id: "b-wog", name: "WOG" },
];

function renderTab() {
  const client = new QueryClient({
    defaultOptions: { queries: { retry: false, queryFn: defaultQueryFn } },
  });
  const wrapper = ({ children }: { children: ReactNode }) => (
    <QueryClientProvider client={client}>{children}</QueryClientProvider>
  );
  return render(<ContractsTab />, { wrapper });
}

beforeAll(() => {
  // Radix's select uses pointer capture and scrollIntoView, which jsdom does not have.
  Element.prototype.hasPointerCapture = () => false;
  Element.prototype.setPointerCapture = () => {};
  Element.prototype.releasePointerCapture = () => {};
  Element.prototype.scrollIntoView = () => {};
});

/** Every rendered cell, whitespace stripped. */
function cellTexts(): string[] {
  return Array.from(document.querySelectorAll("td, th")).map((cell) =>
    (cell.textContent ?? "").replace(/\s/g, ""),
  );
}

beforeEach(() => {
  mockedApiRequest.mockReset();
  vi.mocked(toast.error).mockClear();
  mockedApiRequest.mockImplementation((_method: string, url: string) => {
    if (url.includes("signed-contracts/uc-1"))
      return Promise.resolve(SIGNATURE);
    if (url.includes("signed-contracts")) return Promise.resolve(SIGNED_PAGE);
    if (url.includes("legal-entity/contracts"))
      return Promise.resolve([CONTRACT]);
    if (url.includes("/api/admin/brands")) return Promise.resolve(BRANDS);
    if (url.includes("contract-versions")) return Promise.resolve([]);
    return Promise.resolve([]);
  });
});

describe("ContractsTab", () => {
  it("renders a real contract row instead of throwing on the first one", async () => {
    renderTab();

    // The row is rendered from the fields the endpoint answers with. The tab used to read
    // `contract.status.toLowerCase()`, and a row without a status took the whole tab down.
    await waitFor(() => expect(cellTexts()).toContain("OKKO"));
    expect(cellTexts()).toContain("ACMELLC");
    expect(cellTexts()).toContain("+380991234567");
    expect(cellTexts()).toContain("contracts.notSigned");
  });

  it("says which version of the terms the contract is against", async () => {
    renderTab();

    await waitFor(() => expect(cellTexts()).toContain("OKKO"));
    expect(cellTexts()).toContain("2");
  });

  it("shows who signed what, and fetches the image only when the operator asks for it", async () => {
    const user = userEvent.setup();
    renderTab();

    await waitFor(() =>
      expect(screen.getByText("contracts.view")).toBeDefined(),
    );
    // The customer and the company, from the signature's own row.
    expect(
      cellTexts().filter((c) => c === "+380991234567").length,
    ).toBeGreaterThan(0);
    expect(cellTexts().filter((c) => c === "ACMELLC").length).toBeGreaterThan(
      0,
    );

    // The list carries no image, so the per-record endpoint has not been asked yet.
    const imageCall = () =>
      mockedApiRequest.mock.calls.find((c) =>
        String(c[1]).includes("signed-contracts/uc-1"),
      );
    expect(imageCall()).toBeUndefined();

    // Opening the signature asks for the image - and that call is the one the server records.
    await user.click(screen.getByText("contracts.view"));

    await waitFor(() => expect(imageCall()).toBeDefined());
  });

  it("publishes the document the operator handed over", async () => {
    const user = userEvent.setup();
    renderTab();

    await waitFor(() => expect(cellTexts()).toContain("OKKO"));

    await user.click(screen.getByRole("combobox"));
    await user.click(await screen.findByRole("option", { name: "OKKO" }));

    const file = new File(["the terms"], "terms.docx", {
      type: "application/vnd.openxmlformats-officedocument.wordprocessingml.document",
    });
    const fileInput = document.querySelector(
      'input[type="file"]',
    ) as HTMLInputElement;
    await user.upload(fileInput, file);

    await user.click(screen.getByText("contracts.upload"));

    await waitFor(() => {
      const call = mockedApiRequest.mock.calls.find(
        (c) =>
          c[0] === "POST" && String(c[1]).includes("contract-versions/upload"),
      );
      expect(call).toBeDefined();
    });

    const [, , body] = mockedApiRequest.mock.calls.find(
      (c) =>
        c[0] === "POST" && String(c[1]).includes("contract-versions/upload"),
    )!;
    const form = body as FormData;
    // The provider, the term and the document - the three things the endpoint binds.
    expect(form.get("brandId")).toBe("b-okko");
    expect(form.get("termDays")).toBe("365");
    expect((form.get("file") as File).name).toBe("terms.docx");
  });

  it("words a refused publish in the panel's language", async () => {
    const user = userEvent.setup();
    renderTab();

    await waitFor(() => expect(cellTexts()).toContain("OKKO"));

    // Everything else answers as before; only the publish is refused, by the code the endpoint
    // sends when a scanned document has no text to read.
    mockedApiRequest.mockImplementation((_method: string, url: string) => {
      if (url.includes("contract-versions/upload"))
        return Promise.reject(
          new ApiError(
            "The document has no text in it",
            "contract_document_no_text",
          ),
        );
      if (url.includes("signed-contracts")) return Promise.resolve(SIGNED_PAGE);
      if (url.includes("legal-entity/contracts"))
        return Promise.resolve([CONTRACT]);
      if (url.includes("/api/admin/brands")) return Promise.resolve(BRANDS);
      return Promise.resolve([]);
    });

    await user.click(screen.getByRole("combobox"));
    await user.click(await screen.findByRole("option", { name: "OKKO" }));

    const file = new File(["scanned"], "terms.docx", {
      type: "application/vnd.openxmlformats-officedocument.wordprocessingml.document",
    });
    const fileInput = document.querySelector(
      'input[type="file"]',
    ) as HTMLInputElement;
    await user.upload(fileInput, file);

    await user.click(screen.getByText("contracts.upload"));

    await waitFor(() =>
      expect(toast.error).toHaveBeenCalledWith(
        "contracts.error.documentNoText",
      ),
    );
  });
});
