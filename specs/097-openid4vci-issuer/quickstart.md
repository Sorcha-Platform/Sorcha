# Quickstart: Verifying OpenID4VCI Issuer Endpoint Locally

**Feature**: 097-openid4vci-issuer

## Prerequisites

- Specs 093, 094, 095, and 096 merged to master
- .NET 10 SDK, Docker Desktop
- `curl` and `jq` available on PATH
- An admin JWT token exported as `$ADMIN_TOKEN`

## 1. Start services

```bash
docker-compose up -d
```

Confirm the HAIP service is running:

```bash
curl -s http://localhost:5500/health
# Expected: Healthy
```

## 2. Provision a tenant trust anchor (spec 096)

Skip this step if the tenant already has a root CA provisioned.

```bash
TENANT_ID="your-tenant-id"

curl -s -X POST "http://localhost/api/v1/trust/tenants/${TENANT_ID}/provision" \
  -H "Authorization: Bearer $ADMIN_TOKEN" \
  -H "Content-Type: application/json" \
  -d '{"algorithm":"Ed25519","validityYears":10,"signingMode":"Local"}' | jq
```

### Expected

A `TenantRootCa` record with a self-signed root certificate, Ed25519 public key, and
10-year validity.

## 3. Enrol an org as HAIP issuer

The org wallet must have the `HaipIssuer` capability (spec 094). Enrol it under the
tenant trust anchor:

```bash
ORG_WALLET="sorcha1abc123..."

curl -s -X POST "http://localhost/api/v1/trust/tenants/${TENANT_ID}/orgs/${ORG_WALLET}/enrol" \
  -H "Authorization: Bearer $ADMIN_TOKEN" \
  -H "Content-Type: application/json" \
  -d '{"displayName":"Test Council","validityYears":2}' | jq
```

### Expected

An `OrgCertEnrolment` record with a leaf certificate issued by the tenant root, subject
CN = "Test Council", SAN URI = `did:sorcha:org:{walletAddress}`.

## 4. Create a credential offer via the internal API

Call the HAIP service's internal offer creation endpoint as the Blueprint Service would:

```bash
ORG_ID="your-org-uuid"

OFFER=$(curl -s -X POST "http://localhost:5500/api/v1/offers" \
  -H "Authorization: Bearer $ADMIN_TOKEN" \
  -H "Content-Type: application/json" \
  -d '{
    "credentialType": "urn:sorcha:credential:short-term-let-licence",
    "issuerOrgId": "'"$ORG_ID"'",
    "issuerWalletAddress": "'"$ORG_WALLET"'",
    "blueprintActionId": "00000000-0000-0000-0000-000000000099",
    "mappedClaims": {
      "licenceNumber": "STL-2026-001",
      "propertyAddress": "42 Example Street",
      "validFrom": "2026-04-10",
      "validUntil": "2027-04-10"
    },
    "disclosableFields": ["/propertyAddress", "/licenceNumber"],
    "codeTtlSeconds": 300
  }')

echo "$OFFER" | jq

PRE_AUTH_CODE=$(echo "$OFFER" | jq -r '.preAuthorizedCode')
OFFER_ID=$(echo "$OFFER" | jq -r '.offerId')
```

### Expected

A 201 response with `offerId`, `credentialOfferUri`, `preAuthorizedCode`, `expiresAt`,
and `status: "Pending"`. The `credentialOfferUri` is an `openid-credential-offer://` URI
ready for QR rendering.

## 5. Exchange the pre-authorized code at /token

```bash
TOKEN_RESPONSE=$(curl -s -X POST "http://localhost/token" \
  -H "Content-Type: application/x-www-form-urlencoded" \
  -d "grant_type=urn:ietf:params:oauth:grant-type:pre-authorized_code&pre-authorized_code=${PRE_AUTH_CODE}")

echo "$TOKEN_RESPONSE" | jq

ACCESS_TOKEN=$(echo "$TOKEN_RESPONSE" | jq -r '.access_token')
C_NONCE=$(echo "$TOKEN_RESPONSE" | jq -r '.c_nonce')
```

### Expected

A 200 response containing:
- `access_token` -- short-lived Bearer token
- `token_type: "Bearer"`
- `expires_in: 300`
- `c_nonce` -- nonce for the JWT proof
- `c_nonce_expires_in: 300`

### Verify one-time use

Replay the same code and confirm rejection:

```bash
curl -s -X POST "http://localhost/token" \
  -H "Content-Type: application/x-www-form-urlencoded" \
  -d "grant_type=urn:ietf:params:oauth:grant-type:pre-authorized_code&pre-authorized_code=${PRE_AUTH_CODE}" | jq
# Expected: {"error":"invalid_grant","error_description":"..."}
```

## 6. Get a fresh c_nonce from /nonce

This step is optional if you already have a valid `c_nonce` from the token response.
Use it to refresh the nonce without re-exchanging the code:

```bash
NONCE_RESPONSE=$(curl -s -X POST "http://localhost/nonce" \
  -H "Authorization: Bearer $ACCESS_TOKEN")

echo "$NONCE_RESPONSE" | jq

C_NONCE=$(echo "$NONCE_RESPONSE" | jq -r '.c_nonce')
```

### Expected

A 200 response with a fresh `c_nonce` and `c_nonce_expires_in`. The previous nonce is
now invalid.

## 7. Submit a credential request with JWT proof to /credential

Build a JWT proof of possession. In a real flow the wallet signs this; here we construct
it manually for verification purposes.

### 7a. Generate a holder key pair

```bash
# Generate an ephemeral P-256 key pair for the holder
openssl ecparam -genkey -name prime256v1 -noout -out holder_key.pem
openssl ec -in holder_key.pem -pubout -out holder_pub.pem
```

### 7b. Construct and sign the JWT proof

The JWT proof must have:
- **Header**: `alg: ES256`, `typ: openid4vci-proof+jwt`, `jwk: {holder public key}`
- **Payload**: `aud: "{credential_issuer URL}"`, `iat: {now}`, `nonce: "{c_nonce}"`

Use a JWT library or script to construct and sign the proof with the holder private key.
For example, using a Node.js one-liner or the `jose` CLI:

```bash
# Pseudo-code -- replace with your preferred JWT signing tool
JWT_PROOF=$(build-jwt-proof \
  --key holder_key.pem \
  --aud "https://sorcha.example.com" \
  --nonce "$C_NONCE")
```

### 7c. Call the credential endpoint

```bash
CRED_RESPONSE=$(curl -s -X POST "http://localhost/credential" \
  -H "Authorization: Bearer $ACCESS_TOKEN" \
  -H "Content-Type: application/json" \
  -d '{
    "format": "vc+sd-jwt",
    "vct": "urn:sorcha:credential:short-term-let-licence",
    "proof": {
      "proof_type": "jwt",
      "jwt": "'"$JWT_PROOF"'"
    }
  }')

echo "$CRED_RESPONSE" | jq
```

### Expected

A 200 response containing:
- `credential` -- a serialised SD-JWT VC string
- `c_nonce` -- optional fresh nonce for batch use
- `c_nonce_expires_in` -- optional

## 8. Verify the returned SD-JWT VC

### 8a. Decode and inspect the credential

The credential is a compact SD-JWT: `header.payload~disclosure1~disclosure2~...`

Split on the first `.` to get the base64url-encoded JWS header, then decode:

```bash
SD_JWT=$(echo "$CRED_RESPONSE" | jq -r '.credential')

# Extract and decode the JWS header
HEADER=$(echo "$SD_JWT" | cut -d'.' -f1 | base64 -d 2>/dev/null || echo "$SD_JWT" | cut -d'.' -f1 | python3 -c "import sys,base64,json; print(json.dumps(json.loads(base64.urlsafe_b64decode(sys.stdin.read()+'==')),indent=2))")

# Extract and decode the payload (second segment, up to the first ~)
PAYLOAD_SEGMENT=$(echo "$SD_JWT" | cut -d'.' -f2 | cut -d'~' -f1)
PAYLOAD=$(echo "$PAYLOAD_SEGMENT" | python3 -c "import sys,base64,json; print(json.dumps(json.loads(base64.urlsafe_b64decode(sys.stdin.read()+'==')),indent=2))")

echo "=== JWS Header ==="
echo "$HEADER"
echo ""
echo "=== Payload ==="
echo "$PAYLOAD"
```

### 8b. Verify required claims

Check the following are present in the decoded credential:

| Claim | Location | What to verify |
|-------|----------|----------------|
| `x5c` | JWS header | Array with 2+ entries. Leaf cert issued by tenant root. |
| `cnf.jwk` | Payload | Matches the holder public key from your JWT proof. |
| `status.status_list` | Payload | Object with `uri` (HTTPS URL) and `idx` (integer). |
| `vct` | Payload | Matches `urn:sorcha:credential:short-term-let-licence`. |
| `iss` | Payload | Issuer DID (`did:sorcha:org:{walletAddress}`). |

```bash
# Quick checks with jq
echo "$HEADER" | jq '.x5c | length'
# Expected: >= 2

echo "$PAYLOAD" | jq '.cnf.jwk'
# Expected: holder public key JWK

echo "$PAYLOAD" | jq '.status.status_list'
# Expected: {"uri":"https://...","idx": N}

echo "$PAYLOAD" | jq '.vct'
# Expected: "urn:sorcha:credential:short-term-let-licence"
```

### 8c. Verify the x5c chain

Extract the leaf and root certificates from the `x5c` array and verify the chain:

```bash
# Extract certs from x5c
echo "$HEADER" | jq -r '.x5c[0]' | base64 -d > leaf.cer
echo "$HEADER" | jq -r '.x5c[-1]' | base64 -d > root.cer

# Inspect the leaf cert
openssl x509 -inform DER -in leaf.cer -text -noout

# Verify the chain
openssl verify -CAfile <(openssl x509 -inform DER -in root.cer) \
  -untrusted <(openssl x509 -inform DER -in leaf.cer) \
  <(openssl x509 -inform DER -in leaf.cer)
```

### Expected -- all checks pass

- `x5c` contains a valid certificate chain from org leaf to tenant root
- `cnf.jwk` matches the holder key used in the JWT proof
- `status.status_list` contains a `uri` pointing to the IETF status list endpoint and an `idx` for this credential
- `vct` identifies the credential type
- The SD-JWT VC signature verifies against the leaf certificate's public key

## 9. Check offer status (optional)

Confirm the offer lifecycle progressed to `Issued`:

```bash
curl -s "http://localhost:5500/api/v1/offers/${OFFER_ID}" \
  -H "Authorization: Bearer $ADMIN_TOKEN" | jq '.status'
# Expected: "Issued"
```

## Troubleshooting

| Symptom | Likely cause |
|---------|-------------|
| `/.well-known/openid-credential-issuer` returns 404 | HAIP service not running or no gateway route configured |
| `credentials_supported` is empty | No org enrolled as HAIP issuer under this tenant |
| Token endpoint returns `invalid_grant` | Pre-authorized code expired (5 min default) or already used |
| Credential endpoint returns `invalid_proof` | Nonce mismatch, clock skew > 60s, signature invalid, or wrong `aud` |
| Credential endpoint returns `invalid_token` | Access token expired (5 min default); re-exchange a new offer |
| `x5c` missing from credential header | Org not enrolled under tenant trust anchor (spec 096) |
| `status.status_list` missing from payload | IETF status list not configured (spec 095) |
