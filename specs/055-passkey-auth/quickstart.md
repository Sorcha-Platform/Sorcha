# Quickstart: Passkey Authentication

**Feature**: 055-passkey-auth

## Prerequisites

- .NET 10 SDK
- Docker Desktop (for PostgreSQL, Redis, MongoDB)
- A WebAuthn-capable browser (Chrome, Edge, Firefox, Safari)

## Key Files

| Component | Path |
|-----------|------|
| PasskeyCredential model | `src/Services/Sorcha.Tenant.Service/Models/PasskeyCredential.cs` |
| SocialLoginLink model | `src/Services/Sorcha.Tenant.Service/Models/SocialLoginLink.cs` |
| PublicIdentity model | `src/Services/Sorcha.Tenant.Service/Models/PublicIdentity.cs` |
| Passkey service | `src/Services/Sorcha.Tenant.Service/Services/PasskeyService.cs` |
| Passkey endpoints | `src/Services/Sorcha.Tenant.Service/Endpoints/PasskeyEndpoints.cs` |
| Public auth endpoints | `src/Services/Sorcha.Tenant.Service/Endpoints/PublicAuthEndpoints.cs` |
| TenantDbContext | `src/Services/Sorcha.Tenant.Service/Data/TenantDbContext.cs` |
| WebAuthn JS interop | `src/Apps/Sorcha.UI/Sorcha.UI.Web.Client/wwwroot/js/webauthn.js` |
| Login page | `src/Apps/Sorcha.UI/Sorcha.UI.Web.Client/Pages/Login.razor` |
| API Gateway routes | `src/Services/Sorcha.ApiGateway/appsettings.json` |
| Tests | `tests/Sorcha.Tenant.Service.Tests/Services/PasskeyServiceTests.cs` |

## Testing Passkeys Locally

WebAuthn requires a secure context. For local development:
- `localhost` is treated as secure by browsers (no HTTPS needed)
- Set Relying Party ID to `localhost` in development config
- Use Chrome DevTools → WebAuthn tab to create virtual authenticators for testing

## Configuration

```json
{
  "Fido2": {
    "ServerDomain": "localhost",
    "ServerName": "Sorcha",
    "Origins": ["https://localhost:7082", "http://localhost:80"]
  }
}
```

## Build & Test

```bash
dotnet build src/Services/Sorcha.Tenant.Service
dotnet test tests/Sorcha.Tenant.Service.Tests --filter "FullyQualifiedName~Passkey"
```
