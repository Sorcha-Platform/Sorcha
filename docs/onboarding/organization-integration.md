# Organization Integration Guide

Step-by-step guide for integrating your organization with a Sorcha distributed ledger instance.

## Prerequisites

- Sorcha instance URL (e.g., `https://sorcha.example.com`)
- SystemAdmin or Administrator credentials
- Organization details (name, subdomain)

## 1. Authenticate

Obtain a JWT access token by logging in with your administrator credentials.

```bash
# Get JWT token
curl -X POST https://sorcha.example.com/api/auth/login \
  -H "Content-Type: application/json" \
  -d '{"email": "admin@example.com", "password": "your-password"}'

# Response: { "accessToken": "eyJ...", "refreshToken": "..." }
export TOKEN="eyJ..."
```

C# equivalent:

```csharp
var client = new HttpClient { BaseAddress = new Uri("https://sorcha.example.com") };
var response = await client.PostAsJsonAsync("/api/auth/login", new
{
    email = "admin@example.com",
    password = "your-password"
});
var tokens = await response.Content.ReadFromJsonAsync<TokenResponse>();
client.DefaultRequestHeaders.Authorization =
    new AuthenticationHeaderValue("Bearer", tokens.AccessToken);
```

> **Note:** If your account has TOTP two-factor authentication enabled, the login response will include `requiresTwoFactor: true` and a `loginToken`. Complete login by calling `POST /api/auth/verify-2fa` with the `loginToken` and your TOTP code.

## 2. Create Organization

Create your organization. The authenticated user becomes the organization administrator.

```bash
curl -X POST https://sorcha.example.com/api/organizations \
  -H "Authorization: Bearer $TOKEN" \
  -H "Content-Type: application/json" \
  -d '{
    "name": "Acme Corporation",
    "subdomain": "acme"
  }'

# Response (201 Created):
# {
#   "id": "a1b2c3d4-e5f6-7890-abcd-ef1234567890",
#   "name": "Acme Corporation",
#   "subdomain": "acme",
#   "status": "Active",
#   "createdAt": "2026-03-10T12:00:00Z"
# }
```

C# equivalent:

```csharp
var orgResponse = await client.PostAsJsonAsync("/api/organizations", new
{
    name = "Acme Corporation",
    subdomain = "acme"
});
var org = await orgResponse.Content.ReadFromJsonAsync<OrganizationResponse>();
var orgId = org.Id;
```

You can optionally include branding configuration:

```json
{
  "name": "Acme Corporation",
  "subdomain": "acme",
  "branding": {
    "logoUrl": "https://acme.com/logo.png",
    "primaryColor": "#1a73e8",
    "secondaryColor": "#174ea6",
    "companyTagline": "Innovating the future"
  }
}
```

> **Tip:** Check subdomain availability before creation with `GET /api/organizations/validate-subdomain/{subdomain}`.

## 3. Add Users to Organization

Add users to your organization so they can be registered as participants.

```bash
curl -X POST https://sorcha.example.com/api/organizations/{orgId}/users \
  -H "Authorization: Bearer $TOKEN" \
  -H "Content-Type: application/json" \
  -d '{
    "email": "alice@acme.com",
    "displayName": "Alice Smith",
    "externalIdpSubject": "auth0|abc123",
    "roles": ["Member"]
  }'
```

C# equivalent:

```csharp
var userResponse = await client.PostAsJsonAsync(
    $"/api/organizations/{orgId}/users",
    new
    {
        email = "alice@acme.com",
        displayName = "Alice Smith",
        externalIdpSubject = "auth0|abc123",
        roles = new[] { "Member" }
    });
```

You can manage user lifecycle with these endpoints:

| Action | Method | Path |
|--------|--------|------|
| List users | GET | `/api/organizations/{orgId}/users` |
| Get user | GET | `/api/organizations/{orgId}/users/{userId}` |
| Update user | PUT | `/api/organizations/{orgId}/users/{userId}` |
| Remove user | DELETE | `/api/organizations/{orgId}/users/{userId}` |
| Suspend user | POST | `/api/organizations/{orgId}/users/{userId}/suspend` |
| Reactivate user | POST | `/api/organizations/{orgId}/users/{userId}/reactivate` |
| Unlock user | POST | `/api/organizations/{orgId}/users/{userId}/unlock` |
| Change role | PUT | `/api/organizations/{orgId}/users/{userId}/role` |

## 4. Register Participants

Register users as participants in the Sorcha identity system. Participants can sign transactions, appear in workflow actions, and link wallet addresses.

```
┌─────────┐  Create Participant  ┌──────────┐
│  Admin   │────────────────────>│  Tenant   │
│  Client  │                     │  Service  │
└─────────┘<─────────────────────└──────────┘
              Participant ID
```

```bash
curl -X POST https://sorcha.example.com/api/organizations/{orgId}/participants \
  -H "Authorization: Bearer $TOKEN" \
  -H "Content-Type: application/json" \
  -d '{
    "userId": "b2c3d4e5-f6a7-8901-bcde-f23456789012",
    "displayName": "Alice Smith"
  }'

# Response (201 Created):
# {
#   "id": "c3d4e5f6-a7b8-9012-cdef-345678901234",
#   "userId": "b2c3d4e5-...",
#   "displayName": "Alice Smith",
#   "status": "Active",
#   ...
# }
```

C# equivalent:

```csharp
var participantResponse = await client.PostAsJsonAsync(
    $"/api/organizations/{orgId}/participants",
    new
    {
        userId = userId,
        displayName = "Alice Smith"
    });
var participant = await participantResponse.Content
    .ReadFromJsonAsync<ParticipantDetailResponse>();
```

Additional participant management:

| Action | Method | Path |
|--------|--------|------|
| List participants | GET | `/api/organizations/{orgId}/participants` |
| Get participant | GET | `/api/organizations/{orgId}/participants/{id}` |
| Update participant | PUT | `/api/organizations/{orgId}/participants/{id}` |
| Deactivate | DELETE | `/api/organizations/{orgId}/participants/{id}` |
| Suspend | POST | `/api/organizations/{orgId}/participants/{id}/suspend` |
| Reactivate | POST | `/api/organizations/{orgId}/participants/{id}/reactivate` |

## 5. Wallet Linking

Link wallet addresses to participants via a cryptographic challenge/verify flow. This proves that the participant controls the private key for the wallet address.

```
┌──────────┐  1. Initiate     ┌──────────┐
│  Admin    │────────────────>│  Tenant   │
│  Client   │                 │  Service  │
└──────────┘                  └──────────┘
     │                             │
     │  2. Challenge (nonce,       │
     │     expiresAt)              │
     │<────────────────────────────│
     │                             │
     │  3. Sign nonce with         │
     │     wallet private key      │
     │  (client-side)              │
     │                             │
     │  4. Submit signature +      │
     │     public key              │
     │────────────────────────────>│
     │                             │
     │  5. Verified link           │
     │<────────────────────────────│
```

### Step 1: Initiate wallet link challenge

```bash
curl -X POST https://sorcha.example.com/api/organizations/{orgId}/participants/{id}/wallet-links \
  -H "Authorization: Bearer $TOKEN" \
  -H "Content-Type: application/json" \
  -d '{
    "walletAddress": "ed25519:abc123def456...",
    "algorithm": "ED25519"
  }'

# Response:
# {
#   "challengeId": "d4e5f6a7-b8c9-0123-defg-456789012345",
#   "challenge": "sorcha-link:1710000000:random-nonce-bytes",
#   "walletAddress": "ed25519:abc123def456...",
#   "algorithm": "ED25519",
#   "expiresAt": "2026-03-10T12:05:00Z",
#   "status": "Pending"
# }
```

Supported algorithms: `ED25519`, `P-256`, `RSA-4096`.

### Step 2: Sign the challenge (client-side)

Sign the challenge string with the wallet's private key. This is done locally, not sent to the server.

```csharp
// Using Sorcha.Cryptography
var signer = CryptoFactory.CreateSigner("ED25519", privateKeyBytes);
byte[] signature = signer.Sign(Encoding.UTF8.GetBytes(challenge));
string signatureBase64 = Convert.ToBase64String(signature);
string publicKeyBase64 = Convert.ToBase64String(publicKeyBytes);
```

### Step 3: Submit signed challenge

```bash
curl -X POST https://sorcha.example.com/api/organizations/{orgId}/participants/{id}/wallet-links/{challengeId}/verify \
  -H "Authorization: Bearer $TOKEN" \
  -H "Content-Type: application/json" \
  -d '{
    "signature": "base64-encoded-signature...",
    "publicKey": "base64-encoded-public-key..."
  }'

# Response: 200 OK (wallet linked)
```

> **Important:** Challenges expire after 5 minutes. Each participant can have up to 10 linked wallet addresses.

### Manage wallet links

| Action | Method | Path |
|--------|--------|------|
| List wallet links | GET | `/api/organizations/{orgId}/participants/{id}/wallet-links` |
| Revoke wallet link | DELETE | `/api/organizations/{orgId}/participants/{id}/wallet-links/{linkId}` |

## 6. Publish Participants to Register

Once participants have linked wallets, publish their identity records to a register. Published records allow other participants in the network to resolve identities and encryption keys.

```bash
curl -X POST https://sorcha.example.com/api/organizations/{orgId}/participants/publish \
  -H "Authorization: Bearer $TOKEN" \
  -H "Content-Type: application/json" \
  -d '{
    "registerId": "my-register-id",
    "participantName": "Alice Smith",
    "organizationName": "Acme Corporation",
    "addresses": [
      {
        "walletAddress": "ed25519:abc123def456...",
        "publicKey": "base64-encoded-public-key...",
        "algorithm": "ED25519",
        "primary": true
      }
    ],
    "signerWalletAddress": "ed25519:abc123def456..."
  }'
```

C# equivalent:

```csharp
var publishResponse = await client.PostAsJsonAsync(
    $"/api/organizations/{orgId}/participants/publish",
    new
    {
        registerId = "my-register-id",
        participantName = "Alice Smith",
        organizationName = "Acme Corporation",
        addresses = new[]
        {
            new
            {
                walletAddress = "ed25519:abc123def456...",
                publicKey = "base64-encoded-public-key...",
                algorithm = "ED25519",
                primary = true
            }
        },
        signerWalletAddress = "ed25519:abc123def456..."
    });
```

### Query published participants (via Register Service)

| Action | Method | Path |
|--------|--------|------|
| List published participants | GET | `/registers/{registerId}/participants` |
| Get by wallet address | GET | `/registers/{registerId}/participants/by-address/{walletAddress}` |
| Get by participant ID | GET | `/registers/{registerId}/participants/{participantId}` |
| Resolve public key | GET | `/registers/{registerId}/participants/by-address/{walletAddress}/public-key` |

### Update or revoke published records

```bash
# Update published participant
curl -X PUT https://sorcha.example.com/api/organizations/{orgId}/participants/publish/{participantId} \
  -H "Authorization: Bearer $TOKEN" \
  -H "Content-Type: application/json" \
  -d '{ ... updated fields ... }'

# Revoke published participant
curl -X DELETE https://sorcha.example.com/api/organizations/{orgId}/participants/publish/{participantId} \
  -H "Authorization: Bearer $TOKEN"
```

## 7. Service-to-Service Authentication

For automated integration from backend services, use the OAuth2 token endpoint with client credentials.

### Register a service principal (admin)

```bash
curl -X POST https://sorcha.example.com/api/service-principals \
  -H "Authorization: Bearer $TOKEN" \
  -H "Content-Type: application/json" \
  -d '{
    "name": "acme-backend",
    "scopes": ["registers:read", "blueprints:manage"]
  }'

# Response (201 Created) - credentials shown only once:
# {
#   "clientId": "svc-acme-backend",
#   "clientSecret": "generated-secret-value",
#   ...
# }
```

### Get a service token

```bash
curl -X POST https://sorcha.example.com/api/service-auth/token \
  -H "Content-Type: application/x-www-form-urlencoded" \
  -d 'grant_type=client_credentials&client_id=svc-acme-backend&client_secret=generated-secret-value'
```

C# equivalent:

```csharp
var tokenClient = new HttpClient
{
    BaseAddress = new Uri("https://sorcha.example.com")
};

var tokenResponse = await tokenClient.PostAsync("/api/service-auth/token",
    new FormUrlEncodedContent(new Dictionary<string, string>
    {
        ["grant_type"] = "client_credentials",
        ["client_id"] = "svc-acme-backend",
        ["client_secret"] = "generated-secret-value"
    }));

var serviceToken = await tokenResponse.Content.ReadFromJsonAsync<TokenResponse>();
```

The OAuth2 endpoint also supports `grant_type=password` and `grant_type=refresh_token`.

## 8. Event Subscriptions (SignalR)

Subscribe to real-time notifications for workflow actions, chat messages, and system events via SignalR hubs.

### Available hubs

| Hub | Path | Purpose |
|-----|------|---------|
| Actions | `/actionshub` | Workflow action notifications |
| Chat | `/hubs/chat` | Participant messaging |
| Events | `/hubs/events` | System events |

### Connect to the Actions hub

```csharp
var connection = new HubConnectionBuilder()
    .WithUrl("https://sorcha.example.com/actionshub", options =>
    {
        options.AccessTokenProvider = () => Task.FromResult(token);
    })
    .WithAutomaticReconnect()
    .Build();

connection.On<ActionNotification>("ActionCompleted", notification =>
{
    Console.WriteLine($"Action {notification.ActionId} completed on blueprint {notification.BlueprintId}");
});

await connection.StartAsync();
```

### Connect to the Events hub

```csharp
var eventsConnection = new HubConnectionBuilder()
    .WithUrl("https://sorcha.example.com/hubs/events", options =>
    {
        options.AccessTokenProvider = () => Task.FromResult(token);
    })
    .WithAutomaticReconnect()
    .Build();

eventsConnection.On<EventNotification>("EventReceived", notification =>
{
    Console.WriteLine($"Event: {notification.EventType} - {notification.Description}");
});

await eventsConnection.StartAsync();
```

## 9. End-to-End Example Script

Complete bash script that performs full organization onboarding:

```bash
#!/usr/bin/env bash
set -euo pipefail

BASE_URL="https://sorcha.example.com"

# --- Step 1: Authenticate ---
echo "Authenticating..."
TOKEN=$(curl -s -X POST "$BASE_URL/api/auth/login" \
  -H "Content-Type: application/json" \
  -d '{"email": "admin@example.com", "password": "your-password"}' \
  | jq -r '.accessToken')

if [ "$TOKEN" = "null" ] || [ -z "$TOKEN" ]; then
  echo "Authentication failed"
  exit 1
fi
echo "Authenticated successfully"

# --- Step 2: Create Organization ---
echo "Creating organization..."
ORG_RESPONSE=$(curl -s -X POST "$BASE_URL/api/organizations" \
  -H "Authorization: Bearer $TOKEN" \
  -H "Content-Type: application/json" \
  -d '{
    "name": "Acme Corporation",
    "subdomain": "acme"
  }')
ORG_ID=$(echo "$ORG_RESPONSE" | jq -r '.id')
echo "Organization created: $ORG_ID"

# --- Step 3: Add a User ---
echo "Adding user to organization..."
USER_RESPONSE=$(curl -s -X POST "$BASE_URL/api/organizations/$ORG_ID/users" \
  -H "Authorization: Bearer $TOKEN" \
  -H "Content-Type: application/json" \
  -d '{
    "email": "alice@acme.com",
    "displayName": "Alice Smith",
    "externalIdpSubject": "auth0|alice123",
    "roles": ["Member"]
  }')
USER_ID=$(echo "$USER_RESPONSE" | jq -r '.id')
echo "User added: $USER_ID"

# --- Step 4: Register Participant ---
echo "Registering participant..."
PARTICIPANT_RESPONSE=$(curl -s -X POST "$BASE_URL/api/organizations/$ORG_ID/participants" \
  -H "Authorization: Bearer $TOKEN" \
  -H "Content-Type: application/json" \
  -d "{
    \"userId\": \"$USER_ID\",
    \"displayName\": \"Alice Smith\"
  }")
PARTICIPANT_ID=$(echo "$PARTICIPANT_RESPONSE" | jq -r '.id')
echo "Participant registered: $PARTICIPANT_ID"

# --- Step 5: Initiate Wallet Link ---
echo "Initiating wallet link..."
CHALLENGE_RESPONSE=$(curl -s -X POST \
  "$BASE_URL/api/organizations/$ORG_ID/participants/$PARTICIPANT_ID/wallet-links" \
  -H "Authorization: Bearer $TOKEN" \
  -H "Content-Type: application/json" \
  -d '{
    "walletAddress": "ed25519:abc123def456...",
    "algorithm": "ED25519"
  }')
CHALLENGE_ID=$(echo "$CHALLENGE_RESPONSE" | jq -r '.challengeId')
CHALLENGE=$(echo "$CHALLENGE_RESPONSE" | jq -r '.challenge')
echo "Challenge issued: $CHALLENGE_ID"
echo "Sign this challenge with wallet private key: $CHALLENGE"

# --- Step 6: Verify Wallet Link ---
# In production, the signature would come from the wallet SDK
# SIGNATURE="base64-signature-from-wallet..."
# PUBLIC_KEY="base64-public-key..."
# curl -s -X POST \
#   "$BASE_URL/api/organizations/$ORG_ID/participants/$PARTICIPANT_ID/wallet-links/$CHALLENGE_ID/verify" \
#   -H "Authorization: Bearer $TOKEN" \
#   -H "Content-Type: application/json" \
#   -d "{\"signature\": \"$SIGNATURE\", \"publicKey\": \"$PUBLIC_KEY\"}"

echo ""
echo "Organization onboarding complete!"
echo "  Organization: $ORG_ID"
echo "  User:         $USER_ID"
echo "  Participant:  $PARTICIPANT_ID"
echo "  Next: Sign the wallet challenge and verify to complete wallet linking"
```

## Next Steps

- [Public User Setup Guide](public-user-setup.md) -- configure passkey authentication for end users
- [Authentication Setup](../guides/AUTHENTICATION-SETUP.md) -- JWT configuration details
- [API Documentation](../reference/API-DOCUMENTATION.md) -- full API reference
- [Port Configuration](../getting-started/PORT-CONFIGURATION.md) -- service port assignments
