# Quickstart: Verifying the 093 Security Fixes Locally

**Feature**: 093-vc-security-fixes
**Audience**: Anyone verifying that the three bugs are fixed after merging this spec's implementation.

## Prerequisites

- .NET 10 SDK installed
- Docker Desktop running
- Sorcha repo checked out at branch `093-vc-security-fixes` (or master after merge)

## Bug 1: Presentation verifier now verifies the vpToken

### What you're verifying

Before the fix, submitting any plausible-looking `vpToken` to `POST /api/v1/presentations/{requestId}/submit` would be marked Verified as long as the credential existed in the server-side store. After the fix, an invalid-signature token is rejected.

### Steps

1. Start the Sorcha dev stack:
   ```bash
   docker-compose up -d
   ```
2. Create a test wallet with a known signing algorithm:
   ```bash
   curl -X POST http://localhost/api/v1/wallets \
     -H "Authorization: Bearer $TOKEN" \
     -H "Content-Type: application/json" \
     -d '{"name":"Test Issuer","algorithm":"ED25519","wordCount":12}'
   ```
3. Issue a credential through the existing endpoint:
   ```bash
   curl -X POST http://localhost/api/v1/wallets/{address}/credentials/issue \
     -H "Authorization: Bearer $TOKEN" \
     -H "Content-Type: application/json" \
     -d '{
       "credentialType":"TestLicense",
       "claims":{"licenseNumber":"TL-001","area":"Test"},
       "recipientWallet":"{address}",
       "expiryDuration":"P30D",
       "disclosableClaims":["licenseNumber","area"]
     }'
   ```
4. Create a presentation request for that credential type.
5. Submit a **tampered** vpToken (for example, flip a byte in the signature) to the submit endpoint. **Expected**: response carries a `Denied` outcome with a signature verification error. **Pre-fix behaviour**: the request would have been marked Verified.
6. Submit the **correct** vpToken as returned by the export endpoint. **Expected**: response carries a `Verified` outcome and `VerifiedClaims` contains only the claims actually disclosed in the token.
7. Confirm via inspection that `VerifiedClaims` did not include any server-side claim value that was not in the presented subset.

### Automated assertion

```bash
dotnet test tests/Sorcha.Wallet.Service.IntegrationTests \
  --filter "FullyQualifiedName~PresentationReplayIntegrationTests"
```

This should pass on the 093 branch and fail on master.

## Bug 2: credentialStatus is embedded in the signed payload

### What you're verifying

Before the fix, `POST /api/v1/wallets/{address}/credentials/issue` signed a token whose payload had no `credentialStatus` claim. After the fix, every new credential's decoded payload contains a W3C `BitstringStatusListEntry` pointer.

### Steps

1. Using a HAIP issuer wallet on a tenant that is configured with `CredentialStatus:EnableEmbedding = true` (default), call the issue endpoint with a valid request.
2. Extract the `rawToken` field from the response.
3. Decode the JWT payload (the middle base64url-encoded segment, before any `~`-separated disclosures):
   ```bash
   echo "$RAW_TOKEN" | cut -d'~' -f1 | cut -d'.' -f2 | base64 -d
   ```
4. Confirm the decoded JSON contains a `credentialStatus` object with the five fields listed in `data-model.md`.
5. Confirm the `statusListCredential` URL resolves to a valid W3C BitstringStatusListCredential endpoint and the bit at `statusListIndex` is 0 (Active).
6. Revoke the credential via `POST /api/v1/credentials/{credentialId}/revoke` and re-fetch the status list. Confirm the bit is now 1.

### Automated assertion

```bash
dotnet test tests/Sorcha.Wallet.Service.IntegrationTests \
  --filter "FullyQualifiedName~CredentialStatusEmbeddingIntegrationTests"
```

### Backwards compatibility

On the same stack, fetch a credential that was issued against master before the fix shipped (for example, from a seeded walkthrough). Confirm its `rawToken` does **not** contain `credentialStatus` (pre-fix shape) and that presenting it still succeeds via the server-side row fallback on the verifier side.

## Bug 3: did:sorcha DID documents use valid multibase

### What you're verifying

Before the fix, `SorchaDidResolver` emitted `publicKeyMultibase = "z" + hex` which is not valid multibase. After the fix, the value is `"z" + base58btc(multicodec || rawKey)`.

### Steps

1. Create three wallets with different algorithms (Ed25519, NIST-P256, RSA-4096).
2. For each wallet, resolve its `did:sorcha:w:{address}` via the internal DID resolver endpoint or the service client.
3. Inspect the returned `DidDocument.VerificationMethod[0].PublicKeyMultibase`.
4. Validate each with an independent W3C DID Core multibase parser — for example, a small Node.js script using `@digitalbazaar/multibase`:
   ```javascript
   import { decode } from '@digitalbazaar/multibase';
   const raw = decode('zQmhash...');  // the publicKeyMultibase value from Sorcha
   // Strip varint multicodec prefix and confirm the remaining bytes match the wallet's raw public key
   ```
5. Confirm each parser accepts the value without errors and that the decoded bytes round-trip to the original raw public key.

### Automated assertion

```bash
dotnet test tests/Sorcha.ServiceClients.Http.Tests \
  --filter "FullyQualifiedName~SorchaDidResolverMultibaseTests"
```

```bash
dotnet test tests/Sorcha.Cryptography.Tests \
  --filter "FullyQualifiedName~MulticodecTests"
```

## Full regression check

After applying the fix, run the full test suite to confirm no pre-existing behaviour regresses — particularly anything in spec 039:

```bash
dotnet test
```

Expect all tests to pass. Any failure in tests whose names reference `Presentation`, `Credential`, `StatusList`, or `DidResolver` is a regression of this spec's intended scope and must be investigated before merge.

## Rollback

If any of the three fixes causes a production incident after deployment:

1. Setting `CredentialStatus:EnableEmbedding = false` in `appsettings.json` reverts bug 2 to the pre-fix behaviour for new credentials (legacy fallback handles verification).
2. The `SorchaDidResolver` multibase fix and the `PresentationRequestService` verifier fix have no runtime toggle — rolling back requires a code-level revert.
3. Historical credentials issued between the fix shipping and any rollback retain their embedded `credentialStatus` claim and continue to verify via that path.

## Sign-off criteria

All of the following must be true before this spec is marked complete:

- [ ] All 7 Acceptance Criteria from `spec.md` pass in automated tests.
- [ ] All 10 Success Criteria measurable outcomes are demonstrated.
- [ ] The existing spec 039 regression suite passes without modification.
- [ ] A pre-fix credential (seeded or fetched from a mainline walkthrough) still verifies end-to-end.
- [ ] A tampered vpToken fails with a signature verification error in the presentation submit path.
- [ ] Each supported algorithm (Ed25519, P-256, RSA-4096) produces a DID document that validates in an independent third-party multibase parser.
