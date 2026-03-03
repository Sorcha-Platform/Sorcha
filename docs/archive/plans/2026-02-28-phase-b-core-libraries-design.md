# Phase B: Core Libraries — Design Document

> **Date:** 2026-02-28
> **Status:** Approved
> **Phase:** P0 — Production Packaging & Version Control
> **Scope:** 8 projects in `src/Core/`

---

## Overview

Apply the same production packaging, code quality, and CI/CD standards established in Phase A (Common libraries) to the Core libraries layer. Core libraries contain the business logic engines and domain-specific storage implementations that sit between Common (shared utilities) and Services (microservices).

## Scope

| Project | Purpose | Key Dependencies |
|---------|---------|-----------------|
| Sorcha.Blueprint.Engine | Blueprint execution (validate → calculate → route → disclose) | Blueprint.Models, Cryptography |
| Sorcha.Blueprint.Fluent | Fluent API for programmatic blueprint construction | Blueprint.Models |
| Sorcha.Blueprint.Schemas | Schema management with embedded JSON schemas | Blazored.LocalStorage |
| Sorcha.Register.Core | Ledger business logic (managers, governance) | Register.Models, ServiceClients |
| Sorcha.Register.Storage | Multi-tier storage abstractions with verified cache | Register.Core, Storage.Abstractions |
| Sorcha.Register.Storage.InMemory | In-memory register repository | Register.Core |
| Sorcha.Register.Storage.MongoDB | MongoDB register repository | Register.Core, MongoDB.Driver |
| Sorcha.Register.Storage.Redis | Redis Streams event publishing/subscribing | Register.Core, StackExchange.Redis |

## Current State

- All 8 projects already use CPM (no inline Version attributes)
- All target net10.0 with C# 13 (explicit or inherited)
- No `src/Core/Directory.Build.props` exists
- No NuGet PackageId/Version on any Core project
- 2 projects have `<Description>` tags; 6 do not
- 71 test files across 7 existing test projects
- Only `Register.Storage.InMemory` lacks a dedicated test project (covered by contract tests)

## Design

### B1: Directory.Build.props

Create `src/Core/Directory.Build.props` mirroring the Common pattern:

```xml
<Project>
  <PropertyGroup>
    <TargetFramework>net10.0</TargetFramework>
    <LangVersion>13</LangVersion>
    <Nullable>enable</Nullable>
    <ImplicitUsings>enable</ImplicitUsings>
    <GenerateDocumentationFile>true</GenerateDocumentationFile>
    <Authors>Sorcha Contributors</Authors>
    <Company>Sorcha Platform</Company>
    <Copyright>Copyright (c) 2026 Sorcha Contributors</Copyright>
    <PackageLicenseExpression>MIT</PackageLicenseExpression>
    <RepositoryUrl>https://github.com/sorcha-platform/sorcha</RepositoryUrl>
    <RepositoryType>git</RepositoryType>
  </PropertyGroup>
  <PropertyGroup Condition="'$(Configuration)' == 'Release'">
    <GeneratePackageOnBuild>false</GeneratePackageOnBuild>
    <IncludeSymbols>true</IncludeSymbols>
    <SymbolPackageFormat>snupkg</SymbolPackageFormat>
  </PropertyGroup>
</Project>
```

Individual csproj files can then remove redundant `TargetFramework`, `LangVersion`, `Nullable`, `ImplicitUsings` settings that are now inherited.

### B2: NuGet Metadata

Add to each csproj:

| Project | PackageId | Description |
|---------|-----------|-------------|
| Blueprint.Engine | Sorcha.Blueprint.Engine | Portable blueprint execution engine with 4-step pipeline |
| Blueprint.Fluent | Sorcha.Blueprint.Fluent | Fluent API for programmatic blueprint construction |
| Blueprint.Schemas | Sorcha.Blueprint.Schemas | Schema management with FHIR, ISO 20022, UBL, and W3C VC providers |
| Register.Core | Sorcha.Register.Core | Ledger business logic — register, transaction, and query management |
| Register.Storage | Sorcha.Register.Storage | Multi-tier register storage with verified cache abstraction |
| Register.Storage.InMemory | Sorcha.Register.Storage.InMemory | In-memory register repository for testing and development |
| Register.Storage.MongoDB | Sorcha.Register.Storage.MongoDB | MongoDB register repository implementation |
| Register.Storage.Redis | Sorcha.Register.Storage.Redis | Redis Streams event publishing and subscribing for registers |

All start at **Version 2.0.0** to match Common libraries.

### B3: Code Quality Review

Full 3-tier review (matching Phase A methodology):
- **Critical:** Bugs, security issues, data corruption risks
- **Important:** Performance issues, race conditions, missing validation, dead code
- **Minor:** Documentation gaps, naming inconsistencies, style issues

Review all 8 projects in parallel using sub-agents.

### B4: Test Gaps

Current test coverage is strong. Focus areas:
- Verify coverage of public API surface per project
- `Register.Storage.InMemory` has contract tests but no dedicated project — assess if standalone tests are needed
- Fix any build warnings in MongoDB/Redis test projects (nullable issues in Moq setup)

### B5: CI/CD Updates

1. **nuget-publish.yml**: Add `src/Core/**` to path triggers and 8 library names to detection list
2. **nuget-ci.yml**: Extend pack step to include `src/Core/` alongside `src/Common/`

### Publishing Order

Core libraries have inter-dependencies requiring ordered publishing:

```
Register.Core (no Core deps)
  ├── Register.Storage.InMemory
  ├── Register.Storage.MongoDB
  ├── Register.Storage.Redis
  └── Register.Storage
Blueprint.Schemas (no Core deps)
Blueprint.Engine (depends on Common libs only)
Blueprint.Fluent (depends on Common libs only)
```

The `max-parallel: 1` setting in nuget-publish.yml handles this — library list ordering matters.

## Out of Scope

- Major package upgrades (A17–A22) — separate effort
- Service-layer changes (Phase C)
- New feature development

## Success Criteria

- [ ] All 8 Core projects have consistent NuGet metadata
- [ ] `src/Core/Directory.Build.props` established
- [ ] Code quality review complete with all issues tracked and fixed
- [ ] Test coverage verified (no critical gaps)
- [ ] CI/CD triggers updated for `src/Core/**`
- [ ] `dotnet pack src/Core/` produces valid .nupkg files
- [ ] All tests pass: `dotnet test`
