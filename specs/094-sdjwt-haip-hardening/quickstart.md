# Quickstart: Verifying SD-JWT VC HAIP Hardening Locally

**Feature**: 094-sdjwt-haip-hardening

## Prerequisites

- Spec 093 merged to master
- .NET 10 SDK, Docker Desktop

## 1. Verify `cnf` holder key binding at issuance

### Steps

1. Create two wallets (issuer + holder), note their addresses.
2. Fetch the holder's public JWK:
   ```bash
   curl http://localhost/api/v1/wallets/{holder-address}/holder-binding-key
   ```
3. Issue a credential passing the returned JWK in the new `holderJwk` field on `POST /api/v1/wallets/{issuer-address}/credentials/issue`.
4. Decode the returned `rawToken`'s JWT payload and confirm `cnf.jwk` matches the holder's binding key JWK.

### Expected

Signed payload contains `cnf` as a non-disclosable claim. `cnf.jwk.kty` and `crv` match the holder wallet's algorithm.

## 2. Verify Key Binding JWT at presentation

### Steps

1. Create a presentation request for the credential type.
2. Have the holder create a presentation disclosing a subset of claims, with a KB-JWT bound to the verifier's `audience` and `nonce`.
3. Submit the presentation to `/api/v1/presentations/{requestId}/submit`.

### Expected

`PresentationRequest.Status == "Verified"` and `VerificationResult.HolderKeyVerified == true`.

### Negative cases

- Replay with a different `audience` → KB-JWT audience mismatch error.
- Replay with a different `nonce` → KB-JWT nonce mismatch error.
- Tamper the KB-JWT signature → KB-JWT signature invalid error.
- Omit the KB-JWT from the serialised presentation (for a `cnf`-bearing credential) → "Missing KB-JWT" error.

## 3. Verify nested and array-element disclosure

### Steps

1. Issue a credential with:
   ```json
   {
     "name": "Alice",
     "address": {"street": "1 Main St", "locality": "Edinburgh", "country": "GB"},
     "qualifications": [{"type": "A"}, {"type": "B"}, {"type": "C"}]
   }
   ```
   and `disclosablePaths: ["/address/locality", "/address/country", "/qualifications/0", "/qualifications/1", "/qualifications/2"]`.
2. Decode the payload. Confirm:
   - `address.street` is in plaintext (not disclosable)
   - `address` contains an `_sd` array at its level with 2 digests
   - `qualifications` is an array of 3 `{"...": digest}` placeholders
3. Present disclosing only `/address/locality` and `/qualifications/1`.
4. At the verifier, confirm the reconstructed claims contain exactly `address.locality` and `qualifications[1]`, and nothing else disclosable.

## 4. Verify classical co-key for PQC-primary wallet

### Steps

1. Create a wallet with `algorithm: "ML-DSA-65"` and the `HaipIssuer` capability enabled.
2. Confirm `GET /api/v1/wallets/{address}` shows both the primary ML-DSA key and the derived `HaipIssuerCoKey` with ES256 algorithm.
3. Issue a HAIP-path credential from that wallet.
4. Decode the signed token and inspect the JWS header `alg`: it MUST be `ES256`, not `ML-DSA-65`.

## 5. Backward compatibility

1. Fetch a pre-fix credential seeded from spec 093's walkthroughs (no `cnf` in payload).
2. Present it through the standard flow without a KB-JWT.
3. Confirm verification succeeds.

## Sign-off criteria

- [ ] All spec 094 acceptance scenarios pass in automated tests.
- [ ] All spec 093 tests continue to pass (regression).
- [ ] An externally-signed KB-JWT (for a HAIP-external holder) verifies correctly when supplied via the new `KbJwtSigningDelegate` shape.
- [ ] A PQC-primary wallet without `HaipIssuer` flag is refused HAIP issuance with a capability-missing error.
- [ ] Legacy credentials without `cnf` continue to verify.
