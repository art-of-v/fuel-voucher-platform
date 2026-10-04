import { describe, it, expect } from "vitest";
import { readFileSync, readdirSync } from "node:fs";
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

/** The shared structure block every forge theme inherits. */
function forgeBlock(): string {
    return css.match(/:root\[data-forge\]\s*\{([^}]*)\}/)?.[1] ?? "";
}

/** Ids of the forge family, in catalogue order. */
const forgeIds = themeOptions.map((t) => t.id).filter((id) => themes[id].forge);

/** WCAG relative luminance of a #rrggbb string. */
function luminance(hex: string): number {
    const ch = [1, 3, 5].map((i) => parseInt(hex.slice(i, i + 2), 16) / 255);
    const [r, g, b] = ch.map((c) => (c <= 0.04045 ? c / 12.92 : ((c + 0.055) / 1.055) ** 2.4));
    return 0.2126 * r + 0.7152 * g + 0.0722 * b;
}

/** WCAG contrast ratio between two #rrggbb strings. */
function contrast(a: string, b: string): number {
    const [hi, lo] = [luminance(a), luminance(b)].sort((x, y) => y - x);
    return (hi + 0.05) / (lo + 0.05);
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
            expect(typeof themes[id].forge, `themes[${id}].forge`).toBe("boolean");
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
        // `[a-z-]+` so the prose comments that use a `…` placeholder don't count,
        // and so hyphenated ids like `blue-forge` are captured whole.
        const declared = [...css.matchAll(/:root\[data-theme="([a-z-]+)"\]/g)].map((m) => m[1]);
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

describe("forge family", () => {
    const block = () => forgeBlock();

    it("covers Mercury plus one hue variant per coloured original", () => {
        // The user's brief: the graphite treatment, carried by each hue.
        expect(forgeIds).toEqual(["mercury", "lemberg-forge", "blue-forge", "obsidian-forge"]);
    });

    it("gives every member a hue block, so none inherits another's accent", () => {
        // --primary-foreground is deliberately shared: ink-dark is the only label
        // colour that clears AA on all four keys, and it is asserted below against
        // each key rather than assumed.
        for (const id of forgeIds) {
            const b = themeBlock(id) ?? "";
            for (const token of [
                "--primary",
                "--accent",
                "--accent-foreground",
                "--glow",
                "--text-gradient",
                "--key-hi",
                "--key-mid",
                "--key-lo",
                "--key-edge",
                "--key-deep",
            ]) {
                expect(b, `${id} must declare ${token}`).toMatch(new RegExp(`${token}:`));
            }
        }
    });

    it("labels the primary key legibly in every member", () => {
        // A new hue is one hex edit away from being unreadable, so the guard is
        // the actual contrast ratio rather than "we eyeballed it once".
        const ink = /--primary-foreground:\s*(#[0-9a-f]{6})/i.exec(forgeBlock())?.[1];
        expect(ink, "shared --primary-foreground").toBeTruthy();
        for (const id of forgeIds) {
            const key = /--key-mid:\s*(#[0-9a-f]{6})/i.exec(themeBlock(id)!)?.[1];
            expect(key, `${id} --key-mid`).toBeTruthy();
            expect(contrast(ink!, key!), `${id} key label`).toBeGreaterThanOrEqual(4.5);
        }
    });

    it("gives every member a distinct key colour", () => {
        const mids = forgeIds.map((id) => themeBlock(id)!.match(/--key-mid:\s*(#[0-9a-f]{6})/i)?.[1]);
        expect(mids.every(Boolean)).toBe(true);
        expect(new Set(mids.map((m) => m!.toLowerCase())).size).toBe(forgeIds.length);
    });

    it("declares an opaque graphite surface for every translucency slot", () => {
        // Any alpha in these slots puts the aurora or the page back through the
        // panel — the exact thing this family exists to remove.
        for (const token of [
            "--background",
            "--card",
            "--muted",
            "--border",
            "--input",
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
        // --accent moved into the per-hue blocks, so check those instead.
        for (const id of forgeIds) {
            expect(themeBlock(id)!, `${id} --accent`).toMatch(/--accent:\s*#[0-9a-f]{6};/i);
        }
    });

    it("keeps status colours hue-neutral so a severity means the same everywhere", () => {
        for (const token of ["--warning", "--success", "--info", "--destructive"]) {
            expect(block(), token).toMatch(new RegExp(`${token}:\\s*#[0-9a-f]{6};`, "i"));
        }
        for (const id of forgeIds) {
            for (const token of ["--warning", "--success", "--info", "--destructive"]) {
                expect(themeBlock(id)!, `${id} must not retint ${token}`).not.toContain(token);
            }
        }
    });

    it("uses a monochrome mercury primary with an ink-dark label", () => {
        expect(themeBlock("mercury")).toMatch(/--primary:\s*#cbd4de;/i);
    });

    it("strips the blur and gradient text off the shared .glass-* classes", () => {
        expect(css).toMatch(/:root\[data-forge\][^{]*\.glass-panel[^{]*\{[^}]*backdrop-filter:\s*none/);
        expect(css).toMatch(/:root\[data-forge\][^{]*\.glass-chrome[^{]*\{[^}]*backdrop-filter:\s*none/);
        expect(css).toMatch(
            /:root\[data-forge\][^{]*\.glass-text-gradient[^{]*\{[^}]*color:\s*var\(--foreground\)/,
        );
        // Tailwind's blur helpers carry no token, so the filter is killed outright.
        expect(css).toMatch(
            /:root\[data-forge\][^{]*\.backdrop-blur-md[^{]*\{[^}]*backdrop-filter:\s*none/,
        );
        // The glow used to be the active-nav state; the forge presses the item into
        // the sidebar instead, behind a 3px rail in the theme's own hue.
        expect(css).toMatch(/:root\[data-forge\][^{]*\.glass-glow[^{]*\{[^}]*border-left:\s*3px solid var\(--primary\)/);
    });

    it("switches the ambient aurora off entirely", () => {
        expect(css).toMatch(/:root\[data-forge\][^{]*\.aurora-bg[^{]*\{[^}]*display:\s*none/);
    });

    it("collapses every corner in the app to a hard square", () => {
        // One rule covers rounded-full / rounded-3xl / bare `rounded`, which are
        // not radius tokens — collapsing only --radius-*-base would miss them.
        expect(css).toMatch(
            /:root\[data-forge\]\s+:where\(\*,\s*\*::before,\s*\*::after\)\s*\{[^}]*border-radius:\s*0 !important/,
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
        expect(css).toMatch(/:root\[data-forge\][^{]*\.glass-panel[^{]*\{[^}]*inset 0 1px 0 var\(--edge-hi\)/);
        // Inputs go the other way — a well is sunken, not raised.
        expect(css).toMatch(/:root\[data-forge\][^{]*\.glass-input[^{]*\{[^}]*inset 0 2px 4px/);
        // The primary key is the one place real light is spent, and it is hue-driven
        // so a single rule serves all four members.
        const key = css.match(/:root\[data-forge\][^{]*button\.bg-primary[^{]*\{[^}]*\}/)?.[0] ?? "";
        expect(key).toMatch(/linear-gradient\(180deg, var\(--key-hi\)/);
        expect(key).toMatch(/0 2px 0 var\(--key-edge\)/);
    });

    it("sets the squarish techno face the mobile app uses for display type", () => {
        expect(block()).toMatch(/--font-display:\s*"Rajdhani"/);
        // Rajdhani has been in index.html all along but unused; check it is still
        // requested, or the whole family silently falls back to Inter.
        const html = readFileSync(path.resolve(import.meta.dirname, "../../index.html"), "utf8");
        expect(html).toMatch(/family=Rajdhani/);
        expect(css).toMatch(/:root\[data-forge\][^{]*body[^{]*\{[^}]*font-family:\s*var\(--font-display\)/);
    });

    it("ships the lion as one mask, not a pre-coloured asset per theme", () => {
        // The point of the mask is that a hue change needs no new file, so this
        // asserts both halves: the mask exists, and there is exactly one lion.
        const dir = path.resolve(import.meta.dirname, "../assets");
        const lions = readdirSync(dir).filter((f) => f.startsWith("lion"));
        expect(lions).toEqual(["lion-mask.png"]);

        const bytes = readFileSync(path.join(dir, "lion-mask.png"));
        expect(bytes.subarray(1, 4).toString("ascii")).toBe("PNG");
        expect(bytes.length).toBeLessThan(160 * 1024);

        // And it has to actually be a mask: white RGB with the drawing in alpha.
        const css = readFileSync(path.resolve(import.meta.dirname, "../index.css"), "utf8");
        expect(css).toMatch(/\.brand-mark\s*\{[^}]*mask-image:\s*url\("\.\/assets\/lion-mask\.png"\)/);
        expect(css).toMatch(/--mark-hi:\s*var\(--key-hi\)/);
    });
});