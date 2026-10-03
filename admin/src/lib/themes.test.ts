import { describe, it, expect } from "vitest";
import { readFileSync } from "node:fs";
import path from "node:path";
import { DEFAULT_THEME, themeOptions, themes, type ThemeType } from "./themes";

// The catalogue (themes.ts) and the palettes (index.css) are two halves of one
// contract: a theme only exists if both name it. Nothing in the build or runtime
// connects them, so an id added to one file and forgotten in the other ships a
// theme that silently renders as the default palette. These tests are the glue.
const css = readFileSync(path.resolve(import.meta.dirname, "../index.css"), "utf8");

/** The `[data-theme="x"]` declaration block for a theme, or null if absent. */
function themeBlock(id: ThemeType): string | null {
    return css.match(new RegExp(`:root\\[data-theme="${id}"\\]\\s*\\{([^}]*)\\}`))?.[1] ?? null;
}

/** The selector list of the shared light-theme chrome block. */
function lightChromeSelectors(): string {
    return css.match(/:root\[data-theme="white"\][^{]*\{/)?.[0] ?? "";
}

describe("theme catalogue", () => {
    it("gives every offered theme an isDark flag", () => {
        for (const { id } of themeOptions) {
            expect(themes[id], `themes[${id}]`).toBeDefined();
            expect(typeof themes[id].isDark).toBe("boolean");
        }
    });

    it("offers no duplicate ids or swatches", () => {
        expect(new Set(themeOptions.map((t) => t.id)).size).toBe(themeOptions.length);
        expect(new Set(themeOptions.map((t) => t.swatch.toLowerCase())).size).toBe(themeOptions.length);
    });

    it("uses paintable hex swatches so the picker dot is never blank", () => {
        for (const { id, swatch } of themeOptions) {
            expect(swatch, `swatch for ${id}`).toMatch(/^#[0-9a-f]{6}$/i);
        }
    });

    it("ships the DEFAULT_THEME in the switcher", () => {
        expect(themeOptions.map((t) => t.id)).toContain(DEFAULT_THEME);
    });
});

describe("index.css palette coverage", () => {
    it("declares a palette block for every theme except the default", () => {
        // DEFAULT_THEME's palette *is* bare :root, so it has no [data-theme] block.
        for (const { id } of themeOptions) {
            if (id === DEFAULT_THEME) continue;
            expect(themeBlock(id), `index.css is missing a [data-theme="${id}"] block`).not.toBeNull();
        }
    });

    it("declares no palette block for an id that is not in the catalogue", () => {
        // `[a-z]+` so the prose comments that use a `…` placeholder don't count.
        const declared = [...css.matchAll(/:root\[data-theme="([a-z]+)"\]/g)].map((m) => m[1]);
        for (const id of declared) {
            expect(Object.keys(themes), `index.css declares unknown theme "${id}"`).toContain(id);
        }
    });

    it("flips color-scheme for exactly the themes flagged isDark", () => {
        const selectors = lightChromeSelectors();
        for (const { id } of themeOptions) {
            expect(selectors.includes(`[data-theme="${id}"]`), `${id} in light chrome block`).toBe(
                !themes[id].isDark,
            );
        }
    });
});

describe("mercury theme", () => {
    const block = () => themeBlock("mercury")!;

    it("declares an opaque graphite surface for every translucency slot", () => {
        // Any alpha in these slots puts the aurora or the page back through the
        // panel — the exact thing this theme exists to remove.
        for (const token of [
            "--background",
            "--card",
            "--muted",
            "--border",
            "--input",
            "--accent",
            "--glass-panel-bg",
            "--glass-chrome-bg",
            "--glass-input-bg",
        ]) {
            const value = block().match(new RegExp(`${token}:\\s*([^;]+);`))?.[1] ?? "";
            expect(value, `${token} should be declared`).not.toBe("");
            expect(value, `${token} must be opaque`).not.toMatch(
                /rgba?\(|hsla?\(|color-mix\(|\s\/\s*[\d.]+%?/,
            );
        }
    });

    it("uses a monochrome mercury primary with an ink-dark label", () => {
        expect(block()).toMatch(/--primary:\s*#cbd4de;/i);
        expect(block()).toMatch(/--primary-foreground:\s*#0a0c0f;/i);
    });

    it("strips the blur and gradient text off the shared .glass-* classes", () => {
        expect(css).toMatch(/:root\[data-theme="mercury"\][^{]*\.glass-panel[^{]*\{[^}]*backdrop-filter:\s*none/);
        expect(css).toMatch(/:root\[data-theme="mercury"\][^{]*\.glass-chrome[^{]*\{[^}]*backdrop-filter:\s*none/);
        expect(css).toMatch(
            /:root\[data-theme="mercury"\][^{]*\.glass-text-gradient[^{]*\{[^}]*color:\s*var\(--foreground\)/,
        );
        // Tailwind's blur helpers carry no token, so the filter is killed outright.
        expect(css).toMatch(
            /:root\[data-theme="mercury"\][^{]*\.backdrop-blur-md[^{]*\{[^}]*backdrop-filter:\s*none/,
        );
        // The glow used to be the active-nav state; mercury now presses the item
        // into the sidebar instead, behind a 3px silver rail.
        expect(css).toMatch(/:root\[data-theme="mercury"\][^{]*\.glass-glow[^{]*\{[^}]*border-left:\s*3px solid var\(--primary\)/);
    });

    it("switches the ambient aurora off entirely", () => {
        expect(css).toMatch(/:root\[data-theme="mercury"\][^{]*\.aurora-bg[^{]*\{[^}]*display:\s*none/);
    });

    it("collapses every corner in the app to a hard square", () => {
        // One rule covers rounded-full / rounded-3xl / bare `rounded`, which are
        // not radius tokens — collapsing only --radius-*-base would miss them.
        expect(css).toMatch(
            /:root\[data-theme="mercury"\]\s+:where\(\*,\s*\*::before,\s*\*::after\)\s*\{[^}]*border-radius:\s*0 !important/,
        );
        for (const token of ["--radius-base", "--radius-sm-base", "--radius-md-base", "--radius-xl-base"]) {
            expect(block(), token).toMatch(new RegExp(`${token}:\\s*0;`));
        }
    });

    it("builds volume from hard, unblurred bevels instead of a cast shadow", () => {
        expect(block()).toMatch(/--edge-hi:/);
        expect(block()).toMatch(/--shade-hi:/);
        expect(block()).toMatch(/--shadow-glass-base:\s*none;/);
        // Raised panels: an inset lit top edge plus hard extrusion steps.
        expect(css).toMatch(/:root\[data-theme="mercury"\][^{]*\.glass-panel[^{]*\{[^}]*inset 0 1px 0 var\(--edge-hi\)/);
        // Inputs go the other way — a well is sunken, not raised.
        expect(css).toMatch(/:root\[data-theme="mercury"\][^{]*\.glass-input[^{]*\{[^}]*inset 0 2px 4px/);
        // The primary key is the one place real light is spent.
        expect(css).toMatch(/:root\[data-theme="mercury"\][^{]*button\.bg-primary[^{]*\{[^}]*0 2px 0/);
    });

    it("sets the squarish techno face the mobile app uses for display type", () => {
        expect(block()).toMatch(/--font-display:\s*"Rajdhani"/);
        // Rajdhani has been in index.html all along but unused; check it is still
        // requested, or the whole theme silently falls back to Inter.
        const html = readFileSync(path.resolve(import.meta.dirname, "../../index.html"), "utf8");
        expect(html).toMatch(/family=Rajdhani/);
        expect(css).toMatch(/:root\[data-theme="mercury"\][^{]*body[^{]*\{[^}]*font-family:\s*var\(--font-display\)/);
    });

    it("replaces the sidebar accent bar with the chrome lion", () => {
        const layout = readFileSync(
            path.resolve(import.meta.dirname, "../components/layout.tsx"),
            "utf8",
        );
        expect(layout).toMatch(/LionMark/);
        expect(layout).toMatch(/theme === "mercury"/);
    });

    it("ships the silver lion as a small RGBA asset, not the 1.2MB neon original", () => {
        const asset = path.resolve(import.meta.dirname, "../assets/lion.png");
        const bytes = readFileSync(asset);
        expect(bytes.subarray(1, 4).toString("ascii")).toBe("PNG");
        expect(bytes.length).toBeLessThan(80 * 1024);
    });
});