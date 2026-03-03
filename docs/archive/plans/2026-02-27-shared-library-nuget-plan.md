# Shared Library NuGet Packaging & Quality Review — Implementation Plan

> **For Claude:** REQUIRED SUB-SKILL: Use superpowers:executing-plans to implement this plan task-by-task.

**Goal:** Package all 15 Common libraries as independently versioned NuGet packages with centralized version management, new GitHub Actions for automated publishing, and code quality review across all shared libraries.

**Architecture:** Central Package Management (Directory.Packages.props) for version pinning across 69+ projects. Directory.Build.props for shared NuGet metadata. Three new GitHub Actions workflows replace five old ones. Parallel code quality review across all 15 libraries.

**Tech Stack:** .NET 10, NuGet Central Package Management, GitHub Actions, SourceLink

---

### Task 1: Create Directory.Build.props for Common libraries

**Files:**
- Create: `src/Common/Directory.Build.props`

**Step 1: Create shared build properties**

```xml
<Project>
  <PropertyGroup>
    <!-- Package Metadata -->
    <Authors>Sorcha Contributors</Authors>
    <Company>Sorcha Contributors</Company>
    <Copyright>Copyright (c) 2026 Sorcha Contributors</Copyright>
    <PackageLicenseExpression>MIT</PackageLicenseExpression>
    <PackageProjectUrl>https://github.com/sorcha-platform/sorcha</PackageProjectUrl>
    <RepositoryUrl>https://github.com/sorcha-platform/sorcha</RepositoryUrl>
    <RepositoryType>git</RepositoryType>

    <!-- Build Settings -->
    <TargetFramework>net10.0</TargetFramework>
    <LangVersion>13</LangVersion>
    <Nullable>enable</Nullable>
    <ImplicitUsings>enable</ImplicitUsings>
    <GenerateDocumentationFile>true</GenerateDocumentationFile>

    <!-- NuGet Packaging -->
    <GeneratePackageOnBuild>false</GeneratePackageOnBuild>
    <IncludeSymbols>true</IncludeSymbols>
    <SymbolPackageFormat>snupkg</SymbolPackageFormat>
    <Deterministic>true</Deterministic>
    <EmbedUntrackedSources>true</EmbedUntrackedSources>
  </PropertyGroup>

  <ItemGroup>
    <PackageReference Include="Microsoft.SourceLink.GitHub" PrivateAssets="All" />
  </ItemGroup>
</Project>
```

**Step 2: Verify it doesn't conflict with existing tests/Directory.Build.props**

The tests/ one only sets `<NoWarn>$(NoWarn);xUnit1051</NoWarn>` — no conflict. MSBuild walks up to find Directory.Build.props; tests/ projects find theirs first, Common/ projects find the new one.

---

### Task 2: Create Directory.Packages.props (Central Package Management)

**Files:**
- Create: `Directory.Packages.props` (solution root)

**Step 1: Create centralized version file**

Consolidate ALL PackageReference versions from every .csproj in the solution. Resolve conflicts to highest stable version. Every package referenced anywhere in the solution must appear here.

Key version resolutions:
- FluentAssertions: 7.0.0 / 7.0.1 / 8.8.0 → `8.8.0`
- Grpc.Net.Client: 2.71.0 / 2.76.0 → `2.76.0`
- Grpc.Tools: 2.71.0 / 2.76.0 → `2.76.0`
- Microsoft.NET.Test.Sdk: 17.12.0 / 17.14.1 / 18.0.1 → `18.0.1`
- xunit.runner.visualstudio: 3.0.0 / 3.1.4 / 3.1.5 → `3.1.5`
- coverlet.collector: 6.0.2 / 6.0.4 → `6.0.4`
- Microsoft.AspNetCore.Mvc.Testing: 10.0.0 / 10.0.0-rc.2 / 10.0.2 → `10.0.2`
- Microsoft.AspNetCore.SignalR.Client: 10.0.0 / 10.0.2 → `10.0.2`
- System.IdentityModel.Tokens.Jwt: 8.7.0 / 8.15.0 → `8.15.0`
- xunit 2.9.3 vs xunit.v3 3.2.2 → keep both (different packages, projects choose one)

**Step 2: Update every .csproj in the solution**

Remove `Version="..."` from all `<PackageReference>` elements. For PrivateAssets/IncludeAssets on packages like Grpc.Tools, keep those attributes but move version to central file.

**Step 3: Enable CPM**

The `<ManagePackageVersionsCentrally>true</ManagePackageVersionsCentrally>` property in Directory.Packages.props enables it.

**Step 4: Build to verify**

Run: `dotnet build` — entire solution must compile.

---

### Task 3: Add NuGet PackageId to all 15 Common libraries

**Files:**
- Modify: All 15 `src/Common/*/_.csproj` files

**Step 1: For each library, add package-specific properties**

Each .csproj gets only what's unique to it (shared metadata comes from Directory.Build.props):

```xml
<PropertyGroup>
  <PackageId>Sorcha.X</PackageId>
  <Version>2.0.0</Version>
  <Description>...</Description>
  <PackageTags>sorcha;relevant;tags</PackageTags>
</PropertyGroup>
```

Remove from individual .csproj files:
- Authors, Company, Copyright (now in Directory.Build.props)
- RepositoryUrl, PackageProjectUrl (now in Directory.Build.props)
- SourceLink PackageReference (now in Directory.Build.props)
- LanguageVersion (now in Directory.Build.props)
- TargetFramework (now in Directory.Build.props)
- IncludeSymbols, SymbolPackageFormat, Deterministic (now in Directory.Build.props)

**Descriptions per library:**

| PackageId | Description | Tags |
|-----------|-------------|------|
| Sorcha.Blueprint.Models | Domain models with JSON-LD for blueprint workflow definitions | sorcha;blueprint;models;json-ld;workflow |
| Sorcha.Blueprint.Schemas | Schema management with validation, caching, and storage integration | sorcha;blueprint;schema;validation;jsonschema |
| Sorcha.Cryptography | Multi-algorithm cryptography: ED25519, P-256, RSA-4096, BLS threshold signatures, post-quantum (ML-KEM/ML-DSA), zero-knowledge proofs, SD-JWT, HD wallet support (BIP32/39/44), Merkle trees, and encoding utilities | sorcha;cryptography;ed25519;ecdsa;rsa;bls;post-quantum;zero-knowledge;sd-jwt;wallet |
| Sorcha.Register.Models | Domain models for the distributed ledger register | sorcha;register;models;ledger |
| Sorcha.ServiceClients | Consolidated HTTP and gRPC service clients with auth and resilience | sorcha;service-clients;grpc;http;resilience |
| Sorcha.ServiceDefaults | Shared Aspire configuration, telemetry, JWT auth, and OpenAPI | sorcha;aspire;telemetry;openapi;service-defaults |
| Sorcha.Storage.Abstractions | Core storage abstraction interfaces for multi-tier storage | sorcha;storage;abstractions;repository |
| Sorcha.Storage.EFCore | Entity Framework Core PostgreSQL storage implementation | sorcha;storage;efcore;postgresql |
| Sorcha.Storage.InMemory | In-memory storage for development and testing | sorcha;storage;inmemory;testing |
| Sorcha.Storage.MongoDB | MongoDB document storage implementation | sorcha;storage;mongodb |
| Sorcha.Storage.Redis | Redis hot-tier caching and storage implementation | sorcha;storage;redis;caching |
| Sorcha.Tenant.Models | Multi-tenant identity and organization domain models | sorcha;tenant;models;identity |
| Sorcha.TransactionHandler | Transaction building, signing, and serialization | sorcha;transaction;signing;serialization;distributed-ledger |
| Sorcha.Validator.Core | Enclave-safe validation and consensus logic | sorcha;validator;consensus;validation |
| Sorcha.Wallet.Core | Wallet management with HD derivation and key operations | sorcha;wallet;hd-wallet;bip32;key-management |

**Step 2: Verify packaging**

Run: `dotnet pack src/Common/ --no-build` — all 15 produce .nupkg files.

---

### Task 4: Purge all legacy branding -- COMPLETE

All references to legacy branding (old project names, old GitHub URLs, old company names) have been replaced with Sorcha-branded equivalents across all source code, documentation, specification, and configuration files.

**Verification:** grep for legacy branding terms returns zero matches in active source, test, doc, and spec files.

---

### Task 5: Remove old GitHub Actions workflows

**Files:**
- Delete: `.github/workflows/main-ci-cd.yml`
- Delete: `.github/workflows/pr-validation.yml`
- Delete: `.github/workflows/release.yml`
- Delete: `.github/workflows/docker-build-push.yml`
- Delete: `.github/workflows/api-gateway-AutoDeployTrigger-b1cdb051-e5be-43b2-8082-6a2cdc90b7e9.yml`
- Delete: `.github/workflows/claude.yml`

**Step 1: Delete the 6 workflow files**

Keep: `claude-code-review.yml`, `codeql.yml`, `playwright.yml`

---

### Task 6: Create nuget-ci.yml (PR build & test)

**Files:**
- Create: `.github/workflows/nuget-ci.yml`

**Step 1: Write PR validation workflow**

Triggers on PRs to master. Builds full solution, runs tests, packs NuGet (no publish). Detects which Common libraries changed and runs targeted tests.

---

### Task 7: Create nuget-publish.yml (auto version bump & publish)

**Files:**
- Create: `.github/workflows/nuget-publish.yml`

**Step 1: Write publish workflow**

Triggers on push to master touching `src/Common/**`. For each changed library:
1. Reads current Version from .csproj
2. Bumps minor (2.0.0 → 2.1.0)
3. Commits version bump
4. Packs and publishes to nuget.org

Dependency ordering for publishing:
1. Sorcha.Register.Models (no deps)
2. Sorcha.Tenant.Models (no deps)
3. Sorcha.Blueprint.Models (no deps)
4. Sorcha.Storage.Abstractions (no deps)
5. Sorcha.Cryptography (no deps)
6. Sorcha.Blueprint.Schemas (no deps)
7. Sorcha.Storage.InMemory (→ Abstractions)
8. Sorcha.Storage.EFCore (→ Abstractions)
9. Sorcha.Storage.MongoDB (→ Abstractions)
10. Sorcha.Storage.Redis (→ Abstractions)
11. Sorcha.TransactionHandler (→ Cryptography, Blueprint.Models)
12. Sorcha.Validator.Core (→ Blueprint.Models, Cryptography, Register.Models)
13. Sorcha.Wallet.Core (→ Cryptography, TransactionHandler, Storage.Abstractions)
14. Sorcha.ServiceClients (→ Register.Models)
15. Sorcha.ServiceDefaults (→ ServiceClients)

Uses `secrets.NUGET_API_KEY` with `--skip-duplicate`.

---

### Task 8: Create claude-review-issues.yml (review feedback loop)

**Files:**
- Create: `.github/workflows/claude-review-issues.yml`

**Step 1: Write feedback workflow**

Triggers via `workflow_run` after `claude-code-review.yml` completes. Reads PR comments via GitHub API, identifies Claude review comments, creates labeled GitHub issues for actionable items. Deduplicates against existing open issues.

---

### Task 9: Update codeql.yml branch references

**Files:**
- Modify: `.github/workflows/codeql.yml`

**Step 1: Update branch triggers**

Change `branches: [ main, develop ]` to `branches: [ master ]` and PR target to `branches: [ master ]` to match actual branch naming.

---

### Task 10: Parallel code quality review

**Files:**
- All `.cs` files under `src/Common/`

**Step 1: Dispatch parallel review agents across all 15 libraries**

Review criteria:
- License headers (SPDX-License-Identifier: MIT / Copyright (c) 2026 Sorcha Contributors)
- Import ordering (System → Microsoft → Third-party → Sorcha)
- Naming conventions
- XML documentation on public APIs
- Duplicate models/DTOs across libraries
- Dead code, stale comments, TODOs referencing completed work
- Unused interfaces/classes

**Step 2: Run `dotnet list package --outdated`**

Update any outdated packages in Directory.Packages.props.

**Step 3: Apply fixes from review findings**

---

### Task 11: Fill test gaps

**Files:**
- Create: `tests/Sorcha.Storage.EFCore.Tests/` project and tests
- Create: `tests/Sorcha.Storage.MongoDB.Tests/` project and tests
- Create: `tests/Sorcha.Tenant.Models.Tests/` project and tests
- Create: `tests/Sorcha.Wallet.Core.Tests/` project and tests

**Step 1: Create test projects for the 4 libraries missing coverage**

Each test project follows the standard pattern: xUnit, FluentAssertions, Moq. Test naming: `MethodName_Scenario_ExpectedBehavior`.

---

### Task 12: Update git remote

**Step 1: Update origin remote**

```bash
git remote set-url origin https://github.com/sorcha-platform/sorcha.git
```

---

### Task 13: Validation

**Step 1: Full build**
Run: `dotnet build` — entire solution compiles with zero errors.

**Step 2: Full test**
Run: `dotnet test` — all existing tests pass (pre-existing failures documented in MEMORY.md are acceptable).

**Step 3: Pack all libraries**
Run: `dotnet pack src/Common/ -o ./nupkg` — all 15 produce valid .nupkg files.

**Step 4: Verify no legacy branding references in source**
Run: grep for legacy branding terms in src/ and tests/ -- zero matches.

**Step 5: Validate GitHub Actions YAML**
Verify all .yml files are syntactically valid.

---

### Task 14: Squash commit

**Step 1: Stage all changes and create single clean commit**

```
feat: shared library NuGet packaging, CPM, branding, and CI overhaul

- Package all 15 Common libraries as NuGet packages (v2.0.0)
- Add Central Package Management (Directory.Packages.props)
- Add shared build metadata (Directory.Build.props)
- Replace legacy branding with Sorcha Contributors
- New GitHub Actions: nuget-ci, nuget-publish, claude-review-issues
- Remove obsolete workflows (main-ci-cd, pr-validation, release, docker-build-push, azure-deploy)
- Code quality fixes across all shared libraries
- Fill test gaps: Storage.EFCore, Storage.MongoDB, Tenant.Models, Wallet.Core

Co-Authored-By: Claude Opus 4.6 <noreply@anthropic.com>
```
