# Shared Library NuGet Packaging & Quality Review

**Date:** 2026-02-27
**Status:** Approved

---

## Overview

Comprehensive review and packaging of all 15 shared libraries under `src/Common/` as independently versioned NuGet packages, with new GitHub Actions for automated publishing and Claude review feedback integration.

## Decisions

| Decision | Choice |
|----------|--------|
| NuGet scope | All 15 Common libraries |
| Versioning | Independent minor, synchronized major (all start at 2.0.0) |
| Central Package Management | Full CPM across all 69 projects |
| GitHub Actions kept | Claude code review, CodeQL, Playwright |
| Review feedback loop | Auto-create GitHub issues from Claude review comments |
| Starting major version | 2.0.0 |
| Company/Authors | Sorcha Contributors |
| Repository URL | https://github.com/sorcha-platform/sorcha |
| Branding | Complete removal of all legacy branding references |
| Historical references | Reworded to "Sorcha AI Development spike" |
| Commit strategy | Squash into single clean commit with abbreviated summary |

## Phase 1: Foundation

### 1a. Directory.Build.props (solution root, scoped to src/Common/)
Shared metadata for all 15 libraries:
- Authors: Sorcha Contributors
- Company: Sorcha Contributors
- Copyright: Copyright (c) 2026 Sorcha Contributors
- License: MIT
- RepositoryUrl: https://github.com/sorcha-platform/sorcha
- SourceLink, symbol packages (.snupkg), deterministic builds
- GenerateDocumentationFile: true

### 1b. Directory.Packages.props (solution root)
Single source of truth for all NuGet package versions across all 69 projects. Every .csproj drops Version= attributes from PackageReference elements.

### 1c. Update all .csproj files
- Remove inline version attributes (moved to Directory.Packages.props)
- Add PackageId, Version (2.0.0), Description, PackageTags to the 12 unconfigured libraries
- Remove duplicate metadata that's now in Directory.Build.props

### 1d. Standardize LanguageVersion
All 15 Common libraries set to C# 13 (currently mixed: 12, 13, latest, or omitted).

### 1e. Branding purge -- COMPLETE
- All legacy branding references removed from source code, docs, and specs
- Old GitHub URLs replaced with github.com/sorcha-platform/sorcha
- Historical references reworded to "Sorcha AI Development spike"
- Package tags updated to sorcha

## Phase 2: GitHub Actions

### Remove
- main-ci-cd.yml
- pr-validation.yml
- release.yml
- docker-build-push.yml
- api-gateway-AutoDeployTrigger-*.yml

### Keep (unchanged)
- claude-code-review.yml
- codeql.yml
- playwright.yml

### Create

**nuget-ci.yml** — PR build & test
- Trigger: Pull requests to master
- Detects changed Common libraries
- Builds full solution, runs tests for changed + dependents
- Packs NuGet (no publish) to validate packaging

**nuget-publish.yml** — Auto version bump & publish
- Trigger: Push to master touching src/Common/**
- Per changed library: read version → bump minor → commit → pack → publish
- Uses secrets.NUGET_API_KEY, --skip-duplicate
- Dependency-ordered publishing (e.g., Cryptography before TransactionHandler)

**claude-review-issues.yml** — Review feedback → GitHub issues
- Trigger: workflow_run after claude-code-review completes
- Reads Claude review comments via GitHub API
- Creates labeled (claude-review) GitHub issues for actionable items
- Deduplicates against existing open issues

### Git remote
- Update origin to https://github.com/sorcha-platform/sorcha

## Phase 3: Code Quality Review

Parallel review of all 15 libraries for:

- **Consistency:** License headers, import ordering, naming conventions, XML documentation
- **Duplication:** No duplicate models/DTOs across libraries, no duplicate utilities
- **Completeness:** Fill test gaps (Storage.EFCore, Storage.MongoDB, Tenant.Models, Wallet.Core)
- **Package updates:** Audit all NuGet deps for latest stable, update in Directory.Packages.props
- **Comments:** Remove stale comments, add XML docs to public APIs, clean TODOs
- **Dead code:** Remove unused interfaces/classes, commented-out blocks, compatibility shims

## Phase 4: Validation

- dotnet build — full solution compiles
- dotnet test — all existing tests pass
- dotnet pack — all 15 libraries produce valid .nupkg
- GitHub Actions YAML validated

## NuGet Package Inventory

| PackageId | Version | Description |
|-----------|---------|-------------|
| Sorcha.Blueprint.Models | 2.0.0 | Domain models with JSON-LD for blueprint workflow definitions |
| Sorcha.Blueprint.Schemas | 2.0.0 | Schema management with validation, caching, and storage integration |
| Sorcha.Cryptography | 2.0.0 | Multi-algorithm cryptography: ED25519, P-256, RSA-4096, BLS threshold signatures, post-quantum (ML-KEM/ML-DSA), zero-knowledge proofs, SD-JWT, HD wallet support (BIP32/39/44), Merkle trees, and encoding utilities |
| Sorcha.Register.Models | 2.0.0 | Domain models for the distributed ledger register |
| Sorcha.ServiceClients | 2.0.0 | Consolidated HTTP and gRPC service clients with auth and resilience |
| Sorcha.ServiceDefaults | 2.0.0 | Shared Aspire configuration, telemetry, JWT auth, and OpenAPI |
| Sorcha.Storage.Abstractions | 2.0.0 | Core storage abstraction interfaces for multi-tier storage |
| Sorcha.Storage.EFCore | 2.0.0 | Entity Framework Core PostgreSQL storage implementation |
| Sorcha.Storage.InMemory | 2.0.0 | In-memory storage for development and testing |
| Sorcha.Storage.MongoDB | 2.0.0 | MongoDB document storage implementation |
| Sorcha.Storage.Redis | 2.0.0 | Redis hot-tier caching and storage implementation |
| Sorcha.Tenant.Models | 2.0.0 | Multi-tenant identity and organization domain models |
| Sorcha.TransactionHandler | 2.0.0 | Transaction building, signing, and serialization |
| Sorcha.Validator.Core | 2.0.0 | Enclave-safe validation and consensus logic |
| Sorcha.Wallet.Core | 2.0.0 | Wallet management with HD derivation and key operations |

## Out of Scope

- Docker/docker-compose changes
- Azure deployment workflows
- Service-level code changes (only shared libraries)
- Actual NuGet publishing (happens via new Actions on first real merge)
