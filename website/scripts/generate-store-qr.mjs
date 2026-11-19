/**
 * Renders the QR codes the site paints: the App Store code to
 * public/app-store-qr.svg, and the demo fuel code the phone mock shows to
 * public/voucher-qr.svg.
 *
 * Runs automatically before `dev` and `build` (see the predev/prebuild hooks),
 * so the scannable code can never drift from src/config/store.json. Committed
 * output keeps a fresh checkout working even if the hooks are bypassed.
 *
 * Error correction is M and the quiet zone is 2 modules: this QR is only ever
 * shown on screen, next to the very same button it encodes, so it does not need
 * the extra modules that H would cost.
 *
 * The demo code is a real QR, not a drawn square pattern: a mock that looks
 * like a broken code tells a visitor that the product is a mock. Its payload
 * is a plain, non-routable token, so scanning it shows text and never opens a
 * dead link.
 */
import { readFile, writeFile } from 'node:fs/promises';
import { dirname, resolve } from 'node:path';
import { fileURLToPath } from 'node:url';
import QRCode from 'qrcode';

const root = resolve(dirname(fileURLToPath(import.meta.url)), '..');

const { appStoreUrl, appStoreName } = JSON.parse(
  await readFile(resolve(root, 'src/config/store.json'), 'utf8'),
);

const svg = await QRCode.toString(appStoreUrl, {
  type: 'svg',
  errorCorrectionLevel: 'M',
  margin: 2,
  color: { dark: '#000000', light: '#ffffff' },
});

await writeFile(resolve(root, 'public/app-store-qr.svg'), svg, 'utf8');

const version = /viewBox="0 0 (\d+) /.exec(svg)?.[1];
console.log(
  `[qr] ${appStoreName} -> public/app-store-qr.svg (${version}x${version} modules)`,
);

const { voucherQrPayload } = JSON.parse(
  await readFile(resolve(root, 'src/config/qr.json'), 'utf8'),
);

const voucherSvg = await QRCode.toString(voucherQrPayload, {
  type: 'svg',
  errorCorrectionLevel: 'M',
  margin: 2,
  color: { dark: '#000000', light: '#ffffff' },
});

await writeFile(resolve(root, 'public/voucher-qr.svg'), voucherSvg, 'utf8');

const voucherVersion = /viewBox="0 0 (\d+) /.exec(voucherSvg)?.[1];
console.log(
  `[qr] demo fuel code -> public/voucher-qr.svg (${voucherVersion}x${voucherVersion} modules)`,
);