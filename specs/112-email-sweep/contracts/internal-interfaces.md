# Contract — Internal Interfaces

**Date**: 2026-04-24
**Scope**: C# interface surface introduced or modified by Feature 112. No HTTP endpoints are added; this feature is service-internal.

This file enumerates the interface contracts that downstream callers will consume. Any change to these signatures after plan approval is a contract change and requires a spec amendment.

---

## IEmailSender *(modified — tightened)*

**Location**: `src/Services/Sorcha.Tenant.Service/Services/IEmailSender.cs`

**Before**: three methods — a generic `SendAsync` plus typed `SendVerificationEmailAsync` and `SendInvitationEmailAsync`.

**After**:

```csharp
namespace Sorcha.Tenant.Service.Services;

/// <summary>
/// Backend-agnostic transactional email sender. Implementations ship a multipart
/// HTML+plaintext message to the configured SMTP or cloud backend.
/// </summary>
public interface IEmailSender
{
    /// <summary>
    /// Sends a multipart email (HTML + plaintext alternative) to a single recipient.
    /// Both bodies MUST be provided — the plaintext alternative is mandatory per FR-002.
    /// </summary>
    Task SendAsync(
        string to,
        string subject,
        string htmlBody,
        string textBody,
        CancellationToken cancellationToken = default);
}
```

**Breaking change**: yes. The previous `SendAsync(to, subject, htmlBody, ct)` loses a parameter; callers that did not supply a plaintext body MUST be updated. All Tenant Service callers will be migrated in the same change set. No external NuGet consumers depend on this interface.

**Removed methods**: `SendVerificationEmailAsync`, `SendInvitationEmailAsync` — dead code (FR-022).

---

## IEmailTemplateRenderer *(new)*

**Location**: `src/Services/Sorcha.Tenant.Service/Services/IEmailTemplateRenderer.cs`

```csharp
namespace Sorcha.Tenant.Service.Services;

/// <summary>
/// Renders a named email template (HTML + plaintext pair) against a model.
/// Templates are pre-compiled at startup; rendering is a pure function.
/// </summary>
public interface IEmailTemplateRenderer
{
    /// <summary>
    /// Renders the template pair registered under <paramref name="templateName"/>
    /// against <paramref name="model"/>, returning both HTML and plaintext bodies.
    /// </summary>
    /// <param name="templateName">
    /// Template name without extension — one of:
    /// "verify", "invite", "reset", "welcome-public", "welcome-invited".
    /// </param>
    /// <param name="model">
    /// Strongly-typed view model. Template field paths are resolved against
    /// the model's public properties. Use snake_case in templates
    /// (Scriban convention) to reference PascalCase .NET properties.
    /// </param>
    /// <returns>Rendered HTML body and plaintext body.</returns>
    /// <exception cref="KeyNotFoundException">
    /// Thrown when no template pair is registered under <paramref name="templateName"/>.
    /// </exception>
    (string Html, string Text) Render(string templateName, object model);
}
```

**Default implementation**: `ScribanEmailTemplateRenderer` — singleton, parses all embedded `.html` and `.txt` resources once at startup into a `Dictionary<string, Template>`. Fails fast on parse errors.

---

## IEmailBrandingResolver *(new)*

**Location**: `src/Services/Sorcha.Tenant.Service/Services/IEmailBrandingResolver.cs`

```csharp
namespace Sorcha.Tenant.Service.Services;

/// <summary>
/// Resolves the <see cref="EmailBranding"/> surface used by template rendering.
/// Sorcha-default branding comes from <c>EmailSettings</c>. Per-organisation
/// overrides apply only to invitation and invited-welcome emails.
/// </summary>
public interface IEmailBrandingResolver
{
    /// <summary>Returns the Sorcha platform default branding.</summary>
    EmailBranding GetDefault();

    /// <summary>
    /// Returns branding for a message whose inviting or joining organisation is
    /// <paramref name="organization"/>. Applies per-field fallback to Sorcha defaults.
    /// </summary>
    EmailBranding GetForOrganization(Organization organization);
}
```

**Fallback semantics**: see `data-model.md § Resolution rules`. Org name always wins. Logo and primary colour fall back per-field to Sorcha defaults.

---

## ITransactionalEmailService *(new — the primary facade)*

**Location**: `src/Services/Sorcha.Tenant.Service/Services/ITransactionalEmailService.cs`

This is the interface application code calls. It hides template names, model construction, and branding resolution.

```csharp
namespace Sorcha.Tenant.Service.Services;

public interface ITransactionalEmailService
{
    Task SendVerificationAsync(
        VerifyEmailDispatch dispatch,
        CancellationToken ct = default);

    Task SendInvitationAsync(
        InviteEmailDispatch dispatch,
        CancellationToken ct = default);

    Task SendPasswordResetAsync(
        ResetPasswordDispatch dispatch,
        CancellationToken ct = default);

    Task SendWelcomeAsync(
        WelcomeDispatchContext context,
        CancellationToken ct = default);
}

// --- dispatch records (plain C# records, Tenant Service-internal) ---

public sealed record VerifyEmailDispatch(
    string ToEmail,
    string DisplayName,
    string VerifyUrl,
    int ExpiresInHours);

public sealed record InviteEmailDispatch(
    string ToEmail,
    string InviterName,
    Organization InvitingOrganization,   // source of branding + org name
    string RoleDisplayName,
    string AcceptUrl,
    int ExpiresInDays);

public sealed record ResetPasswordDispatch(
    string ToEmail,
    string DisplayName,
    string ResetUrl,
    int ExpiresInMinutes);

public sealed record WelcomeDispatchContext(
    PlatformUser User,
    WelcomeVariant Variant,
    Organization? InvitingOrganization);  // required when Variant == Invited

public enum WelcomeVariant { Public, Invited }
```

**Semantics**:
- All four methods build the appropriate view model, resolve branding, render via `IEmailTemplateRenderer`, and hand off to `IEmailSender.SendAsync`.
- `SendWelcomeAsync` does NOT write `WelcomeSentAt` itself — that responsibility belongs to `WelcomeEmailDispatcher`, the only code path that should call `SendWelcomeAsync`. The facade is purely stateless.

---

## WelcomeEmailDispatcher *(new — concrete class, no interface)*

**Location**: `src/Services/Sorcha.Tenant.Service/Services/WelcomeEmailDispatcher.cs`

Not an interface — a concrete scoped service. The idempotency guarantees are internal to its implementation.

```csharp
namespace Sorcha.Tenant.Service.Services;

public sealed class WelcomeEmailDispatcher
{
    public WelcomeEmailDispatcher(
        TenantDbContext dbContext,
        ITransactionalEmailService transactional,
        ILogger<WelcomeEmailDispatcher> logger);

    /// <summary>
    /// Sends the appropriate welcome email if and only if the user is eligible
    /// (email verified AND welcome not previously sent). Sets <c>WelcomeSentAt</c>
    /// on success and persists. Safe to call from any number of trigger points.
    /// Failures are logged and swallowed — must not block the calling flow (FR-020).
    /// </summary>
    public Task SendIfPendingAsync(PlatformUser user, CancellationToken ct);
}
```

**Contract**:
- Idempotent — second and subsequent calls for the same user are a no-op.
- Non-throwing — swallows send exceptions after logging. Verification or login must proceed regardless.
- Writes `WelcomeSentAt` only on successful send.
- Decides variant internally by inspecting `PlatformUserOrgMembership` for the user — no variant parameter on the public surface.

---

## Caller migration summary

| Caller | Before | After |
|--------|--------|-------|
| `EmailVerificationService.GenerateAndSendVerificationAsync` | `IEmailSender.SendAsync(email, subject, plaintextTokenBody, ct)` | `ITransactionalEmailService.SendVerificationAsync(dispatch, ct)` |
| `EmailVerificationService.VerifyTokenAsync` | *(no welcome dispatch)* | Append `await _welcomeDispatcher.SendIfPendingAsync(platformUser, ct)` |
| `InvitationService.CreateInvitationAsync` | `IEmailSender.SendAsync(email, subject, plaintextTokenBody, ct)` | `ITransactionalEmailService.SendInvitationAsync(dispatch, ct)` (loads `Organization` + branding) |
| `PasswordResetService.SendResetLinkAsync` | `IEmailSender.SendAsync(email, subject, BuildResetEmailHtml(…), ct)` | `ITransactionalEmailService.SendPasswordResetAsync(dispatch, ct)` — `BuildResetEmailHtml` deleted |
| `LoginService` success path | *(no welcome dispatch)* | Append `await _welcomeDispatcher.SendIfPendingAsync(platformUser, ct)` |
| `SocialLoginService` success path | *(no welcome dispatch)* | Append `await _welcomeDispatcher.SendIfPendingAsync(platformUser, ct)` |

All six callers already hold a DI-injected dependency on either `IEmailSender` (for the first four) or an auth-pipeline seam (for the last two). The migration is a DI swap plus a call-site change, not a structural rewrite.
