# Quickstart: Verifying OpenID4VP Verifier Endpoint Locally

**Feature**: 098-openid4vp-verifier

## Prerequisites

- Specs 093 through 097 merged to master
- .NET 10 SDK, Docker Desktop
- `curl` and `jq` available on PATH
- An admin JWT token exported as `$ADMIN_TOKEN`
- A tenant trust anchor provisioned (spec 096)
- An org enrolled as HAIP issuer (spec 097)

## 1. Start services

```bash
docker-compose up -d
```

Confirm the HAIP service is running:

```bash
curl -s http://localhost:5500/health
# Expected: Healthy
```

## 2. Issue a credential via the HAIP issuer (spec 097)

Follow the spec 097 quickstart to issue a credential. The short version:

```bash
ORG_ID="your-org-uuid"
ORG_WALLET="sorcha1abc123..."

# Create a credential offer
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
      "propertyAddress": {"streetAddress": "42 Example Street", "council": "Riverside"},
      "validFrom": "2026-04-10",
      "validUntil": "2027-04-10"
    },
    "disclosableFields": ["/propertyAddress", "/licenceNumber", "/validUntil"],
    "codeTtlSeconds": 300
  }')

PRE_AUTH_CODE=$(echo "$OFFER" | jq -r '.preAuthorizedCode')

# Exchange code for access token
TOKEN_RESPONSE=$(curl -s -X POST "http://localhost/token" \
  -H "Content-Type: application/x-www-form-urlencoded" \
  -d "grant_type=urn:ietf:params:oauth:grant-type:pre-authorized_code&pre-authorized_code=${PRE_AUTH_CODE}")

ACCESS_TOKEN=$(echo "$TOKEN_RESPONSE" | jq -r '.access_token')
C_NONCE=$(echo "$TOKEN_RESPONSE" | jq -r '.c_nonce')

# Generate holder key pair
openssl ecparam -genkey -name prime256v1 -noout -out holder_key.pem
openssl ec -in holder_key.pem -pubout -out holder_pub.pem

# Build JWT proof and request credential (see 097 quickstart for full details)
JWT_PROOF=$(build-jwt-proof --key holder_key.pem --aud "https://sorcha.example.com" --nonce "$C_NONCE")

CRED_RESPONSE=$(curl -s -X POST "http://localhost/credential" \
  -H "Authorization: Bearer $ACCESS_TOKEN" \
  -H "Content-Type: application/json" \
  -d '{
    "format": "vc+sd-jwt",
    "vct": "urn:sorcha:credential:short-term-let-licence",
    "proof": { "proof_type": "jwt", "jwt": "'"$JWT_PROOF"'" }
  }')

SD_JWT_VC=$(echo "$CRED_RESPONSE" | jq -r '.credential')
echo "Issued credential: ${SD_JWT_VC:0:80}..."
```

### Expected

A serialised SD-JWT VC with `x5c` chain, `cnf.jwk`, and `status.status_list` claims.
Save `$SD_JWT_VC` and the holder private key -- you will need both to build a presentation.

## 3. Create a Presentation Request via the internal API

Call the HAIP verifier's internal endpoint as the Blueprint Service would:

```bash
VERIFIER_ORG_ID="your-verifier-org-uuid"
VERIFIER_WALLET="sorcha1xyz789..."

PRES_REQ=$(curl -s -X POST "http://localhost:5500/api/v1/verifier/requests" \
  -H "Authorization: Bearer $ADMIN_TOKEN" \
  -H "Content-Type: application/json" \
  -d '{
    "credentialType": "urn:sorcha:credential:short-term-let-licence",
    "acceptedIssuers": ["did:sorcha:org:'"$ORG_WALLET"'"],
    "requiredClaims": ["licenceNumber", "/propertyAddress/streetAddress", "validUntil"],
    "verifierOrgId": "'"$VERIFIER_ORG_ID"'",
    "verifierWalletAddress": "'"$VERIFIER_WALLET"'",
    "blueprintActionId": "00000000-0000-0000-0000-000000000088",
    "ttlSeconds": 300
  }')

echo "$PRES_REQ" | jq

REQUEST_ID=$(echo "$PRES_REQ" | jq -r '.requestId')
REQUEST_URI=$(echo "$PRES_REQ" | jq -r '.requestUri')
NONCE=$(echo "$PRES_REQ" | jq -r '.nonce')
STATE=$(echo "$PRES_REQ" | jq -r '.state')
AUTH_REQUEST_URI=$(echo "$PRES_REQ" | jq -r '.authorizationRequestUri')
```

### Expected

A 201 response with:
- `requestId` -- unique identifier for polling
- `authorizationRequestUri` -- `openid4vp://authorize?...` deep-link for QR rendering
- `requestUri` -- HTTPS URL the wallet fetches the signed Request Object from
- `nonce` -- bound into the Request Object for KB-JWT verification
- `state` -- correlates the `direct_post` callback to this request
- `expiresAt` -- UTC expiry (default 5 minutes from now)
- `status: "Pending"`

## 4. Fetch the Request Object (simulating wallet)

A HAIP wallet fetches the signed Request Object via the `request_uri`:

```bash
REQUEST_OBJECT=$(curl -s "$REQUEST_URI" \
  -H "Accept: application/oauth-authz-req+jwt")

echo "Request Object (compact JWT):"
echo "${REQUEST_OBJECT:0:120}..."

# Decode the payload to inspect the presentation_definition
PAYLOAD=$(echo "$REQUEST_OBJECT" | cut -d'.' -f2 | python3 -c \
  "import sys,base64,json; print(json.dumps(json.loads(base64.urlsafe_b64decode(sys.stdin.read()+'==')),indent=2))")

echo ""
echo "=== Request Object Payload ==="
echo "$PAYLOAD" | jq
```

### Expected

A signed JWT (`application/oauth-authz-req+jwt`) whose decoded payload contains:
- `client_id` -- the verifier's DID (from X.509 SAN URI)
- `client_id_scheme: "x509_san_uri"`
- `response_type: "vp_token"`
- `response_mode: "direct_post"`
- `response_uri` -- absolute URL of the `direct_post` endpoint
- `nonce` -- matches the nonce from step 3
- `state` -- matches the state from step 3
- `aud: "https://self-issued.me/v2"`
- `presentation_definition` -- DIF PE 2.0 document with input descriptors

### Verify the presentation_definition

```bash
echo "$PAYLOAD" | jq '.presentation_definition.input_descriptors[0].constraints.fields'
```

Each required claim from step 3 should appear as a field constraint with a JSON Path
(e.g., `$.licenceNumber`, `$.propertyAddress.streetAddress`, `$.validUntil`).

### Verify the Request Object signature

```bash
# Extract the x5c header
HEADER=$(echo "$REQUEST_OBJECT" | cut -d'.' -f1 | python3 -c \
  "import sys,base64,json; print(json.dumps(json.loads(base64.urlsafe_b64decode(sys.stdin.read()+'==')),indent=2))")

echo "$HEADER" | jq '.x5c | length'
# Expected: >= 2 (leaf + root, matching the issuer chain from spec 096)
```

### Verify expired request returns 410

Wait for the TTL to elapse (or create a request with `ttlSeconds: 5` for quick testing):

```bash
# After TTL elapses:
curl -s -o /dev/null -w "%{http_code}" "$REQUEST_URI"
# Expected: 410
```

## 5. Submit a vp_token via direct_post (simulating wallet approval)

Build a Verifiable Presentation (SD-JWT with Key Binding JWT) and submit it.

### 5a. Select disclosures

From the issued SD-JWT VC, select the disclosures that match the required claims
(`licenceNumber`, `propertyAddress.streetAddress`, `validUntil`). The SD-JWT VC format
is `header.payload~disclosure1~disclosure2~...~`. Each disclosure is a base64url-encoded
JSON array `[salt, claim_name, claim_value]`.

```bash
# List all disclosures in the issued credential
IFS='~' read -ra PARTS <<< "$SD_JWT_VC"
echo "Issuer JWT: ${PARTS[0]:0:40}..."
for i in "${!PARTS[@]}"; do
  if [ $i -gt 0 ] && [ -n "${PARTS[$i]}" ]; then
    echo "Disclosure $i: $(echo "${PARTS[$i]}" | python3 -c \
      "import sys,base64,json; print(json.loads(base64.urlsafe_b64decode(sys.stdin.read()+'==')))" 2>/dev/null || echo "${PARTS[$i]:0:40}...")"
  fi
done
```

Select only the disclosures matching the required claims and concatenate them with `~`
separators after the issuer JWT.

### 5b. Build the Key Binding JWT

The KB-JWT proves holder binding. It must be signed by the holder's private key
(matching `cnf.jwk` in the credential).

KB-JWT header:
```json
{
  "alg": "ES256",
  "typ": "kb+jwt"
}
```

KB-JWT payload:
```json
{
  "aud": "<client_id from Request Object>",
  "nonce": "<nonce from Request Object>",
  "iat": 1712000000,
  "sd_hash": "<SHA-256 of issuer-jwt~selected-disclosures~>"
}
```

- `aud` must match the verifier's `client_id` from the Request Object payload
- `nonce` must match the Request Object's `nonce` (same as step 3)
- `sd_hash` is the base64url-encoded SHA-256 hash of everything before the KB-JWT

```bash
# Pseudo-code -- replace with your preferred JWT signing tool
VERIFIER_CLIENT_ID=$(echo "$PAYLOAD" | jq -r '.client_id')
RESPONSE_URI=$(echo "$PAYLOAD" | jq -r '.response_uri')

KB_JWT=$(build-kb-jwt \
  --key holder_key.pem \
  --aud "$VERIFIER_CLIENT_ID" \
  --nonce "$NONCE" \
  --sd-jwt-prefix "$SELECTED_DISCLOSURES_PREFIX")
```

### 5c. Assemble the vp_token

Concatenate: `issuer-jwt~selected-disclosure1~...~selected-disclosureN~kb-jwt`

```bash
VP_TOKEN="${ISSUER_JWT}~${SELECTED_DISCLOSURES}~${KB_JWT}"
```

### 5d. Build the presentation_submission

```bash
DEFINITION_ID=$(echo "$PAYLOAD" | jq -r '.presentation_definition.id')
DESCRIPTOR_ID=$(echo "$PAYLOAD" | jq -r '.presentation_definition.input_descriptors[0].id')

PRES_SUBMISSION=$(cat <<ENDJSON
{
  "id": "submission-$(date +%s)",
  "definition_id": "$DEFINITION_ID",
  "descriptor_map": [
    {
      "id": "$DESCRIPTOR_ID",
      "format": "vc+sd-jwt",
      "path": "\$"
    }
  ]
}
ENDJSON
)
```

### 5e. POST to direct_post

```bash
DIRECT_POST_RESPONSE=$(curl -s -w "\n%{http_code}" -X POST "$RESPONSE_URI" \
  -H "Content-Type: application/x-www-form-urlencoded" \
  --data-urlencode "vp_token=${VP_TOKEN}" \
  --data-urlencode "presentation_submission=${PRES_SUBMISSION}" \
  --data-urlencode "state=${STATE}")

HTTP_CODE=$(echo "$DIRECT_POST_RESPONSE" | tail -1)
BODY=$(echo "$DIRECT_POST_RESPONSE" | sed '$d')

echo "HTTP Status: $HTTP_CODE"
echo "Response: $BODY"
```

### Expected

- HTTP 200 with `{"redirect_uri": null}` -- verification succeeded
- The Presentation Request transitions from `Pending` to `Verified`

### Verify replay is rejected

Replay the same submission:

```bash
curl -s -X POST "$RESPONSE_URI" \
  -H "Content-Type: application/x-www-form-urlencoded" \
  --data-urlencode "vp_token=${VP_TOKEN}" \
  --data-urlencode "presentation_submission=${PRES_SUBMISSION}" \
  --data-urlencode "state=${STATE}" | jq

# Expected: {"error":"invalid_request","error_description":"...already fulfilled..."}
```

## 6. Check the verification result

Poll the internal result endpoint:

```bash
curl -s "http://localhost:5500/api/v1/verifier/requests/${REQUEST_ID}/result" \
  -H "Authorization: Bearer $ADMIN_TOKEN" | jq
```

### Expected

```json
{
  "requestId": "a2c4e6f8-...",
  "status": "Verified",
  "verifiedAt": "2026-04-10T12:01:30Z",
  "issuerIdentity": {
    "did": "did:sorcha:org:sorcha1abc123...",
    "x509Subject": "CN=Test Council",
    "trustPath": "X509"
  },
  "verifiedClaims": {
    "licenceNumber": "STL-2026-001",
    "/propertyAddress/streetAddress": "42 Example Street",
    "validUntil": "2027-04-10"
  },
  "inputDescriptorResults": [
    {
      "descriptorId": "short_term_let_licence",
      "matched": true,
      "matchedClaims": ["licenceNumber", "/propertyAddress/streetAddress", "validUntil"]
    }
  ],
  "verifierAttestation": "eyJhbGciOi...",
  "failureCause": null
}
```

Key checks:
- `status` is `Verified`
- `verifiedClaims` contains exactly the claims requested in step 3
- `issuerIdentity.trustPath` is `X509` (credential used an `x5c` chain)
- `inputDescriptorResults[0].matched` is `true`
- `failureCause` is `null`

## 7. Verify claims match the presentation_definition

Cross-reference the verified claims against the `presentation_definition` from step 4:

```bash
# Extract required field paths from the presentation_definition
echo "=== Required claim paths ==="
echo "$PAYLOAD" | jq -r '.presentation_definition.input_descriptors[0].constraints.fields[].path[0]'

echo ""
echo "=== Verified claim keys ==="
curl -s "http://localhost:5500/api/v1/verifier/requests/${REQUEST_ID}/result" \
  -H "Authorization: Bearer $ADMIN_TOKEN" | jq -r '.verifiedClaims | keys[]'
```

### Expected

Every field path declared in the `presentation_definition` (excluding the `$.vct`
type filter) has a corresponding key in the `verifiedClaims` map. The JSON Path
notation (`$.licenceNumber`) maps to the claim key (`licenceNumber`), and nested
paths (`$.propertyAddress.streetAddress`) map to JSON Pointer keys
(`/propertyAddress/streetAddress`).

## Troubleshooting

| Symptom | Likely cause |
|---------|-------------|
| 404 on `POST /api/v1/verifier/requests` | HAIP service not running or gateway route not configured |
| 400 "unknown credential type" | The `credentialType` VCT is not in `credentials_supported` metadata |
| 400 "verifier org not enrolled" | Verifier org wallet does not have a trust anchor (run spec 096 provisioning) |
| 410 on Request Object fetch | Presentation Request TTL expired (default 5 min); create a new one |
| 400 "state mismatch" on direct_post | The `state` value does not match any active request; check copy-paste |
| 403 "access_denied" on direct_post | Credential verification failed; check the result endpoint for `failureCause` |
| `kb_jwt_nonce_mismatch` | KB-JWT `nonce` does not match; ensure you used the nonce from step 3 |
| `kb_jwt_audience_mismatch` | KB-JWT `aud` does not match verifier `client_id`; check the Request Object |
| `trust_anchor_unknown` | Issuer's `x5c` chain root is not in the verifier's trust store |
| `credential_revoked` | Credential was revoked via status list between issuance and presentation |
| `input_descriptor_unmatched` | Selected disclosures do not cover all required claims |
| `request_already_fulfilled` | A submission was already accepted; each request is single-use |
