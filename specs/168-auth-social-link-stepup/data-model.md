# Phase 1 Data Model: Step-Up-Gated Social Account Linking

This feature adds **no database tables**. It adds one stateless token, one enum value, and one
outcome; it reuses existing persisted entities (`PlatformSocialLogin`, `AuthChallengeToken`,
`PlatformUser`) unchanged.

---

## New: Link-pending token (`LinkPendingToken`)

A signed, short-lived, **stateless** credential meaning "this verified social identity matched this
existing account; linking is pending proof of the existing account." Not persisted (FR-004).

| Field | Type | Notes |
|-------|------|-------|
| `Provider` | string | Social provider key, e.g. `google`, `microsoft`, `github`, `apple`. |
| `Subject` | string | Provider's stable subject id for the social identity. |
| `SocialEmail` | string | Verified email asserted by the provider (the matched email). |
| `DisplayName` | string? | Display name from the social profile (may be null). |
| `TargetAccountId` | Guid | `PlatformUser.Id` of the existing account the email matched. |
| `ExpiresAt` | DateTimeOffset | UTC expiry, ~5 minutes after mint. Enforced server-side. |

**Wire form**: `base64url(payload) | unixSeconds(expiresAt) | hex(HMAC-SHA256)`. The HMAC covers the
payload **and** the expiry, so tampering with either fails verification (FR-004).

**Signing key**: 32 bytes, `HKDF-SHA256(ikm = JwtSettings:SigningKey, info =
"sorcha:tenant:link-pending-hmac:v1")` — deployment-stable, replica-stable, distinct from the 2FA
login-token key. Held by a `LinkPendingTokenKey` singleton (mirrors `LoginTokenSigningKey`).

**Validation rules**:
- Reject if signature does not match (constant-time compare) → invalid (FR-004, FR-015).
- Reject if `ExpiresAt` is in the past → expired (FR-015, FR-016, US4).
- Reject if malformed / absent → invalid (FR-015).
- `TargetAccountId` must resolve to an existing `PlatformUser` at confirm time; otherwise reject.

**Lifecycle**: minted on the social callback LinkRequired branch → presented once at link-confirm →
discarded. No revocation list; expiry + the single-use paired challenge proof bound replay.

---

## New: `ScopedOperation.LinkSocial`

Add one value to the existing `ScopedOperation` enum
(`src/Services/Sorcha.Tenant.Service/Models/AuthChallengeEnums.cs`):

```
LinkSocial = 5,   // Step-up proof for connecting a new social identity to an existing account.
```

A challenge token issued for `LinkSocial` is only valid at link-confirm; a proof scoped to any other
operation is rejected (FR-008, US2 scenario 4). No change to `ChallengeMethod` or `AuthAssuranceTier`.

---

## New: LinkRequired outcome on the social resolve result

`ResolveOrCreateSocialUserAsync` currently returns `ResolveSocialUserResult(User, IsNew, SocialLoginRefusal)`.
Add a **LinkRequired** signal so the callback can branch without issuing a session. Two viable shapes
(decide in tasks — both are non-breaking to the two unchanged paths):

- **Option A (preferred)**: add `LinkRequired` to a result discriminator and carry the matched
  `PlatformUser` (target) + claim through, so the callback mints the token. Keeps `SocialLoginRefusal`
  semantics ("no user, here's why") distinct from "user matched but link is gated."
- **Option B**: add `SocialLoginRefusal.LinkRequired` plus the target account id on the result.

Either way the result must surface: `Provider`, `Subject`, `SocialEmail`, `DisplayName`,
`TargetAccountId`. **No session is issued on this branch** (FR-001, FR-002, SC-001).

---

## Reused (unchanged) entities

### `PlatformSocialLogin` (persisted)
The durable `(Provider, Subject) → PlatformUser` link. Created **only** by
`ISocialLinkService.LinkAsync` at successful link-confirm — never on the callback branch anymore.
Collision semantics preserved (`SocialLinkOutcome`): `Linked`, `AlreadyLinkedToCaller` (idempotent
success), `AlreadyLinkedToDifferentUser` + `EmailCollision` (→ 409). (FR-012, SC-006.)

### `AuthChallengeToken` (persisted, hashed)
Issued by `IAuthChallengeService.VerifyAsync`, single-use, 5-minute, scoped to a `ScopedOperation` and
bound to a `PlatformUserId`. Reused verbatim for `LinkSocial`; link-confirm consumes it and asserts its
`PlatformUserId == LinkPendingToken.TargetAccountId` (FR-007).

### `PlatformUser` (persisted)
The target existing account. Resolved at confirm time by `TargetAccountId`. Its primary
`UserIdentity` supplies the `UserIdentityId` for the pre-session `ChallengeContext`.

---

## State transitions

```
social callback (unconnected (provider,subject), verified social email
                 matches existing verified account)
        │
        ▼
  LinkRequired  ──mint──▶  link-pending token (TTL ~5m)   [NO session]
        │
        │  (US4: abandon / expire → no link, no session, account unchanged)
        ▼
  pre-session step-up challenge (ScopedOperation.LinkSocial, target account)
        │  verify → single-use challenge token (bound to target PlatformUserId)
        ▼
  POST link-confirm  { link-pending token + X-Auth-Challenge }
        │
        ├─ token invalid/expired ............................. 401, no change
        ├─ no/invalid proof .................................. 401, no change
        ├─ proof account ≠ target / wrong op / wrong tier .... 403, no change
        ├─ LinkAsync collision ............................... 409, no link
        └─ success → LinkAsync(Linked) + issue session ....... 200, linked + signed in
```
