# Research — Feature 112: Transactional Email & Verification Sweep

**Date**: 2026-04-24
**Status**: Complete — all decisions resolved during brainstorming, no `NEEDS CLARIFICATION` markers in spec

This document records the research and decisions that shaped the design. No open investigations remain. The decisions below are carried from the brainstormed design document (`docs/superpowers/specs/2026-04-24-email-sweep-design.md`) and validated here for the planning phase.

---

## Decision 1: Template engine — Scriban

**Decision**: Use Scriban 5.12 as the template engine. Six HTML + six plaintext templates ship as embedded resources in the Tenant Service assembly. Templates are parsed once at startup into an in-memory dictionary.

**Rationale**:
- Single-assembly, MIT-licensed, maintained, ~1.5M NuGet downloads/month. Targets netstandard2.0+, fine on .NET 10.
- Supports `{{ capture }}` + `{{ include 'base.html' }}`, giving layout inheritance in two lines per child template — no Razor view-rendering ceremony required.
- Conditionals (`{{ if branding.logo_url }}...{{ end }}`) let a single template serve both the Sorcha-default and org-branded cases without duplicating files. Critical for the invitation template's optional logo/colour overrides.
- Hot-reload-friendly for designers — templates are plain `.html` files editable outside Visual Studio.
- Parses once, renders many — no runtime compilation cost per email.

**Alternatives considered**:
- **Inline C# interpolated strings (current state)** — rejected. Already produced the bugs we're fixing. Duplicated HTML across services, no plaintext fallback, no shared layout.
- **Razor views (`.cshtml`) via `IRazorViewEngine`** — rejected. Rendering Razor outside an HTTP request pipeline requires a `HttpContext` stub or a dedicated view-renderer harness; ceremony outweighs the benefit. Also ties template authoring to Visual Studio's Razor tooling.
- **Static HTML files + `string.Replace()` placeholders** — rejected. No conditionals means a separate file per branded variant. Six templates would become ten within a release.
- **Liquid / DotLiquid / Fluid** — rejected at the margin. Scriban's syntax is closer to Razor-familiar developers than Liquid's, and `capture`/`include` is more ergonomic than Fluid's `{% layout %}` for our shallow inheritance.

---

## Decision 2: Welcome-dispatch state — single column on PlatformUser

**Decision**: Track welcome-email dispatch with a nullable `DateTimeOffset? WelcomeSentAt` column on the `PlatformUsers` table. Null = not sent. Non-null = sent, with timestamp. One dispatcher (`WelcomeEmailDispatcher.SendIfPendingAsync`) is the sole writer.

**Rationale**:
- One column serves as both the idempotency flag and an audit timestamp. Avoids double-bookkeeping.
- Nullable semantics map directly to the "not yet" vs "sent at" distinction.
- Matches the existing pattern for `EmailVerifiedAt`, `VerificationTokenExpiresAt` on the same table — zero learning cost.

**Alternatives considered**:
- **Separate `WelcomeEmailEvents` audit table** — rejected as overkill for a one-shot binary flag. Would add a repository, a migration, and joins without delivering insight.
- **Redis key per user (`welcome:sent:{userId}`)** — rejected. Welcome-sent status should survive cache eviction; Postgres is the durable source of truth.
- **Boolean `WelcomeSent` flag without timestamp** — rejected. Losing the timestamp forfeits audit/debug ability with no storage saving on a nullable `timestamptz`.

---

## Decision 3: Trigger point — both verify-success and first-login paths, one dispatcher

**Decision**: `WelcomeEmailDispatcher.SendIfPendingAsync(platformUser, ct)` is invoked from exactly two points in the Tenant Service: (a) the end of `EmailVerificationService.VerifyTokenAsync` after `EmailVerified` is set true, and (b) the tail of each successful login path in `LoginService` and `SocialLoginService` before JWT issuance. The dispatcher is idempotent — `WelcomeSentAt` guards a second send.

**Rationale**:
- Email/password signups have a "just verified" moment that maps perfectly to the welcome email's psychological purpose ("you're in").
- Social and passkey signups skip verification (the IdP already verified). Their first-login is the natural "I've arrived" moment.
- A single dispatcher holds the idempotency logic so callers can fire-and-forget without worrying about double-sends.
- Matches FR-009, FR-010, FR-011, FR-013 as a literal structural mapping.

**Alternatives considered**:
- **Event bus (Redis pub-sub) fan-out** — rejected. Adds a queue hop, delivery-reliability concerns, and a new consumer for a flow that fires a handful of emails per day. Dispatch-at-callsite is simpler and satisfies the spec.
- **Background cron sweep ("find users whose welcome is pending, send them")** — rejected. Adds latency (users wait for the next sweep cycle), introduces state-machine complexity, and needs a lease or lock to prevent concurrent workers from double-sending.
- **Single trigger at account creation** — rejected. Users whose email is unverified should not receive a welcome (FR-012).
- **Single trigger only on first login regardless of signup path** — rejected. Email/password users who verify via email-link typically don't "log in" for the first time afterwards in a way that distinguishes itself from subsequent logins; the verification moment is cleaner.

---

## Decision 4: Per-org branding scope — invitation email only

**Decision**: The invitation email uses the inviting organisation's `Name`, `LogoUrl`, and `PrimaryColor` from the existing `Organization.Branding` record, with per-field fallback to Sorcha defaults. The invited-welcome email uses the same lookup. All other emails (verification, password reset, public welcome) are Sorcha-branded only.

**Rationale**:
- Invitations have a natural "inviting organisation" context. The invited person is specifically joining *Acme*, not *Sorcha*. Branded treatment reinforces trust.
- Verification and password reset are platform-level concerns ("confirm the email on your Sorcha account") and benefit from consistent Sorcha identity, not a per-org shift that would confuse users with memberships in multiple orgs.
- The `BrandingConfiguration` record already exists on `Organization` (`LogoUrl`, `PrimaryColor`, `SecondaryColor`, `CompanyTagline`). Zero schema work for per-org branding.
- Leaving verify/reset platform-branded keeps the scope inside the agreed "A+" bucket and avoids having to make multi-org decisions for a user during a password reset.

**Alternatives considered**:
- **No per-org branding anywhere in this sweep** — rejected. The user explicitly wanted the first round of corporate identity for invitations, and the existing `BrandingConfiguration` record makes it low-cost.
- **Per-org branding across all four flows** — rejected. Verify-your-email-at-Sorcha arriving in Acme branding is confusing when the user signed up on the Sorcha page. Post-MOB-007 (full org-branded signup) is the natural time to expand.

---

## Decision 5: Migration placement — fold into existing InitialCreate

**Decision**: Add the `WelcomeSentAt` column to the `PlatformUsers` `CreateTable` block inside the existing `20260408160910_InitialCreate.cs` migration. Regenerate `InitialCreate.Designer.cs` snapshot. No new `AddColumn` migration.

**Rationale**:
- Project is pre-release. The team convention is to fold schema additions into the single initial migration rather than stacking migrations that will all be squashed before ship anyway.
- No production data exists that needs migrating. Developers re-running `dotnet ef database update` get a fresh schema.
- Keeps the migration tree clean for code review.

**Alternatives considered**:
- **New `20260424_AddWelcomeSentAt.cs` migration** — rejected per pre-release convention. Would be squashed anyway; adds noise.
- **Hold the column addition until post-release** — rejected. The welcome-dispatch behaviour depends on the column; shipping the feature requires the column.

---

## Decision 6: Email-backend abstraction — keep existing SMTP/ACS split, add multipart

**Decision**: `IEmailSender` is tightened to a single method `SendAsync(to, subject, htmlBody, textBody, ct)`. Both `SmtpEmailSender` (MailKit) and `AcsEmailSender` (Azure Communication Services) are modified to emit multipart messages (HTML + plaintext). The existing startup-time selection (`AcsConnectionString` non-null → ACS; else SMTP) is unchanged. The obsolete typed methods `SendVerificationEmailAsync` and `SendInvitationEmailAsync` are deleted.

**Rationale**:
- Both backends natively support multipart alternatives — MailKit via `BodyBuilder.TextBody`, ACS via `EmailContent.PlainText`. Zero new dependencies; single additional string parameter.
- A multipart message satisfies FR-002 (plaintext fallback) and improves deliverability on spam scoring engines that penalise HTML-only transactional email.
- Removing dead code (FR-022) prevents future drift.

**Alternatives considered**:
- **Keep the obsolete typed methods in case someone wants them later** — rejected. Unused code rots. If a future flow needs a bespoke signature, it can be added then.
- **Introduce a third backend (SendGrid, SES)** — rejected, out of scope. The existing SMTP/ACS split is sufficient for current deployment targets.

---

## Decision 7: Plaintext fallback strategy — hand-authored, not stripped-HTML

**Decision**: Every HTML template ships a hand-authored plaintext counterpart (`verify.html` + `verify.txt`, etc.). Both receive the same model. The plaintext version is tailored for readability, not a mechanical HTML-strip.

**Rationale**:
- Stripped HTML produces ugly plaintext (stray padding phrases, inlined styles leak, URLs wrapped in `<a>` attribute noise).
- Hand-authored plaintext lets the tone and phrasing shift slightly for the medium (e.g., a line break instead of a button; the URL appears inline as a raw link).
- Authors can confidently change the HTML template without a separate regression on the plaintext rendering.

**Alternatives considered**:
- **HTML-to-text converter library** (`HtmlAgilityPack` stripping) — rejected. Brittle, opinionated output, and extra dependency.
- **No plaintext fallback** — rejected, violates FR-002.

---

## Decision 8: Template storage — embedded resources, not filesystem

**Decision**: Templates live under `src/Services/Sorcha.Tenant.Service/Emails/Templates/`, marked as embedded resources in the csproj via `<EmbeddedResource Include="Emails/Templates/**/*" />`. The renderer reads them via `Assembly.GetManifestResourceStream` at startup.

**Rationale**:
- Templates travel with the service image. No filesystem ordering dependency, no "forgot to copy templates to container" failure mode.
- Consistent with how other Sorcha services ship static assets (e.g., `system-register-genesis.json` in `Sorcha.Register.Models/Resources/`).
- `dotnet publish` and Docker image builds handle them automatically with no additional configuration.

**Alternatives considered**:
- **Filesystem files under `wwwroot/emails/`** — rejected. Adds a deployment step to ensure the directory is present in all runtime environments.
- **Templates in a database table** — rejected. Massive over-engineering for six static files and introduces runtime failure modes (DB unreachable at boot).
