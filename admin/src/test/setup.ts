import "@testing-library/jest-dom/vitest";

// Node >= 22 exposes an experimental global `localStorage` that resolves to
// undefined unless --localstorage-file is passed, and it shadows the jsdom one —
// so every zustand `persist` store (theme-store, language) silently reads and
// writes nothing under vitest. Install a minimal in-memory Storage when the
// environment provides none.
if (typeof globalThis.localStorage === "undefined") {
    const entries = new Map<string, string>();
    const memoryStorage: Storage = {
        get length() {
            return entries.size;
        },
        clear: () => entries.clear(),
        getItem: (key) => entries.get(key) ?? null,
        key: (index) => [...entries.keys()][index] ?? null,
        removeItem: (key) => void entries.delete(key),
        setItem: (key, value) => void entries.set(key, String(value)),
    };
    for (const target of [globalThis, typeof window === "undefined" ? null : window]) {
        if (!target) continue;
        Object.defineProperty(target, "localStorage", {
            value: memoryStorage,
            configurable: true,
            writable: true,
        });
    }
}
