import { describe, it, expect, vi, beforeEach } from "vitest";
import { render, screen } from "@testing-library/react";
import { QueryClient, QueryClientProvider } from "@tanstack/react-query";
import FeedbackTab from "@/components/FeedbackTab";

/**
 * The one line on this screen that interpolates five values at once, and the reason this file
 * exists at all.
 *
 * `i18n-parity.test.ts` proved the dictionary uses positional slots — after it shipped a failure
 * of exactly the shape it would have caught (`feedback.summary` was `{total}`/`{kept}` in all four
 * locales, so cross-locale parity passed while the screen rendered the braces literally). That
 * test cannot see the other half: whether the COMPONENT passes its arguments in the order the
 * slots expect. Five arguments in the wrong order is equally invisible and just as wrong, and a
 * count is exactly the kind of value nobody sanity-checks by eye.
 *
 * So the tab is rendered against the REAL `t()`, and the sentence it produces is asserted. If the
 * two ever disagree again, this fails with the whole line in the diff.
 */

vi.mock("@/lib/api-client", () => ({ apiRequest: vi.fn() }));
vi.mock("sonner", () => ({ toast: { error: vi.fn(), success: vi.fn() } }));

import { apiRequest } from "@/lib/api-client";
import { useI18n } from "@/lib/i18n";

const mockedApiRequest = apiRequest as unknown as ReturnType<typeof vi.fn>;

const FACETS = {
  categories: ["Bug", "Idea"],
  statuses: ["New", "Handled"],
  total: 42,
  kept: 17,
  junk: 24,
  unclassified: 1,
  replied: 9,
};

/** The figures in the order the slots expect them, deliberately not in a readable order. */
const EXPECTED_ORDER = [
  FACETS.total,
  FACETS.kept,
  FACETS.junk,
  FACETS.unclassified,
  FACETS.replied,
];

function renderTab() {
  const client = new QueryClient({
    defaultOptions: { queries: { retry: false } },
  });

  return render(
    <QueryClientProvider client={client}>
      <FeedbackTab />
    </QueryClientProvider>,
  );
}

beforeEach(() => {
  vi.clearAllMocks();

  // The real dictionary, in English. Mocking `t` to echo keys - what the sibling component
  // tests do - would hide the entire defect: the point is that the SHAPE of the value and the
  // SHAPE of the call agree.
  useI18n.getState().setLanguage("en");

  mockedApiRequest.mockImplementation((_method: string, url: string) => {
    if (url.includes("/facets")) return Promise.resolve(FACETS);
    return Promise.resolve({ total: 0, items: [] });
  });
});

describe("FeedbackTab — the triage states", () => {
  // The API spells its states for itself ("Handled"); the column printed that word into a
  // Ukrainian table while its own filter and buttons were translated.
  const ITEM = {
    id: "f-1",
    userId: null,
    telegramChatId: 1,
    text: "Паливо не наливають",
    verdict: "Kept",
    category: "Bug",
    severity: null,
    summary: null,
    language: "uk",
    confidence: 0.9,
    classifierError: null,
    status: "Handled",
    staffRepliedAtUtc: null,
    appVersion: null,
    platform: null,
    createdAtUtc: "2026-10-27T10:00:00Z",
    replyCount: 0,
  };

  it("translates a state instead of printing the API's word", async () => {
    useI18n.getState().setLanguage("uk");
    mockedApiRequest.mockImplementation((_method: string, url: string) => {
      if (url.includes("/facets")) return Promise.resolve(FACETS);
      return Promise.resolve({ total: 1, items: [ITEM] });
    });

    renderTab();

    expect(await screen.findByText("Опрацьовано")).toBeTruthy();
    expect(screen.queryByText("Handled")).toBeNull();
  });
});

describe("FeedbackTab — the triage summary line", () => {
  it("renders the numbers, not the placeholder names", async () => {
    renderTab();

    // The defect this pins: the screen showed
    //   "{total} received · {kept} useful · …"
    // because t() substitutes {0}, {1}, … and the dictionary asked for {total}, {kept}.
    const summary = await screen.findByText(
      /42 received .* 17 useful .* 24 dropped .* 1 unclassified .* 9 answered/,
    );

    expect(summary.textContent).not.toContain("{");
  });

  it("puts each figure in the slot its position names", async () => {
    renderTab();

    const summary = await screen.findByText(/received/);

    // Split rather than substring-search: two of the five fixtures are single digits, and
    // indexOf("1") inside "42" would make a swapped pair look correct.
    const segments = (summary.textContent ?? "")
      .split("·")
      .map((part) => part.trim());

    expect(segments).toHaveLength(5);
    segments.forEach((segment, index) => {
      expect(segment, `segment ${index} should carry its own figure`).toContain(
        String(EXPECTED_ORDER[index]),
      );
    });
  });
});
