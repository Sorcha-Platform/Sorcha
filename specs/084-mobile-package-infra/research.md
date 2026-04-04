# Research: Mobile Package Infrastructure

**Feature**: 084-mobile-package-infra
**Date**: 2026-04-04

## Research Summary

All unknowns were resolved during brainstorming and codebase analysis. No NEEDS CLARIFICATION items.

---

## R1: Multi-Target Framework Requirement

**Decision**: Single-target net10.0 only. No multi-targeting.

**Rationale**: SorchaMobile confirmed as .NET 10 MAUI project. Previous MOB-001 (multi-target net8.0;net10.0) is eliminated. This removes significant complexity — no `#if` conditionals, no API surface trimming, no TFM-specific package dependencies.

**Alternatives considered**:
- `net10.0;net8.0` multi-target: Originally planned but unnecessary now. Would add build complexity and API surface management overhead.

---

## R2: Entity Extraction Safety

**Decision**: All 12 wallet entities are safe to extract to Portable. Zero EF Core annotations found.

**Rationale**: Comprehensive codebase analysis confirmed all entities use POCO patterns with fluent API configuration in `WalletDbContext.OnModelCreating()`. No `[Required]`, `[Key]`, `[Column]`, `[ForeignKey]`, or other data annotation attributes. The DbContext configures all relationships, constraints, and column types via the fluent API. Entities can be referenced without EF Core.

**Evidence**: Audited all 12 entity files — Wallet.cs, WalletAddress.cs, WalletAccess.cs, WalletTransaction.cs, CredentialEntity.cs, RecoveryKeyWrap.cs, RecoveryAuditLog.cs, OrgMasterKey.cs, DerivedKeyRecord.cs, ThresholdKeyGroup.cs, SigningKeyShare.cs, SigningSession.cs.

---

## R3: PeerServiceClient Hybrid Split

**Decision**: PeerServiceClient stays in Sorcha.ServiceClients (gRPC package). Interface moves to Http.

**Rationale**: `PeerServiceClient` creates an internal `GrpcChannel` for peer discovery and communication, plus uses `HttpClient` for REST fallback. Splitting the implementation would break the fallback mechanism. Mobile doesn't use peer-to-peer communication, so it doesn't need the implementation — just the interface for DI completeness. The interface (`IPeerServiceClient`) moves to ServiceClients.Http so mobile can register a no-op stub.

**Alternatives considered**:
- Split into PeerHttpClient + PeerGrpcClient: Breaks the hybrid fallback logic. More complexity for no mobile benefit.
- Leave interface in ServiceClients (gRPC): Mobile can't reference the interface without pulling in gRPC deps.

---

## R4: Package Registry

**Decision**: NuGet.org, public and listed. Use existing API key from repo secrets.

**Rationale**: Repository is MIT licensed and public on GitHub. API key already configured. Zero additional infrastructure. SorchaMobile adds packages from the default NuGet feed — no extra NuGet source configuration.

**Alternatives considered**:
- GitHub Packages: Requires NuGet source configuration in SorchaMobile. Extra friction.
- Azure Artifacts: Richer features but overkill for current needs.
- Unlisted on NuGet.org: Obscurity, not security. No benefit for a public MIT project.

---

## R5: SignalR in HTTP Package

**Decision**: Include `SorchaHubConnectionBuilder` in ServiceClients.Http with `Microsoft.AspNetCore.SignalR.Client` dependency.

**Rationale**: SignalR client package is lightweight (~200KB), mobile-compatible, and has no server-side dependencies. The hub connection helper configures JWT auth token attachment, exponential backoff reconnection, and URL resolution — non-trivial logic that shouldn't be duplicated between web and mobile. Both Sorcha.UI and SorchaMobile use the same Blueprint and Register hubs.

**Alternatives considered**:
- Leave SignalR to each consumer: Duplicates JWT auth setup and reconnection policy.
- Separate Sorcha.SignalR.Client package: Over-engineering for one helper class.

---

## R6: Versioning Strategy

**Decision**: Monorepo shared version from git tags. Pre-release on merge to master, stable on tag push.

**Rationale**: All packages come from the same repo and should version together. A mobile developer consuming `Sorcha.Wallet.Portable` 1.2.0 should know that `Sorcha.ServiceClients.Http` 1.2.0 is compatible. Independent versioning would create a compatibility matrix nightmare.

**Format**: `{major}.{minor}.{patch}` for tags (e.g., `v1.0.0`), `{major}.{minor}.{patch}-ci.{run_number}` for pre-release.

**Alternatives considered**:
- Per-package versioning: Flexible but creates compatibility issues.
- CalVer (date-based): Unusual for .NET packages, confusing for consumers.

---

## R7: Namespace Preservation

**Decision**: Keep original namespaces in moved files. Add `[assembly: InternalsVisibleTo]` as needed.

**Rationale**: Changing namespaces would break every `using` statement in every consumer project — defeating the "zero source changes" constraint. Files physically move to new projects but retain their original namespace (e.g., `Sorcha.Wallet.Core.Domain.Entities` stays even though the file is now in `Sorcha.Wallet.Portable`). Type forwarding is unnecessary because the types are the same — just in a different assembly.

**Impact**: Consumer projects' `using` statements continue working. The assembly reference changes from `Sorcha.Wallet.Core.dll` to `Sorcha.Wallet.Portable.dll` (resolved transitively via Wallet.Core → Wallet.Portable reference).
