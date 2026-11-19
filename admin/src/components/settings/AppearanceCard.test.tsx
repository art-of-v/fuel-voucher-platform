import { describe, expect, it, vi, beforeEach, beforeAll } from "vitest";
import { render, screen } from "@testing-library/react";
import userEvent from "@testing-library/user-event";
import { AppDefaultThemeCard } from "./AppearanceCard";

beforeAll(() => {
  // Radix's select uses pointer capture and scrollIntoView, which jsdom does not have (the same
  // shims ContractsTab's test installs for its own select).
  Element.prototype.hasPointerCapture = () => false;
  Element.prototype.setPointerCapture = () => {};
  Element.prototype.releasePointerCapture = () => {};
  Element.prototype.scrollIntoView = () => {};
});

/**
 * The app-default picker answers to the server's list, not the panel's.
 *
 * <p>
 * That is the whole reason this card exists separately from the browser-only theme switcher: the
 * panel knows twelve themes, a phone knows eight, and the four forge treatments have no phone
 * palette at all. The server refuses an id outside its own catalogue, so the picker must not offer
 * one — otherwise the only way an operator learns is by triggering an error on save.
 * </p>
 */
describe("AppDefaultThemeCard", () => {
  beforeEach(() => {
    vi.clearAllMocks();
  });

  it("offers exactly the ids the server said it accepts", async () => {
    const user = userEvent.setup();
    render(
      <AppDefaultThemeCard
        theme="obsidian"
        available={["lemberg", "obsidian"]}
        onChange={vi.fn()}
      />,
    );

    await user.click(screen.getByRole("combobox"));

    expect(screen.getAllByRole("option").map((o) => o.textContent)).toEqual([
      "Lemberg",
      "Obsidian",
    ]);
    // The forge themes are in the panel's catalogue and must not be here.
    expect(screen.queryByText("Obsidian Forge")).not.toBeInTheDocument();
  });

  it("reports the chosen id, not the label", async () => {
    const user = userEvent.setup();
    const onChange = vi.fn();
    render(
      <AppDefaultThemeCard
        theme="obsidian"
        available={["lemberg", "obsidian", "nova"]}
        onChange={onChange}
      />,
    );

    await user.click(screen.getByRole("combobox"));
    await user.click(screen.getByRole("option", { name: /Nova/ }));

    expect(onChange).toHaveBeenCalledWith("nova");
  });

  it("keeps an id the panel has no label for, rather than hiding it", async () => {
    const user = userEvent.setup();
    render(
      <AppDefaultThemeCard
        theme="obsidian"
        available={["obsidian", "aurora"]}
        onChange={vi.fn()}
      />,
    );

    await user.click(screen.getByRole("combobox"));

    // A theme added to the phone before the panel learnt about it is still selectable, under its
    // raw id — the alternative is a default nobody can set until the panel ships again.
    expect(screen.getByRole("option", { name: /aurora/ })).toBeInTheDocument();
  });
});
