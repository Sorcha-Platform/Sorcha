# JWT Workflows Reference

## Contents
- Adding JWT to a New Service
- Creating Custom Authorization Policies
- Testing Authentication
- Troubleshooting Authentication Failures

---

## Adding JWT to a New Service

### Checklist

Copy this checklist and track progress:
- [ ] Add `Sorcha.ServiceDefaults` project reference
- [ ] Call `builder.AddJwtAuthentication()` in Program.cs
- [ ] Create `AuthenticationExtensions.cs` with policies
- [ ] Call `builder.Services.Add{Service}Authorization()`
- [ ] Ensure `app.UseAuthentication()` before `app.UseAuthorization()`
- [ ] Apply `.RequireAuthorization()` to protected endpoints
- [ ] Test with valid and invalid tokens

### Step-by-Step

**1. Add Project Reference**

```xml
<!-- MyService.csproj -->
<ItemGroup>
  <ProjectReference Include="..\..\Common\Sorcha.ServiceDefaults\Sorcha.ServiceDefaults.csproj" />
</ItemGroup>
```

**2. Configure Authentication**

```csharp
// Program.cs
var builder = WebApplication.CreateBuilder(args);
builder.AddServiceDefaults();

// JWT authentication - auto-generates key in development
builder.AddJwtAuthentication();

// Service-specific authorization policies
builder.Services.AddMyServiceAuthorization();
```

**3. Create Authorization Policies**

```csharp
// Extensions/AuthenticationExtensions.cs
namespace MyService.Extensions;

public static class AuthenticationExtensions
{
    public static IServiceCollection AddMyServiceAuthorization(this IServiceCollection services)
    {
        services.AddAuthorization(options =>
        {
            // NOTE: a consumer-tier (citizen) token also carries org_id (Feature 136), so this
            // bare org_id/service check admits citizens. For a tier-sensitive (authoring/admin)
            // resource, fold the tier audience in via a requirement + handler — see the jwt skill
            // "Tier-aware policy (Feature 147)".
            options.AddPolicy("CanManageResources", policy =>
                policy.RequireAssertion(context =>
                {
                    var hasOrgId = context.User.Claims.Any(c => c.Type == "org_id" && !string.IsNullOrEmpty(c.Value));
                    var isService = context.User.Claims.Any(c => c.Type == "token_type" && c.Value == "service");
                    return hasOrgId || isService;
                }));

            options.AddPolicy("RequireService", policy =>
                policy.RequireClaim("token_type", "service"));
        });

        return services;
    }
}
```

**4. Configure Middleware Order**

```csharp
var app = builder.Build();

app.UseAuthentication(); // ← FIRST
app.UseAuthorization();  // ← SECOND

app.MapMyEndpoints();
app.Run();
```

**5. Protect Endpoints**

```csharp
// Endpoints/ResourceEndpoints.cs
public static void MapResourceEndpoints(this IEndpointRouteBuilder routes)
{
    var group = routes.MapGroup("/api/resources")
        .WithTags("Resources");

    group.MapGet("/", ListResources)
        .RequireAuthorization("CanManageResources");

    group.MapPost("/", CreateResource)
        .RequireAuthorization("CanManageResources");
        
    // Service-only endpoint
    group.MapPost("/internal/notify", NotifyChange)
        .RequireAuthorization("RequireService");
}
```

---

## Creating Custom Authorization Policies

### Requirement Types

| Requirement | Use Case | Example |
|-------------|----------|---------|
| `RequireRole()` | Role-based access | Admin-only endpoints |
| `RequireClaim()` | Claim existence/value | Token type checking |
| `RequireAssertion()` | Complex logic | OR conditions, claim validation |
| `RequireAuthenticatedUser()` | Any valid token | Public authenticated endpoints |

### Policy Naming Convention

```csharp
// Action-based naming
"CanManageBlueprints"    // CRUD operations
"CanExecuteBlueprints"   // Execution/invocation
"CanPublishBlueprints"   // Publishing/deployment

// Role-based naming
"RequireAdministrator"   // Admin role required
"RequireAuditor"         // Auditor role required

// Token-type naming
"RequireService"         // Service-to-service only
"RequireOrganizationMember"  // User with org context
```

### Feedback Loop

1. Create policy in `AuthenticationExtensions.cs`
2. Apply to endpoint: `.RequireAuthorization("PolicyName")`
3. Test with `curl` or HTTP client
4. If 403, inspect token claims: `jwt decode YOUR_TOKEN`
5. Adjust policy or token claims
6. Repeat until authorization works

---

## Testing Authentication

### Local Testing with curl

```bash
# 1. Get user token
TOKEN=$(curl -s -X POST http://localhost:5110/api/auth/login \
  -H "Content-Type: application/json" \
  -d '{"email":"admin@test.com","password":"password123"}' \
  | jq -r '.accessToken')

# 2. Use token on protected endpoint
curl -H "Authorization: Bearer $TOKEN" \
  http://localhost:5000/api/blueprints

# 3. Decode token to inspect claims
echo "$TOKEN" | cut -d'.' -f2 | base64 -d | jq
```

### Service Token Testing

```bash
# Get service token (OAuth2 client credentials)
SERVICE_TOKEN=$(curl -s -X POST http://localhost:5110/api/service-auth/token \
  -H "Content-Type: application/x-www-form-urlencoded" \
  -d "grant_type=client_credentials&client_id=blueprint-service&client_secret=secret&scope=blueprints:write" \
  | jq -r '.accessToken')

# Use for service-to-service calls
curl -H "Authorization: Bearer $SERVICE_TOKEN" \
  http://localhost:5000/api/blueprints/internal/sync
```

### Integration Test Pattern

```csharp
// Tests use WebApplicationFactory with auto-generated key
public class AuthenticationTests : IClassFixture<TenantServiceWebApplicationFactory>
{
    [Fact]
    public async Task ProtectedEndpoint_WithoutToken_Returns401()
    {
        var client = _factory.CreateClient();
        
        var response = await client.GetAsync("/api/blueprints");
        
        response.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
    }

    [Fact]
    public async Task ProtectedEndpoint_WithValidToken_Returns200()
    {
        var client = _factory.CreateClient();
        var token = await GetTestTokenAsync();
        client.DefaultRequestHeaders.Authorization = 
            new AuthenticationHeaderValue("Bearer", token);
        
        var response = await client.GetAsync("/api/blueprints");
        
        response.StatusCode.Should().Be(HttpStatusCode.OK);
    }
}
```

---

## Troubleshooting Authentication Failures

### 401 Unauthorized Flowchart

```
Token present? 
    ├─ No  → Add "Authorization: Bearer {token}" header
    └─ Yes → Token format valid?
               ├─ No  → Check "Bearer " prefix, no extra whitespace
               └─ Yes → Token expired?
                          ├─ Yes → Request new token or refresh
                          └─ No  → Signing key matches?
                                    ├─ No  → Sync JwtSettings across services
                                    └─ Yes → Issuer/Audience match?
                                              ├─ No  → Check JwtSettings:Issuer and :Audience
                                              └─ Yes → Check logs for specific error
```

### 403 Forbidden Flowchart

```
Token validates successfully?
    └─ Yes → Required claims present?
               ├─ No  → Add claims when issuing token
               └─ Yes → Claim values correct?
                          ├─ No  → Check exact values (e.g., "service" not "Service")
                          └─ Yes → Policy logic correct?
                                    └─ Debug RequireAssertion with logging
```

### Common Issues

**Issue: Different signing keys across services**

```bash
# Check: Services should share the same key
grep -r "SigningKey" src/Services/*/appsettings*.json

# Fix: Use shared config or environment variable
export JwtSettings__SigningKey="shared-key-min-32-chars"
```

**Issue: Token works locally but not in Docker**

```bash
# Check: InstallationName may derive different issuer/audience
docker exec container-name env | grep Jwt

# Fix: Explicit configuration in docker-compose
environment:
  - JwtSettings__Issuer=http://localhost
  - JwtSettings__Audience__0=http://localhost
```

**Issue: Middleware order wrong**

```csharp
// Symptoms: Always 401 or 403 regardless of token
// Check Program.cs for correct order:
app.UseAuthentication(); // Must be BEFORE
app.UseAuthorization();  // This one
```

### Debugging Claims

```csharp
// Temporarily add to endpoint for debugging
app.MapGet("/debug/claims", (ClaimsPrincipal user) =>
{
    return Results.Ok(user.Claims.Select(c => new { c.Type, c.Value }));
}).RequireAuthorization();
```

### JwtBearerEvents Logging

Already configured in `JwtAuthenticationExtensions.cs:196-227`:

```csharp
options.Events = new JwtBearerEvents
{
    OnAuthenticationFailed = context =>
    {
        logger.LogWarning(context.Exception, "JWT authentication failed: {Message}",
            context.Exception.Message);
        return Task.CompletedTask;
    },
    OnTokenValidated = context =>
    {
        var userId = context.Principal?.FindFirst(ClaimTypes.NameIdentifier)?.Value;
        var orgId = context.Principal?.FindFirst("org_id")?.Value;
        logger.LogDebug("Token validated for user {UserId}, org {OrgId}", userId, orgId);
        return Task.CompletedTask;
    }
};
```

Check logs at `Debug` level for validation details.