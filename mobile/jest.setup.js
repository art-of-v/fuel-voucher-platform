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
