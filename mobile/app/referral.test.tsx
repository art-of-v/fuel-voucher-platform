import React from 'react';
import { Share } from 'react-native';
import { render, screen, fireEvent, act } from '@testing-library/react-native';

import ReferralScreen from './referral';

/**
 * The referral screen — the surface the referral backend was built for
 * (planning #288).
 *
 * The screen has two jobs and each of them has a way to go wrong:
 *
 * - **Show the customer's own code.** The customer chooses it, the server sets
 *   it once and never changes it. A screen that offered a "change my code"
 *   action would be offering something the backend refuses; a screen that
 *   promised a bonus would be promising money the product does not pay.
 * - **Take a friend's code.** Once. Never the customer's own.
 *
 * The failures are part of the contract too: the server answers a rejected
 * referral with a bare sentence, and the rate limiter with a number of
 * seconds. Both have to reach the customer in words they can act on.
 */

const mockCreateCode = jest.fn();
const mockRedeemCode = jest.fn();
const mockSetStringAsync = jest.fn();

jest.mock('expo-clipboard', () => ({
  __esModule: true,
  setStringAsync: (...args: unknown[]) => mockSetStringAsync(...args),
}));

jest.mock('expo-router', () => ({
  __esModule: true,
  Redirect: () => null,
  router: { back: jest.fn(), push: jest.fn() },
  useRouter: () => ({ back: jest.fn(), push: jest.fn() }),
  usePathname: () => '/referral',
}));

jest.mock('../src/core/i18n', () => {
  // The store is used both ways in the app — `useI18n()` for the whole state and
  // `useI18n((s) => s.t)` for one field — so the stand-in has to answer both.
  const state = {
    t: (key: string, ...params: string[]) => (params.length ? `${key}:${params.join(',')}` : key),
    language: 'uk',
    setLanguage: jest.fn(),
  };
  return {
    __esModule: true,
    useI18n: (selector?: (s: typeof state) => unknown) => (selector ? selector(state) : state),
  };
});

jest.mock('../src/features/auth/hooks/useAuth', () => ({
  useAuth: () => ({ isAuthenticated: true, isLoading: false }),
}));

let mockReferral: Record<string, unknown> = {};

jest.mock('../src/features/referral/hooks/useReferral', () => ({
  useReferral: () => mockReferral,
}));

function baseReferral(overrides: Record<string, unknown> = {}) {
  return {
    myCode: null,
    referredBy: null,
    createCode: mockCreateCode,
    redeemCode: mockRedeemCode,
    isCreating: false,
    isRedeeming: false,
    createError: null,
    redeemError: null,
    retryAfterSeconds: undefined,
    ...overrides,
  };
}

function renderScreen(overrides: Record<string, unknown> = {}) {
  mockReferral = baseReferral(overrides);
  return render(<ReferralScreen />);
}

beforeEach(() => {
  mockReferral = baseReferral();
  jest.spyOn(Share, 'share').mockResolvedValue({ action: 'sharedAction' } as never);
});

describe('ReferralScreen — the customer’s own code', () => {
  it('offers a code to choose when the customer has none', () => {
    renderScreen();

    expect(screen.getByTestId('referral-create-code')).toBeTruthy();
    // Nothing to share yet, so there is nothing to copy.
    expect(screen.queryByText('referral.copy')).toBeNull();
    expect(screen.queryByText('referral.share')).toBeNull();
  });

  it('shows the code the customer already has, with copy and share', () => {
    renderScreen({ myCode: 'ABC-123' });

    expect(screen.getByDisplayValue('ABC-123')).toBeTruthy();
    expect(screen.getByText('referral.copy')).toBeTruthy();
    expect(screen.getByText('referral.share')).toBeTruthy();
    // The code is set for good, so the form to choose one is gone.
    expect(screen.queryByTestId('referral-create-code')).toBeNull();
  });

  it('sends the code the customer typed', async () => {
    renderScreen();

    fireEvent.changeText(screen.getByTestId('referral-create-code'), 'abc-123');
    await act(async () => {
      fireEvent.press(screen.getByText('referral.createCode'));
    });

    expect(mockCreateCode).toHaveBeenCalledWith('abc-123');
  });

  it('says why the server refused, in the field that caused it', () => {
    // "Referral code already taken" is the sentence the server sends with a 409;
    // the customer has to learn it in the field they are still standing in.
    renderScreen({ createError: 'already_taken' });

    expect(screen.getByText('referral.error.alreadyTaken')).toBeTruthy();
  });

  it('copies the code to the clipboard', async () => {
    renderScreen({ myCode: 'ABC-123' });

    await act(async () => {
      fireEvent.press(screen.getByText('referral.copy'));
    });

    expect(mockSetStringAsync).toHaveBeenCalledWith('ABC-123');
  });

  it('shares the code itself, not a link to a screen', async () => {
    renderScreen({ myCode: 'ABC-123' });

    await act(async () => {
      fireEvent.press(screen.getByText('referral.share'));
    });

    expect(Share.share).toHaveBeenCalledWith({ message: 'referral.shareMessage:ABC-123' });
  });
});

describe('ReferralScreen — a friend’s code', () => {
  it('takes the code the customer was given', async () => {
    renderScreen();

    fireEvent.changeText(screen.getByTestId('referral-friend-code'), 'FRIEND10');
    await act(async () => {
      fireEvent.press(screen.getByText('referral.applyCode'));
    });

    expect(mockRedeemCode).toHaveBeenCalledWith('FRIEND10');
  });

  it('hides the form once the customer has been referred', () => {
    // A customer is referred once, for good. Leaving the form on screen would
    // invite them to spend one of the five writes an hour on a second attempt
    // the server will refuse.
    renderScreen({ referredBy: 'FRIEND10' });

    expect(screen.getByDisplayValue('FRIEND10')).toBeTruthy();
    expect(screen.queryByTestId('referral-friend-code')).toBeNull();
  });

  it('turns the rate limit into a number of minutes', () => {
    // The limiter answers 429 with a Retry-After. "Try again later" is not an
    // answer; "in 2 minutes" is a fact the customer can plan around.
    renderScreen({ redeemError: 'rate_limited', retryAfterSeconds: 120 });

    expect(screen.getByText('referral.error.rateLimited:2')).toBeTruthy();
  });
});
