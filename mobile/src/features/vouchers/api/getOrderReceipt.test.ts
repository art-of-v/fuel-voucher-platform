import { File } from 'expo-file-system';
import * as Sharing from 'expo-sharing';

import { apiFetch } from '../../../core/api/apiClient';
import { shareOrderReceipt } from './getOrderReceipt';

// The network boundary: apiFetch pulls in expo-constants, secure-store and device signing, none of
// which belong in a unit test.
jest.mock('../../../core/api/apiClient', () => ({
  apiFetch: jest.fn(),
}));

// The native boundary. jest-expo cannot load the real file-system or sharing modules, and the
// assertions below are about what bytes get written and shared, not about Expo's own behaviour.
jest.mock('expo-file-system', () => {
  const createNode = (uri = 'file:///cache/receipts/unknown.pdf') => ({
    uri,
    exists: false,
    create: jest.fn(),
    write: jest.fn(),
  });
  return {
    Paths: { cache: 'file:///cache/' },
    Directory: jest.fn().mockImplementation(() => createNode('file:///cache/receipts/')),
    // The real File takes (directory, name) and builds the uri from them, which is what the share
    // call depends on - so the mock has to reproduce that rather than hand back a bare object.
    File: jest
      .fn()
      .mockImplementation((dir: { uri?: string }, name: string) =>
        createNode(`${dir?.uri ?? 'file:///cache/receipts/'}${name}`),
      ),
  };
});
jest.mock('expo-sharing', () => ({
  shareAsync: jest.fn().mockResolvedValue(undefined),
}));

const mockedApiFetch = apiFetch as jest.MockedFunction<typeof apiFetch>;
const mockedShareAsync = Sharing.shareAsync as jest.MockedFunction<typeof Sharing.shareAsync>;

function pdfResponse(status = 200, body: ArrayBuffer | string = new ArrayBuffer(8)) {
  return {
    ok: status >= 200 && status < 300,
    status,
    arrayBuffer: jest.fn().mockResolvedValue(body),
    text: jest.fn().mockResolvedValue(typeof body === 'string' ? body : ''),
  } as unknown as Response;
}

describe('shareOrderReceipt', () => {
  beforeEach(() => jest.clearAllMocks());

  it('requests the customer-scoped endpoint for that order', async () => {
    mockedApiFetch.mockResolvedValue(pdfResponse());

    await shareOrderReceipt('order-123');

    expect(mockedApiFetch).toHaveBeenCalledWith('/api/purchases/order-123/receipt');
  });

  // The failure that matters: the endpoint answers a non-OK with a bare "Order not found" string,
  // so an unguarded JSON.parse turns a clear message into a SyntaxError the customer never sees
  // the cause of.
  it('throws the server message when the body is plain text', async () => {
    mockedApiFetch.mockResolvedValue(pdfResponse(404, 'Order not found'));

    await expect(shareOrderReceipt('order-123')).rejects.toThrow('Order not found');
    expect(mockedShareAsync).not.toHaveBeenCalled();
  });

  it('prefers the structured message from a JSON error body', async () => {
    mockedApiFetch.mockResolvedValue(
      pdfResponse(403, JSON.stringify({ code: 'forbidden', detail: 'Not your order' })),
    );

    await expect(shareOrderReceipt('order-123')).rejects.toThrow('Not your order');
  });

  it('does not share anything when the response is empty and unlabelled', async () => {
    mockedApiFetch.mockResolvedValue(pdfResponse(500, ''));

    await expect(shareOrderReceipt('order-123')).rejects.toThrow('500');
    expect(mockedShareAsync).not.toHaveBeenCalled();
  });

  it('writes the bytes and shares them as a PDF', async () => {
    const bytes = new Uint8Array([0x25, 0x50, 0x44, 0x46]); // %PDF
    mockedApiFetch.mockResolvedValue(pdfResponse(200, bytes.buffer));

    await shareOrderReceipt('order-123');

    const file = (File as unknown as jest.Mock).mock.results[0].value as { write: jest.Mock };
    expect(file.write).toHaveBeenCalledWith(expect.any(Uint8Array));
    expect(Array.from(file.write.mock.calls[0][0] as Uint8Array)).toEqual([0x25, 0x50, 0x44, 0x46]);

    expect(mockedShareAsync).toHaveBeenCalledWith(
      expect.stringContaining('order-123'),
      expect.objectContaining({ mimeType: 'application/pdf' }),
    );
  });

  // Re-downloading an order the customer already has a copy of is the common case, not a rare one;
  // the old create-then-delete dance turned it into a thrown error.
  it('overwrites rather than failing when the file already exists', async () => {
    mockedApiFetch.mockResolvedValue(pdfResponse());

    await shareOrderReceipt('order-123');

    const file = (File as unknown as jest.Mock).mock.results[0].value as {
      create: jest.Mock;
    };
    expect(file.create).toHaveBeenCalledWith({ overwrite: true });
  });
});
