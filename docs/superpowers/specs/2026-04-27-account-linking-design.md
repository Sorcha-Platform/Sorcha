# Account Linking & Auth-Method Management

**Date:** 2026-04-27
**Status:** Design — pending implementation plan
**Owner:** Stuart Fraser
**Related code:** `src/Services/Sorcha.Tenant.Service/`, `src/Apps/Sorcha.UI/Sorcha.UI.Web.Client/Pages/Settings.razor`
**Related architecture:** `sorcha-architecture` skill → "Platform Organisation Topology API"

---

## 1. Goal

Let one `PlatformUser` (one verified email) carry multiple sign-in methods — a password, OAuth links to social providers (Google, GitHub, Microsoft, Apple), and FIDO2 / WebAuthn passkeys — and let the user list, add, rename, and remove them from a new **Accounts** tab in Settings. Password set/change moves into the existing **Security** tab next to 2FA.

The data model already supports multiple methods per `PlatformUser`. The work is end-to-end: post-login link flows (currently only signup-shaped), a re-auth challenge primitive that gates dangerous operations, the new Settings UI, and a hard floor preventing self-lockout.

## 2. Decisions

| # | Decision | Pick | Rationale |
|---|---|---|---|
| Q1 | OAuth email-collision policy on link | **Reject if collides, allow if free.** Match `(Provider, Subject)` and `Email` against existing `PlatformUser` / `PlatformSocialLogin`. On collision → HTTP 409, no merge offered. | Standard pattern. Account merge is its own multi-week feature touching `UserIdentity`, memberships, persona, wallet ownership; deferred. |
| Q2 | Re-auth gating | **Asymmetric.** Adds run unguarded (already-signed-in user expanding own access). Removes and password change require a fresh challenge. Renames run unguarded. | Matches GitHub / Google. Strict gating trains users to dismiss prompts; lax gating lets a hijacked session prune the legitimate owner's recovery. |
| Q2b | Challenge ladder | TOTP if 2FA enabled → current password if set → WebAuthn step-up against an existing passkey or re-OAuth via a still-linked provider. | Picks the strongest available factor; degrades only when the stronger factor isn't enrolled. |
| Q3 | Last-method floor | **Hard.** At least one of `{password, social link, active passkey}` must remain. UI disables the Remove button on the last-surviving method; server enforces in the same transaction as the mutation. TOTP does not count as a method (it's a second factor on top of one). | Lockout blast radius in Sorcha is huge: org-derived wallets, persona vault, citizen-wallet enrolments. Self-inflicted lockout via misclick is unacceptable. |
| Q4 | Audit retention on remove | **Hybrid.** Soft-delete passkeys (`Status = Revoked`, `DisabledAt`, `DisabledReason = "user-removed"`). Hard-delete `PlatformSocialLogin` rows. | Passkeys carry forensic weight (`SignatureCounter`, `AaGuid`, `AttestationType`) — exactly what an incident responder needs. Social rows have no equivalent evidence; meaningful audit lives in the OAuth provider's logs. |
| Q5 | Tab placement | **Add `Accounts` as the first tab; rename existing `Connections` → `Service Profiles`.** | Existing tab is service-profile config, not auth. Single localisation rename + icon swap (`Dns` → `Cable`). Honours the user-requested first-tab placement. |
| Q6 | OAuth link/login dispatch | **`intent` field in server-signed `state`.** Existing `/api/auth/social/callback` branches on the decoded intent. | Canonical OAuth pattern — `state` is exactly the spec mechanism for binding intent through the round-trip. Avoids duplicating redirect URIs across provider consoles per environment. Inferring intent from session cookie is dangerous (cross-tab confusion). |

## 3. Data model

### 3.1 No-schema-change reuses

| Entity | Field | Use |
|---|---|---|
| `PlatformUser` | `PasswordHash` (nullable) | Already represents "no password set". |
| `PasskeyCredential` | `Status`, `DisabledAt`, `DisabledReason` | Soft-delete on user-initiated remove. `DisabledReason = "user-removed"` distinguishes from existing `"signature-counter-regression"`. |
| `PasskeyCredential` | `DisplayName` | Already exists. **Tightened**: required non-empty at register-time. UI fallback `"Unnamed passkey"` for any pre-existing empty rows. |
| `PlatformSocialLogin` | `LastUsedAt`, `LinkedAt`, `Email`, `DisplayName` | Already populated by login flows; surfaced in the Accounts list. |

### 3.2 New entity — `AuthChallengeToken`

Located in `src/Services/Sorcha.Tenant.Service/Models/AuthChallengeToken.cs`. Tenant DB, public schema. **Squashed into the existing `20260425152258_InitialCreate` migration** (pre-release).

```csharp
public class AuthChallengeToken
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public Guid PlatformUserId { get; set; }
    public string TokenHash { get; set; } = string.Empty;     // SHA-256(token)
    public ChallengeMethod Method { get; set; }
    public ScopedOperation ScopedOperation { get; set; }
    public DateTimeOffset IssuedAt { get; set; } = DateTimeOffset.UtcNow;
    public DateTimeOffset ExpiresAt { get; set; }              // IssuedAt + 5 min
    public DateTimeOffset? ConsumedAt { get; set; }            // null until first use
    public PlatformUser PlatformUser { get; set; } = null!;
}

public enum ChallengeMethod  { Totp, Password, Passkey, ReOAuth }
public enum ScopedOperation { RemoveAuthMethod, ChangePassword, SetPassword, RemovePassword, Disable2Fa }
```

Indexes: unique on `TokenHash`. Filtered index on `(PlatformUserId, ConsumedAt)` `WHERE ConsumedAt IS NULL` for the "list active challenges for this user" debug path.

### 3.3 Squash procedure (pre-release)

1. `git rm src/Services/Sorcha.Tenant.Service/Migrations/20260425152258_InitialCreate*`
2. Add the `AuthChallengeToken` `DbSet` and `OnModelCreating` configuration to `TenantDbContext`.
3. `$env:ConnectionStrings__Sorcha__Postgres = "any-value"; dotnet ef migrations add InitialCreate --project src/Services/Sorcha.Tenant.Service`
4. Verify the new migration includes `auth_challenge_tokens` table.
5. Dev environments redeploy from scratch — no upgrade path needed.

## 4. Endpoint surface

### 4.1 Reused as-is

| Endpoint | Use |
|---|---|
| `POST /api/auth/social/initiate` | Request gains optional `intent: "login" \| "link"` field. `state` token gains the same field plus `targetPlatformUserId` when `intent=link`. Existing callers default to `intent=login`. |
| `POST /api/passkeys/register/options` + `register/verify` | Reused unchanged for "add another passkey" — already require auth. |
| `GET /api/passkeys/credentials` | List query already filters; will be wrapped by the new aggregate endpoint. |

### 4.2 Modified

| Endpoint | Change |
|---|---|
| `POST /api/auth/social/callback` | Branch on decoded `intent`. `login` → existing path. `link` → require auth header, verify bearer's `PlatformUserId` matches `state.targetPlatformUserId`, run collision check, insert `PlatformSocialLogin`. |
| `DELETE /api/passkeys/credentials/{id}` | Soft-delete (`Status=Revoked + DisabledAt + DisabledReason="user-removed"`) instead of hard-delete. Adds `[RequireAuthChallenge(RemoveAuthMethod)]`. Server-enforces last-method floor. Disabled passkeys (already non-functional) bypass the challenge requirement. |
| `POST /api/auth/social/link` | **Removed.** UI calls `social/initiate` directly with `intent=link`. Pre-release, no callers to break. |
| `POST /api/2fa/disable` (existing) | Adopts `[RequireAuthChallenge(Disable2Fa)]`. Closes the gap where a stolen session could disable 2FA unguarded and then prune recovery methods unguarded. |

### 4.3 New endpoints

| Method | Path | Purpose | Challenge | `ScopedOperation` |
|---|---|---|---|---|
| `GET` | `/api/me/auth-methods` | Aggregated read for the Accounts tab. Returns `{ email, emailVerified, password: { isSet, lastChangedAt, canRemove }, socials: [...], passkeys: [...] }` with each entry carrying a `canRemove` boolean computed by the floor check. | No | — |
| `POST` | `/api/auth/challenge/initiate` | Issue a challenge. Request: `{ scopedOperation, preferredMethod? }`. Response: `{ challengeId, method, payload? }`. Picks method per ladder (Q2b). | n/a | — |
| `POST` | `/api/auth/challenge/verify` | Submit proof. Request: `{ challengeId, proof }`. Response: `{ token, expiresIn }`. | n/a | — |
| `POST` | `/api/auth/password/set` | Set initial password. Bootstrap mode (zero methods) bypasses challenge; otherwise requires one. | Conditional | `SetPassword` |
| `POST` | `/api/auth/password/change` | Rotate password. Old password not re-checked here — challenge already proved possession. | Yes | `ChangePassword` |
| `POST` | `/api/auth/password/remove` | Clear `PasswordHash`. | Yes | `RemovePassword` |
| `DELETE` | `/api/auth/social/{linkId}` | Hard-delete `PlatformSocialLogin` row. | Yes | `RemoveAuthMethod` |
| `PUT` | `/api/passkeys/credentials/{id}` | Rename (`DisplayName` only, non-empty). | No | — |

### 4.4 Endpoint grouping

Password endpoints live under `/api/auth/password/...` (alongside the existing forgot-password reset endpoint in `AuthEndpoints.cs`). Aggregate read lives at `/api/me/auth-methods` (consistent with existing `/api/me/persona`, `/api/me/organizations`).

### 4.5 Last-method floor — single source of truth

`IAuthMethodService.WouldRemovingLeaveZero(platformUserId, methodKind, methodId)`:
- Used inside every Remove endpoint within the same EF transaction (`SELECT ... FOR UPDATE` on `PlatformUser`).
- Same call populates `canRemove` in `/api/me/auth-methods` response so the UI matches server truth.
- TOTP enrolment is **not** counted as a method.
- Disabled / Revoked passkeys are **not** counted as active methods.

## 5. UI design

### 5.1 Settings.razor tab order

`Accounts` | Appearance | Language | Security | Notifications | Service Profiles | About

- **Accounts** — new, leftmost, icon `Icons.Material.Filled.ManageAccounts`.
- **Service Profiles** — formerly "Connections". Localisation key `settings.connections` → `settings.serviceProfiles`. Icon `Dns` → `Cable`. Body unchanged.
- **Security** — adds a Password section above the existing 2FA section.

### 5.2 Accounts tab — sections

Top to bottom: **Account email** (read-only, verification status) → **Password** (Set / Not set, Change, Remove) → **Linked sign-in providers** (rows + add-pills, already-linked providers struck through) → **Passkeys** (rows + Add button, Disabled passkeys rendered with yellow warning + cloned-authenticator tooltip).

Last-method enforcement: each destructive button is `Disabled` when `canRemove == false`, with MudTooltip *"You must keep at least one sign-in method."*

Empty-state vs full-state mockup is preserved at `.superpowers/brainstorm/964-1777316849/content/accounts-tab.html`.

### 5.3 Component split

| File | Responsibility |
|---|---|
| `Pages/Settings.razor` | Adds new tab panel, no other changes. |
| `Components/Settings/AccountsTab.razor` | Top-level tab body. Loads `/api/me/auth-methods`. |
| `Components/Settings/AuthMethods/PasswordSection.razor` | Set / change / remove password. Hosted in both Accounts and Security tabs. |
| `Components/Settings/AuthMethods/SocialLinksSection.razor` | List + add (per-provider pills) + unlink. |
| `Components/Settings/AuthMethods/PasskeysSection.razor` | List + add + rename + remove. |
| `Components/Settings/AuthMethods/AuthChallengeDialog.razor` | Shared MudDialog. Calls `/challenge/initiate` with no `preferredMethod` — server picks the strongest method per the ladder. If the user has multiple methods enrolled, the dialog renders a small "Use a different method" link that re-initiates with an explicit `preferredMethod`. Renders TOTP-input / password-input / WebAuthn-prompt / re-OAuth-launch per the chosen method. Returns the challenge token to the caller. |
| `Sorcha.UI.Core/Services/IAuthMethodsService.cs` | Typed client over the new endpoints. |

## 6. Re-auth challenge primitive

### 6.1 Flow

```
UI → POST /api/auth/challenge/initiate { scopedOperation, preferredMethod? }
     ← 200 { challengeId, method, payload? }

UI shows AuthChallengeDialog, collects proof
   (TOTP code | password | WebAuthn assertion | OAuth code via redirect)

UI → POST /api/auth/challenge/verify { challengeId, proof }
     ← 200 { token: "ch_…", expiresIn: 300 }

UI → mutation endpoint, header X-Auth-Challenge: ch_…
     server filter consumes token atomically, mutation runs
```

### 6.2 Method ladder

`/challenge/initiate` picks the strongest available method:

```
if user.TotpEnabled                  → method = "totp"
else if user.PasswordHash != null    → method = "password"
else if user has Active passkey      → method = "passkey"
else if user has any social link     → method = "reoauth"
else                                  → 400 NoChallengeAvailable
                                        (only reachable in bootstrap;
                                         set-password runs unguarded then)
```

Client may pass `preferredMethod` to override; server still validates the choice is enrolled. Cannot downgrade past TOTP if 2FA is on.

### 6.3 Why a token, not inline proof

Re-OAuth and WebAuthn step-up are multi-step round-trips that don't fit as parameters on the mutation endpoint body. A short-lived token decouples proof flow from mutation flow uniformly across all four methods.

### 6.4 Server filter

`[RequireAuthChallenge(ScopedOperation.X)]` attribute. Filter:

1. Read `X-Auth-Challenge` header (401 if missing).
2. Look up `AuthChallengeToken` by `SHA-256(headerValue)`.
3. Reject (401) if any of: not found / `PlatformUserId != caller` / `ScopedOperation != attribute value` / `ConsumedAt != null` / `ExpiresAt < now`.
4. Atomic update: `UPDATE auth_challenge_tokens SET consumed_at = now() WHERE id = X AND consumed_at IS NULL` — if 0 rows affected, reject (race).
5. Proceed.

### 6.5 Cleanup

`AuthChallengeTokenCleanupService` — `BackgroundService` in Tenant Service, daily tick (24h interval), deletes rows where `ExpiresAt < now() - INTERVAL '7 days'`. 7-day forensic retention then prune. Single-process safe; multi-instance safe (no harm in concurrent deletes).

## 7. Edge cases & error handling

| # | Case | Handling |
|---|---|---|
| 1 | Two simultaneous links of the same Google account to different `PlatformUser`s | Unique index on `PlatformSocialLogin (Provider, Subject)`. Loser receives **HTTP 409 `SocialProviderAlreadyLinked`**, UI surfaces *"This Google account is linked to a different Sorcha account."* |
| 2 | TOCTOU race on last-method floor (two tabs both pass UI check) | Floor check happens inside the mutation transaction with `SELECT … FOR UPDATE` on `PlatformUser`. Loser gets **HTTP 409 `LastSignInMethodProtected`**. |
| 3 | Challenge token reused for wrong operation | Filter rejects with **HTTP 401 `ChallengeOperationMismatch`**. |
| 4 | OAuth `state` tampering on link | `state` is HMAC-SHA256 signed with `SocialLogin:StateSigningKey`. Tampering → **HTTP 400 `InvalidOAuthState`**, no link, no login. Logged at Warning. |
| 5 | OAuth provider returns no email (Apple "Hide my email", private GitHub) | Collision check skipped (no email to collide). `(Provider, Subject)` unique index alone enforces uniqueness. UI shows *"(no email shared)"*. |
| 6 | User has zero auth methods (bootstrap) | `POST /api/auth/password/set` checks `methods.Count == 0` and bypasses challenge. Reachable only via DB corruption — allowing self-recovery is the right call. |
| 7 | Cloned-authenticator detector trips on existing passkey | Status flips to `Disabled` via existing FIDO2 logic. Accounts tab renders Disabled rows with yellow warning + tooltip + Remove (no Rename). Removing a Disabled passkey transitions to `Revoked` and **bypasses challenge** (already non-functional). |
| 8 | Concurrent passkey rename | Last-write-wins on `DisplayName`. UI revalidates by re-fetching `/api/me/auth-methods`. |
| 9 | User unlinks the social provider they're authenticated via | JWT stays valid until natural expiry (~60 min). Refresh tokens stay valid until natural expiry. Next sign-in attempt via that provider fails; user falls back to another method. No forced logout. |
| 10 | TOTP code reuse across two challenges in the same window | Each challenge's verify call is independent. TOTP service's existing rate limit + 30s window applies. A second challenge inside the same window needs a fresh code. |

## 8. Testing

### 8.1 Unit — `tests/Sorcha.Tenant.Service.Tests/Services/`

- `AuthChallengeServiceTests` — initiate, verify, consume, ladder selection, expired/consumed/wrong-operation rejection, atomic-consume race (exactly-one-winner across concurrent verify calls).
- `AuthMethodServiceTests` — `WouldRemovingLeaveZero` across the seven combinations of `{password?, socials, passkeys}`. Disabled passkey not counting as active.
- `SocialLinkServiceTests` — collision detection (matching `Subject`, matching `Email`, no email at all). `intent=link` callback path with `state` HMAC verify and tamper rejection.
- `PasswordSetChangeRemoveServiceTests` — bootstrap mode (no challenge), set with challenge, change with challenge, remove with challenge + floor.
- `PasskeyRevocationTests` — soft-delete flips `Status + DisabledReason + DisabledAt`. List endpoint hides Revoked by default. Disabled passkey can be removed without challenge.

### 8.2 Integration — `tests/Sorcha.Tenant.Service.Tests/Endpoints/`

`WebApplicationFactory` per existing pattern (Redis mocked per project memory).

- End-to-end: initiate → verify → mutation → re-use same token → 401.
- `/api/me/auth-methods` aggregation matches DB state across all method-shape combinations.
- TOCTOU floor race — two concurrent removes against `password + 1 passkey`; assert exactly one succeeds.
- OAuth `state` tamper — modify each field, assert each variant returns 400.

### 8.3 EF migration

Existing test infrastructure runs migrations on the test container. The squashed `InitialCreate` is exercised end-to-end. Verify `auth_challenge_tokens` table appears in the snapshot test if one exists.

### 8.4 E2E — `tests/Sorcha.UI.E2E.Tests/Docker/`

Playwright, following `frontend-design` and `playwright` skill patterns.

- `AccountsTab_RemoveButton_DisabledWhenLastMethod` — only-password user; assert Remove disabled with tooltip.
- `AccountsTab_AddPasskey_FullFlow` — registered passkey appears with user-supplied DisplayName and `Last used: never`.
- `AccountsTab_UnlinkGoogle_RequiresChallenge` — unlink triggers `AuthChallengeDialog`, TOTP submission proceeds, list refreshes.
- `AccountsTab_RenameNotChallengeGated` — rename happens inline, no dialog.
- `Security_ChangePassword_RequiresChallenge` — change-password from Security tab triggers the same dialog.

### 8.5 Coverage target

>85% line coverage on new code per project bar.

## 9. Out of scope

- **Account merge** (Q1 path C) — collision returns 409 with no merge offer. Future feature.
- **Email change** for a `PlatformUser` — separate flow with re-verification, not addressed here. Email is read-only in the Accounts tab.
- **Passwordless-default** — not changing the default registration flow.
- **Recovery codes** for the "I lost everything" scenario.
- **Admin-side reset** — admin endpoints for SystemAdmin to clear another user's methods.
- **Audit log UI** — Revoked passkey rows are queryable in DB; no UI surface in this design.

## 10. Open follow-ups (post-ship)

- Account merge feature (touches `UserIdentity`, `PlatformUserOrgMembership`, persona, wallet ownership).
- Email-change feature with re-verification flow.
- Recovery codes (single-use TOTP-replacement codes).
- Admin reset surface for SystemAdmin role.
- Audit log UI surface for Revoked passkeys + Disabled (cloned-detected) passkeys + social-link history.

## 11. References

- Brainstorming session: this conversation, 2026-04-27.
- Visual companion mockup: `.superpowers/brainstorm/964-1777316849/content/accounts-tab.html`.
- Existing Tenant Service auth surface: `src/Services/Sorcha.Tenant.Service/Endpoints/{Auth,Passkey,SocialLogin}Endpoints.cs`.
- Existing data model: `src/Services/Sorcha.Tenant.Service/Models/{PlatformUser,PlatformSocialLogin,PasskeyCredential}.cs`.
- Existing Settings UI: `src/Apps/Sorcha.UI/Sorcha.UI.Web.Client/Pages/Settings.razor`.
- Architecture context: `sorcha-architecture` skill → "Platform Organisation Topology API".
