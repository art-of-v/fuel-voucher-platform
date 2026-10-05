// Metro configuration for the mobile app.
//
// Exists to keep test files out of the app bundle. `expo-router`'s entry point runs
// `require.context` over `app/`, so every file there is treated as a route and pulled
// into the bundle — including `*.test.tsx`. A screen test sitting in `app/` therefore
// dragged `@testing-library/react-native` into the shipped bundle, which in turn pulled
// in `logging-library`, whose `logger.js` does `require("console")`. React Native has no
// Node standard library, so the app failed to load with:
//
//   Unable to resolve module console from
//   node_modules/logging-library/react-native/build/helpers/logger.js
//
// Jest's `roots` covers both `src` and `app`, so screen tests have to be able to live in
// `app/` — which is exactly why the exclusion belongs here rather than in the test
// layout.
const { getDefaultConfig } = require('expo/metro-config');

const config = getDefaultConfig(__dirname);

/**
 * Test files, in our source only.
 *
 * Scoped to `/app/` and `/src/` rather than matching every `*.test.*` anywhere: some
 * published packages ship files named `.test.js` as fixtures, and blocking those would
 * break them. Metro normalises to forward slashes, so one separator is enough.
 */
const TEST_FILES = /(^|\/)(app|src)\/.*\.test\.[jt]sx?$/;

// Whatever Expo already excludes, kept — this replaces nothing.
const existing = config.resolver.blockList;
const previous = Array.isArray(existing) ? existing : existing ? [existing] : [];

config.resolver.blockList = [...previous, TEST_FILES];

module.exports = config;