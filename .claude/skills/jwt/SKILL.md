---
name: jwt
description: |
  Implements JWT Bearer authentication for service-to-service and user authorization.
  Use when: Configuring authentication, creating authorization policies, issuing/validating tokens, or troubleshooting 401/403 errors.
allowed-tools: Read, Edit, Write, Glob, Grep, Bash, mcp__context7__resolve-library-id, mcp__context7__query-docs
---

# JWT Authentication Skill

Sorcha uses **JWT Bearer authentication** with the **Tenant Service** as the token issuer. All services validate tokens using shared `JwtSettings` from `Sorcha.ServiceDefaults`. Tokens support three types: user (email/password), service (client credentials), and delegated (service acting on behalf of user).

## Tiered audiences + issuer hardening (Spec 136 / Feature 136)

**The `aud` claim is the trust-tier boundary.** Every token carries an installation-namespaced, tier-scoped audience — `{installation}:consumer | platform | service | enrol-session` — derived from the **single source of truth** `SorchaAudiences` in `Sorcha.ServiceDefaults.Auth`. `InstallationName` (default `sorcha`, overridable per deployment) drives **both** the audience namespace and the issuer. Never hand-build an audience string — use `new SorchaAudiences(installationName).For(Tier.X)` / `.All`.

| Tier | Audience | Who | Claim set |
|------|----------|-----|-----------|
| `Consumer` | `{install}:consumer` | citizen / wallet holder (web + PWA) | `sub`, `email`, `platform_user_id`, `org_id`/`org_name` (home/public org) — **omits `roles`/`wallet_address`**. Inert on platform surfaces by audience + no-roles, not by absence of org context. |
| `Platform` | `{install}:platform` | admin / designer / auditor / org operator | full user shape: + `org_id`, `org_name`, `roles[]`, `wallet_address?` |
| `Service` | `{install}:service` | service-to-service / internal | `client_id`, `service_name`, `scope[]`, `delegated_*?` |
| `EnrolSession` | `{install}:enrol-session` | one-time device pairing | `scope:"enrol"`, single-use JTI |

**Validation = authenticate-broad / authorize-narrow.** The bearer pipeline accepts any of the installation's four tier audiences (`ValidAudiences = SorchaAudiences.All`), rejecting cross-installation tokens. The specific tier is enforced **per endpoint** by policies registered in `AddSorchaAuthorizationPolicies` (called by every service): `RequireConsumerAudience`, `RequirePlatformAudience`, and the extended `RequireService` (now `token_type==service` **AND** `aud==:service`; `CanWriteDockets`/`CanReportRegisterObservation` mirror it). They resolve the installation's `SorchaAudiences` from DI at request time via `TierAudienceAuthorizationHandler` (built from `JwtSettings:InstallationName`) — no per-host wiring needed. Use `AuthorizationPolicyExtensions.HasTierAudience(user, audiences, tier)` to test the predicate.

**Issuer hardening.** No shared default. `SorchaIssuer.Resolve(explicitIssuer, installationName, allowDevLocalFallback)`: explicit wins → `urn:sorcha:{installation}` → (non-prod) `urn:sorcha:dev-local` → otherwise **throws at startup** (fail-closed in Production/Staging). `allowDevLocalFallback = SorchaIssuer.AllowsDevLocalFallback(env)` is true for any non-Production/Staging environment (Development, Testing). Mint side (`TokenService`, `EnrolSessionService` via the Tenant `JwtConfiguration`) and validate side (`AddJwtAuthentication`) MUST resolve issuer + audiences through the **same** `SorchaIssuer`/`SorchaAudiences`, or tokens self-reject.

**Mint mapping today:** `TokenService.GenerateUserTokenAsync` → `platform`; `GenerateServiceTokenAsync` → `service`; `EnrolSessionService` redeem → `consumer`, mint → `enrol-session`. Refresh tokens carry a `tier` claim and re-mint the same tier.

> **In-progress (US1/US4/US5, not yet landed):** tier *selection* at login (consumer-vs-platform from `returnTo`), per-endpoint tier *classification* across services, the `RequireService` `:service`-audience extension, and the `IdentityMetrics` (`Sorcha.Identity` meter) DI/OTel wiring. Until US1 lands, endpoints are not yet tier-gated — any valid tier audience authenticates. Spec/plan/tasks: `specs/136-jwt-audience-tiers/`.

**No migration:** coordinated config rollout; existing tokens expire (pre-release).

## Quick Start

### Service Authentication Setup

```csharp
// Program.cs - Any Sorcha service
var builder = WebApplication.CreateBuilder(args);

// 1. Add JWT authentication (shared key auto-generated in dev)
builder.AddJwtAuthentication();

// 2. Add service-specific authorization policies
builder.Services.AddBlueprintAuthorization();

var app = builder.Build();

// 3. CRITICAL: Order matters!
app.UseAuthentication();
app.UseAuthorization();

app.MapBlueprintEndpoints();
app.Run();
```

### Protect an Endpoint

```csharp
// Minimal API pattern
group.MapPost("/", CreateBlueprint)
    .WithName("CreateBlueprint")
    .RequireAuthorization("CanManageBlueprints");
```

## Key Concepts

| Concept | Usage | Example |
|---------|-------|---------|
| Token Types | Differentiate user vs service | `token_type` claim: `"user"` or `"service"` |
| Organization Scope | Isolate tenant data | `org_id` claim in token |
| Signing Key | Symmetric HMAC-SHA256 | Auto-generated in dev, Azure Key Vault in prod |
| Token Lifetime | Configurable per type | Access: 60min, Refresh: 24hr, Service: 8hr |

## Common Patterns

### Custom Authorization Policy

**When:** Endpoint requires specific claims beyond role-based auth.

```csharp
// AuthenticationExtensions.cs
options.AddPolicy("CanPublishBlueprints", policy =>
    policy.RequireAssertion(context =>
        context.User.Claims.Any(c => c.Type == "can_publish_blueprint" && c.Value == "true")
        || context.User.IsInRole("Administrator")));
```

### Tier-aware policy (fold the audience in — Feature 147)

**When:** A gate must admit a service-tier caller OR a specific human-tier caller. Because a
**consumer**-tier token also carries `org_id` (Feature 136), a bare `hasOrgId || isService` check
lets a citizen through — the tier **audience** must be part of the gate. Resolve the expected
audience from the DI singleton `SorchaAudiences` (never hard-code the string), so the check lives in
a requirement + handler rather than an inline assertion. `CanManageBlueprints` is the canonical example:

```csharp
// Sorcha.Blueprint.Service/Authorization/BlueprintManagementAuthorizationHandler.cs
//   succeed iff (token_type==service AND HasTierAudience(user, audiences, Tier.Service))
//            || (org_id present       AND HasTierAudience(user, audiences, Tier.Platform))
services.AddSingleton<IAuthorizationHandler, BlueprintManagementAuthorizationHandler>();
options.AddPolicy("CanManageBlueprints", policy =>
    policy.AddRequirements(new BlueprintManagementRequirement()));
```

The Wallet Service's `CanRecoverSystemWallet` (system-wallet BIP39 import) follows the same shape:
service-tier **OR** (`Administrator`/`SystemAdmin` role AND `:platform` audience).

### Extract Claims in Handler

**When:** Need user/org context in endpoint logic.

```csharp
async Task<IResult> HandleRequest(ClaimsPrincipal user, ...)
{
    var userId = user.FindFirst(JwtRegisteredClaimNames.Sub)?.Value;
    var orgId = user.FindFirst("org_id")?.Value;
    
    if (string.IsNullOrEmpty(orgId))
        return Results.Forbid();
    
    // Use orgId for data isolation
}
```

## See Also

- [patterns](references/patterns.md) - Token generation, validation, policies
- [workflows](references/workflows.md) - Setup, testing, troubleshooting

## Related Skills

- See the **minimal-apis** skill for endpoint configuration with `.RequireAuthorization()`
- See the **aspire** skill for shared configuration via `ServiceDefaults`
- See the **redis** skill for token revocation tracking
- See the **yarp** skill for gateway-level authentication

## Documentation Resources

> Fetch latest JWT/authentication documentation with Context7.

**How to use Context7:**
1. Use `mcp__context7__resolve-library-id` to search for "asp.net core authentication jwt"
2. **Prefer website documentation** (IDs starting with `/websites/`) over source code repositories
3. Query with `mcp__context7__query-docs` using the resolved library ID

**Recommended Queries:**
- "JWT Bearer authentication setup"
- "authorization policies claims"
- "token validation parameters"