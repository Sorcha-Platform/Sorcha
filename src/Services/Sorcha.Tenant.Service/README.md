# Sorcha Tenant Service

**Version**: 1.0.0
**Status**: Complete (100% MVD)
**Framework**: .NET 10.0
**Architecture**: Microservice

---

## Overview

The **Sorcha Tenant Service** is a multi-tenant authentication and authorization service that acts as a Secure Token Service (STS) for the Sorcha platform. It enables organizations to bring their own identity providers (Azure Entra ID, AWS Cognito, etc.) and provides passwordless authentication via FIDO2/WebAuthn PassKeys.

### Key Features

- **Multi-Organization Support**: Each organization has its own identity provider configuration
- **External Identity Federation**: Integrate with Azure Entra ID, AWS Cognito, Google Workspace, or any OIDC-compliant provider
- **PassKey Authentication**: FIDO2/WebAuthn passwordless authentication for enhanced security
- **Service-to-Service Authentication**: OAuth2 client credentials flow for microservice communication
- **JWT Token Issuance**: RS256-signed tokens with configurable lifetimes
- **Token Revocation**: Redis-backed token blacklist with automatic TTL cleanup
- **Multi-Tenant Data Isolation**: PostgreSQL schema-based tenant isolation
- **Audit Logging**: Comprehensive audit trail of all authentication events
- **Rate Limiting**: Protect against brute force attacks

---

## Architecture

```
┌─────────────────────────────────────────────────────────────┐
│                    Sorcha Tenant Service                    │
├─────────────────────────────────────────────────────────────┤
│  ┌──────────────┐  ┌───────────────┐  ┌────────────────┐  │
│  │   Auth API   │  │   Admin API   │  │   Audit API    │  │
│  │              │  │               │  │                │  │
│  │ • OAuth2     │  │ • Org Mgmt    │  │ • Log Query    │  │
│  │ • PassKey    │  │ • IDP Config  │  │ • Analytics    │  │
│  │ • Token Mgmt │  │ • User Mgmt   │  │                │  │
│  └──────────────┘  └───────────────┘  └────────────────┘  │
│                                                             │
│  ┌──────────────────────────────────────────────────────┐  │
│  │             Service Layer                            │  │
│  │  • OrganizationService  • TokenService               │  │
│  │  • AuthenticationService • PassKeyService            │  │
│  └──────────────────────────────────────────────────────┘  │
│                                                             │
│  ┌──────────────────────────────────────────────────────┐  │
│  │             Data Layer                               │  │
│  │  • EF Core (PostgreSQL)  • Redis Cache               │  │
│  └──────────────────────────────────────────────────────┘  │
└─────────────────────────────────────────────────────────────┘
           │                    │                  │
           ▼                    ▼                  ▼
    ┌──────────────┐    ┌─────────────┐   ┌──────────────┐
    │  PostgreSQL  │    │    Redis    │   │ External IDP │
    │  (Multi-     │    │  (Revoke    │   │ (Azure/AWS/  │
    │   tenant)    │    │   List)     │   │  Google)     │
    └──────────────┘    └─────────────┘   └──────────────┘
```

---

## Quick Start

### Prerequisites

- **.NET 10 SDK** - [Download](https://dotnet.microsoft.com/download/dotnet/10.0)
- **Docker Desktop** - For PostgreSQL and Redis
- **Git** - Version control

### 1. Clone and Navigate

```bash
cd C:\Projects\Sorcha
```

### 2. Set Up Local Secrets

**Option A: Automated Setup (Recommended)**

```bash
# Windows (PowerShell)
.\specs\001-tenant-auth\setup-local-secrets.ps1

# macOS/Linux (Bash)
chmod +x ./specs/001-tenant-auth/setup-local-secrets.sh
./specs/001-tenant-auth/setup-local-secrets.sh
```

**Option B: Manual Setup**

```bash
# Initialize User Secrets
dotnet user-secrets init --project src/Services/Sorcha.Tenant.Service

# Generate and set JWT signing key (see secrets-setup.md)
openssl genrsa -out jwt_private.pem 4096
dotnet user-secrets set "JwtSettings:SigningKey" "$(cat jwt_private.pem)" --project src/Services/Sorcha.Tenant.Service

# Set database password
dotnet user-secrets set "ConnectionStrings:Password" "dev_password123" --project src/Services/Sorcha.Tenant.Service
```

For detailed secrets management guide, see [specs/001-tenant-auth/secrets-setup.md](../../../specs/001-tenant-auth/secrets-setup.md).

### 3. Start Dependencies

```bash
# Start PostgreSQL and Redis
docker-compose up -d postgres redis
```

### 4. Run Database Migrations

```bash
cd src/Services/Sorcha.Tenant.Service
dotnet ef database update
```

### 5. Run the Service

```bash
dotnet run
```

Service will start at:
- **HTTPS**: https://localhost:7080
- **HTTP**: http://localhost:7081
- **Scalar API Docs**: https://localhost:7080/scalar

---

## Configuration

### appsettings.json Structure

```json
{
  "ConnectionStrings": {
    "TenantDatabase": "Host=localhost;Port=5432;Database=sorcha_tenant;Username=sorcha_user;Password=placeholder"
  },
  "Redis": {
    "ConnectionString": "localhost:6379",
    "InstanceName": "SorchaTenant:"
  },
  "JwtSettings": {
    "Issuer": "https://localhost:7080",
    "Audience": ["https://localhost:7081"],
    "AccessTokenLifetimeMinutes": 60,
    "RefreshTokenLifetimeMinutes": 1440
  },
  "Fido2": {
    "ServerDomain": "localhost",
    "ServerName": "Sorcha Tenant Service"
  }
}
```

### Environment Variables

For production deployment, use environment variables:

```bash
ConnectionStrings__TenantDatabase="Host=prod-db;Port=5432;..."
Redis__ConnectionString="prod-redis:6379"
JwtSettings__Issuer="https://api.sorcha.example.com"
AzureKeyVault__Enabled="true"
AzureKeyVault__VaultUri="https://sorcha-kv.vault.azure.net/"
```

---

## API Endpoints

### Authentication API (`/api/auth`)

| Endpoint | Method | Description |
|----------|--------|-------------|
| `/api/auth/login` | GET | Initiate OAuth2 login flow |
| `/api/auth/callback` | GET | OAuth2 callback handler |
| `/api/auth/logout` | POST | End user session |
| `/api/auth/passkey/register-options` | POST | Get PassKey registration options |
| `/api/auth/passkey/register` | POST | Complete PassKey registration |
| `/api/auth/passkey/login-options` | POST | Get PassKey login options |
| `/api/auth/passkey/login` | POST | Complete PassKey login |
| `/api/auth/token/refresh` | POST | Refresh access token |
| `/api/auth/token/revoke` | POST | Revoke token |
| `/api/auth/token/validate` | POST | Validate token |
| `/api/auth/token/service` | POST | Service-to-service token |

### Admin API (`/api/admin`)

| Endpoint | Method | Description |
|----------|--------|-------------|
| `/api/admin/organizations` | GET | List organizations |
| `/api/admin/organizations` | POST | Create organization |
| `/api/admin/organizations/{id}` | GET | Get organization details |
| `/api/admin/organizations/{id}` | PUT | Update organization |
| `/api/admin/organizations/{id}/idp` | PUT | Configure identity provider |
| `/api/admin/organizations/{id}/users` | GET | List organization users |
| `/api/admin/organizations/{id}/permissions` | PUT | Configure permissions |

### Audit API (`/api/audit`)

| Endpoint | Method | Description |
|----------|--------|-------------|
| `/api/audit/logs` | GET | Query audit logs |
| `/api/audit/logs/{orgId}` | GET | Organization-specific logs |

For full API documentation, open **Scalar UI** at `https://localhost:7080/scalar`.

---

## Development

### Project Structure

```
src/Services/Sorcha.Tenant.Service/
├── Endpoints/              # Minimal API endpoint groups
│   ├── AuthEndpoints.cs
│   ├── AdminEndpoints.cs
│   └── AuditEndpoints.cs
├── Services/               # Business logic services
│   ├── OrganizationService.cs
│   ├── AuthenticationService.cs
│   ├── TokenService.cs
│   └── PassKeyService.cs
├── Data/                   # Data access layer
│   ├── TenantDbContext.cs
│   ├── Repositories/
│   │   ├── IOrganizationRepository.cs
│   │   ├── OrganizationRepository.cs
│   │   └── ...
│   └── Migrations/
├── Models/                 # Request/response DTOs
│   ├── Requests/
│   ├── Responses/
│   └── Configuration/
├── Extensions/             # Service extensions
│   ├── ServiceCollectionExtensions.cs
│   └── ApplicationBuilderExtensions.cs
├── appsettings.json
├── appsettings.Development.json
└── Program.cs
```

### Running Tests

```bash
# Unit tests
dotnet test tests/Sorcha.Tenant.Service.Tests

# Integration tests (uses Testcontainers)
dotnet test tests/Sorcha.Tenant.Service.IntegrationTests

# Performance tests
dotnet run --project tests/Sorcha.Tenant.Service.PerformanceTests
```

### Code Coverage

```bash
dotnet test --collect:"XPlat Code Coverage"
dotnet tool install -g dotnet-reportgenerator-globaltool
reportgenerator -reports:"**/coverage.cobertura.xml" -targetdir:"coverage" -reporttypes:Html
```

### Database Migrations

```bash
# Create new migration
dotnet ef migrations add MigrationName --context TenantDbContext

# Apply migrations
dotnet ef database update

# Revert migration
dotnet ef database update PreviousMigrationName

# Generate SQL script
dotnet ef migrations script --output migrations.sql
```

---

## Security Considerations

### Secrets Management

- **Local Development**: Use .NET User Secrets (stored outside project directory)
- **Production**: Use Azure Key Vault, AWS Secrets Manager, or HashiCorp Vault
- **NEVER commit secrets** to source control

### JWT Signing Keys

- **Algorithm**: RS256 (RSA-SHA256) with 4096-bit keys
- **Rotation**: Rotate keys every 90 days
- **Storage**: Private key in Key Vault, public key in JWKS endpoint

### Multi-Tenancy

- **Data Isolation**: PostgreSQL schemas per organization (`org_{id}`)
- **Row-Level Security**: EF Core query filters prevent cross-tenant data access
- **Audit Logging**: All operations logged with organization context

### Rate Limiting

- **Login Attempts**: 5 attempts per 5 minutes per IP
- **Token Requests**: 100 requests per minute per client
- **Admin Operations**: 20 requests per minute per user

---

## Deployment

### .NET Aspire (Development)

```bash
# Run via Aspire orchestration
dotnet run --project src/Apps/Sorcha.AppHost

# Aspire Dashboard: http://localhost:15888
```

### Docker

```bash
# Build image
docker build -t sorcha-tenant-service -f src/Services/Sorcha.Tenant.Service/Dockerfile .

# Run container
docker run -p 7080:8080 \
  -e ConnectionStrings__TenantDatabase="Host=db;..." \
  -e Redis__ConnectionString="redis:6379" \
  sorcha-tenant-service
```

### Azure App Service

```bash
# Deploy via Azure CLI
az webapp create --name sorcha-tenant-service --resource-group sorcha-rg --plan sorcha-plan
az webapp deployment source config-zip --name sorcha-tenant-service --resource-group sorcha-rg --src publish.zip
```

---

## Observability

### Logging (Serilog + OTLP)

- **Structured Logging**: Serilog with machine name, thread ID, application enrichment
- **Correlation IDs**: Track requests across services
- **Aspire Dashboard**: Centralized log viewer via OTLP (http://localhost:18888)

```csharp
// Example log entry
Log.Information("User {UserId} authenticated for organization {OrgId}", userId, orgId);
```

### Tracing (OpenTelemetry + Zipkin)

- **Distributed Tracing**: End-to-end request tracing
- **Zipkin Dashboard**: http://localhost:9411

### Metrics (Prometheus)

- **Metrics Endpoint**: `/metrics`
- **Custom Metrics**: Login success/failure rates, token issuance latency

---

## Troubleshooting

### Database Connection Issues

**Error**: "Connection refused" or "password authentication failed"

**Solution**:
```bash
# Check PostgreSQL is running
docker ps | grep postgres

# Verify User Secrets
dotnet user-secrets list --project src/Services/Sorcha.Tenant.Service

# Test connection
psql -h localhost -U sorcha_user -d sorcha_tenant_dev
```

### Redis Connection Issues

**Error**: "It was not possible to connect to the redis server(s)"

**Solution**:
```bash
# Check Redis is running
docker ps | grep redis

# Test connection
redis-cli ping  # Should return: PONG
```

### Token Validation Failures

**Error**: "Invalid signature" or "Token has expired"

**Solution**:
- Ensure JWT signing key is configured in User Secrets
- Check system clock synchronization (token validation uses timestamps)
- Verify JWKS endpoint is accessible: `https://localhost:7080/.well-known/jwks.json`

---

## Contributing

### Development Workflow

1. **Create Feature Branch**: `git checkout -b feature/your-feature`
2. **Write Tests First**: Follow TDD (Test-Driven Development)
3. **Implement Feature**: Follow existing code patterns
4. **Run Tests**: Ensure all tests pass
5. **Update Documentation**: Update README, API docs, specs
6. **Submit PR**: Reference task ID in commit message

### Code Standards

- **C# Conventions**: Follow Microsoft C# coding conventions
- **Async/Await**: Use async for all I/O operations
- **Dependency Injection**: Use constructor injection
- **OpenAPI Documentation**: All endpoints must have XML documentation

---

## Resources

- **Specification**: [specs/001-tenant-auth/spec.md](../../../specs/001-tenant-auth/spec.md)
- **Implementation Plan**: [specs/001-tenant-auth/plan.md](../../../specs/001-tenant-auth/plan.md)
- **Secrets Setup**: [specs/001-tenant-auth/secrets-setup.md](../../../specs/001-tenant-auth/secrets-setup.md)
- **Quickstart Guide**: [specs/001-tenant-auth/quickstart.md](../../../specs/001-tenant-auth/quickstart.md)
- **API Contracts**: [specs/001-tenant-auth/contracts/](../../../specs/001-tenant-auth/contracts/)

---

## License

This project is licensed under the Apache License 2.0. See [LICENSE](../../../LICENSE) for details.

---

## Support

For issues, questions, or contributions:
- **GitHub Issues**: [Sorcha Issues](https://github.com/your-org/sorcha/issues)
- **Documentation**: [Sorcha Docs](../../../docs/)
- **CLAUDE.md**: [AI Assistant Guide](../../../CLAUDE.md)

---

**Last Updated**: 2026-03-01
**Maintained By**: Sorcha Contributors
**Deferred (Post-MVD)**: Azure AD B2C integration
