# Implementation Plan: Credential-gated second council service (Blue Badge)

**Branch**: `127-credential-gated-service` | **Date**: 2026-05-15 | **Spec**: [`spec.md`](./spec.md)
**Input**: Feature specification from `specs/127-credential-gated-service/spec.md`
**Design contract**: [`docs/superpowers/specs/2026-05-15-spec-4-credential-gated-second-service-design.md`](../../docs/superpowers/specs/2026-05-15-spec-4-credential-gated-second-service-design.md) — see §14 for F111 reconciliation
**Boundary contract**: [`docs/superpowers/specs/2026-05-15-platform-consumer-boundary-design.md`](../../docs/superpowers/specs/2026-05-15-platform-consumer-boundary-design.md)
**F111 reconciliation**: [`docs/superpowers/specs/2026-05-15-f127-f111-reconciliation.md`](../../docs/superpowers/specs/2026-05-15-f127-f111-reconciliation.md)

## Summary

Spec 4 of the Strathcarron citizen arc. Sarah returns to her council weeks after onboarding (Spec 3) to apply for a Blue Badge. The application's first action is **gated on her existing `AssuredIdentityCredential`** — she presents it from her wallet, the council form is pre-populated with the disclosed claims, she fills the Blue Badge-specific fields, submits, and the `BlueBadgeCredential` lands in the same wallet.

Spec 4 ships two artifacts on opposite sides of the platform-vs-consumer boundary:

1. **Platform side** (`src/`, F111-reconciled):
   - A new `IPresentationConsumer` named `"sorcha-wallet"` in `Sorcha.Blueprint.Service`. Wraps `Sorcha.Verifier.Engine` server-side; mirrors the existing `HaipPresentationConsumer` shape.
   - A new optional `BuildInitiationAsync` method on `IPresentationConsumer` (the extension F111's docstring flagged as "deferred to a future phase"); `SorchaWalletPresentationConsumer` overrides it to produce the OID4VP request URI for the citizen's wallet.
   - A new endpoint `GET /api/presentations/{requestId}/disclosed-claims?token={ClaimsFetchToken}` (the small F111 supplement) so the council page can autofill from F111-encrypted-on-register claims in plaintext. The token is issued by `InitiateAsync`, single-use, TTL = remaining validity window.
   - A new SignalR event `IBlueprintHubClient.PresentationOutcomeReady(presentationRequestId)` published from F111's `HandleOutcomeAsync` on success; new group builder `BlueprintHubGroups.PresentationNonce(presentationRequestId)`.
   - `CredentialGateComponent` in `Sorcha.UI.Components.User` — same consumer API as the locked design; internal wiring consumes F111's existing surface (action submission → status poll / hub event → claims-fetch) instead of F127's discarded greenfield endpoints.
   - `IPresentationSignal` in `Sorcha.UI.Components.User` — composes the new SignalR event with F111's existing status-poll endpoint as fallback.
2. **Consumer side** (`samples/strathcarron-portal/`): the Blue Badge page lives in the sample. **PR-A shipped** (creates the sample, moves the F126 page, lands the CI grep gate). **PR-C** adds the Blue Badge blueprint (three-action chain: `verify-identity` → `submit-blue-badge-application` → `issue-blue-badge`) and the Blue Badge page in the sample.

## Technical Context

**Language/Version**: C# 14 / .NET 10.0
**Primary Dependencies**: Blazor WASM (component library + sample host), SignalR (Sorcha hub topology — `BlueprintHub`), Sorcha.Verifier.Engine (server-side validation, extracted in F125), `Sorcha.UI.Components.User` (shared library), MudBlazor (within the library; samples may theme away from defaults)
**Storage**: PostgreSQL via Entity Framework Core (blueprint + credential register; existing F124 / F126 schemas — no new EF migrations expected in Spec 4); Redis via `IAtomicDistributedCache` for the short-lived presentation-request nonce + disclosed-claim stash (mirrors F126's enrol-session pattern)
**Testing**: xUnit + FluentAssertions + Moq (unit + integration); bunit (Blazor component tests for `CredentialGateComponent` + sample pages); Playwright (`[Demo("blue-badge-second-service")]` E2E walk against the Docker stack)
**Target Platform**: Linux containers (docker-compose locally; no n1 in Spec 4); browsers (Blazor WASM samples + PWA); per-component target frameworks per existing csproj definitions
**Project Type**: web — existing multi-service architecture, plus a new consumer sample alongside `src/Apps/`
**Performance Goals**: Presentation-signal latency ≤ 2 s in 95% of attempts (SC-004 / FR-021); Tier 1 returning-citizen journey ≤ 45 s end-to-end in 95% of attempts (SC-001 / FR-010)
**Constraints**: Sample must build to its own container image with NO `ProjectReference` into `src/Apps/Sorcha.UI/` other than `Sorcha.UI.Components.User` (SC-007 / FR-006); CI grep gate enforces (FR-007); no n1 deployment in Spec 4 (FR-009); existing F124 / F125 / F126 demo journeys must remain green (SC-006)
**Scale/Scope**: Single new sample artifact, one new component family (`CredentialGate*`), one new blueprint pattern (`prerequisites.presentationRequests`), two new Blueprint Service endpoints, one new SignalR event (`PresentationReceived(nonce)`), one new credential type (`BlueBadgeCredential`)

## Constitution Check

*GATE: Must pass before Phase 0 research. Re-check after Phase 1 design.*

| Principle | Compliance | Notes |
|---|---|---|
| **I. Microservices-First** | ✅ | New endpoints land in the existing `Sorcha.Blueprint.Service`; no new services. The sample is its own deployable but is a *consumer* of the platform's APIs, not a platform service — boundary doc enforces this. |
| **II. Security First** | ✅ | Presentations validated server-side via `Sorcha.Verifier.Engine`; nonce stashed in `IAtomicDistributedCache` (Redis) with TTL; revoked-credential path tested (FR-019 / SC-005); input validation on both new endpoints. Consent surface is all-or-nothing in v1 (per umbrella Q1 brainstorm decision) — citizen explicitly confirms disclosed claims. |
| **III. API Documentation** | ✅ | Two new endpoints on `Sorcha.Blueprint.Service` documented via .NET 10 OpenAPI + Scalar (`.WithSummary()` / `.WithDescription()`); XML comments on public methods. No Swagger/Swashbuckle. |
| **IV. Testing Requirements** | ✅ | Unit + integration coverage planned per design §9; bunit component tests for `CredentialGateComponent`; Playwright `[Demo("blue-badge-second-service")]` E2E; coverage targets ≥85% on new code. |
| **V. Code Quality** | ✅ | C# 14 / .NET 10 / nullable reference types enabled; async/await on all I/O; DI throughout; no Release-build warnings. |
| **VI. Blueprint Standards** | ✅ | Blue Badge blueprint authored as JSON (`blueprints/strathcarron-blue-badge.json`), same shape as the F126 driving-licence template. `prerequisites.presentationRequests` is declarative JSON, not code. |
| **VII. Domain-Driven Design** | ✅ | Uses Blueprint / Action / Participant / Disclosure / Publish vocabulary consistently. New domain term: **credential gate** (a prerequisite on a starting action that demands a verifiable presentation before the action can run). |
| **VIII. Observability** | ✅ | OpenTelemetry instrumentation on the new endpoints + `IPresentationSignal` latency histogram (SC-004 verification mechanism). Structured logging on the validation pipeline. Health checks unchanged. |

**Result**: No constitutional violations. No entries in Complexity Tracking. Proceed to Phase 0.

## Project Structure

### Documentation (this feature)

```text
specs/127-credential-gated-service/
├── plan.md              # This file
├── spec.md              # Already written (/speckit.specify output)
├── checklists/
│   └── requirements.md  # Quality checklist (all pass)
├── research.md          # Phase 0 output (this command)
├── data-model.md        # Phase 1 output (this command)
├── quickstart.md        # Phase 1 output (this command)
├── contracts/           # Phase 1 output (this command)
│   ├── presentation-requests-endpoint.md
│   ├── presentation-responses-endpoint.md
│   ├── prerequisites-presentation-requests.schema.json
│   └── presentation-received.signalr.md
└── tasks.md             # Phase 2 output (/speckit.tasks command — NOT created here)
```

### Source Code (repository root)

```text
# Platform side (in src/) — F111-reconciled, mostly extends shipped surfaces
src/Apps/Sorcha.UI/Sorcha.UI.Components.User/
└── Components/
    └── CredentialGate/                                 # NEW component family
        └── CredentialGateComponent.razor               # consumes F111's surface internally

src/Apps/Sorcha.UI/Sorcha.UI.Components.User/Services/User/
└── Presentation/                                       # NEW service family
    ├── IPresentationSignal.cs                          # SignalR primary (PresentationOutcomeReady), F111 status-poll fallback, 60 s manual recovery
    └── PresentationSignal.cs                           # mirrors F126 EnrolPairingSignal shape

src/Apps/Sorcha.UI/Sorcha.UI.Components.User/Services/Shared/Hubs/
└── BlueprintHubConnection.cs                           # EXTENDED — OnPresentationOutcomeReady event hook

src/Common/Sorcha.PresentationLifecycle.Abstractions/
└── IPresentationConsumer.cs                            # EXTENDED — new optional BuildInitiationAsync method (default throws)

src/Services/Sorcha.Blueprint.Service/Services/Implementation/
├── HaipPresentationConsumer.cs                         # EXISTING (F111)
└── SorchaWalletPresentationConsumer.cs                 # NEW — wraps Sorcha.Verifier.Engine; overrides BuildInitiationAsync

src/Services/Sorcha.Blueprint.Service/Services/Implementation/
└── PresentationLifecycleService.cs                     # EXTENDED (small) — dispatches BuildInitiationAsync for non-HAIP consumers; publishes PresentationOutcomeReady on success; issues ClaimsFetchToken on initiate

src/Services/Sorcha.Blueprint.Service/Endpoints/
└── PresentationEndpoints.cs                            # EXTENDED — new GET /api/presentations/{id}/disclosed-claims?token=...

src/Services/Sorcha.Blueprint.Service/Hubs/
├── BlueprintHubGroups.cs                               # EXTENDED — PresentationNonce(presentationRequestId) builder
└── IBlueprintHubClient.cs                              # EXTENDED — PresentationOutcomeReady(presentationRequestId) typed-client method

src/Services/Sorcha.Blueprint.Service/Storage/Presentations/
└── IClaimsFetchTokenStore.cs                           # NEW — minimal Redis-backed store: SET NX at mint, GetAndRemoveAsync at fetch (NonceStore pattern)

# Consumer side (in samples/) — application-specific
samples/                                                # NEW top-level folder
└── strathcarron-portal/                                # NEW sample artifact
    ├── Sorcha.Sample.StrathcarronPortal.csproj         # Blazor WASM host
    ├── Dockerfile
    ├── Program.cs
    ├── App.razor
    ├── Layout/
    │   ├── CouncilLayout.razor                         # plausible council chrome
    │   ├── CouncilHeader.razor                         # logotype, primary nav
    │   └── CouncilFooter.razor                         # address, accessibility links
    ├── Pages/
    │   ├── Index.razor                                 # "Strathcarron Council — services"
    │   ├── DrivingLicence.razor                        # MOVED from src/Apps/Sorcha.UI/Sorcha.UI.Web.Client/Pages/
    │   └── BlueBadge.razor                             # NEW — composes EnrolGateComponent → CredentialGateComponent → form
    ├── Components/
    │   ├── BlueBadgeForm.razor                         # form fields specific to Blue Badge
    │   └── DrivingLicenceForm.razor                    # extracted from the moved page
    ├── wwwroot/
    │   ├── css/council.css                             # distinct council styling (not MudBlazor defaults)
    │   └── images/strathcarron-logotype.svg
    └── README.md

walkthroughs/Strathcarron/
├── setup-cold-start-demo.ps1                           # existing — Spec 3 cold-start
└── setup-blue-badge-demo.ps1                           # NEW — chains off Spec 3 state.json, seeds the Blue Badge blueprint

walkthroughs/Strathcarron/blueprints/
├── strathcarron-driving-licence.json                   # existing
└── strathcarron-blue-badge.json                        # NEW — declares prerequisites.presentationRequests against AssuredIdentityCredential

# CI surface
scripts/
└── check-samples-references.ps1                        # NEW — grep gate over samples/**/*.csproj

# Tests
src/Apps/Sorcha.UI/Sorcha.UI.Components.User.Tests/
└── Components/CredentialGate/
    ├── CredentialGateComponentTests.cs                 # bunit
    └── PresentationSignalTests.cs                      # timer-bound; uses TimeProvider.CreateTimer

src/Services/Sorcha.Blueprint.Service.Tests/
├── Endpoints/
│   └── PresentationEndpointsTests.cs                   # WebApplicationFactory
├── Services/
│   └── PresentationRequestServiceTests.cs
└── BlueprintRuntime/
    └── PrerequisitesResolverTests.cs

tests/Sorcha.E2E/
└── Demo/
    └── BlueBadgeSecondServiceDemo.cs                   # Playwright [Demo("blue-badge-second-service")]
```

**Structure Decision**:

- **Platform-side new code** lives where its existing siblings live — `CredentialGateComponent` next to `EnrolGateComponent` in `Sorcha.UI.Components.User/Components/`; presentation endpoints next to other blueprint endpoints in `Sorcha.Blueprint.Service/Endpoints/`. No new projects on the platform side.
- **Consumer-side code** lives in a brand-new top-level `samples/` folder, in a brand-new Blazor WASM project (`Sorcha.Sample.StrathcarronPortal.csproj`). The sample consumes `Sorcha.UI.Components.User` as a `ProjectReference` (treated as if it were a NuGet — CI grep gate enforces that NO other `src/Apps/Sorcha.UI/` reference is permitted).
- **Sub-PR sequence**:
  - **PR-A** (structural extract): creates `samples/strathcarron-portal/`, moves the F126 driving-licence page, sets up Dockerfile / docker-compose / CI grep gate / baseline chrome. The F126 walkthrough must still run end-to-end after PR-A.
  - **PR-B** (platform contract + library): `prerequisites.presentationRequests` syntax, `PresentationRequestService`, three endpoints, `BlueprintHub.PresentationReceived` event, `CredentialGateComponent` + `IPresentationSignal`. Server-side `Sorcha.Verifier.Engine` integration.
  - **PR-C** (Blue Badge content): `strathcarron-blue-badge.json` blueprint, `BlueBadge.razor` page in the sample, `BlueBadgeCredential` issuance, walkthrough seed script.
  - **PR-D** (Playwright + polish): `[Demo("blue-badge-second-service")]` E2E, structured logging audit, doc propagation (skill files, API docs, MASTER-TASKS).

## Phase 0 — Outline & Research

The locked design doc and the locked boundary doc already resolve every load-bearing question Spec 4 raises (Q1–Q6 from the design brainstorm and the boundary's four-tile rule decision). Phase 0 captures the outcomes for future readers and surfaces the few remaining tactical decisions that still need a research pass.

**See `research.md` for the consolidated findings.** Headline decisions:

1. **Consent surface = all-or-nothing in v1** (design Q1, locked). No per-claim toggles. Per-claim disclosure deferred until a real verifier-isn't-issuer use case demands it.
2. **Picker hides when 1 match, force-selects when ≥2** (design Q2, locked). ConsentSheet always renders. F125 picker component already handles the multi-match case.
3. **SignalR primary + 3 s polling fallback + 60 s manual recovery** (design Q3, locked). `IPresentationSignal` is a one-line variant of F126's `IEnrolPairingSignal`. Reuses the existing `BlueprintHub` connection.
4. **PWA-side confirmation dialog before signing** (design Q4, locked). Same trust model as F126's `EnrolmentRedeemConfirmDialog`. Server-set cookie binding deferred to Spec 5.
5. **Walkthrough chains off Spec 3 `state.json`** (design Q5, locked). Phase-2-on-phase-1 pattern.
6. **Linear gate composition: EnrolGate wraps CredentialGate wraps the form** (design Q6, locked).
7. **Samples folder build topology** (boundary doc §3, locked). Own container image, ProjectReference into `src/Apps/Sorcha.UI/` only allowed to `Sorcha.UI.Components.User`, CI grep gate enforces.
8. **No n1 in Spec 4** (boundary doc §6, locked). Local docker-compose only; operator-owned domain / services work follows.

Tactical research items still open and resolved in `research.md`:

- **Where does the sample sit in docker-compose?** Port, hostname, dependency edges, traefik / YARP wiring.
- **OID4VP request-URI shape** for the presentation request — exact field set, exact nonce derivation, exact expiry default.
- **Council chrome visual baseline** — minimal viable IA that reads as "council" without overshooting Spec 4's scope.
- **Sample's auth model for calling Sorcha** — does PR-A's structural extract preserve F126's existing auth flow (gateway-fronted, no new identity model), or does this spec introduce the third-party-integrator auth path the boundary doc flags as a future requirement?
- **Existing tests that touch the moved page** — F126 walkthrough body, Playwright nav tests (`navigation-coverage.spec.ts`). Identified and updated as part of PR-A.

**Output of Phase 0**: `research.md` (this command writes it).

## Phase 1 — Design & Contracts

**Prerequisites**: `research.md` complete.

### Data model

`data-model.md` captures the new domain entities:

- **CredentialGate** — declared on `BlueprintAction.Prerequisites.PresentationRequests`. Fields: `id`, `credentialType`, `issuerAllowlist[]`, `requiredClaims[]`.
- **PresentationRequest** — short-lived, stashed in `IAtomicDistributedCache`. Fields: `nonce`, `requestUri`, `qrUrl`, `tapUrl`, `gate` (CredentialGate reference), `expiresAt`. TTL 5 minutes (matches F126 enrol session).
- **PresentationResponse** — produced by the wallet. Fields: `nonce`, `signedVp` (compact JWS), `disclosedClaims` (after server-side validation).
- **DisclosedClaims** — the validated claims surfaced to the council page after verification. Map of claim name → value.
- **BlueBadgeCredential** — issued credential type. Fields: `givenName`, `familyName`, `dateOfBirth`, `homeAddress`, `mobilityCondition`, `previousBadgeNumber?`, `issuedAt`, `expiresAt`. Issuer: `did:sorcha:org:strathcarron-council`. Target audience: `SorchaLocalWallet`.

### API contracts

`contracts/` captures:

- **`presentation-requests-endpoint.md`** — `POST /api/blueprint/presentation-requests`. Request body: `{ blueprintId, startingActionId }`. Response: `{ requestUri, nonce, qrUrl, tapUrl, expiresAt }`. Owned by `Sorcha.Blueprint.Service`. Rate-limited via the standard `RateLimitPolicies.Api` policy. OpenAPI documented via `.WithSummary()` + `.WithDescription()`.
- **`presentation-responses-endpoint.md`** — `POST /api/blueprint/presentation-responses` and `GET /api/blueprint/presentation-responses/{nonce}`. POST validates the VP against the original request, stashes claims keyed by nonce, fires SignalR event. GET fetches stashed claims for the council page polling fallback.
- **`prerequisites-presentation-requests.schema.json`** — JSON schema for the `prerequisites.presentationRequests` block on a blueprint starting action. Validated at blueprint publish.
- **`presentation-received.signalr.md`** — typed-client method on `BlueprintHub`: `PresentationReceived(string nonce)`. Thin-signal contract — no domain payload (per Feature 118).

### Quickstart

`quickstart.md` walks an operator through: start docker-compose with the new `strathcarron-portal` service, run `setup-cold-start-demo.ps1`, run `setup-blue-badge-demo.ps1` (chains off Spec 3's `state.json`), browse to the council portal, walk the returning-Tier-1 journey end-to-end. Aligns with Spec 3's quickstart shape so operators recognise the rhythm.

### Agent context update

Runs `.specify/scripts/powershell/update-agent-context.ps1 -AgentType claude` to refresh the agent context file with any new technology (none expected — Spec 4 reuses every dependency already in the stack).

**Output of Phase 1**: `data-model.md`, `contracts/*` (4 files), `quickstart.md`, agent context file updated.

## Constitution Check — Post-Design

Re-evaluated after Phase 0 + Phase 1:

| Principle | Compliance | Notes |
|---|---|---|
| **I. Microservices-First** | ✅ | Endpoints added to existing service; no new services. Sample is a consumer, not a platform service. |
| **II. Security First** | ✅ | All endpoints validate input; presentation signing verified server-side; nonce-stash uses `IAtomicDistributedCache` with TTL; revoked credentials path tested; rate-limiting via standard policy. |
| **III. API Documentation** | ✅ | All new endpoints documented via .NET 10 OpenAPI + Scalar; contracts/ folder pins the surface in Markdown for design review. |
| **IV. Testing Requirements** | ✅ | Unit (services), integration (endpoints via WebApplicationFactory), bunit (component), Playwright (E2E demo). Coverage target ≥ 85% on new code. |
| **V. Code Quality** | ✅ | No Release-build warnings; nullable refs enabled; DI throughout. |
| **VI. Blueprint Standards** | ✅ | New blueprint authored as JSON; `prerequisites.presentationRequests` is declarative; no fluent-API authoring on the new pattern. |
| **VII. Domain-Driven Design** | ✅ | New term **credential gate** documented in `data-model.md` and in the design doc's vocabulary section. |
| **VIII. Observability** | ✅ | OTel histogram on `IPresentationSignal` latency (SC-004 verification); structured logs on validation pipeline; existing hub instrumentation covers SignalR. |

**Result**: No new violations introduced by the Phase 1 design. Ready for `/speckit.tasks`.

## Complexity Tracking

No constitutional violations. No entries.
