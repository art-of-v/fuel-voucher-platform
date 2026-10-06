import React from 'react';
import { Modal } from 'react-native';
import { render, screen, fireEvent, waitFor } from '@testing-library/react-native';

import ContextsScreen from './contexts';
import { Haptics } from '../src/core/utils/haptics';

/**
 * The account-context switcher, which had no tests.
 *
 * This screen decides *whose* money and *whose* fuel the rest of the app is looking
 * at. Everything downstream — the basket, checkout, renewal, the map — reads the
 * context set here, so a bug is not cosmetic: the wrong company can be billed for a
 * purchase, or a manager can see owner tools they do not have.
 *
 * What it must get right:
 *
 * - the personal root is always present and is the active context when nothing else
 *   is selected
 * - a company the user *works for* is listed separately from one they *own*, and is
 *   never listed twice when the backend reports both memberships for the same id
 * - switching sets the context and returns to where the user came from, rather than
 *   pushing a new screen onto the stack
 * - creating a company needs a name and an EDRPOU, both non-blank after trimming, and
 *   sends trimmed values with the optional fields omitted rather than empty strings
 * - a failure to create surfaces a reason and leaves the sheet open so it can be
 *   corrected
 */

const mockSetContext = jest.fn();
const mockRefetch = jest.fn();
let mockCreateCompany: jest.Mock;
let mockOnCreated: (() => void) | null = null;
const mockShowToast = jest.fn();
const mockBack = jest.fn();
const mockImpact = jest.fn();

let mockCompanies: any[] = [];
let mockMemberships: any[] = [];
let mockIsLoading = false;
let mockHasError = false;
let mockIsCreating = false;
let mockCurrentId: string | null = null;
let mockIsAuthenticated = true;
let mockAuthLoading = false;

jest.mock('expo-router', () => {
  const R = jest.requireActual<typeof import('react')>('react');
  const { Text } = jest.requireActual<typeof import('react-native')>('react-native');
  return {
    __esModule: true,
    useRouter: () => ({ back: mockBack, push: jest.fn(), replace: jest.fn() }),
    Redirect: ({ href }: { href: string }) => R.createElement(Text, { testID: 'redirect' }, href),
  };
});

jest.mock('../src/core/i18n', () => ({
  __esModule: true,
  useI18n: () => ({ t: (key: string) => key, language: 'uk', setLanguage: jest.fn() }),
}));

jest.mock('../src/features/auth/hooks/useAuth', () => ({
  useAuth: () => ({ isAuthenticated: mockIsAuthenticated, isLoading: mockAuthLoading }),
}));

jest.mock('../src/core/state/appStore', () => ({
  // Selector form: the screen reads two separate slices of the store.
  useStore: (selector?: (s: any) => unknown) => {
    const state = {
      currentLegalEntityId: mockCurrentId,
      setCurrentContext: mockSetContext,
    };
    return selector ? selector(state) : state;
  },
}));

jest.mock('../src/core/feedback/toastStore', () => ({
  useToastStore: (selector?: (s: any) => unknown) => {
    const state = { show: mockShowToast };
    return selector ? selector(state) : state;
  },
}));

jest.mock('../src/core/api/apiClient', () => ({
  // Loading the real hook module walks into the native keychain and biometrics,
  // which throw under Jest. Only the error-key mapping is wanted from it.
  apiFetch: jest.fn(),
}));

jest.mock('../src/features/company/hooks/useLegalEntities', () => {
  // The real error-key mapping, so the toast assertion pins the actual reason.
  const actual = jest.requireActual<
    typeof import('../src/features/company/hooks/useLegalEntities')
  >('../src/features/company/hooks/useLegalEntities');
  return {
    ...actual,
    useLegalEntities: (opts: any) => {
      mockOnCreated = opts?.onCreated ?? null;
      return {
        companies: mockCompanies,
        isLoading: mockIsLoading,
        hasError: mockHasError,
        refetch: mockRefetch,
        isCreating: mockIsCreating,
        createCompany: async (...a: unknown[]) => {
          const result = await mockCreateCompany(...a);
          mockOnCreated?.();
          return result;
        },
      };
    },
  };
});

jest.mock('../src/features/company/hooks/useMemberships', () => ({
  useMemberships: () => ({ memberships: mockMemberships }),
}));

jest.mock('../src/core/utils/haptics', () => {
  const actual = jest.requireActual<typeof import('../src/core/utils/haptics')>(
    '../src/core/utils/haptics',
  );
  return {
    __esModule: true,
    ...actual,
    Haptics: { ...actual.Haptics, impactAsync: (...a: unknown[]) => mockImpact(...a) },
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
    User: icon('User'),
    Building2: icon('Building2'),
    Check: icon('Check'),
    Plus: icon('Plus'),
    X: icon('X'),
    AlertTriangle: icon('AlertTriangle'),
    Briefcase: icon('Briefcase'),
  };
});

/**
 * Whether the create-company sheet is open.
 *
 * Asserted on the Modal's own isible prop rather than on whether the fields are found:
 * 
eact-test-renderer keeps a Modal's children mounted whatever isible says, so
 * absence proves nothing — the prop is the thing the screen actually controls.
 */
const sheetIsOpen = (): boolean => (screen.UNSAFE_getByType(Modal) as any).props.visible;

function company(id: string, name: string) {
  return { id, name, edrpou: '1234567890', isActive: true, role: 'owner' };
}

function membership(legalEntityId: string, name: string, isOwner = false) {
  return { legalEntityId, name, isOwner, role: isOwner ? 'owner' : 'worker' };
}

beforeEach(() => {
  mockCreateCompany = jest.fn().mockResolvedValue({ id: 'c-2' });
  mockOnCreated = null;
  mockCompanies = [company('c-1', 'Romashka')];
  mockMemberships = [];
  mockIsLoading = false;
  mockHasError = false;
  mockIsCreating = false;
  mockCurrentId = null;
  mockIsAuthenticated = true;
  mockAuthLoading = false;
  mockSetContext.mockClear();
  mockRefetch.mockClear();
  mockShowToast.mockClear();
  mockBack.mockClear();
  mockImpact.mockClear();
});

describe('Contexts — who can get in', () => {
  it('sends a signed-out visitor to the landing screen', () => {
    mockIsAuthenticated = false;
    render(<ContextsScreen />);

    expect(screen.getByTestId('redirect')).toHaveTextContent('/landing');
  });

  it('waits rather than bouncing while the auth query is loading', () => {
    // Redirecting during the query would flash the landing screen at every
    // signed-in customer on a cold start.
    mockIsAuthenticated = false;
    mockAuthLoading = true;
    mockIsLoading = true;
    render(<ContextsScreen />);

    expect(screen.queryByTestId('redirect')).toBeNull();
    expect(screen.getByTestId('loading')).toBeTruthy();
  });

  it('shows a loading state while the companies are fetched', () => {
    mockIsLoading = true;
    render(<ContextsScreen />);

    expect(screen.getByTestId('loading')).toBeTruthy();
  });

  it('offers a retry when the companies fail to load', () => {
    mockHasError = true;
    render(<ContextsScreen />);

    // The personal row still renders, so the user is not stuck on an error page
    // with no way back to a working context.
    expect(screen.getByText('context.loadError')).toBeTruthy();
    fireEvent.press(screen.getByText('common.retry'));
    expect(mockRefetch).toHaveBeenCalled();
  });
});

describe('Contexts — the personal root', () => {
  it('is always listed', () => {
    mockCompanies = [];
    render(<ContextsScreen />);

    expect(screen.getByText('context.personal')).toBeTruthy();
  });

  it('is the active context when no company is selected', () => {
    mockCurrentId = null;
    render(<ContextsScreen />);

    expect(screen.getByText('CONTEXT.ACTIVE')).toBeTruthy();
  });

  it('is no longer active once a company is selected', () => {
    mockCurrentId = 'c-1';
    render(<ContextsScreen />);

    // Exactly one row may be current. Two would mean the personal root kept its
    // highlight while a company was selected, leaving the user unsure which
    // account the app is acting as.
    expect(screen.getAllByText('CONTEXT.ACTIVE')).toHaveLength(1);
  });

  it('switches back to personal and returns to where the user came from', () => {
    mockCurrentId = 'c-1';
    render(<ContextsScreen />);

    fireEvent.press(screen.getByText('context.personal'));

    expect(mockSetContext).toHaveBeenCalledWith(null);
    // `back`, not a push: this screen is reached from the profile and must not
    // leave itself behind the user.
    expect(mockBack).toHaveBeenCalled();
  });
});

describe('Contexts — owned companies', () => {
  it('lists an owned company', () => {
    mockCompanies = [company('c-1', 'Romashka')];
    render(<ContextsScreen />);

    expect(screen.getByText('Romashka')).toBeTruthy();
  });

  it('switches to the company that was pressed', () => {
    mockCompanies = [company('c-1', 'Romashka'), company('c-2', 'Svytloplyach')];
    render(<ContextsScreen />);

    fireEvent.press(screen.getByText('Svytloplyach'));

    expect(mockSetContext).toHaveBeenCalledWith('c-2');
    expect(mockImpact).toHaveBeenCalledWith(Haptics.ImpactFeedbackStyle.Medium);
  });
});

describe('Contexts — companies the user only works for', () => {
  it('lists a worker company separately from owned ones', () => {
    mockCompanies = [company('c-1', 'Romashka')];
    mockMemberships = [membership('c-9', 'Energoatom', false)];
    render(<ContextsScreen />);

    expect(screen.getByText('context.workerCompanies')).toBeTruthy();
    expect(screen.getByText('Energoatom')).toBeTruthy();
  });

  it('never lists the same company twice', () => {
    // The backend can report both an ownership and a membership for one id; showing
    // it twice gives the user two identical-looking rows for one company.
    mockCompanies = [company('c-1', 'Romashka')];
    mockMemberships = [membership('c-1', 'Romashka', true), membership('c-9', 'Energoatom', false)];
    render(<ContextsScreen />);

    expect(screen.getAllByText('Romashka')).toHaveLength(1);
    expect(screen.getAllByText('Energoatom')).toHaveLength(1);
  });

  it('shows no worker section when there are none', () => {
    mockMemberships = [];
    render(<ContextsScreen />);

    expect(screen.queryByText('context.workerCompanies')).toBeNull();
  });

  it('switches into a worker company, with no owner tools implied', () => {
    mockMemberships = [membership('c-9', 'Energoatom', false)];
    render(<ContextsScreen />);

    fireEvent.press(screen.getByText('Energoatom'));
    expect(mockSetContext).toHaveBeenCalledWith('c-9');
  });
});

describe('Contexts — creating a company', () => {
  /** Fills the two required fields; both are needed for the submit to enable. */
  function fillRequired(name = 'Nova', edrpou = '1234567890') {
    fireEvent.changeText(screen.getByTestId('field-context.create.name'), name);
    fireEvent.changeText(screen.getByTestId('field-context.create.edrpou'), edrpou);
  }

  it('opens the sheet from the add control', () => {
    render(<ContextsScreen />);

    expect(sheetIsOpen()).toBe(false);
    fireEvent.press(screen.getByText('context.addCompany'));
    expect(sheetIsOpen()).toBe(true);
  });

  it('refuses to submit without a name', () => {
    render(<ContextsScreen />);
    fireEvent.press(screen.getByText('context.addCompany'));

    fillRequired('', '1234567890');
    fireEvent.press(screen.getByText('context.create.submit'));

    // A company with no name would be created and then unusable everywhere else.
    expect(mockCreateCompany).not.toHaveBeenCalled();
  });

  it('refuses to submit without an EDRPOU', () => {
    render(<ContextsScreen />);
    fireEvent.press(screen.getByText('context.addCompany'));

    fillRequired('Nova', '');
    fireEvent.press(screen.getByText('context.create.submit'));

    // The EDRPOU is the tax id the invoice is issued against.
    expect(mockCreateCompany).not.toHaveBeenCalled();
  });

  it('refuses to submit on whitespace alone', () => {
    render(<ContextsScreen />);
    fireEvent.press(screen.getByText('context.addCompany'));

    fillRequired('   ', '   ');
    fireEvent.press(screen.getByText('context.create.submit'));

    expect(mockCreateCompany).not.toHaveBeenCalled();
  });

  it('sends trimmed values with the optional fields omitted', async () => {
    render(<ContextsScreen />);
    fireEvent.press(screen.getByText('context.addCompany'));

    fireEvent.changeText(screen.getByTestId('field-context.create.name'), '  Nova  ');
    fireEvent.changeText(screen.getByTestId('field-context.create.edrpou'), ' 1234567890 ');
    fireEvent.press(screen.getByText('context.create.submit'));

    await waitFor(() => expect(mockCreateCompany).toHaveBeenCalled());
    // Empty optional fields go as `undefined`, not `''` — the API treats an empty
    // string as a supplied-but-blank value.
    expect(mockCreateCompany).toHaveBeenCalledWith({
      name: 'Nova',
      edrpou: '1234567890',
      vatNumber: undefined,
      directorName: undefined,
      address: undefined,
    });
  });

  it('sends the optional fields that were filled in', async () => {
    render(<ContextsScreen />);
    fireEvent.press(screen.getByText('context.addCompany'));

    fillRequired('Nova', '1234567890');
    fireEvent.changeText(screen.getByTestId('field-context.create.vatNumber'), '9999999999');
    fireEvent.changeText(screen.getByTestId('field-context.create.address'), 'Kyiv');
    fireEvent.press(screen.getByText('context.create.submit'));

    await waitFor(() => expect(mockCreateCompany).toHaveBeenCalled());
    expect(mockCreateCompany).toHaveBeenCalledWith(
      expect.objectContaining({ vatNumber: '9999999999', address: 'Kyiv' }),
    );
  });

  it('keeps the sheet open and says why when the create fails', async () => {
    render(<ContextsScreen />);
    fireEvent.press(screen.getByText('context.addCompany'));

    fillRequired();
    mockCreateCompany.mockRejectedValue(new Error('duplicate'));
    fireEvent.press(screen.getByText('context.create.submit'));

    // Closing the sheet on failure would discard everything the user just typed.
    await waitFor(() => expect(mockShowToast).toHaveBeenCalled());
    expect(sheetIsOpen()).toBe(true);
    expect(mockShowToast.mock.calls[0][0].kind).toBe('danger');
  });

  it('closes the sheet and confirms when the create succeeds', async () => {
    render(<ContextsScreen />);
    fireEvent.press(screen.getByText('context.addCompany'));

    fillRequired();
    fireEvent.press(screen.getByText('context.create.submit'));

    await waitFor(() => expect(sheetIsOpen()).toBe(false));
    expect(mockShowToast.mock.calls[0][0].kind).toBe('success');
  });

  it('does not submit while a create is already in flight', async () => {
    render(<ContextsScreen />);
    fireEvent.press(screen.getByText('context.addCompany'));

    fillRequired();
    // Re-render with the flag raised: `submitCreate`'s own guard
    // (`if (!canSubmit || isCreating) return`) is what has to hold, and it can only
    // be read from a render that saw the flag.
    mockIsCreating = true;
    screen.rerender(<ContextsScreen />);

    // While creating, the label gives way to a spinner — so the button is addressed
    // by its own testID rather than by text that is no longer there.
    expect(screen.queryByText('context.create.submit')).toBeNull();
    fireEvent.press(screen.getByTestId('confirm-create'));

    // A second create with the same EDRPOU is a duplicate company.
    expect(mockCreateCompany).not.toHaveBeenCalled();
  });

  it('starts the next company from a blank form', async () => {
    render(<ContextsScreen />);
    fireEvent.press(screen.getByText('context.addCompany'));

    fillRequired('Nova', '1234567890');
    fireEvent.changeText(screen.getByTestId('field-context.create.address'), 'Kyiv');
    fireEvent.press(screen.getByText('context.create.submit'));
    await waitFor(() => expect(sheetIsOpen()).toBe(false));

    // Reopening must not greet the user with the company they just created: the
    // fields still hold its name and EDRPOU, so a second tap would submit it again.
    fireEvent.press(screen.getByText('context.addCompany'));
    expect(screen.getByTestId('field-context.create.name').props.value).toBe('');
    expect(screen.getByTestId('field-context.create.edrpou').props.value).toBe('');
    expect(screen.getByTestId('field-context.create.address').props.value).toBe('');
  });
});

describe('Contexts — dismissing the sheet', () => {
  it('closes without creating anything', () => {
    render(<ContextsScreen />);
    fireEvent.press(screen.getByText('context.addCompany'));
    fireEvent.changeText(screen.getByTestId('field-context.create.name'), 'Nova');

    fireEvent.press(screen.getByTestId('icon-X'));

    expect(sheetIsOpen()).toBe(false);
    expect(mockCreateCompany).not.toHaveBeenCalled();
  });
});
