# Contract: Pre-session social-link step-up challenge

A thin, **unauthenticated** entry that lets the holder of a valid link-pending token run the existing
step-up challenge against the target account, scoped to `ScopedOperation.LinkSocial`. Delegates to the
unchanged `IAuthChallengeService`. The authenticated `/api/auth/challenge/*` endpoints are untouched.

Rate limit: `RateLimitPolicies.PlatformAuth` ("platform-auth"). No bearer required; the link-pending
token is the principal.

## POST /api/auth/social/link/challenge/initiate

**Request**
```json
{ "linkPendingToken": "<opaque>", "preferredMethod": "Passkey|Totp|Password|ReOAuth|null" }
```

**Behaviour**
1. `TryVerify(linkPendingToken)` → 401 if invalid/expired.
2. Build `ChallengeContext(PlatformUserId = targetAccountId, UserIdentityId = <target's identity>)`.
3. `IAuthChallengeService.InitiateAsync(ctx, ScopedOperation.LinkSocial, preferredMethod, targetMethodKind: null)`.

**Responses**
| Status | When | Body |
|--------|------|------|
| 200 | Method prepared | `{ "method": "...", "payload": <method-specific|null> }` |
| 400 | No enrolled method (bootstrap edge) | problem |
| 401 | Link-pending token invalid/expired | — |
| 429 | Rate limited | — |

## POST /api/auth/social/link/challenge/verify

**Request**
```json
{ "linkPendingToken": "<opaque>", "method": "Passkey", "proof": { /* method-specific */ } }
```

**Behaviour**
1. `TryVerify(linkPendingToken)` → 401 if invalid/expired.
2. Same `ChallengeContext` as initiate.
3. `IAuthChallengeService.VerifyAsync(ctx, method, ScopedOperation.LinkSocial, proof)`.

**Responses**
| Status | When | Body |
|--------|------|------|
| 200 | Proof accepted | `{ "challengeToken": "<opaque>", "expiresInSeconds": N }` |
| 401 | Proof rejected / token invalid | — |
| 403 | `ProofTierInsufficient` (FR-010 floor; e.g. bare password when 2FA enrolled) | `proof_tier_insufficient` |
| 429 | Rate limited | — |

## Invariants
- FR-009: standard proof methods (passkey, re-auth linked social, password, 2FA) apply via the
  existing ladder.
- FR-010: proof strength = strongest the account has, never more than configured (Decision 5).
- The issued challenge token is single-use, 5-minute, scoped to `LinkSocial`, bound to the target
  `PlatformUserId`.
