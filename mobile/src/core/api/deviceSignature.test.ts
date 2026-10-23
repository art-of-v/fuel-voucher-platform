import {
  DEVICE_SIGNATURE_ALGORITHM,
  DEVICE_SIGNATURE_PAYLOAD_VERSION,
  buildChallengeSignaturePayload,
  buildRequestSignaturePayload,
} from './deviceSignature';

describe('deviceSignature', () => {
  it('registers the payload version and the algorithm the server pins', () => {
    expect(DEVICE_SIGNATURE_PAYLOAD_VERSION).toBe(2);
    expect(DEVICE_SIGNATURE_ALGORITHM).toBe('RsaPkcs1Sha256');
  });

  it('builds the challenge payload the server reconstructs', () => {
    expect(buildChallengeSignaturePayload('device-1', 'abc')).toBe(
      'FuelFlow-DeviceSignature-v2\nchallenge\ndevice-1\nabc',
    );
  });

  it('builds the request payload with the body and timestamp in a fixed order', () => {
    expect(
      buildRequestSignaturePayload('device-1', 'POST', '/api/purchases', '{"x":1}', '1700000000000'),
    ).toBe('FuelFlow-DeviceSignature-v2\nrequest\ndevice-1\nPOST\n/api/purchases\n1700000000000\n{"x":1}');
  });

  it('drops the query string, because the server signs the path only', () => {
    expect(
      buildRequestSignaturePayload('device-1', 'POST', '/api/purchases?ignored=1', '', '1700000000000'),
    ).toBe('FuelFlow-DeviceSignature-v2\nrequest\ndevice-1\nPOST\n/api/purchases\n1700000000000\n');
  });
});
