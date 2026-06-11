# Unified Account Security Surface — Design

**Date:** 2026-06-10
**Status:** Design — approved in brainstorm, pending written-spec review
**Author:** Stuart Fraser (with Claude)
**Topic area:** Auth-method management (Feature 116 successor), 2FA channels, web + PWA parity

---

## 1. Context & problem

Account-security management already largely *exists* in the platform (Feature 116), but it is **fragmented and undiscoverable**, and the second-factor story is TOTP-only.

Ground truth as of this design (verified against the codebase, not memory):

- **The web app surfaces management today, but buried and split.** It lives inside `Settings` across two tabs:
  - *Accounts* tab → `PasswordSection`, `SocialLinksSection`, `PasskeysSection` (add / rename / remove, all wired, all step-up-gated, all protected by a last-sign-in-method floor).
  - *Security* tab → password + TOTP 2FA.
  Password straddles both; a user reasoning about "how do I sign in / how do I lock this down" has no single home, and there is **no top-level entry** — it is `Avatar → Settings → {one of two tabs}`.
- **The backend is mature.** ~16 endpoints (`/api/me/auth-methods` aggregate, `/api/auth/challenge/*` step-up, social link/unlink, passkey lifecycle, password lifecycle, TOTP), a server-authoritative `IAuthMethodService.WouldRemovingLeaveZeroAsync` floor, and a fully-wired typed UI client `IAuthMethodsClientService` (already in the shared `Sorcha.UI.Components.User` library).
- **The PWA (`/wallet`, consumer tier) has nothing** — just a stub page. Same `PlatformUser`, same backend, no UI. Under the F136 tier model a citizen is the *same person* on web and PWA, so this is a real parity hole.
- **Two step-up proofs are stubbed** — the challenge dialog implements TOTP + Password proofs; Passkey and Re-OAuth as *proof methods* are placeholder-messaged.
- **2FA is TOTP-only** (with backup codes). No email/SMS option for users who won't install an authenticator app.

The work is therefore **consolidation + discoverability + new channels + parity**, not net-new plumbing.

---

## 2. Goals & non-goals

**Goals (v1)**

1. One discoverable **Security** home, surfaced in the user menu, built **once** as a shared component and rendered **verbatim** on web (`/app`) and PWA (`/wallet`).
2. Add **Email OTP** and **SMS OTP** as honestly-labelled *lower-assurance* second factors.
3. Make admitting weak factors **safe** via an assurance-aware authorization rule (the "ladder floor").
4. Finish the stubbed **Passkey** + **Re-OAuth** step-up proofs so the surface is internally consistent.
5. Full **web ⇄ PWA parity** for every method.

**Non-goals (v1)**

- Sign-up-time method nudges / method choice during onboarding (raised, deliberately deferred to a follow-up spec).
- Mandatory-2FA **enforcement** (grace periods, org/platform policy gates). The design is *policy-ready* but ships optional-for-all.
- Recovery-email as a distinct channel. Recovery in v1 = backup codes only.
- Changing the wallet **device-delegation** model (F114/F128 "My Devices") — kept strictly distinct from login auth methods.

---

## 3. Decision log (locked in brainstorm)

| # | Decision | Rationale |
|---|----------|-----------|
| D1 | Email/SMS are **lower-assurance second factors** (not recovery-only, not first factors) | User choice; serves access for authenticator-averse users, with honesty + a floor rule to stay safe |
| D2 | **SMS is config-gated** (`ISmsSender` dormant until a provider is configured); **Email** uses existing F112 infra | Mirrors ACS-email auto-select; no vendor lock-in; per-install opt-in |
| D3 | **Job-based IA**: *How you sign in* / *Two-factor* / *Recovery* | Matches Google/GitHub/Microsoft account-security pages — recognition over learning |
| D4 | **Full PWA parity** via a shared `Sorcha.UI.Components.User` component | F122 convention; maximises reuse; one surface everywhere |
| D5 | **"Security" entry in the user menu, between *My Profile* and *My Devices*** | Discoverability is the cheapest high-impact win; no Settings detour |
| D6 | 2FA stays **optional** in v1, design **policy-ready** | Avoid enforcement complexity now without painting into a corner |
| D7 | Phone number lives on `PlatformUser`, **captured + verified at SMS-enable time** | Purely a security-surface concern; keeps signup untouched |
| D8 | Floor rule is **strict** (`proof tier ≥ target tier`), no relaxation; paired with **always-notify** | The single mitigation that makes weak-factor admission defensible |

---

## 4. Architecture — conceptual core

### 4.1 Assurance tiers (computed, not stored)

A static method→tier map drives every badge and the floor rule:

| Method | Role | Tier |
|--------|------|------|
| Passkey (WebAuthn) | Sign-in + satisfies 2FA | **Strongest** (phishing-resistant) |
| Authenticator (TOTP) | 2FA | **Strong** |
| Email OTP / SMS OTP | 2FA | **Basic** |
| Password | Sign-in (knowledge) | — (the base factor 2FA protects) |
| Social (OAuth) | Sign-in (delegated) | — (provider-controlled) |
| Backup codes | Recovery | Basic (one-time) |

Tier is a pure function of method kind — never persisted, so it cannot drift.

### 4.2 The ladder-floor rule — the security spine

Generalise today's `WouldRemovingLeaveZeroAsync` + `ScopedOperation` challenge into one **assurance-aware authorization policy**, server-authoritative:

> A step-up proof may authorise a destructive / downgrade operation on a method **only if the proof's tier ≥ the target method's tier.**

Consequences:
- An **Email/SMS (Basic) step-up can never** remove a passkey, disable TOTP, or change the password. This closes the "compromise the weak channel → strip the strong factor → full takeover" path that earns SMS/email-2FA their criticism.
- The existing **last-method floor** (`Total ≥ 1`) continues to apply *in addition*.
- The policy table is **code** (single source of truth, exhaustively unit-tested) and is surfaced to the UI as per-row `CanRemove` + `RequiredProofTier` flags on the existing aggregate read. **The UI never decides — it only reflects.**

Paired defense — **always-notify**: every security-state change (method added/removed/renamed, 2FA enabled/disabled, phone changed) writes an **F118 inbox entry** *and* sends a Sorcha-branded email ("A passkey was removed from your account"). An authorised-but-unexpected change is therefore always visible, even at the Basic tier. Inbox writes follow the F118 try / `LogError` / swallow rule — a notification failure must never roll back the security operation.

> **Edge case (documented, not relaxed):** to remove a Strongest method (passkey) the user must prove with a Strongest method (another passkey). If they hold only one passkey, the last-method floor already blocks removal; if they hold a passkey + want to remove it while keeping TOTP, the UI guides "verify with your passkey." We do **not** add a lower-tier fallback — that would reopen the downgrade hole. The UX cost is accepted in exchange for the assurance guarantee.

### 4.3 Multi-channel verification abstraction

Two OTP shapes exist and must not be force-merged:

- **Authenticator-generated (TOTP):** verify-only, no send step. Keeps its existing path.
- **Server-sent (Email / SMS):** generate → hash + store (Redis, short TTL, single-use via GETDEL) → send → verify, with rate-limit + attempt cap + expiry.

Introduce a thin channel abstraction consumed by **both** the login-2FA path and the step-up `IAuthChallengeService`:

```csharp
public interface IVerificationChannel
{
    ChallengeMethod Kind { get; }          // Totp | EmailOtp | SmsOtp | Passkey | ReOAuth | Password | BackupCode
    AuthAssuranceTier Tier { get; }        // Strongest | Strong | Basic
    Task<ChannelInitiation> InitiateAsync(PlatformUser user, ChallengeContext ctx, CancellationToken ct);
    Task<ChannelVerification> VerifyAsync(PlatformUser user, JsonElement proof, ChallengeContext ctx, CancellationToken ct);
}
```

- A **registry** resolves channels; the **SMS channel is registered only when `ISmsSender` is configured** — unconfigured ⇒ the option is absent from the registry, so it never renders and never validates.
- Login-2FA consumes the registry to challenge after a successful password/first-factor.
- Step-up consumes the same registry and layers the **floor rule** (`minTier` filter on which rungs it offers + server enforcement on verify).

`ServerSentOtpService` centralises generate / store / send / verify for the Email + SMS channels (rate-limit, attempt cap, expiry, single-use). `IEmailSender` already exists; `ISmsSender` is new and config-gated.

---

## 5. Information architecture & UX

### 5.1 The Security home (job-based)

```
User menu ▾
  My Profile
  Security          ← NEW, between My Profile and My Devices
  My Devices
  Open my wallet
  View Token
  Settings
  Log out
──────────────────────────────
Security

▾ How you sign in
   Password           Set ✓      Change ›
   Passkeys (2)       Strongest  Add ›
      iPhone · MacBook
   Social accounts    Google ✓   Add ›

▾ Two-factor authentication
   Authenticator      Strong     Enable ›
   Email code         Basic      Enable ›
   SMS code           Basic      (shown only if SMS configured)

▾ Recovery
   Backup codes (10 left)        View ›
```

Each method row carries an **assurance badge** (Strongest / Strong / Basic) and a server-driven `CanRemove` / `RequiredProofTier` state.

### 5.2 Menu placement (web)

In `Sorcha.UI.Core/Components/Shared/UserProfileMenu.razor`, insert between the *My Profile* item (current line 49–51) and the *My Devices* item (current line 52–56):

```razor
<MudMenuItem Icon="@Icons.Material.Filled.Security"
             OnClick="@(() => Navigation.NavigateTo("security"))"
             data-testid="user-menu-security">
    Security
</MudMenuItem>
```

Base-relative `NavigateTo("security")` — identical mechanism to the adjacent `"profile"` / `"my-devices"` items. The PWA does **not** use this component (it has its own nav); the PWA Security entry is added separately (§6).

### 5.3 Distinct from "My Devices"

The Security home's **Passkeys** = WebAuthn *login* authenticators. **My Devices** = F114/F128 wallet *holder-key delegation*. These are different systems and must stay verbally and visually distinct so a citizen never believes revoking a passkey unpairs their wallet (or vice-versa). Copy and iconography are chosen to reinforce the separation; the two surfaces never cross-link as if equivalent.

### 5.4 Routing & base-path (explicit — do not assume origin-root)

The web Blazor client (`Sorcha.UI.Web` + `.Web.Client`) is mounted under **`/app`** behind the gateway (`PathRemovePrefix`); the PWA under **`/wallet`**. This has bitten the codebase before (F128 PR #698, F149) when origin-root paths were assumed.

| Concern | Web (`Sorcha.UI.Web.Client`) | PWA (`Sorcha.Wallet.Pwa`) |
|---------|------------------------------|---------------------------|
| Page directive | `@page "/security"` → served at `…/app/security` | `@page "/security"` → served at `…/wallet/security` |
| In-app nav | `NavigateTo("security")` (base-relative) | `NavigateTo("security")` (base-relative — never `"/security"`) |
| API calls | `/api/*` via the gateway-proxied typed clients (existing base address) | same |

Rules:
- **Never** write an origin-absolute UI path for an in-app route. Use base-relative nav, matching the existing working menu items.
- New endpoints live under the already-proxied `/api/*` prefix; confirm any new sub-path is covered by the gateway's `/api` route (prefix match — it is) and add no new top-level prefix without a corresponding YARP route.

---

## 6. Components & file layout

**Relocation (F122):** the three sign-in sections live in `Sorcha.UI.Web.Client/Components/Settings/AuthMethods/` today (web-only). Move them into the shared library so the PWA can render them. The typed client `IAuthMethodsClientService` is *already* shared, so only the components move — and these are cleanly user-facing (no F122 bi-modal coupling risk).

```
Sorcha.UI.Components.User/Components/Security/
  SecurityHome.razor            ← job-based shell, mounted by both hosts
  SignInMethodsSection.razor    ← password + passkeys + social (relocated)
  TwoFactorSection.razor        ← TOTP + Email OTP + SMS OTP (new)
  RecoverySection.razor         ← backup codes
  AssuranceBadge.razor          ← Strongest / Strong / Basic chip
  AuthChallengeDialog.razor     ← relocated; Passkey + Re-OAuth proofs completed
```

**Hosts:**
- **Web:** new `@page "/security"` page hosting `<SecurityHome/>`; `Security` added to `UserProfileMenu.razor` (§5.2); Settings *Accounts* + *Security* tabs **retired**, with their existing deep-links (`/settings?tab=...`, `/settings/...`) 302/redirecting to `/security`.
- **PWA:** `@page "/security"` route hosting `<SecurityHome/>`, plus a Security entry in the PWA's own nav/settings. Validate the **social-account-linking OAuth round-trip** from inside the PWA (we already do social sign-in there, so it is a known quantity, but it is the one flow to prove).

---

## 7. Data model

- `PlatformUser` += `PhoneNumber` (E.164, nullable) + `PhoneVerifiedAt` (nullable). Captured/verified **at SMS-enable time**.
- 2FA channel enablement: `PlatformUserTwoFactor` 1:1 (or columns) carrying explicit flags — `TotpEnabled` (exists), `EmailOtpEnabled`, `SmsOtpEnabled`.
- Sent-OTP state: **Redis-backed, no EF migration** (matches `NonceStore` / `PreAuthCodeStore`): `{ codeHash, channel, purpose ∈ {login-2fa | step-up | phone-verify}, expiresAt, attempts }`, single-use via GETDEL.
- Assurance map + floor policy: **code**, static, exhaustively unit-tested — not data.

All audited storage registrations follow the F113 `IStorageRegistrationLog` pattern; the Redis OTP store is cache-style (not on the fail-fast audited list).

---

## 8. Endpoints

Extend the existing surface; new endpoints follow the **`/me/*` cross-tier `.RequireAuthorization()`** convention (F136) so **one surface serves both consumer and platform tokens**.

| Method | Path | Purpose |
|--------|------|---------|
| POST | `/api/totp/...` | *(unchanged)* |
| POST | `/api/me/2fa/email/enable` · `/verify` | Enable Email OTP (sends a live-inbox confirmation code) |
| POST | `/api/me/2fa/sms/phone` · `/verify` | Capture + verify phone (gated on `ISmsSender` configured) |
| POST | `/api/me/2fa/sms/enable` | Enable SMS OTP (requires `PhoneVerifiedAt`) |
| DELETE | `/api/me/2fa/{channel}` | Disable a 2FA channel (floor-rule + always-notify) |
| POST | `/api/auth/challenge/initiate` | *(extended)* sends Email/SMS code when that rung is chosen; honours `minTier`; completes Passkey + Re-OAuth |
| POST | `/api/auth/challenge/verify` | *(extended)* verifies the new channels + completed proofs |
| GET | `/api/me/auth-methods` | *(extended)* per-row `RequiredProofTier` alongside `CanRemove` |
| POST | `/api/auth/login` (+ 2FA step) | *(extended)* after first factor, challenge via strongest enrolled 2FA channel, with "use another method" fallback |

**Email OTP via F112:** the 2FA-code email is transactional and **must** route through `ITransactionalEmailService` (CLAUDE.md pattern #9) — add a typed `SendTwoFactorCodeAsync(TwoFactorCodeDispatch)` dispatch + a new Sorcha-branded Scriban template (`twofactor-code.html/.txt`) + snapshot fixtures. **No** per-org branding (security email, like verify/reset).

**SMS OTP via `ISmsSender`:** new abstraction in Tenant.Service, provider auto-selected on config (e.g. `Sms:AcsConnectionString` / `Sms:Provider`), mirroring `IEmailSender`'s SMTP/ACS selection. Plain-text body; code generation centralised in `ServerSentOtpService`.

---

## 9. Error handling & security

- **OTP:** per-user+channel sliding-window rate-limit, attempt cap → backoff, ~10-min expiry, single-use. **SMS** adds a per-phone send cap + cost guard.
- **Floor-rule violation** → `403` with a machine-readable reason code (e.g. `proof_tier_insufficient`).
- **Last-method floor** → existing `409`.
- **Provider send failure** (email/SMS down) → inline error; **never** locks the user out of their other 2FA methods.
- **SMS unconfigured** → option absent from the registry; if the endpoint is hit directly → `400`.
- **Always-notify** on every security-state change (inbox + email) as a standing defense.
- **Tier safety:** the floor rule is identical across tiers, so a citizen's credential-assurance story is preserved — email/SMS can never strip a passkey regardless of host.

---

## 10. Testing

- **Unit:** the assurance/floor policy as an **exhaustive matrix** (every proof-tier × target-tier × operation); `ServerSentOtpService` (rate-limit, attempt cap, expiry, single-use); channel registry (SMS **absent** when unconfigured); `ISmsSender` selection.
- **bUnit:** `SecurityHome` group rendering + assurance badges + `CanRemove` / `RequiredProofTier` gating; `AuthChallengeDialog` new rungs (Email/SMS) + completed Passkey/Re-OAuth.
- **Playwright (sorcha-ui skill):**
  - Web `/app/security`: enable Email OTP → log in with Email OTP; **remove-passkey blocked when only a Basic proof is available**; user-menu `Security` entry → `data-testid="user-menu-security"` → URL `…/app/security`.
  - PWA `/wallet/security`: parity smoke + social-link round-trip.
  - SMS path driven by a fake `ISmsSender`.
- **F112 snapshot fixtures** for the new `twofactor-code` template (regenerate via `UPDATE_EMAIL_FIXTURES=1`).

---

## 11. Delivery phasing (each independently shippable)

1. **Consolidation + shared shell** — relocate the three sign-in sections into `Sorcha.UI.Components.User`; build `SecurityHome` (job-based); add the user-menu `Security` entry + `/security` route; retire the Settings *Accounts*/*Security* tabs (redirect deep-links); finish the **Passkey + Re-OAuth** step-up proofs; ship assurance badges + the **assurance-aware floor rule** (widen the existing `CanRemove` inputs). *Delivers the top ask — discoverability — with zero new channels.*
2. **Email OTP** — `ServerSentOtpService` + email channel (F112 typed dispatch + template + fixtures); enable/verify; login-2FA + step-up integration as a Basic factor; always-notify; rate-limits.
3. **SMS OTP (config-gated)** — `ISmsSender` seam + ACS impl; phone capture + verify; `SmsOtpEnabled`; SMS rate/cost guards; registry hides when unconfigured.
4. **PWA parity** — mount `<SecurityHome/>` in the PWA, `/wallet/security` route + nav entry, validate the social-linking OAuth round-trip, keep distinct from My Devices, E2E.

---

## 12. Open questions / deferred

- **Mandatory-2FA policy** (org/platform enforcement, grace periods) — design is policy-ready; build deferred.
- **Recovery email as a distinct channel** — backup codes only in v1.
- **Sign-up-time method experience** — originally raised, deferred to a follow-up spec.
- **SMS provider default** — left to per-install config; no platform default shipped.
