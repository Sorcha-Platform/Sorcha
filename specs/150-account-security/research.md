# Phase 0 Research: Unified Account Security Surface

All major architectural decisions were settled in the approved design (`docs/superpowers/specs/2026-06-10-unified-account-security-design.md`). This document records the decisions plus the specific parameters and integration points pinned during planning. **No `[NEEDS CLARIFICATION]` remain.**

---

## R-001 — Assurance tiers as a static computed map

**Decision**: A pure method-kind → tier function: passkey = **Strongest**, TOTP = **Strong**, Email/SMS OTP = **Basic**; password and social are sign-in methods (no second-factor tier); backup codes = Basic (recovery). Implemented as `AssurancePolicy` constants in the Tenant Service — never persisted.

**Rationale**: Tier is intrinsic to the method, not user data; computing it removes any chance of drift between a stored tier and the actual method, and makes the floor-rule policy a pure function (trivially unit-testable as a matrix).

**Alternatives considered**: Persisting an assurance column per enrolled method (rejected — drift risk, migration cost, no benefit); deriving tier in the UI (rejected — server must own authorization, FR-008/FR-010).

## R-002 — Ladder-floor authorization policy (the security spine)

**Decision**: Generalise the existing `IAuthMethodService.WouldRemovingLeaveZeroAsync` into an assurance-aware policy: a step-up proof authorises a destructive/downgrade op on a target method only if `proofTier >= targetTier`. The full operation × target-tier table lives in `contracts/floor-rule-policy.md` and is enforced in `AuthChallengeEndpoints` (on verify) and surfaced as `CanRemove` + `RequiredProofTier` on `/api/me/auth-methods`. **Strict — no lower-tier fallback.**

**Rationale**: This is the single mitigation that makes admitting Basic factors defensible — it forecloses "compromise the weak channel → strip the strong factor → takeover". Keeping it strict (vs. a relaxed fallback) preserves the citizen-credential assurance story.

**Alternatives considered**: Relaxed fallback when no equal-tier proof exists (rejected — reopens the downgrade hole); per-operation bespoke checks scattered across endpoints (rejected — not a single source of truth, untestable as a whole).

## R-003 — One-time-code parameters

**Decision**: 6-digit numeric codes; **10-minute** expiry; **single-use** (consumed on first valid verify); **5 verification attempts** per code then invalidate; request rate-limit **5 sends per user per channel per 15 min** sliding window; SMS adds a **per-number cap of 5 sends/hour** and a global per-number daily cap. Codes stored **hashed** (not plaintext) in Redis with the TTL.

**Rationale**: 6 digits + short TTL + attempt cap is the NIST SP 800-63B / industry-standard envelope for OTP entropy vs. usability. Hashing at rest means a Redis read never exposes a live code. SMS caps bound cost and SIM-swap abuse.

**Alternatives considered**: 8-digit codes (rejected — marginal entropy gain, worse usability for a Basic factor); storing codes plaintext (rejected — constitution: encrypt/secure sensitive data at rest); per-IP rather than per-user limits (rejected — per-user+channel matches the authenticated surface).

## R-004 — Server-sent OTP state in Redis (no EF migration)

**Decision**: OTP challenge state (`{codeHash, channel, purpose, expiresAt, attempts}`) lives in Redis keyed by purpose+user, single-use via the GETDEL/atomic pattern already used by `NonceStore` / `PreAuthCodeStore`. Registered through F113 `IStorageRegistrationLog` as a **cache-style** store (warns on in-memory fallback, **not** on the fail-fast audited list).

**Rationale**: OTP state is ephemeral and self-expiring; a relational table would add migration + cleanup burden for no durability benefit. Reuses an established Sorcha pattern (and `Sorcha.AtomicCache` if a CAS guarantee is wanted).

**Alternatives considered**: EF-backed `OtpChallenge` table (rejected — needless migration + a cleanup job); in-memory only (rejected — breaks multi-replica Tenant).

## R-005 — Verification channel abstraction shared by login-2FA and step-up

**Decision**: `IVerificationChannel { Kind, Tier, InitiateAsync, VerifyAsync }` + `VerificationChannelRegistry`, consumed by **both** the login second-factor path and the step-up `IAuthChallengeService`. TOTP keeps its verify-only shape; Email/SMS use `ServerSentOtpService` (send-then-verify). The `ChallengeMethod` enum gains `EmailOtp`, `SmsOtp`.

**Rationale**: Login-2FA and step-up both need "enrolled methods + their tiers + initiate + verify"; one abstraction avoids duplicating channel logic and keeps tiers consistent across both. Step-up simply layers the floor-rule `minTier` filter on top.

**Alternatives considered**: Separate login-2FA and step-up channel stacks (rejected — duplication, drift); forcing TOTP into a send/verify shape (rejected — TOTP has no send step; a leaky abstraction).

## R-006 — SMS provider: config-gated `ISmsSender`, ACS first

**Decision**: New `ISmsSender` seam mirroring `IEmailSender`'s SMTP/ACS auto-selection. `AcsSmsSender` (Azure Communication Services SMS) is the first implementation, **selected only when `Sms:*` configuration is present**. When no provider is configured, the SMS channel is **not registered** → the SMS option is absent end-to-end (registry, endpoints return 400 if hit, UI row hidden).

**Rationale**: Matches the platform's existing ACS-email posture and per-install config philosophy (D2). No vendor lock-in; operators opt in. Absence-by-non-registration is the cleanest "feature off" (no dead UI, no reachable endpoint).

**Alternatives considered**: Twilio as default (rejected — second vendor; ACS already in the stack for email); a feature-flag boolean with a stub sender (rejected — a stub that silently no-ops is worse than the option not existing).

## R-007 — Email OTP + change-alert via F112 transactional pipeline

**Decision**: Two new Sorcha-branded Scriban template pairs — `twofactor-code.{html,txt}` (the login/enable code) and `security-change.{html,txt}` (the always-notify alert) — added under `Sorcha.Tenant.Service/Emails/Templates/`, dispatched via new typed records `TwoFactorCodeDispatch` and `SecurityChangeDispatch` on `ITransactionalEmailService`. Committed snapshot fixtures, regenerated with `UPDATE_EMAIL_FIXTURES=1`. **No per-org branding** (security email, like verify/reset).

**Rationale**: CLAUDE.md pattern #9 mandates all transactional mail through `ITransactionalEmailService` with templates, never string-interpolated HTML. The snapshot-fixture discipline catches accidental copy/branding changes.

**Alternatives considered**: Ad-hoc `IEmailSender.SendAsync` with an inline body (rejected — violates F112); reusing the `reset` template (rejected — different content + the always-notify alert is a distinct message type).

## R-008 — Always-notify: F118 inbox + email on every change

**Decision**: `SecurityChangeNotifier` writes an F118 durable inbox entry (via `TenantSecurityInboxWriter`) **and** sends the `security-change` email on every security-state change (add/rename/remove/enable/disable, phone change). Wrapped in `try`/`LogError`/swallow — a notification failure never rolls back the security op (FR-011).

**Rationale**: With Basic factors admitted, an out-of-band alert is the backstop that makes an unexpected-but-authorised change visible to the real owner. F118 is the durable bell-drawer; email reaches the user off-session.

**Alternatives considered**: Inbox-only (rejected — misses off-session users); email-only (rejected — not durable/cross-session); making notification transactional with the change (rejected — a mail/inbox outage must not block a legitimate security change).

## R-009 — Completing the Passkey + Re-OAuth step-up proofs

**Decision**: **Passkey proof** reuses the existing FIDO2 assertion flow (`/api/passkey/assertion/*` shape) scoped to a challenge nonce; a successful assertion yields a Strongest-tier proof. **Re-OAuth proof** re-runs the social flow with a `stepup` intent and verifies the returned identity matches a *linked* social account, yielding the social method's tier. Both replace the current placeholder messaging in `IAuthChallengeService` + `AuthChallengeDialog`.

**Rationale**: Reuses proven WebAuthn + OAuth machinery rather than inventing new proof transports; closes the ladder so a passkey-only or social-only user can actually satisfy a high-tier step-up.

**Alternatives considered**: Leaving them stubbed (rejected — a passkey-only user couldn't remove a passkey, which is the exact high-tier case the floor rule needs to support); a bespoke step-up WebAuthn ceremony (rejected — duplicates the existing assertion endpoints).

## R-010 — Login-2FA method selection when multiple enrolled

**Decision**: After the first factor, offer the user's **strongest enrolled** 2FA method by default, with a "use another method" affordance listing the rest. Email/SMS initiate a send on selection; TOTP prompts directly.

**Rationale**: Defaulting to strongest nudges good security without trapping users who've lost their strong factor; the fallback keeps the Basic channels useful as intended.

**Alternatives considered**: Always prompt the last-used method (rejected — can default to the weakest); let the user pre-set a default (deferred — not needed for v1, adds settings surface).

## R-011 — Phone number storage

**Decision**: Store `PhoneNumber` as **E.164** plaintext on `PlatformUser` alongside `PhoneVerifiedAt`, captured at SMS-enable time. Flag column-level encryption as a **follow-up** (consistent with how email is stored today).

**Rationale**: The SMS sender needs the cleartext number to dispatch, so it can't be hashed; storing E.164 matches the existing email-as-plaintext posture. Encrypting the column is a worthwhile hardening but is parity-neutral and deferrable.

**Alternatives considered**: Hashing the number (rejected — can't send to a hash); a separate `UserContact` table (rejected — 1:1 with the user, no multiplicity need in v1).

## R-012 — Relocation approach for the shared components (F122)

**Decision**: Move `PasswordSection`, `SocialLinksSection`, `PasskeysSection`, `AuthChallengeDialog` from `Sorcha.UI.Web.Client/Components/Settings/AuthMethods/` into `Sorcha.UI.Components.User/Components/Security/`, preserving namespaces via the library's `RootNamespace = Sorcha.UI.Core` (so consumer `using`s are stable). The typed client `IAuthMethodsClientService` is **already** shared, so no service-layer move is needed.

**Rationale**: These are cleanly user-facing components (none of the F122-Phase-2 bi-modal coupling — that pain was admin/governance interfaces, not these). Relocating once lets both hosts render the same surface (D4/FR-025).

**Alternatives considered**: Duplicating components per host (rejected — two surfaces to maintain, parity drift); leaving them in Web.Client and referencing across (rejected — the PWA must not depend on the web SPA; F122 places shared user components in the dedicated library).

## R-013 — Endpoint placement & tier

**Decision**: New 2FA-channel endpoints at `/api/me/2fa/{email,sms}/*` (cross-tier `/me/*`, plain `.RequireAuthorization()` per F136). Existing TOTP endpoints stay at `/api/totp/*` (no churn). Extend `/api/me/auth-methods` (aggregate) and `/api/auth/challenge/*` (step-up) in place.

**Rationale**: `/me/*` is the F136 cross-tier convention, so one surface serves both consumer (citizen) and platform tokens — required for web+PWA parity. Moving TOTP would be a needless breaking change.

**Alternatives considered**: Tier-specific duplicated endpoints (rejected — defeats the single-surface goal); relocating TOTP under `/api/me/2fa/totp/*` for tidiness (rejected — breaking change for zero functional gain; documented inconsistency accepted).
