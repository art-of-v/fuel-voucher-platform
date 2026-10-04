import React from 'react';
import { fireEvent, render, screen } from '@testing-library/react-native';

import { Haptics } from '../../../core/utils/haptics';
import { AccountActions } from './AccountActions';

// `lucide-react-native` ships ESM and is not in the Jest transform allow-list, so
// any test touching a component that imports icons has to stub it. The icons carry
// no behaviour here; the buttons' accessible names come from `Button`'s label.
jest.mock('lucide-react-native', () => {
  const Icon = () => null;
  return { __esModule: true, LogOut: Icon, Trash2: Icon };
});

// `lucide-react-native` ships ESM and is not in the Jest transform allow-list, so
// any test touching a component that imports icons has to stub it. The icons carry
// no behaviour here; the buttons' accessible names come from `Button`'s label.
jest.mock('expo-router', () => ({
  __esModule: true,
  useRouter: () => ({ push: jest.fn() }),
}));

// Echo translation keys so the accessible names are the keys themselves. Left real,
// `t()` would resolve to Ukrainian (the store's default language) and every
// assertion here would be asserting against copy that changes with the locale.
//
// `useI18n` is a zustand store, so it is called both ways in this codebase —
// `useI18n((s) => s.t)` and plain `useI18n()` — and the mock has to answer both.
jest.mock('../../../core/i18n', () => ({
  __esModule: true,
  useI18n: (selector?: (s: any) => unknown) => {
    const state = { t: (key: string) => key, language: 'uk', setLanguage: jest.fn() };
    return selector ? selector(state) : state;
  },
}));

beforeEach(() => {
  jest.spyOn(Haptics, 'impactAsync').mockResolvedValue(undefined as any);
});

/**
 * Both rows are `Button`s, and `Button` fires a haptic on every press itself
 * (`hapticStyle`, default `medium`). That is why the destructive row is weighted
 * through the prop rather than through a manual call in `onPress` — the screen
 * used to buzz twice here, a Medium from the primitive plus a Heavy from the
 * handler, which reads as a stutter rather than as emphasis.
 */
describe('AccountActions', () => {
  it('calls back directly on sign out', () => {
    const onSignOut = jest.fn();
    render(<AccountActions onSignOut={onSignOut} onDelete={jest.fn()} />);

    fireEvent.press(screen.getByRole('button', { name: 'profile.signOut' }));

    expect(onSignOut).toHaveBeenCalledTimes(1);
  });

  it('asks before deleting, and never deletes from the row itself', () => {
    const onDelete = jest.fn();
    render(<AccountActions onSignOut={jest.fn()} onDelete={onDelete} />);

    fireEvent.press(screen.getByRole('button', { name: 'profile.deleteAccount' }));

    expect(onDelete).toHaveBeenCalledTimes(1);
  });

  it('gives the destructive row the heaviest haptic in the app', () => {
    render(<AccountActions onSignOut={jest.fn()} onDelete={jest.fn()} />);

    fireEvent.press(screen.getByRole('button', { name: 'profile.deleteAccount' }));

    expect(Haptics.impactAsync).toHaveBeenCalledWith(Haptics.ImpactFeedbackStyle.Heavy);
  });

  it('buzzes exactly once per press', () => {
    // The regression this guards: a manual Haptics call in `onPress` on top of
    // the one Button already fires.
    render(<AccountActions onSignOut={jest.fn()} onDelete={jest.fn()} />);

    fireEvent.press(screen.getByRole('button', { name: 'profile.deleteAccount' }));

    expect(Haptics.impactAsync).toHaveBeenCalledTimes(1);
  });

  it('leaves sign out at the default weight', () => {
    render(<AccountActions onSignOut={jest.fn()} onDelete={jest.fn()} />);

    fireEvent.press(screen.getByRole('button', { name: 'profile.signOut' }));

    expect(Haptics.impactAsync).toHaveBeenCalledWith(Haptics.ImpactFeedbackStyle.Medium);
  });
});
