# Quickstart: Mobile Package Infrastructure

**Feature**: 084-mobile-package-infra

## For SorchaMobile Developers

### Adding Sorcha Packages

```bash
# Core wallet types (entities, enums, derivation)
dotnet add package Sorcha.Wallet.Portable

# REST API clients + SignalR
dotnet add package Sorcha.ServiceClients.Http

# Cryptography (ED25519, P-256, RSA, encryption)
dotnet add package Sorcha.Cryptography

# Blueprint/register domain models (as needed)
dotnet add package Sorcha.Blueprint.Models
dotnet add package Sorcha.Register.Models
dotnet add package Sorcha.Tenant.Models
```

### Using the Wallet Package

```csharp
using Sorcha.Wallet.Core.Domain.Entities;
using Sorcha.Wallet.Core.Domain.Enums;
using Sorcha.Wallet.Service.Services.Implementation; // DerivationPathBuilder

// Build a derivation path
var path = DerivationPathBuilder.Build(
    orgId: Guid.Parse("org-guid"),
    departmentId: 0,
    userId: Guid.Parse("user-guid"),
    usage: KeyUsage.Identity,
    index: 0);
// Result: m/5459794'/orgHash'/0'/userHash'/0/0
```

### Using the HTTP Client Package

```csharp
using Sorcha.ServiceClients.Http.Extensions;

// Register all HTTP clients in DI
builder.Services.AddHttpServiceClients(builder.Configuration);

// Configuration (appsettings.json)
// "ServiceClients": {
//   "WalletService": { "Address": "https://gateway.sorcha.dev" },
//   "RegisterService": { "Address": "https://gateway.sorcha.dev" }
// }
```

### Using the SignalR Hub Helper

```csharp
using Sorcha.ServiceClients.Http.Hub;

// Build hub connection with JWT auth
var connection = SorchaHubConnectionBuilder.Build(
    hubUrl: "https://gateway.sorcha.dev/hubs/actions",
    tokenProvider: async () => await authService.GetTokenAsync());

await connection.StartAsync();

// Subscribe to action notifications
connection.On<ActionNotification>("ActionPending", notification =>
{
    // Handle pending action
});
```

## For Sorcha Server Developers

### No Changes Required

Existing server projects continue to reference `Sorcha.Wallet.Core` and `Sorcha.ServiceClients` as before. The new portable packages are included transitively:

```
Sorcha.Wallet.Core → Sorcha.Wallet.Portable (transitive)
Sorcha.ServiceClients → Sorcha.ServiceClients.Http (transitive)
```

All `using` statements, type references, and DI registrations continue to work unchanged.

### Publishing Packages

Packages are published automatically:
- **On merge to master**: Pre-release versions (e.g., `1.0.0-ci.42`)
- **On tag push**: Stable versions (e.g., `v1.0.0` → `1.0.0`)

```bash
# Create a release
git tag v1.0.0
git push origin v1.0.0
# Pipeline publishes all 9 packages to NuGet.org
```

## Testing

```bash
# Verify extraction didn't break anything
dotnet restore && dotnet build && dotnet test

# All 638+ tests should pass with zero modifications
```
