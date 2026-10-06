import React from 'react';
import { Alert } from 'react-native';
import { act, render, screen, fireEvent } from '@testing-library/react-native';

import CompanyScreen from './company';

/**
 * What this suite covers.
 *
 * `company.tsx` is now 331 lines with all seven sections extracted, so the screen's
 * remaining logic is the roster's own rules — and the important one is access:
 *
 * - **a non-owner must never see the roster.** The screen redirects to `/my-codes`
 *   when the context is not an owner one. Without that, a stale link or a fired worker
 *   would see every employee, their phone numbers and the fuel issued to them.
 * - **fire, recall and block all confirm first**, and each passes the right id. Block
 *   stops a worker spending a voucher even though it is reversible; recall takes fuel
 *   back. Neither should be one tap.
 * - **a completed gift clears the selection and closes the sheet.** Otherwise the next
 *   employee inherits the previous one's selection — issuing fuel that was already
 *   spoken for.
 *
 * `workerName`/`invitationName` fall back to the phone number when a name is missing,
 * which is also covered since it is what a half-filled HR import renders as.
 */

const mockUseCompany = jest.fn();
const mockFire = jest.fn();
const mockRecall = jest.fn();
const mockBlock = jest.fn();
const mockUnblock = jest.fn();
const mockGift = jest.fn();
const mockInvite = jest.fn();
const mockCancelInvite = jest.fn();
const alertSpy = jest.spyOn(Alert, 'alert');

jest.mock('expo-router', () => ({
  // A host element, not a bare text fragment: `toJSON()` shows a raw string root when a
  // component returns only text, and `getByText` does not match that — which looked like
  // the redirect never happening when it had. Defined inside the factory because Jest
  // forbids referencing an out-of-scope binding from a module factory.
  Redirect: ({ href }: { href: string }) => {
    // eslint-disable-next-line @typescript-eslint/no-require-imports
    const { Text } = require('react-native');
    return <Text testID="redirect">{href}</Text>;
  },
  Stack: { Screen: () => null },
  Tabs: { Screen: () => null },
  router: { push: jest.fn(), replace: jest.fn(), back: jest.fn(), navigate: jest.fn() },
  useLocalSearchParams: () => ({}),
  useGlobalSearchParams: () => ({}),
  usePathname: () => '/company',
  useSegments: () => [],
  useRootNavigationState: () => ({
    key: 'root',
    index: 0,
    routes: [],
    stale: false,
    type: 'stack',
  }),
  useNavigation: () => ({ setOptions: jest.fn() }),
  useNavigationState: () => ({ index: 0 }),
  useIsFocused: () => true,
  useFocusEffect: () => {},
  Link: () => null,
}));

// `useI18n` has two call shapes in this codebase: `const { t } = useI18n()` in
// screens, and `useI18n((s) => s.t)` in `core/ui` components like ScreenHeader. The
// mock has to answer both, or the real ScreenHeader receives the whole state object
// where it expects the translate function and fails with "t is not a function".
jest.mock('../src/core/i18n', () => ({
  __esModule: true,
  useI18n: (selector?: (s: any) => unknown) => {
    const state = { t: (key: string) => key, language: 'uk', setLanguage: jest.fn() };
    return selector ? selector(state) : state;
  },
}));

jest.mock('../src/features/company/hooks/useCompany', () => ({
  useCompany: (callbacks: Record<string, unknown>) => mockUseCompany(callbacks),
}));

// Each section mock surfaces only the handlers under test. A module mock must return
// an object carrying the named exports — returning a bare function once made a
// component undefined and React blamed an unrelated line.
jest.mock('../src/features/company/components', () => {
  // eslint-disable-next-line @typescript-eslint/no-require-imports
  const { Pressable, Text } = require('react-native');
  const btn = (id: string, onPress?: () => void) => (
    <Pressable testID={id} accessibilityRole="button" onPress={onPress} />
  );
  return {
    CompanyStatsRow: () => <Text testID="stats" />,
    InviteWorkerForm: (p: Record<string, any>) => (
      <>
        <Text testID="phone">{String(p.phone)}</Text>
        {/* Type into the field, so the reset has something to reset. Asserting the
            prefix before and after a callback that was never preceded by a change
            passes whether or not the reset exists. */}
        {btn('type-phone', () => p.setPhone('+380501119988'))}
      </>
    ),
    PendingInvites: () => <Text testID="pending" />,
    // `HubOrdersBranch` was added alongside the Orders tab (#858). A barrel mock must
    // carry every name the screen imports — a missing one is `undefined`, which React
    // reports as an invalid element type several frames from the real cause.
    HubOrdersBranch: () => <Text testID="orders-branch" />,
    // `HubWorkersBranch` is the single branch component the screen mounts; it composes
    // the worker table, the issued-voucher list and the blocked list internally.
    HubWorkersBranch: (p: Record<string, any>) => (
      <>
        {p.members.map((m: any) => (
          <Text key={m.id} testID={'name-' + m.id}>
            {p.memberName(m)}
          </Text>
        ))}
        {p.gifted.map((v: any) => (
          <Text key={v.id} testID={'recall-' + v.id}>
            {v.id}
          </Text>
        ))}
        {btn('fire-' + p.members[0]?.id, () => p.confirmFire(p.members[0]))}
        {btn('gift-' + p.members[0]?.id, () => p.openGift(p.members[0]))}
        {btn('recall-btn-' + p.gifted[0]?.id, () => p.confirmRecall(p.gifted[0]))}
        {btn('block-btn-' + p.gifted[0]?.id, () => p.confirmBlock(p.gifted[0]))}
        {p.blocked.map((v: any) => btn('unblock-' + v.id, () => p.unblock(v.id)))}
      </>
    ),
    IssueVoucherModal: (p: Record<string, any>) => (
      <>
        <Text testID="modal">{p.giftTarget ? 'open' : 'closed'}</Text>
        {/* The modal is pointed at a person by a pre-built `{ workerUserId, label }` (as of
            #858), not at a member — so the label arrives resolved. Asserting the
            rendered name keeps the test about what the owner sees, not who computed
            the string. */}
        <Text testID="worker-label">{p.giftTarget ? String(p.giftTarget.label) : ''}</Text>
        <Text testID="selected">{[...p.selected].sort().join(',')}</Text>
        {btn('select-all', p.toggleSelectAll)}
        {btn('confirm-gift', () => p.gift([...p.selected]))}
      </>
    ),
  };
});

const member = (id: string, extra: Record<string, unknown> = {}) => ({
  id,
  userId: 'u-' + id,
  workerFirstName: 'Іван',
  workerLastName: 'Петров',
  workerPhoneNumber: '+380501112233',
  role: 'WORKER',
  ...extra,
});

const voucher = (id: string) => ({
  id,
  status: 'active',
  amount: 10,
  provider: 'okko',
  fuelType: 'A95',
  expirationDate: null,
  workerUserId: 'u-1',
});

const baseData = {
  isAuthenticated: true,
  authLoading: false,
  isLoading: false,
  hasQueryError: false,
  isOwnerContext: true,
  members: [member('m1')],
  giftable: [voucher('v1'), voucher('v2')],
  gifted: [voucher('g1')],
  blocked: [voucher('b1')],
  pendingInvites: [],
  giftGroups: [],
  invite: mockInvite,
  cancelInvite: mockCancelInvite,
  fire: mockFire,
  gift: mockGift,
  recall: mockRecall,
  block: mockBlock,
  unblock: mockUnblock,
  refreshAll: jest.fn(),
  isInviting: false,
  isCancelling: false,
  isFiring: false,
  isGifting: false,
  isRecalling: false,
  isBlocking: false,
  isUnblocking: false,
};

/** Captures the callbacks the screen passes to useCompany. */
let capturedCallbacks: Record<string, any> = {};

function renderScreen(overrides: Record<string, unknown> = {}) {
  capturedCallbacks = {};
  mockUseCompany.mockImplementation((cb: Record<string, any>) => {
    capturedCallbacks = cb;
    return { ...baseData, ...overrides };
  });
  return render(<CompanyScreen />);
}

/** Answers the most recent Alert by invoking the button with the given label. */
function answerAlert(label: string) {
  const calls = alertSpy.mock.calls;
  const call = calls[calls.length - 1];
  if (!call) throw new Error('Alert.alert was never called');
  const buttons = call[2] as { text: string; onPress?: () => void }[] | undefined;
  const button = buttons?.find((b) => b.text === label);
  if (!button) {
    throw new Error(
      'no button labelled ' + label + '; offered: ' + (buttons ?? []).map((b) => b.text).join(', '),
    );
  }
  button.onPress?.();
}

describe('CompanyScreen', () => {
  describe('who is allowed to see the roster', () => {
    it('shows the roster to an owner', () => {
      renderScreen({ isOwnerContext: true });
      expect(screen.getByTestId('stats')).toBeTruthy();
      expect(screen.queryByTestId('redirect')).toBeNull();
    });

    it('redirects a worker context away instead of showing employees and their phones', () => {
      // The security-relevant case: a fired worker with a stale link, or someone who
      // followed a link meant for an owner, must not see the roster or the fuel.
      renderScreen({ isOwnerContext: false });

      expect(screen.getByTestId('redirect')).toHaveTextContent(/\/my-codes/);
      expect(screen.queryByTestId('stats')).toBeNull();
      expect(screen.queryByTestId('name-m1')).toBeNull();
    });

    it('redirects a personal context away too', () => {
      renderScreen({ isOwnerContext: false });
      expect(screen.getByTestId('redirect')).toHaveTextContent(/\/my-codes/);
    });

    it('waits for the load rather than redirecting while the context is still unknown', () => {
      // Redirecting during the load would bounce a legitimate owner out before the
      // context had resolved — the classic race on a slow connection.
      renderScreen({ isLoading: true, isOwnerContext: false });

      expect(screen.queryByTestId('redirect')).toBeNull();
      expect(screen.queryByTestId('stats')).toBeNull();
    });

    it('redirects a signed-out user to landing', () => {
      renderScreen({ isAuthenticated: false, authLoading: false });
      expect(screen.getByTestId('redirect')).toHaveTextContent(/\/landing/);
    });
  });

  describe('firing an employee', () => {
    it('confirms first, naming the employee', () => {
      renderScreen();
      fireEvent.press(screen.getByTestId('fire-m1'));

      expect(alertSpy).toHaveBeenCalledWith(
        'company.fire.confirmTitle',
        'company.fire.confirmDesc',
        expect.anything(),
      );
      expect(mockFire).not.toHaveBeenCalled();
    });

    it('fires the right employee once confirmed', () => {
      renderScreen();
      fireEvent.press(screen.getByTestId('fire-m1'));
      answerAlert('company.fire.confirm');

      expect(mockFire).toHaveBeenCalledWith('m1');
    });

    it('does nothing when cancelled', () => {
      renderScreen();
      fireEvent.press(screen.getByTestId('fire-m1'));
      answerAlert('common.cancel');

      expect(mockFire).not.toHaveBeenCalled();
    });
  });

  describe('recall and block', () => {
    it('confirms a recall before taking fuel back', () => {
      renderScreen();
      fireEvent.press(screen.getByTestId('recall-btn-g1'));

      expect(alertSpy).toHaveBeenCalledWith(
        'company.recall.confirmTitle',
        'company.recall.confirmDesc',
        expect.anything(),
      );
      expect(mockRecall).not.toHaveBeenCalled();

      answerAlert('company.recall.confirm');
      expect(mockRecall).toHaveBeenCalledWith('g1');
    });

    it('confirms a block even though it is reversible', () => {
      // Block stops the worker spending the voucher right now, which is reason enough
      // to ask. Unfreezing needs no prompt, so it must not add one.
      renderScreen();
      fireEvent.press(screen.getByTestId('block-btn-g1'));

      expect(mockBlock).not.toHaveBeenCalled();
      answerAlert('company.block.confirm');
      expect(mockBlock).toHaveBeenCalledWith('g1');
    });

    it('unfreezes without a prompt', () => {
      renderScreen();
      fireEvent.press(screen.getByTestId('unblock-b1'));

      expect(alertSpy).not.toHaveBeenCalled();
      expect(mockUnblock).toHaveBeenCalledWith('b1');
    });
  });

  describe('issuing fuel', () => {
    it('opens the sheet for the chosen employee, cleared of any previous selection', () => {
      // Otherwise the next employee inherits the previous one's selection and fuel
      // already spoken for gets issued again.
      renderScreen();
      fireEvent.press(screen.getByTestId('gift-m1'));

      expect(screen.getByTestId('modal')).toHaveTextContent(/open/);
      expect(screen.getByTestId('worker-label')).toHaveTextContent(/Іван Петров/);
      expect(screen.getByTestId('selected')).toHaveTextContent(/^$/);
    });

    it('selects every voucher, then clears them on a second press', () => {
      renderScreen();
      fireEvent.press(screen.getByTestId('gift-m1'));

      fireEvent.press(screen.getByTestId('select-all'));
      expect(screen.getByTestId('selected')).toHaveTextContent(/v1,v2/);

      // Tapping select-all again must clear, not re-select — the button's meaning
      // flips on whether everything is already picked.
      fireEvent.press(screen.getByTestId('select-all'));
      expect(screen.getByTestId('selected')).toHaveTextContent(/^$/);
    });

    it('sends exactly the selected vouchers', () => {
      renderScreen();
      fireEvent.press(screen.getByTestId('gift-m1'));
      fireEvent.press(screen.getByTestId('select-all'));
      fireEvent.press(screen.getByTestId('confirm-gift'));

      expect(mockGift).toHaveBeenCalledWith(['v1', 'v2']);
    });

    it('closes the sheet and clears the selection after a successful gift', () => {
      renderScreen();
      fireEvent.press(screen.getByTestId('gift-m1'));
      fireEvent.press(screen.getByTestId('select-all'));

      // The hook calls this after the issue succeeds.
      // act(): the callback drives screen state, and jest.setup.js turns an un-acted
      // update into a hard failure rather than a warning.
      act(() => capturedCallbacks.onGiftSuccess?.());

      expect(screen.getByTestId('modal')).toHaveTextContent(/closed/);
      expect(screen.getByTestId('selected')).toHaveTextContent(/^$/);
    });
  });

  describe('a worker with no name on file', () => {
    it('shows the phone number instead of a blank row', () => {
      // What a half-filled HR import looks like; a blank row is unreadable.
      renderScreen({
        members: [member('m2', { workerFirstName: null, workerLastName: null })],
      });

      expect(screen.getByTestId('name-m2')).toHaveTextContent(/\+380501112233/);
    });

    it('joins whichever name parts exist', () => {
      renderScreen({
        members: [member('m3', { workerLastName: null })],
      });

      expect(screen.getByTestId('name-m3')).toHaveTextContent(/^Іван$/);
    });
  });

  describe('inviting a worker', () => {
    it('resets the phone to the dialling prefix after a successful invite', () => {
      // Otherwise the next invite carries the previous invitee's number, which is a
      // real way to send a company's roster to a wrong number.
      //
      // Exact string, not a regex: `/\+380/` also matches the typed number, which is
      // what made an earlier version of this test pass whether or not the reset existed.
      renderScreen();
      expect(screen.getByTestId('phone')).toHaveTextContent('+380');

      fireEvent.press(screen.getByTestId('type-phone'));
      expect(screen.getByTestId('phone')).toHaveTextContent('+380501119988');

      act(() => capturedCallbacks.onInviteSuccess?.());

      expect(screen.getByTestId('phone')).toHaveTextContent('+380');
    });
  });
});
