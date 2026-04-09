# Phase 0 Research: IETF Token Status List

**Feature**: 095-ietf-token-status-list
**Date**: 2026-04-09

## Research items

1. IETF Token Status List JWT wire format — exact header and payload shape
2. zlib compression choice for IETF vs gzip for W3C — how the `StatusListManager` exposes the raw bitstring
3. Dual-envelope consistency — how to guarantee byte-identical decompressed bytes across both forms
4. `status.status_list` credential claim shape and placement in the SD-JWT payload
5. List issuer signing key resolution — which key signs the IETF JWT envelope
6. Endpoint caching and invalidation on lifecycle operations

---

## R1. IETF Token Status List JWT wire format

### Reference

IETF draft-ietf-oauth-status-list (current version as of HAIP 1.0 reference):
- JOSE header: `typ: "statuslist+jwt"`, `alg` per the signing key
- Payload claims:
  - `iss` — list issuer identifier (a DID or URL, matching the signing chain)
  - `sub` — the list URI
  - `iat` — issuance time
  - `exp` — envelope expiry (rolling; not the credentials' expiry)
  - `ttl` — cache TTL in seconds (optional, supplements `exp`)
  - `status_list`:
    - `bits` — 1, 2, 4, or 8 (bits per entry)
    - `lst` — base64url of zlib-compressed bitstring

### Decision: implement the current stable draft shape

**Rationale.** HAIP 1.0 references this draft. The shape is stable across recent drafts. `typ` is `statuslist+jwt`. Signing algorithm follows the list issuer's classical signing key (Ed25519, ES256, or RS256 — Ed25519 preferred for small envelopes).

**Consequence.** A new `IetfTokenStatusListSerializer` class serialises a `BitstringStatusList` (the existing type) into this envelope. Uses the existing `SdJwtService` infrastructure for JWS construction and signing — *or* a simpler inline JWS builder since the envelope is a plain JWT without SD-JWT disclosures.

**Simpler path**: use a small local helper that builds a plain JWT (no SD-JWT tilde suffix). The signing delegate calls into the Wallet Service's signing path to sign the JWS input bytes.

---

## R2. zlib vs gzip compression

### Current state

The existing W3C `StatusListManager` stores the backing bitstring as a `byte[]` or similar; the W3C endpoint compresses it with gzip before base64-encoding into `encodedList`. Looking at the existing `StatusListManager.cs` and the W3C endpoint at `StatusListEndpoints.cs:57-93`:

- W3C spec requires gzip of the bitstring before base64url.
- IETF spec requires zlib (which is gzip minus the gzip header wrapper — the raw deflate stream with zlib's 2-byte header, per RFC 1950).

### Decision: the `StatusListManager` exposes the raw uncompressed bitstring, and each envelope path compresses as needed

**Rationale.** Storing raw bytes means neither envelope is privileged. The W3C endpoint gzips on serialise; the new IETF endpoint zlibs on serialise. Both endpoints can hit the same cached raw bytes.

**Consequence.** Extract a `GetRawBitstringAsync(listId, ct)` accessor on `IStatusListManager` that returns the raw bytes. The W3C endpoint handler is refactored to call this accessor and gzip locally; the new IETF handler does the same and zlibs. The existing W3C on-the-wire output is byte-identical before and after this refactor (the gzip step moves from wherever it is now to a dedicated serialiser call).

**Alternative rejected.** Storing two pre-compressed byte arrays (one gzip, one zlib). Rejected because it doubles the in-memory storage and introduces a drift risk if the two ever get out of sync.

---

## R3. Dual-envelope byte identity

### Decision: SC-004 is enforced by a parametrised test over every list lifecycle operation

**Rationale.** The simpler dedupe is "store raw bytes once, serialise on demand with gzip for W3C and zlib for IETF". Decompress the output of both, compare, assert identity. A test at `tests/Sorcha.Blueprint.Service.Tests/Services/StatusListDualEnvelopeIdentityTests.cs` runs this assertion after:
- List creation with zero bits set
- Allocation of index N
- Setting bit N to 1 (revoke)
- Clearing bit N (reinstate)
- Mass allocation up to list capacity

All five scenarios produce identical decompressed bytes from both envelopes.

---

## R4. `status.status_list` credential claim

### Reference

IETF SD-JWT VC with IETF Token Status List uses a `status` claim at the top level of the SD-JWT payload:

```json
{
  "status": {
    "status_list": {
      "idx": 42,
      "uri": "https://deployment/api/v1/credentials/ietf-status-lists/{listId}"
    }
  }
}
```

### Decision: HAIP-path issuance embeds `status.status_list`; internal-path issuance keeps W3C `credentialStatus` (spec 093)

**Rationale.** Matches spec 095 FR-020, FR-022, FR-023. The choice is driven by issuance path, not by the Blueprint author. A credential carries exactly one claim form at a time.

**Consequence.** `CredentialEndpoints.IssueCredential` gains a new optional `StatusClaimForm` request field (enum: `W3cBitstringStatusListEntry` | `IetfTokenStatusList`). Defaults to the W3C form for backward compatibility with spec 093. The Blueprint-driven path explicitly requests the IETF form when the `TargetAudience` is `HaipExternalWallet` (coming in spec 097). Until spec 097 lands, the IETF form is not routinely used but the infrastructure is in place and testable.

---

## R5. List issuer signing key

### Decision: reuse the HAIP issuer co-key from spec 094

**Rationale.** The IETF TSL JWT envelope needs a classical signature. Spec 094 defines `HaipIssuerCoKeyService.GetSigningKeyForHaipIssuanceAsync(walletAddress)` which returns the appropriate classical signing material for a HAIP-issuer wallet. The same service is used here.

**Consequence.** `IetfTokenStatusListSerializer` takes an `IHaipIssuerCoKeyService` dependency (or a simpler signing delegate wrapping it) so the envelope is signed in lockstep with HAIP credential signing. Falls back to the wallet's primary classical key for wallets without the `HaipIssuer` flag — status list serving is not gated on HAIP capability.

**Alternative rejected.** Introducing a separate BIP32 purpose `sorcha:status-list-signing`. Rejected for simplicity — the list issuer and the credential issuer are the same organisation, so one key does both jobs.

---

## R6. Caching and invalidation

### Current state

The existing W3C endpoint uses a `Cache-Control: public, max-age=300` header. There is no server-side cache mutation on bit flips — clients honour the TTL.

### Decision: same caching model for the IETF endpoint; server-side envelope regeneration on every request (or short cache)

**Rationale.** Lifecycle operations (revoke, suspend) are rare relative to verification reads. Server-side regeneration on each request is fine for list sizes up to 131,072 entries (the W3C minimum) — gzip or zlib of ~16 KB is microsecond-level work. A tiny in-process cache (30-second TTL) can be added if profiling shows a bottleneck, but is not in scope for this spec.

**Consequence.** The IETF endpoint handler regenerates the envelope on each GET using the same cache-control header as W3C. Revocation is visible to clients after their cache TTL elapses (5 minutes default).

---

## Summary

All six research items resolved. Key decisions:

1. IETF TSL JWT uses `typ: "statuslist+jwt"`, payload contains `status_list: { bits, lst }` plus `iss`, `sub`, `iat`, `exp`, optional `ttl`.
2. Raw bitstring stored once in `StatusListManager`; gzip for W3C, zlib for IETF, compressed on-demand.
3. Byte identity enforced by parametrised test covering all lifecycle scenarios.
4. `status.status_list` claim form selected per-issuance via `StatusClaimForm` enum on the request DTO.
5. List envelope signing reuses `IHaipIssuerCoKeyService` from spec 094.
6. 5-minute cache TTL for the IETF endpoint matching W3C precedent.

Ready for Phase 1.
