# Phase 2 Discovery — Migration Blocked on Pre-Migration Refactor

**Date:** 2026-05-11
**Status:** Phase 1 complete and merged-to-branch; Phase 2 attempted, rolled back; feature dependency added on Feature 123 (pre-migration refactor).
**Outcome:** Roll-back complete at commit `654744f9`. Working tree clean. Plan revised — Phases 2-7 are blocked until Feature 123 lands a cleaner separation between user-facing and admin-facing types in `Sorcha.UI.Core`.

## What happened

Phase 1 (scaffolding the empty `Sorcha.UI.Components.User` project) executed cleanly. Phase 2 (the atomic file move) was attempted and revealed that the user-facing / admin-facing boundary the original research drew at the *component* level does not hold at the *type* level.

The attempt moved the planned set (Forms, Credentials, Wallet, Participants, the Shared subset, the planned service folders, the planned model folders). The build of the new library standalone failed in waves, dropping from 83 errors → 37 → 22 → 24 as each fix surfaced the next layer of coupling.

The terminal coupling shape — the one that triggered the roll-back — was:

- User-facing components (`PublishParticipantDialog`, `TransactionDetailDrawer`) depend on `IRegisterService`.
- `IRegisterService` is a single interface that mixes **read-side methods** (`ListRegistersAsync`, `GetTransactionsAsync`) and **governance methods** (`GetGovernanceRosterAsync`, `GetPolicyAsync`, `ProposePolicyUpdateAsync`).
- The governance methods return types from `Models/Blueprints/` (e.g., `GovernanceRosterViewModel`, `PolicyUpdateProposalViewModel`).
- `Models/Blueprints/` is admin/designer-flavoured and the plan correctly held that it should stay in UI.Core.
- But the .NET type system requires the whole interface to come along when the interface comes along — including return types — so moving `IRegisterService` into the user library transitively requires moving `Models/Blueprints/` and `Models/Admin/` into the user library too. Bundle hygiene collapses.

The same shape exists less acutely with `IOrganizationAdminService` (admin operations and the `OrganizationDto`/`BrandingDto` DTOs co-located in the same file).

## Why the original research missed this

Phase 0 research (`research.md` § R2) inventoried the host-coupling of components by grep-ing `@inject` directives across migration-candidate Razor files. That grep returned eight clean injection points and concluded "the coupling is much shallower than feared."

It was. **For injection.** The grep did not inspect:

1. Return types of injected service methods.
2. Parameter types declared on `[Parameter]` properties.
3. Transitive types reached through any of the above.

Those are the channels through which `Models/Blueprints/` reaches into user-facing components. The grep-based method was rigorous within its own scope but its scope was too narrow.

## The architectural finding

`Sorcha.UI.Core` was never cleanly separable along the user/admin axis at the type level. The clean split exists at the *component* level — admin pages are visibly separate from user pages — but several service interfaces and model folders straddle the boundary by carrying both audiences' shapes in the same type. Specifically:

- `IRegisterService` is bi-modal (user-read + admin-governance).
- `IOrganizationAdminService.cs` co-locates admin operations with shared DTOs (`OrganizationDto`, `BrandingDto`).
- `IWalletApiService` is likely bi-modal too (not fully audited at roll-back time; consumed by `FormSigningService` which moved).
- `Models/Blueprints/` contains both schema-design types (admin) and governance-result types (referenced by user-facing register-display components).
- `Models/Registers/` contains both user-facing view models (`TransactionViewModel`, `RegisterViewModel`) and admin-flavoured types (`RegisterPolicyViewModel`, `PolicyUpdateProposalViewModel`).

A clean extraction requires fixing these straddling cases first, then the migration is mechanical.

## Decision: Feature 122 depends on Feature 123

A new feature is required, ahead of Feature 122's Phase 2 onwards:

**Feature 123 — UI.Core User/Admin Type-Level Boundary Refactor.** Splits bi-modal service interfaces, classifies mixed model folders per audience, updates consumers, leaves UI.Core in a state where Feature 122's extraction is purely a file-move with no surprises. Detailed spec to be authored separately (recommend a fresh `/speckit.specify` invocation in a clean context).

Feature 122 stays in its current state on the `122-shared-user-components` branch with Phase 1 done. Phases 2-7 remain in `tasks.md` but are explicitly blocked on Feature 123. When Feature 123 merges, Feature 122 resumes by re-running its Phase 0 research with the same grep methodology extended to cover method return types and parameter types; the verdict tables in `research.md` get updated; Phases 2-7 then execute against the cleaner target.

## Concrete first targets for Feature 123 (informative — not normative for that spec)

This is what a Feature 123 plan would likely tackle, recorded here so the next session has a starting point rather than reinventing the analysis:

1. **`IRegisterService` split.** Two new interfaces: `IRegisterReadService` (user-facing — `ListRegistersAsync`, `GetTransactionsAsync`, ...) and `IRegisterGovernanceService` (admin — `GetGovernanceRosterAsync`, `GetPolicyAsync`, `ProposePolicyUpdateAsync`, ...). `IRegisterService` either becomes a marker interface that derives from both, or is deleted with consumers updated to inject the narrower one. Same for `RegisterService` implementation (split into two implementations or one class implementing both interfaces).

2. **`IRegisterSubscriptionService` review.** Less obviously bi-modal but co-resident with `IRegisterService` in the migration plan. Audit and split if needed.

3. **`IOrganizationAdminService` DTO extraction.** Move `OrganizationDto`, `BrandingDto`, `UserDto`, and other shared DTOs out of the admin-service interface file into a dedicated `Models/Organization/` or similar location that can be referenced without inheriting the admin service surface. The admin interface keeps its operation contracts; the DTOs become library-portable.

4. **`Models/Registers/` classification.** Walk the folder file-by-file. `TransactionViewModel`, `RegisterViewModel`, `WalletViewModel`, `PayloadViewModel`, `TransactionListResponse`, `TransactionGraphNode`, `TransactionQueryState`, `RegisterFilterState`, `ConnectionState`, `NavigationContext` are likely user-facing. `RegisterPolicyViewModel`, `RegisterPolicyFields`, `PolicyUpdateProposalViewModel`, `PolicyHistoryViewModel`, `RegisterCreationState` are likely admin/governance. Split into `Models/Registers/` (user) and `Models/Registers.Governance/` (admin), or rename so the audience is encoded in the type name.

5. **`Models/Blueprints/` audit.** `GovernanceRosterViewModel` is the type that broke Phase 2's atomic move. Determine whether it's truly admin-only (likely yes; only `IRegisterService.GetGovernanceRosterAsync` returns it) and either: (a) keep it in `Models/Blueprints/`, accept that user-facing components do not need it after Feature 123 splits `IRegisterService`; or (b) move governance-roster types to a `Models/Governance/` namespace if other admin types live there.

6. **`IWalletApiService` audit.** Inspect the interface for similar bi-modality. `FormSigningService.cs` (user-facing, signs form payloads) consumes it. If admin operations live alongside, split similarly.

7. **`SchemaOverlayFieldInfo` + `BlueprintSchemaService.cs`.** The type is genuinely shared (user-facing components like `JsonTreeView` use it; the admin `BlueprintSchemaService` populates it). Already approachable via the extraction pattern attempted in Phase 2 (extract the small record into its own file in a shared location, the larger service stays in UI.Core). Feature 123 should formalise this pattern for any other shared-by-coincidence types found during the audit.

8. **Consumer update.** Every page in the six Sorcha.UI host apps that injects the old `IRegisterService` gets its injection updated to the narrower interface that actually matches what it uses. Most pages will use `IRegisterReadService`; a handful of admin pages will use `IRegisterGovernanceService`. Pages that use both can inject both.

The Feature 123 plan should also explicitly audit the *parameter types* and *return types* across the user-facing component surface, not just `@inject` directives, to avoid reproducing the same blind spot.

## What's preserved from Phase 2's attempt

The discovery itself, and a clearer mental model of what the migration is actually moving. The two extracted types (`SchemaOverlayFieldInfo` and `OrganizationDto`/`BrandingDto`) that we created in the new library during Phase 2 are not on disk anymore — they were reverted with the rest of Phase 2 — but the pattern of "extract small shared types into the user library, leave large services in UI.Core" is documented above as Item 7 for Feature 123 to formalise.

The QRCoder PackageReference we added to the new library's csproj during Phase 2 is also reverted, but the discovery is recorded here for Feature 123 / future-122 to apply: QRCoder is user-facing (genuinely required for `QrPresentationDisplay`), so the original bundle-hygiene exclusion list was too aggressive. Final exclusion list should be just `Z.Blazor.Diagrams` and `YamlDotNet`.

## What remains on the 122 branch

- Commit `1993b312` — spec
- Commit `ff95840e` — plan
- Commit `10d931ca` — tasks
- Commit `654744f9` — Phase 1 scaffold + Phase 1 build verification
- This document (committed alongside the tasks.md / plan.md updates that reflect the blocked status)

The branch is **not abandoned** — it is parked until Feature 123 merges. When that happens, Feature 122 resumes from Phase 1's commit with an updated `research.md` Phase 0 pass and Phase 2 re-attempted against the cleaner target.
