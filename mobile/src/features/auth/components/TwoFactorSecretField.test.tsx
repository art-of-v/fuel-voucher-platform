import React from 'react';
import { render, screen, fireEvent, waitFor } from '@testing-library/react-native';

import { TwoFactorSecretField } from './TwoFactorSecretField';
import { useToastStore } from '../../../core/feedback/toastStore';

const mockSetStringAsync = jest.fn();

jest.mock('expo-clipboard', () => ({
  __esModule: true,
  setStringAsync: (...args: unknown[]) => mockSetStringAsync(...args),
}));

jest.mock('../../../core/i18n', () => {
  const state = {
    t: (key: string) => key,
    language: 'uk',
    setLanguage: jest.fn(),
  };
  return {
    __esModule: true,
    useI18n: (selector?: (s: typeof state) => unknown) => (selector ? selector(state) : state),
  };
});

jest.mock('../../../core/utils/haptics', () => ({
  Haptics: {
    impactAsync: jest.fn(),
    notificationAsync: jest.fn(),
    selectionAsync: jest.fn(),
    ImpactFeedbackStyle: { Light: 'light', Medium: 'medium', Heavy: 'heavy' },
    NotificationFeedbackType: { Success: 'success', Warning: 'warning', Error: 'error' },
  },
}));

// A fake enrollment secret. The field only shows and copies the string, and a random-looking
// base32 value here is read by the secret scanner as a leaked key (gitleaks: generic-api-key).
const SECRET = 'test-totp-secret';

describe('TwoFactorSecretField', () => {
  it('shows the enrollment secret as selectable text', () => {
    render(<TwoFactorSecretField secret={SECRET} />);

    const secret = screen.getByText(SECRET);
    expect(secret.props.selectable).toBe(true);
  });

  it('copies the secret to the clipboard when the copy action is pressed', async () => {
    render(<TwoFactorSecretField secret={SECRET} />);

    fireEvent.press(screen.getByTestId('two-factor-secret-copy'));

    await waitFor(() => expect(mockSetStringAsync).toHaveBeenCalledWith(SECRET));
  });

  it('confirms the copy', async () => {
    render(<TwoFactorSecretField secret={SECRET} />);

    fireEvent.press(screen.getByTestId('two-factor-secret-copy'));

    await waitFor(() => {
      expect(useToastStore.getState().current?.kind).toBe('success');
      expect(useToastStore.getState().current?.message).toBe('phoneAuth.twoFactorSecretCopied');
    });
  });
});
