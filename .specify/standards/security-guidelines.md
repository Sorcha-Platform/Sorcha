# Security Guidelines

**Version:** 1.1.0
**Status:** MANDATORY
**Audience:** All developers and AI assistants

---

## Overview

This document defines the security requirements and best practices for the Sorcha project. Security is a critical concern, and all code MUST adhere to these guidelines.

**Note**: Authentication, Authorization, and Rate Limiting are NOT yet implemented in the current codebase. This document describes required future implementation.

---

## 1. OWASP Top 10 Protection

### A01: Broken Access Control

#### Authorization Checks (Future Implementation)

```csharp
// Verify user has permission
app.MapDelete("/api/blueprints/{id}", async (
    string id,
    ClaimsPrincipal user,
    IBlueprintRepository repo) =>
{
    var blueprint = await repo.GetByIdAsync(id);
    if (blueprint == null)
        return Results.NotFound();

    // Verify ownership
    if (blueprint.OwnerId != user.GetUserId())
        return Results.Forbid();

    await repo.DeleteAsync(id);
    return Results.NoContent();
})
.RequireAuthorization();
```

### A02: Cryptographic Failures

#### Secure Data Storage

```csharp
// Use Data Protection API
public class SecureDataService
{
    private readonly IDataProtector _protector;

    public SecureDataService(IDataProtectionProvider provider)
    {
        _protector = provider.CreateProtector("Sorcha.Blueprint.SecureData");
    }

    public string EncryptSensitiveData(string plaintext)
    {
        return _protector.Protect(plaintext);
    }
}
```

#### Secrets Management

```csharp
// Use Azure Key Vault in production
builder.Configuration.AddAzureKeyVault(
    new Uri($"https://{keyVaultName}.vault.azure.net/"),
    new DefaultAzureCredential());

// Development: User Secrets
builder.Configuration.AddUserSecrets<Program>();

// NEVER hardcode secrets
const string ApiKey = "sk_live_abc123xyz"; // VIOLATION
```

### A03: Injection

#### Input Validation

```csharp
// Parameterized queries (EF Core)
var blueprints = await context.Blueprints
    .Where(b => b.Title.Contains(searchTerm)) // Safe
    .ToListAsync();

// Validate and sanitize input
public async Task<IResult> CreateBlueprint(
    BlueprintRequest request,
    IValidator<BlueprintRequest> validator)
{
    var validationResult = await validator.ValidateAsync(request);
    if (!validationResult.IsValid)
        return Results.ValidationProblem(validationResult.ToDictionary());

    request = request with
    {
        Title = HtmlEncoder.Default.Encode(request.Title),
        Description = HtmlEncoder.Default.Encode(request.Description)
    };
}

// WRONG: SQL Injection
var query = $"SELECT * FROM Blueprints WHERE Title = '{searchTerm}'"; // VIOLATION
```

#### JSON Schema Validation

```csharp
public async Task<ValidationResult> ValidateBlueprintAsync(JsonNode blueprintJson)
{
    var schema = await _schemaRepository.GetBlueprintSchemaAsync();

    var evaluationResult = schema.Evaluate(blueprintJson, new EvaluationOptions
    {
        OutputFormat = OutputFormat.List
    });

    if (!evaluationResult.IsValid)
    {
        throw new BlueprintValidationException(
            "Blueprint JSON does not conform to schema");
    }

    return ValidationResult.Success;
}
```

### A04: Insecure Design

#### Security by Design Principles

1. **Least Privilege**: Grant minimum necessary permissions
2. **Defense in Depth**: Multiple layers of security
3. **Fail Securely**: Default to deny access
4. **Separation of Duties**: Require multiple approvals for sensitive actions

### A05: Security Misconfiguration

#### Security Headers

```csharp
app.Use(async (context, next) =>
{
    context.Response.Headers.Append("X-Frame-Options", "DENY");
    context.Response.Headers.Append("X-Content-Type-Options", "nosniff");
    context.Response.Headers.Append("X-XSS-Protection", "1; mode=block");
    context.Response.Headers.Append(
        "Content-Security-Policy",
        "default-src 'self'; script-src 'self' 'unsafe-inline'");
    context.Response.Headers.Append(
        "Strict-Transport-Security",
        "max-age=31536000; includeSubDomains");
    context.Response.Headers.Append("Referrer-Policy", "strict-origin-when-cross-origin");

    await next();
});
```

#### HTTPS Enforcement

```csharp
if (!app.Environment.IsDevelopment())
{
    app.UseHttpsRedirection();
    app.UseHsts();
}
```

#### CORS Configuration

```csharp
// CORRECT: Restrictive CORS
builder.Services.AddCors(options =>
{
    options.AddPolicy("AllowedOrigins", policy =>
    {
        policy.WithOrigins(
                "https://n1.sorcha.dev",
                "https://designer.sorcha.dev")
            .AllowAnyMethod()
            .AllowAnyHeader()
            .AllowCredentials();
    });
});

// WRONG: Permissive CORS
policy.AllowAnyOrigin(); // VIOLATION
```

### A06: Vulnerable and Outdated Components

#### Dependency Management

```bash
# Check for vulnerabilities
dotnet list package --vulnerable --include-transitive

# Update packages regularly
dotnet outdated
```

```xml
<!-- Enable NuGet package validation -->
<PropertyGroup>
  <EnablePackageValidation>true</EnablePackageValidation>
  <NuGetAudit>true</NuGetAudit>
  <NuGetAuditMode>all</NuGetAuditMode>
  <NuGetAuditLevel>low</NuGetAuditLevel>
</PropertyGroup>
```

### A07: Identification and Authentication Failures

#### Strong Authentication (Future Implementation)

```csharp
builder.Services.AddAuthentication(JwtBearerDefaults.AuthenticationScheme)
    .AddJwtBearer(options =>
    {
        options.Authority = "https://identity.sorcha.dev";
        options.Audience = "sorcha-api";
        options.TokenValidationParameters = new TokenValidationParameters
        {
            ValidateIssuer = true,
            ValidateAudience = true,
            ValidateLifetime = true,
            ValidateIssuerSigningKey = true,
            ClockSkew = TimeSpan.FromMinutes(5)
        };
    });
```

#### Session Management

```csharp
builder.Services.AddSession(options =>
{
    options.Cookie.HttpOnly = true;
    options.Cookie.SecurePolicy = CookieSecurePolicy.Always;
    options.Cookie.SameSite = SameSiteMode.Strict;
    options.IdleTimeout = TimeSpan.FromMinutes(30);
    options.Cookie.IsEssential = true;
});
```

### A08: Software and Data Integrity Failures

#### Anti-Tampering

```csharp
public class DataIntegrityService
{
    public string ComputeHash(byte[] data)
    {
        using var sha256 = SHA256.Create();
        var hash = sha256.ComputeHash(data);
        return Convert.ToBase64String(hash);
    }

    public bool VerifyHash(byte[] data, string expectedHash)
    {
        var actualHash = ComputeHash(data);
        return actualHash == expectedHash;
    }
}
```

### A09: Security Logging and Monitoring Failures

#### Audit Logging

```csharp
public class AuditLogger
{
    private readonly ILogger<AuditLogger> _logger;

    public void LogAuthentication(string userId, bool success)
    {
        _logger.LogInformation(
            "Authentication {Status} for user {UserId}",
            success ? "succeeded" : "failed",
            userId);
    }

    public void LogAuthorizationFailure(string userId, string resource)
    {
        _logger.LogWarning(
            "User {UserId} denied access to {Resource}",
            userId,
            resource);
    }

    public void LogDataModification(string userId, string resourceId, string action)
    {
        _logger.LogInformation(
            "User {UserId} {Action} resource {ResourceId}",
            userId,
            action,
            resourceId);
    }
}
```

#### Sensitive Data Protection

```csharp
// CORRECT: Never log sensitive data
_logger.LogInformation("User {UserId} logged in", userId);

// WRONG: Logging sensitive data
_logger.LogInformation("User logged in with password {Password}", password); // VIOLATION
_logger.LogInformation("Credit card {CardNumber} processed", cardNumber); // VIOLATION

// CORRECT: Log with masking
_logger.LogInformation(
    "Credit card ending in {Last4} processed",
    cardNumber.Substring(cardNumber.Length - 4));
```

### A10: Server-Side Request Forgery (SSRF)

#### Validate URLs

```csharp
public async Task<string> FetchExternalDataAsync(string url)
{
    if (!Uri.TryCreate(url, UriKind.Absolute, out var uri))
        throw new ArgumentException("Invalid URL");

    var allowedHosts = new[] { "api.trusted.com", "data.sorcha.dev" };
    if (!allowedHosts.Contains(uri.Host))
        throw new SecurityException($"Host {uri.Host} not allowed");

    var address = Dns.GetHostAddresses(uri.Host).FirstOrDefault();
    if (address != null && IsInternalIp(address))
        throw new SecurityException("Internal IP addresses not allowed");

    using var client = _httpClientFactory.CreateClient();
    return await client.GetStringAsync(uri);
}
```

---

## 2. Rate Limiting (Future Implementation)

```csharp
builder.Services.AddRateLimiter(options =>
{
    options.AddFixedWindowLimiter("fixed", options =>
    {
        options.PermitLimit = 100;
        options.Window = TimeSpan.FromMinutes(1);
        options.QueueProcessingOrder = QueueProcessingOrder.OldestFirst;
        options.QueueLimit = 10;
    });

    options.AddConcurrencyLimiter("concurrency", options =>
    {
        options.PermitLimit = 10;
        options.QueueProcessingOrder = QueueProcessingOrder.OldestFirst;
        options.QueueLimit = 5;
    });
});

app.UseRateLimiter();

app.MapPost("/api/blueprints", CreateBlueprint)
    .RequireRateLimiting("fixed");
```

---

## 3. Data Protection and Privacy

### Personal Data Handling

```csharp
public class Participant
{
    public string Id { get; init; } = string.Empty;

    [PersonalData]
    public required string Name { get; init; }

    [PersonalData]
    public string? Email { get; init; }
}

// GDPR: Data deletion
public async Task DeleteUserDataAsync(string userId)
{
    await _participantRepository.DeleteByUserIdAsync(userId);
    await _blueprintRepository.AnonymizeByUserIdAsync(userId);
    _auditLogger.LogDataDeletion(userId, "All personal data deleted");
}
```

### Data Minimization

```csharp
// CORRECT: Only collect necessary data
public record BlueprintRequest(
    string Title,
    string Description,
    List<string> ParticipantIds);

// WRONG: Collecting unnecessary data
public record BlueprintRequest(
    string Title,
    string UserSSN,      // VIOLATION - Not needed
    string UserBirthDate // VIOLATION - Not needed
);
```

---

## 4. Secure Communication

### TLS/SSL Requirements

```csharp
builder.WebHost.ConfigureKestrel(options =>
{
    options.ConfigureHttpsDefaults(httpsOptions =>
    {
        httpsOptions.SslProtocols = SslProtocols.Tls12 | SslProtocols.Tls13;
    });
});
```

### Certificate Validation

```csharp
// CORRECT: Validate certificates
var handler = new HttpClientHandler
{
    ServerCertificateCustomValidationCallback = (message, cert, chain, errors) =>
    {
        if (errors == SslPolicyErrors.None)
            return true;

        _logger.LogError("Certificate validation failed: {Errors}", errors);
        return false;
    }
};

// WRONG: Disabling certificate validation
ServerCertificateCustomValidationCallback = (_, _, _, _) => true; // VIOLATION
```

---

## 5. gRPC Security

```csharp
builder.WebHost.ConfigureKestrel(options =>
{
    options.ListenAnyIP(5051, listenOptions =>
    {
        listenOptions.Protocols = HttpProtocols.Http2;
        listenOptions.UseHttps();
    });
});

builder.Services.AddGrpc(options =>
{
    options.EnableDetailedErrors = false;
    options.MaxReceiveMessageSize = 4 * 1024 * 1024;
    options.MaxSendMessageSize = 4 * 1024 * 1024;
});

app.MapGrpcService<PeerService>()
    .RequireAuthorization();
```

---

## 6. Secrets Scanning

### Pre-commit Hooks

```bash
# .git/hooks/pre-commit
#!/bin/bash
detect-secrets scan --baseline .secrets.baseline

if [ $? -ne 0 ]; then
    echo "Secrets detected! Commit aborted."
    exit 1
fi
```

### NEVER Commit

- API keys
- Passwords
- Connection strings with credentials
- Private keys
- OAuth tokens
- Session secrets
- Encryption keys

---

## Security Checklist

Before committing code:

- [ ] All inputs validated
- [ ] SQL injection prevented (parameterized queries)
- [ ] XSS prevented (output encoding)
- [ ] CSRF tokens implemented
- [ ] Authentication required for protected endpoints
- [ ] Authorization checks on all resources
- [ ] Secrets stored securely (Key Vault/User Secrets)
- [ ] Security headers configured
- [ ] HTTPS enforced
- [ ] Rate limiting applied
- [ ] Audit logging implemented
- [ ] Error messages don't leak sensitive info
- [ ] Dependencies scanned for vulnerabilities
- [ ] Personal data handling compliant with GDPR

---

## References

- [OWASP Top 10](https://owasp.org/www-project-top-ten/)
- [OWASP Cheat Sheet Series](https://cheatsheetseries.owasp.org/)
- [ASP.NET Core Security](https://learn.microsoft.com/en-us/aspnet/core/security/)
- [Azure Security Best Practices](https://learn.microsoft.com/en-us/azure/security/fundamentals/best-practices-and-patterns)
