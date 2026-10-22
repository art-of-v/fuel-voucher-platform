import { Directory, File, Paths } from 'expo-file-system';
import * as Sharing from 'expo-sharing';

import { apiFetch } from '../../../core/api/apiClient';

/**
 * Downloads the customer's own PDF receipt for an order and hands it to the system share sheet.
 *
 * <p>The share sheet rather than "save to files" on purpose: on Android it needs no storage
 * permission, and on iOS it is the only route that does not require a document-picker round trip.
 * The receipt is a record, not a working file - the customer sends it to themselves, their employer
 * or their accountant, which is exactly what the share sheet is for.
 *
 * <p>Kept in this feature's api/ folder rather than in apiClient because it is the only call in the
 * app that wants a binary body: everything else goes through apiRequest and is JSON.
 */
export async function shareOrderReceipt(orderId: string): Promise<void> {
  const response = await apiFetch(`/api/purchases/${orderId}/receipt`);

  if (!response.ok) {
    throw new Error(await readErrorText(response));
  }

  // arrayBuffer rather than blob(): React Native's Blob has no reliable stream API, and this is
  // the one shape the runtime exposes for arbitrary bytes.
  const buffer = await response.arrayBuffer();

  // Cache, not Documents: nothing here is app state the customer would miss if the OS reclaimed it.
  const directory = new Directory(Paths.cache, 'receipts');
  directory.create({ intermediates: true, overwrite: true });

  // overwrite rather than a delete-then-create: re-downloading an order the customer already has a
  // copy of is the common case here, not the rare one, and the older dance turned it into an error.
  const file = new File(directory, `receipt-${orderId}.pdf`);
  file.create({ overwrite: true });
  file.write(new Uint8Array(buffer));

  await Sharing.shareAsync(file.uri, {
    mimeType: 'application/pdf',
    UTI: 'com.adobe.pdf',
    dialogTitle: 'Receipt',
  });
}

/** Prefers the structured sentence the API returns, falling back to the raw text body. */
async function readErrorText(response: Response): Promise<string> {
  const text = await response.text();

  try {
    const body = JSON.parse(text);
    if (body && typeof body.detail === 'string' && body.detail) {
      return body.detail;
    }
    if (body && typeof body.title === 'string' && body.title) {
      return body.title;
    }
  } catch {
    // Not JSON. The endpoint answers a 404 with a bare "Order not found", so this is the normal
    // path for the most likely failure, not an exceptional one.
  }

  return text || `Request failed (${response.status})`;
}
