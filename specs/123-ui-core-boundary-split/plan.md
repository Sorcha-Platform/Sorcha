# Implementation Plan: UI.Core User/Admin Type-Level Boundary Refactor

**Branch**: `123-ui-core-boundary-split` | **Date**: 2026-05-12 | **Spec**: [spec.md](./spec.md)
**Input**: Feature specification from `/specs/123-ui-core-boundary-split/spec.md`

## Summary

Refactor `Sorcha.UI.Core` so that its service-interface surface and its model folders telegraph the audience (user-facing vs. admin) at the *type* level. Split bi-modal interfaces (`IRegisterService` is the canonical case — 2 user-facing methods and 7 admin/governance methods on one interface). Classify each `Models/*` folder per audience and re-organise mixed folders. Extract shared DTOs out of admin-service files into a neutral location. Update every consumer to inject the narrower interface that matches its actual usage. Adopt one consistent **audience-tag convention** (locked in Phase 0 — see research.md) and document it for future contributors.

Feature 123 produces no new application functionality and no new external surface. It is a pure refactor whose only outcome is that `Sorcha.UI.Core` is *internally* re-partitioned so Feature 122's eventual component-library extraction becomes a mechanical file move.

The refactor is motivated by the Feature 122 Phase 2 forensic discovery captured in `specs/122-shared-user-components/phase-2-discovery.md`. The methodology lesson from that discovery — that `@inject`-grep is necessary but insufficient for migration scoping; method return types and parameter types are equally load-bearing — is encoded in this feature's FR-008 and applied in Phase 0 research.

## Technical Context

**Language/Version**: C# 14 on .NET 10 (per Sorcha constitution v1.1.0).
**Primary Dependencies**: Existing `Sorcha.UI.Core` dependency set unchanged. No new packages introduced.
**Storage**: N/A (no persistence change).
**Testing**: xUnit + FluentAssertions + Moq for any new unit tests. `tests/Sorcha.UI.Core.Tests` retains its current contents; existing tests pass unchanged except where they exercise a now-split interface, in which case they switch to the narrower interface but keep the same assertions.
**Target Platform**: Browser (Blazor WebAssembly), unchanged.
**Project Type**: Web — single Razor class library refactor. No new projects.
**Performance Goals**: Zero runtime impact. The refactor changes C# interface partitioning, not application behaviour.
**Constraints**: No REST endpoint, gRPC contract, or wire format changes (FR-009). No host-app csproj changes required. No visible UX change.
**Scale/Scope**: 28 top-level service interfaces in `Sorcha.UI.Core/Services/`, 14 service subfolders, 20 model subfolders. Initial estimate from quick inspection: `IRegisterService` confirmed bi-modal; `IOrganizationAdminService` confirmed for DTO extraction; `IWalletApiService` requires Phase 0 audit. Initial estimate: 2-4 model folders mixed-audience (`Models/Registers/` confirmed; `Models/Blueprints/` partial; others TBD by Phase 0).

## Constitution Check

*GATE: Must pass before Phase 0 research. Re-check after Phase 1 design.*

The Sorcha constitution targets microservices and service-shaped concerns. Several principles do not apply to a pure interface-partition refactor. Applicable gates:

- **I. Microservices-First** — N/A (no service introduced; no service decomposed; no dependency direction changes).
- **II. Security First** — Applies weakly: no new external boundary, no new validation surface. Existing input validation paths are preserved through the interface split (a method moves from interface A to interface B but its body is unchanged).
- **III. API Documentation** — Applies: XML doc comments on every new narrower interface, recording the audience of each method. The chosen audience-tag convention must be documented at a single discoverable location (FR-010, SC-005).
- **IV. Testing Requirements** — Applies: existing test coverage must not regress; tests exercising bi-modal interfaces must be split or rewritten to keep the same assertions (FR-007, SC-004). New tests are not required by this refactor; existing tests are sufficient to demonstrate behavioural preservation.
- **V. Code Quality** — Applies: nullable reference types preserved, no new Release-build warnings, async/await patterns preserved across the interface split, DI registrations updated coherently.
- **VI. Blueprint Standards** — N/A.
- **VII. Domain-Driven Design** — Applies: the new narrower interfaces preserve the existing ubiquitous-language nouns (Register, Participant, etc.). The audience qualifier (Read / Governance / Admin / whatever Phase 0 locks) is added as a suffix or prefix that does not displace the domain noun.
- **VIII. Observability** — Applies weakly: any service implementation that emits telemetry continues to do so through the same instrumented method body, regardless of which narrower interface the method now lives on. No telemetry surface changes.

**Gate verdict**: PASS — no constitution violations. The refactor preserves behaviour, validation, telemetry, naming, and dependency direction.

## Project Structure

### Documentation (this feature)

```text
specs/123-ui-core-boundary-split/
├── plan.md                    # this file
├── spec.md                    # feature specification
├── research.md                # Phase 0 — full UI.Core audit; audience-tag convention lock; per-interface and per-folder verdicts
├── data-model.md              # Phase 1 — interface mapping (old → new); folder mapping; DTO extraction targets
├── quickstart.md              # Phase 1 — how a contributor picks the right interface / folder for new work after this feature
├── contracts/                 # Phase 1 — per-bi-modal-interface split contract docs
│   ├── README.md              # contract pattern overview
│   ├── register-service-split.md            # IRegisterService → IRegisterReadService + IRegisterGovernanceService
│   ├── organization-admin-dto-extraction.md # OrganizationDto/BrandingDto/UserDto extraction pattern
│   ├── wallet-api-service-audit.md          # verdict + split (if any) for IWalletApiService
│   ├── register-subscription-audit.md       # verdict for IRegisterSubscriptionService
│   └── shared-dto-extraction-pattern.md     # general pattern formalising SchemaOverlayFieldInfo / OrganizationDto extractions
├── checklists/
│   └── requirements.md        # specification quality checklist (already exists, all pass)
└── tasks.md                   # Phase 2 — (created by /speckit.tasks, not here)
```

### Source Code (repository root)

Only `Sorcha.UI.Core` is touched. No new projects. No host-app csproj changes. No `Common/` changes.

```text
src/Apps/Sorcha.UI/Sorcha.UI.Core/
├── Services/
│   ├── IRegisterService.cs                    # SPLIT — either kept as a marker interface deriving from both
│   │                                          # narrower interfaces (back-compat) or deleted, per research.md
│   ├── IRegisterReadService.cs                # NEW — user-facing read methods
│   ├── IRegisterGovernanceService.cs          # NEW — admin/governance methods
│   ├── RegisterService.cs                     # CHANGED — implements both narrower interfaces (single class)
│   ├── IOrganizationAdminService.cs           # CHANGED — DTOs extracted; interface keeps only admin operations
│   ├── IWalletApiService.cs                   # POSSIBLY SPLIT — verdict from Phase 0
│   ├── IRegisterSubscriptionService.cs        # POSSIBLY SPLIT — verdict from Phase 0
│   ├── …                                      # other interfaces stay or split per Phase 0 verdicts
│   ├── Organization/                          # NEW SUBFOLDER (or location locked in Phase 0) — shared DTOs
│   │   ├── OrganizationDto.cs                 # EXTRACTED from IOrganizationAdminService.cs
│   │   ├── BrandingDto.cs                     # EXTRACTED
│   │   └── UserDto.cs                         # EXTRACTED if shared
│   ├── (other audience-classified subfolders if Phase 0 locks folder-split as the convention)
│   └── …
├── Models/
│   ├── Registers/                             # SPLIT — Phase 0 verdict drives whether folder is split,
│   │   │                                      # renamed, or files are tagged in-place
│   │   ├── (user-facing types — TransactionViewModel, RegisterViewModel, WalletViewModel, PayloadViewModel,
│   │   │   TransactionListResponse, TransactionGraphNode, TransactionQueryState, RegisterFilterState,
│   │   │   ConnectionState, NavigationContext)
│   │   └── Governance/                        # NEW SUBFOLDER (if folder-split is the chosen convention)
│   │       ├── RegisterPolicyViewModel.cs
│   │       ├── RegisterPolicyFields.cs
│   │       ├── PolicyUpdateProposalViewModel.cs
│   │       ├── PolicyHistoryViewModel.cs
│   │       └── RegisterCreationState.cs
│   ├── Blueprints/                            # CLASSIFIED — governance types either move out, or are tagged
│   │   ├── GovernanceRosterViewModel.cs       # MOVED or TAGGED
│   │   └── (designer-flavoured schema/canvas types stay)
│   └── …                                      # other Models folders audited; verdict per folder in research.md
└── Sorcha.UI.Core.csproj                      # UNCHANGED
```

```text
src/Apps/Sorcha.UI/Sorcha.UI.{Admin,App,Designer,Explorer,Web,Web.Client}/
└── Pages/, Components/                        # CHANGED — every @inject of a bi-modal interface updated to
                                               # inject the narrower interface that matches the page's actual usage
```

```text
tests/Sorcha.UI.Core.Tests/
└── …                                          # CHANGED — tests injecting old bi-modal interfaces updated to
                                               # inject the narrower interfaces; assertions unchanged
```

**Structure Decision**: Pure in-place refactor of `Sorcha.UI.Core`. No new projects. No top-level structural change. Within `Services/` and `Models/`, the chosen audience-tag convention (locked in Phase 0) drives whether new subfolders appear, files are renamed, or files are tagged in-place via comment/attribute. Phase 0 research records the decision and applies it consistently.

## Complexity Tracking

> **Fill ONLY if Constitution Check has violations that must be justified**

No constitution violations. Section intentionally empty.
