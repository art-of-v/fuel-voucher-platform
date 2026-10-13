import React from 'react';
import { Linking } from 'react-native';
import { render, screen, fireEvent, waitFor, act } from '@testing-library/react-native';

import FeedbackScreen from './feedback';

/**
 * The feedback explainer.
 *
 * Three things are easy to get wrong here and all three cost a customer their complaint:
 *
 * - **It must not deep-link straight from the tab.** A button that silently throws someone into a
 *   third-party messenger with no explanation is where "where did my message go" starts.
 * - **A 404 is "off", not "broken".** The tab is hidden on it; the screen is the fallback when
 *   the switch was flipped while it was open.
 * - **A network failure must not remove the channel.** It has to say so and keep the button -
 *   hiding the only way to report a problem because the phone was offline would be perverse.
 */

const mockGetFeedbackDeepLink = jest.fn();
const mockOpenTelegramChat = jest.fn();

jest.mock('../src/features/feedback/api/feedback', () => ({
  __esModule: true,
  getFeedbackDeepLink: (...a: unknown[]) => mockGetFeedbackDeepLink(...a),
  openTelegramChat: (...a: unknown[]) => mockOpenTelegramChat(...a),
}));

jest.mock('expo-router', () => ({
  __esModule: true,
  useFocusEffect: () => {},
  useRouter: () => ({ back: jest.fn() }),
}));

jest.mock('../src/core/i18n', () => ({
  __esModule: true,
  useI18n: () => ({
    t: (key: string) => key,
    language: 'uk',
    setLanguage: jest.fn(),
  }),
}));

jest.mock('../src/core/hooks/useTheme', () => {
  const { getTokens } = jest.requireActual<typeof import('../src/core/design/tokens')>(
    '../src/core/design/tokens',
  );
  return { useDesignTokens: () => getTokens() };
});

jest.mock('../src/core/ui', () => {
  const R = jest.requireActual<typeof import('react')>('react');
  const { Text: RNText, View } = jest.requireActual<typeof import('react-native')>('react-native');
  const Box = (testID: string, children: any) => R.createElement(View, { testID }, children);

  return {
    __esModule: true,
    PageLayout: ({ children }: any) => Box('page-layout', children),
    ScreenHeader: ({ title, subtitle }: any) =>
      R.createElement(
        View,
        null,
        R.createElement(RNText, { testID: 'screen-header' }, title),
        subtitle ? R.createElement(RNText, { testID: 'screen-subtitle' }, subtitle) : null,
      ),
    Card: ({ children }: any) => Box('card', children),
    Text: ({ children }: any) => R.createElement(RNText, null, children),
    Button: ({ label, onPress, testID }: any) =>
      R.createElement(RNText, { testID, onPress }, label),
    LoadingState: () => R.createElement(RNText, { testID: 'loading' }, 'loading'),
    ErrorState: ({ title, description, onRetry, secondaryAction }: any) =>
      R.createElement(
        View,
        null,
        R.createElement(RNText, { testID: 'error-state' }, title ?? description),
        R.createElement(RNText, { testID: 'error-retry', onPress: onRetry }, 'retry'),
        secondaryAction
          ? R.createElement(
              RNText,
              { testID: 'error-secondary', onPress: secondaryAction.onPress },
              secondaryAction.label,
            )
          : null,
      ),
  };
});

const DEEP_LINK = 'https://t.me/palne_feedback_bot?start=abc.def';

beforeEach(() => {
  jest.clearAllMocks();
  jest.spyOn(Linking, 'openURL').mockResolvedValue(undefined as never);
  mockGetFeedbackDeepLink.mockResolvedValue({ url: DEEP_LINK });
  mockOpenTelegramChat.mockResolvedValue(true);
});

describe('FeedbackScreen — the channel is offered', () => {
  it('explains the channel before handing over, instead of deep-linking straight away', async () => {
    render(<FeedbackScreen />);

    // The tab goes here first on purpose: a bare deep link gives the customer no idea what they
    // are about to open, or whether anyone will read it.
    expect(await screen.findByTestId('feedback-write-to-us')).toBeTruthy();
    expect(screen.getByText('feedback.whatTitle')).toBeTruthy();
    expect(screen.getByText('feedback.privateBody')).toBeTruthy();
    expect(mockOpenTelegramChat).not.toHaveBeenCalled();
  });

  it('opens the chat with the URL the server signed, not one it built', async () => {
    render(<FeedbackScreen />);
    const button = await screen.findByTestId('feedback-write-to-us');

    await act(async () => {
      fireEvent.press(button);
    });

    expect(mockOpenTelegramChat).toHaveBeenCalledWith(DEEP_LINK);
  });

  it('offers the web form as a way out rather than leaving a dead button', async () => {
    render(<FeedbackScreen />);

    const fallback = await screen.findByTestId('feedback-open-web-form');
    await act(async () => {
      fireEvent.press(fallback);
    });

    expect(Linking.openURL).toHaveBeenCalledWith('https://palne.shop/support');
  });

  it('explains itself when Telegram refuses to open, instead of appearing to have worked', async () => {
    mockOpenTelegramChat.mockResolvedValue(false);

    render(<FeedbackScreen />);
    // Resolved outside act(): the load lands first, and only then is there a button to press.
    const button = await screen.findByTestId('feedback-write-to-us');

    await act(async () => {
      fireEvent.press(button);
    });

    // Telegram is not installed on every phone. Silence here reads as "sent", and the customer
    // has just lost the complaint they were writing.
    expect(await screen.findByText('feedback.noTelegramTitle')).toBeTruthy();
  });
});

describe('FeedbackScreen — when it cannot offer the channel', () => {
  it('a 404 means the channel is switched off, and points at the web form', async () => {
    mockGetFeedbackDeepLink.mockResolvedValue(null);

    render(<FeedbackScreen />);

    await waitFor(() => expect(screen.getByTestId('error-state')).toBeTruthy());
    expect(screen.queryByTestId('feedback-write-to-us')).toBeNull();

    await act(async () => {
      fireEvent.press(screen.getByTestId('error-secondary'));
    });
    expect(Linking.openURL).toHaveBeenCalledWith('https://palne.shop/support');
  });

  it('a network failure keeps the button and says the load failed', async () => {
    // The opposite of "hide it": an offline phone must not remove the only route to reporting a
    // problem, because a broken payment is exactly when someone will be offline and angry.
    mockGetFeedbackDeepLink.mockRejectedValue(new Error('offline'));

    render(<FeedbackScreen />);

    await waitFor(() => expect(screen.getByTestId('error-state')).toBeTruthy());
    expect(screen.getByTestId('feedback-write-to-us')).toBeTruthy();
    expect(screen.queryByTestId('error-secondary')).toBeNull();
  });

  it('a retry re-asks and can still find the channel', async () => {
    mockGetFeedbackDeepLink
      .mockRejectedValueOnce(new Error('offline'))
      .mockResolvedValueOnce({ url: DEEP_LINK });

    render(<FeedbackScreen />);

    const retry = await screen.findByTestId('error-retry');
    await act(async () => {
      fireEvent.press(retry);
    });

    expect(await screen.findByTestId('feedback-write-to-us')).toBeTruthy();
  });
});