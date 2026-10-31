import React from 'react';
import { TextInput } from 'react-native';
import { fireEvent, render, screen } from '@testing-library/react-native';

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

function mockLoginAt(step: AuthStep, overrides: Record<string, unknown> = {}) {
  mockUseLogin.mockReturnValue({
    step,
    phone: '+380991234567',
    code: '',
    twoFactorCode: '',
    twoFactor: { challenge: 'challenge-1', needsEnrollment: false, enrollmentSecret: null },
    loading: false,
    error: '',
    diagResult: '',
    codeExpired: false,
    secondsLeft: null,
    setPhone: jest.fn(),
    setCode: jest.fn(),
    setTwoFactorCode: jest.fn(),
    handleSendCode: jest.fn(),
    handleResendCode: jest.fn(),
    handleVerifyCode: jest.fn(),
    handleVerifyTwoFactor: jest.fn(),
    resetToPhone: jest.fn(),
    ...overrides,
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

describe('PhoneAuthForm - the code field', () => {
  beforeEach(() => {
    mockUseLogin.mockReset();
  });

  it('is the same field after the sign-in moves from the SMS code to the second factor', () => {
    // Two steps, two copies of the same six-digit field. They were two separate text
    // inputs, so moving on from the SMS code tore one down and mounted the next - with
    // `autoFocus` asking iOS for the keyboard inside that same commit, which is where
    // the app died. One shared field means there is nothing to tear down.
    mockLoginAt('code');
    const view = render(<PhoneAuthForm onSuccess={jest.fn()} />);
    const field = screen.getByTestId('auth-code-field');

    mockLoginAt('two_factor');
    view.rerender(<PhoneAuthForm onSuccess={jest.fn()} />);

    expect(screen.getByTestId('auth-code-field')).toBe(field);
    // ...and it is now asking for the code from the authenticator app, not from the SMS.
    expect(screen.getByText('phoneAuth.twoFactorCodeLabel')).toBeTruthy();
    expect(screen.queryByText('phoneAuth.codeLabel')).toBeNull();
  });

  it('takes focus back when the second factor step appears', () => {
    // The field is mounted once, so `autoFocus` can no longer be what opens the keyboard
    // on the later step. Without this the person would land on the authenticator screen
    // with a closed keyboard and have to tap a field that had already been focused once.
    const focus = jest.spyOn(TextInput.prototype as any, 'focus');

    mockLoginAt('code');
    const view = render(<PhoneAuthForm onSuccess={jest.fn()} />);
    focus.mockClear();

    mockLoginAt('two_factor');
    view.rerender(<PhoneAuthForm onSuccess={jest.fn()} />);

    expect(focus).toHaveBeenCalled();

    focus.mockRestore();
  });

  it('still names the SMS code on the SMS step', () => {
    mockLoginAt('code');

    render(<PhoneAuthForm onSuccess={jest.fn()} />);

    expect(screen.getByText('phoneAuth.codeLabel')).toBeTruthy();
    expect(screen.queryByText('phoneAuth.twoFactorCodeLabel')).toBeNull();
    expect(screen.getByText('phoneAuth.changePhone')).toBeTruthy();
    expect(screen.queryByText('phoneAuth.startOver')).toBeNull();
  });
});

/*
 * The code dies on a clock. While it lives the step counts it down; once it is gone the step
 * stops pretending and offers the one action that helps, without a trip back to the phone
 * step. Both states are copy, so they are pinned here.
 */
describe('PhoneAuthForm - the code clock', () => {
  beforeEach(() => {
    mockUseLogin.mockReset();
  });

  it('counts the code down while it is still alive', () => {
    mockLoginAt('code', { secondsLeft: 300 });

    render(<PhoneAuthForm onSuccess={jest.fn()} />);

    expect(screen.getByText('phoneAuth.codeValidFor')).toBeTruthy();
    expect(screen.queryByText('phoneAuth.codeExpired')).toBeNull();
    expect(screen.queryByText('phoneAuth.sendNewCode')).toBeNull();
  });

  it('says the code is gone and offers a new one from the same screen', () => {
    const handleResendCode = jest.fn();
    mockLoginAt('code', { codeExpired: true, secondsLeft: 0, handleResendCode });

    render(<PhoneAuthForm onSuccess={jest.fn()} />);

    expect(screen.getByText('phoneAuth.codeExpired')).toBeTruthy();
    // The countdown is about a code that no longer exists.
    expect(screen.queryByText('phoneAuth.codeValidFor')).toBeNull();

    fireEvent.press(screen.getByText('phoneAuth.sendNewCode'));
    expect(handleResendCode).toHaveBeenCalledTimes(1);
  });

  it('shows no clock and no expiry on the second factor', () => {
    // The authenticator code has its own lifetime and its own app; the SMS clock has no
    // business on that step, even if a stale deadline is still in state.
    mockLoginAt('two_factor', { secondsLeft: 300 });

    render(<PhoneAuthForm onSuccess={jest.fn()} />);

    expect(screen.queryByText('phoneAuth.codeValidFor')).toBeNull();
    expect(screen.queryByText('phoneAuth.codeExpired')).toBeNull();
  });
});
