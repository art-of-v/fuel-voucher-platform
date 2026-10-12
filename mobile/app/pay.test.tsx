import React from 'react';
import { Alert } from 'react-native';
import { act, fireEvent, render, screen, waitFor } from '@testing-library/react-native';

import PaymentScreen from './pay';

/**
 * What this suite covers, and why these three cases and not "it renders".
 *
 * `/pay` is the screen the customer is looking at once their money is committed, on a page
 * this app does not control. A bug here does not look wrong - it looks like a payment that
 * silently did nothing. Three failures account for most of that:
 *
 *  - the frame never loads, leaving an empty screen with no way forward;
 *  - the post-payment redirect is followed INSIDE the WebView, so the app never hands off to
 *    the wallet and the customer is left on a dead page (the redirect is a `fuelflow://` deep
 *    link, which a WebView cannot follow);
 *  - the screen is entered with no URL at all, which must say so rather than render nothing.
 *
 * Deliberately NOT covered: whether Monobank's widget actually renders inside a WKWebView.
 * That needs a device, and it is the one thing in this screen no unit test can answer.
 */

const mockReplace = jest.fn();
const mockBack = jest.fn();
const mockOpenURL = jest.fn().mockResolvedValue(true);
/** Props of the single rendered WebView, so the handlers can be invoked directly. */
let webViewProps: Record<string, unknown> | null = null;

jest.mock('expo-router', () => ({
  useRouter: () => ({ replace: mockReplace, back: mockBack, push: jest.fn() }),
  useLocalSearchParams: () => mockParams,
}));

jest.mock('expo-constants', () => ({
  __esModule: true,
  default: { expoConfig: { scheme: 'fuelflow' } },
}));

jest.mock('expo-linking', () => ({ openURL: (...a: unknown[]) => mockOpenURL(...a) }));

jest.mock('react-native-webview', () => {
  const RN = require('react-native');
  return {
    WebView: (props: Record<string, unknown>) => {
      webViewProps = props;
      return <RN.View testID="payment-webview" />;
    },
  };
});

let mockParams: Record<string, string> = {};
const mockAlert = jest.spyOn(Alert, 'alert').mockImplementation(() => {});

beforeEach(() => {
  jest.clearAllMocks();
  webViewProps = null;
  mockParams = {};
});

describe('Payment screen', () => {
  it('renders the payment page for the url it was given', () => {
    mockParams = { url: 'https://pay.monobank.ua/frame/inv-1' };

    render(<PaymentScreen />);

    expect(screen.getByTestId('payment-webview')).toBeTruthy();
    expect(webViewProps?.source).toEqual({ uri: 'https://pay.monobank.ua/frame/inv-1' });
  });

  it('explains itself instead of rendering a dead screen when the url is missing', () => {
    render(<PaymentScreen />);

    // The failure this guards: /pay renders nothing, the customer has already committed money,
    // and there is no affordance to recover. Asserted on the testID rather than the wording so
    // a translation edit cannot turn this into a false failure - `t()` returns real copy, not
    // the key.
    expect(screen.getByTestId('payment-missing-url')).toBeTruthy();
    expect(screen.queryByTestId('payment-webview')).toBeNull();
    expect(mockReplace).not.toHaveBeenCalled();
  });

  it('hands off to the wallet on our own deep link instead of loading it in the frame', () => {
    // The post-payment redirect is `fuelflow://payment-result`. Loaded inside the WebView it
    // navigates the frame away and the customer never sees their order; blocked here it becomes
    // the app's normal hand-off to the wallet.
    mockParams = { url: 'https://pay.monobank.ua/frame/inv-1' };

    render(<PaymentScreen />);

    const handler = webViewProps?.onShouldStartLoadWithRequest as (r: { url: string }) => boolean;

    expect(handler({ url: 'fuelflow://payment-result' })).toBe(false);
    expect(mockReplace).toHaveBeenCalledWith('/my-codes');

    // An ordinary https navigation inside the payment flow must still be allowed through -
    // a guard that blocks everything would break the page it is supposed to protect.
    expect(handler({ url: 'https://pay.monobank.ua/3ds/challenge' })).toBe(true);
  });

  it('follows the frame request to open the Monobank app', () => {
    mockParams = { url: 'https://pay.monobank.ua/frame/inv-1' };

    render(<PaymentScreen />);

    const handler = webViewProps?.onMessage as (e: { nativeEvent: { data: string } }) => void;

    handler({
      nativeEvent: {
        data: JSON.stringify({ message: 'monopay-link', value: 'https://mbnk.app/or/x' }),
      },
    });
    expect(mockOpenURL).toHaveBeenCalledWith('https://mbnk.app/or/x');

    // The customer's own "back" inside the frame is a hand-off, not a no-op.
    handler({ nativeEvent: { data: JSON.stringify({ message: 'close-button' }) } });
    expect(mockReplace).toHaveBeenCalledWith('/my-codes');

    // Anything the frame posts that is not ours to act on must not crash or navigate.
    handler({ nativeEvent: { data: 'not json at all' } });
    expect(mockAlert).not.toHaveBeenCalled();
  });

  it('keeps the Monobank-app route open for an order resumed from the wallet', async () => {
    // `appUrl` is a route parameter and is NOT persisted with the order, so paying for an
    // existing unpaid order from «Мої паливні активи» arrives here without one. Rendering the
    // button only when `appUrl` existed took away the Monobank-app route for exactly that
    // customer - the common one, after an abandoned or failed checkout - leaving only the card
    // form inside this WebView and no 3DS hand-off of our own.
    mockParams = { url: 'https://pay.monobank.ua/frame/inv-resumed' };

    render(<PaymentScreen />);

    const button = screen.getByTestId('payment-open-monobank');
    fireEvent.press(button);

    // No appUrl to hand off to, so the existing fallback opens the page we are already showing.
    await waitFor(() =>
      expect(mockOpenURL).toHaveBeenCalledWith('https://pay.monobank.ua/frame/inv-resumed'),
    );
  });

  it('prefers the app deep link over the page when both are present', async () => {
    mockParams = {
      url: 'https://pay.monobank.ua/frame/inv-1',
      appUrl: 'https://mbnk.app/or/deep',
    };

    render(<PaymentScreen />);
    fireEvent.press(screen.getByTestId('payment-open-monobank'));

    // The direct checkout path carries a real Monobank-app deep link and must use it.
    await waitFor(() => expect(mockOpenURL).toHaveBeenCalledWith('https://mbnk.app/or/deep'));
  });

  it('covers the frame with a loading state until the acquirer page paints', () => {
    mockParams = { url: 'https://pay.monobank.ua/frame/inv-1' };

    render(<PaymentScreen />);

    // Monobank's page is light and this app is dark: an unpainted WebView is a white flash into a
    // payment. The loader covers that and must not resize the frame when it goes away.
    expect(screen.getByTestId('payment-loading')).toBeTruthy();

    act(() => {
      (webViewProps?.onLoadEnd as () => void)();
    });

    expect(screen.queryByTestId('payment-loading')).toBeNull();
  });

  it('brings the loading state back for an in-frame navigation like 3DS', () => {
    mockParams = { url: 'https://pay.monobank.ua/frame/inv-1' };

    render(<PaymentScreen />);
    act(() => {
      (webViewProps?.onLoadEnd as () => void)();
    });
    expect(screen.queryByTestId('payment-loading')).toBeNull();

    // 3DS replaces the document inside the frame, which blanks it exactly like the first load.
    act(() => {
      (webViewProps?.onLoadStart as () => void)();
    });
    expect(screen.getByTestId('payment-loading')).toBeTruthy();
  });

  it('shows what is being paid, in our own words, above a page we do not control', () => {
    mockParams = { url: 'https://pay.monobank.ua/frame/inv-1', amount: '1249.5' };

    render(<PaymentScreen />);

    expect(screen.getByTestId('payment-amount')).toBeTruthy();
  });

  it('omits the amount row rather than showing a broken one when checkout did not pass it', () => {
    // Re-opening an unpaid order from the wallet carries no amount: the row must disappear, not hang.
    mockParams = { url: 'https://pay.monobank.ua/frame/inv-resumed' };

    render(<PaymentScreen />);

    expect(screen.queryByTestId('payment-amount')).toBeNull();
  });

  it('ignores an amount that is not a number instead of printing NaN', () => {
    mockParams = { url: 'https://pay.monobank.ua/frame/inv-1', amount: 'not-a-number' };

    render(<PaymentScreen />);

    expect(screen.queryByTestId('payment-amount')).toBeNull();
  });

  it('offers a real recovery on failure instead of a muted sentence', () => {
    mockParams = { url: 'https://pay.monobank.ua/frame/inv-1' };

    render(<PaymentScreen />);
    // act: onHttpError swaps the whole subtree for the error state, which is a state update.
    act(() => {
      (webViewProps?.onHttpError as () => void)();
    });

    // The frame is gone; the customer needs the title, the reason and a way forward.
    expect(screen.getByTestId('payment-failed')).toBeTruthy();
    expect(screen.queryByTestId('payment-webview')).toBeNull();
  });
});
