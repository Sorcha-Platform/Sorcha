# Task: Configure NuGet Package

**ID:** TASK-018
**Status:** Not Started
**Priority:** High
**Estimate:** 4 hours
**Assignee:** Unassigned
**Created:** 2025-11-12

## Objective

Configure NuGet package generation with proper metadata, versioning, icon, README, and publish configuration.

## Package Configuration

### .csproj Updates
```xml
<PropertyGroup>
  <!-- Package metadata -->
  <PackageId>Sorcha.Cryptography</PackageId>
  <Version>2.0.0</Version>
  <Authors>Sorcha Contributors</Authors>
  <Company>Sorcha Contributors</Company>
  <Product>Sorcha.Cryptography</Product>
  <Description>Standalone cryptography library for the Sorcha platform, providing key management, digital signatures, encryption, and encoding utilities for ED25519, NIST P-256, and RSA-4096.</Description>
  <Copyright>Copyright © 2025 Sorcha Contributors</Copyright>

  <!-- Package tags -->
  <PackageTags>cryptography;sorcha;blockchain;ed25519;ecdsa;rsa;encryption;signing;wallet;bip39</PackageTags>

  <!-- URLs -->
  <RepositoryUrl>https://github.com/sorcha-platform/sorcha</RepositoryUrl>
  <PackageProjectUrl>https://github.com/sorcha-platform/sorcha</PackageProjectUrl>
  <PackageLicenseExpression>MIT</PackageLicenseExpression>
  <PackageRequireLicenseAcceptance>false</PackageRequireLicenseAcceptance>

  <!-- Package assets -->
  <PackageIcon>icon.png</PackageIcon>
  <PackageReadmeFile>README.md</PackageReadmeFile>

  <!-- Source link -->
  <PublishRepositoryUrl>true</PublishRepositoryUrl>
  <EmbedUntrackedSources>true</EmbedUntrackedSources>
  <IncludeSymbols>true</IncludeSymbols>
  <SymbolPackageFormat>snupkg</SymbolPackageFormat>

  <!-- Strong naming (optional) -->
  <SignAssembly>false</SignAssembly>

  <!-- Deterministic builds -->
  <Deterministic>true</Deterministic>
  <ContinuousIntegrationBuild Condition="'$(GITHUB_ACTIONS)' == 'true'">true</ContinuousIntegrationBuild>
</PropertyGroup>

<ItemGroup>
  <None Include="icon.png" Pack="true" PackagePath="\" />
  <None Include="README.md" Pack="true" PackagePath="\" />
</ItemGroup>
```

### Package Release Notes

Create `RELEASE_NOTES.md`:
```markdown
# Sorcha.Cryptography v2.0.0

## Breaking Changes
- Complete rewrite of legacy cryptography library
- New API with async/await support
- Renamed namespaces to `Sorcha.Cryptography`

## New Features
- ✅ ED25519, NIST P-256, and RSA-4096 support
- ✅ BIP39 mnemonic recovery phrases
- ✅ Multiple symmetric encryption algorithms
- ✅ Bech32 wallet addresses (ws1 prefix)
- ✅ Comprehensive test coverage (>90%)
- ✅ Full async/await support
- ✅ Minimal dependencies (only Sodium.Core)

## Migration Guide
See [MIGRATION.md](MIGRATION.md) for upgrading from v1.x
```

## Tasks

1. **Create Package Icon**
   - [ ] Design 128x128 PNG icon
   - [ ] Add to project root as `icon.png`

2. **Create Package README**
   - [ ] Quick start guide
   - [ ] Installation instructions
   - [ ] Basic usage examples
   - [ ] Link to full documentation

3. **Configure Build Pipeline**
   - [ ] Add pack command to CI/CD
   - [ ] Configure version stamping
   - [ ] Setup NuGet publish action

4. **Test Package Locally**
   ```bash
   dotnet pack -c Release
   dotnet nuget push bin/Release/Sorcha.Cryptography.2.0.0.nupkg --source local-feed
   ```

## Acceptance Criteria

- [ ] Package metadata complete
- [ ] Package icon included
- [ ] Package README included
- [ ] Source link configured
- [ ] Symbol package (.snupkg) generated
- [ ] Package builds successfully
- [ ] Package can be installed and used in test project
- [ ] All dependencies correctly specified

---

**Task Control**
- **Created By:** Claude Code
- **Dependencies:** TASK-001 through TASK-017
