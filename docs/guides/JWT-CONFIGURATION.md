# JWT Configuration Guide

## Overview

Sorcha uses JSON Web Tokens (JWT) for authentication and authorization across all microservices. The JWT configuration ensures that tokens issued by the Tenant Service can be validated by all other services in the installation.

## Key Concepts

### Installation Name

The `InstallationName` is a unique identifier for your Sorcha deployment (e.g., `localhost`, `dev.sorcha.io`, `prod.sorcha.io`). It's used to automatically derive the JWT issuer and audience values.

### Issuer (iss claim)

The issuer identifies who created and signed the token. In Sorcha, the **Tenant Service is always the issuer**.

### Audience (aud claim)

The audience identifies who the token is intended for. All services in the same installation should accept tokens with the installation's audience.

## Default Configuration

### Docker Compose (Local Development)

The default docker-compose setup uses:

- **Installation Name:** `localhost`
- **Issuer:** `http://localhost` (derived from InstallationName)
- **Audience:** `http://localhost` (derived from InstallationName)
- **Signing Key:** Shared base64-encoded key (auto-generated or from environment)

### Configuration Hierarchy

JWT settings are resolved in this priority order:

1. **Explicit Configuration** - `JwtSettings:Issuer` and `JwtSettings:Audience` in appsettings or environment variables
2. **Derived from InstallationName** - If not explicitly set, uses `http://{InstallationName}`
3. **Fallback Defaults** - `https://tenant.sorcha.io` (issuer) and `https://api.sorcha.io` (audience)

## Environment Variables

### Required for Docker Compose

```yaml
environment:
  # Installation identifier (required for automatic issuer/audience derivation)
  JwtSettings__InstallationName: ${INSTALLATION_NAME:-localhost}

  # Shared signing key (required - must be same across all services)
  JwtSettings__SigningKey: ${JWT_SIGNING_KEY:-<base64-key>}
```

### Optional Overrides

```yaml
environment:
  # Explicit issuer (overrides InstallationName derivation)
  JwtSettings__Issuer: "https://auth.example.com"

  # Explicit audience (overrides InstallationName derivation)
  JwtSettings__Audience__0: "https://api.example.com"
  JwtSettings__Audience__1: "https://app.example.com"

  # Token lifetimes
  JwtSettings__AccessTokenLifetimeMinutes: 60
  JwtSettings__RefreshTokenLifetimeHours: 24
  JwtSettings__ServiceTokenLifetimeHours: 8

  # Validation settings
  JwtSettings__ValidateIssuer: true
  JwtSettings__ValidateAudience: true
  JwtSettings__ValidateIssuerSigningKey: true
  JwtSettings__ValidateLifetime: true
  JwtSettings__ClockSkewMinutes: 5
```

## Configuration by Environment

### Local Development (Docker Compose)

**File:** `docker-compose.yml`

```yaml
# Shared JWT configuration for all services
x-jwt-env: &jwt-env
  JwtSettings__InstallationName: ${INSTALLATION_NAME:-localhost}
  JwtSettings__SigningKey: ${JWT_SIGNING_KEY:-<default-dev-key>}
```

**Result:**
- Issuer: `http://localhost`
- Audience: `http://localhost`

### .NET Aspire (Local Development)

**File:** `src/Apps/Sorcha.AppHost/AppHost.cs`

```csharp
builder.AddProject<Projects.Sorcha_Tenant_Service>("tenant-service")
    .WithEnvironment("JwtSettings__InstallationName", "localhost");
```

**Result:**
- Issuer: `http://localhost`
- Audience: `http://localhost`

### Staging/Production

**Option 1: Use Installation Name**

```yaml
environment:
  JwtSettings__InstallationName: "staging.sorcha.io"
  JwtSettings__SigningKey: ${JWT_SIGNING_KEY}  # From Azure Key Vault
```

**Result:**
- Issuer: `http://staging.sorcha.io`
- Audience: `http://staging.sorcha.io`

**Option 2: Explicit Configuration**

```yaml
environment:
  JwtSettings__Issuer: "https://auth.sorcha.io"
  JwtSettings__Audience__0: "https://api.sorcha.io"
  JwtSettings__SigningKey: ${JWT_SIGNING_KEY}  # From Azure Key Vault
```

**Result:**
- Issuer: `https://auth.sorcha.io`
- Audience: `https://api.sorcha.io`

## Signing Key Management

### Development

**Auto-Generated Key:**
- Services auto-generate a shared development key stored in `%LOCALAPPDATA%\Sorcha\dev-jwt-signing-key.txt`
- Key is persisted across service restarts
- **DO NOT use in production**

**Docker Compose:**
- Uses a default base64-encoded key specified in `docker-compose.yml`
- Can be overridden via `.env` file:
  ```
  JWT_SIGNING_KEY=your-base64-key-here
  ```

### Production

**REQUIRED:** Provide signing key via:

1. **Environment Variable** (recommended):
   ```bash
   export JwtSettings__SigningKey="<base64-encoded-key>"
   ```

2. **Azure Key Vault** (best for production):
   ```yaml
   environment:
     JwtSettings__SigningKeySource: "AzureKeyVault"
     JwtSettings__KeyVaultUri: "https://your-vault.vault.azure.net/"
     JwtSettings__SigningKeyName: "sorcha-jwt-signing-key"
   ```

3. **AWS Secrets Manager**:
   ```yaml
   environment:
     JwtSettings__SigningKeySource: "AwsSecretsManager"
     JwtSettings__SecretId: "sorcha/jwt-signing-key"
   ```

## Token Structure

### User Token (Access Token)

```json
{
  "sub": "00000000-0000-0000-0001-000000000001",
  "email": "admin@sorcha.local",
  "jti": "unique-token-id",
  "name": "System Administrator",
  "org_id": "00000000-0000-0000-0000-000000000001",
  "org_name": "Sorcha Local",
  "token_type": "user",
  "role": ["Administrator", "SystemAdmin"],
  "iss": "http://localhost",
  "aud": "http://localhost",
  "exp": 1735858000,
  "iat": 1735854400
}
```

### Service Token

```json
{
  "sub": "00000000-0000-0000-0002-000000000001",
  "jti": "unique-token-id",
  "client_id": "service-blueprint",
  "service_name": "Blueprint Service",
  "token_type": "service",
  "scope": ["blueprints:read", "blueprints:write"],
  "iss": "http://localhost",
  "aud": "http://localhost",
  "exp": 1735883200,
  "iat": 1735854400
}
```

## Troubleshooting

### Authentication Fails: "Invalid audience"

**Cause:** Issuer or audience mismatch between token issuer (Tenant Service) and validator (other services).

**Solution:**

1. Check `InstallationName` is identical across all services:
   ```bash
   docker-compose config | grep InstallationName
   ```

2. Verify all services use the same configuration:
   ```bash
   docker logs sorcha-tenant-service 2>&1 | grep -i "jwt\|issuer"
   docker logs sorcha-wallet-service 2>&1 | grep -i "jwt\|issuer"
   ```

3. Restart all services after configuration change:
   ```bash
   docker-compose down
   docker-compose up -d
   ```

### Authentication Fails: "Invalid signature"

**Cause:** Services are using different signing keys.

**Solution:**

1. Ensure all services use the same `JwtSettings__SigningKey`:
   ```bash
   docker-compose config | grep SigningKey
   ```

2. Rebuild services to pick up new key:
   ```bash
   docker-compose build
   docker-compose up -d
   ```

### Tokens Expire Immediately

**Cause:** Clock skew between services or token lifetime too short.

**Solution:**

1. Increase clock skew tolerance:
   ```yaml
   JwtSettings__ClockSkewMinutes: 10
   ```

2. Adjust token lifetimes:
   ```yaml
   JwtSettings__AccessTokenLifetimeMinutes: 120
   JwtSettings__RefreshTokenLifetimeHours: 48
   ```

## Best Practices

### Local Development

✅ **DO:**
- Use `InstallationName` approach for simplicity
- Use the default development signing key
- Set `ValidateAudience: true` to catch configuration issues early

❌ **DON'T:**
- Disable token validation
- Use production keys in development

### Production

✅ **DO:**
- Store signing key in Azure Key Vault or AWS Secrets Manager
- Use HTTPS for issuer and audience URLs
- Enable all validation options
- Rotate signing keys periodically
- Use separate installations for staging/production

❌ **DON'T:**
- Use auto-generated development keys
- Commit signing keys to source control
- Disable signature validation
- Use HTTP in production

## Default Credentials

For local development, the Tenant Service creates a default admin user:

- **Email:** `admin@sorcha.local`
- **Password:** `Dev_Pass_2025!`
- **Organization:** `Sorcha Local`

⚠️ **Change these credentials immediately in production!**

## Testing JWT Configuration

### Using Sorcha CLI

```bash
# Configure CLI for local Docker
sorcha config list

# Login with default credentials
sorcha auth login
# Email: admin@sorcha.local
# Password: Dev_Pass_2025!

# Check token
sorcha auth status
```

### Manual Testing

```bash
# Get token
curl -X POST http://localhost/api/service-auth/token \
  -H "Content-Type: application/x-www-form-urlencoded" \
  -d "grant_type=password&username=admin@sorcha.local&password=Dev_Pass_2025!&client_id=sorcha-cli"

# Decode token (using jwt.io or jwt-cli)
echo "<access_token>" | jwt decode -

# Verify issuer and audience match your configuration
```

## References

- [JWT Standard (RFC 7519)](https://tools.ietf.org/html/rfc7519)
- [OAuth 2.0 (RFC 6749)](https://tools.ietf.org/html/rfc6749)
- [.NET JWT Bearer Authentication](https://learn.microsoft.com/aspnet/core/security/authentication/)
- [Sorcha Service Defaults](../src/Common/Sorcha.ServiceDefaults/JwtAuthenticationExtensions.cs)
- [Tenant Service Token Issuance](../src/Services/Sorcha.Tenant.Service/Services/TokenService.cs)
