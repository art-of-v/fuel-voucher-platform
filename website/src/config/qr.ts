/**
 * The demo fuel code the phone mock paints.
 *
 * `scripts/generate-store-qr.mjs` renders a REAL QR for the payload in
 * src/config/qr.json into public/voucher-qr.svg at build time. A drawn square
 * pattern is not a small cosmetic detail: a visitor who scans the mock
 * expects it to resolve, and a fake pattern tells them the whole product is a
 * mock.
 *
 * The payload is a plain, non-routable token: scanning it shows the text and
 * never opens a dead link.
 */
import qr from './qr.json';

export const VOUCHER_QR_SRC = '/voucher-qr.svg';

export const VOUCHER_QR_PAYLOAD = qr.voucherQrPayload;
