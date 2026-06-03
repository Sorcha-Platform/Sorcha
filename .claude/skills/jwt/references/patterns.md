# JWT Patterns Reference

## Contents
- Token Generation Patterns
- Token Validation Configuration
- Authorization Policy Patterns
- Claims Extraction
- Anti-Patterns

---

## Token Generation Patterns

### User Token Generation

Location: `src/Services/Sorcha.Tenant.Service/Services/TokenService.cs:65-115`

```csharp
public async Task<TokenResponse> GenerateUserTokenAsync(
    UserIdentity user,
    Organization organization,
    CancellationToken cancellationToken = default)
{
    var accessTokenJti = Guid.NewGuid().ToString();
    var claims = new List<Claim>
    {
        new(JwtRegisteredClaimNames.Sub, user.Id.ToString()),
        new(JwtRegisteredClaimNames.Email, user.Email),
        new(JwtRegisteredClaimNames.Jti, accessTokenJti),
        new("name", user.DisplayName),
        new("org_id", organization.Id.ToString()),
        new("org_name", organization.Name),
        new("token_type", "user")
    };

    // Add role claims
    foreach (var role in user.Roles)
    {
        claims.Add(new Claim(ClaimTypes.Role, role.ToString()));
    }

    var accessTokenExpiry = DateTimeOffset.UtcNow.AddMinutes(_config.AccessTokenLifetimeMinutes);
    var accessToken = GenerateToken(claims, accessTokenExpiry);
    
    // Track for revocation
    await _revocationService.TrackTokenAsync(accessTokenJti, user.Id.ToString(), 
        organization.Id.ToString(), accessTokenExpiry, cancellationToken);

    return new TokenResponse
    {
        AccessToken = accessToken,
        RefreshToken = GenerateRefreshToken(...),
        ExpiresIn = _config.AccessTokenLifetimeMinutes * 60
    };
}
```

### Service Token Generation

```csharp
// Service tokens include scopes, NO refresh token
public Task<TokenResponse> GenerateServiceTokenAsync(
    ServicePrincipal servicePrincipal,
    Guid? delegatedUserId = null,
    Guid? delegatedOrgId = null,
    CancellationToken cancellationToken = default)
{
    var claims = new List<Claim>
    {
        new(JwtRegisteredClaimNames.Sub, servicePrincipal.Id.ToString()),
        new(JwtRegisteredClaimNames.Jti, Guid.NewGuid().ToString()),
        new("client_id", servicePrincipal.ClientId),
        new("service_name", servicePrincipal.ServiceName),
        new("token_type", "service")
    };

    // Add scopes
    foreach (var scope in servicePrincipal.Scopes)
    {
        claims.Add(new Claim("scope", scope));
    }

    // Optional: delegation claims for acting on behalf of user
    if (delegatedUserId.HasValue)
        claims.Add(new Claim("delegated_user_id", delegatedUserId.Value.ToString()));

    var accessTokenExpiry = DateTimeOffset.UtcNow.AddHours(_config.ServiceTokenLifetimeHours);
    return Task.FromResult(new TokenResponse
    {
        AccessToken = GenerateToken(claims, accessTokenExpiry),
        RefreshToken = string.Empty, // Service tokens don't refresh
        ExpiresIn = _config.ServiceTokenLifetimeHours * 3600
    });
}
```

---

## Token Validation Configuration

Location: `src/Common/Sorcha.ServiceDefaults/JwtAuthenticationExtensions.cs:175-194`

```csharp
builder.Services.AddAuthentication(JwtBearerDefaults.AuthenticationScheme)
    .AddJwtBearer(options =>
    {
        options.TokenValidationParameters = new TokenValidationParameters
        {
            ValidateIssuer = jwtSettings.ValidateIssuer,
            ValidIssuer = jwtSettings.Issuer,

            ValidateAudience = jwtSettings.ValidateAudience,
            ValidAudiences = jwtSettings.Audience,

            ValidateIssuerSigningKey = jwtSettings.ValidateIssuerSigningKey,
            IssuerSigningKey = new SymmetricSecurityKey(keyBytes),

            ValidateLifetime = jwtSettings.ValidateLifetime,
            ClockSkew = TimeSpan.FromMinutes(jwtSettings.ClockSkewMinutes),

            NameClaimType = ClaimTypes.Name,
            RoleClaimType = ClaimTypes.Role
        };
    });
```

### DO: Use Shared JwtSettings

```csharp
// GOOD - All services validate tokens the same way
builder.AddJwtAuthentication(); // From ServiceDefaults
```

### DON'T: Configure JWT Manually Per Service

```csharp
// BAD - Each service might have different validation
builder.Services.AddAuthentication()
    .AddJwtBearer(options => {
        options.TokenValidationParameters = new TokenValidationParameters
        {
            ValidIssuer = "hardcoded-value" // ← Will break if not matching
        };
    });
```

**Why This Breaks:** Tokens from Tenant Service won't validate if issuer/audience/key differ.

---

## Authorization Policy Patterns

### Role-Based Policy

```csharp
options.AddPolicy("RequireAdministrator", policy =>
    policy.RequireRole("Administrator"));
```

### Claim-Based Policy

```csharp
options.AddPolicy("RequireOrganizationMember", policy =>
    policy.RequireClaim("org_id"));

options.AddPolicy("RequireService", policy =>
    policy.RequireClaim("token_type", "service"));
```

### Composite Policy (OR logic)

```csharp
// Either org member OR service token (benign example — no tier sensitivity)
options.AddPolicy("CanReadReports", policy =>
    policy.RequireAssertion(context =>
    {
        var hasOrgId = context.User.Claims.Any(c => c.Type == "org_id" && !string.IsNullOrEmpty(c.Value));
        var isService = context.User.Claims.Any(c => c.Type == "token_type" && c.Value == "service");
        return hasOrgId || isService;
    }));
```

> **⚠️ Tier sensitivity (Feature 136/147).** A **consumer**-tier (citizen) token also carries
> `org_id`, so `hasOrgId || isService` admits citizens. For an authoring/admin gate, the tier
> **audience** must be part of the check. Do NOT hard-code the audience string — resolve
> `SorchaAudiences` from DI in a requirement + handler. `CanManageBlueprints` is implemented this
> way: `(token_type==service AND :service aud) || (org_id AND :platform aud)` — see
> `Sorcha.Blueprint.Service/Authorization/BlueprintManagementAuthorizationHandler.cs`.

### Multiple Conditions (AND logic)

```csharp
// Service with delegation authority
options.AddPolicy("RequireDelegatedAuthority", policy =>
{
    policy.RequireClaim("token_type", "service");
    policy.RequireClaim("delegated_user_id");
});
```

---

## Claims Extraction

### In Endpoint Handler

```csharp
async Task<IResult> GetBlueprint(
    ClaimsPrincipal user,
    Guid blueprintId,
    IBlueprintService service)
{
    var userId = user.FindFirst(JwtRegisteredClaimNames.Sub)?.Value;
    var orgId = user.FindFirst("org_id")?.Value;
    var tokenType = user.FindFirst("token_type")?.Value;
    
    // For service tokens, check scope
    if (tokenType == "service")
    {
        var hasScope = user.FindAll("scope").Any(c => c.Value == "blueprints:read");
        if (!hasScope) return Results.Forbid();
    }
    
    return Results.Ok(await service.GetAsync(blueprintId, orgId));
}
```

---

## Anti-Patterns

### WARNING: Missing Middleware Order

**The Problem:**

```csharp
// BAD - Authorization before Authentication
var app = builder.Build();
app.UseAuthorization();  // ← Runs first
app.UseAuthentication(); // ← Token not validated yet
```

**Why This Breaks:**
1. Authorization checks run against unauthenticated principal
2. All `[Authorize]` endpoints return 401 even with valid token
3. Extremely confusing to debug

**The Fix:**

```csharp
// GOOD - Authentication MUST come first
app.UseAuthentication();
app.UseAuthorization();
```

---

### WARNING: Hardcoded Signing Key in Production

**The Problem:**

```json
// appsettings.json committed to repo
{
  "JwtSettings": {
    "SigningKey": "my-super-secret-key-that-everyone-can-see"
  }
}
```

**Why This Breaks:**
1. Anyone with repo access can forge valid tokens
2. Key rotation requires code deployment
3. Violates security compliance requirements

**The Fix:**

```bash
# Production: Use environment variable
export JwtSettings__SigningKey="$(az keyvault secret show --name JwtKey --query value -o tsv)"

# Or Azure Key Vault reference in appsettings
{
  "JwtSettings": {
    "SigningKey": "@Microsoft.KeyVault(SecretUri=https://vault.vault.azure.net/secrets/jwt-key/)"
  }
}
```

---

### WARNING: Missing Token Revocation Check

**The Problem:**

```csharp
// BAD - Only validates signature, not revocation status
var principal = _tokenHandler.ValidateToken(token, _validationParameters, out _);
return new TokenIntrospectionResponse { Active = true, ... };
```

**Why This Breaks:** Compromised tokens remain valid until expiry.

**The Fix:**

```csharp
// GOOD - Check revocation before accepting
var jti = jwt.Claims.FirstOrDefault(c => c.Type == JwtRegisteredClaimNames.Jti)?.Value;
if (!string.IsNullOrEmpty(jti) && await _revocationService.IsTokenRevokedAsync(jti))
{
    return new TokenIntrospectionResponse { Active = false };
}
```

See the **redis** skill for revocation service implementation.