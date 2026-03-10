# API Contracts: Passkey Authentication

**Feature**: 055-passkey-auth
**Date**: 2026-03-10

## Passkey Registration (Org Users — authenticated)

### POST /api/passkey/register/options

Generate WebAuthn registration challenge for the authenticated user.

**Auth**: Required (org user JWT)
**Rate Limit**: 10 per 24 hours per user

**Request Body**:
```json
{
  "displayName": "MacBook Pro Touch ID"
}
```

**Response 200**:
```json
{
  "transactionId": "uuid",
  "options": { /* Fido2NetLib CredentialCreateOptions JSON */ }
}
```

### POST /api/passkey/register/verify

Complete registration by verifying the attestation response.

**Auth**: Required (org user JWT)

**Request Body**:
```json
{
  "transactionId": "uuid",
  "attestationResponse": { /* AuthenticatorAttestationRawResponse JSON */ }
}
```

**Response 201**:
```json
{
  "credentialId": "uuid",
  "displayName": "MacBook Pro Touch ID",
  "createdAt": "2026-03-10T12:00:00Z"
}
```

**Response 400**: Invalid attestation, expired challenge, or max credentials reached.

---

## Passkey Registration (Public Users — unauthenticated)

### POST /api/auth/public/passkey/register/options

Generate registration challenge for a new public user.

**Auth**: None (public endpoint)
**Rate Limit**: 5 per minute per IP

**Request Body**:
```json
{
  "displayName": "Jane Doe",
  "email": "jane@example.com"
}
```

**Response 200**:
```json
{
  "transactionId": "uuid",
  "options": { /* CredentialCreateOptions JSON */ }
}
```

**Response 409**: Email already registered (direct to sign-in).

### POST /api/auth/public/passkey/register/verify

Complete public user registration.

**Auth**: None (public endpoint)

**Request Body**:
```json
{
  "transactionId": "uuid",
  "attestationResponse": { /* AuthenticatorAttestationRawResponse JSON */ }
}
```

**Response 201**:
```json
{
  "accessToken": "jwt...",
  "refreshToken": "jwt...",
  "expiresIn": 3600,
  "tokenType": "Bearer"
}
```

---

## Passkey Authentication

### POST /api/auth/passkey/assertion/options

Generate assertion challenge. Works for both discoverable and non-discoverable credentials.

**Auth**: None (public endpoint)
**Rate Limit**: 5 per minute per IP

**Request Body** (optional — empty body for discoverable flow):
```json
{
  "email": "jane@example.com"
}
```

**Response 200**:
```json
{
  "transactionId": "uuid",
  "options": { /* AssertionOptions JSON */ }
}
```

### POST /api/auth/passkey/assertion/verify

Verify the assertion and issue tokens.

**Auth**: None (public endpoint)

**Request Body**:
```json
{
  "transactionId": "uuid",
  "assertionResponse": { /* AuthenticatorAssertionRawResponse JSON */ }
}
```

**Response 200** (public user):
```json
{
  "accessToken": "jwt...",
  "refreshToken": "jwt...",
  "expiresIn": 3600,
  "tokenType": "Bearer"
}
```

**Response 200** (org user 2FA — passkey completes the 2FA step):
```json
{
  "accessToken": "jwt...",
  "refreshToken": "jwt...",
  "expiresIn": 3600,
  "tokenType": "Bearer"
}
```

**Response 400**: Invalid assertion, expired challenge.
**Response 401**: Credential not found or disabled.

---

## Org User 2FA with Passkey

### Modified: POST /api/auth/login (existing endpoint)

When an org user has passkey credentials registered (with or without TOTP), the response includes available 2FA methods.

**Modified Response** (when 2FA required):
```json
{
  "requiresTwoFactor": true,
  "loginToken": "short-lived-jwt",
  "availableMethods": ["totp", "passkey"]
}
```

### POST /api/auth/verify-passkey

Complete org user login using passkey as 2FA (parallel to existing `/api/auth/verify-2fa` for TOTP).

**Auth**: None (uses loginToken)

**Request Body**:
```json
{
  "loginToken": "short-lived-jwt",
  "assertionResponse": { /* AuthenticatorAssertionRawResponse JSON */ }
}
```

**Response 200**:
```json
{
  "accessToken": "jwt...",
  "refreshToken": "jwt...",
  "expiresIn": 3600,
  "tokenType": "Bearer"
}
```

---

## Passkey Credential Management

### GET /api/passkey/credentials

List current user's registered passkey credentials.

**Auth**: Required

**Response 200**:
```json
{
  "credentials": [
    {
      "id": "uuid",
      "displayName": "MacBook Pro Touch ID",
      "deviceType": "platform",
      "status": "Active",
      "createdAt": "2026-03-10T12:00:00Z",
      "lastUsedAt": "2026-03-10T14:00:00Z"
    }
  ],
  "maxCredentials": 10
}
```

### DELETE /api/passkey/credentials/{id}

Remove a passkey credential.

**Auth**: Required

**Response 204**: Credential revoked.
**Response 400**: Cannot remove last auth method.
**Response 404**: Credential not found.

---

## Public User Social Login

### POST /api/auth/public/social/initiate

Start social login flow for public user registration/sign-in.

**Auth**: None (public endpoint)

**Request Body**:
```json
{
  "provider": "Google",
  "redirectUri": "https://app.sorcha.dev/auth/callback"
}
```

**Response 200**:
```json
{
  "authorizationUrl": "https://accounts.google.com/o/oauth2/v2/auth?...",
  "state": "random-state-value"
}
```

### POST /api/auth/public/social/callback

Complete social login and issue tokens.

**Auth**: None (public endpoint)

**Request Body**:
```json
{
  "provider": "Google",
  "code": "authorization-code",
  "state": "random-state-value"
}
```

**Response 200**:
```json
{
  "accessToken": "jwt...",
  "refreshToken": "jwt...",
  "expiresIn": 3600,
  "tokenType": "Bearer",
  "isNewUser": true
}
```

---

## Public User Auth Method Management

### GET /api/auth/public/methods

List current public user's authentication methods.

**Auth**: Required (public user JWT)

**Response 200**:
```json
{
  "passkeys": [
    { "id": "uuid", "displayName": "...", "status": "Active", "createdAt": "..." }
  ],
  "socialLinks": [
    { "id": "uuid", "provider": "Google", "email": "jane@gmail.com", "createdAt": "..." }
  ]
}
```

### POST /api/auth/public/social/link

Link a social provider to an existing public user account.

**Auth**: Required (public user JWT)

**Request Body**:
```json
{
  "provider": "GitHub",
  "redirectUri": "https://app.sorcha.dev/auth/callback"
}
```

**Response 200**: Returns authorization URL (same as initiate).

### DELETE /api/auth/public/social/{linkId}

Remove a social login link.

**Auth**: Required (public user JWT)

**Response 204**: Link removed.
**Response 400**: Cannot remove last auth method.

---

## API Gateway Routes (YARP)

New routes to add to `appsettings.json`:

| Route ID | Path | Cluster | Auth |
|----------|------|---------|------|
| passkey-register-options | /api/passkey/register/options | tenant-cluster | Required |
| passkey-register-verify | /api/passkey/register/verify | tenant-cluster | Required |
| passkey-credentials | /api/passkey/credentials/{**catch-all} | tenant-cluster | Required |
| auth-public-passkey | /api/auth/public/passkey/{**catch-all} | tenant-cluster | Anonymous |
| auth-passkey-assertion | /api/auth/passkey/assertion/{**catch-all} | tenant-cluster | Anonymous |
| auth-verify-passkey | /api/auth/verify-passkey | tenant-cluster | Anonymous |
| auth-public-social | /api/auth/public/social/{**catch-all} | tenant-cluster | Anonymous |
| auth-public-methods | /api/auth/public/methods | tenant-cluster | Required |
