import React from 'react';
import { Alert } from 'react-native';
import { render, screen, fireEvent, waitFor } from '@testing-library/react-native';

import ContractsScreen from './contracts';
import { Haptics } from '../src/core/utils/haptics';

/**
 * Contract signing, which had no tests.
 *
 * Signing a contract here is a legal act with a station, and this screen is where a
 * mistake becomes expensive: the wrong contract signed, or a signature submitted
 * against no contract at all. Three things carry the risk:
 *
 * - a contract is **auto-selected only when exactly one is available**. With two or
 *   more the screen must force a choice, because silently picking the first would
 *   sign a document the user never read
 * - signing needs all three of station, contract and signature; anything missing is
 *   refused rather than half-submitted
 * - a failure surfaces the server's own message and keeps the sheet, so the
 *   signature is not lost and the user can retry
 *
 * There is also a safety net at the top: a user with no legal profile is sent to
 * complete it rather than left on a screen that cannot succeed.
 */

const mockInvalidate = jest.fn();
const mockSignContracts = jest.fn();
const mockGetLegalProfile = jest.fn();
const mockNotification = jest.fn();
const mockImpact = jest.fn();

let mockStations: any[] = [];
let mockContracts: any[] = [];
let mockSignedContracts: any[] = [];
let mockLoadingKeys = new Set<string>();
let mockIsPending = false;
let alertSpy: jest.SpyInstance;

jest.mock('@tanstack/react-query', () => ({
  __esModule: true,
  // Keyed by queryKey so each of the three fetches can be driven independently.
  useQuery: ({ queryKey }: any) => {
    const key = queryKey[0] as string;
    if (mockLoadingKeys.has(key)) return { data: undefined, isLoading: true };
    const data =
      key === 'stations' ? mockStations : key === 'contracts' ? mockContracts : mockSignedContracts;
    return { data, isLoading: false };
  },
  useMutation: (cfg: any) => ({
    isPending: mockIsPending,
    mutate: async (vars: unknown) => {
      try {
        const result = await cfg.mutationFn(vars);
        cfg.onSuccess?.(result);
      } catch (e) {
        cfg.onError?.(e);
      }
    },
  }),
  useQueryClient: () => ({ invalidateQueries: mockInvalidate }),
}));

jest.mock('../src/features/contracts/api/signContract', () => ({
  signContracts: (...a: unknown[]) => mockSignContracts(...a),
}));

jest.mock('../src/features/profile/api/updateLegalProfile', () => ({
  getLegalProfile: (...a: unknown[]) => mockGetLegalProfile(...a),
}));

// Both contract lists come from one module; the query functions are stubbed because
// `useQuery` is mocked above and never calls them.
jest.mock('../src/features/contracts/api/getContracts', () => ({
  __esModule: true,
  getAvailableContracts: jest.fn(),
  getSignedContracts: jest.fn(),
}));

jest.mock('../src/features/stations/api/getStations', () => ({
  __esModule: true,
  getStations: jest.fn(),
}));

jest.mock('../src/components/SignaturePad', () => {
  const R = jest.requireActual<typeof import('react')>('react');
  const { Pressable, Text } = jest.requireActual<typeof import('react-native')>('react-native');
  return {
    __esModule: true,
    // A stand-in that "draws" a fixed signature, so the suite can put a signature on
    // the pad without simulating gesture drawing.
    SignaturePad: ({ onCapture }: any) =>
      R.createElement(
        Pressable,
        { testID: 'signature-pad', onPress: () => onCapture('data:image/png;base64,SIG') },
        R.createElement(Text, null, 'pad'),
      ),
  };
});

jest.mock('expo-router', () => ({
  __esModule: true,
  useRouter: () => ({ replace: mockReplace, push: jest.fn(), back: jest.fn() }),
}));

const mockReplace = jest.fn();

jest.mock('../src/core/i18n', () => ({
  __esModule: true,
  useI18n: () => ({ t: (key: string) => key, language: 'uk', setLanguage: jest.fn() }),
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
  };
});

jest.mock('lucide-react-native', () => {
  const R = jest.requireActual<typeof import('react')>('react');
  const { Text: RNText } = jest.requireActual<typeof import('react-native')>('react-native');
  const icon = (n: string) => () => R.createElement(RNText, { testID: `icon-${n}` });
  return {
    __esModule: true,
    FileText: icon('FileText'),
    CheckCircle2: icon('CheckCircle2'),
    Eye: icon('Eye'),
    PenTool: icon('PenTool'),
    X: icon('X'),
    Landmark: icon('Landmark'),
  };
});

const station = (id: string, name: string) => ({ id, name, color: '#ff0000', logoText: name });
const contract = (id: string, title: string) => ({ id, title, content: `text of ${title}` });
const signed = (id: string, stationId: string, c = contract('c-1', 'Framework')) => ({
  id,
  station: { id: stationId, name: 'Shell' },
  contract: c,
  signedAt: '2026-01-01',
});

/** Signed contracts as an array, which is what the query returns when there are some. */
const noSigned = [] as any[];

beforeEach(() => {
  mockStations = [station('st-1', 'Shell'), station('st-2', 'OKKO')];
  mockContracts = [contract('c-1', 'Framework agreement')];
  mockSignedContracts = noSigned;
  mockLoadingKeys = new Set();
  mockIsPending = false;
  mockInvalidate.mockClear();
  mockSignContracts.mockReset();
  mockGetLegalProfile.mockReset();
  mockNotification.mockClear();
  mockImpact.mockClear();
  mockReplace.mockClear();
  mockSignContracts.mockResolvedValue({ ok: true });
  mockGetLegalProfile.mockResolvedValue({ id: 'le-1', name: 'Romashka' });
  alertSpy = jest.spyOn(Alert, 'alert').mockImplementation(() => {});
});

afterEach(() => {
  jest.restoreAllMocks();
});

describe('Contracts — the safety net', () => {
  it('sends a user with no legal profile off to complete it', async () => {
    mockGetLegalProfile.mockResolvedValue(null);
    render(<ContractsScreen />);

    await waitFor(() => expect(mockReplace).toHaveBeenCalledWith('/profile'));
    // Leaving them here would let them attempt a contract with nothing to sign as.
    expect(alertSpy).toHaveBeenCalledWith('contracts.needProfile', 'contracts.needProfileDesc');
  });

  it('leaves a user with a profile alone', async () => {
    render(<ContractsScreen />);

    await waitFor(() => expect(mockGetLegalProfile).toHaveBeenCalled());
    expect(mockReplace).not.toHaveBeenCalled();
  });

  it('does not send anyone away when the profile check itself fails', async () => {
    mockGetLegalProfile.mockRejectedValue(new Error('offline'));
    render(<ContractsScreen />);

    await waitFor(() => expect(mockGetLegalProfile).toHaveBeenCalled());
    // A network blip is not a missing profile; bouncing to /profile would strand a
    // perfectly valid company on a form they do not need to fill in.
    expect(mockReplace).not.toHaveBeenCalled();
  });

  it('shows a loading state while any of the three lists is in flight', () => {
    mockLoadingKeys = new Set(['signedContracts']);
    render(<ContractsScreen />);

    expect(screen.getByTestId('loading')).toBeTruthy();
  });
});

describe('Contracts — choosing a provider', () => {
  it('lists every station and marks the unsigned ones', () => {
    render(<ContractsScreen />);

    expect(screen.getByText('Shell')).toBeTruthy();
    expect(screen.getByText('OKKO')).toBeTruthy();
    expect(screen.getAllByText('contracts.needsSignature')).toHaveLength(2);
  });

  it('marks a station that is already signed and offers no second signature', () => {
    mockSignedContracts = [signed('sc-1', 'st-1')];
    render(<ContractsScreen />);

    expect(screen.getByText('contracts.signedStatus')).toBeTruthy();
    expect(screen.getAllByText('contracts.needsSignature')).toHaveLength(1);
    // Signing twice would create a duplicate agreement with the same station.
    expect(screen.getAllByTestId('icon-PenTool')).toHaveLength(1);
  });

  it('opens the signing sheet for the station that was pressed', () => {
    render(<ContractsScreen />);

    fireEvent.press(screen.getAllByTestId('icon-PenTool')[1]);

    // The sheet header and the summary both name the provider; the count is what proves
    // the sheet opened on the station that was pressed.
    expect(screen.getAllByText('contracts.provider: OKKO')).toHaveLength(2);
  });
});

describe('Contracts — which document gets signed', () => {
  it('auto-selects the contract when there is exactly one', () => {
    mockContracts = [contract('c-1', 'Framework agreement')];
    render(<ContractsScreen />);
    fireEvent.press(screen.getAllByTestId('icon-PenTool')[0]);

    // One option is not a choice; the sheet can go straight to it.
    expect(screen.getAllByText('Framework agreement').length).toBeGreaterThan(0);
    expect(screen.getByText('contracts.reviewText')).toBeTruthy();
  });

  it('forces a choice when several contracts are available', () => {
    mockContracts = [contract('c-1', 'Framework agreement'), contract('c-2', 'Service addendum')];
    render(<ContractsScreen />);
    fireEvent.press(screen.getAllByTestId('icon-PenTool')[0]);

    // Both are offered...
    expect(screen.getByText('Framework agreement')).toBeTruthy();
    expect(screen.getByText('Service addendum')).toBeTruthy();
  });

  it('will not sign anything until a contract is chosen', async () => {
    mockContracts = [contract('c-1', 'Framework agreement'), contract('c-2', 'Service addendum')];
    render(<ContractsScreen />);
    fireEvent.press(screen.getAllByTestId('icon-PenTool')[0]);
    fireEvent.press(screen.getByTestId('signature-pad'));

    // Signing with no contract chosen would submit the wrong document — or none.
    fireEvent.press(screen.getByText('contracts.signAndConfirm'));
    await waitFor(() => expect(alertSpy).toHaveBeenCalled());
    expect(mockSignContracts).not.toHaveBeenCalled();
  });

  it('signs the contract the user picked', async () => {
    mockContracts = [contract('c-1', 'Framework agreement'), contract('c-2', 'Service addendum')];
    render(<ContractsScreen />);
    fireEvent.press(screen.getAllByTestId('icon-PenTool')[0]);
    fireEvent.press(screen.getByText('Service addendum'));
    fireEvent.press(screen.getByTestId('signature-pad'));
    fireEvent.press(screen.getByText('contracts.signAndConfirm'));

    await waitFor(() =>
      expect(mockSignContracts).toHaveBeenCalledWith(['c-2'], 'data:image/png;base64,SIG', 'st-1'),
    );
  });

  it('opens a chosen contract for reading before it is signed', () => {
    render(<ContractsScreen />);
    fireEvent.press(screen.getAllByTestId('icon-PenTool')[0]);

    fireEvent.press(screen.getByText('contracts.readFull'));

    expect(screen.getByText('text of Framework agreement')).toBeTruthy();
  });
});

describe('Contracts — signing', () => {
  it('cannot be submitted without a signature', () => {
    render(<ContractsScreen />);
    fireEvent.press(screen.getAllByTestId('icon-PenTool')[0]);

    // The submit button is disabled outright, which is the real protection: a
    // contract sent with an empty signature is not a signature. `handleSign`'s own
    // check behind it is unreachable through the UI and therefore untested.
    expect(screen.getByTestId('sign-submit').props.accessibilityState.disabled).toBe(true);

    fireEvent.press(screen.getByTestId('signature-pad'));

    expect(screen.getByTestId('sign-submit').props.accessibilityState.disabled).toBe(false);
  });

  it('signs with the station, the contract and the signature', async () => {
    render(<ContractsScreen />);
    fireEvent.press(screen.getAllByTestId('icon-PenTool')[1]);
    fireEvent.press(screen.getByTestId('signature-pad'));
    fireEvent.press(screen.getByText('contracts.signAndConfirm'));

    // All three parts of the act, on the station that was actually chosen.
    await waitFor(() =>
      expect(mockSignContracts).toHaveBeenCalledWith(['c-1'], 'data:image/png;base64,SIG', 'st-2'),
    );
  });

  it('confirms, refreshes and shows the signed list afterwards', async () => {
    render(<ContractsScreen />);
    fireEvent.press(screen.getAllByTestId('icon-PenTool')[0]);
    fireEvent.press(screen.getByTestId('signature-pad'));
    fireEvent.press(screen.getByText('contracts.signAndConfirm'));

    await waitFor(() => expect(alertSpy).toHaveBeenCalled());
    expect(alertSpy).toHaveBeenCalledWith('contracts.success', 'contracts.signedSuccess');
    expect(mockInvalidate).toHaveBeenCalledWith({ queryKey: ['signedContracts'] });
    expect(mockNotification).toHaveBeenCalledWith(Haptics.NotificationFeedbackType.Success);
    // Landing on the signed tab shows the user the result of what they just did.
    expect(screen.getByText('contracts.noSignedContracts')).toBeTruthy();
  });

  it('surfaces the server message and keeps the sheet when signing fails', async () => {
    mockSignContracts.mockRejectedValue(new Error('Contract already signed'));
    render(<ContractsScreen />);
    fireEvent.press(screen.getAllByTestId('icon-PenTool')[0]);
    fireEvent.press(screen.getByTestId('signature-pad'));
    fireEvent.press(screen.getByText('contracts.signAndConfirm'));

    await waitFor(() =>
      expect(alertSpy).toHaveBeenCalledWith('contracts.error', 'Contract already signed'),
    );
    // The sheet stays open so the signature is not thrown away over a transient failure.
    expect(screen.getByTestId('signature-pad')).toBeTruthy();
  });

  it('falls back to a generic message when the failure carries none', async () => {
    mockSignContracts.mockRejectedValue({});
    render(<ContractsScreen />);
    fireEvent.press(screen.getAllByTestId('icon-PenTool')[0]);
    fireEvent.press(screen.getByTestId('signature-pad'));
    fireEvent.press(screen.getByText('contracts.signAndConfirm'));

    await waitFor(() =>
      expect(alertSpy).toHaveBeenCalledWith('contracts.error', 'contracts.signFailed'),
    );
  });

  it('shows that signing is under way instead of the button label', async () => {
    mockIsPending = true;
    render(<ContractsScreen />);
    fireEvent.press(screen.getAllByTestId('icon-PenTool')[0]);

    expect(screen.getByText('contracts.signing')).toBeTruthy();
    expect(screen.queryByText('contracts.signAndConfirm')).toBeNull();
  });
});

describe('Contracts — reading what is already signed', () => {
  it('says so when nothing is signed yet', () => {
    render(<ContractsScreen />);
    fireEvent.press(screen.getByText('contracts.signed'));

    expect(screen.getByText('contracts.noSignedContracts')).toBeTruthy();
  });

  it('lists signed contracts with their station and date', () => {
    mockSignedContracts = [signed('sc-1', 'st-1')];
    render(<ContractsScreen />);
    fireEvent.press(screen.getByText('contracts.signed'));

    expect(screen.getByText('Framework')).toBeTruthy();
    expect(screen.getByText('contracts.provider: Shell')).toBeTruthy();
    expect(screen.getByText('contracts.signedAt: formatted(2026-01-01)')).toBeTruthy();
  });

  it('opens a signed contract for reading', () => {
    mockSignedContracts = [signed('sc-1', 'st-1')];
    render(<ContractsScreen />);
    fireEvent.press(screen.getByText('contracts.signed'));

    fireEvent.press(screen.getByText('Framework'));

    expect(screen.getByText('text of Framework')).toBeTruthy();
  });

  it('falls back to the network name when the station is gone', () => {
    mockSignedContracts = [signed('sc-1', 'st-1')];
    mockSignedContracts[0].station = null;
    render(<ContractsScreen />);
    fireEvent.press(screen.getByText('contracts.signed'));

    // A signed contract whose station no longer resolves must still be attributable.
    expect(screen.getByText('contracts.provider: FuelFlow Network')).toBeTruthy();
  });

  it('does not carry a signature over into the next contract', async () => {
    mockSignContracts.mockResolvedValue({ ok: true });
    render(<ContractsScreen />);
    fireEvent.press(screen.getAllByTestId('icon-PenTool')[0]);
    fireEvent.press(screen.getByTestId('signature-pad'));
    fireEvent.press(screen.getByText('contracts.signAndConfirm'));

    await waitFor(() => expect(alertSpy).toHaveBeenCalled());

    // A successful signature lands the user on the signed tab, so the providers are
    // behind a tab switch.
    fireEvent.press(screen.getByText('contracts.available'));

    // Reopening the sheet must start unsigned. A signature left on the pad would let
    // the next contract be signed with a stroke the user never drew on it.
    fireEvent.press(screen.getAllByTestId('icon-PenTool')[1]);
    expect(screen.getByTestId('sign-submit').props.accessibilityState.disabled).toBe(true);
  });

  it('closes the reader', () => {
    mockSignedContracts = [signed('sc-1', 'st-1')];
    render(<ContractsScreen />);
    fireEvent.press(screen.getByText('contracts.signed'));
    fireEvent.press(screen.getByText('Framework'));

    fireEvent.press(screen.getByText('contracts.close'));

    expect(screen.queryByText('text of Framework')).toBeNull();
  });

  it('closes the reader from its close icon too', () => {
    mockSignedContracts = [signed('sc-1', 'st-1')];
    render(<ContractsScreen />);
    fireEvent.press(screen.getByText('contracts.signed'));
    fireEvent.press(screen.getByText('Framework'));

    // A reader that cannot be dismissed by the X would trap the user over a document
    // they only wanted to glance at.
    fireEvent.press(screen.getByTestId('close-reader-x'));

    expect(screen.queryByText('text of Framework')).toBeNull();
  });

  it('abandons the signing sheet from its close icon, discarding the signature', () => {
    mockContracts = [contract('c-1', 'Framework agreement'), contract('c-2', 'Service addendum')];
    render(<ContractsScreen />);
    fireEvent.press(screen.getAllByTestId('icon-PenTool')[0]);
    fireEvent.press(screen.getByText('Service addendum'));
    fireEvent.press(screen.getByTestId('signature-pad'));

    fireEvent.press(screen.getByTestId('close-signing-x'));

    // Reopening must be a clean slate: no chosen contract, no signature.
    fireEvent.press(screen.getAllByTestId('icon-PenTool')[1]);
    expect(screen.getByTestId('sign-submit').props.accessibilityState.disabled).toBe(true);
  });

  it('switches between the two tabs', () => {
    mockSignedContracts = [signed('sc-1', 'st-1')];
    render(<ContractsScreen />);

    fireEvent.press(screen.getByText('contracts.signed'));
    expect(screen.getByText('contracts.provider: Shell')).toBeTruthy();

    fireEvent.press(screen.getByText('contracts.available'));
    expect(screen.getByText('contracts.selectProvider')).toBeTruthy();
  });
});
