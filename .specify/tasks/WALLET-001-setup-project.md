# WALLET-001: Setup Sorcha.WalletService Project

**Status:** Not Started
**Priority:** Critical
**Estimated Hours:** 6
**Dependencies:** None
**Related Spec:** [sorcha-wallet-service.md](../specs/sorcha-wallet-service.md)

## Objective

Create the foundational project structure for Sorcha.WalletService library, including the main library project, API project, test projects, and necessary configuration files integrated with .NET Aspire.

## Requirements

### Project Structure

Create the following project structure:

```
src/
├── Common/
│   └── Sorcha.WalletService/              # Main library
│       ├── Domain/
│       │   ├── Entities/
│       │   ├── ValueObjects/
│       │   └── Events/
│       ├── Services/
│       │   ├── Interfaces/
│       │   └── Implementation/
│       ├── Repositories/
│       │   ├── Interfaces/
│       │   └── Implementation/
│       ├── Encryption/
│       │   ├── Interfaces/
│       │   └── Providers/
│       ├── Events/
│       │   ├── Interfaces/
│       │   └── Publishers/
│       └── Sorcha.WalletService.csproj
│
└── Services/
    └── Sorcha.WalletService.Api/         # Minimal API service
        ├── Endpoints/
        ├── Program.cs
        └── Sorcha.WalletService.Api.csproj

tests/
├── Sorcha.WalletService.Tests/           # Unit tests
│   ├── Services/
│   ├── Repositories/
│   ├── Encryption/
│   ├── Events/
│   ├── Fixtures/
│   └── Sorcha.WalletService.Tests.csproj
│
└── Sorcha.WalletService.IntegrationTests/  # Integration tests
    ├── Database/
    ├── Encryption/
    ├── Events/
    ├── Fixtures/
    └── Sorcha.WalletService.IntegrationTests.csproj
```

### Main Library Project (Sorcha.WalletService.csproj)

```xml
<Project Sdk="Microsoft.NET.Sdk">

  <PropertyGroup>
    <TargetFramework>net10.0</TargetFramework>
    <LangVersion>latest</LangVersion>
    <Nullable>enable</Nullable>
    <GenerateDocumentationFile>true</GenerateDocumentationFile>
    <PackageId>Sorcha.WalletService</PackageId>
    <Version>1.0.0</Version>
    <Authors>Sorcha Contributors</Authors>
    <Company>Sorcha</Company>
    <Description>Wallet management library for Sorcha distributed ledger platform</Description>
    <PackageLicenseExpression>MIT</PackageLicenseExpression>
    <RepositoryUrl>https://github.com/sorcha-platform/sorcha</RepositoryUrl>
  </PropertyGroup>

  <ItemGroup>
    <!-- Internal Dependencies -->
    <ProjectReference Include="..\Sorcha.Cryptography\Sorcha.Cryptography.csproj" />
    <ProjectReference Include="..\Sorcha.TransactionHandler\Sorcha.TransactionHandler.csproj" />

    <!-- Database -->
    <PackageReference Include="Microsoft.EntityFrameworkCore" Version="10.0.0" />
    <PackageReference Include="Microsoft.EntityFrameworkCore.Relational" Version="10.0.0" />

    <!-- Encryption -->
    <PackageReference Include="Azure.Security.KeyVault.Keys" Version="4.7.0" />
    <PackageReference Include="Azure.Identity" Version="1.13.1" />
    <PackageReference Include="Microsoft.AspNetCore.DataProtection" Version="10.0.0" />

    <!-- HD Wallet Support -->
    <PackageReference Include="NBitcoin" Version="7.0.42" />

    <!-- Logging -->
    <PackageReference Include="Microsoft.Extensions.Logging.Abstractions" Version="10.0.0" />

    <!-- Dependency Injection -->
    <PackageReference Include="Microsoft.Extensions.DependencyInjection.Abstractions" Version="10.0.0" />
    <PackageReference Include="Microsoft.Extensions.Options" Version="10.0.0" />
  </ItemGroup>

</Project>
```

### API Project (Sorcha.WalletService.Api.csproj)

```xml
<Project Sdk="Microsoft.NET.Sdk.Web">

  <PropertyGroup>
    <TargetFramework>net10.0</TargetFramework>
    <Nullable>enable</Nullable>
  </PropertyGroup>

  <ItemGroup>
    <ProjectReference Include="..\..\Common\Sorcha.WalletService\Sorcha.WalletService.csproj" />
    <ProjectReference Include="..\..\Common\Sorcha.ServiceDefaults\Sorcha.ServiceDefaults.csproj" />
  </ItemGroup>

  <ItemGroup>
    <PackageReference Include="Aspire.Npgsql.EntityFrameworkCore.PostgreSQL" Version="13.0.0" />
    <PackageReference Include="Aspire.StackExchange.Redis" Version="13.0.0" />
    <PackageReference Include="Microsoft.AspNetCore.OpenApi" Version="10.0.0" />
  </ItemGroup>

</Project>
```

### API Program.cs

```csharp
using Sorcha.WalletService;
using Sorcha.WalletService.Api.Endpoints;

var builder = WebApplication.CreateBuilder(args);

// Add service defaults & Aspire client integrations.
builder.AddServiceDefaults();

// Add wallet service
builder.Services.AddWalletService(builder.Configuration);

// Add database
builder.AddNpgsqlDbContext<WalletDbContext>("walletdb");

// Add Redis cache
builder.AddRedisClient("cache");

// Add OpenAPI
builder.Services.AddOpenApi();

var app = builder.Build();

// Map service defaults & health checks
app.MapDefaultEndpoints();

// Map wallet endpoints
app.MapWalletEndpoints();

// Configure OpenAPI
if (app.Environment.IsDevelopment())
{
    app.MapOpenApi();
}

app.Run();
```

### Test Project (Sorcha.WalletService.Tests.csproj)

```xml
<Project Sdk="Microsoft.NET.Sdk">

  <PropertyGroup>
    <TargetFramework>net10.0</TargetFramework>
    <LangVersion>latest</LangVersion>
    <Nullable>enable</Nullable>
    <IsPackable>false</IsPackable>
    <IsTestProject>true</IsTestProject>
  </PropertyGroup>

  <ItemGroup>
    <ProjectReference Include="..\..\src\Common\Sorcha.WalletService\Sorcha.WalletService.csproj" />
  </ItemGroup>

  <ItemGroup>
    <PackageReference Include="Microsoft.NET.Test.Sdk" Version="17.12.0" />
    <PackageReference Include="xunit" Version="2.9.3" />
    <PackageReference Include="xunit.runner.visualstudio" Version="2.8.2" />
    <PackageReference Include="coverlet.collector" Version="6.0.2" />
    <PackageReference Include="Moq" Version="4.20.72" />
    <PackageReference Include="FluentAssertions" Version="6.12.2" />
    <PackageReference Include="AutoFixture" Version="4.18.1" />
    <PackageReference Include="AutoFixture.Xunit2" Version="4.18.1" />
  </ItemGroup>

</Project>
```

### Integration Test Project (Sorcha.WalletService.IntegrationTests.csproj)

```xml
<Project Sdk="Microsoft.NET.Sdk">

  <PropertyGroup>
    <TargetFramework>net10.0</TargetFramework>
    <LangVersion>latest</LangVersion>
    <Nullable>enable</Nullable>
    <IsPackable>false</IsPackable>
    <IsTestProject>true</IsTestProject>
  </PropertyGroup>

  <ItemGroup>
    <ProjectReference Include="..\..\src\Common\Sorcha.WalletService\Sorcha.WalletService.csproj" />
    <ProjectReference Include="..\..\src\Services\Sorcha.WalletService.Api\Sorcha.WalletService.Api.csproj" />
  </ItemGroup>

  <ItemGroup>
    <PackageReference Include="Microsoft.NET.Test.Sdk" Version="17.12.0" />
    <PackageReference Include="xunit" Version="2.9.3" />
    <PackageReference Include="xunit.runner.visualstudio" Version="2.8.2" />
    <PackageReference Include="FluentAssertions" Version="6.12.2" />
    <PackageReference Include="Testcontainers" Version="3.10.0" />
    <PackageReference Include="Testcontainers.PostgreSql" Version="3.10.0" />
    <PackageReference Include="Microsoft.EntityFrameworkCore.InMemory" Version="10.0.0" />
    <PackageReference Include="Npgsql.EntityFrameworkCore.PostgreSQL" Version="10.0.0" />
    <PackageReference Include="Aspire.Hosting.Testing" Version="13.0.0" />
  </ItemGroup>

</Project>
```

### Configuration Files

**README.md** (root of Sorcha.WalletService/)
```markdown
# Sorcha.WalletService

Wallet management library for Sorcha distributed ledger platform.

## Features

- HD wallet creation and recovery (BIP32/BIP39/BIP44)
- Multi-algorithm support (ED25519, SECP256K1, RSA)
- Transaction signing and verification
- Encrypted key storage with multiple providers
- Access control and delegation
- Event-driven architecture with .NET Aspire
- Cloud-native design

## Getting Started

See [Integration Guide](../../.specify/tasks/WALLET-031-integration-guide.md) for details.

## Documentation

- [Specification](../../.specify/specs/sorcha-wallet-service.md)
- [Task Overview](../../.specify/tasks/WALLET-OVERVIEW.md)

## License

MIT
```

### Aspire Integration

**Update Sorcha.AppHost/Program.cs:**

```csharp
// Add PostgreSQL for wallet storage
var postgres = builder.AddPostgres("postgres")
    .WithPgAdmin()
    .AddDatabase("walletdb");

// Add wallet service
var walletService = builder.AddProject<Projects.Sorcha_WalletService_Api>("wallet-service")
    .WithReference(postgres)
    .WithReference(cache);

// Update API Gateway to include wallet service
builder.AddProject<Projects.Sorcha_ApiGateway>("api-gateway")
    .WithReference(blueprintService)
    .WithReference(peerService)
    .WithReference(walletService)
    .WithReference(cache);
```

## Acceptance Criteria

- [ ] All project files created with correct structure
- [ ] Main library project compiles successfully
- [ ] API project compiles successfully
- [ ] Test projects compile successfully
- [ ] All NuGet dependencies resolve correctly
- [ ] Projects added to Sorcha.sln
- [ ] README.md with basic documentation
- [ ] Integrated with Sorcha.AppHost
- [ ] Initial commit to repository
- [ ] CI/CD pipeline updated to include new projects

## Testing

1. Build solution: `dotnet build`
2. Verify all projects compile without warnings
3. Run empty test suite: `dotnet test`
4. Verify project references work correctly
5. Run Aspire: `dotnet run --project src/Apps/Sorcha.AppHost`
6. Verify wallet service appears in Aspire dashboard

## Notes

- Use .NET 10 as the target framework
- Enable nullable reference types across all projects
- Configure XML documentation generation for the main library
- Follow Sorcha architectural patterns and conventions
- Integrate with existing Sorcha.Cryptography and Sorcha.TransactionHandler

## Next Steps

After completing this task:
1. Proceed to WALLET-002 (Domain Models & Enums)
2. Update CI/CD pipeline for automated builds and tests
3. Configure code coverage reporting
