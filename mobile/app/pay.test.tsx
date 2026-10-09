import React from 'react';
import { Alert } from 'react-native';
import { render, screen } from '@testing-library/react-native';

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
});
