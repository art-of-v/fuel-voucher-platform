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
        // The glow is the active-nav state; mercury trades it for a solid rail.
        expect(css).toMatch(/:root\[data-theme="mercury"\][^{]*\.glass-glow[^{]*\{[^}]*inset 2px 0 0 0 var\(--primary\)/);
    });

    it("switches the ambient aurora off entirely", () => {
        expect(css).toMatch(/:root\[data-theme="mercury"\][^{]*\.aurora-bg[^{]*\{[^}]*display:\s*none/);
    });

    it("tightens the corners and drops the cast shadow", () => {
        expect(block()).toMatch(/--radius-md-base:\s*0\.375rem;/);
        expect(block()).toMatch(/--shadow-glass-base:\s*0 1px 2px/);
        expect(block()).not.toMatch(/--shadow-glow-base:[^;]*0 0 24px/);
    });
});