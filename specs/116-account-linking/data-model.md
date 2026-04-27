# Phase 1: Data Model — Feature 116 Account Linking

Source of truth for entity shape, validation rules, state transitions, and storage choices. The authoritative narrative lives in `docs/superpowers/specs/2026-04-27-account-linking-design.md` §3; this file is the structured extract.

## Storage location

| Database | Schema | Service |
|---|---|---|
| `tenant` (PostgreSQL) | `public` | `Sorcha.Tenant.Service` |

All entities below live in the Tenant Service Postgres database. No new Redis usage. No cross-service data flow.

## Reused entities — no schema change

### `PlatformUser` (existing — `src/Services/Sorcha.Tenant.Service/Models/PlatformUser.cs`)

Reused as-is. The fields exercised by this feature:

- `PasswordHash` (string, nullable) — `null` means "no password set"; non-null is BCrypt hash.
- `SocialLogins` (collection) — links to `PlatformSocialLogin`.
- `PasskeyCredentials` (collection) — links to `PasskeyCredential`.

No fields added. No fields modified.

### `PlatformSocialLogin` (existing — `Models/PlatformSocialLogin.cs`)

Reused as-is. Existing unique index on `(Provider, Subject)` does the cross-account collision protection.

User-initiated unlink → **hard delete** (no audit retention). The provider's own activity log is canonical.

### `PasskeyCredential` (existing — `Models/PasskeyCredential.cs`)

Reused as-is. Behaviour change only: user-initiated remove transitions the row through the existing `Status` state machine instead of hard-deleting.

**State transitions** (existing enum `CredentialStatus { Active, Disabled, Revoked }`):

| From | Trigger | To | Side effects |
|---|---|---|---|
| `Active` | User clicks Remove (with valid challenge) | `Revoked` | Set `DisabledAt = now`, `DisabledReason = "user-removed"` |
| `Active` | Cloned-authenticator detector trips (existing) | `Disabled` | Set `DisabledAt = now`, `DisabledReason = "signature-counter-regression"` |
| `Disabled` | User clicks Remove (no challenge required — already non-functional) | `Revoked` | Update `DisabledReason = "user-removed-after-disable"` |
| `Revoked` | (terminal) | — | — |

**Tightened validation**: `DisplayName` required non-empty at register-time. UI fallback `"Unnamed passkey"` for any pre-existing empty rows.

**List queries** filter `Status != Revoked` by default. The aggregate `/api/me/auth-methods` returns `Disabled` rows so the UI can render the warning state; `Revoked` rows are excluded from user-facing reads.

**Last-method floor counts** only `Active` passkeys. `Disabled` does not count as available.

## New entity — `AuthChallengeToken`

**Location**: `src/Services/Sorcha.Tenant.Service/Models/AuthChallengeToken.cs`
**Schema**: `tenant.public.auth_challenge_tokens`
**Migration**: squashed into existing `20260425152258_InitialCreate` (pre-release policy — see plan §Constraints)

### Shape

| Field | Type | Notes |
|---|---|---|
| `Id` | `Guid` | Primary key. Default `Guid.NewGuid()`. |
| `PlatformUserId` | `Guid` | FK → `platform_users.id`. Cascade delete on user deletion. |
| `TokenHash` | `string` (64 hex chars) | `SHA-256(token)`. **Unique** — never store the raw token. |
| `Method` | `ChallengeMethod` enum | How the user proved possession (see enum below). |
| `ScopedOperation` | `ScopedOperation` enum | Which operation this token authorises (see enum below). |
| `IssuedAt` | `DateTimeOffset` | Default `DateTimeOffset.UtcNow`. |
| `ExpiresAt` | `DateTimeOffset` | `IssuedAt + 5 minutes`. |
| `ConsumedAt` | `DateTimeOffset?` | Null until first successful consume. Atomic `UPDATE … WHERE ConsumedAt IS NULL` enforces one-shot. |

Navigation: `public PlatformUser PlatformUser { get; set; } = null!;`

### Indexes

| Name | Columns | Type | Purpose |
|---|---|---|---|
| `pk_auth_challenge_tokens` | `Id` | Primary | EF default. |
| `ix_auth_challenge_tokens_token_hash` | `TokenHash` | Unique | Lookup by `SHA-256(headerValue)` from the filter; ensures distinct tokens. |
| `ix_auth_challenge_tokens_user_unconsumed` | `(PlatformUserId, ConsumedAt)` `WHERE ConsumedAt IS NULL` | Filtered | "List active challenges for user X" debug path; small selective index. |
| `ix_auth_challenge_tokens_expires_at` | `ExpiresAt` | B-tree | Drives the cleanup BackgroundService prune query. |

### Enums

```csharp
public enum ChallengeMethod
{
    Totp = 0,
    Password = 1,
    Passkey = 2,
    ReOAuth = 3
}

public enum ScopedOperation
{
    RemoveAuthMethod = 0,    // Unlink social, remove passkey
    ChangePassword = 1,
    SetPassword = 2,
    RemovePassword = 3,
    Disable2Fa = 4
}
```

### Validation rules

| Rule | Where enforced |
|---|---|
| `TokenHash` MUST be 64-char hex (SHA-256 output). | `AuthChallengeService.IssueAsync` constructs it; never accepted from input. |
| `ExpiresAt > IssuedAt`. | EF model configuration check constraint + service-side guard. |
| Consume MUST be atomic: `UPDATE … SET consumed_at = now() WHERE id = X AND consumed_at IS NULL`. | `RequireAuthChallengeAttribute` filter. 0 rows affected → reject. |
| Token cross-operation use → reject. | Filter compares `Method.ScopedOperation` against attribute's expected `ScopedOperation`. |
| Token expired → reject. | Filter checks `ExpiresAt < now`. |
| Token wrong owner → reject. | Filter compares `PlatformUserId` against caller's `sub` claim. |

## Aggregate read shape — `AuthMethodsResponse`

DTO returned by `GET /api/me/auth-methods`. Single round-trip; powers the entire Accounts tab UI.

```csharp
public sealed record AuthMethodsResponse(
    string Email,
    bool EmailVerified,
    AuthMethodsPassword Password,
    IReadOnlyList<AuthMethodsSocial> Socials,
    IReadOnlyList<AuthMethodsPasskey> Passkeys);

public sealed record AuthMethodsPassword(
    bool IsSet,
    DateTimeOffset? LastChangedAt,    // tracked via existing change history; null if never changed since creation
    bool CanRemove);                   // false iff floor would be violated

public sealed record AuthMethodsSocial(
    Guid LinkId,
    string Provider,                   // "google" | "github" | "microsoft" | "apple"
    string? Email,                     // null when provider hides email
    string? DisplayName,
    DateTimeOffset LinkedAt,
    DateTimeOffset? LastUsedAt,
    bool CanRemove);

public sealed record AuthMethodsPasskey(
    Guid Id,
    string DisplayName,                // never empty — fallback "Unnamed passkey" applied server-side
    string? DeviceType,
    PasskeyStatus Status,              // Active | Disabled (Revoked excluded from response)
    string? DisabledReason,
    DateTimeOffset CreatedAt,
    DateTimeOffset? LastUsedAt,
    bool CanRemove,
    bool CanRename);                   // false for Disabled passkeys
```

### `CanRemove` computation (single source of truth)

`IAuthMethodService.WouldRemovingLeaveZero(platformUserId, methodKind, methodId)` returns the inverse of `canRemove`. Pseudocode:

```
methods = {
    password:  user.PasswordHash != null,
    socials:   socials.count,
    passkeys:  passkeys.count(s => s.Status == Active)
}

activeCount = (password ? 1 : 0) + socials + passkeys
removingThis = (kind, id matches one of methods)
return activeCount - removingThis == 0
```

The same helper runs:
1. Inside the aggregate read to populate `CanRemove`.
2. Inside every mutation endpoint, **inside the same EF transaction** with `SELECT … FOR UPDATE` on `PlatformUser`, to defeat the two-tab TOCTOU race (FR-029).

## Migration squash — procedure

Pre-release; we modify the existing `20260425152258_InitialCreate` rather than version forward.

```bash
git rm src/Services/Sorcha.Tenant.Service/Migrations/20260425152258_InitialCreate.cs \
       src/Services/Sorcha.Tenant.Service/Migrations/20260425152258_InitialCreate.Designer.cs

# Add the AuthChallengeToken DbSet + OnModelCreating config to TenantDbContext

# Re-add (PowerShell)
$env:ConnectionStrings__Sorcha__Postgres = "Host=localhost;Database=tenant;Username=stub;Password=stub"
dotnet ef migrations add InitialCreate `
  --project src/Services/Sorcha.Tenant.Service `
  --startup-project src/Services/Sorcha.Tenant.Service `
  --output-dir Migrations
```

Verify the regenerated migration includes `auth_challenge_tokens`. Dev environments redeploy from scratch — no upgrade path required.

## Telemetry surface

OpenTelemetry counters on the `Sorcha.Tenant.Auth` meter:

| Metric | Tags | Increment |
|---|---|---|
| `sorcha_auth_challenge_issued_total` | `method`, `scope` | Once per successful `/challenge/initiate` + `/challenge/verify` pair |
| `sorcha_auth_challenge_consumed_total` | `method`, `scope`, `outcome` ∈ `{success, mismatch, expired, replay}` | Once per filter invocation |
| `sorcha_auth_method_added_total` | `kind` ∈ `{password, social, passkey}` | Once per successful add |
| `sorcha_auth_method_removed_total` | `kind` | Once per successful remove |
| `sorcha_auth_floor_blocked_total` | `kind` | Once per server-side last-method rejection |
| `sorcha_auth_link_collision_total` | `provider` | Once per email-collision rejection |

Cleanup BackgroundService logs an info-level structured event per tick: `{TickAt, RowsDeleted}`.
