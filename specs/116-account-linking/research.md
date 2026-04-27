# Phase 0: Research — Feature 116 Account Linking

**Status**: All architectural unknowns resolved during the prior brainstorming session. The locked decisions live in `docs/superpowers/specs/2026-04-27-account-linking-design.md` (committed `ded4218c`). This file consolidates them in research-decision format and captures the alternatives that were considered and rejected.

## Decision summary (from brainstorming Q1–Q6)

### R-001 — OAuth email-collision policy on link

**Decision**: Reject the link if the OAuth provider's returned email already belongs to another `PlatformUser` (HTTP 409 with a clear user message). Allow the link if the email is free.

**Rationale**: Standard pattern — what GitHub, Google, Microsoft, and most consumer SaaS products do. Avoids the multi-week scope explosion of a real account-merge feature, which would touch `UserIdentity`, `PlatformUserOrgMembership`, `PlatformUserPersona`, and wallet ownership.

**Alternatives considered**:
- *Hard reject regardless of free / collision* — surprising for users with two accounts; offers no path forward.
- *Account merge on collision* — correct long-term but is its own multi-week feature; deferred. See spec §Out of Scope.

### R-002 — Re-authentication gating profile

**Decision**: Asymmetric. Adds run unguarded (already-signed-in user is expanding their own access). Removes and password change require a fresh re-authentication challenge. Renames run unguarded.

**Rationale**: Matches GitHub and Google. Strict gating on every operation trains users to dismiss prompts; lax gating lets a hijacked session quietly prune the legitimate owner's recovery methods.

**Alternatives considered**:
- *Strict on every add and remove* — desensitises users to challenge prompts; high friction for low-risk additions.
- *Lax — only password change gated* — leaves the hijacked-session pruning attack open.

### R-003 — Re-authentication challenge ladder

**Decision**: Per-user method selection in priority order: TOTP if 2FA enabled → current password if set → WebAuthn step-up against an existing passkey → re-OAuth via a still-linked provider.

**Rationale**: Picks the strongest enrolled factor; degrades only when the stronger factor is not available. The user can switch methods from the dialog if multiple are enrolled.

**Alternatives considered**:
- *Always demand the strongest factor regardless of enrolment* — locks out users who have not enrolled it.
- *Let user always pick* — adds a click; the default-to-strongest behaviour matches user expectation.

### R-004 — Last-method floor

**Decision**: Hard floor — at least one of `{password, social link, active passkey}` must remain. UI disables the destructive action on the last surviving method; server re-enforces in the same transaction as the mutation. TOTP enrolment does not count as a method.

**Rationale**: Sorcha-specific blast radius — a locked-out `PlatformUser` loses access to org-derived wallets, persona vault, citizen-wallet enrolments, and any in-flight credentials. Self-inflicted lockout via misclick is unacceptable.

**Alternatives considered**:
- *Hard floor + verified-email recovery escape hatch* — requires a "create fresh password from no-password state" flow we do not have today; subtle UX (can remove last password, sign back in via email reset).
- *Soft warning only* — incompatible with the lockout blast radius.

### R-005 — Audit retention on user-initiated remove

**Decision**: Hybrid. Soft-delete passkeys (`Status = Revoked`, set `DisabledAt`, set `DisabledReason = "user-removed"`). Hard-delete `PlatformSocialLogin` rows.

**Rationale**: Passkey records carry forensic weight — `SignatureCounter`, `AaGuid`, `AttestationType` are exactly what an incident responder reconstructing "what authenticated as this user, when" needs. `PlatformSocialLogin` rows have no equivalent — the meaningful audit trail lives in the OAuth provider's own logs, not in the link record.

**Alternatives considered**:
- *Hard-delete both* — loses passkey forensic trail.
- *Soft-delete both* — pointlessly retains social-link rows whose only meaningful evidence lives at the provider.

### R-006 — Settings tab placement

**Decision**: Add **Accounts** as the first (leftmost) tab. Rename existing **Connections** tab → **Service Profiles** (icon swap `Dns` → `Cable`); body unchanged.

**Rationale**: The user-requested first-tab placement maximises discoverability; the "Connections" rename removes a real naming collision (current tab is service-profile config, not auth connections).

**Alternatives considered**:
- *Add "Accounts" alongside existing "Connections" with no rename* — two similar-sounding tabs, maximally confusing.
- *Use the label "Sign-in methods" instead of "Accounts"* — technically accurate but reads as a 2008-era pref panel; loses the friendly framing.
- *Fold into Security tab as a section* — buries the feature; users do not look in Security for "manage my linked Google account".

### R-007 — OAuth link-vs-login dispatch

**Decision**: Same callback URL across all OAuth flows (`POST /api/auth/social/callback`). Dispatch on an `intent: "login" | "link"` field encoded into the server-signed `state` parameter. When `intent=link`, `state` also carries the target `PlatformUserId`.

**Rationale**: Canonical OAuth pattern — `state` is exactly the spec mechanism for binding intent through the round-trip. Avoids doubling redirect URIs across all four providers across every environment (real ops cost). HMAC-signing `state` makes tampering detectable.

**Alternatives considered**:
- *Separate `/callback-link` URL* — doubles per-environment provider console configuration.
- *Infer intent from session cookie at callback* — dangerous: a user might be signed in in one tab and click "Sign in with Google" on the public landing page in another expecting login, accidentally linking instead.

## Implementation-detail decisions inside the design (no further alternatives)

These are recorded in the design doc and not litigated here:

- **Challenge token persistence**: Postgres-only (Tenant DB). Redis was considered but 5-minute lifetime + low volume + value of audit trail favours Postgres.
- **Token shape**: opaque base64url, 32 bytes random; stored as `SHA-256(token)` server-side; `X-Auth-Challenge` header transport.
- **Token scope**: bound to one `ScopedOperation` enum value at issue; cross-operation reuse rejected at the filter.
- **Token lifetime**: 5 minutes. One-shot; second consume returns 401.
- **Cleanup cadence**: `AuthChallengeTokenCleanupService` BackgroundService, daily tick, prune `ExpiresAt < now() - 7 days`. 7-day forensic window.
- **Last-method floor enforcement**: single helper `IAuthMethodService.WouldRemovingLeaveZero(...)` invoked inside the mutation transaction with `SELECT ... FOR UPDATE` on `PlatformUser`; same helper populates `canRemove` in `/api/me/auth-methods` so UI and server share one source of truth.
- **Squash policy**: pre-release, so `AuthChallengeToken` table goes into the existing `20260425152258_InitialCreate` migration rather than a new one. Procedure documented in design §3.3.

## Best-practices research

Applied directly from the prior brainstorming session and existing project patterns; no fresh external research needed.

- **OAuth account linking**: aligned with Auth0 / Okta / Clerk multi-method patterns; rejection-on-collision is the modal industry choice.
- **Re-authentication challenge for sensitive operations**: aligned with GitHub's "sudo mode" and Google's "recent authentication" pattern; both use short-lived per-operation tokens.
- **Passkey forensics retention**: aligned with NIST SP 800-63B credential-lifecycle guidance — revocation is recorded, not erased.
- **Last-method floor**: pattern from password manager UX (1Password, Bitwarden) — never let the user remove their last access path through normal UI.

## NEEDS CLARIFICATION

None. The brainstorming session resolved every load-bearing decision; the spec contains zero `[NEEDS CLARIFICATION]` markers; the requirements quality checklist passes all items.
