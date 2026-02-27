# Task: Setup Sorcha.TransactionHandler Project

**ID:** TX-001
**Status:** Not Started
**Priority:** Critical
**Estimate:** 4 hours
**Created:** 2025-11-12

## Objective

Create the new `Sorcha.TransactionHandler` library project with proper configuration, dependencies on Sorcha.Cryptography v2.0, and organized folder structure.

## Implementation Details

### Project Configuration (.csproj)
```xml
<Project Sdk="Microsoft.NET.Sdk">
  <PropertyGroup>
    <TargetFramework>net10.0</TargetFramework>
    <LangVersion>12</LangVersion>
    <Nullable>enable</Nullable>

    <!-- Package info -->
    <PackageId>Sorcha.TransactionHandler</PackageId>
    <Version>2.0.0</Version>
    <Authors>Sorcha Contributors</Authors>
    <Company>Sorcha Contributors</Company>
    <Description>Transaction and payload management library for the Sorcha platform</Description>
    <PackageTags>sorcha;blockchain;transaction;payload;distributed-ledger</PackageTags>
    <RepositoryUrl>https://github.com/sorcha-platform/sorcha</RepositoryUrl>
    <PackageLicenseExpression>MIT</PackageLicenseExpression>

    <!-- Documentation -->
    <GenerateDocumentationFile>true</GenerateDocumentationFile>
    <NoWarn>$(NoWarn);1591</NoWarn>

    <!-- Build -->
    <Deterministic>true</Deterministic>
    <PublishRepositoryUrl>true</PublishRepositoryUrl>
    <EmbedUntrackedSources>true</EmbedUntrackedSources>
    <IncludeSymbols>true</IncludeSymbols>
    <SymbolPackageFormat>snupkg</SymbolPackageFormat>
  </PropertyGroup>

  <ItemGroup>
    <!-- Core dependency -->
    <PackageReference Include="Sorcha.Cryptography" Version="2.0.0" />
    <PackageReference Include="System.Text.Json" Version="9.0.0" />
    <PackageReference Include="Microsoft.SourceLink.GitHub" Version="8.0.0" PrivateAssets="All" />
  </ItemGroup>
</Project>
```

### Folder Structure
```
src/Common/Sorcha.TransactionHandler/
├── Sorcha.TransactionHandler.csproj
├── Enums/
├── Interfaces/
├── Core/
├── Payload/
├── Serialization/
├── Models/
└── Versioning/
```

## Acceptance Criteria

- [ ] New project created in correct location
- [ ] Depends on Sorcha.Cryptography v2.0
- [ ] All folders created
- [ ] Project builds without errors
- [ ] XML documentation enabled
- [ ] Added to Sorcha.sln

---

**Dependencies:** Sorcha.Cryptography v2.0 must be available
