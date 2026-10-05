import React from 'react';
import { render, screen, fireEvent } from '@testing-library/react-native';

import ProfileScreen from './profile';

/**
 * What this suite is for, and what it is not for.
 *
 * Not "renders without crashing" — that passes for a screen that renders the wrong
 * thing, which is exactly the failure this repo shipped once: a tab-bar change went
 * out with a green suite while the app laid out nothing at all.
 *
 * These are the decisions `profile.tsx` makes that nothing else covers, each with a
 * visible wrong answer if it breaks:
 *
 * - which sheet the header's edit button opens, which differs by account type
 * - the context-switcher label, which differs for a worker, so the employer's rights
 *   are never painted on the employee's own screen
 * - the ISO birthdate converted to a display date for the edit field
 * - the auth/loading gate, which must not flash one session's profile at the next user
 */

const mockUseProfile = jest.fn();
const mockUpdateProfile = jest.fn();
const mockUpdateCompany = jest.fn();
const mockDeleteAccount = jest.fn();

jest.mock('../src/core/i18n', () => ({
  __esModule: true,
  useI18n: () => ({ t: (key: string) => key, language: 'uk', setLanguage: jest.fn() }),
}));

jest.mock('../src/features/profile/hooks/useProfile', () => ({
  useProfile: (callbacks: unknown) => mockUseProfile(callbacks),
}));

jest.mock('../src/features/notifications/hooks/useNotifications', () => ({
  useUnreadNotificationCount: () => 0,
}));

// The nine sections are component-tested or trivially presentational. What matters
// here is which one the screen opens and with what props, so each mock surfaces
// exactly the values under test. Rendering them for real would drag in `core/ui`,
// Sentry and AsyncStorage for no assertion.
jest.mock('../src/features/profile/components', () => {
  // `require` here, not a top-level import: babel-plugin-jest-hoist moves this
  // factory above the imports, and referencing an out-of-scope binding from a module
  // factory is exactly what Jest forbids. Requiring inside is the sanctioned form.
  // eslint-disable-next-line @typescript-eslint/no-require-imports
  const { Text, Pressable } = require('react-native');

  /** Renders only when open, like the real Modal-backed sheets do. */
  const sheet = (label: string, surface?: (props: Record<string, any>) => string | null) => {
    const C = (props: Record<string, any>) => {
      if (!props.visible) return null;
      const extra = surface ? surface(props) : null;
      return (
        <Text testID={label}>
          {extra ? extra + '|' : ''}
          {label}
        </Text>
      );
    };
    C.displayName = label;
    return C;
  };

  return {
    ProfileHeaderCard: (props: Record<string, any>) => (
      <Pressable testID="header-card" onPress={props.onEdit} accessibilityRole="button" />
    ),
    ManagementSection: (props: Record<string, any>) => (
      <Text testID="management">{String(props.contextLabel)}</Text>
    ),
    ActivitySection: () => <Text testID="activity" />,
    PreferencesSection: () => <Text testID="preferences" />,
    AccountActions: (props: Record<string, any>) => (
      <Pressable testID="delete" onPress={props.onDelete} accessibilityRole="button" />
    ),
    EditPersonalSheet: sheet('personal-sheet', (p) => p.form.birthdate || null),
    EditCompanySheet: sheet('company-sheet', (p) => p.form.edrpou || null),
    ChangeEmailSheet: sheet('change-email-sheet'),
  };
});

const baseProfile = {
  user: {
    id: 'u1',
    firstName: 'Taras',
    lastName: 'K.',
    phone: '+380501234567',
    email: 't@example.com',
    birthdate: '1990-05-17',
  },
  isAuthenticated: true,
  isLoading: false,
  legalProfile: null,
  isBusiness: false,
  isWorkerContext: false,
  currentCompany: null,
  companies: [],
  pendingInvitationCount: 0,
  updateProfile: mockUpdateProfile,
  updateCompany: mockUpdateCompany,
  logout: jest.fn(),
  deleteAccount: mockDeleteAccount,
  isUpdatingProfile: false,
  isUpdatingCompany: false,
  isDeleting: false,
};

function renderScreen(overrides: Record<string, unknown> = {}) {
  mockUseProfile.mockReturnValue({ ...baseProfile, ...overrides });
  return render(<ProfileScreen />);
}

/** The header card's edit button is the only way into either edit sheet. */
function pressEdit() {
  fireEvent.press(screen.getByTestId('header-card'));
}

describe('ProfileScreen', () => {
  describe('the auth and loading gate', () => {
    it('shows a loading state instead of the profile while loading', () => {
      renderScreen({ isLoading: true });
      expect(screen.queryByTestId('management')).toBeNull();
    });

    it('shows a loading state while signed out, so profile data never flashes', () => {
      // The dangerous version of this bug: a signed-out user briefly sees the
      // previous session's name, phone and company before the redirect lands.
      renderScreen({ isAuthenticated: false });
      expect(screen.queryByTestId('management')).toBeNull();
    });

    it('renders the sections once authenticated and loaded', () => {
      renderScreen();
      expect(screen.getByTestId('management')).toBeTruthy();
      expect(screen.getByTestId('activity')).toBeTruthy();
      expect(screen.getByTestId('preferences')).toBeTruthy();
    });
  });

  describe('which sheet the edit button opens', () => {
    it('opens the personal sheet for a private account', () => {
      renderScreen({ isBusiness: false });
      pressEdit();
      expect(screen.getByTestId('personal-sheet')).toBeTruthy();
      expect(screen.queryByTestId('company-sheet')).toBeNull();
    });

    it('opens the company sheet for a business account', () => {
      // A business owner editing their company profile must not be handed a
      // personal-name form, and vice versa.
      renderScreen({ isBusiness: true });
      pressEdit();
      expect(screen.getByTestId('company-sheet')).toBeTruthy();
      expect(screen.queryByTestId('personal-sheet')).toBeNull();
    });

    it('keeps both sheets closed until edit is pressed', () => {
      // The real sheets render a `Modal`, which mounts nothing while hidden — so a
      // "visible" sheet that is really mounted would show up here.
      renderScreen();
      expect(screen.queryByTestId('personal-sheet')).toBeNull();
      expect(screen.queryByTestId('company-sheet')).toBeNull();
    });
  });

  describe('the context-switcher label', () => {
    it('names the active company', () => {
      renderScreen({ currentCompany: { name: 'Паливна', edrpou: '12345678' } });
      expect(screen.getByText('Паливна')).toBeTruthy();
    });

    it('marks a worker context, because the same company name means different rights', () => {
      // Without this suffix a worker managing the employer's stock sees what looks
      // like their own company settings page.
      renderScreen({
        isWorkerContext: true,
        currentCompany: { name: 'Паливна', edrpou: '12345678' },
      });
      expect(screen.getByText('Паливна · context.workerSubtitle')).toBeTruthy();
    });

    it('falls back to the personal root with no company', () => {
      renderScreen({ currentCompany: null });
      expect(screen.getByText('context.personal')).toBeTruthy();
    });
  });

  describe('the birthdate handed to the edit sheet', () => {
    it('converts the stored ISO date to the display format', () => {
      // The sheet takes this as its initial field content. A raw `1990-05-17` in a
      // field presented as a display date is the bug this guards.
      renderScreen();
      pressEdit();
      expect(screen.getByTestId('personal-sheet')).toHaveTextContent(/17\.05\.1990/);
    });

    it('leaves a non-ISO birthdate alone rather than mangling it', () => {
      renderScreen({ user: { ...baseProfile.user, birthdate: 'не відомо' } });
      pressEdit();
      expect(screen.getByTestId('personal-sheet')).toHaveTextContent(/не відомо/);
    });

    it('shows an empty field when there is no birthdate', () => {
      renderScreen({ user: { ...baseProfile.user, birthdate: null } });
      pressEdit();
      expect(screen.getByTestId('personal-sheet')).toBeTruthy();
      expect(screen.getByTestId('personal-sheet')).not.toHaveTextContent(/\d{2}\.\d{2}\.\d{4}/);
    });
  });

  describe('the company form the sheet receives', () => {
    it('is seeded from the legal profile, including the EDRPOU', () => {
      renderScreen({
        isBusiness: true,
        legalProfile: {
          name: 'Паливна',
          edrpou: '12345678',
          vatNumber: '999999999',
          directorName: 'Директор',
          address: 'Київ',
          phone: '+380441234567',
          email: 'legal@example.com',
        },
      });
      pressEdit();
      expect(screen.getByTestId('company-sheet')).toHaveTextContent(/12345678/);
    });
  });

  describe('delete confirmation', () => {
    it('does not offer the dialog until the delete action is used', () => {
      renderScreen();
      expect(screen.queryByText('profile.deleteAccountConfirm')).toBeNull();
    });

    it('asks for confirmation after the delete action is used', () => {
      // `deleteAccount` is destructive and irreversible, so the confirm gate is the
      // only thing standing between a tap and a gone account.
      renderScreen();
      fireEvent.press(screen.getByTestId('delete'));
      expect(screen.getByText('profile.deleteAccountConfirm')).toBeTruthy();
      expect(mockDeleteAccount).not.toHaveBeenCalled();
    });
  });
});
