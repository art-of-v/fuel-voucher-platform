import { isFrameLoadableUrl, isOpenableUrl, schemeOf } from './linkPolicy';

/**
 * The rules the payment screen runs on, tested on their own.
 *
 * These are the sentences that decide what a page in the WebView is allowed to do to the
 * customer's phone, so they are pinned here rather than only where they are used: a change
 * to a scheme list is a security change, and it should have to argue with a test.
 */

describe('the scheme of a URL', () => {
  it('reads the scheme, lower-cased', () => {
    expect(schemeOf('https://pay.monobank.ua/frame/inv-1')).toBe('https');
    expect(schemeOf('HTTPS://pay.monobank.ua/frame/inv-1')).toBe('https');
    expect(schemeOf('intent://evil#Intent;component=com.evil/.Main;end')).toBe('intent');
    expect(schemeOf('sms:380501234567')).toBe('sms');
  });

  it('has no scheme to read when there is none', () => {
    expect(schemeOf('/relative/path')).toBeNull();
    expect(schemeOf('')).toBeNull();
    expect(schemeOf(undefined)).toBeNull();
  });
});

describe('a link the app may hand to the operating system', () => {
  it('is an HTTPS link', () => {
    // Monobank's own links are HTTPS universal links, so the hand-off to the Monobank app
    // is the one that has to keep working.
    expect(isOpenableUrl('https://mbnk.app/or/x')).toBe(true);
    expect(isOpenableUrl('https://pay.monobank.ua/frame/inv-1')).toBe(true);
  });

  it('is not an intent URI, a phone number, a message, or anything else', () => {
    // On Android an `intent://` URI is dispatched to an arbitrary exported activity; `tel:`
    // and `sms:` open other apps. A page in the frame picks these.
    expect(isOpenableUrl('intent://evil#Intent;component=com.evil/.Main;end')).toBe(false);
    expect(isOpenableUrl('tel:+380501234567')).toBe(false);
    expect(isOpenableUrl('sms:380501234567')).toBe(false);
    expect(isOpenableUrl('mailto:someone@example.com')).toBe(false);
    expect(isOpenableUrl('javascript:alert(1)')).toBe(false);
    expect(isOpenableUrl('data:text/html,<script>alert(1)</script>')).toBe(false);
    expect(isOpenableUrl('file:///etc/passwd')).toBe(false);
    expect(isOpenableUrl('http://evil.example/phish')).toBe(false);
  });

  it('is not a URL at all', () => {
    expect(isOpenableUrl(undefined)).toBe(false);
    expect(isOpenableUrl('')).toBe(false);
  });
});

describe('a page the frame may load', () => {
  it('is an HTTPS page, or the blank page the WebView itself uses', () => {
    expect(isFrameLoadableUrl('https://pay.monobank.ua/frame/inv-1')).toBe(true);
    expect(isFrameLoadableUrl('https://bank.gov.ua/3ds/challenge')).toBe(true);
    expect(isFrameLoadableUrl('about:blank')).toBe(true);
  });

  it('is never a plain-HTTP page', () => {
    // The frame is the customer's card form. A payment page that arrives unencrypted over a
    // network anyone can read is not a payment page.
    expect(isFrameLoadableUrl('http://pay.monobank.ua/frame/inv-1')).toBe(false);
  });

  it('is never a URL that is not a page', () => {
    expect(isFrameLoadableUrl('intent://evil#Intent;component=com.evil/.Main;end')).toBe(false);
    expect(isFrameLoadableUrl('tel:+380501234567')).toBe(false);
    expect(isFrameLoadableUrl('javascript:alert(1)')).toBe(false);
    expect(isFrameLoadableUrl('data:text/html,<script>alert(1)</script>')).toBe(false);
    expect(isFrameLoadableUrl(undefined)).toBe(false);
  });

  it('is not fooled by whitespace or by a scheme in the middle of the string', () => {
    expect(isFrameLoadableUrl('  https://pay.monobank.ua/frame/inv-1  ')).toBe(true);
    expect(isFrameLoadableUrl('java\tscript:alert(1)')).toBe(false);
    expect(isFrameLoadableUrl('https://pay.monobank.ua/javascript:alert(1)')).toBe(true);
  });
});
