/**
 * Renders the App Store QR code to public/app-store-qr.svg.
 *
 * Runs automatically before `dev` and `build` (see the predev/prebuild hooks),
 * so the scannable code can never drift from src/config/store.json. Committed
 * output keeps a fresh checkout working even if the hooks are bypassed.
 *
 * Error correction is M and the quiet zone is 2 modules: this QR is only ever
 * shown on screen, next to the very same button it encodes, so it does not need
 * the extra modules that H would cost.
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