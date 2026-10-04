import { describe, it, expect, beforeEach } from "vitest";
import { DEFAULT_THEME } from "./themes";
import { useTheme } from "./theme-store";

const STORAGE_KEY = "admin-lemberg-theme";

function storedTheme(): unknown {
    return JSON.parse(localStorage.getItem(STORAGE_KEY) ?? "{}")?.state?.theme;
}

describe("theme store", () => {
    beforeEach(() => {
        localStorage.clear();
        useTheme.getState().setTheme(DEFAULT_THEME);
    });

    it("defaults to the theme whose palette lives on bare :root", () => {
        expect(DEFAULT_THEME).toBe("lemberg");
        expect(document.documentElement.dataset.theme).toBe("lemberg");
    });

    it("reflects a selection onto <html data-theme>, which is the whole switch", () => {
        useTheme.getState().setTheme("mercury");

        expect(document.documentElement.dataset.theme).toBe("mercury");
        expect(useTheme.getState().theme).toBe("mercury");
    });

    it("flags forge themes with <html data-forge>, which is what keys their 3D CSS", () => {
        for (const id of ["mercury", "lemberg-forge", "blue-forge", "obsidian-forge"] as const) {
            useTheme.getState().setTheme(id);
            expect(document.documentElement.dataset.forge, id).toBe("");
        }
    });

    it("clears data-forge for the other themes, so they keep their rounded glass", () => {
        // The flag is what applies the zero-radius / bevel rules, so leaving it set
        // would silently square the eight non-forge themes too.
        useTheme.getState().setTheme("mercury");
        useTheme.getState().setTheme("lemberg");

        expect(document.documentElement.dataset.theme).toBe("lemberg");
        expect("forge" in document.documentElement.dataset).toBe(false);
    });

    it("persists the selection so the next load does not flash the default", () => {
        useTheme.getState().setTheme("mercury");

        expect(storedTheme()).toBe("mercury");
    });

    it("clamps a stored theme that no longer exists back to the default", () => {
        // localStorage is user-writable and survives renames/removals, so a stale
        // id must not land on <html>: nothing would match it and the UI would
        // silently render as the default palette.
        localStorage.setItem(STORAGE_KEY, JSON.stringify({ state: { theme: 'retired-theme' }, version: 0 }));

        useTheme.persist.rehydrate();

        expect(useTheme.getState().theme).toBe(DEFAULT_THEME);
        expect(document.documentElement.dataset.theme).toBe(DEFAULT_THEME);
    });
});