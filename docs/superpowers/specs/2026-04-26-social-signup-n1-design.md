# Social Signup on n1 — Production-Leaning Public Signup Design

**Date:** 2026-04-26
**Author:** Brainstorm with Stuart Fraser
**Status:** Approved for planning
**Scope tag:** B (deploy/config + policy fixes). C-bucket items captured as backlog.

## Goal

Make public-user social signup work end-to-end on `n1.sorcha.dev` with two real
OAuth providers (Google + GitHub), and lock in the policy decisions that affect
security and identity provenance, before n1 starts taking real signups from
organic public traffic.

## Background

Sorcha's Tenant Service already contains the full OAuth2/OIDC plumbing for
Google, Microsoft, GitHub, and Apple — `SocialLoginService` with PKCE for
OIDC providers, state CSRF, well-known endpoint defaults, claim extraction,
`PlatformSocialLogin` table for `(provider, sub)` linking, `SocialCallback`
Razor page that auto-creates `PlatformUser` and `UserIdentity` in the public
org, welcome-email dispatcher integration (feature 112), and authenticated
provider-link endpoint.

**Feature 114 dependency.** As of master commit `d0cdd55a` (Citizen Wallet
PWA, #420), social signup is also the entry point for the citizen wallet
flow — wallet recovery is anchored on `PlatformUser` identity, so a Google
or GitHub sign-in becomes the gateway to enrolling a wallet device and
holding verifiable credentials. This elevates the security importance of
the strict link policy (REQ-2): the threat model is no longer just "account
takeover" but "wallet impersonation against a verifiable-credential
holder." The unverified-existing-account hijack scenario must be closed.

The plumbing is in good shape; what is missing is:

1. **Real OAuth client IDs/secrets** — `appsettings.json` ships
   `"SocialProviders": []`. n1 currently cannot do social login because no
   provider is configured.
2. **A live bug in the redirect URI** — `SocialLoginEndpoints.cs` sets
   `redirect_uri` to `/api/auth/social/callback-redirect`, which has no
   handler. The Razor page that *does* handle the callback lives at
   `/auth/social/callback`. Today a real user would 404 after Google's
   redirect.
3. **Policy gaps that affect security** — the existing
   `ResolveOrCreateSocialUserAsync` links by email match without checking
   either side's verification state, allowing an unverified-existing-account
   hijack scenario.
4. **Operational defaults that block fresh n1 deploys** —
   `PublicOrgEnabled = false` is hard-coded in the DB seed, so a freshly-reset
   n1 has social login disabled until an admin manually flips it.

## Scope

### In scope

- Fix the redirect-URI mismatch (single per-env URL at `/auth/social/callback`)
- Capture provider `email_verified` claim and act on it
- Strict link policy: refuse cross-method linking unless both sides verified
- Refuse new-user signup when provider asserts `email_verified=false`
- Refresh `PlatformUser.DisplayName` from provider `name` claim each login
- Provider buttons render only for configured providers (REQ-1)
- `PublicOrgEnabled` seed value becomes config-driven
- `docker-compose.n1.yml` + `.env` additions for Google + GitHub
- `n1-deploy.ps1` / setup runbook documents OAuth-app registration
- xUnit tests covering the policy decisions
- Refusal-message UX in the existing `SocialCallback` error rendering

### Out of scope (backlog)

- BACKLOG-1: provider email-change conflict UX (drift option C from brainstorm)
- BACKLOG-2: Microsoft provider (next; needs work-vs-personal account policy)
- BACKLOG-3: Apple provider (requires JWT-based `client_secret` refactor —
  current code uses static form-param secret, which Apple does not accept)
- BACKLOG-4: Consumer Persona attribute model (feature 092 territory)
- BACKLOG-5: Azure Key Vault for OAuth secrets at first Kubernetes deployment
- BACKLOG-6: Google OAuth consent-screen real-publisher verification when
  organic n1 traffic exceeds the 100-user test-mode cap
- BACKLOG-7: Existing-`PlatformSocialLogin`-row audit before any non-reset
  rollout of the strict-link policy. n1 is reset on this deploy so all
  rows are fresh under the new policy; if the policy is later applied
  to an environment that has accumulated history, those rows carry no
  link-time `EmailVerified` evidence and would all pass Scenario A
  (returning user, no re-check) silently. Surfaced by claude-review on
  PR #423.

## Decisions

| ID | Decision |
|---|---|
| REQ-1 | Signup buttons render only for providers configured with non-empty `ClientId` and `ClientSecret`. Driven by `Model.AvailableProviders` set in `SignupModel.OnGet`. |
| REQ-2 | Strict link policy. Both sides must be verified. Provider must assert `email_verified=true` (or be GitHub with primary-verified email); existing user must have `EmailVerified=true`. New-user signup refused when provider says unverified. |
| REQ-3 | Refresh `PlatformUser.DisplayName` from provider `name` claim on every successful login. Email never refreshed in this scope (BACKLOG-1). |
| REQ-4 | `DemoEnvironment.Enabled=true` on n1, default `false` elsewhere. Already wired in `docker-compose.n1.yml:55`; verify deployed. |
| REQ-5 | OAuth client secrets in `/opt/sorcha/.env` on the n1 host (hand-created on first deploy, gitignored). `docker-compose.n1.yml` references via `${GOOGLE_OAUTH_CLIENT_ID}` etc. KV migration deferred to first Kubernetes deployment (BACKLOG-5). |
| REQ-6 | Single redirect URI per env at `https://n1.sorcha.dev/auth/social/callback`. Fixes a live bug in `SocialLoginEndpoints.cs` (was `/api/auth/social/callback-redirect`, no handler). `SocialCallback.cshtml.cs` resolves provider from cached state-data, not query string. |
| REQ-7 | Stay `ASPNETCORE_ENVIRONMENT=Development` on n1 for now. Switch to `Staging` in ~1 week (`/schedule` reminder) to activate feature 113 storage fail-fast. Switching requires a one-line compose edit + verifying all six audited interfaces resolve to Postgres/Redis backends. |
| REQ-8 | `PlatformSettings__SeedPublicOrgEnabled` env var read by `DatabaseInitializer` at seed time only. n1's compose sets `true`. Once seeded, admin UI/API toggles take precedence. |
| REQ-9 | OAuth-app setup steps documented in `docs/guides/SOCIAL-LOGIN-SETUP.md` plus a pointer from the `n1-deploy` skill. Actual key generation + paste happens collaboratively at deploy time, not autonomously. |

## Architecture

No new services. No new database tables. No new endpoints. The user-visible
flow is unchanged; we are fixing a bug, hardening policy, and making
configuration drive what was previously hard-coded.

```
┌──────────────────────────┐
│  Signup.cshtml (Razor)   │  ← buttons driven by Model.AvailableProviders
└──────────┬───────────────┘
           │  POST /api/auth/social/initiate
           ▼
┌──────────────────────────┐
│  SocialLoginEndpoints    │  ← redirect_uri to /auth/social/callback (BUG FIX)
└──────────┬───────────────┘
           │  caches {provider, codeVerifier, redirectUri} keyed by state
           ▼
┌──────────────────────────┐
│  SocialLoginService      │  ← reads email_verified claim; honour it
└──────────┬───────────────┘
           │  user → Google/GitHub → consent → redirect back
           ▼
┌──────────────────────────────────┐
│  SocialCallback.cshtml (Razor)   │  ← reads provider from cached state
└──────────┬───────────────────────┘
           ▼
┌──────────────────────────┐
│  PlatformUserService     │  ← strict link policy: both sides verified
│  ResolveOrCreate…        │  ← refresh DisplayName each login
└──────────┬───────────────┘
           │
           ▼  WelcomeEmailDispatcher fires (existing) → /app/#token=…
```

## Data flow & policy

`SocialAuthCallbackResult` gains an `EmailVerified` field:

```csharp
record SocialAuthCallbackResult(
    bool Success, string? Error,
    string? Subject, string? Email, string? DisplayName,
    bool EmailVerified,      // NEW
    string Provider);
```

Population rules:

| Provider | `EmailVerified` source |
|---|---|
| Google / Microsoft / Apple | `email_verified` claim from ID token (preferred) or userinfo response. **Default `false`** when claim absent. Never assume verified. |
| GitHub | True only when `/user/emails` returns the primary email with `verified: true`. Existing logic; surfaced explicitly on the result. |

### Scenario A — Returning user (`PlatformSocialLogin` row exists)

Resolve `PlatformUser` by `(provider, subject)`. Update
`PlatformSocialLogin.LastUsedAt` and `PlatformUser.LastLoginAt`. Refresh
`PlatformUser.DisplayName` from claim if non-empty (REQ-3). Issue token. We
do **not** re-check `EmailVerified` here — the link is already trusted; a
provider's transient state should not lock out a genuine returning user.

### Scenario B — Existing `PlatformUser` matched by email, no provider link yet

Strict gate (REQ-2):

- Provider `EmailVerified == true` AND
- Existing `PlatformUser.EmailVerified == true`

If both true → call `LinkSocialLoginAsync`, update last-used timestamps,
refresh display name, issue token.

If either false → return failure with a specific message:

- Existing user not verified:
  *"An account exists for this email but isn't verified. Sign in with your
  password and verify your email first, or recover access at /auth/login."*
- Provider unverified:
  *"Your `<provider>` account hasn't verified this email address. Please
  verify it with `<provider>` and try again."*

Refusal renders as a 400 in the `SocialCallback` page error banner — no
silent redirect, no auto-upgrade of either side.

### Scenario C — No `sub` link, no email match (genuinely new user)

Provider `EmailVerified == false` → refuse with the provider-unverified
message above. No `PlatformUser` is created.

Provider `EmailVerified == true` (or GitHub with primary-verified) →

1. `CreateAsync` new `PlatformUser` with `EmailVerified=true,
   EmailVerifiedAt=now`
2. `LinkSocialLoginAsync` to attach `PlatformSocialLogin`
3. Existing flow takes over: create `UserIdentity` in `WellKnownIds.PublicOrgId`
   with `Roles=[Consumer]`, `ProvisionedVia=SocialLogin`
4. Add `PlatformUserOrgMembership` if missing
5. Fire `IWelcomeEmailDispatcher.SendIfPendingAsync` (existing wiring)
6. Issue JWT, redirect to `/app/#token=…&refresh=…`

## Code changes

### `Sorcha.Tenant.Service.Models.Dtos.SocialLoginDtos`

- Add `bool EmailVerified` to `SocialAuthCallbackResult` record.

### `Sorcha.Tenant.Service.Services.SocialLoginService`

- `ParseIdTokenClaims` returns `email_verified` boolean from JWT payload.
- `FetchUserInfoClaimsAsync` returns `email_verified` from userinfo response.
- `ExtractGitHubClaimsAsync` sets `EmailVerified = true` only when the primary
  email entry has `verified: true` (current behaviour, surfaced explicitly).
- `ExchangeCodeAsync` propagates `EmailVerified` into `SocialAuthCallbackResult`.
- Add `IReadOnlyList<string> GetConfiguredProviderNames()` to
  `ISocialLoginService` for REQ-1.

### `Sorcha.Tenant.Service.Services.PlatformUserService`

- `ResolveOrCreateSocialUserAsync` signature gains `bool emailVerified` (or
  takes the full `SocialAuthCallbackResult` — pick during planning). Policy
  gate per Scenario B+C. Returns a discriminated result with shape
  `(PlatformUser? User, bool IsNew, SocialLoginRefusal? Refusal)` where
  `SocialLoginRefusal` is an enum identifying `ProviderUnverified` /
  `ExistingUnverified` so the callback page can render the matching copy
  without string matching.
- Each successful resolution refreshes `PlatformUser.DisplayName` from the
  claim if claim is non-empty and differs.

### `Sorcha.Tenant.Service.Endpoints.SocialLoginEndpoints`

- Two redirect_uri call sites change from `/api/auth/social/callback-redirect`
  to `/auth/social/callback` (lines 99 and 262). The new path matches the
  Razor page route.

### `Sorcha.Tenant.Service.Pages.Auth.SocialCallback.cshtml.cs`

- `OnGetAsync` no longer takes `provider` from the query — the OAuth provider
  doesn't preserve query parameters across redirect, so the current behaviour
  was relying on a query-string fragment that doesn't exist. Provider is
  resolved from cached state inside `ExchangeCodeAsync`. The result already
  carries `Provider`, so the page just consumes it.
- Apply Scenario B/C refusal rendering: set `ErrorMessage` to the appropriate
  copy and return `Page()`.

### `Sorcha.Tenant.Service.Pages.Auth.Signup.cshtml(.cs)`

- `SignupModel.OnGet` populates `Model.AvailableProviders` from
  `ISocialLoginService.GetConfiguredProviderNames()`.
- Razor view renders one button per entry in `Model.AvailableProviders`
  (currently hard-coded to all four). Buttons map provider name to the JS
  click handler.
- Remove dead JS in the social-login click handler (`redirectUri`, `nonce`,
  `state`, `sessionStorage.setItem`) — none of it is sent to the server. The
  CSRF state parameter is generated server-side in
  `SocialLoginService.GenerateAuthorizationUrlAsync` and validated there on
  callback; the JS-side computation is misleading dead weight.

### `Sorcha.Tenant.Service.Data.DatabaseInitializer`

- Read `IConfiguration.GetValue<bool>("PlatformSettings:SeedPublicOrgEnabled",
  false)` when seeding `PlatformSettings`. Used only on a fresh DB; admin
  toggles persisted in DB take precedence on subsequent boots.

## Configuration

### `.env.example`

```bash
# Social login OAuth credentials (n1 only — leave blank for local dev)
GOOGLE_OAUTH_CLIENT_ID=
GOOGLE_OAUTH_CLIENT_SECRET=
GITHUB_OAUTH_CLIENT_ID=
GITHUB_OAUTH_CLIENT_SECRET=
```

### `docker-compose.n1.yml` (`tenant-service.environment` additions)

```yaml
SocialProviders__0__Name: Google
SocialProviders__0__ClientId: ${GOOGLE_OAUTH_CLIENT_ID}
SocialProviders__0__ClientSecret: ${GOOGLE_OAUTH_CLIENT_SECRET}
SocialProviders__1__Name: GitHub
SocialProviders__1__ClientId: ${GITHUB_OAUTH_CLIENT_ID}
SocialProviders__1__ClientSecret: ${GITHUB_OAUTH_CLIENT_SECRET}
PlatformSettings__SeedPublicOrgEnabled: "true"
```

`DemoEnvironment__Enabled: "true"` is already at line 55 — no change needed,
verify on deploy.

## OAuth-app registration runbook

A new doc at `docs/guides/SOCIAL-LOGIN-SETUP.md` captures the per-provider
steps. Summary:

**Google** — at `console.cloud.google.com`:

1. Project: "Sorcha n1" (or reuse existing).
2. APIs & Services → OAuth consent screen → External; scopes
   `openid email profile`; add `n1.sorcha.dev` to authorized domains.
3. Credentials → Create OAuth 2.0 Client ID → Web application.
4. Authorised redirect URIs:
   - `https://n1.sorcha.dev/auth/social/callback`
   - `https://localhost:7110/auth/social/callback` (optional, dev)
5. Copy Client ID + Secret → `/opt/sorcha/.env` on n1.

**GitHub** — at `github.com/settings/developers`:

1. New OAuth App. Name "Sorcha n1". Homepage `https://n1.sorcha.dev`.
2. Authorisation callback URL: `https://n1.sorcha.dev/auth/social/callback`.
3. Generate client secret → `/opt/sorcha/.env` on n1.

## Deploy procedure

After this PR merges and Docker Publish completes (10–15 min):

1. SSH to n1 once to seed `.env`:

   ```
   ssh <ssh-user>@<n1-host>
   cd /opt/sorcha
   nano .env  # paste the four OAuth values
   chmod 600 .env
   ```

2. Full reset to pick up new images, fresh DB seed, fresh `PlatformSettings`:

   ```powershell
   .\scripts\n1-reset.ps1 -ResourceGroup sorcha-n1-uk -UpdateCompose -Yes
   ```

3. Verify:
   - `https://n1.sorcha.dev/auth/signup` shows demo banner + Google + GitHub
     buttons (no Microsoft, no Apple)
   - "Continue with Google" → consent → land logged in at Sorcha.UI
   - DB check: `select email, "EmailVerified" from tenant.platform_users` —
     verified true; `select * from public.platform_social_logins` — one row
   - Welcome email visible in Mailpit (or real mailbox if SMTP configured)

4. Repeat with GitHub.

## Testing

Policy decisions are the high-value tests; OAuth wire mechanics are largely
framework code already exercised. New / extended xUnit classes in
`tests/Sorcha.Tenant.Service.Tests/`:

| Test class | What it covers |
|---|---|
| `SocialLoginPolicyTests` (new) | Refusal when provider says `email_verified=false`; refusal when existing user not verified; success when both verified; GitHub primary-verified-only path; returning-user `DisplayName` refresh; returning-user `LastUsedAt` update |
| `SocialLoginEndpointsTests` (extend) | redirect_uri value contains `/auth/social/callback` (regression guard) |
| `SignupModelTests` (extend) | `AvailableProviders` reflects configured providers; empty list renders no buttons |
| `DatabaseInitializerTests` (extend or new) | `PublicOrgEnabled` seeds `true` when `PlatformSettings:SeedPublicOrgEnabled=true`; defaults `false` |
| `SocialAuthCallbackResultTests` (new) | `EmailVerified` populated from ID-token claim; defaults `false` when claim missing |

Mock the HTTP client at the `IHttpClientFactory` boundary (existing pattern in
the test project). No real OAuth round-trips in CI.

### Manual smoke (n1 post-deploy)

1. Fresh n1 reset with social config → `/auth/signup` shows demo banner + 2
   buttons.
2. Google new-user signup → lands logged in, welcome email arrives.
3. Google second login → `LastUsedAt` updated, `DisplayName` refreshed if
   changed in Google profile.
4. GitHub user with non-verified primary email → refused with the
   unverified-provider message.
5. Email-collision: register `test@gmail.com` with password, do not verify,
   then "Continue with Google" with same Google account → refused.
6. Email-collision happy path: same as 5 but verify the email first → "Continue
   with Google" links the account, login succeeds.

## Telemetry

New counter on `Sorcha.Tenant` meter:

```
sorcha_social_login_refusal_total{provider, reason}
  reason ∈ {provider_unverified, existing_unverified,
            state_invalid, code_exchange_failed}
```

Surfaces unexpected refusal volume — useful when an OAuth-app config drifts or
a provider changes verified-email semantics.

Refusals also log at `LogWarning` with `provider`, `reason`, and a
hash-based redacted email tag. No PII in plaintext.

## Risks & open items

| Risk | Plan |
|---|---|
| Sorcha.UI fragment-token parsing untested in this scope | Verify during smoke step 2. If broken, scope grows by one fix to Sorcha.UI startup auth handler. Likely fine — passkey/email login also redirect to `/app/#token=…`. |
| `SocialProviders` env-var array binding edge cases (.NET requires `__0__`/`__1__`) | Surfaces immediately — empty `_providers` dict means empty button list on signup page. Spotted in smoke step 1. |
| `PlatformSocialLogins` already has rows from prior n1 testing | Full reset wipes volumes; a non-reset deploy preserves them, which is correct behaviour. |
| Google consent-screen "verification" warning at scale | Test mode (≤100 users) is fine for demonstrator. BACKLOG-6 captures real-publisher verification when traffic justifies. |
| GitHub `/user/emails` rate limit (5000/hr authenticated) | Well within signup-rate budget for a demo node. |
| Forgetting to flip `Staging` per REQ-7 | `/schedule` reminder set when this design is approved for planning. |
| `Storage:AllowInMemoryInProduction` bypass tempting if `Staging` flip trips fail-fast | Do not use it as a workaround. Fix the missing connection string. The bypass exists for ephemeral CI smoke tests, not production-leaning environments. |
| Feature 114 adds `citizen-wallet` + `citizen-verifier` images to n1's pull set on next reset (~500MB extra) | n1's 29GB disk + the pre-deploy `docker image prune -a -f` step in the n1-deploy skill comfortably handles this. Confirm in the deploy step before pulling. |
| Feature 114 PlatformUserDevice migration is the new baseline | Our spec adds no migrations — REQ-8 reads config at seed time. No conflict, but planning phase must verify no schema changes sneak in. |

## Deploy collaboration model

The deploy is **not** autonomous:

1. PR ships with code changes + `.env.example` + `docker-compose.n1.yml`
   updates + `docs/guides/SOCIAL-LOGIN-SETUP.md`.
2. Stuart provisions OAuth apps at Google + GitHub (or together with Claude
   driving a step-by-step session).
3. Joint SSH session to n1 to paste `.env` values.
4. Claude drives `n1-reset.ps1` + smoke walkthrough; Stuart watches the auth
   flows in a browser to confirm UX.

## Follow-ups

- `/schedule` agent in ~7 days to flip `ASPNETCORE_ENVIRONMENT` to `Staging`
  on n1 once the social-login dust settles.
- BACKLOG-2 (Microsoft) becomes the natural next phase once Google + GitHub
  are stable in production traffic — Microsoft adds the work-vs-personal
  account policy decision that's worth designing with real signup data in
  hand.
- BACKLOG-4 (Consumer Persona attribute model) re-enters when there's a need
  beyond "verified email" — likely tied to feature 092 work or a credential
  flow that wants to surface social-claim-backed attributes.
