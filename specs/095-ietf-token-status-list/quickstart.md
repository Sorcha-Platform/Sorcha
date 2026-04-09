# Quickstart: Verifying IETF Token Status List Locally

**Feature**: 095-ietf-token-status-list

## Prerequisites

- Spec 093 and 094 merged to master
- .NET 10 SDK, Docker Desktop

## 1. Fetch the IETF Token Status List envelope

### Steps

1. Issue a HAIP-path credential (via spec 097's eventual flow, or directly via the issue endpoint with `StatusClaimForm: IetfTokenStatusList`).
2. Note the allocated `listId` from the credential's `status.status_list.uri`.
3. Fetch the IETF endpoint:
   ```bash
   curl http://localhost/api/v1/credentials/ietf-status-lists/{listId}
   ```

### Expected

A signed JWT with header `{"typ":"statuslist+jwt","alg":"EdDSA"}` and payload containing `iss`, `sub`, `iat`, `exp`, `ttl`, `status_list: { bits, lst }`.

Decoded `lst` (base64url → zlib decompress) MUST be byte-identical to decoded W3C `encodedList` (base64url → gzip decompress) for the same list.

## 2. Verify dual-envelope consistency

### Steps

1. Fetch both endpoints for the same list:
   ```bash
   curl http://localhost/api/v1/credentials/status-lists/{listId}       # W3C
   curl http://localhost/api/v1/credentials/ietf-status-lists/{listId}  # IETF
   ```
2. Extract `encodedList` (W3C) and `lst` (IETF) from the respective responses.
3. Decompress both and compare bytes.

### Expected

Bytes match exactly.

## 3. Flip a bit and observe both envelopes

### Steps

1. Revoke a credential that has been allocated on the list:
   ```bash
   curl -X POST http://localhost/api/v1/credentials/{credentialId}/revoke -H "Authorization: Bearer $TOKEN"
   ```
2. Wait ≥ 5 minutes (default cache TTL) or clear client-side caches.
3. Refetch both endpoints and confirm the bit at the credential's index is now `1` in both.

## 4. Verify the IETF consumer in the presentation verifier

### Steps

1. Issue a credential with `StatusClaimForm: IetfTokenStatusList` — the signed payload contains `status.status_list` pointing at the IETF endpoint.
2. Present the credential via `/api/v1/presentations/{requestId}/submit`.
3. The verifier should fetch the IETF endpoint, verify the JWT envelope signature, decompress the bitstring, read the bit, and accept the presentation.

### Negative case

Revoke the credential, then present again. The verifier should fail with a `Revoked` status error.

## Sign-off criteria

- [ ] All spec 095 acceptance scenarios pass in automated tests
- [ ] W3C and IETF envelopes for the same list have byte-identical decompressed bytes
- [ ] A bit flip propagates to both endpoints after cache TTL
- [ ] The presentation verifier accepts credentials carrying IETF `status.status_list` claims
- [ ] The presentation verifier continues to accept credentials carrying W3C `credentialStatus` claims (spec 093 regression)
- [ ] Legacy credentials without either claim form continue to verify via the server-side row fallback (spec 093 FR-010)
