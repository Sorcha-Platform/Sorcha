# Research — Feature 115 Social Signup

**Feature**: Public Social Signup on n1 (Google + GitHub)
**Date**: 2026-04-26
**Phase**: 0 — Outline & Research

The feature description has no `NEEDS CLARIFICATION` markers. The
companion design doc (`docs/superpowers/specs/2026-04-26-social-signup-n1-design.md`)
already encodes the technical decisions reached during brainstorming.
Research below confirms the standards and prior-art the design relies
on, so planning has solid ground.

---

## R-1: Provider verified-email semantics

**Decision**: Capture `email_verified` from the OIDC ID token (Google,
Microsoft, Apple) and from the `verified` flag on the GitHub `/user/emails`
primary entry. Default to `false` when the claim is absent.

**Rationale**:

- **Google** publishes `email_verified` in every ID token issued for the
  `email` scope. Google verifies user emails before issuing tokens for
  Google Workspace and consumer accounts. Reference: Google Identity
  Platform OIDC spec — `email_verified` is a Boolean claim that
  indicates whether the email address has been verified by Google.
- **Microsoft Entra ID** publishes `xms_edov` (email-domain-owner-verified)
  for work/school accounts and `email_verified` in the v2.0 ID token. For
  personal Microsoft accounts (consumer), `email_verified` may be
  `false` because Microsoft does not always re-confirm consumer email
  addresses. The strict policy (FR-010) means we will refuse those
  tokens until the user has verified at Microsoft. (Microsoft is out of
  scope for this feature; this is research for BACKLOG-2.)
- **Apple** issues `email_verified` in the ID token and additionally
  publishes `is_private_email`. We respect `email_verified=true`
  regardless of relay status. (Apple is out of scope for this feature.)
- **GitHub** does not implement OIDC or issue ID tokens. `/user/emails`
  returns an array, each entry with `primary` and `verified` Boolean
  flags. The current `ExtractGitHubClaimsAsync` already filters for the
  primary verified entry; we surface that as `EmailVerified=true` on
  the result.

**Alternatives considered**:

- *Trust the userinfo endpoint*. Some providers populate `email_verified`
  in userinfo but not in the ID token (or vice versa). Best practice is
  to prefer the ID token because it is signed and bound to the
  authentication event, then fall back to userinfo. Existing code
  follows this order; we extend it to read the new claim, not change
  the order.
- *Default to `true` when claim absent*. Rejected — violates strict
  policy and creates an unbounded trust surface.
- *Maintain a per-provider trust matrix in code*. Considered but
  rejected as over-engineering for two providers. The provider's claim
  is the source of truth; if a provider has known weaknesses (e.g.
  Microsoft personal accounts), the per-account claim still reports
  `false` and the gate works correctly without bespoke per-provider
  rules.

**References**:

- Google: https://developers.google.com/identity/openid-connect/openid-connect#id_token-payload
- Microsoft: https://learn.microsoft.com/en-us/entra/identity-platform/id-token-claims-reference
- Apple: https://developer.apple.com/documentation/sign_in_with_apple/tokenresponse
- GitHub: https://docs.github.com/en/rest/users/emails

---

## R-2: .NET configuration array binding for `SocialProviders`

**Decision**: Use the standard zero-indexed double-underscore syntax
`SocialProviders__0__Name=Google`, `SocialProviders__0__ClientId=…`,
`SocialProviders__0__ClientSecret=…`, with a second provider at index
`__1__`. The existing `SocialLoginService` constructor already calls
`configuration.GetSection("SocialProviders").Get<List<SocialProviderConfig>>()`
which honours this binding.

**Rationale**:

- This is the canonical .NET Configuration mechanism for binding a
  `List<T>` from environment variables. Documented in the
  `Microsoft.Extensions.Configuration.EnvironmentVariables` provider
  reference.
- The existing `SocialLoginService` is already configuration-driven; the
  `_providers` dictionary is built at construction time from the bound
  list. No code change needed for binding semantics — only for the
  surface that exposes "which providers are configured" to the UI
  layer (FR-001).
- Empty `ClientId` or `ClientSecret` strings cause the provider to be
  registered in the dictionary but with non-functional credentials.
  We must filter those out at the `GetConfiguredProviderNames` layer to
  honour FR-002 (no greyed-out buttons).

**Alternatives considered**:

- *Comma-separated single env var*. Rejected — non-standard, would
  require custom parsing.
- *JSON blob in a single var*. Rejected — secrets would be embedded in
  a single string, harder to rotate, harder to read.
- *Per-provider env var prefixes (e.g., `GOOGLE_OAUTH_*`)*. Rejected as
  the binding target — but **adopted as the .env file naming**
  (REQ-5), then mapped through compose-file interpolation
  (`${GOOGLE_OAUTH_CLIENT_ID}`) to the canonical
  `SocialProviders__0__ClientId` config key. This gives operators a
  readable `.env` while keeping the .NET-side configuration shape
  standard.

---

## R-3: Sorcha.UI fragment-token handoff

**Decision**: The existing `Sorcha.UI.Web/wwwroot/app/js/fragment-handoff.js`
parses `window.location.hash` for `token=…&refresh=…`, stores the
tokens, and removes the hash. The social-login redirect to
`/app/#token=…&refresh=…` works as-is. No change needed in this
feature.

**Rationale**: Verified by inspection (`grep` for `location.hash` in
`Sorcha.UI.Web/wwwroot/`). The same handoff is used today by
password-login and passkey-login redirects, and confirmed working by
the existing walkthroughs that exercise those flows.

**Alternatives considered**:

- *Switch to query-parameter token pass*. Rejected — fragment-based
  pass keeps tokens out of server-access logs and out of
  `Referer`-header forwarding. This is established practice (OAuth
  implicit flow) and the existing handler already implements it.

---

## R-4: Razor page route alignment with single redirect URI

**Decision**: `/auth/social/callback` is the canonical redirect URI for
all providers per environment. The Razor page at
`Pages/Auth/SocialCallback.cshtml` already declares
`@page "/auth/social/callback"`. The fix is in
`SocialLoginEndpoints.cs:99,262` where the redirect URI is constructed
as `/api/auth/social/callback-redirect` (a non-existent endpoint).

**Rationale**:

- Single per-env URI minimises OAuth-app-config surface (one redirect
  URI per provider per environment).
- The existing Razor page at `/auth/social/callback` already calls
  `SocialLoginService.ExchangeCodeAsync` and integrates with
  `WelcomeEmailDispatcher`. Reusing it is the smallest possible fix.
- Provider identity is available in `SocialStateData.Provider` (cached
  alongside the `state` parameter at initiate time) so the page does
  not need to receive `provider` as a query parameter.

**Alternatives considered**:

- *Per-provider path segments* (`/auth/social/callback/google`).
  Rejected — N redirect URIs per environment scale poorly and
  re-registering OAuth apps for every config change adds friction.
- *Add a new GET endpoint* (`MapGet("/api/auth/social/callback")`).
  Rejected — duplicates the Razor page's behaviour; would require
  deleting the Razor page or maintaining two callback handlers.

---

## R-5: Strict link policy threat-model coverage

**Decision**: Refuse social-link to a pre-existing unverified Sorcha
account even when the provider asserts `email_verified=true`. Refuse
new-user signup when the provider asserts `email_verified=false`. Do
not re-check verification on returning users (provider+sub already
linked).

**Rationale**:

- The unverified-existing-account hijack is documented in past security
  advisories (e.g. Auth0's 2018 advisory on social-account-link
  takeover; GitHub's 2020 OAuth-link race-condition disclosure). The
  pattern is the same: an attacker creates a password account with
  someone else's email but never verifies, then a real owner of that
  email signs in via social provider and the social provider gets
  linked to the attacker's account.
- Feature 114 (Citizen Wallet) anchors wallet identity on
  `PlatformUser`. A hijacked account in this feature now compromises
  the wallet too — strict policy is non-negotiable.
- Returning-user verification re-checks are unnecessary: the link was
  established under the strict gate, and re-checking creates a fragile
  flow where transient provider misbehaviour locks out a legitimate
  user. The trust boundary is the link, not the login.

**Alternatives considered**:

- *Lenient policy — auto-upgrade existing unverified to verified using
  the provider's verification*. Rejected — this is exactly the hijack.
  An attacker who controls the email at the provider can take over a
  password account that someone else created with that email.
- *Always-create-new on email collision*. Rejected — double accounts,
  poor UX, no consolidation path. Also documented as the worst option
  in the brainstorm.

---

## R-6: Welcome email idempotency in social context

**Decision**: Continue using `IWelcomeEmailDispatcher.SendIfPendingAsync`
on the social-login success path. Already wired in
`SocialCallback.cshtml.cs` per F112. Idempotent on
`PlatformUser.WelcomeSentAt`, non-throwing.

**Rationale**:

- Existing F112 architecture explicitly designed welcome dispatch for
  the social path: "social/passkey paths skip email verification (IdP
  already asserted the address), so first-login is the natural welcome
  moment" (existing comment in `SocialCallback.cshtml.cs:151-153`).
- Per CLAUDE.md feature 112 documentation: "**Do NOT add new
  welcome-email trigger sites without routing through the
  dispatcher**." The current code already complies.

**Alternatives considered**: None — established pattern.

---

## R-7: Fail-fast on missing connection strings (when `Staging` is enabled)

**Decision**: Out of scope for this feature. REQ-7 keeps n1 in
`Development` mode for now, deferring `Staging` flip to a scheduled
follow-up. When the flip happens, feature 113 fail-fast will require
all six audited interfaces have proper Postgres/Redis backed
registrations on n1.

**Rationale**:

- `docker-compose.n1.yml` already wires `ConnectionStrings:Sorcha:*`
  (Postgres, Redis, Mongo) which the audited storage registrations
  consume. Spot-check during Staging-flip task will confirm.
- This research item exists to flag that the flip is a separate
  follow-up, not to design it now.

**Alternatives considered**: Flip to Staging in this feature. Rejected
per REQ-7 — adds risk unrelated to social signup, distracts from the
core deliverable.

---

## Summary

All technical unknowns are resolved. Design is grounded. No
configuration spikes, prototypes, or POCs needed before Phase 1
artefacts are written.
