import * as Sentry from '@sentry/react-native';
import Constants from 'expo-constants';
import { resolveSentryDsn, initSentry, reportError } from './sentry';

// The native module cannot load under Jest, so mock the SDK surface we call. This also
// guards the "inert without a DSN" contract: init/captureException must never fire unless
// a DSN resolves. Mirrors the backend SentryOptionsTests and admin sentry.test.
jest.mock('@sentry/react-native', () => ({
  init: jest.fn(),
  captureException: jest.fn(),
}));

// Mutable mock: each test sets extra.sentryDsn to exercise the app.json fallback path.
jest.mock('expo-constants', () => ({
  __esModule: true,
  default: { expoConfig: { version: '1.0.1', extra: { sentryDsn: '' } } },
}));

const mockedExtra = (Constants as unknown as { expoConfig: { extra: { sentryDsn: string } } })
  .expoConfig.extra;

const REAL_DSN = 'https://key@o1.ingest.de.sentry.io/42';

describe('mobile Sentry', () => {
  beforeEach(() => {
    delete process.env.EXPO_PUBLIC_SENTRY_DSN;
    mockedExtra.sentryDsn = '';
    jest.mocked(Sentry.init).mockClear();
    jest.mocked(Sentry.captureException).mockClear();
  });

  describe('resolveSentryDsn', () => {
    it('is undefined when neither env nor app config provides a DSN', () => {
      expect(resolveSentryDsn()).toBeUndefined();
    });

    it('prefers the build-time env var', () => {
      process.env.EXPO_PUBLIC_SENTRY_DSN = REAL_DSN;
      mockedExtra.sentryDsn = 'https://other@o1.ingest.de.sentry.io/99';
      expect(resolveSentryDsn()).toBe(REAL_DSN);
    });

    it('falls back to the embedded app config (expo.extra.sentryDsn)', () => {
      mockedExtra.sentryDsn = REAL_DSN;
      expect(resolveSentryDsn()).toBe(REAL_DSN);
    });
  });

  describe('initSentry', () => {
    it('does not initialise when no DSN is configured', () => {
      expect(initSentry()).toBe(false);
      expect(Sentry.init).not.toHaveBeenCalled();
    });

    it('initialises when a DSN resolves', () => {
      process.env.EXPO_PUBLIC_SENTRY_DSN = REAL_DSN;
      expect(initSentry()).toBe(true);
      expect(Sentry.init).toHaveBeenCalledTimes(1);
    });

    it('keeps PII off and tracing disabled (errors only)', () => {
      process.env.EXPO_PUBLIC_SENTRY_DSN = REAL_DSN;
      initSentry();
      expect(Sentry.init).toHaveBeenCalledWith(
        expect.objectContaining({ dsn: REAL_DSN, sendDefaultPii: false, tracesSampleRate: 0 }),
      );
    });
  });

  describe('reportError', () => {
    it('no-ops when Sentry is off', () => {
      reportError(new Error('boom'));
      expect(Sentry.captureException).not.toHaveBeenCalled();
    });

    it('captures the error when a DSN is configured', () => {
      process.env.EXPO_PUBLIC_SENTRY_DSN = REAL_DSN;
      const err = new Error('boom');
      reportError(err);
      expect(Sentry.captureException).toHaveBeenCalledWith(err);
    });
  });
});
