# Public User Setup Guide

Configure public user authentication and guide users through self-registration on a Sorcha instance.

## For System Administrators

### Passkey Authentication Overview

Sorcha includes built-in passkey (WebAuthn/FIDO2) authentication for public users. No external identity provider is required. Users register with a passkey (biometric, security key, or platform authenticator) and receive a JWT token on successful authentication.

Public authentication endpoints are rate-limited to 5 attempts per minute per IP address to prevent brute-force attacks.

### Configuration

Set the following environment variables for your Sorcha Tenant Service deployment:

| Variable | Description | Example |
|----------|-------------|---------|
| `Passkey__Enabled` | Enable passkey authentication | `true` |
| `Passkey__DisplayName` | Platform name shown in passkey prompts | `Sorcha Platform` |
| `Passkey__RelyingPartyId` | Domain for credential scoping (must match your domain) | `sorcha.example.com` |
| `Passkey__Origin` | Allowed origin for WebAuthn ceremonies | `https://sorcha.example.com` |

Docker Compose example:

```yaml
services:
  tenant-service:
    environment:
      - Passkey__Enabled=true
      - Passkey__DisplayName=Sorcha Platform
      - Passkey__RelyingPartyId=sorcha.example.com
      - Passkey__Origin=https://sorcha.example.com
```

### Authentication Flow

```
┌──────────┐  1. Register Options  ┌──────────┐
│   User    │─────────────────────>│  Tenant   │
│  Browser  │  (email, display     │  Service  │
│           │   name)              │           │
│           │<─────────────────────│           │
│           │  Challenge + options │           │
│           │                      │           │
│           │  2. Browser WebAuthn │           │
│           │  ceremony (biometric │           │
│           │  or security key)    │           │
│           │                      │           │
│           │  3. Register Verify  │           │
│           │─────────────────────>│           │
│           │  (attestation)       │           │
│           │                      │           │
│           │<─────────────────────│           │
│           │  JWT Token (user     │           │
│           │  created + passkey   │           │
│           │  registered)         │           │
└──────────┘                      └──────────┘
```

### Public Auth API Endpoints

| Method | Path | Purpose |
|--------|------|---------|
| POST | `/api/auth/public/passkey/register/options` | Generate registration challenge for new user |
| POST | `/api/auth/public/passkey/register/verify` | Verify attestation and create user account |
| POST | `/api/auth/passkey/assertion/options` | Generate sign-in challenge |
| POST | `/api/auth/passkey/assertion/verify` | Verify assertion and issue JWT |

### Authenticated Passkey Management

Users who are already logged in can register additional passkeys:

| Method | Path | Purpose |
|--------|------|---------|
| POST | `/api/passkey/register/options` | Generate challenge for adding a passkey |
| POST | `/api/passkey/register/verify` | Verify and register additional passkey |
| GET | `/api/passkey/credentials` | List user's passkey credentials |
| DELETE | `/api/passkey/credentials/{id}` | Revoke a passkey credential |

> **Note:** A user cannot revoke their last authentication method. They must have TOTP 2FA or another passkey registered before revoking a credential.

### Self-Registration Policies

Control whether authenticated users can self-register as participants in organizations:

- Self-registration endpoint: `POST /api/me/organizations/{organizationId}/self-register`
- Requires the user to be authenticated (JWT Bearer token)
- Returns `409 Conflict` if the user is already a participant in the organization

Per-organization self-registration control can be managed by organization administrators through the organization settings.

---

## For End Users

### Creating an Account with a Passkey

Passkeys replace passwords with biometric authentication (fingerprint, face recognition) or hardware security keys. They are phishing-resistant and easier to use than passwords.

#### Step 1: Start registration

Navigate to your Sorcha instance login page (e.g., `https://sorcha.example.com/auth/login`) and click "Register with Passkey".

Or via API:

```bash
# Request registration options
curl -X POST https://sorcha.example.com/api/auth/public/passkey/register/options \
  -H "Content-Type: application/json" \
  -d '{
    "email": "alice@example.com",
    "displayName": "Alice Smith"
  }'

# Response:
# {
#   "transactionId": "a1b2c3d4-...",
#   "options": { ... WebAuthn credential creation options ... }
# }
```

#### Step 2: Complete the browser passkey ceremony

Your browser will prompt you to create a passkey using:
- **Biometric** -- fingerprint or face recognition on your device
- **Security key** -- a hardware FIDO2 key (e.g., YubiKey)
- **Platform authenticator** -- Windows Hello, macOS Touch ID, etc.

#### Step 3: Verify and receive token

The browser sends the attestation response back to the server:

```bash
curl -X POST https://sorcha.example.com/api/auth/public/passkey/register/verify \
  -H "Content-Type: application/json" \
  -d '{
    "transactionId": "a1b2c3d4-...",
    "attestationResponse": { ... from WebAuthn API ... }
  }'

# Response:
# {
#   "accessToken": "eyJ...",
#   "refreshToken": "...",
#   "userId": "b2c3d4e5-..."
# }
```

Your account is now created and you are signed in.

### Signing In with a Passkey

```bash
# Step 1: Request assertion options
curl -X POST https://sorcha.example.com/api/auth/passkey/assertion/options \
  -H "Content-Type: application/json" \
  -d '{"email": "alice@example.com"}'

# Step 2: Complete browser passkey ceremony (biometric/key verification)

# Step 3: Submit assertion response
curl -X POST https://sorcha.example.com/api/auth/passkey/assertion/verify \
  -H "Content-Type: application/json" \
  -d '{
    "transactionId": "...",
    "assertionResponse": { ... from WebAuthn API ... }
  }'

# Response: { "accessToken": "eyJ...", "refreshToken": "..." }
export TOKEN="eyJ..."
```

### Self-Registering as a Participant

After logging in, you can register yourself as a participant in an organization (if self-registration is enabled by the organization administrator).

```bash
curl -X POST https://sorcha.example.com/api/me/organizations/{organizationId}/self-register \
  -H "Authorization: Bearer $TOKEN" \
  -H "Content-Type: application/json" \
  -d '{"displayName": "Alice Smith"}'

# Response (201 Created):
# {
#   "id": "c3d4e5f6-...",
#   "displayName": "Alice Smith",
#   "status": "Active",
#   ...
# }
```

Or via the UI: navigate to **Profile** and select **Register as Participant**, then choose the organization.

### Viewing Your Participant Profiles

See all organizations where you are registered as a participant:

```bash
curl https://sorcha.example.com/api/me/participant-profiles \
  -H "Authorization: Bearer $TOKEN"

# Response:
# [
#   {
#     "id": "c3d4e5f6-...",
#     "organizationId": "a1b2c3d4-...",
#     "displayName": "Alice Smith",
#     "status": "Active",
#     "walletLinks": [...]
#   }
# ]
```

### Linking a Wallet

Link a cryptographic wallet address to your participant identity to sign transactions on registers.

#### Via the UI

1. Navigate to **Profile** then **Wallet Settings**
2. Click **Link Wallet Address**
3. Enter your wallet address and select the signing algorithm (ED25519, P-256, or RSA-4096)
4. Sign the challenge message displayed on screen with your wallet's private key
5. Paste the base64-encoded signature and public key
6. Click **Verify** -- your wallet is now linked

#### Via the API

```bash
# Step 1: Initiate wallet link
curl -X POST \
  https://sorcha.example.com/api/organizations/{orgId}/participants/{participantId}/wallet-links \
  -H "Authorization: Bearer $TOKEN" \
  -H "Content-Type: application/json" \
  -d '{
    "walletAddress": "ed25519:your-wallet-address",
    "algorithm": "ED25519"
  }'

# Response:
# {
#   "challengeId": "d4e5f6a7-...",
#   "challenge": "sorcha-link:1710000000:random-nonce",
#   "walletAddress": "ed25519:your-wallet-address",
#   "algorithm": "ED25519",
#   "expiresAt": "2026-03-10T12:05:00Z"
# }

# Step 2: Sign the challenge string with your wallet private key (locally)

# Step 3: Submit signature
curl -X POST \
  https://sorcha.example.com/api/organizations/{orgId}/participants/{participantId}/wallet-links/{challengeId}/verify \
  -H "Authorization: Bearer $TOKEN" \
  -H "Content-Type: application/json" \
  -d '{
    "signature": "base64-encoded-signature",
    "publicKey": "base64-encoded-public-key"
  }'

# Response: 200 OK
```

### Managing Your Passkeys

Add additional passkeys or revoke existing ones:

```bash
# List your passkeys
curl https://sorcha.example.com/api/passkey/credentials \
  -H "Authorization: Bearer $TOKEN"

# Revoke a passkey (must have another auth method)
curl -X DELETE https://sorcha.example.com/api/passkey/credentials/{credentialId} \
  -H "Authorization: Bearer $TOKEN"
```

### Troubleshooting

| Issue | Solution |
|-------|----------|
| Passkey not accepted | Ensure your browser supports WebAuthn: Chrome 67+, Firefox 60+, Safari 14+, Edge 79+ |
| "Email already in use" (409) | An account with this email already exists. Use the sign-in flow instead |
| Registration fails | Check if self-registration is enabled for the target organization |
| Wallet link challenge timeout | Challenges expire after 5 minutes. Initiate a new challenge and retry |
| Cannot revoke passkey | You must have at least one other authentication method (another passkey or TOTP 2FA) |
| Rate limit exceeded (429) | Public auth endpoints are limited to 5 attempts per minute. Wait and retry |
| Token expired (401) | Use the refresh token at `POST /api/auth/token/refresh` to get a new access token |

## Next Steps

- [Organization Integration Guide](organization-integration.md) -- for administrators setting up organizations
- [Authentication Setup](../guides/AUTHENTICATION-SETUP.md) -- detailed JWT and auth configuration
- [API Documentation](../reference/API-DOCUMENTATION.md) -- full API reference
