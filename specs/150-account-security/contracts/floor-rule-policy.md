# Contract: Assurance-Aware Floor-Rule Authorization Policy

**Owner**: `AssurancePolicy` (Tenant Service) — the single server-authoritative source. Enforced on every step-up `verify`; surfaced read-only to the UI as per-row `CanRemove` + `RequiredProofTier`. The UI never decides (FR-008/FR-010).

**Core rule (FR-007)**: a step-up proof authorises a destructive/downgrade operation on a target method **iff** `proofTier >= RequiredProofTier(operation, target)` **and** the last-sign-in-method floor (FR-006) is not breached. **Strict — no lower-tier fallback** (R-002).

---

## Table A — Proof method → assurance tier

| Proof method | Tier | Note |
|--------------|------|------|
| Passkey (WebAuthn assertion) | **Strongest** | Phishing-resistant. |
| Authenticator (TOTP) | **Strong** | |
| Password (re-entry) | **Basic** | ✅ **T061 resolved (2026-06-11): password is Basic.** A password is a phishable knowledge factor. The hard guarantee holds and strengthens — a Basic proof can never disable TOTP (Strong) or remove a passkey (Strongest). |
| Social re-authentication (Re-OAuth) | **Strong** | Proves current control of a *linked* IdP account. |
| Email OTP | **Basic** | |
| SMS OTP | **Basic** | |
| Backup code | **Basic** | Recovery proof; single-use. |

> "Proof tier" is the tier of whatever method the user satisfies the step-up with. The number of distinct proof options offered in the dialog is filtered to those with `tier >= RequiredProofTier`.

## Table B — Operation × target → required proof tier

The required tier equals **the assurance being removed/weakened** ("you can't use a weaker thing to take away a stronger protection"). Since the password is **Basic** (T061), its own change/remove are Basic-gated by this same rule.

| Operation | Target method | Required proof tier | Loses (rationale) |
|-----------|---------------|---------------------|-------------------|
| Remove | Passkey | **Strongest** | Removes a Strongest factor. Satisfiable by asserting the very passkey being removed (assert → then delete). |
| Remove / Disable | Authenticator (TOTP) | **Strong** | Removes Strong protection. A Basic password can't. |
| Change | Password | **Basic** | Rotates a Basic (knowledge) factor — re-enter your password to change it; no dead-end for password-only users (T061). |
| Remove | Password | **Basic** | Removes a Basic factor; + last-method floor. |
| Unlink | Social account | **Basic** | Removes a delegated sign-in path of no particular strength; + last-method floor. |
| Disable | Email OTP | **Basic** | Removes a Basic factor. |
| Disable | SMS OTP | **Basic** | Removes a Basic factor. |
| Change | Phone number (while SMS enabled) | **Strong** | Redirects where SMS codes land — treated as a Strong-protected change to prevent code-redirect hijack. |
| View / Regenerate | Backup codes | **Strong** | Exposes/rotates a recovery login path that backs the Strong factor. |

## Table C — Operations with NO floor gate

| Operation | Why ungated |
|-----------|-------------|
| Add passkey / Add social / Enable Email or SMS / Set initial password (bootstrap) | Adding a method does not weaken existing protection. Channel ownership is still proven by the enable-time verification code (email/SMS) or the WebAuthn/OAuth ceremony itself; setting the first password when none exists is the existing bootstrap path. |
| Rename passkey | Non-destructive metadata edit. |

## Derived UI signals (on `/api/me/auth-methods`)

For each method row the server emits:

- `AssuranceTier` — from Table A (badge).
- `RequiredProofTier` — from Table B for the row's primary destructive op (Remove/Disable/Unlink).
- `CanRemove` — `true` iff (a) removal keeps `≥ 1` sign-in method (FR-006) **and** (b) the user currently holds at least one enrolled proof method of `tier >= RequiredProofTier`. When `false`, the row's Remove control is disabled with a reason (`last-method` | `needs-stronger-proof`).

## Worked invariants (must be covered by the exhaustive matrix test)

1. A user whose only step-up-capable proof is Email OTP (Basic) **cannot** remove a passkey, disable TOTP, change the phone, or view/regenerate backup codes. They **can** disable Email/SMS OTP, unlink a social (subject to last-method), and — since T061 made the password Basic — change/remove the password (Basic-gated; equivalent to the existing email-reset flow, always-notified). The load-bearing guarantee is unchanged: **a Basic proof can never reach a Strong (TOTP) or Strongest (passkey) operation.**
2. A passkey-only user **can** remove a passkey (assert-then-delete) but the last-method floor blocks removing their *only* sign-in method.
3. Disabling a 2FA channel never breaches the last-method floor (2FA channels are not sign-in methods) but is still always-notified.
4. Every removal/downgrade that passes the gate emits an in-app inbox entry **and** an email (FR-009), best-effort (FR-011).
