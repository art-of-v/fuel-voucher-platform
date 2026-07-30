import { BASE_URL } from './apiClient';

const CryptoSubVerify = async (
  challenge: string,
  signatureBase64: string,
  publicKeyBase64: string,
): Promise<boolean | null> => {
  if (typeof crypto === 'undefined' || !crypto.subtle) {
    console.warn('[Diag] crypto.subtle not available in this environment');
    return null;
  }

  try {
    const sigBytes = Uint8Array.from(atob(signatureBase64), c => c.charCodeAt(0));
    const challengeBytes = new TextEncoder().encode(challenge);

    const pemHeader = '-----BEGIN PUBLIC KEY-----\n';
    const pemFooter = '\n-----END PUBLIC KEY-----';
    const pem = `${pemHeader}${publicKeyBase64}${pemFooter}`;
    const b64 = pem
      .replace(/-----BEGIN PUBLIC KEY-----/, '')
      .replace(/-----END PUBLIC KEY-----/, '')
      .replace(/\s/g, '');
    const derBytes = Uint8Array.from(atob(b64), c => c.charCodeAt(0));

    const key = await crypto.subtle.importKey(
      'spki',
      derBytes,
      { name: 'RSASSA-PKCS1-v1_5', hash: 'SHA-256' },
      false,
      ['verify'],
    );

    const valid = await crypto.subtle.verify(
      'RSASSA-PKCS1-v1_5',
      key,
      sigBytes,
      challengeBytes,
    );

    console.warn(`[Diag] Client-side verify result: ${valid}`);
    return valid;
  } catch (err) {
    console.warn('[Diag] Client-side verify failed:', (err as Error).message);
    return null;
  }
};

async function directFetchVerify(
  deviceId: string,
  challenge: string,
  signature: string,
): Promise<string | null> {
  try {
    const response = await fetch(`${BASE_URL}/api/auth/device/verify`, {
      method: 'POST',
      headers: { 'Content-Type': 'application/json' },
      body: JSON.stringify({ deviceId, challenge, signature }),
    });
    const text = await response.text();
    console.warn(`[Diag] Direct fetch status: ${response.status}, body: ${text}`);
    return text;
  } catch (err) {
    console.warn('[Diag] Direct fetch failed:', (err as Error).message);
    return null;
  }
}

export async function diagnoseSigning(
  deviceId: string,
  challenge: string,
  signature: string,
  publicKey: string,
): Promise<void> {
  const sigPreview = signature.length > 32 ? `${signature.slice(0, 32)}...` : signature;
  const keyPreview = publicKey.length > 64 ? `${publicKey.slice(0, 64)}...` : publicKey;

  console.warn('========== SIGNATURE DIAGNOSTICS ==========');
  console.warn(`[Diag] deviceId: ${deviceId}`);
  console.warn(`[Diag] challenge: ${challenge}`);
  console.warn(`[Diag] challenge length: ${challenge.length}`);
  console.warn(`[Diag] signature (truncated): ${sigPreview}`);
  console.warn(`[Diag] signature length: ${signature.length}`);
  console.warn(`[Diag] publicKey starts with "-----": ${publicKey.startsWith('-----')}`);
  console.warn(`[Diag] publicKey (truncated): ${keyPreview}`);
  console.warn(`[Diag] publicKey length: ${publicKey.length}`);

  const clientResult = await CryptoSubVerify(challenge, signature, publicKey);
  if (clientResult === true) {
    console.warn('[Diag] ✓ Signature verified CLIENT-SIDE — mobile signing is correct');
  } else if (clientResult === false) {
    console.warn('[Diag] ✗ Signature FAILED client-side verification — mobile signing may be broken');
  } else {
    console.warn('[Diag] ? Client-side verification not available');
  }

  const directResult = await directFetchVerify(deviceId, challenge, signature);
  if (directResult) {
    const parsed = JSON.parse(directResult);
    if (parsed.isValid || parsed.accessToken) {
      console.warn('[Diag] ✓ Direct fetch VERIFY SUCCEEDED — apiClient may be interfering');
    } else {
      console.warn('[Diag] ✗ Direct fetch also returned error — issue is in the backend or signing');
    }
  }

  console.warn('===========================================');
}
