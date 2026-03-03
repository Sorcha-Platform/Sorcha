# ACT-001: Create Project Structure and Solution Integration

**Phase:** 1 - Foundation & Project Setup
**Estimated Effort:** 4 hours
**Priority:** Critical
**Dependencies:** None

## Objective

Create the Sorcha.Action.Service project with proper directory structure and integrate it into the Sorcha solution following architectural guidelines.

## Tasks

### 1. Create Service Project

```bash
# Navigate to Services directory
cd src/Services

# Create new ASP.NET Core Web API project
dotnet new webapi -n Sorcha.Action.Service -f net10.0 -controllers false

# Remove template files
cd Sorcha.Action.Service
rm WeatherForecast.cs
```

### 2. Create Directory Structure

```bash
mkdir -p Endpoints
mkdir -p Services
mkdir -p Models
mkdir -p Validators
mkdir -p Exceptions
mkdir -p Hubs
```

### 3. Update Project File

Edit `Sorcha.Action.Service.csproj`:

```xml
<Project Sdk="Microsoft.NET.Sdk.Web">

  <PropertyGroup>
    <TargetFramework>net10.0</TargetFramework>
    <Nullable>enable</Nullable>
    <ImplicitUsings>enable</ImplicitUsings>
    <GenerateDocumentationFile>true</GenerateDocumentationFile>
    <DocumentationFile>Sorcha.Action.Service.xml</DocumentationFile>
    <NoWarn>$(NoWarn);1591</NoWarn>
  </PropertyGroup>

  <ItemGroup>
    <!-- Project References -->
    <ProjectReference Include="..\..\Common\Sorcha.Blueprint.Models\Sorcha.Blueprint.Models.csproj" />
    <ProjectReference Include="..\..\Common\Sorcha.Cryptography\Sorcha.Cryptography.csproj" />
    <ProjectReference Include="..\..\Common\Sorcha.TransactionHandler\Sorcha.TransactionHandler.csproj" />
    <ProjectReference Include="..\..\Common\Sorcha.ServiceDefaults\Sorcha.ServiceDefaults.csproj" />
  </ItemGroup>

</Project>
```

### 4. Create Test Projects

```bash
# Navigate to tests directory
cd ../../../tests

# Create unit test project
dotnet new xunit -n Sorcha.Action.Service.Tests -f net10.0

# Create integration test project
dotnet new xunit -n Sorcha.Action.Service.Integration.Tests -f net10.0
```

### 5. Add to Solution

```bash
# Navigate to solution root
cd ..

# Add service project
dotnet sln Sorcha.sln add src/Services/Sorcha.Action.Service/Sorcha.Action.Service.csproj

# Add test projects
dotnet sln Sorcha.sln add tests/Sorcha.Action.Service.Tests/Sorcha.Action.Service.Tests.csproj
dotnet sln Sorcha.sln add tests/Sorcha.Action.Service.Integration.Tests/Sorcha.Action.Service.Integration.Tests.csproj
```

### 6. Add Copyright Headers

Add to all .cs files:

```csharp
// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Sorcha Contributors
```

### 7. Create README.md

Create `src/Services/Sorcha.Action.Service/README.md`:

```markdown
# Sorcha Action Service

Manages participant interactions within Blueprint-controlled workflows.

## Features

- Action retrieval for participants
- Action submission with schema validation
- Payload encryption for selective disclosure
- Transaction construction and submission
- File attachment support
- Real-time notifications via SignalR

## API Documentation

- OpenAPI Spec: `/openapi/v1.json`
- Interactive Docs: `/scalar/v1`

## Health Checks

- Health: `/health`
- Liveness: `/alive`

## Configuration

See `appsettings.json` for configuration options.

## Dependencies

- Sorcha.WalletService - Wallet operations and encryption
- Sorcha.RegisterService - Transaction storage
- Sorcha.Blueprint.Service - Blueprint definitions
- Redis - Caching and SignalR backplane

## Development

```bash
# Run service
dotnet run

# Run tests
dotnet test

# Build Docker image
docker build -t sorcha/action-service:latest .
```
```

## Acceptance Criteria

- [ ] Project created in `src/Services/Sorcha.Action.Service/`
- [ ] Directory structure matches specification
- [ ] Project added to `Sorcha.sln`
- [ ] Test projects created and added to solution
- [ ] Project references configured correctly
- [ ] Copyright headers added to all files
- [ ] README.md created
- [ ] Build succeeds: `dotnet build`
- [ ] Solution builds successfully

## Verification

```bash
# Verify project structure
ls -la src/Services/Sorcha.Action.Service/

# Should show:
# - Endpoints/
# - Services/
# - Models/
# - Validators/
# - Exceptions/
# - Hubs/
# - Program.cs
# - appsettings.json
# - Sorcha.Action.Service.csproj
# - README.md

# Verify solution includes projects
dotnet sln list | grep Action.Service

# Should show:
# - src/Services/Sorcha.Action.Service/Sorcha.Action.Service.csproj
# - tests/Sorcha.Action.Service.Tests/Sorcha.Action.Service.Tests.csproj
# - tests/Sorcha.Action.Service.Integration.Tests/Sorcha.Action.Service.Integration.Tests.csproj

# Build solution
dotnet build
# Should succeed
```

## References

- [Project Structure Documentation](../../docs/reference/project-structure.md)
- [Architecture Documentation](../../docs/reference/architecture.md)
- [Action Service Specification](../specs/sorcha-action-service.md)
