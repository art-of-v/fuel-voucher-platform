// Jest config for the mobile app. Uses the `jest-expo` preset, which is pinned to
// the same SDK as `expo` in package.json (54.x) — it supplies the babel transform,
// the React Native module mocks, and the `transformIgnorePatterns` whitelist that
// lets the RN/Expo packages (shipped as untranspiled ESM/Flow) run under Jest.
//
// jest-expo bundles Jest 29, so the `jest` devDependency is pinned to ^29 to match:
// a Jest 30 CLI driving jest-expo's 29-era runtime is a known dual-runtime break.
//
// Tests live next to the code as `*.test.ts(x)`. tsconfig.json already excludes
// `**/*.test.ts` from `tsc --noEmit`, so the strict typecheck and the test transform
// stay independent — a test file never changes the app's type surface.
module.exports = {
  preset: 'jest-expo',
  // RNTL's `cleanup` runs automatically; this file adds any shared setup. Kept as
  // setupFilesAfterEnv (not setupFiles) so it runs after the test framework is live.
  setupFilesAfterEnv: ['<rootDir>/jest.setup.js'],
  // Only our own source. `roots` scopes discovery to app code (excludes the RN/Expo
  // internals jest-expo would otherwise walk); the glob is kept free of `<rootDir>`
  // on purpose — on Windows `<rootDir>` expands to a backslash path that does not
  // match a forward-slash glob, so an embedded-rootDir testMatch silently finds 0
  // tests. Scoping via `roots` + a relative glob is separator-agnostic.
  roots: ['<rootDir>/src', '<rootDir>/app'],
  testMatch: ['**/*.test.{ts,tsx}'],
  // Coverage is opt-in (`--coverage`); when collected, measure only app code.
  collectCoverageFrom: [
    'src/**/*.{ts,tsx}',
    '!src/**/*.test.{ts,tsx}',
    '!src/**/index.ts',
  ],
  clearMocks: true,
};
