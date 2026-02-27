# Task: Configure NuGet Package

**ID:** TX-015
**Status:** ✅ Complete
**Priority:** High
**Estimate:** 3 hours
**Created:** 2025-11-12
**Completed:** 2025-11-13

## Objective

Configure NuGet package with proper metadata, versioning, and dependencies.

## Package Configuration

### .csproj Metadata
```xml
<PropertyGroup>
  <PackageId>Sorcha.TransactionHandler</PackageId>
  <Version>2.0.0</Version>
  <Authors>Sorcha Contributors</Authors>
  <Description>Transaction and payload management library for the Sorcha distributed ledger platform. Provides transaction building, signing, verification, and multi-recipient payload encryption.</Description>
  <PackageTags>sorcha;blockchain;transaction;payload;distributed-ledger;encryption</PackageTags>
  <RepositoryUrl>https://github.com/sorcha-platform/sorcha</RepositoryUrl>
  <PackageLicenseExpression>MIT</PackageLicenseExpression>
  <PackageIcon>icon.png</PackageIcon>
  <PackageReadmeFile>README.md</PackageReadmeFile>
</PropertyGroup>
```

### Release Notes
```markdown
# Sorcha.TransactionHandler v2.0.0

## Features
- ✅ Fluent TransactionBuilder API
- ✅ Multi-recipient payload encryption
- ✅ Per-recipient access control
- ✅ Transaction signing with double SHA-256
- ✅ Backward compatibility (v1-v4 transactions)
- ✅ Multiple serialization formats (Binary, JSON, Transport)
- ✅ Comprehensive test coverage (>90%)

## Dependencies
- Sorcha.Cryptography v2.0.0
- System.Text.Json v9.0.0
```

## Acceptance Criteria

- [ ] Package metadata complete
- [ ] Dependencies correctly specified
- [ ] Package builds successfully
- [ ] Can be installed in test project

---

**Dependencies:** TX-001 through TX-014
