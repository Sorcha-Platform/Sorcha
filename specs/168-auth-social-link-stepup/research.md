# Phase 0 Research: Step-Up-Gated Social Account Linking (B-Backend)

All Technical-Context unknowns are resolved below. The feature reuses existing Tenant Service
subsystems; the research is about *how to wire them* for a pre-session linking flow, not about new
technology choices.

> Source design doc `docs/superpowers/specs/2026-06-27-auth-login-hardening-design.md` (Workstream
> B-backend) is referenced by the spec but is **not present in this worktree**. The decisions below
> are reconstructed from the spec + the existing code (verified by reading the Tenant Service).

---

## Decision 1 — Where the silent auto-link happens, and what replaces it

**Decision**: Cut the auto-link at `PlatformUserService.ResolveOrCreateSocialUserAsync` **Step 2**
(`src/Services/Sorcha.Tenant.Service/Services/PlatformUserService.cs:302–348`). Today, when an
unconnected `(provider, subject)` has a verified email matching an existing verified account, that
branch calls `LinkSocialLoginAsync(...)` and returns `ResolveSocialUserResult(existingByEmail, …,
None)` → caller issues a session. Replace the `LinkSocialLoginAsync` call with a new **LinkRequired**
result that carries the matched **target account id** (and the social claim fields) up to the callback,
which mints a link-pending token instead of a session.

**Rationale**: This is the single, exact site of the vulnerability. The two surrounding branches —
Step 1 (already linked → direct sign-in) and Step 3 (no match → create/refuse) — are left untouched,
satisfying FR-013/FR-014/SC-004 by construction. The verified-email gates (lines 313, 322) stay as
the entry condition, so unverified-either-side still refuses exactly as today (Edge Cases).

**Alternatives considered**: Branch in the callback/endpoint layer instead of the service — rejected,
because the email-match decision and both `EmailVerified` gates already live in the service; duplicating
them in the endpoint would risk drift and re-expose the hole on one path.

---

## Decision 2 — Link-pending token: stateless HMAC, reusing the Feature 146 key pattern

**Decision**: The link-pending token is a self-contained, HMAC-SHA256-signed string (claims `|`
expiry `|` signature), signed with a 32-byte key derived once via **HKDF-SHA256 from
`JwtSettings:SigningKey`** with a **new distinct info label** `sorcha:tenant:link-pending-hmac:v1`.
Add `ResolveLinkPendingTokenSigningKey()` to `TenantSecretKeyResolver` and a `LinkPendingTokenKey`
singleton — mirroring `ResolveLoginTokenSigningKey()` + `LoginTokenSigningKey`
(`TenantSecretKeyResolver.cs:119`, `:140`). No new persistence (FR-004). TTL ~5 minutes
(`DateTimeOffset` expiry embedded and enforced server-side).

**Token claims** (FR-003): `provider`, `subject`, `socialEmail`, `displayName`, `targetAccountId`
(PlatformUser.Id), `expiresAt`. Verification recomputes the HMAC in constant time and rejects on any
mismatch or past-expiry (FR-004, FR-015).

**Rationale**: The spec explicitly says to reuse the deployment-stable HMAC approach of the short-lived
login token (Assumptions). A distinct `info` label gives domain separation from the 2FA login token so
the two token types can never be cross-validated. Stateless ⇒ no DB, no cleanup, no replica coordination;
expiry + the single-use challenge proof bound replay risk (Edge Cases).

**Alternatives considered**: (a) JWT — heavier, and the codebase already standardised short-lived
intermediate tokens on raw HMAC, so a JWT would be an inconsistent new shape. (b) Persisted
nonce/handle — rejected: violates the "no new persistent storage" assumption and adds cleanup burden
for a ~5-minute artifact.

---

## Decision 3 — Pre-session step-up: derive ChallengeContext from the token, reuse IAuthChallengeService

**Decision**: Add a thin **pre-session challenge entry** (new endpoints under
`/api/auth/social/link/challenge/{initiate,verify}`) that accepts the **link-pending token** in place
of a bearer, validates it, and builds the `ChallengeContext(PlatformUserId = targetAccountId,
UserIdentityId = <resolved from target account>)`, then delegates to the **unchanged**
`IAuthChallengeService.InitiateAsync` / `VerifyAsync` with the new `ScopedOperation.LinkSocial`. The
existing `/api/auth/challenge/*` endpoints (which `.RequireAuthorization()` and resolve context from
bearer claims — `AuthChallengeEndpoints.cs:33,46,114`) are **not modified**.

**Rationale**: This is the core architectural tension: the link flow is *pre-session* (no JWT was
issued — that is the whole point), but the existing challenge initiate/verify require a bearer. Rather
than weaken the authenticated surface (e.g. making auth optional there), a dedicated pre-session entry
keeps the security-sensitive authorization on the existing endpoints intact while still reusing the
challenge *service* (ladder, proof methods, single-use token, floor rule) verbatim — satisfying FR-009
("surface it through the existing challenge mechanism") and the Assumption that the ladder/service are
reused unchanged.

`UserIdentityId` is needed because TOTP/passkey state is keyed per-identity. Resolve it from the target
PlatformUser's primary identity (same lookup `ResolveContextAsync` falls back to via
`IIdentityRepository.GetUserByIdAsync`, inverted: PlatformUser → its identity).

**Alternatives considered**: (a) Make `/api/auth/challenge/*` accept the link-pending token as an
alternate principal — rejected: blurs the authenticated-only contract of those endpoints and risks the
token being accepted where a real session is expected. (b) Issue a provisional/limited session for the
target account so the existing endpoints "just work" — rejected: that *is* a session for an unproven
account, reopening the very hole being closed.

---

## Decision 4 — Link-confirm: bind the proof to the token's target account

**Decision**: New endpoint `POST /api/auth/social/link/confirm`. Input: the link-pending token (body)
+ the challenge token via the existing `X-Auth-Challenge` header. Sequence:
1. Verify link-pending token (signature + expiry) → `targetAccountId`, social claim (FR-015).
2. Consume the challenge token for `ScopedOperation.LinkSocial` and assert its bound
   **PlatformUserId == targetAccountId** (FR-006, FR-007, FR-008). Mismatch / wrong-operation /
   missing → reject (401/403, no link).
3. Call `ISocialLinkService.LinkAsync(targetAccountId, provider, subject, email, displayName)` →
   map `SocialLinkOutcome` (`Linked` / `AlreadyLinkedToCaller` → success; `AlreadyLinkedToDifferentUser`
   / `EmailCollision` → **409 Conflict**) (FR-012, SC-006, Edge Cases).
4. On success issue the **same** session as a normal social sign-in via `ITokenService` with the same
   tier/audience derivation the callback uses (FR-011, SC-003).

**Rationale**: The challenge token is already single-use and bound to a `PlatformUserId`
(`IAuthChallengeService.VerifyAsync` persists a hashed, scoped token). Asserting equality of that bound
account against the link-pending token's target is the FR-007 safety property that makes US1 meaningful.
Reusing `LinkAsync` inherits the collision rules unchanged (FR-012) — no new merge logic.

**Status-code policy (FR-018)**: missing/invalid proof → **401**; proof valid but wrong account /
wrong operation / wrong tier → **403**; link-time collision → **409**; expired/tampered/absent
link-pending token → **401**. Responses must not reveal target-account existence beyond what the social
flow already exposes.

**Alternatives considered**: Pass the challenge token in the body alongside the link-pending token —
rejected for consistency: every other gated mutation in the service presents the proof via
`X-Auth-Challenge` (see `RequireAuthChallengeAttribute`), so link-confirm follows the same convention.

---

## Decision 5 — FR-010 proof policy maps onto the existing ladder/floor rule

**Decision**: Add `ScopedOperation.LinkSocial` to `AuthChallengeEnums.cs`. The FR-010 policy
(passkey ✓; re-auth linked social ✓; password-only ✓ when no 2FA; password **and** 2FA required when
2FA enrolled; never demand more than configured) is exactly what the existing ladder
(`TOTP → Password → Passkey → ReOAuth`) + `AssurancePolicy` floor rule already produce when initiated
against the target account: the ladder offers the strongest enrolled method, and "2FA enrolled ⇒ TOTP
selected over password" enforces the password-insufficient-when-2FA clause without new policy code.

**Rationale**: The challenge service already "selects the strongest available proof per the ladder"
and re-checks the floor on verify (`ChallengeVerificationOutcome.ProofTierInsufficient`). Mapping the
policy onto the existing mechanism (rather than re-implementing it) is the Assumption and keeps SC-005
testable as a five-config matrix against one code path.

**Open verification for Phase 1/tasks**: confirm that for an account with **password + 2FA**, the
ladder does not let a bare password proof satisfy `LinkSocial`. If the ladder alone doesn't guarantee
the "password ∧ 2FA" composite, the floor for `LinkSocial` must be set so a password proof is rejected
when 2FA is enrolled. This is captured as a policy assertion in `SocialLinkStepUpPolicyTests` rather
than a new subsystem.

---

## Decision 6 — Telemetry, rate limiting, surfaces

**Decision**:
- **Telemetry (FR-017)**: extend `SocialLoginMetrics` with the new outcomes — `link_required` (on the
  callback branch), and link-confirm `success` / `conflict` / `rejected` tags on the existing
  `sorcha_social_login_*` counter. No PII tags (provider + reason only).
- **Rate limiting (FR-018)**: apply the existing `RateLimitPolicies.PlatformAuth` ("platform-auth")
  policy to both the pre-session challenge entry and link-confirm, matching the rest of the social/auth
  surface.
- **Session issuance (FR-011)**: reuse the callback's existing `ITokenService` path and tier derivation
  — web surface ⇒ `Tier.Platform`, citizen-wallet surface ⇒ `Tier.Consumer` — so the issued
  session/JWT is identical to a normal social sign-in (Assumption, SC-003).
- **Callback outcome surfacing**: `SocialCallback.cshtml.cs` (Razor, user-facing redirect) and the JSON
  `/api/auth/social/callback` endpoint both return the LinkRequired outcome + the link-pending token to
  the client; the user-facing prompt that consumes it is **out of scope** (Workstream B-UI).

**Rationale**: All four are existing, named mechanisms; reusing them keeps the change surgical and the
new behaviour observable and consistent with platform conventions.

---

## Resolved unknowns summary

| Unknown | Resolution |
|---------|-----------|
| Where to break the auto-link | `PlatformUserService.ResolveOrCreateSocialUserAsync` Step 2 (Decision 1) |
| Token signing | HMAC-SHA256, HKDF-from-JWT key, new info label, stateless (Decision 2) |
| Pre-session step-up | New thin entry derives `ChallengeContext` from token; reuse `IAuthChallengeService` (Decision 3) |
| Proof↔token binding | Challenge token's `PlatformUserId` must equal token's `targetAccountId` (Decision 4) |
| Proof strength policy | `ScopedOperation.LinkSocial` on existing ladder/floor (Decision 5) |
| Status codes / replay | 401/403/409 policy; single-use challenge + ~5-min token expiry (Decisions 2, 4) |
| Telemetry / rate limit / session | Extend `SocialLoginMetrics`; `PlatformAuth` policy; reuse `ITokenService` (Decision 6) |
