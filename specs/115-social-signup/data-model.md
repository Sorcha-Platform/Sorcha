# Data Model — Feature 115 Social Signup

**Phase**: 1 — Design
**Schema impact**: Zero migrations. No new tables, no new columns. The
feature uses existing entities; one DTO record gains a field (in-memory
only, not persisted).

---

## Entities (existing, unchanged)

### `PlatformUser` (`tenant.platform_users`)

The root user identity for a public-org consumer.

| Field | Type | Notes |
|---|---|---|
| `Id` | `Guid` | PK |
| `Email` | `string` | Frozen post-signup. **FR-009** prohibits refresh from provider. |
| `EmailVerified` | `bool` | Set `true` on successful social signup (provider asserted verified). Read-side gate for **FR-011**. |
| `EmailVerifiedAt` | `DateTimeOffset?` | Stamped at first signup or first verification email click. |
| `DisplayName` | `string` | **Refreshed each login** from provider `name` claim per **FR-008**. |
| `WelcomeSentAt` | `DateTimeOffset?` | Drives idempotent welcome dispatch per **FR-015**. |
| `LastLoginAt` | `DateTimeOffset?` | Updated on every successful sign-in. |
| `LockedPermanently` | `bool` | Existing lockout state — orthogonal to this feature. |

### `PlatformSocialLogin` (`public.platform_social_logins`)

The link from a `PlatformUser` to one external provider identity.

| Field | Type | Notes |
|---|---|---|
| `Id` | `Guid` | PK |
| `PlatformUserId` | `Guid` | FK → `PlatformUser.Id`, cascade delete |
| `Provider` | `string` | `"google"`, `"github"`, lowercase by convention |
| `Subject` | `string` | Provider's stable `sub` claim (or `id` for GitHub). Immutable once linked. |
| `Email` | `string?` | Snapshot at link time. **FR-009** — not refreshed. |
| `DisplayName` | `string?` | Snapshot at link time. (Refresh happens on `PlatformUser.DisplayName`, not here.) |
| `LinkedAt` | `DateTimeOffset` | Set on row creation. |
| `LastUsedAt` | `DateTimeOffset?` | **FR-007** — updated each sign-in. |

**Composite invariant**: `(Provider, Subject)` is unique per row by
convention; the resolution path queries on this pair to find returning
users. (No DB-level unique constraint exists today; existing flow
treats first-found-wins. This feature does not add one — the invariant
is preserved by the existing `GetByProviderSubjectAsync` query path.)

### `PlatformSettings` (singleton, `tenant.platform_settings`)

| Field | Type | Notes |
|---|---|---|
| `PublicOrgEnabled` | `bool` | Gates `/api/auth/social/initiate`. **FR-019** — seed value made configurable in this feature. |
| `MaxOrgsPerUser` | `int` | Existing — not touched. |

### `UserIdentity` (`{org-schema}.user_identities`)

Per-org membership profile. Created in the public-org schema on first
social signup with `Roles=[Consumer]`, `ProvisionedVia=SocialLogin`.

(Existing entity; this feature does not modify its shape.)

---

## Records & DTOs (in-memory)

### `SocialAuthCallbackResult` — **MODIFIED**

Existing record, gains one field.

```csharp
public record SocialAuthCallbackResult(
    bool Success,
    string? Error,
    string? Subject,
    string? Email,
    string? DisplayName,
    bool EmailVerified,    // NEW — FR-010 substrate
    string Provider);
```

Population rules (per provider):

| Provider | `EmailVerified` source |
|---|---|
| Google / Microsoft / Apple | `email_verified` claim from ID token (preferred) or userinfo response. **Default `false` when claim absent.** |
| GitHub | `true` only when `/user/emails` returns the primary email with `verified: true`. (Existing logic; surfaced explicitly on the result.) |

### `SocialLoginRefusal` — **NEW**

```csharp
public enum SocialLoginRefusal
{
    None = 0,
    ProviderUnverified,    // Provider asserted email_verified=false
    ExistingUnverified,    // Email-collision with an unverified Sorcha user
}
```

### `ResolveSocialUserResult` — **NEW**

Replaces the existing `(PlatformUser User, bool IsNew)` tuple return of
`PlatformUserService.ResolveOrCreateSocialUserAsync`.

```csharp
public record ResolveSocialUserResult(
    PlatformUser? User,
    bool IsNew,
    SocialLoginRefusal Refusal);
```

`Refusal != None` ⇒ `User` is null and the callback page must render the
matching copy. `User != null` ⇒ `Refusal == None`.

---

## State transitions

### Resolve flow — three terminal states

```
                        ┌─────────────────────────────────┐
                        │ ExchangeCodeAsync returns claim │
                        └────────┬────────────────────────┘
                                 │
                  ┌──────────────┴──────────────┐
                  │                             │
                  ▼                             ▼
    PlatformSocialLogin row exists?   No row — fresh attempt
    ┌──────────────────────────────┐  ┌─────────────────────────────────────┐
    │ ResolveSocialUserResult(     │  │ provider.EmailVerified == false?    │
    │   User: existing,            │  │ ┌───────────────────────────────┐   │
    │   IsNew: false,              │  │ │ Yes → Refusal.ProviderUnver…  │   │
    │   Refusal: None)             │  │ └───────────────────────────────┘   │
    │ Side effect:                 │  │                                     │
    │  • LastUsedAt = now          │  │ Email-collision with PlatformUser?  │
    │  • DisplayName ← claim       │  │   Existing user EmailVerified=true? │
    │ Note: NOT re-checking        │  │   Provider EmailVerified=true?      │
    │  EmailVerified per FR-013    │  │                                     │
    └──────────────────────────────┘  │   ┌───────────────────────────┐     │
                                      │   │ All true → link, return   │     │
                                      │   │   User=existing, IsNew=f  │     │
                                      │   │ Side effect:              │     │
                                      │   │  • new PlatformSocialLogin│     │
                                      │   │  • LastUsedAt = now       │     │
                                      │   │  • DisplayName ← claim    │     │
                                      │   └───────────────────────────┘     │
                                      │                                     │
                                      │   ┌───────────────────────────┐     │
                                      │   │ Existing unverified →     │     │
                                      │   │   Refusal.ExistingUnver…  │     │
                                      │   └───────────────────────────┘     │
                                      │                                     │
                                      │ No collision → create new           │
                                      │   PlatformUser(EmailVerified=true,  │
                                      │     EmailVerifiedAt=now)            │
                                      │   + PlatformSocialLogin             │
                                      │   + UserIdentity in public org      │
                                      │   + PlatformUserOrgMembership       │
                                      │   Welcome dispatcher fires          │
                                      └─────────────────────────────────────┘
```

### Welcome email dispatch (existing F112 — unchanged)

Driven by `PlatformUser.WelcomeSentAt`:

- `null` → dispatcher fires, sets `WelcomeSentAt = now`
- non-`null` → dispatcher returns silently

This is invoked once per resolve-success in `SocialCallback.cshtml.cs`,
already wired and idempotent. No change in this feature.

---

## Validation rules

| Rule | Source FR | Applies in |
|---|---|---|
| `Provider.EmailVerified == false` ⇒ refuse with `ProviderUnverified` | FR-010 | `ResolveOrCreateSocialUserAsync` (new path), or upstream in `SocialCallback.cshtml.cs` |
| Email-collision + existing user `EmailVerified == false` ⇒ refuse with `ExistingUnverified` | FR-011 | `ResolveOrCreateSocialUserAsync` step 2 |
| Email-collision + both verified ⇒ link | FR-012 | `ResolveOrCreateSocialUserAsync` step 2 |
| Returning-user (link exists) ⇒ no verification re-check | FR-013 | `ResolveOrCreateSocialUserAsync` step 1 |
| New-user creation ⇒ `EmailVerified=true`, `EmailVerifiedAt=now` | FR-005 | `ResolveOrCreateSocialUserAsync` step 3 |
| Each successful resolve ⇒ refresh `DisplayName` if claim non-empty | FR-008 | All 3 paths |
| Each successful resolve ⇒ update `LastUsedAt` | FR-007 | Steps 1, 2 (step 3 sets via row creation) |
| Welcome dispatch fires once per user | FR-015 | `SocialCallback.cshtml.cs` (already wired) |

---

## Persistence implications

- **No migrations.** Confirmed against master at commit `d0cdd55a`
  (Feature 114 baseline). Schema is unchanged in this feature.
- **No new indexes.** Existing index on `PlatformSocialLogin (Provider,
  Subject)` and `PlatformUser (Email)` cover the lookup paths.
- **Transactional consistency**: the resolve-and-create paths perform
  multiple writes (`PlatformUser` create, `PlatformSocialLogin` insert,
  `UserIdentity` create, `PlatformUserOrgMembership` add, `LastLoginAt`
  update). Existing code uses `_db.SaveChangesAsync()` between steps —
  not a single transaction. This is pre-existing behaviour; this
  feature does not change it. (Note: a partial-failure recovery path
  is not in scope; logged as a non-blocker.)
