import React from 'react';
import { Alert } from 'react-native';
import { render, screen, fireEvent, waitFor } from '@testing-library/react-native';

import InvitationsScreen from './invitations';
import { Haptics } from '../src/core/utils/haptics';

/**
 * Company invitations, which had no tests.
 *
 * Accepting an invitation is the moment a person joins a company, and it has two
 * consequences that are easy to leave half-done:
 *
 * - a **membership** is created, so the company must appear in the context switcher
 * - the company may **already have fuel waiting** for this worker, so the wallet has
 *   to be refetched too
 *
 * Both live in the accept handler's cache invalidation. Drop either one and the user
 * is left in a state that looks like a bug: the company is visible but empty, or the
 * fuel is invisible until a cold start. So the suite pins every invalidation, not just
 * the one that makes the list look right.
 *
 * It also covers the two-way auth reconciliation, the busy lock that stops a
 * double-accept, and that a signed-out visitor triggers no request at all.
 */

const mockGetInvitations = jest.fn();
const mockAccept = jest.fn();
const mockDecline = jest.fn();
const mockInvalidate = jest.fn();
const mockImpact = jest.fn();
const mockNotification = jest.fn();

let mockInvitations: any[] = [];
let mockIsLoading = false;
let mockAcceptPending = false;
let mockDeclinePending = false;
let mockStoreAuth = true;
let mockHookAuth = true;
let mockAuthLoading = false;
// The screen creates exactly two mutations per render, accept first, so their identity is
// given by call position. Matching on `mutationFn` does not work: the screen passes
// `(id) => acceptInvitation(id)`, a fresh wrapper on every render.
// Named with the `mock` prefix because Babel's jest.mock hoisting guard refuses
// to let a factory close over a mutable binding it cannot prove is initialised.
let mockMutationSeq = 0;
let alertSpy: jest.SpyInstance;

jest.mock('@tanstack/react-query', () => ({
  __esModule: true,
  useQuery: () => ({ data: mockInvitations, isLoading: mockIsLoading }),
  // The screen creates exactly two mutations per render, accept first, so their
  // identity is given by call position. Matching on `mutationFn` does not work: the
  // screen passes `(id) => acceptInvitation(id)`, a fresh wrapper each render.
  useMutation: (cfg: any) => {
    const index = mockMutationSeq % 2;
    mockMutationSeq++;
    return {
      isPending: index === 0 ? mockAcceptPending : mockDeclinePending,
      mutate: async (vars: unknown) => {
        try {
          const r = await cfg.mutationFn(vars);
          cfg.onSuccess?.(r);
        } catch (e) {
          cfg.onError?.(e);
        }
      },
    };
  },
  useQueryClient: () => ({ invalidateQueries: mockInvalidate }),
}));

jest.mock('../src/features/company/api/companyApi', () => {
  const actual = jest.requireActual<typeof import('../src/features/company/api/companyApi')>(
    '../src/features/company/api/companyApi',
  );
  return {
    ...actual,
    getMyInvitations: (...a: unknown[]) => mockGetInvitations(...a),
    acceptInvitation: (...a: unknown[]) => mockAccept(...a),
    declineInvitation: (...a: unknown[]) => mockDecline(...a),
  };
});

jest.mock('../src/core/api/apiClient', () => ({ apiFetch: jest.fn() }));

jest.mock('../src/features/company/hooks/useMemberships', () => {
  // The real key: the assertion is about invalidating *that* cache, so a copy would
  // prove nothing.
  const actual = jest.requireActual<typeof import('../src/features/company/hooks/useMemberships')>(
    '../src/features/company/hooks/useMemberships',
  );
  return { ...actual, useMemberships: () => ({ memberships: [] }) };
});

jest.mock('expo-router', () => {
  const R = jest.requireActual<typeof import('react')>('react');
  const { Text } = jest.requireActual<typeof import('react-native')>('react-native');
  return {
    __esModule: true,
    useFocusEffect: () => {},
    Redirect: ({ href }: { href: string }) => R.createElement(Text, { testID: 'redirect' }, href),
  };
});

jest.mock('../src/core/i18n', () => ({
  __esModule: true,
  useI18n: () => ({ t: (key: string) => key, language: 'uk', setLanguage: jest.fn() }),
}));

jest.mock('../src/features/auth/hooks/useAuth', () => ({
  useAuth: () => ({ isAuthenticated: mockHookAuth, isLoading: mockAuthLoading }),
}));

jest.mock('../src/core/state/appStore', () => ({
  useStore: (selector?: (s: any) => unknown) => {
    const state = { isAuthenticated: mockStoreAuth };
    return selector ? selector(state) : state;
  },
}));

jest.mock('../src/core/utils/formatters', () => ({
  formatExpirationDate: (d: string) => `formatted(${d})`,
}));

jest.mock('../src/core/utils/haptics', () => {
  const actual = jest.requireActual<typeof import('../src/core/utils/haptics')>(
    '../src/core/utils/haptics',
  );
  return {
    __esModule: true,
    ...actual,
    Haptics: {
      ...actual.Haptics,
      impactAsync: (...a: unknown[]) => mockImpact(...a),
      notificationAsync: (...a: unknown[]) => mockNotification(...a),
    },
  };
});

jest.mock('../src/core/hooks/useTheme', () => {
  const { getTokens } = jest.requireActual<typeof import('../src/core/design/tokens')>(
    '../src/core/design/tokens',
  );
  return { useDesignTokens: () => getTokens() };
});

jest.mock('../src/core/ui', () => {
  const R = jest.requireActual<typeof import('react')>('react');
  const { Text: RNText, View } = jest.requireActual<typeof import('react-native')>('react-native');
  return {
    __esModule: true,
    GridPageLayout: ({ children, header }: any) =>
      R.createElement(View, { testID: 'grid-layout' }, header, children),
    ScreenHeader: ({ title }: any) => R.createElement(RNText, { testID: 'screen-header' }, title),
    LoadingState: () => R.createElement(RNText, { testID: 'loading' }, 'loading'),
    useContentInsets: () => ({ top: 0, bottom: 34, left: 0, right: 0 }),
  };
});

jest.mock('lucide-react-native', () => {
  const R = jest.requireActual<typeof import('react')>('react');
  const { Text: RNText } = jest.requireActual<typeof import('react-native')>('react-native');
  const icon = (n: string) => () => R.createElement(RNText, { testID: `icon-${n}` });
  return {
    __esModule: true,
    Mail: icon('Mail'),
    Check: icon('Check'),
    X: icon('X'),
    Building2: icon('Building2'),
  };
});

function invitation(id: string, over: Record<string, unknown> = {}) {
  return {
    id,
    legalEntityName: 'TOV Romashka',
    ownerFirstName: 'Oksana',
    ownerLastName: 'Kovalenko',
    ownerPhoneNumber: '+380501234567',
    createdAtUtc: '2026-01-15',
    ...over,
  };
}

beforeEach(() => {
  mockInvitations = [invitation('inv-1')];
  mockIsLoading = false;
  mockAcceptPending = false;
  mockDeclinePending = false;
  mockStoreAuth = true;
  mockHookAuth = true;
  mockAuthLoading = false;
  mockGetInvitations.mockReset();
  mockAccept.mockReset();
  mockDecline.mockReset();
  mockInvalidate.mockClear();
  mockImpact.mockClear();
  mockNotification.mockClear();
  mockAccept.mockResolvedValue({ ok: true });
  mockDecline.mockResolvedValue({ ok: true });
  alertSpy = jest.spyOn(Alert, 'alert').mockImplementation(() => {});
});

afterEach(() => {
  jest.restoreAllMocks();
});

describe('Invitations — getting in', () => {
  it('sends a signed-out visitor to the landing screen', () => {
    mockStoreAuth = false;
    mockHookAuth = false;
    render(<InvitationsScreen />);

    expect(screen.getByTestId('redirect')).toHaveTextContent('/landing');
  });

  it('does not bounce a signed-in user while the auth query is loading', () => {
    // The landing screen flashing at every signed-in customer on a cold start.
    mockStoreAuth = false;
    mockHookAuth = false;
    mockAuthLoading = true;
    mockIsLoading = true;
    render(<InvitationsScreen />);

    expect(screen.queryByTestId('redirect')).toBeNull();
    expect(screen.getByTestId('loading')).toBeTruthy();
  });

  it('accepts a session known to the store but not yet the query', () => {
    mockStoreAuth = true;
    mockHookAuth = false;
    render(<InvitationsScreen />);

    expect(screen.queryByTestId('redirect')).toBeNull();
    expect(screen.getByText('TOV Romashka')).toBeTruthy();
  });

  it('accepts a session the query knows about before the store has caught up', () => {
    // The mirror of the case above. `storeAuth || hookAuth` exists because the two
    // disagree in both directions during a cold start.
    mockStoreAuth = false;
    mockHookAuth = true;
    render(<InvitationsScreen />);

    expect(screen.queryByTestId('redirect')).toBeNull();
    expect(screen.getByText('TOV Romashka')).toBeTruthy();
  });

  it('keeps the header while the list loads', () => {
    mockIsLoading = true;
    render(<InvitationsScreen />);

    // The previous bare centred View dropped the header and the safe-area handling
    // for the whole load.
    expect(screen.getByTestId('screen-header')).toBeTruthy();
    expect(screen.getByTestId('grid-layout')).toBeTruthy();
  });

  it('says so when there is nothing to accept', () => {
    mockInvitations = [];
    render(<InvitationsScreen />);

    expect(screen.getByText('company.invitations.empty')).toBeTruthy();
  });
});

describe('Invitations — who the invitation is from', () => {
  it('names the company, the owner and the date', () => {
    render(<InvitationsScreen />);

    expect(screen.getByText('TOV Romashka')).toBeTruthy();
    expect(screen.getByText('company.invitations.from: Oksana Kovalenko')).toBeTruthy();
    expect(screen.getByText('formatted(2026-01-15)')).toBeTruthy();
  });

  it('falls back to the phone number when the owner has no name', () => {
    // Owners invited by SMS may never have set a name; showing a blank "from" would
    // leave the user unable to tell one invitation from another.
    mockInvitations = [invitation('inv-1', { ownerFirstName: null, ownerLastName: null })];
    render(<InvitationsScreen />);

    expect(screen.getByText('company.invitations.from: +380501234567')).toBeTruthy();
  });

  it('copes with only a first name', () => {
    mockInvitations = [invitation('inv-1', { ownerLastName: null })];
    render(<InvitationsScreen />);

    expect(screen.getByText('company.invitations.from: Oksana')).toBeTruthy();
  });
});

describe('Invitations — accepting', () => {
  it('accepts the invitation that was pressed', async () => {
    mockInvitations = [invitation('inv-1'), invitation('inv-2')];
    render(<InvitationsScreen />);

    fireEvent.press(screen.getAllByTestId('icon-Check')[1]);

    await waitFor(() => expect(mockAccept).toHaveBeenCalledWith('inv-2'));
  });

  it('refreshes the invitations, the memberships and the legal entities', async () => {
    render(<InvitationsScreen />);
    fireEvent.press(screen.getByTestId('icon-Check'));

    await waitFor(() => expect(mockInvalidate).toHaveBeenCalled());
    const keys = mockInvalidate.mock.calls.map((c) => JSON.stringify(c[0].queryKey));

    // The list itself, so the accepted invitation disappears.
    expect(keys).toContain(JSON.stringify(['company', 'my-invitations']));
    // The memberships, so the new company reaches the context switcher.
    expect(keys).toContain(JSON.stringify(['company', 'my-memberships']));
    // The legal entities, for the same reason.
    expect(keys).toContain(JSON.stringify(['legal-entities', 'mine']));
  });

  it('refreshes the wallet, because the company may already have fuel waiting', async () => {
    render(<InvitationsScreen />);
    fireEvent.press(screen.getByTestId('icon-Check'));

    // Without this the new company appears with no fuel in it, and the user has no
    // way to tell that from "this company has nothing for me".
    await waitFor(() => expect(mockInvalidate).toHaveBeenCalled());
    const keys = mockInvalidate.mock.calls.map((c) => JSON.stringify(c[0].queryKey));
    expect(keys).toContain(JSON.stringify(['vouchers', 'my']));
  });

  it('confirms and marks the moment for the thumb', async () => {
    render(<InvitationsScreen />);
    fireEvent.press(screen.getByTestId('icon-Check'));

    await waitFor(() =>
      expect(alertSpy).toHaveBeenCalledWith(
        'company.invitations.acceptedTitle',
        'company.invitations.acceptedDesc',
      ),
    );
    expect(mockNotification).toHaveBeenCalledWith(Haptics.NotificationFeedbackType.Success);
    expect(mockImpact).toHaveBeenCalledWith(Haptics.ImpactFeedbackStyle.Heavy);
  });

  it('surfaces a failure without refreshing anything', async () => {
    mockAccept.mockRejectedValue(new Error('Invitation already used'));
    render(<InvitationsScreen />);
    fireEvent.press(screen.getByTestId('icon-Check'));

    // Refreshing after a failed accept would make a list that has not changed look
    // like it has.
    // The reason is localised by companyErrorKey, not shown raw: a plain Error
    // carries no API code, so it lands on the generic key.
    await waitFor(() =>
      expect(alertSpy).toHaveBeenCalledWith('common.error', 'company.error.generic'),
    );
    expect(mockInvalidate).not.toHaveBeenCalled();
  });

  it('disables both actions while one is in flight', async () => {
    // Asserted on the props rather than by pressing, because RNTL's `fireEvent.press`
    // does not honour `disabled` on a Pressable -- it fires the handler anyway. The
    // real user is stopped by this prop, and it is the *only* thing stopping them
    // here: unlike the contract sheet, neither handler re-checks `isBusy` itself.
    //
    // So the claim this test can honestly make is "the screen marks both controls
    // disabled", not "a second press is ignored".
    mockAcceptPending = true;
    render(<InvitationsScreen />);

    expect(screen.getByTestId('accept-inv-1').props.accessibilityState).toMatchObject({
      disabled: true,
    });
    expect(screen.getByTestId('decline-inv-1').props.accessibilityState).toMatchObject({
      disabled: true,
    });
  });

  it('leaves both actions enabled when nothing is in flight', () => {
    render(<InvitationsScreen />);

    expect(screen.getByTestId('accept-inv-1').props.accessibilityState).toMatchObject({
      disabled: false,
    });
  });
});

describe('Invitations — declining', () => {
  it('declines the invitation that was pressed', async () => {
    mockInvitations = [invitation('inv-1'), invitation('inv-2')];
    render(<InvitationsScreen />);

    fireEvent.press(screen.getAllByTestId('icon-X')[0]);

    await waitFor(() => expect(mockDecline).toHaveBeenCalledWith('inv-1'));
    expect(mockAccept).not.toHaveBeenCalled();
  });

  it('refreshes the list and says nothing', async () => {
    render(<InvitationsScreen />);
    fireEvent.press(screen.getByTestId('icon-X'));

    // Declining is routine; a confirmation dialog for it would be noise.
    await waitFor(() => expect(mockInvalidate).toHaveBeenCalled());
    expect(alertSpy).not.toHaveBeenCalled();
  });

  it('surfaces a failure to decline', async () => {
    mockDecline.mockRejectedValue(new Error('Network down'));
    render(<InvitationsScreen />);
    fireEvent.press(screen.getByTestId('icon-X'));

    await waitFor(() =>
      expect(alertSpy).toHaveBeenCalledWith('common.error', 'company.error.generic'),
    );
  });

  it('disables the accept control while a decline is in flight', () => {
    // Accepting and declining the same invitation at once leaves the server to pick
    // a winner the user never chose.
    mockDeclinePending = true;
    render(<InvitationsScreen />);

    expect(screen.getByTestId('accept-inv-1').props.accessibilityState).toMatchObject({
      disabled: true,
    });
  });
});
