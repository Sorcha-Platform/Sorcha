# Phase B: Core Libraries Implementation Plan

> **For Claude:** REQUIRED SUB-SKILL: Use superpowers:executing-plans to implement this plan task-by-task.

**Goal:** Apply production packaging, code quality, and CI/CD to the 8 Core libraries in `src/Core/`.

**Architecture:** Follow the Phase A pattern — create `Directory.Build.props`, add NuGet metadata to each csproj, run code quality review, verify test coverage, and update CI/CD workflows. Core libraries sit between Common (utilities) and Services (microservices).

**Tech Stack:** .NET 10, C# 13, Central Package Management, GitHub Actions, NuGet, xUnit

---

## Task 1: Create Directory.Build.props for Core libraries

**Files:**
- Create: `src/Core/Directory.Build.props`

**Step 1: Create the file**

Create `src/Core/Directory.Build.props` with this exact content:

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

**Step 2: Verify build still works**

Run: `dotnet build src/Core/ --configuration Release`
Expected: All 8 projects build successfully.

**Step 3: Commit**

```bash
git add src/Core/Directory.Build.props
git commit -m "feat: [B1] add Directory.Build.props for Core libraries"
```

---

## Task 2: Clean up redundant properties from Core csproj files

Now that `Directory.Build.props` provides defaults, remove redundant properties from each csproj. Keep only project-specific settings (PackageId, Version, Description, project-specific NoWarn, InternalsVisibleTo, EmbeddedResource, etc.).

**Files:**
- Modify: `src/Core/Sorcha.Blueprint.Engine/Sorcha.Blueprint.Engine.csproj`
- Modify: `src/Core/Sorcha.Blueprint.Fluent/Sorcha.Blueprint.Fluent.csproj`
- Modify: `src/Core/Sorcha.Blueprint.Schemas/Sorcha.Blueprint.Schemas.csproj`
- Modify: `src/Core/Sorcha.Register.Core/Sorcha.Register.Core.csproj`
- Modify: `src/Core/Sorcha.Register.Storage/Sorcha.Register.Storage.csproj`
- Modify: `src/Core/Sorcha.Register.Storage.InMemory/Sorcha.Register.Storage.InMemory.csproj`
- Modify: `src/Core/Sorcha.Register.Storage.MongoDB/Sorcha.Register.Storage.MongoDB.csproj`
- Modify: `src/Core/Sorcha.Register.Storage.Redis/Sorcha.Register.Storage.Redis.csproj`

**Step 1: Edit each csproj**

Remove these properties that are now inherited from Directory.Build.props:
- `<TargetFramework>net10.0</TargetFramework>`
- `<Nullable>enable</Nullable>`
- `<ImplicitUsings>enable</ImplicitUsings>`
- `<LangVersion>13</LangVersion>`
- `<GenerateDocumentationFile>true</GenerateDocumentationFile>`

Keep project-specific properties:
- `RootNamespace` (Blueprint.Engine, Blueprint.Fluent)
- `DocumentationFile` (Blueprint.Engine)
- `NoWarn` (Blueprint.Engine, Register.Storage)
- `TreatWarningsAsErrors` (Register.Storage)
- `InternalsVisibleTo` (Blueprint.Fluent)
- `EmbeddedResource` (Blueprint.Schemas)
- `Description` (Register.Storage, Register.Storage.Redis — will be overwritten in Task 3)

**Target csproj content after cleanup:**

**Blueprint.Engine:**
```xml
<!-- SPDX-License-Identifier: MIT -->
<!-- Copyright (c) 2026 Sorcha Contributors -->
<Project Sdk="Microsoft.NET.Sdk">

  <PropertyGroup>
    <RootNamespace>Sorcha.Blueprint.Engine</RootNamespace>
    <DocumentationFile>Sorcha.Blueprint.Engine.xml</DocumentationFile>
    <NoWarn>$(NoWarn);1591</NoWarn>
  </PropertyGroup>

  <ItemGroup>
    <ProjectReference Include="..\..\Common\Sorcha.Blueprint.Models\Sorcha.Blueprint.Models.csproj" />
    <ProjectReference Include="..\..\Common\Sorcha.Cryptography\Sorcha.Cryptography.csproj" />
  </ItemGroup>

  <ItemGroup>
    <PackageReference Include="JsonSchema.Net" />
    <PackageReference Include="JsonLogic" />
    <PackageReference Include="JsonPath.Net" />
    <PackageReference Include="JsonE.Net" />
    <PackageReference Include="Microsoft.Extensions.Caching.Memory" />
  </ItemGroup>

</Project>
```

**Blueprint.Fluent:**
```xml
<Project Sdk="Microsoft.NET.Sdk">

  <PropertyGroup>
    <RootNamespace>Sorcha.Blueprint.Fluent</RootNamespace>
  </PropertyGroup>

  <ItemGroup>
    <InternalsVisibleTo Include="Sorcha.Blueprint.Fluent.Tests" />
  </ItemGroup>

  <ItemGroup>
    <ProjectReference Include="..\..\Common\Sorcha.Blueprint.Models\Sorcha.Blueprint.Models.csproj" />
  </ItemGroup>

  <ItemGroup>
    <PackageReference Include="JsonLogic" />
  </ItemGroup>

</Project>
```

**Blueprint.Schemas:**
```xml
<Project Sdk="Microsoft.NET.Sdk">

  <ItemGroup>
    <PackageReference Include="Blazored.LocalStorage" />
  </ItemGroup>

  <ItemGroup>
    <EmbeddedResource Include="BuiltInSchemas\*.json" />
  </ItemGroup>

</Project>
```

**Register.Core:**
```xml
<Project Sdk="Microsoft.NET.Sdk">

  <ItemGroup>
    <PackageReference Include="Microsoft.Extensions.Logging.Abstractions" />
  </ItemGroup>

  <ItemGroup>
    <ProjectReference Include="..\..\Common\Sorcha.Register.Models\Sorcha.Register.Models.csproj" />
    <ProjectReference Include="..\..\Common\Sorcha.ServiceClients\Sorcha.ServiceClients.csproj" />
  </ItemGroup>

</Project>
```

**Register.Storage:**
```xml
<Project Sdk="Microsoft.NET.Sdk">

  <PropertyGroup>
    <TreatWarningsAsErrors>false</TreatWarningsAsErrors>
    <NoWarn>$(NoWarn);CS1591</NoWarn>
  </PropertyGroup>

  <ItemGroup>
    <ProjectReference Include="..\..\Common\Sorcha.Register.Models\Sorcha.Register.Models.csproj" />
    <ProjectReference Include="..\Sorcha.Register.Core\Sorcha.Register.Core.csproj" />
    <ProjectReference Include="..\Sorcha.Register.Storage.InMemory\Sorcha.Register.Storage.InMemory.csproj" />
    <ProjectReference Include="..\..\Common\Sorcha.Storage.Abstractions\Sorcha.Storage.Abstractions.csproj" />
    <ProjectReference Include="..\..\Common\Sorcha.Storage.InMemory\Sorcha.Storage.InMemory.csproj" />
  </ItemGroup>

  <ItemGroup>
    <PackageReference Include="Microsoft.Extensions.DependencyInjection.Abstractions" />
    <PackageReference Include="Microsoft.Extensions.Logging.Abstractions" />
    <PackageReference Include="Microsoft.Extensions.Options" />
  </ItemGroup>

</Project>
```

**Register.Storage.InMemory:**
```xml
<Project Sdk="Microsoft.NET.Sdk">

  <ItemGroup>
    <ProjectReference Include="..\Sorcha.Register.Core\Sorcha.Register.Core.csproj" />
  </ItemGroup>

</Project>
```

**Register.Storage.MongoDB:**
```xml
<Project Sdk="Microsoft.NET.Sdk">

  <ItemGroup>
    <ProjectReference Include="..\Sorcha.Register.Core\Sorcha.Register.Core.csproj" />
    <ProjectReference Include="..\..\Common\Sorcha.Register.Models\Sorcha.Register.Models.csproj" />
  </ItemGroup>

  <ItemGroup>
    <PackageReference Include="MongoDB.Driver" />
    <PackageReference Include="Microsoft.Extensions.Configuration.Abstractions" />
    <PackageReference Include="Microsoft.Extensions.Options.ConfigurationExtensions" />
    <PackageReference Include="Microsoft.Extensions.DependencyInjection.Abstractions" />
    <PackageReference Include="Microsoft.Extensions.Logging.Abstractions" />
  </ItemGroup>

</Project>
```

**Register.Storage.Redis:**
```xml
<Project Sdk="Microsoft.NET.Sdk">

  <ItemGroup>
    <ProjectReference Include="..\Sorcha.Register.Core\Sorcha.Register.Core.csproj" />
  </ItemGroup>

  <ItemGroup>
    <PackageReference Include="StackExchange.Redis" />
    <PackageReference Include="Microsoft.Extensions.Hosting.Abstractions" />
    <PackageReference Include="Microsoft.Extensions.DependencyInjection.Abstractions" />
    <PackageReference Include="Microsoft.Extensions.Options" />
    <PackageReference Include="Microsoft.Extensions.Logging.Abstractions" />
    <PackageReference Include="Polly" />
  </ItemGroup>

</Project>
```

**Step 2: Verify build**

Run: `dotnet build src/Core/ --configuration Release`
Expected: All 8 projects build. No new warnings introduced.

**Step 3: Commit**

```bash
git add src/Core/
git commit -m "refactor: [B1] remove redundant properties from Core csproj files (inherited from Directory.Build.props)"
```

---

## Task 3: Add NuGet PackageId/Version/Description to all Core libraries

**Files:**
- Modify: all 8 csproj files in `src/Core/`

**Step 1: Add NuGet metadata to each csproj**

Add a `<PropertyGroup>` block (or extend existing one) with PackageId, Version, and Description:

| Project | PackageId | Version | Description |
|---------|-----------|---------|-------------|
| Blueprint.Engine | Sorcha.Blueprint.Engine | 2.0.0 | Portable blueprint execution engine with validate, calculate, route, and disclose pipeline |
| Blueprint.Fluent | Sorcha.Blueprint.Fluent | 2.0.0 | Fluent API for programmatic blueprint construction and JSON-LD generation |
| Blueprint.Schemas | Sorcha.Blueprint.Schemas | 2.0.0 | Schema management with FHIR, ISO 20022, UBL, W3C VC, and Schema.org providers |
| Register.Core | Sorcha.Register.Core | 2.0.0 | Ledger business logic for register, transaction, docket, and governance management |
| Register.Storage | Sorcha.Register.Storage | 2.0.0 | Multi-tier register storage abstraction with verified cache |
| Register.Storage.InMemory | Sorcha.Register.Storage.InMemory | 2.0.0 | In-memory register repository for testing and development |
| Register.Storage.MongoDB | Sorcha.Register.Storage.MongoDB | 2.0.0 | MongoDB register repository implementation |
| Register.Storage.Redis | Sorcha.Register.Storage.Redis | 2.0.0 | Redis Streams event publishing and subscribing for registers |

For each csproj, add as the first `<PropertyGroup>` child:

```xml
<PackageId>Sorcha.Blueprint.Engine</PackageId>
<Version>2.0.0</Version>
<Description>Portable blueprint execution engine with validate, calculate, route, and disclose pipeline</Description>
```

If a `<Description>` already exists (Register.Storage, Register.Storage.Redis), replace it with the new description.

**Step 2: Verify pack works**

Run: `dotnet pack src/Core/ --configuration Release --no-build -o ./nupkg-test`
Expected: 8 `.nupkg` files produced. Verify with `ls ./nupkg-test/`.

Run: `rm -rf ./nupkg-test` to clean up.

**Step 3: Commit**

```bash
git add src/Core/
git commit -m "feat: [B2] add NuGet PackageId/Version/Description to all 8 Core libraries (v2.0.0)"
```

---

## Task 4: Code quality review across all 8 Core libraries

**Files:**
- Read: all `.cs` files in `src/Core/` (8 projects)

**Step 1: Run parallel code quality review**

Dispatch sub-agents to review each Core project in parallel. Use the same 3-tier methodology as Phase A12:

- **Critical:** Bugs, security issues, data corruption, deadlocks
- **Important:** Performance issues, race conditions, missing validation, dead code, API design
- **Minor:** Documentation gaps, naming inconsistencies, unused imports, style

Review projects:
1. `src/Core/Sorcha.Blueprint.Engine/` — largest project, JSON processing, credential handling
2. `src/Core/Sorcha.Blueprint.Fluent/` — builder pattern, JSON-LD output
3. `src/Core/Sorcha.Blueprint.Schemas/` — schema providers, embedded resources
4. `src/Core/Sorcha.Register.Core/` — managers, governance, consensus logic
5. `src/Core/Sorcha.Register.Storage/` — verified cache, multi-tier abstraction
6. `src/Core/Sorcha.Register.Storage.InMemory/` — in-memory repositories
7. `src/Core/Sorcha.Register.Storage.MongoDB/` — MongoDB queries, index creation
8. `src/Core/Sorcha.Register.Storage.Redis/` — Redis Streams, Polly resilience

**Step 2: Catalogue issues**

Create a tracking section in MASTER-TASKS.md under Phase B (mirroring B3a/B3b/B3c for critical/important/minor) with each issue as a table row.

**Step 3: Fix all issues**

Fix each issue, verify tests still pass after each fix batch.

**Step 4: Commit per severity tier**

```bash
git commit -m "fix: [B3a] critical fixes in Core libraries"
git commit -m "fix: [B3b] important fixes in Core libraries"
git commit -m "fix: [B3c] minor fixes in Core libraries"
```

---

## Task 5: Verify test coverage for Core libraries

**Files:**
- Read: all test projects that cover Core libraries

**Step 1: Inventory existing tests**

Count tests per Core project:

| Core Project | Test Project | Expected Tests |
|-------------|-------------|----------------|
| Blueprint.Engine | Sorcha.Blueprint.Engine.Tests | 27 files |
| Blueprint.Fluent | Sorcha.Blueprint.Fluent.Tests | 8 files |
| Blueprint.Schemas | Sorcha.Blueprint.Schemas.Tests + Core.Tests | 13 files |
| Register.Core | Sorcha.Register.Core.Tests | 18 files |
| Register.Storage | Sorcha.Register.Storage.Tests | 4 files |
| Register.Storage.InMemory | (contract tests in Storage.Tests) | 1 file |
| Register.Storage.MongoDB | Sorcha.Register.Storage.MongoDB.Tests | 2 files |
| Register.Storage.Redis | Sorcha.Register.Storage.Redis.Tests | 4 files |

**Step 2: Run all Core-related tests**

Run: `dotnet test --filter "FullyQualifiedName~Blueprint.Engine|FullyQualifiedName~Blueprint.Fluent|FullyQualifiedName~Blueprint.Schemas|FullyQualifiedName~Register.Core|FullyQualifiedName~Register.Storage" --verbosity normal`

Expected: All pass (note 17 pre-existing JsonLogic failures in Blueprint.Engine are known).

**Step 3: Identify untested public APIs**

For each Core project, compare public types/methods against test coverage. Focus on:
- Public methods with no test at all
- Complex branching logic with only happy-path tests
- Error/edge cases

**Step 4: Fill critical test gaps (if any)**

If significant gaps are found, create new test files following the pattern `MethodName_Scenario_ExpectedBehavior`. Add them to existing test projects.

**Step 5: Commit**

```bash
git add tests/
git commit -m "test: [B4] fill test gaps in Core libraries"
```

---

## Task 6: Update CI/CD workflows for Core libraries

**Files:**
- Modify: `.github/workflows/nuget-publish.yml`
- Modify: `.github/workflows/nuget-ci.yml`

**Step 1: Update nuget-publish.yml**

Add `src/Core/**` to path triggers (line 7):

```yaml
on:
  push:
    branches: [ master ]
    paths:
      - 'src/Common/**'
      - 'src/Core/**'
```

Update the force-publish library list (line 36) to include Core libraries:

```
["Sorcha.Register.Models","Sorcha.Tenant.Models","Sorcha.Blueprint.Models","Sorcha.Storage.Abstractions","Sorcha.Cryptography","Sorcha.Blueprint.Schemas","Sorcha.Storage.InMemory","Sorcha.Storage.EFCore","Sorcha.Storage.MongoDB","Sorcha.Storage.Redis","Sorcha.TransactionHandler","Sorcha.Validator.Core","Sorcha.Wallet.Core","Sorcha.ServiceClients","Sorcha.ServiceDefaults","Sorcha.Blueprint.Engine","Sorcha.Blueprint.Fluent","Sorcha.Register.Core","Sorcha.Register.Storage","Sorcha.Register.Storage.InMemory","Sorcha.Register.Storage.MongoDB","Sorcha.Register.Storage.Redis"]
```

Add a second detection loop for Core libraries after the Common loop (after line 51):

```bash
for dir in src/Core/*/; do
  LIB=$(basename "$dir")
  if git diff --name-only HEAD~1 HEAD | grep -q "src/Core/$LIB/"; then
    if [ "$FIRST" = true ]; then
      FIRST=false
    else
      CHANGED="$CHANGED,"
    fi
    CHANGED="$CHANGED\"$LIB\""
  fi
done
```

Update the publish job to handle both Common and Core paths. The `CSPROJ` path detection (line 84) needs to check both locations:

```bash
if [ -f "src/Common/${{ matrix.library }}/${{ matrix.library }}.csproj" ]; then
  CSPROJ="src/Common/${{ matrix.library }}/${{ matrix.library }}.csproj"
elif [ -f "src/Core/${{ matrix.library }}/${{ matrix.library }}.csproj" ]; then
  CSPROJ="src/Core/${{ matrix.library }}/${{ matrix.library }}.csproj"
else
  echo "ERROR: Cannot find csproj for ${{ matrix.library }}"
  exit 1
fi
```

Apply similar path resolution to the build (line 100), pack (line 103), and commit (line 112) steps.

**Step 2: Update nuget-ci.yml**

Add Core to the pack validation step (line 28). Change:

```yaml
- name: Pack NuGet packages (validation only)
  run: |
    dotnet pack src/Common/ --no-build --configuration Release -o ./nupkg
    dotnet pack src/Core/ --no-build --configuration Release -o ./nupkg
```

**Step 3: Verify workflow syntax**

Run: `cat .github/workflows/nuget-publish.yml | head -60` to verify YAML is valid.

**Step 4: Commit**

```bash
git add .github/workflows/
git commit -m "ci: [B5] add Core libraries to NuGet CI/CD workflows"
```

---

## Task 7: Update MASTER-TASKS.md and validate

**Files:**
- Modify: `.specify/MASTER-TASKS.md`

**Step 1: Mark Phase B tasks complete**

Update B1–B5 status to ✅ in MASTER-TASKS.md.

If B3 (code quality) generated sub-tasks, add them as B3a/B3b/B3c tables matching the Phase A pattern.

**Step 2: Final validation**

Run full build and test:

```bash
dotnet restore && dotnet build --configuration Release && dotnet test --configuration Release
```

Run pack for all Core libraries:

```bash
dotnet pack src/Core/ --configuration Release -o ./nupkg-validate
ls ./nupkg-validate/
rm -rf ./nupkg-validate
```

Expected: 8 `.nupkg` files, all tests pass.

**Step 3: Commit**

```bash
git add .specify/MASTER-TASKS.md
git commit -m "docs: [B5] mark Phase B complete in MASTER-TASKS.md"
```

---

## Execution Order & Dependencies

```
Task 1 (Directory.Build.props)
  └─► Task 2 (csproj cleanup) — depends on Task 1
       └─► Task 3 (NuGet metadata) — depends on Task 2
            └─► Task 4 (code quality) — depends on Task 3
            └─► Task 5 (test gaps) — can run parallel with Task 4
                 └─► Task 6 (CI/CD) — depends on Task 3
                      └─► Task 7 (validation) — depends on all above
```

Tasks 4 and 5 can run in parallel after Task 3 completes.
