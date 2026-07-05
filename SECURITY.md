# FuelFlow Security Guide

## Simple Guide

FuelFlow uses a **Magic Safe and a Secret Stamp** approach to security.

### The Magic Safe (Secure Enclave)
Inside your phone, there is a tiny, invisible safe. It is so strong that even the phone's owner can't peek inside. When you first join FuelFlow, your phone goes into this safe and creates two items:
1. **The Private Key**: This stays locked in the safe forever.
2. **The Public Key**: A lock given to the FuelFlow server.

### The Secret Question (The Challenge)
Every time you open the app, the server sends your phone a random riddle (a "Challenge"). The riddle is different every time.

### The Secret Stamp (Digital Signature)
To answer the riddle, your phone uses FaceID or Fingerprint to "stamp" the answer with your Private Key. This stamp is unique to your key and breaks if anyone tampers with the request.

### The Server's Check
The server verifies the stamp with your Public Key. If it fits, you're authenticated. If not, access is denied.

### Why is this better?
- **Stolen token is useless** — every request needs a fresh stamp from your physical phone.
- **No passwords to leak** — only public keys are stored server-side.

---

## Production-Grade Fintech Authentication & Device Binding Architecture

This section outlines a high-security authentication system for mobile fintech applications, using cryptographic device binding, biometric-gated keys, and challenge-response handshakes.

### Registration / Login Flow
1. User enters phone number.
2. Backend sends OTP via SMS.
3. User enters OTP.
4. Backend verifies OTP.
5. If phone exists — return existing user for device registration.
6. If new — create user and return token for device registration.

### Device Registration
On first login, the app must:
1. Generate a stable `device_id` (UUID).
2. Generate an asymmetric keypair using hardware-backed keystores.
3. Store `private_key` in Secure Enclave (iOS) / Keystore (Android).
4. Send `POST /auth/register-device` with `phone`, `device_id`, `public_key`, `device_model`, `os_version`, `app_version`.
5. Backend stores the device linked to the user.

### Database Schema

```sql
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
    status VARCHAR(50) DEFAULT 'ACTIVE',
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

CREATE INDEX idx_devices_device_id ON devices(device_id);
CREATE INDEX idx_sessions_refresh_token ON sessions(refresh_token);
```

### Redis Usage
- **Challenges**: `challenge:{device_id}` -> `<32_byte_random>` (TTL: 30s)
- **Replay Cache**: `nonce:{signature_hash}` -> `1` (TTL: 30s)
- **Rate Limits**: `ratelimit:otp:{phone}` (TTL: 60s)
- **JWT Blacklist**: `blacklist:{jti}` -> `1` (TTL: matches token expiration)

### App Integrity & Advanced Protections
1. **App Attestation**: Google Play Integrity (Android) / Apple App Attest (iOS).
2. **SSL Pinning**: App refuses connection if certificate fingerprint doesn't match.
3. **Rate Limiting**: Strict limits on OTP delivery (e.g., 3 per minute).
4. **JWT Blacklist**: Revoked tokens are blacklisted in Redis until expiration.
