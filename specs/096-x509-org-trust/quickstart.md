# Quickstart: Verifying X.509 Organisation Trust Locally

**Feature**: 096-x509-org-trust

## Prerequisites

- Specs 093 and 094 merged to master
- .NET 10 SDK, Docker Desktop

## 1. Provision a Tenant Root CA

### Steps

1. Provision:
   ```bash
   curl -X POST http://localhost/api/v1/trust/tenants/{tenantId}/provision \
     -H "Authorization: Bearer $ADMIN_TOKEN" \
     -H "Content-Type: application/json" \
     -d '{"algorithm":"Ed25519","validityYears":10,"signingMode":"Local"}'
   ```
2. Fetch the trust anchor:
   ```bash
   curl http://localhost/api/v1/trust/tenants/{tenantId}/trust-anchor > root.cer
   ```
3. Inspect with OpenSSL:
   ```bash
   openssl x509 -inform DER -in root.cer -text -noout
   ```

### Expected

- Self-signed root with `CN=tenant display name`, 10-year validity, Ed25519 public key.

## 2. Enrol an organisation

### Steps

1. Create a wallet with `HaipIssuer` capability (spec 094).
2. Enrol the wallet as an org issuer:
   ```bash
   curl -X POST http://localhost/api/v1/trust/tenants/{tenantId}/orgs/{walletAddress}/enrol \
     -H "Authorization: Bearer $ADMIN_TOKEN" \
     -d '{"displayName":"ACME Ltd","validityYears":2}'
   ```
3. Fetch the org cert and inspect:
   ```bash
   # Response body contains `certDer` field in base64
   echo "{certDerBase64}" | base64 -d > org.cer
   openssl x509 -inform DER -in org.cer -text -noout
   ```

### Expected

- Subject CN = "ACME Ltd"
- SAN URI = `did:sorcha:org:{walletAddress}`
- CRL Distribution Points = tenant CRL URL
- Subject Public Key Info matches the wallet's `HaipIssuerCoKey.PublicKey`
- No EKU extension (Q4.2 ruling)
- Issued by the Tenant Root CA

## 3. Issue a HAIP-path credential with x5c

### Steps

1. Issue a credential from the enrolled wallet via the Blueprint or direct HTTP path with the HAIP flag set.
2. Extract the `rawToken` and decode its JWS header:
   ```bash
   echo "{headerBase64}" | base64 -d | jq
   ```

### Expected

Header contains `x5c` array with two entries (leaf = Org Cert, root = Tenant Root CA), both DER-encoded base64.

## 4. Verify externally

### Steps

1. Build the chain from the token's `x5c`.
2. Walk against the tenant root fetched at step 1:
   ```bash
   openssl verify -CAfile root.cer org.cer
   ```
3. Verify the token signature against the Org Cert's public key.

### Expected

Chain valid, signature valid.

## 5. Revoke and observe propagation

### Steps

1. Revoke the Org Cert:
   ```bash
   curl -X POST http://localhost/api/v1/trust/tenants/{tenantId}/orgs/{walletAddress}/revoke \
     -H "Authorization: Bearer $ADMIN_TOKEN" \
     -d '{"reason":"keyCompromise"}'
   ```
2. Refetch the CRL after the cache TTL elapses:
   ```bash
   curl http://localhost/api/v1/trust/tenants/{tenantId}/crl > crl.der
   openssl crl -inform DER -in crl.der -text -noout
   ```

### Expected

CRL contains the Org Cert's serial number with the `keyCompromise` reason code. Subsequent HAIP-path issuance attempts from the same wallet fail until a fresh Org Cert is enrolled.

## 6. Swap to external trust provider

### Steps

1. Configure a custom `ITrustProvider` implementation via DI that returns a pre-generated externally-rooted chain.
2. Run provisioning on a fresh tenant.
3. Confirm the trust anchor endpoint returns the externally-rooted cert, and org enrolment uses the external provider for signing.

## Sign-off criteria

- [ ] Tenant Root CA provisioning is idempotent
- [ ] Org Cert enrolment fails for wallets without `HaipIssuer` capability
- [ ] Issued credential JWS header contains valid `x5c` chain
- [ ] External OpenSSL `verify` confirms the chain
- [ ] Revocation propagates to the CRL after cache TTL
- [ ] Internal-path credentials (no `x5c`) continue to verify unchanged (spec 093 regression)
- [ ] DID-based verification path still works for credentials without `x5c` (spec 039 regression)
- [ ] External trust provider swap produces a deployment where the HAIP boundary works with no Sorcha code change
