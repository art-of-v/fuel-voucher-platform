import React from 'react';
import { render, screen } from '@testing-library/react-native';

import { PhoneAuthForm } from './PhoneAuthForm';
import type { AuthStep } from '../types';

/**
 * The screen that says WHICH code is wanted.
 *
 * The second factor used to be labelled "6-DIGIT CODE", with a hint that named the
 * authenticator app but not the trap: the code that just arrived by push is also six
 * digits. People typed the push code there, the server refused it, and the refusal was
 * reported as "could not confirm the device" - so the message pointed at the phone
 * instead of at the code. The copy below is what closes that trap, so it is pinned here.
 */

// The hook is the state machine; this suite is about what the screen says. The state
// machine has its own suite (useLogin.test.ts).
const mockUseLogin = jest.fn();
jest.mock('../hooks/useLogin', () => ({
  useLogin: (...args: unknown[]) => mockUseLogin(...args),
}));

jest.mock('../../../core/i18n', () => ({
  __esModule: true,
  useI18n: (selector?: (s: any) => unknown) => {
    const state = { t: (key: string) => key, language: 'uk', setLanguage: jest.fn() };
    return selector ? selector(state) : state;
  },
}));

jest.mock('../../../core/utils/haptics', () => ({
  Haptics: {
    impactAsync: jest.fn(),
    notificationAsync: jest.fn(),
    ImpactFeedbackStyle: { Light: 'light', Medium: 'medium', Heavy: 'heavy' },
    NotificationFeedbackType: { Success: 'success', Warning: 'warning', Error: 'error' },
  },
}));

function mockLoginAt(step: AuthStep) {
  mockUseLogin.mockReturnValue({
    step,
    phone: '+380991234567',
    code: '',
    twoFactorCode: '',
    twoFactor: { challenge: 'challenge-1', needsEnrollment: false, enrollmentSecret: null },
    loading: false,
    error: '',
    diagResult: '',
    setPhone: jest.fn(),
    setCode: jest.fn(),
    setTwoFactorCode: jest.fn(),
    handleSendCode: jest.fn(),
    handleVerifyCode: jest.fn(),
    handleVerifyTwoFactor: jest.fn(),
    resetToPhone: jest.fn(),
  });
}

describe('PhoneAuthForm - the second factor', () => {
  beforeEach(() => {
    mockUseLogin.mockReset();
  });

  it('names the authenticator app, and says the push code does not belong here', () => {
    mockLoginAt('two_factor');

    render(<PhoneAuthForm onSuccess={jest.fn()} />);

    // The label names the source of the code, so the field is not "just six digits".
    expect(screen.getByText('phoneAuth.twoFactorCodeLabel')).toBeTruthy();
    // The hint rules out the code that arrived by push - the one people typed here.
    expect(screen.getByText('phoneAuth.twoFactorHint')).toBeTruthy();
  });

  it('offers to start the sign-in over, not to change the phone number', () => {
    mockLoginAt('two_factor');

    render(<PhoneAuthForm onSuccess={jest.fn()} />);

    // A spent or expired challenge cannot be redeemed with any code, so the way forward
    // is a new sign-in. "Change phone number" does not say that, and on this step the
    // button does not change the number either.
    expect(screen.getByText('phoneAuth.startOver')).toBeTruthy();
    expect(screen.queryByText('phoneAuth.changePhone')).toBeNull();
  });
});
