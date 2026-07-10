# Implementation Plan: EUDI Conformance — Protocol Alignment & External Trust Rail

**Branch**: `181-eudi-conformance` | **Date**: 2026-07-10 | **Spec**: [spec.md](./spec.md)

**Input**: Feature specification from `/specs/181-eudi-conformance/spec.md`

## Summary

Bring Sorcha's presentation dialect up to the final EUDI-aligned profiles (DCQL replaces Presentation
Exchange, `dc+sd-jwt` replaces `vc+sd-jwt`, prefixed `x509_san_dns:` client identifiers, multibase
status-list decode) as a clean break with **all OpenID4VP routes preserved**, and make the external
X.509 trust rail real (ETSI TS 119 612 trusted-list snapshot import backing `x509-lotl`,
operator-imported external org certificates, persistent internal CA with auto-enrolment, typed
Ed25519 exclusion, wallet-side verifier authentication). Technical approach: one shared DCQL
builder/parser in the WASM-safe `Sorcha.Verifier.Engine`; a persistent Tenant-hosted trusted-list
snapshot store filling the already-wired F135 `trustlist` resolver seam; EF persistence under the
existing `InternalCaTrustProvider`; a remote-signing CSR generator over the existing Wallet sign seam.
Full decisions in [research.md](./research.md) (R1–R15).

## Technical Context

**Language/Version**: C# 14 / .NET 10 (net10.0 everywhere; PWA + Components.User are Blazor WASM)

**Primary Dependencies**: existing only — `System.Text.Json`, `System.Security.Cryptography.Xml`
(TS 119 612 dsig, server-side), `System.Formats.Asn1` + BCL `CertificateRequest`/`X509SignatureGenerator`
(CSR, server-side), BouncyCastle.Cryptography (already in Verifier.Engine; WASM request-object
verification), EF Core/Npgsql (Tenant), MudBlazor (admin UI). **No new NuGet packages.**

**Storage**: Tenant Postgres `public` schema — `TrustedListSnapshot(+Anchors)`, `TenantRootCa`,
`OrgCertificateRecord`, `CsrRecord` (data-model.md §2–3); squashed into Tenant `InitialCreate` per the
pre-release convention; `IStorageRegistrationLog` registered, warn-tier (not fail-fast audited).

**Testing**: xUnit + FluentAssertions + Moq; reflection-based endpoint-handler tests (repo pattern);
bUnit for PWA components; fixture-generated signed TS 119 612 XML + test CA; walkthroughs as the SC-002
regression oracle; deliberate red-test for the CI dialect gate (SC-008).

**Target Platform**: Linux containers (services) + Blazor WASM (wallet PWA, shared components).
WASM constraint drives R13: request-object crypto via BouncyCastle, never BCL `X509Chain`/`ECDsa`.

**Project Type**: multi-service platform — changes span 2 common libs, 3 services, 2 WASM apps, 1 CLI
agent, scripts/CI.

**Performance Goals**: no regression on the presentation hot path (dialect swap is shape-for-shape);
anchor reads served from a 15-min in-process cache (one Tenant HTTP call per service per window);
trusted-list import is an operator action (seconds-scale acceptable).

**Constraints**: existing OpenID4VP/VCI routes byte-stable (D1/FR-002); verify-side must keep accepting
already-issued `vc+sd-jwt` credentials (FR-004); org private keys never leave Wallet Service (FR-018);
fail-closed at every trust boundary; clean break enforced by CI gate.

**Scale/Scope**: ~6 DCQL model types; ~10 touched producer/parser/verifier sites; 4 new EF entities;
~8 new/changed endpoints; 2 admin UI surfaces (trust-list panel, org-certificates panel); ~12 typed
error codes; 5 metrics.

## Constitution Check

*GATE evaluated pre-Phase 0 and re-checked post-design — PASS (no violations to justify).*

| Principle | Compliance |
|---|---|
| I. Microservices-first | No new services; new common code goes to existing WASM-safe lib; Tenant remains the trust authority; downward deps only (Engine gains no service refs — anchors injected via existing seams). |
| II. Security first | Fail-closed trust decisions throughout (FR-014/016/020); CA private keys move from **plaintext in-memory** to AES-256-GCM-at-rest EF rows (improves current state); input validation (FluentValidation/DataAnnotations) on import/CSR endpoints per VAL-001 pattern; no secrets in source. |
| III. API documentation | New/changed Minimal API endpoints get `.WithSummary()`/`.WithDescription()`; OpenAPI contracts authored in `contracts/`; Scalar (no Swashbuckle). |
| IV. Testing | Unit coverage targets >85% on new code; deterministic fixtures (self-generated signed TL + test CA — no network); integration tests per endpoint; AAA + `MethodName_Scenario_ExpectedBehavior`. |
| V. Code quality | Nullable enabled, async I/O, DI seams reused (`ITrustListProvider`, `ITenantTrustAnchorProvider`, `IWalletServiceClient`). |
| VI. Blueprint standards | `credentialRequirements` JSON stays the authoring surface; DCQL is derived wire shape, never authored. |
| VII. DDD language | "Trusted-list snapshot", "trust anchor", "org certificate", "credential query" adopted consistently; Blueprint/Action/Participant terms untouched. |
| VIII. Observability | 5 new instruments on existing meters (data-model §7); structured logging; health unaffected. |

## Project Structure

### Documentation (this feature)

```text
specs/181-eudi-conformance/
├── plan.md              # This file
├── research.md          # Phase 0 — R1..R15 decisions
├── data-model.md        # Phase 1 — DCQL model, EF entities, error codes, metrics
├── quickstart.md        # Phase 1 — per-US validation recipes
├── contracts/
│   ├── dcql-presentation.md          # dialect contract (routes unchanged)
│   ├── trustlist-admin.openapi.yaml  # snapshot import/lifecycle/anchor read
│   └── org-certificates.openapi.yaml # CSR / import / list / enrol semantics
├── checklists/requirements.md
└── tasks.md             # Phase 2 (/speckit.tasks — NOT created by /speckit.plan)
```

### Source Code (repository root)

```text
src/Common/Sorcha.Verifier.Engine/
├── Dcql/                                  # NEW — shared model + builder + parser (R1/R2, FR-008)
│   ├── DcqlModels.cs                      #   DcqlQuery/CredentialQuery/CredentialSetQuery/VpToken
│   ├── DcqlRequestBuilder.cs              #   the ONE producer
│   └── DcqlRequestParser.cs               #   the ONE parser (same review unit as builder)
├── RequestObjectValidator.cs              # NEW — JWS x5c verify + SAN binding + anchor check (R13)
└── VerifiablePresentationValidator.cs     # CHANGED — object-keyed vp_token, per-query, aud=prefixed client_id

src/Common/Sorcha.Cryptography/SdJwt/SdJwtService.cs        # CHANGED — typ dc+sd-jwt (R3)
src/Core/Sorcha.Blueprint.Engine/Credentials/
├── BitstringStatusListChecker.cs          # CHANGED — multibase 'u' decode (R7, line 86)
└── Sources/…                              # unchanged — trustlist seam already wired (F135)

src/Common/Sorcha.ServiceClients.Http/Trust/
└── TrustListProvider.cs                   # CHANGED — HTTP-backed caching ITrustListProvider (R6)

src/Services/Sorcha.Tenant.Service/
├── Trust/
│   ├── X509CertificateBuilder.cs          # CHANGED — eligibility guard (typed CERT_KEY_NOT_ELIGIBLE)
│   ├── InternalCaTrustProvider.cs         # CHANGED — write-through cache over EF store (R8)
│   ├── TrustedListImportService.cs        # NEW — TS 119 612 parse + XMLDSig verify + extract (R5)
│   ├── OrgCertificateService.cs           # NEW — eligibility/CSR/import/auto-enrol logic (R9/R10/R11)
│   └── WalletBackedSignatureGenerator.cs  # NEW — X509SignatureGenerator over IWalletServiceClient (R10)
├── Endpoints/TrustEndpoints.cs            # CHANGED — import/list/detail/delete/anchors + cert routes
├── Services/OrganizationService.cs        # CHANGED — auto-enrol hook post-wallet-provision (FR-022)
├── Services/OrgWalletReconciliationService.cs  # CHANGED — enrol retry rides reconciliation
├── Models/ + Data/TenantDbContext.cs      # NEW entities (data-model §2–3), squashed migration
└── Migrations/…InitialCreate.cs           # CHANGED — squash per pre-release convention

src/Services/Sorcha.Haip.Service/
├── Endpoints/VerifierEndpoints.cs         # CHANGED — dcql_query, prefixed client_id, object vp_token,
│                                          #   LEGACY_DIALECT 400, x5c on request object (R4/R12)
├── Endpoints/{Metadata,Credential}Endpoints.cs  # CHANGED — dc+sd-jwt format identifiers
└── Services/RequestObjectSigner.cs        # CHANGED — signs with verifier certificate (R12)

src/Services/Sorcha.Blueprint.Service/Services/Implementation/
└── SorchaWalletPresentationConsumer.cs    # CHANGED — request_uri form + DCQL body (R4)

src/Services/Sorcha.Wallet.Service/…/IssueCredentialChainResolver.cs  # CHANGED — x509-lotl → imported chain (R11)

src/Apps/Sorcha.Wallet.Pwa/
├── Services/Presentation/PresentationEngine.cs  # CHANGED — request_uri fetch, DCQL parse, query-set Match,
│                                                #   object vp_token, prefixed aud (R4/R13)
└── Pages/Present.razor                          # CHANGED — multi-query flow

src/Apps/Sorcha.UI/Sorcha.UI.Components.User/
├── Components/Presentation/{ConsentSheet,CredentialPickerDialog}.razor  # CHANGED — per-query + alternatives
└── Models/User/Presentation/PresentationModels.cs                        # CHANGED — query-set shapes

src/Apps/Sorcha.UI/Sorcha.UI.Web.Client/Pages/Admin/
├── Settings/OrgSettings.razor             # CHANGED — org certificates panel (US4/US5)
└── (platform admin)                       # NEW — trusted-lists panel (US3, SC-009)

src/Apps/Sorcha.Agent/Commands/HaipPresentCommand.cs  # CHANGED — drop submission, object vp_token

scripts/check-presentation-dialect.ps1     # NEW — CI ratchet gate (R14, FR-009)
.github/workflows/…                        # CHANGED — gate step
walkthroughs/, demos/                      # CHANGED — R15 inventory (fixtures regenerated as DCQL)

tests/  # per-project test additions mirroring every CHANGED/NEW item above; fixtures:
        # signed TS 119 612 XML generator + test CA under tests/Sorcha.Tenant.Service.Tests/Fixtures/TrustLists/
```

**Structure Decision**: no new projects, no new services. The single genuinely shared artefact (DCQL
model) lands in the existing WASM-safe `Sorcha.Verifier.Engine` (R1); all trust-rail state is Tenant-owned
(it already hosts the CA + trust endpoints); every other change is in-place migration of the surfaces
inventoried in R4/R15.

## Delivery order (feeds /speckit.tasks)

1. **Foundation (US1 prerequisites)**: DCQL model + builder/parser + tests → `dc+sd-jwt` typ flip +
   dual-accept → CI gate (lands early with allowlist, ratchets to empty).
2. **US1 dialect cutover**: HAIP verifier → Blueprint consumer → engine validator → PWA engine/UI →
   agent → walkthrough/demo/test migration (SC-001/002).
3. **US2 multi-credential**: query-set Match + consent UI + per-query results (SC-003). Depends on US1.
4. **US3 trust rail (parallel with US1/US2 after foundation)**: EF snapshot store + import service +
   endpoints + HTTP anchor provider + admin panel + multibase fix (SC-004/009).
5. **US4/US5 certificates**: CA persistence (R8) → eligibility + auto-enrol + backfill + typed Ed25519
   exclusion (SC-006) → CSR + import + chain-attach (SC-005). US4 verification tests lean on US3 fixtures.
6. **US6 verifier auth (last)**: verifier certificate config + prefixed client_id + x5c request objects +
   `RequestObjectValidator` + PWA three-state consent (SC-007). Composes US1 + US3.

Independent-test boundaries and acceptance mapping per story are in spec.md; per-US validation recipes in
quickstart.md.

## Complexity Tracking

*No constitution violations — table intentionally empty.*

## Notes for the tasks phase

- **R9 flag**: certificates bind the org's P-256 key (primary or HAIP co-key). This narrows D6's
  practical exclusion to "no P-256 key resolvable" — confirmed direction needed from the platform owner
  before US5 tasks execute (surfaced in the plan report).
- The Feature 135 placeholder `PUT /api/v1/trust/trustlists/{id}` raw-roots route is replaced by the
  import route (clean break; it has no production callers).
- Documentation sync (CLAUDE.md Feature API pointer → sorcha-architecture skill, STANDARDS.md rows,
  API-DOCUMENTATION.md, service READMEs) is a mandatory tail task per repo policy (FR-029).
