# Data Model — Feature 112: Transactional Email & Verification Sweep

**Date**: 2026-04-24

This feature adds **one** persisted field to an existing entity and introduces **one** transient (non-persisted) model record plus **six** template-specific view models. No new tables, no new repositories, no new indexes.

---

## Persisted entities

### PlatformUser *(modified)*

**Table**: `PlatformUsers` (existing, Tenant Service DB)
**Migration**: `20260408160910_InitialCreate` *(modified in-place, pre-release convention)*

| Field | Type | Nullable | Notes |
|-------|------|----------|-------|
| `WelcomeSentAt` | `DateTimeOffset?` / `timestamp with time zone` | Yes | **NEW**. Null = welcome email not yet sent. Non-null = sent, timestamp captured for audit. Written exclusively by `WelcomeEmailDispatcher.SendIfPendingAsync`. |

**Existing fields referenced by this feature** (not modified):
- `Id` (Guid) — identifies the user across trigger points
- `Email` (string) — send target
- `DisplayName` (string) — greeting personalisation
- `EmailVerified` (bool) — gate for welcome dispatch (FR-012)
- `EmailVerifiedAt` (DateTimeOffset?) — not read by this feature but sibling of the new field

**Invariants**:
- `WelcomeSentAt` is set exactly once per user across the lifetime of the account. `WelcomeEmailDispatcher` is the sole writer.
- If `WelcomeSentAt` is non-null then `EmailVerified` MUST be true. The dispatcher enforces the ordering.

**State transitions**:

```
(WelcomeSentAt = null, EmailVerified = false)
            │
            │ user verifies email OR IdP asserts verified on first login
            ▼
(WelcomeSentAt = null, EmailVerified = true)
            │
            │ WelcomeEmailDispatcher.SendIfPendingAsync → send succeeds
            ▼
(WelcomeSentAt = <now>, EmailVerified = true)  [terminal for this field]
```

A send failure leaves `WelcomeSentAt` null (the save happens only after the send returns without throwing). The next trigger will retry.

---

## Transient models (in-memory only, not persisted)

### EmailBranding

Captures the sender-identity surface for a single email render.

```csharp
public sealed record EmailBranding(
    string SenderName,       // e.g. "Sorcha" or "Acme Verification Co."
    string? LogoUrl,         // absolute https URL; null → text-only header
    string PrimaryColor,     // hex, e.g. "#2563eb"; always non-null (falls back to Sorcha default)
    string? Tagline,         // optional footer line
    string ReplyTo);         // e.g. "help@sorcha.dev"
```

**Resolution rules** (implemented in `EmailBrandingResolver`):

1. **Default branding** — read from `EmailSettings` (Sorcha platform defaults).
2. **Organisation branding** — when resolving for an invitation or invited-welcome:
   - `SenderName` = `Organization.Name` (always present)
   - `LogoUrl` = `Organization.Branding?.LogoUrl` ?? `EmailSettings.LogoUrl`
   - `PrimaryColor` = `Organization.Branding?.PrimaryColor` ?? `EmailSettings.PrimaryColor`
   - `Tagline` = `Organization.Branding?.CompanyTagline` (no fallback to Sorcha tagline — if the org has nothing to say, say nothing)
   - `ReplyTo` = `EmailSettings.ReplyTo` (platform-level, not org-level)

Per-field fallback — the resolver never returns a partially-populated org branding where a null means "no logo anywhere." Either the org's logo wins, or Sorcha's logo does.

---

## Template view models

One strongly-typed record per template. Every model composes `EmailBranding`. No reflection-based binding — Scriban reads public properties directly.

```csharp
public sealed record VerifyEmailModel(
    string DisplayName,
    string VerifyUrl,
    int ExpiresInHours,
    EmailBranding Branding);

public sealed record InviteEmailModel(
    string InviterName,
    string OrganizationName,
    string RoleDisplayName,
    string AcceptUrl,
    int ExpiresInDays,
    EmailBranding Branding);          // org-branded

public sealed record ResetPasswordModel(
    string DisplayName,
    string ResetUrl,
    int ExpiresInMinutes,
    EmailBranding Branding);

public sealed record WelcomePublicModel(
    string DisplayName,
    string DashboardUrl,
    string BrowseRegistersUrl,
    string DemoWorkflowsUrl,
    string DocsUrl,
    EmailBranding Branding);          // Sorcha-branded

public sealed record WelcomeInvitedModel(
    string DisplayName,
    string OrganizationName,
    string RoleDisplayName,
    string DashboardUrl,
    EmailBranding Branding);          // org-branded
```

**Base template** (`base.html` / `base.txt`) has no direct model — it receives whichever concrete model is being rendered and pulls `Branding` + a `content` capture block from the child template.

---

## Template ↔ model binding map

| Template (HTML + text pair) | Model record | Branding source |
|------------------------------|--------------|-----------------|
| `verify` | `VerifyEmailModel` | Sorcha default |
| `invite` | `InviteEmailModel` | Inviting organisation (per-field fallback to Sorcha) |
| `reset` | `ResetPasswordModel` | Sorcha default |
| `welcome-public` | `WelcomePublicModel` | Sorcha default |
| `welcome-invited` | `WelcomeInvitedModel` | User's first-joined standard organisation |

---

## Relationships to existing data

This feature reads but does not modify:

- `PlatformUser.Id / Email / DisplayName / EmailVerified` — identity and gating.
- `PlatformUserOrgMembership` — join table queried by `WelcomeEmailDispatcher.BuildContextAsync` to determine public-vs-invited welcome. Earliest-joined standard-org membership wins (edge case documented in spec).
- `Organization.Name / Branding` — source of invitation and invited-welcome branding.
- `OrgInvitation.AssignedRole / Email / Token / ExpiresAt` — source of invitation email's dynamic content (unchanged field set, just flows through a different code path).

No foreign keys added. No indexes added — `WelcomeSentAt` is read only via `PlatformUser.Id` (already PK-indexed).
