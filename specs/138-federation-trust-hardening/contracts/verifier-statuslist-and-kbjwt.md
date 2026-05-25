# Contract: Verifier — Status-List Verification & KB-JWT Expiry (US1, US5)

**Component**: `Sorcha.Verifier.Engine`

## US1 — Status-list signature verification contract

`StatusListCache` MUST verify a fetched status-list JWT before trusting any revocation bit.

```
trust(statusList) ⇔
    signatureValid(statusList, key)                 # key from IIssuerKeyResolver, sealed-state anchored
  ∧ statusList.iss == expectedOrgDid                 # FR-002 issuer pinning
  ∧ now < statusList.exp (within skew)               # FR-004 freshness, no +24h default
```

| Condition | Outcome |
|-----------|---------|
| signature valid + issuer pinned + fresh | list trusted; result cached |
| signature invalid | **reject**; credential treated as unverifiable (fail closed) |
| `iss` ≠ expected org DID | **reject** (even if signature internally valid) |
| issuer key unresolved | **reject** |
| list expired | **reject** |
| fetch failed | **reject** — MUST NOT serve stale cache (removes current fail-open at `StatusListCache.cs:88-96`) |

- Resolver: reuse `IIssuerKeyResolver` (`ResolveAsync(issuer, kid, ct) → JWK?`), injected into `StatusListCache` (new dependency). Composite = DID-backed (production) then JWK-registry (demo, dev-only).
- Publisher (`CitizenStatusListPublisher`) adds a `kid` header; verifier matches by `kid`, falling back to first published verification method matching `alg` when `kid` absent (pre-release back-compat).
- Caller (`VerifiablePresentationValidator.IsRevokedAsync` path) treats "unverifiable" as **fail** (not "unknown ⇒ allowed").

## US5 — KB-JWT expiry contract

`VerifiablePresentationValidator` (currently checks nonce + aud at `:196-206`, no `exp`) MUST additionally:

```
accept(kbJwt) requires
    kbJwt.exp present                                 # FR-017, missing ⇒ reject
  ∧ now ≤ kbJwt.exp + ClockSkewSeconds                # FR-018 wall-clock check via injected TimeProvider
  ∧ nonce == session.nonce ∧ aud == session.clientId  # existing
```
- Ordering: KB-JWT `exp` validated **before** delegation/status validation, so a replayed proof for a since-revoked credential cannot pass.
- Revocation re-checked at verify time (already at `:400-413`); combined with US1 fail-closed this satisfies FR-019 (mid-session revocation fails verification).
- Skew: shared `Verifier:ClockSkewSeconds` (default 60), applied consistently to KB-JWT `exp`, delegation `exp`, and US2 heartbeat freshness.
