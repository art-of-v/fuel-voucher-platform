# Production-Grade Fintech Authentication & Device Binding Architecture

This document outlines a high-security, production-grade authentication system designed for mobile fintech applications, inspired by architectures used by Revolut and Monobank. It leverages cryptographic device binding, biometric-gated keys, challenge-response handshakes, and strict signature verification for every request.

## 1. System Requirements & Flows

### First App Launch Flow (Registration/Login)
1. User enters phone number.
2. Backend sends OTP via SMS.
3. User enters OTP.
4. Backend verifies OTP.
5. If phone exists → return existing user id/token for device registration.
6. If phone does not exist → create new user and return user id/token.

### Device Registration
During the first successful login, the React Native app must:
1. Generate a stable `device_id` (UUID).
2. Generate an asymmetric keypair (`public_key`, `private_key`) using hardware-backed keystores.
3. Store the `private_key` securely inside the Secure Enclave (iOS) or Keystore (Android).
4. Send a `POST /auth/register-device` request containing `phone`, `device_id`, `public_key`, `device_model`, `os_version`, and `app_version`.
5. Backend stores the device linking it to the user.

---

## 2. Database Schema (PostgreSQL)

```sql
-- Enable UUID extension
CREATE EXTENSION IF NOT EXISTS "uuid-ossp";

CREATE TABLE users (
    id UUID PRIMARY KEY DEFAULT uuid_generate_v4(),
    phone VARCHAR(20) UNIQUE NOT NULL,
    created_at TIMESTAMP WITH TIME ZONE DEFAULT NOW()
);

CREATE TABLE devices (
    id UUID PRIMARY KEY DEFAULT uuid_generate_v4(),
    user_id UUID REFERENCES users(id) ON DELETE CASCADE,
    device_id VARCHAR(255) UNIQUE NOT NULL,
    public_key TEXT NOT NULL,
    device_model VARCHAR(255),
    os_version VARCHAR(255),
    app_version VARCHAR(255),
    status VARCHAR(50) DEFAULT 'ACTIVE', -- ACTIVE, REVOKED
    created_at TIMESTAMP WITH TIME ZONE DEFAULT NOW(),
    last_seen TIMESTAMP WITH TIME ZONE DEFAULT NOW()
);

CREATE TABLE otp_codes (
    phone VARCHAR(20) PRIMARY KEY,
    code VARCHAR(10) NOT NULL,
    attempts INT DEFAULT 0,
    expires_at TIMESTAMP WITH TIME ZONE NOT NULL
);

CREATE TABLE sessions (
    id UUID PRIMARY KEY DEFAULT uuid_generate_v4(),
    user_id UUID REFERENCES users(id) ON DELETE CASCADE,
    device_id VARCHAR(255) REFERENCES devices(device_id) ON DELETE CASCADE,
    refresh_token TEXT UNIQUE NOT NULL,
    expires_at TIMESTAMP WITH TIME ZONE NOT NULL,
    created_at TIMESTAMP WITH TIME ZONE DEFAULT NOW()
);

-- Indexes for fast lookups
CREATE INDEX idx_devices_device_id ON devices(device_id);
CREATE INDEX idx_sessions_refresh_token ON sessions(refresh_token);
```

---

## 3. Redis Usage (Ephemeral State & Security)

Redis is essential for temporal state management and attack mitigation.

1. **Challenges**: `challenge:{device_id}` -> `<32_byte_random_string>` (TTL: 30s)
2. **Replay Cache**: `nonce:{signature_hash}` -> `1` (TTL: 30s) - *Prevents duplicating requests with the same signature.*
3. **Rate Limits**: `ratelimit:otp:{phone}` (TTL: 60s) - *Limits brute-forcing SMS gateways.*
4. **JWT Blacklist**: `blacklist:{jti}` -> `1` (TTL: matches token expiration) - *Prevents using revoked access tokens.*

---

## 4. Backend Implementation (Node.js/Express)

### Project Structure (Node.js)
```text
src/
├── controllers/
│   └── auth.controller.ts
├── middleware/
│   ├── auth.middleware.ts
│   ├── signature.middleware.ts
│   └── rate-limiter.middleware.ts
├── services/
│   ├── crypto.service.ts
│   ├── jwt.service.ts
│   └── otp.service.ts
├── routes/
│   └── auth.routes.ts
└── db/
    ├── postgres.ts
    └── redis.ts
```

### A. Crypto Service (`src/services/crypto.service.ts`)
```typescript
import crypto from 'crypto';

export class CryptoService {
  static generateChallenge(): string {
    return crypto.randomBytes(32).toString('hex');
  }

  static verifySignature(
    payload: string,
    signatureBase64: string,
    publicKeyPem: string
  ): boolean {
    try {
      const verify = crypto.createVerify('SHA256');
      verify.update(payload);
      verify.end();
      return verify.verify(publicKeyPem, signatureBase64, 'base64');
    } catch (err) {
      return false;
    }
  }
}
```

### B. Auth Controller - Challenge & Verify (`src/controllers/auth.controller.ts`)
```typescript
import { Request, Response } from 'express';
import { redisClient } from '../db/redis';
import { db } from '../db/postgres';
import { CryptoService } from '../services/crypto.service';
import { JwtService } from '../services/jwt.service';

export const requestChallenge = async (req: Request, res: Response) => {
  const { device_id } = req.body;
  if (!device_id) return res.status(400).json({ error: 'device_id required' });

  const device = await db.query('SELECT id FROM devices WHERE device_id = $1 AND status = $2', [device_id, 'ACTIVE']);
  if (!device) return res.status(404).json({ error: 'Device not registered or revoked' });

  const challenge = CryptoService.generateChallenge();
  
  // Store securely in Redis with 30s TTL
  await redisClient.setEx(`challenge:${device_id}`, 30, challenge);

  res.json({ challenge });
};

export const verifyChallenge = async (req: Request, res: Response) => {
  const { device_id, challenge, signature } = req.body;
  
  const storedChallenge = await redisClient.get(`challenge:${device_id}`);
  if (!storedChallenge || storedChallenge !== challenge) {
    return res.status(401).json({ error: 'Invalid or expired challenge' });
  }

  const device = await db.query('SELECT user_id, public_key FROM devices WHERE device_id = $1', [device_id]);
  
  const isValid = CryptoService.verifySignature(challenge, signature, device.public_key);
  if (!isValid) return res.status(401).json({ error: 'Invalid signature' });

  // Challenge used successfully, delete it to prevent reuse
  await redisClient.del(`challenge:${device_id}`);

  // Generate tokens
  const accessToken = JwtService.generateAccessToken(device.user_id, device_id);
  const refreshToken = JwtService.generateRefreshToken(device.user_id, device_id);

  res.json({ access_token: accessToken, refresh_token: refreshToken });
};
```

### C. Signature Verification Middleware (`src/middleware/signature.middleware.ts`)
*Applies to all protected endpoints.*

```typescript
import { Request, Response, NextFunction } from 'express';
import { redisClient } from '../db/redis';
import { db } from '../db/postgres';
import { CryptoService } from '../services/crypto.service';

export const verifyApiSignature = async (req: Request, res: Response, next: NextFunction) => {
  const deviceId = req.headers['x-device-id'] as string;
  const signature = req.headers['x-signature'] as string;
  const timestamp = parseInt(req.headers['x-timestamp'] as string, 10);

  if (!deviceId || !signature || !timestamp) {
    return res.status(401).json({ error: 'Missing security headers' });
  }

  // 1. Timestamp Freshness Check (30 seconds window strictly enforced)
  const now = Date.now();
  if (Math.abs(now - timestamp) > 30000) {
    return res.status(401).json({ error: 'Request expired. Sync device time.' });
  }

  // 2. Replay Attack Prevention (Check if signature was already used)
  const isReplay = await redisClient.set(
    `nonce:${signature.substring(0, 32)}`, '1', { NX: true, EX: 30 }
  );
  if (!isReplay) return res.status(401).json({ error: 'Replay attack detected' });

  // 3. Obtain Public Key (In production, use Redis caching here)
  const device = await db.query('SELECT public_key FROM devices WHERE device_id = $1 AND status = $2', [deviceId, 'ACTIVE']);
  if (!device) return res.status(401).json({ error: 'Device not found or revoked' });

  // 4. Construct Payload String exactly as the client did
  const method = req.method.toUpperCase();
  const path = req.originalUrl;
  const bodyString = Object.keys(req.body || {}).length ? JSON.stringify(req.body) : '';
  const payloadToSign = `${method}:${path}:${bodyString}:${timestamp}`;

  // 5. Verify Signature
  const isValid = CryptoService.verifySignature(payloadToSign, signature, device.public_key);
  if (!isValid) return res.status(401).json({ error: 'Invalid signature' });

  next();
};
```

---

## 5. Mobile Client Architecture (React Native)

### Project Structure (React Native)
```text
src/
├── api/
│   └── apiClient.ts
├── services/
│   ├── auth.service.ts
│   ├── crypto.service.ts
│   └── storage.service.ts
└── features/
    └── biometric/
```

### A. Crypto & Biometric Service (`src/services/crypto.service.ts`)
Using `react-native-biometrics` (or a similar Keystore wrapper) ensures the private key never touches Javascript memory.

```typescript
import ReactNativeBiometrics from 'react-native-biometrics';
import * as SecureStore from 'expo-secure-store';

const rnBiometrics = new ReactNativeBiometrics();

// Generates hardware-backed RSA 2048 keypair
export const registerSecureDevice = async () => {
  const { publicKey } = await rnBiometrics.createKeys();
  return publicKey;
};

// Prompts FaceID/TouchID to sign critical payloads (like Login Challenge)
export const signPayloadWithBiometrics = async (payload: string, prompt: string) => {
  const { success, signature } = await rnBiometrics.createSignature({
    promptMessage: prompt,
    payload: payload,
  });

  if (!success) throw new Error('Biometric authentication failed or cancelled');
  return signature;
};

// For standard API requests, an in-memory or SecureStore "soft" ECDSA key is used
// to provide continuous signature without prompting the user's face constantly.
export const signStandardPayload = async (payload: string) => {
    // Uses a separate soft key stored securely (implementation via robust crypto libs)
    // Return base64 signature
};
```

### B. Axios API Interceptor for Signed Requests (`src/api/apiClient.ts`)
```typescript
import axios from 'axios';
import { getDeviceId } from 'react-native-device-info';
import { signStandardPayload } from '../services/crypto.service';

const api = axios.create({ baseURL: 'https://api.yourbank.com' });

api.interceptors.request.use(async (config) => {
  const timestamp = Date.now().toString();
  const method = config.method?.toUpperCase() || 'GET';
  const path = config.url || '';
  const body = config.data ? JSON.stringify(config.data) : '';

  // Construct payload strictly matching backend logic
  const payloadToSign = `${method}:${path}:${body}:${timestamp}`;

  // Silent sign using the Soft Key stored in Secure Enclave
  const signature = await signStandardPayload(payloadToSign);
  const deviceId = await getDeviceId();

  config.headers['X-Device-ID'] = deviceId;
  config.headers['X-Signature'] = signature;
  config.headers['X-Timestamp'] = timestamp;

  return config;
});

// Response interceptor to handle 401s and token refreshes
api.interceptors.response.use(
  (response) => response,
  async (error) => {
      // Logic for catching 401, refreshing JWT using refresh token, and retrying request...
      return Promise.reject(error);
  }
);

export default api;
```

---

## 6. App Integrity & Advanced Protections

1. **App Attestation API Integration**:
   - **Android**: Implement Google Play Integrity API. During device registration, fetch an integrity token. The backend verifies it with Google servers, proving the app is not running in an emulator, is not rooted, and the binary matches the Play Store version.
   - **iOS**: Apple App Attest (DeviceCheck API) provides cryptographic proof that requests come from a legitimate, unmodified instance of the app.
   
2. **Man-in-the-Middle (MITM) Protection (SSL Pinning)**:
   - React Native apps must use SSL Pinning (e.g., `react-native-ssl-pinning`). The app refuses connection if the server's SSL certificate fingerprint does not strictly match the hardcoded values within the compiled binary, shutting down proxy tools like Charles or Wireshark.

3. **Rate Limiting & JWT Blacklist**:
   - Strict `express-rate-limit` combined with Redis caching limits OTP deliveries to 3 per minute.
   - When a user signs out, the JWT's `jti` is written to Redis and acts as a blacklist to prevent stolen token reuse before expiration.
