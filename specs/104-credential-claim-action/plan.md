# Implementation Plan: Credential Claim Action (Feature 103 Wave 14)

**Branch**: `104-credential-claim-action` | **Date**: 2026-04-14 | **Spec**: [spec.md](./spec.md)
**Input**: Feature specification from `specs/104-credential-claim-action/spec.md`
**Design reference**: [`docs/superpowers/specs/2026-04-14-wave-14-credential-claim-action-design.md`](../../docs/superpowers/specs/2026-04-14-wave-14-credential-claim-action-design.md)

## Summary

Deliver HAIP credential offers minted during blueprint execution to the recipient citizen (not the issuing assessor) by introducing a new blueprint engine primitive — `Route.OutputMapping` and `Instance.PendingActionPayloads` — that carries data forward from one action's execution result into the next action's prepopulated payload, then using that primitive to seed a new credential claim action whose renderer is a dedicated `CredentialClaimCard` wired to wave 13's existing `HaipLocalReceiveService` for client-side claim, with a QR fallback for external wallets, retry-friendly failure semantics, and automatic Decline/Expire paths.

The feature is split across two PRs: **wave 14a** lands the engine primitive as a general-purpose payload carry-forward mechanism with full test coverage and zero user-visible changes, and **wave 14b** consumes it via an `x-credential-offer` schema extension and the `CredentialClaimCard` component, updating the Verified Citizen v2 and HAIP Driving Licence blueprints to use the new three-action shape. The payload shape aligns with OpenID4VCI for the protocol-level offer and DIF Credential Manifest for the display descriptor, so the seeded payload remains interoperable with non-Sorcha tooling.

## Technical Context

**Language/Version**: C# 13 on .NET 10 (runtime target for all services, libraries, and tests)
**Primary Dependencies**: .NET Aspire 13, Minimal APIs, JsonSchema.Net 7.4.0, FluentValidation 11.10.0, Scalar.AspNetCore, Blazor WebAssembly (UI), MudBlazor, Sodium.Core (existing, reused from wave 13), NBitcoin (existing, unchanged)
**Storage**: MongoDB via `EfCoreInstanceStore` for blueprint instance state (adds `Instance.PendingActionPayloads` as plaintext JSON alongside `AccumulatedData` — see research decision 1)
**Testing**: xUnit v3.2.2 primary framework, FluentAssertions 8.8.0, Moq 4.20.72 for unit and integration tests; Playwright .NET for end-to-end UI tests; existing HaipVerifiedCitizen and HaipDrivingLicence walkthrough scripts updated to exercise the new flow
**Target Platform**: Cross-platform .NET 10 services running in Docker Compose on Linux containers, Blazor WebAssembly client in evergreen browsers, remote target `n1.sorcha.dev` for n1 validation
**Project Type**: Web (Blazor WASM client + multiple .NET services behind a YARP API Gateway). Wave 14 touches Blueprint.Service, Blueprint.Engine, Blueprint.Models, Sorcha.UI.Core, Sorcha.UI.Web.Client, and the two credential-issuing blueprint templates.
**Performance Goals**: Citizen completes the claim flow end-to-end in under 60 seconds on a stable connection (SC-003). Engine routing evaluation of `OutputMapping` adds negligible overhead — per-route dictionary lookup plus JSON Pointer resolution, bounded by mapping entry count (typically <10).
**Constraints**: Purely additive to the existing blueprint engine — existing blueprints without `OutputMapping` must execute identically with zero observable change (SC-009 / FR-009). Plaintext-at-rest for `PendingActionPayloads` accepted for v1 consistency with `AccumulatedData`. No new hosted background services. No breaking schema migrations.
**Scale/Scope**: Feature affects Verified Citizen v2 and HAIP Driving Licence blueprints (two issuers today); design is general enough to apply to any future credential-issuing blueprint. Engine primitive is consumable by any blueprint that declares `OutputMapping` on a route.

## Constitution Check

Evaluating against `.specify/memory/constitution.md` v1.1.0:

| Principle | Status | Notes |
|-----------|--------|-------|
| I. Microservices-First Architecture | PASS | No new service added. Changes contained within Blueprint.Service, Blueprint.Engine, Blueprint.Models, and UI. No upward dependencies introduced. The engine primitive lives in `Sorcha.Blueprint.Engine` (Core layer); `Sorcha.Blueprint.Service` (Application layer) consumes it. |
| II. Security First | PASS | FR-019 enforces that the recipient wallet is sourced from the authenticated session, not from payload data — prevents blueprint-author redirection attacks. FR-020 guarantees the credential is bound to the recipient's key (cryptographic `cnf` claim via HAIP proof-of-possession). Pre-authorized codes are short-lived bearer tokens; plaintext-at-rest storage of `PendingActionPayloads` is consistent with existing `AccumulatedData` handling and is documented explicitly in research decision 1. No new secrets, no new network boundaries. |
| III. API Documentation | PASS | One new endpoint (`POST /api/blueprint/instances/{instanceId}/actions/{actionId}/claim-expired`) fully specified in `contracts/claim-expired.yaml` (OpenAPI 3.1), with XML `///` comments on all new public members. Existing endpoints' modified behaviour documented in `contracts/README.md`. Scalar UI at `/openapi/v1.json` will pick up the new endpoint automatically via Minimal APIs metadata. |
| IV. Testing Requirements | PASS (with required coverage) | Wave 14a ships engine unit tests (`RoutingEngine` `OutputMapping` evaluation, merge-with-seed semantics, edge cases) + integration tests (two-action carry-forward blueprint end-to-end). Wave 14b ships renderer unit tests, `CredentialClaimCard` unit tests, and Playwright E2E tests for the P1/P2/P3 user stories. Coverage target >85% on all new code per constitution. Arrange-Act-Assert pattern enforced. |
| V. Code Quality | PASS | C# 13 on .NET 10. Nullable reference types enabled project-wide (existing). `async`/`await` used for all I/O (HAIP calls, wallet signing, instance store writes). DI via existing `ServiceCollectionExtensions`. No new compiler warnings. License headers required on all new files. |
| VI. Blueprint Creation Standards | PASS | Blueprint updates (Verified Citizen v2 v3, HAIP Driving Licence v2) ship as JSON files in `examples/templates/`. No runtime blueprint generation. Output mapping is declared declaratively in JSON (`route.outputMapping`), not via Fluent API. |
| VII. Domain-Driven Design | PASS | Uses existing vocabulary: Blueprint, Action, Participant, Route, Instance. New term "Prepopulated Action Payload" is introduced in the data model; it fits the existing ubiquitous language (it is a variant of action payload, tied to pending state). `Instance.State` transitions reuse existing `Rejected` and `Failed` values — no new states. |
| VIII. Observability by Default | PASS | Engine `OutputMapping` evaluation adds a new OpenTelemetry span (`blueprint.routing.output_mapping.evaluate`) tagged with route ID and next action IDs. `CredentialClaimCard` emits logs on claim attempts, successes, and failures via existing `ILogger<CredentialClaimCard>`. The claim-expired endpoint emits structured logs including instance ID, action ID, and expiry timestamp. No string-interpolated log messages. Health checks unchanged. |

**Verdict:** All eight principles PASS. No violations. No complexity tracking entries required.

**Re-evaluation after Phase 1:** The Phase 1 artifacts (data model, contracts, quickstart) do not introduce any new design that would shift the constitution posture. Constitution Check remains PASS.

## Project Structure

### Documentation (this feature)

```text
specs/104-credential-claim-action/
├── spec.md                              # Feature specification (written by /speckit.specify)
├── plan.md                              # This file (written by /speckit.plan)
├── research.md                          # Phase 0 — open question resolutions with code evidence
├── data-model.md                        # Phase 1 — entities, relationships, state transitions
├── quickstart.md                        # Phase 1 — verification walkthrough
├── contracts/
│   ├── README.md                        # Contract summary for new + modified endpoints
│   ├── claim-expired.yaml               # OpenAPI 3.1 for the new claim-expired endpoint
│   └── output-mapping.schema.json       # JSON Schema for Route.OutputMapping shape
└── checklists/
    └── requirements.md                  # Spec quality checklist (all items pass)
```

### Source Code (repository root)

Wave 14 extends the existing Sorcha monorepo. All changes land in-place; no new projects or directories.

**Wave 14a — engine primitive** (PR #1)

```text
src/Common/Sorcha.Blueprint.Models/
└── Route.cs                             # +OutputMapping: Dictionary<string, string>?

src/Core/Sorcha.Blueprint.Engine/
├── Models/
│   └── RoutingResult.cs                 # +PendingPayloads: IReadOnlyDictionary<int, JsonObject>?
└── Routing/
    └── RoutingEngine.cs                 # +evaluate OutputMapping per next action

src/Services/Sorcha.Blueprint.Service/
├── Models/
│   └── Instance.cs                      # +PendingActionPayloads: Dictionary<int, JsonObject>
├── Storage/
│   └── EfCoreInstanceStore.cs           # serialize/deserialize PendingActionPayloads
├── Services/Implementation/
│   └── ActionExecutionService.cs        # seed on route, merge on submit, clear on resolve
└── Endpoints/
    └── PendingActionEndpoints.cs        # expose prepopulatedPayload on pending action views

src/Services/Sorcha.Blueprint.Service/
└── Services/BlueprintValidator.cs       # +VAL_BP_011 (output mapping target paths exist)

tests/Sorcha.Blueprint.Engine.Tests/
└── Routing/
    └── OutputMappingTests.cs            # unit tests: mapping eval, absent source, nested paths

tests/Sorcha.Blueprint.Service.IntegrationTests/
└── OutputMappingCarryForwardTests.cs    # two-action carry-forward end-to-end

tests/Sorcha.Blueprint.Service.Tests/
└── ActionExecutionService/
    └── PrepopulatedPayloadMergeTests.cs # merge semantics unit tests
```

**Wave 14b — credential claim feature** (PR #2)

```text
src/Apps/Sorcha.UI/Sorcha.UI.Web.Client/Components/Credentials/
└── CredentialClaimCard.razor            # NEW — wraps CredentialOfferQrCard with header + Claim + Decline + QR

src/Apps/Sorcha.UI/Sorcha.UI.Core/Components/Forms/
└── SorchaFormRenderer.razor(.cs)        # +x-credential-offer handler (swap in CredentialClaimCard)

src/Apps/Sorcha.UI/Sorcha.UI.Core/Services/Forms/
└── CredentialOfferSchemaResolver.cs     # NEW — parses x-credential-offer extension

src/Services/Sorcha.Blueprint.Service/
├── Endpoints/
│   └── ClaimExpiredEndpoint.cs          # NEW — POST .../claim-expired
└── Services/Implementation/
    └── ActionExecutionService.cs        # +write /haip/* into route source document when HAIP mint output present

src/Services/Sorcha.Blueprint.Service/
└── Services/BlueprintValidator.cs       # +VAL_BP_012 (x-credential-offer on object fields only)
                                         # +WARN_BP_006 (x-credential-offer object should require credential_offer_uri)

examples/templates/
├── verified-citizen-v2.json             # MODIFIED — add action 2 + OutputMapping on action 1 route
└── haip-driving-licence.json            # MODIFIED — same shape

walkthroughs/HaipVerifiedCitizen/
└── Program.cs                           # MODIFIED — drive action 2, assert credential lands in citizen wallet

walkthroughs/HaipDrivingLicence/
└── Program.cs                           # MODIFIED — same

tests/Sorcha.UI.Core.Tests/Components/Forms/
└── CredentialOfferSchemaResolverTests.cs # NEW — x-credential-offer detection unit tests

tests/Sorcha.UI.Core.Tests/Components/Credentials/
└── CredentialClaimCardTests.cs          # NEW — card rendering, Claim/Decline/Expired states

tests/Sorcha.UI.E2E.Tests/Docker/
└── CredentialClaimTests.cs              # NEW — Playwright: full P1 story end-to-end

tests/Sorcha.Blueprint.Service.IntegrationTests/
└── ClaimExpiredEndpointTests.cs         # NEW — authz, expiry check, terminal state check
```

**Structure Decision**: The Sorcha monorepo uses a Common/Core/Services/Apps layering already. Wave 14's engine primitive lives in `Sorcha.Blueprint.Models` (contract) and `Sorcha.Blueprint.Engine` (behaviour) so it is WASM-compatible and reusable. Persistence lives in `Sorcha.Blueprint.Service.Storage`. UI lives in `Sorcha.UI.Core` (shared renderer logic) and `Sorcha.UI.Web.Client` (Blazor component). No new projects are created; everything fits the existing structure. The 14a / 14b PR split keeps engine changes and UI changes in separate review conversations.

## Complexity Tracking

No constitution violations. No complexity tracking entries needed.

## Phase 0 — Research

Complete. See [`research.md`](./research.md).

Four open planning questions from the design doc resolved with concrete code evidence:
1. **Encryption:** `PendingActionPayloads` stored plaintext alongside `AccumulatedData` for v1 consistency.
2. **Decline semantics:** Reuse existing `RejectionConfig.IsTerminal = true` pattern.
3. **Expiry mechanism:** Client-side transition via new `claim-expired` endpoint; no background sweep.
4. **Validation with `x-credential-offer`:** Existing merge-before-validate ordering satisfies validation; no new validation code.

All `NEEDS CLARIFICATION` markers resolved. Phase 1 proceeded.

## Phase 1 — Design & Contracts

Complete. Artifacts:

- [`data-model.md`](./data-model.md) — entity definitions for `OutputMapping`, `PendingActionPayload`, `RoutingResult.PendingPayloads`, `x-credential-offer` extension, `CredentialOfferPayload` runtime shape, and state transitions. All additive; no breaking changes.
- [`contracts/README.md`](./contracts/README.md) — summary of the one new endpoint, one modified endpoint, and one modified response shape. Full OpenAPI 3.1 in [`contracts/claim-expired.yaml`](./contracts/claim-expired.yaml). JSON Schema for `Route.OutputMapping` in [`contracts/output-mapping.schema.json`](./contracts/output-mapping.schema.json).
- [`quickstart.md`](./quickstart.md) — step-by-step verification flow including the isolation test for wave 14a's engine primitive, the full P1/P2/P3 credential claim flows, and rollback + observability checks.

## Phase 2 — Planning notes (for `/speckit.tasks`)

The task breakdown in the next phase should follow the two-PR split established here:

**Wave 14a tasks** will derive from user story 5 (blueprint author uses the engine primitive) plus the foundational phases. The primitive is general-purpose and deserves its own test-first implementation: engine unit tests first, integration test, then service-layer wiring. Acceptance is the two-action smoke blueprint from the quickstart exercising `OutputMapping` end-to-end.

**Wave 14b tasks** will derive from user stories 1 (P1, claim path), 2 (P2, external wallet), 3 (P2, retry), and 4 (P3, expiry). The critical path is P1: schema extension handler → `CredentialClaimCard` component → blueprint update → Playwright test. P2 and P3 are layered on top of the P1 foundation. Each user story should emerge as an independently-testable slice.

Task generation should respect:
- Engine primitive (wave 14a) is a hard prerequisite for the credential claim feature (wave 14b). Tasks must be sequenced so that 14a's engine and storage changes land first, followed by 14b's UI and blueprint work.
- `CredentialClaimCard` depends on wave 13's `HaipLocalReceiveService` and `CredentialOfferQrCard`, both already on master — no prerequisite work.
- Each user story phase should produce its own set of tests before the implementation work for that story, following TDD per the constitution (Testing Requirements).
- The walkthrough updates (`HaipVerifiedCitizen`, `HaipDrivingLicence`) are shared polish that belong in the final phase.

## Phase 3 — NOT created by this command

Task list will be generated by `/speckit.tasks`. Implementation is executed by `/speckit.implement`.

## Verification against the spec

| Spec element | Addressed by |
|--------------|--------------|
| FR-001 – FR-009 (engine primitive) | Data model `OutputMapping` + `PendingActionPayload`, contracts README engine section, research decision 4 |
| FR-010 – FR-022 (credential claim feature) | Data model `x-credential-offer` + `CredentialOfferPayload`, contracts README and quickstart section 2 |
| FR-023 – FR-025 (audit and integrity) | Decline and expiry paths both go through normal action-execution transaction sealing (research decisions 2 and 3) |
| SC-001 (credential in recipient wallet, not sender) | FR-019 + FR-020 + late-binding + `HaipLocalReceiveService` reading wallet address from authenticated session |
| SC-002 (95% retry success) | Retry-friendly failure semantics (research decision — no state change on transient error, action stays pending) |
| SC-003 (<60s claim flow) | Reuses wave 13 flow with no additional latency beyond the routing evaluation |
| SC-004 (ordered audit trail) | Three actions seal normally via existing transaction chain |
| SC-009 (no regression) | FR-009 + `OutputMapping` null check ensures pre-14 blueprints execute identically |

All spec elements mapped to plan artifacts.

---

**Plan complete.** Ready for `/speckit.tasks` to generate the task breakdown.
