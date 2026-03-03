# Sorcha Project Structure

## Overview

Sorcha follows a clean, modular architecture with clear separation of concerns. This document defines the official directory structure and placement rules for all projects.

**Last Updated:** 2025-01-12
**Version:** 2.0.0

## Directory Structure

```
Sorcha/
├── src/
│   ├── Apps/                          # Application layer
│   │   ├── Sorcha.AppHost             # .NET Aspire orchestration host
│   │   └── UI/
│   │       └── Sorcha.Blueprint.Designer.Client  # Blazor WASM UI
│   ├── Common/                        # Cross-cutting concerns
│   │   ├── Sorcha.Blueprint.Models    # Domain models and DTOs
│   │   ├── Sorcha.Cryptography        # Cryptography library
│   │   └── Sorcha.ServiceDefaults     # Service configuration utilities
│   ├── Core/                          # Business logic layer
│   │   ├── Sorcha.Blueprint.Engine    # Blueprint execution engine
│   │   ├── Sorcha.Blueprint.Fluent    # Fluent API builders
│   │   └── Sorcha.Blueprint.Schemas   # Schema management
│   └── Services/                      # Service layer
│       ├── Sorcha.ApiGateway          # YARP-based API Gateway
│       ├── Sorcha.Blueprint.Service   # Blueprint REST API
│       └── Sorcha.Peer.Service        # P2P networking service
├── tests/                             # All test projects
│   ├── Sorcha.Blueprint.Engine.Tests
│   ├── Sorcha.Blueprint.Fluent.Tests
│   ├── Sorcha.Blueprint.Models.Tests
│   ├── Sorcha.Blueprint.Schemas.Tests
│   ├── Sorcha.Cryptography.Tests
│   ├── Sorcha.Gateway.Integration.Tests
│   ├── Sorcha.Integration.Tests
│   ├── Sorcha.Peer.Service.Tests
│   ├── Sorcha.Performance.Tests
│   └── Sorcha.UI.E2E.Tests
├── docs/                              # Documentation
│   ├── architecture.md
│   ├── testing.md
│   ├── project-structure.md           # This file
│   └── ...
├── scripts/                           # Build and deployment scripts
├── infra/                             # Infrastructure as Code
├── samples/                           # Sample blueprints and examples
├── .github/                           # GitHub workflows
└── Sorcha.sln                         # Solution file

```

## Directory Purposes

### src/Apps/

Application layer - orchestration and UI applications.

- **Sorcha.AppHost**: .NET Aspire orchestration host that coordinates all services
- **UI/**: User interface applications (Blazor WASM, Blazor Server)

**Placement Rule**:
- Orchestration and hosting projects (AppHost)
- UI applications that run in the browser or as desktop apps
- Projects that coordinate multiple services but don't contain business logic

### src/Common/

Shared libraries used across multiple projects (cross-cutting concerns).

- **Models**: Domain models, DTOs, value objects
- **Cryptography**: Reusable cryptography library (can be published as NuGet package)
- **ServiceDefaults**: Shared configuration utilities and extensions

**Placement Rule**: Libraries that:
- Are used by multiple projects across different layers
- Contain no business logic (models, utilities, helpers)
- Could potentially be extracted as standalone NuGet packages
- Represent cross-cutting concerns

**Examples**:
- ✅ Domain models (Blueprint, Action, Participant)
- ✅ Cryptography utilities
- ✅ Common DTOs and interfaces
- ✅ Service configuration extensions
- ❌ Business logic (belongs in Core/)
- ❌ API endpoints (belongs in Apps/)

### src/Core/

Core business logic and domain services.

- **Engine**: Blueprint execution engine
- **Fluent**: Fluent API builders for creating blueprints
- **Schemas**: Schema management and validation

**Placement Rule**: Libraries that:
- Contain core business logic
- Implement domain services
- Are used by Apps/ layer
- Should not depend on Apps/ layer (dependency inversion)

**Examples**:
- ✅ Blueprint execution logic
- ✅ Fluent builders
- ✅ Schema validation
- ✅ Business rules and workflows
- ❌ Database access (if we add it, goes in Infrastructure/)
- ❌ External API clients (if we add them, goes in Infrastructure/)

### src/Services/

Service layer - REST APIs, gRPC services, and background services.

- **Sorcha.ApiGateway**: YARP-based API Gateway for routing and aggregation
- **Sorcha.Blueprint.Service**: Blueprint REST API service
- **Sorcha.Peer.Service**: P2P networking service for decentralized communication

**Placement Rule**: Projects that:
- Provide HTTP/REST APIs or gRPC services
- Run as independent services orchestrated by AppHost
- Could be deployed separately from the main application
- Implement service interfaces and API endpoints

**Examples**:
- ✅ REST API services
- ✅ gRPC services
- ✅ API Gateways
- ✅ P2P networking services
- ✅ Background workers and job processors
- ❌ Core business logic (belongs in Core/)
- ❌ UI applications (belongs in Apps/UI/)

### tests/

All test projects following naming convention `{ProjectName}.Tests`.

**Placement Rule**:
- Unit tests: Test a single project in isolation
- Integration tests: Test multiple projects together (suffix with `.Integration.Tests`)
- E2E tests: Test the entire system (suffix with `.E2E.Tests`)
- Performance tests: Load and performance testing (suffix with `.Performance.Tests`)

## Dependency Rules

### Allowed Dependencies

```
Apps/
  ↓ (can depend on)
Core/ + Common/ + Services/

Core/
  ↓ (can depend on)
Common/

Services/
  ↓ (can depend on)
Core/ + Common/

Common/
  ↓ (no dependencies on other src/ projects)
```

### Forbidden Dependencies

- ❌ Common/ → Core/
- ❌ Common/ → Apps/
- ❌ Common/ → Services/
- ❌ Core/ → Apps/
- ❌ Core/ → Services/

## Target Frameworks

### Standard Targets

| Project Type | Target Framework(s) | Reason |
|--------------|-------------------|---------|
| Apps/ | `net10.0` | Latest .NET for full framework features |
| Common/ libraries | `net10.0` | Latest .NET for all libraries |
| Core/ libraries | `net10.0` | Latest .NET for all libraries |
| Services/ | `net10.0` | Backend services use latest .NET |
| Tests | `net10.0` | Match project under test |

### Target Framework Standard

All projects in the Sorcha solution target `net10.0`:
- Sorcha.Blueprint.Models
- Sorcha.Blueprint.Fluent
- Sorcha.Blueprint.Schemas
- Sorcha.Cryptography
- All other projects

## Naming Conventions

### Project Names

- **Apps**: `Sorcha.{Feature}`
  - Examples: `Sorcha.AppHost`, `Sorcha.Blueprint.Designer.Client`
- **Services**: `Sorcha.{Feature}.Service` or `Sorcha.{Feature}Gateway`
  - Examples: `Sorcha.ApiGateway`, `Sorcha.Blueprint.Service`, `Sorcha.Peer.Service`
- **Common**: `Sorcha.{Feature}`
  - Examples: `Sorcha.Cryptography`, `Sorcha.ServiceDefaults`, `Sorcha.Blueprint.Models`
- **Core**: `Sorcha.Blueprint.{Feature}`
  - Examples: `Sorcha.Blueprint.Engine`, `Sorcha.Blueprint.Fluent`, `Sorcha.Blueprint.Schemas`

### Test Projects

- **Unit**: `{ProjectName}.Tests`
  - Example: `Sorcha.Blueprint.Engine.Tests`
- **Integration**: `Sorcha.{Feature}.Integration.Tests`
  - Example: `Sorcha.Gateway.Integration.Tests`
- **E2E**: `Sorcha.{Feature}.E2E.Tests`
  - Example: `Sorcha.UI.E2E.Tests`
- **Performance**: `Sorcha.Performance.Tests`

## Adding New Projects

### Checklist

1. ✅ Choose the correct directory based on purpose:
   - Entry point/service → `src/Apps/`
   - Shared library → `src/Common/`
   - Business logic → `src/Core/`
   - Background service → `src/Services/`

2. ✅ Follow naming conventions

3. ✅ Set correct target framework:
   - All projects → `net10.0`

4. ✅ Add to solution file:
   ```bash
   dotnet sln add src/{Category}/{ProjectName}/{ProjectName}.csproj
   ```

5. ✅ Create corresponding test project

6. ✅ Verify dependency rules (no circular dependencies)

7. ✅ Update architecture documentation if needed

## Common Mistakes to Avoid

### ❌ Wrong: Duplicate Projects

```
src/Services/Sorcha.Peer.Service/
src/Apps/Services/Sorcha.Peer.Service/   # DUPLICATE - Don't do this!
```

**Solution**: Keep one in the correct location based on its purpose.

### ❌ Wrong: Mixing Concerns

```
src/Common/Sorcha.Blueprint.Engine/   # Business logic in Common
```

**Solution**: Business logic belongs in `src/Core/`.

### ❌ Wrong: Circular Dependencies

```
Common/ → Core/ → Common/   # Circular!
```

**Solution**: Common should never depend on Core or Apps.

### ❌ Wrong: Incorrect Target Framework

```xml
<TargetFramework>net8.0</TargetFramework>   # Outdated!
```

**Solution**: Use `<TargetFramework>net10.0</TargetFramework>` for all projects.

## Migration Guide

If you need to move a project:

1. **Update project file path** in solution:
   ```bash
   dotnet sln remove old/path/Project.csproj
   dotnet sln add new/path/Project.csproj
   ```

2. **Move directory**:
   ```bash
   git mv old/path/Project new/path/Project
   ```

3. **Update all project references** that point to the moved project

4. **Update documentation**

5. **Test build**:
   ```bash
   dotnet build
   ```

## Related Documentation

- [Architecture Overview](architecture.md)
- [Testing Guide](../guides/testing/testing.md)
- [Getting Started](../getting-started/getting-started.md)
- [Contributing Guidelines](../CONTRIBUTING.md)

---

**Questions or Issues?**
- Review this document
- Check [architecture.md](architecture.md)
- Ask in GitHub Discussions
