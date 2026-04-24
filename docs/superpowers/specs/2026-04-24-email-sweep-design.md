# Email & Verification Sweep — Design

**Date:** 2026-04-24
**Status:** Implemented — shipped in PR #391 on branch `112-email-sweep`, spec + plan + tasks under `specs/112-email-sweep/`
**Related task:** AUTH-006 (MASTER-TASKS.md Theme 5) — closed
**Scope:** Tenant Service transactional email — verify, invite, reset, welcome

---

## Problem

Transactional email in Sorcha is in three inconsistent states, two of them effectively broken:

| Flow | Current behaviour |
|------|-------------------|
| Email verification (signup) | Sends a plaintext body containing the raw 64-char token. No clickable link. User is expected to copy the token into a UI they have to find. |
| Org invitation | Sends a plaintext body with the raw token and inviter's name. No mention of *which* org, what role, or where to click. |
| Password reset | Sends proper HTML with a button — but hand-rolled, blue `#2563eb`, unshared layout. |

A dormant "nice" version of verify + invite HTML exists on `SmtpEmailSender` (purple `#6366f1`, not aligned with the reset colour) but nothing calls it. Net result: two colour palettes, zero shared layout, no branding, no plaintext multipart alternative, no welcome email at all.

There is also no moment in the new-user lifecycle that introduces the **recovery phrase** safety concept. The 12-word mnemonic is displayed once in `CreateWallet.razor` (then `Array.Clear`'d from memory by design) and that's the only touch-point. New public users have no warm introduction to why they must save it.

## Goals

1. Every transactional email the Tenant Service sends goes through a single templated path with a shared Sorcha-branded base layout, plaintext multipart fallback, and a consistent voice (professional, friendly, one clear action per message).
2. Fix the two latent plaintext-token bugs — verification and invitation — so users receive clickable links with context.
3. Introduce a welcome email that fires exactly once per user across both signup paths (email-password and social/passkey).
4. The public welcome email does one job beyond greeting: prime users for the recovery-phrase moment they'll encounter when they create their first wallet. No shaming, no panic, no phrase content in email.
5. Invitation emails visually carry the inviting organisation's branding (name + logo + primary colour) when the org has branding configured.

## Non-goals

- New notification email categories (pending action, credential received, etc.) — belongs to Feature 062.
- Per-org branding on verification and password reset — stays Sorcha-branded in this sweep.
- Local dev mail catcher (Mailpit / MailHog) — worth doing, separate work.
- In-app wallet-creation UX for the recovery phrase — separate UX review.
- Delivery retries, outbox pattern, bounce handling — fire-and-forget stays.

---

## Architecture

Five components, composed top-down:

```
┌──────────────────────────────────────────────────────────────┐
│ Caller services                                              │
│  EmailVerificationService • InvitationService                │
│  PasswordResetService     • LoginService / SocialLoginService│
└──────────────────────────┬───────────────────────────────────┘
                           │ typed methods
                           ▼
┌──────────────────────────────────────────────────────────────┐
│ ITransactionalEmailService  (new — facade)                   │
│   SendVerificationAsync(user, token, ct)                     │
│   SendInvitationAsync(invitation, invitingOrg, ct)           │
│   SendPasswordResetAsync(user, rawToken, ct)                 │
│   SendWelcomeAsync(user, ctx, ct)    ← picks pub/invited     │
└──────────────┬─────────────────────────────┬─────────────────┘
               │ render                      │ send
               ▼                             ▼
┌───────────────────────────┐   ┌──────────────────────────────┐
│ IEmailTemplateRenderer    │   │ IEmailSender  (unchanged API │
│  ScribanEmailTemplate…    │   │   other than html+text)      │
│  returns (html, text)     │   │  Smtp… / Acs…                │
└───────────────────────────┘   └──────────────────────────────┘
               │
               ▼
┌──────────────────────────────────────────────────────────────┐
│ Templates (embedded resources)                               │
│  Emails/Templates/                                           │
│    base.html / base.txt          ← shared layout             │
│    verify.html / verify.txt                                  │
│    invite.html / invite.txt      ← reads org branding        │
│    reset.html  / reset.txt                                   │
│    welcome-public.html  / .txt                               │
│    welcome-invited.html / .txt                               │
└──────────────────────────────────────────────────────────────┘
```

### Key design points

- **`ITransactionalEmailService` is the only thing application code calls.** Callers pass domain objects; the service builds the model and delegates to renderer + sender.
- **`IEmailSender` tightens** to a single method: `SendAsync(to, subject, htmlBody, textBody, ct)`. Both backends natively support multipart (MailKit `BodyBuilder.TextBody`, ACS `EmailContent.PlainText`). The three existing typed methods on `IEmailSender` (`SendVerificationEmailAsync`, `SendInvitationEmailAsync`) are removed as dead code — nothing calls them.
- **Templates are embedded resources**, loaded once at startup into an in-memory `Dictionary<string, Template>`. No disk I/O per email.
- **`base.html` / `base.txt`** use Scriban `capture` + `include` for layout inheritance. Individual templates focus on unique content; branding changes land in one file.
- **Welcome trigger** is a single helper, `WelcomeEmailDispatcher.SendIfPendingAsync(platformUser, ct)`, called from both the verify-email success path and each login success path (email/password, social, passkey). It checks `EmailVerified && !WelcomeSent`, decides public vs invited based on org memberships, dispatches, sets `WelcomeSentAt`, saves.
- **`PlatformUser.WelcomeSentAt`** — new nullable `DateTimeOffset?` column. Migration backfills existing verified users to `NOW()` so the deploy doesn't send a blast.
- **Per-org branding lookup** is entirely inside `SendInvitationAsync` and `SendWelcomeAsync` (invited variant). It loads the inviting `Organization.Branding` and passes name + logo + colour into the template model. Template falls back to Sorcha defaults if any field is null.

---

## Template model contracts

Shared branding lives in one record, composed into each template's model:

```csharp
public sealed record EmailBranding(
    string SenderName,      // "Sorcha" or "Acme Verification Co."
    string? LogoUrl,        // absolute https URL; null → text-only header
    string PrimaryColor,    // "#2563eb" default, hex
    string? Tagline,        // footer line under sign-off
    string ReplyTo);        // "help@sorcha.dev"

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
    EmailBranding Branding);  // org-branded, not Sorcha

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
    EmailBranding Branding);

public sealed record WelcomeInvitedModel(
    string DisplayName,
    string OrganizationName,
    string RoleDisplayName,
    string DashboardUrl,
    EmailBranding Branding);
```

`EmailBranding` is built by `EmailBrandingResolver`:
- **Default** — reads `EmailSettings` (Sorcha branding).
- **For invitation and invited-welcome** — reads `Organization.Branding`; per-field fallback to Sorcha defaults. Org name always wins.

## Scriban templates

### `Emails/Templates/base.html`

```html
<!DOCTYPE html>
<html>
<head><meta charset="utf-8"><title>{{ subject }}</title></head>
<body style="margin:0;font-family:-apple-system,'Segoe UI',Roboto,sans-serif;background:#f4f4f7;">
  <table width="100%" cellpadding="0" cellspacing="0" style="background:#f4f4f7;padding:32px 0;">
    <tr><td align="center">
      <table width="600" cellpadding="0" cellspacing="0" style="background:white;border-radius:8px;">
        <tr><td style="padding:32px 32px 16px;">
          {{ if branding.logo_url }}
            <img src="{{ branding.logo_url }}" alt="{{ branding.sender_name }}" style="max-height:40px;">
          {{ else }}
            <strong style="font-size:20px;color:{{ branding.primary_color }};">{{ branding.sender_name }}</strong>
          {{ end }}
        </td></tr>
        <tr><td style="padding:0 32px 32px;color:#111;line-height:1.5;">
          {{ content }}
        </td></tr>
        <tr><td style="padding:16px 32px;border-top:1px solid #eee;font-size:12px;color:#888;">
          {{ if branding.tagline }}<p style="margin:0 0 8px;">{{ branding.tagline }}</p>{{ end }}
          <p style="margin:0;">Questions? Reply to this email or write to
            <a href="mailto:{{ branding.reply_to }}" style="color:{{ branding.primary_color }};">{{ branding.reply_to }}</a>.
          </p>
        </td></tr>
      </table>
    </td></tr>
  </table>
</body>
</html>
```

### `Emails/Templates/verify.html` (pattern illustration)

```html
{{ capture content }}
<h2 style="margin:0 0 16px;">Confirm your email</h2>
<p>Hi {{ display_name }} — thanks for signing up to {{ branding.sender_name }}.</p>
<p>Tap the button below to confirm this is your email, and you'll be in.</p>
<p style="margin:24px 0;">
  <a href="{{ verify_url }}"
     style="background:{{ branding.primary_color }};color:white;padding:12px 24px;
            text-decoration:none;border-radius:6px;display:inline-block;">
    Confirm my email
  </a>
</p>
<p style="font-size:14px;color:#666;">
  The link works for {{ expires_in_hours }} hours. If you didn't sign up, just ignore this — no account will be created.
</p>
{{ end }}
{{ include 'base.html' }}
```

### `Emails/Templates/verify.txt`

```
Hi {{ display_name }} — thanks for signing up to {{ branding.sender_name }}.

Confirm your email by opening this link:
{{ verify_url }}

The link works for {{ expires_in_hours }} hours. If you didn't sign up,
just ignore this — no account will be created.

— the {{ branding.sender_name }} team
Questions? Write to {{ branding.reply_to }}.
```

Both HTML and text receive the same model. Renderer returns `(html, text)` tuple; sender attaches both as multipart.

### `Emails/Templates/welcome-public.html` (copy outline)

Body content:

> **Welcome to Sorcha, {DisplayName} 👋**
>
> Your account is ready. Jump in whenever you're set.
>
> **[Take me to my dashboard]** → DashboardUrl
>
> **One thing worth knowing now**
>
> When you create your first wallet, Sorcha will show you a **12-word recovery phrase**. Write it down the moment you see it — a password manager or a piece of paper in a drawer both work. That phrase is the only way back into your wallet if you lose access. We can't see it and can't get it back for you — that's the whole point.
>
> No rush, but when you're ready to create a wallet, that's the moment to have a pen handy.
>
> **What's next**
> - 🧭 [Browse your registers](BrowseRegistersUrl)
> - 🧰 [Try a demo workflow](DemoWorkflowsUrl)
> - 📚 [Read the docs](DocsUrl)
>
> Any questions, just reply to this email — a human's at the other end.
>
> — the Sorcha team

Voice: warm, one moment of seriousness around the recovery phrase, no panic, no in-email phrase content.

### `Emails/Templates/welcome-invited.html` (copy outline)

Body content:

> **You've joined {OrganizationName}**
>
> Hi {DisplayName} — welcome aboard. Your {RoleDisplayName} account at {OrganizationName} is ready to go.
>
> **[Take me to my dashboard]** → DashboardUrl
>
> If you have any questions about your role or what you can access, your organisation's admin team is the best place to start.
>
> — the {OrganizationName} team

Org-branded (logo, colour, sender name all reflect the inviting org). No recovery-phrase content — org-managed users typically use org infrastructure.

### Renderer behaviour

```csharp
public interface IEmailTemplateRenderer
{
    (string Html, string Text) Render(string templateName, object model);
}
```

- **Startup:** walks the embedded resource namespace, parses every `.html` and `.txt` as Scriban templates into a `Dictionary<string, Template>`. Fails fast on parse errors (caught by tests).
- **Per call:** looks up `{name}.html` and `{name}.txt`, renders both against the model, returns.
- **Missing template → `KeyNotFoundException`** (loud in tests, loud in prod).

---

## Code changes per caller

### `IEmailSender` tightening

```csharp
public interface IEmailSender
{
    Task SendAsync(
        string to, string subject,
        string htmlBody, string textBody,
        CancellationToken ct = default);
}
```

- `SmtpEmailSender` → `BodyBuilder { HtmlBody = htmlBody, TextBody = textBody }`
- `AcsEmailSender` → `EmailContent { Html = htmlBody, PlainText = textBody }`
- Delete `SendVerificationEmailAsync` + `SendInvitationEmailAsync` and their duplicate inline HTML.

### `EmailVerificationService.GenerateAndSendVerificationAsync`

Before: `SendAsync(email, "Verify…", "…token: {token}", ct)` — plaintext token.
After:
```csharp
var verifyUrl = $"{_baseUrl}/auth/verify-email?token={Uri.EscapeDataString(token)}";
await _transactional.SendVerificationAsync(
    new VerifyEmailDispatch(user.Email, user.DisplayName, verifyUrl, TokenExpiry.TotalHours),
    ct);
```

### `EmailVerificationService.VerifyTokenAsync`

After setting `EmailVerified = true`, add one line:
```csharp
await _welcomeDispatcher.SendIfPendingAsync(platformUser, ct);
```

### `InvitationService.CreateInvitationAsync`

Before: `SendAsync(email, "…", "{inviter} has invited you to…", ct)` — plaintext token.
After:
```csharp
var invitingOrg = await _orgRepository.GetByIdAsync(organizationId, ct);
var acceptUrl = $"{_baseUrl}/invitations/accept?token={Uri.EscapeDataString(token)}";
await _transactional.SendInvitationAsync(
    new InviteEmailDispatch(
        request.Email, inviterName, invitingOrg, request.Role,
        acceptUrl, request.ExpiryDays),
    ct);
```

Org branding resolution lives inside `SendInvitationAsync`.

### `PasswordResetService.SendResetLinkAsync`

Replace 16 lines of inline HTML:
```csharp
await _transactional.SendPasswordResetAsync(
    new ResetPasswordDispatch(user.Email, user.DisplayName, resetLink, (int)TokenTtl.TotalMinutes),
    ct);
```
Delete `BuildResetEmailHtml`.

### Login success paths

`LoginService`, `SocialLoginService`, passkey sign-in each get one call right before JWT issuance:
```csharp
await _welcomeDispatcher.SendIfPendingAsync(platformUser, ct);
```

### `WelcomeEmailDispatcher.SendIfPendingAsync`

```csharp
if (platformUser.WelcomeSentAt.HasValue || !platformUser.EmailVerified) return;
var ctx = await BuildContextAsync(platformUser, ct);  // decides public vs invited + loads org
await _transactional.SendWelcomeAsync(ctx, ct);
platformUser.WelcomeSentAt = DateTimeOffset.UtcNow;
await _dbContext.SaveChangesAsync(ct);
```

**Public vs invited decision:** count the user's `PlatformUserOrgMembership` rows. If all memberships are to the public org → public template. If any membership is to a standard org → invited template with that org's branding and name. Users with memberships in multiple standard orgs get the invited template for whichever has the earliest `JoinedAt` (first org wins).

---

## EF migration

**Pre-release convention:** schema changes fold into the single `20260408160910_InitialCreate` migration rather than stacking a new one. So the `WelcomeSentAt` column is added directly to the `PlatformUsers` `CreateTable` block, next to the existing `EmailVerified` column:

```csharp
WelcomeSentAt = table.Column<DateTimeOffset>(
    type: "timestamp with time zone", nullable: true),
```

Matching field on `PlatformUser` entity:
```csharp
public DateTimeOffset? WelcomeSentAt { get; set; }
```

The `InitialCreate.Designer.cs` snapshot is regenerated alongside (standard EF workflow after editing the model). No backfill SQL is needed — no production data exists yet, so developers re-running migrations get a fresh schema. If/when a pre-release environment has real signup traffic that *must* be preserved, the ops step is a one-liner (`UPDATE "PlatformUsers" SET "WelcomeSentAt" = NOW() WHERE "EmailVerified" = TRUE;`) run manually before the first welcome-dispatch deploy; this stays out of the migration itself.

---

## DI wiring

```csharp
services.Configure<EmailSettings>(configuration.GetSection("Email"));
services.AddSingleton<IEmailTemplateRenderer, ScribanEmailTemplateRenderer>();
services.AddScoped<IEmailBrandingResolver, EmailBrandingResolver>();
services.AddScoped<ITransactionalEmailService, TransactionalEmailService>();
services.AddScoped<WelcomeEmailDispatcher>();

if (!string.IsNullOrEmpty(acsConnectionString))
    services.AddSingleton<IEmailSender, AcsEmailSender>();
else
    services.AddSingleton<IEmailSender, SmtpEmailSender>();
```

## Package addition

`Directory.Packages.props`:
```xml
<PackageVersion Include="Scriban" Version="5.12.0" />
```
Referenced only from `Sorcha.Tenant.Service.csproj`.

## Config additions

New fields on `EmailSettings` (all optional with defaults):
```csharp
public string? LogoUrl { get; set; }
public string PrimaryColor { get; set; } = "#2563eb";
public string? Tagline { get; set; }
public string ReplyTo { get; set; } = "help@sorcha.dev";
```

Existing fields (`BaseUrl`, `FromAddress`, `FromName`, SMTP settings, `AcsConnectionString`) stay as-is.

---

## Testing

### Unit tests

- **`ScribanEmailTemplateRendererTests`**
  - Every embedded template parses at startup
  - Rendering known model against a golden `.html` fixture (snapshot)
  - Missing template name throws `KeyNotFoundException`
  - Malformed model (null required field) throws with clear message
- **`EmailBrandingResolverTests`**
  - Default branding populated from `EmailSettings`
  - Org branding: logo present → org logo; logo missing → Sorcha logo; colour missing → Sorcha colour; name always wins
  - Null `Organization.Branding` → pure Sorcha defaults
- **`WelcomeEmailDispatcherTests`** (moq'd `ITransactionalEmailService` + in-memory `TenantDbContext`)
  - Sends once, sets `WelcomeSentAt`, saves
  - Second call is a no-op
  - Skips if `!EmailVerified`
  - Public-org-only membership → public template
  - Standard-org membership → invited template, branding from inviting org
- **`TransactionalEmailServiceTests`**
  - Each typed method renders the right template and calls `IEmailSender.SendAsync` with both HTML and text

### Integration tests updated

- `InvitationServiceTests`, `PasswordResetServiceTests`, `EmailVerificationService` integration tests swap their fake `IEmailSender` assertions from "body contains token" to "body contains verifyUrl / acceptUrl / resetUrl".

### Snapshot tests

- Render each of the six templates with a known model, assert against a committed `.html` / `.txt` fixture. Catches accidental copy changes or regressed layout.

---

## Rollout

- **No feature flag.** All changes are strict improvements — the old code was buggy (plaintext tokens). Flip on deploy.
- **Schema change folds into `InitialCreate`** (pre-release convention) — no separate migration, no runtime backfill in the migration itself.
- **No SMTP/ACS config change required** in production. New `LogoUrl / PrimaryColor / Tagline / ReplyTo` default if unset.
- **Monitoring:** existing `_logger.LogError` on send failure stays. Optionally add `email_send_failures_total{template}` counter — not blocking.
- **Smoke test on deploy:** trigger a real signup in staging, confirm verify email arrives with a clickable link and the welcome fires after. Trigger an invitation from a branded org, confirm logo + colour appear.

---

## Open questions (resolved during implementation, not blocking design)

- Exact Sorcha logo URL and where it's hosted (CDN, asset in UI project, or base64-embedded in the template).
- Final copy for the six templates — directionally right but can be polished before ship.
- Precise "What's next" links in `welcome-public.html` — depends on confirmed dashboard / demo / docs URLs.
- Sorcha tagline copy for footer (`EmailSettings.Tagline`).
- Whether to add `Mailpit` to `docker-compose.yml` alongside this work or defer (see follow-ups).

---

## Recommended follow-ups

- **Mailpit in docker-compose** (~1h) — local dev mail catcher, enables UI-level testing of emails.
- **In-app wallet-creation UX review** — ensure `CreateWallet.razor` enforces / strongly encourages recovery-phrase capture before the mnemonic is cleared from memory.
- **Per-org branding on verify / password reset** — revisit after MOB-007 ships org branding UI.
- **Delivery reliability pass** — retries, outbox, bounce handling.
- **Notification email categories** (Feature 062 territory) — pending action assigned, credential received, invitation accepted, password changed.
