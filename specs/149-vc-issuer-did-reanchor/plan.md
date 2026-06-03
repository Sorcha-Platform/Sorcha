# Implementation Plan: Re-anchor org VC-issuer DID to the operational wallet (+ fail-closed issuance)

**Branch**: `149-vc-issuer-did-reanchor` | **Date**: 2026-06-03 | **Spec**: [spec.md](./spec.md)
**Input**: Feature specification from `specs/149-vc-issuer-did-reanchor/spec.md`; design doc `docs/superpowers/specs/2026-06-03-org-vc-issuer-did-reanchor-design.md`; research `research.md`.

## Summary

A native SD-JWT VC's issuer DID is currently anchored on the org's derived VC-issuance child wallet (C), which never matches the org's canonical operational wallet (A = `Organization.WalletAddress`) used by the rest of the platform (register ownership, invitations, X.509 SAN, trust allowlists). Re-anchor `iss`/`kid`/published-`did.json` onto A, with the derived child C's key published as a verification method **under** `did:sorcha:org:{A}`; make the verifier resolve the **published** Tenant `did.json` (because the signing key C ≠ the DID-subject wallet A); and make issuance **fail closed** when no resolvable issuer DID can be produced. Clean break (no migration). Seven coupled changes across Tenant, Wallet, Blueprint, HAIP, and `ServiceClients.Http` (see `research.md` summary table).

## Technical Context

**Language/Version**: C# 14 / .NET 10
**Primary Dependencies**: ASP.NET Core Minimal APIs, EF Core (Tenant), `Sorcha.Cryptography.SdJwt`, `Sorcha.ServiceClients.Http`, OpenTelemetry
**Storage**: Tenant PostgreSQL (`OrgDidDocuments`, `Organization`); no schema change (`PrimaryDid` index already exists, `TenantDbContext.cs:205`)
**Testing**: xUnit + FluentAssertions + Moq; existing per-service test projects (`Sorcha.Wallet.Service.Tests`, `Sorcha.Tenant.Service.Tests`, `Sorcha.Blueprint.Service.Tests`/engine)
**Target Platform**: Linux containers (docker-compose / Aspire); services reachable via `ServiceClients:TenantService:Address`
**Project Type**: Web — multi-service backend (microservices)
**Performance Goals**: No regression; one added internal GET (org→wallet-address) per first issuance per org (lazy-derive path); one added Tenant `did.json` fetch per issuer-key resolution (verification), cacheable like existing DID resolution
**Constraints**: WASM-safe engine boundary preserved (engine holds only `IDidResolverRegistry`); fail-closed trust posture (no silent degradation); no compiler warnings in Release
**Scale/Scope**: ~7 code changes + tests; no data migration (pre-production clean break)

## Constitution Check

*GATE: Must pass before Phase 0 research. Re-check after Phase 1 design.*

- **I. Microservices-First / dependency direction**: PASS. New cross-service call is Wallet→Tenant (downward via an HTTP client in `ServiceClients.Http`), mirroring the existing `IOrgDidDocumentClient`. No upward or engine→service dependency added; the engine keeps only the `IDidResolverRegistry` seam.
- **II. Security First**: PASS / strengthened. Removes a silent-degradation path (bare-wallet unverifiable `iss`) and enforces fail-closed issuance + fail-closed verification. New internal endpoint is `RequireService` (service-principal, `:service` audience). New public `did.json` by-DID route exposes only already-public DID-document data (the existing `did.json` route is anonymous).
- **III. API Documentation**: PASS. New endpoints get `.WithSummary()`/`.WithDescription()` and XML docs on client methods (no Swagger; Scalar/OpenAPI per existing pattern).
- **IV. Testing**: PASS. Unit + integration tests per user story (US1 trusted issuance EdDSA, US2 fail-closed, US3 rotation); target >85% on changed code; deterministic (mock Tenant client / DID fetch).
- **V. Code Quality**: PASS. Nullable handled explicitly (null-A → fail closed); async I/O; DI; no new warnings.
- **VII. Domain-Driven Design / ubiquitous language**: PASS. Uses Participant/Publish/Disclosure terms; "issuer DID", "verification method", "assertionMethod" are W3C/IETF domain terms.
- **VIII. Observability**: PASS. Reuse existing `Sorcha.Identity`/issuance metrics; add structured (non-interpolated) log + a counter tag for fail-closed refusals and for DID-resolution source (published vs none). No new meter required.

**No violations — Complexity Tracking not required.**

## Project Structure

### Documentation (this feature)

```text
specs/149-vc-issuer-did-reanchor/
├── plan.md              # This file
├── spec.md              # Feature spec
├── research.md          # Phase 0 decisions (D1–D6)
├── data-model.md        # Phase 1 — entities/state touched (no new tables)
├── quickstart.md        # Phase 1 — how to validate end-to-end
├── contracts/           # Phase 1 — the two new endpoints
│   ├── org-wallet-address.internal.md
│   └── org-did-by-did.public.md
└── tasks.md             # Phase 2 — /speckit.tasks output
```

### Source Code (repository root) — files touched

```text
src/Services/Sorcha.Tenant.Service/
├── Endpoints/InternalEndpoints.cs                 # + GET /api/internal/orgs/{orgId}/wallet-address
├── Endpoints/OrgDidDocumentEndpoints.cs           # + GET /orgs/by-did/{did}/did.json
└── Services/OrgDidDocumentService.cs              # + GetByPrimaryDidAsync (PrimaryDid lookup); id stays opaque

src/Common/Sorcha.ServiceClients.Http/
├── OrgInfo/IOrgInfoClient.cs + OrgInfoClient.cs   # + ResolveCanonicalWalletAddressAsync(orgId)
└── Did/SorchaDidResolver.cs                        # ResolveOrgDidAsync → Tenant by-DID did.json; drop hardcoded #vc-issuance-1

src/Services/Sorcha.Wallet.Service/
├── Services/Implementation/IssuanceKeyService.cs   # iss/kid/snapshot from A (via IOrgInfoClient); null-A → null material
└── Endpoints/CredentialEndpoints.cs                # fail-closed guard; remove bare-wallet/null-kid fallback

src/Services/Sorcha.Blueprint.Service/Program.cs    # override AddScoped<SorchaDidResolver> → 3-arg ctor, Tenant base address
src/Services/Sorcha.Haip.Service/Program.cs         # repoint public-DID HttpClient base address Wallet → Tenant

tests/
├── Sorcha.Wallet.Service.Tests/                    # IssuanceKeyService (A-anchored, null-A), CredentialEndpoints fail-closed
├── Sorcha.Tenant.Service.Tests/                    # by-DID did.json route + org wallet-address endpoint
└── Sorcha.Blueprint.Service.Tests/ (+ engine)      # DidX5cIssuerKeyResolver verifies via published doc; rotation; allowlist on A
```

**Structure Decision**: Multi-service backend; changes follow existing service-folder conventions (`Endpoints/`, `Services/Implementation/`) and the `ServiceClients.Http/<Area>/I*Client.cs + *Client.cs` client pattern. No new project.

## Phase 0 — Research

Complete. See `research.md` (decisions D1–D6, blast-radius, alternatives). All NEEDS CLARIFICATION resolved.

## Phase 1 — Design & Contracts

- `data-model.md` — entities and state touched (no new tables; `Organization.WalletAddress` read path, `OrgDidDocument.PrimaryDid` lookup, `IssuanceKeyState` rotation set).
- `contracts/` — the two new endpoints (internal org→wallet-address; public org `did.json` by-DID).
- `quickstart.md` — end-to-end validation against the local Docker stack (CE-UAC-shaped: org with master key issues, allowlist on A, verify accepts; org without master key fails closed).
- Agent context refresh via `update-agent-context.ps1`.

## Phase 2 — Tasks

Generated by `/speckit.tasks` into `tasks.md` (not produced by this command).

## Complexity Tracking

No constitution violations — section intentionally empty.
