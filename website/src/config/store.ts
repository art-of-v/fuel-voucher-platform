import store from './store.json';

/**
 * Live App Store listing for the FuelFlow iPhone app.
 *
 * The same URL is the source for the download buttons, the scannable QR code
 * (rendered at build time by scripts/generate-store-qr.mjs) and the backend's
 * forced-update wall. Keep it in store.json so the Node script and the React
 * tree can never drift apart.
 */
export const APP_STORE_URL = store.appStoreUrl;
export const APP_STORE_NAME = store.appStoreName;

/** Pre-rendered by `npm run qr` into public/, so the QR stays crisp and needs no client JS. */
export const APP_STORE_QR_SRC = '/app-store-qr.svg';