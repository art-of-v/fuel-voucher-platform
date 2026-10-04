// Shared test setup, run after the framework is live (see jest.config.js).
//
// @testing-library/react-native v13 registers its matchers automatically when the
// library is first imported, so there is no `extend-expect` import to make here.
//
// jest.config.js sets `clearMocks: true`, so mock call history is reset between
// tests; the spies below are re-established in beforeEach so each test starts clean
// (no implementation leaks between tests).

beforeEach(() => {
  // A React `act(...)` warning means state updated outside an awaited act/waitFor —
  // the usual source of flaky hook tests — so turn it into a hard failure instead of
  // a log nobody reads.
  //
  // Any OTHER console.error is swallowed: this is a unit suite that deliberately
  // exercises the hooks' error branches (a rejected fetch, a VoucherActionError),
  // and those branches log by design. Real defects still surface as assertion
  // failures (wrong state, wrong Alert), and a test that wants to prove a log
  // happened can assert on this spy.
  jest.spyOn(console, 'error').mockImplementation((...args) => {
    const first = args[0];
    if (typeof first === 'string' && first.includes('not wrapped in act')) {
      throw new Error(first);
    }
  });
  // Info/warning noise from expected-failure paths (e.g. the hook's
  // "Data fetch failed" / "Unknown order status" logs). Kept quiet in tests.
  jest.spyOn(console, 'log').mockImplementation(() => {});
  jest.spyOn(console, 'warn').mockImplementation(() => {});
});

/**
 * Stubs for dependencies that need a native module or ship untranspiled ESM, and
 * that every component test pulls in transitively.
 *
 * These live here rather than in each test file because the `core/ui` barrel means
 * importing a single `Button` reaches `PageLayout` → `useTheme` → `appStore` →
 * AsyncStorage, and `ErrorBoundary` → Sentry. A component test cannot opt out of
 * any of it. `lucide-react-native` is not in the Jest transform allow-list, so
 * importing an icon is a parse error, and `@sentry/react-native` resolves to ESM
 * under Jest and needs a native module besides.
 *
 * The icons themselves are stubbed to null: they carry no behaviour, and a test
 * that cares about an icon's accessible name should read the control's label.
 */
jest.mock('@react-native-async-storage/async-storage', () =>
  require('@react-native-async-storage/async-storage/jest/async-storage-mock'),
);

jest.mock('@sentry/react-native', () => ({
  init: jest.fn(),
  captureException: jest.fn(),
  captureMessage: jest.fn(),
  addBreadcrumb: jest.fn(),
  setUser: jest.fn(),
  setTag: jest.fn(),
  setContext: jest.fn(),
  withScope: (cb) => cb({ setTag: jest.fn() }),
}));

jest.mock('lucide-react-native', () => {
  const Icon = () => null;
  return { __esModule: true, Icon };
});
