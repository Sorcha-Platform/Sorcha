---

description: "Task list for Feature 122 — Shared User-Facing UI Component Library"
---

# Tasks: Shared User-Facing UI Component Library

**Input**: Design documents from `/specs/122-shared-user-components/`
**Prerequisites**: plan.md, spec.md, research.md, data-model.md, contracts/, quickstart.md

**Tests**: Tests are included for the new test project (Sorcha.UI.Components.User.Tests). Existing tests migrating from Sorcha.UI.Core.Tests are moved unchanged (per FR-005 / SC-004); new bUnit tests cover the inverse-migration components (`ConsentSheet`, `CredentialPickerDialog`, `NoMatchingCredentialDialog`) that previously lived in the PWA.

**Organization**: Tasks are grouped by user story to enable independent implementation and testing of each story.

## Format: `[ID] [P?] [Story] Description`

- **[P]**: Can run in parallel (different files, no dependencies)
- **[Story]**: Which user story this task belongs to (e.g., US1, US2, US3, US4)
- Include exact file paths in descriptions

## Path Conventions

- Repository root: `C:\projects\Sorcha\`
- New library: `src/Apps/Sorcha.UI/Sorcha.UI.Components.User/`
- Existing libraries: `src/Apps/Sorcha.UI/Sorcha.UI.Core/`, `src/Apps/Sorcha.Citizen.Wallet/`
- Test project (new): `tests/Sorcha.UI.Components.User.Tests/`

---

## Phase 1: Setup — Create the New Project Shell

**Purpose**: Scaffold `Sorcha.UI.Components.User` as an empty Razor class library and verify it builds. No files moved yet; no consumer references added.

- [x] T001 Create directory `src/Apps/Sorcha.UI/Sorcha.UI.Components.User/` and add `Sorcha.UI.Components.User.csproj` with `Microsoft.NET.Sdk.Razor` SDK targeting .NET 10. PackageReferences: `MudBlazor`, `Microsoft.AspNetCore.Components.Authorization`, `Microsoft.AspNetCore.Components.WebAssembly`, `Microsoft.Extensions.Http`, `Microsoft.JSInterop`. ProjectReferences: `Common/Sorcha.Blueprint.Models`, `Common/Sorcha.Register.Models`, `Common/Sorcha.Tenant.Models`, `Common/Sorcha.ServiceClients.Http`, `Core/Sorcha.Blueprint.Schemas.Client`. **Explicitly DO NOT add** `Z.Blazor.Diagrams`, `YamlDotNet`, `Blazored.LocalStorage`, `QRCoder`, `SimpleBase`. Set `<NoWarn>$(NoWarn);MUD0002;RZ10012</NoWarn>` to match UI.Core conventions.
- [x] T002 Add `src/Apps/Sorcha.UI/Sorcha.UI.Components.User/_ViewImports.razor` (empty) and `src/Apps/Sorcha.UI/Sorcha.UI.Components.User/Components/_Imports.razor` (with `@using MudBlazor` and `@using Microsoft.AspNetCore.Components`) so the empty Razor project compiles.
- [x] T003 Add `Sorcha.UI.Components.User` project to the solution file `src/Apps/Sorcha.UI/Sorcha.UI.sln`.
- [x] T004 [P] Create `src/Apps/Sorcha.UI/Sorcha.UI.Components.User/Extensions/ServiceCollectionExtensions.cs` with stub `public static IServiceCollection AddSorchaUserComponents(this IServiceCollection services, IConfiguration configuration)` that returns the collection unchanged. Concrete service registrations are filled in T037 once services have been moved.
- [x] T005 Verify the empty project builds: run `dotnet build src/Apps/Sorcha.UI/Sorcha.UI.Components.User/Sorcha.UI.Components.User.csproj` and confirm zero errors. Commit with message `chore(122): scaffold Sorcha.UI.Components.User project shell`.

**Checkpoint**: Empty library project exists, compiles, is registered in the solution. Nothing else has changed.

---

## Phase 2: Foundational — Atomic File Move Across Folders

**STATUS — 2026-05-13:** Unblocked. Feature 123 (PR #641, merged 2026-05-13) shipped the audience-folder split in `Sorcha.UI.Core`. The bi-modal coupling that broke the 2026-05-11 Phase 2 attempt is resolved: `IRegisterService` is now `IRegisterReadService` + `IRegisterGovernanceService`, and shared DTOs live in `Services/Shared/`. See `phase-2-discovery.md` for the historical forensic narrative and `research.md` for the refreshed verdict tables.

When Feature 123 merges, the steps below execute against a cleaner target. The original task descriptions stay below as the eventual instruction set; the verdict tables in `research.md` will need refreshing during Feature 122's resume to reflect Feature 123's outcome.

**Purpose**: Move all user-facing components, services, and models from `Sorcha.UI.Core` into the new library in a single coherent change set. Add the UI.Core → new-library reference so the six host apps transparently see the moved files. After this phase, the entire Sorcha.UI family must still build and pass tests with no consumer-side changes.

**⚠️ CRITICAL**: This is a single commit. Every file move within this phase happens together. No intermediate state.

**Namespace policy**: Files moved into the new library **preserve their existing namespaces** (e.g., a file that declared `namespace Sorcha.UI.Core.Components.Forms;` keeps that namespace verbatim after the move). This is what lets the six existing host apps continue to compile without touching their `@using` directives.

**Namespace policy addendum (2026-05-11):** the empty new library currently sets no `<RootNamespace>`. The Phase 2 attempt found that the simplest way to honour the namespace-preservation policy is `<RootNamespace>Sorcha.UI.Core</RootNamespace>` in the new csproj — files moved retain their original namespaces automatically, with no per-file edits. Post-Feature-123, when Phase 2 resumes, that csproj setting should be added before the moves begin.

- [ ] T006 Move `src/Apps/Sorcha.UI/Sorcha.UI.Core/Components/Forms/` (all subfolders: root .razor files, `Controls/`, `Layouts/`, `Panels/`, including `SorchaFormRenderer.razor` + `.css`, `ControlDispatcher.razor`, `ReviewSummaryRenderer.razor`, `PersonaFillSummary.razor` + `.css`, `IdCardLayout.razor` + `.css`, and every other file in the tree) to `src/Apps/Sorcha.UI/Sorcha.UI.Components.User/Components/Forms/`. Preserve namespaces verbatim. Use `git mv` so renames are tracked.
- [ ] T007 [P] Move `src/Apps/Sorcha.UI/Sorcha.UI.Core/Components/Credentials/` (13 files: `CredentialAcceptCard`, `CredentialCard`, `CredentialCardList`, `CredentialDetailView`, `CredentialLifecycleDialog`, `DisclosurePicker`, `IssuanceSummaryPanel`, `PresentationRequestDetail`, `PresentationRequestDialog`, `PresentationRequestList`, `PresentationSubmitDialog`, `QrPresentationDisplay`, `VerificationTrustView`) → `src/Apps/Sorcha.UI/Sorcha.UI.Components.User/Components/Credentials/`. Preserve namespaces. Use `git mv`.
- [ ] T008 [P] Move `src/Apps/Sorcha.UI/Sorcha.UI.Core/Components/Wallet/` (4 files: `ReceiptProofCard.razor`, `TransactionDetailDrawer.razor`, `TransactionLifecycleTicks.razor` + `.css`, `TransactionTickStatus.cs`) → `src/Apps/Sorcha.UI/Sorcha.UI.Components.User/Components/Wallet/`. Preserve namespaces. Use `git mv`.
- [ ] T009 [P] Move `src/Apps/Sorcha.UI/Sorcha.UI.Core/Components/Participants/` (6 files: `ParticipantDetail`, `ParticipantForm`, `ParticipantList`, `ParticipantSearch`, `PublishParticipantDialog`, `WalletLinkForm`) → `src/Apps/Sorcha.UI/Sorcha.UI.Components.User/Components/Participants/`. Preserve namespaces. Use `git mv`.
- [ ] T010 Selectively move user-facing files from `src/Apps/Sorcha.UI/Sorcha.UI.Core/Components/Shared/` to `src/Apps/Sorcha.UI/Sorcha.UI.Components.User/Components/Shared/`: move `ConfirmDialog.razor`, `EmptyState.razor`, `JsonTreeNode.razor`, `JsonTreeView.razor`, `JwtViewerDialog.razor`, `ResizableSplitter.razor`, `ServiceUnavailable.razor`, `TruncatedId.razor`. **Leave behind** in UI.Core: `BreadcrumbNav.razor`, `UserProfileMenu.razor`, `LogoutConfirmDialog.razor` (web-chrome only).
- [ ] T011 [P] Move `src/Apps/Sorcha.UI/Sorcha.UI.Core/Services/Forms/` → `src/Apps/Sorcha.UI/Sorcha.UI.Components.User/Services/Forms/`. Preserve namespaces. Use `git mv`.
- [ ] T012 [P] Move `src/Apps/Sorcha.UI/Sorcha.UI.Core/Services/Persona/` → `src/Apps/Sorcha.UI/Sorcha.UI.Components.User/Services/Persona/`. Preserve namespaces.
- [ ] T013 [P] Move `src/Apps/Sorcha.UI/Sorcha.UI.Core/Services/Credentials/` → `src/Apps/Sorcha.UI/Sorcha.UI.Components.User/Services/Credentials/`. Preserve namespaces.
- [ ] T014 [P] Move `src/Apps/Sorcha.UI/Sorcha.UI.Core/Services/AddressLookup/` → `src/Apps/Sorcha.UI/Sorcha.UI.Components.User/Services/AddressLookup/`. Preserve namespaces.
- [ ] T015 [P] Move `src/Apps/Sorcha.UI/Sorcha.UI.Core/Models/Actions/` → `src/Apps/Sorcha.UI/Sorcha.UI.Components.User/Models/Actions/`. Preserve namespaces.
- [ ] T016 [P] Move `src/Apps/Sorcha.UI/Sorcha.UI.Core/Models/Common/` → `src/Apps/Sorcha.UI/Sorcha.UI.Components.User/Models/Common/`. Preserve namespaces.
- [ ] T017 [P] Move `src/Apps/Sorcha.UI/Sorcha.UI.Core/Models/Credentials/` → `src/Apps/Sorcha.UI/Sorcha.UI.Components.User/Models/Credentials/`. Preserve namespaces.
- [ ] T018 [P] Move `src/Apps/Sorcha.UI/Sorcha.UI.Core/Models/Forms/` → `src/Apps/Sorcha.UI/Sorcha.UI.Components.User/Models/Forms/`. Preserve namespaces.
- [ ] T019 [P] Move `src/Apps/Sorcha.UI/Sorcha.UI.Core/Models/Participants/` → `src/Apps/Sorcha.UI/Sorcha.UI.Components.User/Models/Participants/`. Preserve namespaces.
- [ ] T020 [P] Move `src/Apps/Sorcha.UI/Sorcha.UI.Core/Models/Wallet/` → `src/Apps/Sorcha.UI/Sorcha.UI.Components.User/Models/Wallet/`. Preserve namespaces.
- [ ] T021 Audit `src/Apps/Sorcha.UI/Sorcha.UI.Core/Models/Authentication/` file-by-file. For each file, decide whether the type is referenced by a component that has moved (move it) or by admin/designer/explorer code only (leave it). Document the decision per file inline in the commit message.
- [ ] T022 Edit `src/Apps/Sorcha.UI/Sorcha.UI.Core/Sorcha.UI.Core.csproj` to add `<ProjectReference Include="..\Sorcha.UI.Components.User\Sorcha.UI.Components.User.csproj" />`. This is the single edge that lets the six existing host apps transparently re-find the moved files via UI.Core's transitive re-export.
- [ ] T023 Run `dotnet build src/Apps/Sorcha.UI/Sorcha.UI.sln` from the solution root. **Required outcome**: zero errors across all six web host apps. Any consumer that fails to build indicates a namespace was not preserved or a model dependency was missed in T015–T021.
- [ ] T024 Run `dotnet test tests/Sorcha.UI.Core.Tests/Sorcha.UI.Core.Tests.csproj --nologo`. **Required outcome**: every test that previously passed still passes, with zero test-source-code modifications. (FR-005 / SC-004.)
- [ ] T025 Commit the entire move atomically with message `refactor(122): extract user-facing components from Sorcha.UI.Core into Sorcha.UI.Components.User`. The diff is large but mechanical (mostly git renames); review commit-by-commit shows the structural change.

**Checkpoint**: Sorcha.UI.* family compiles and tests pass. User-facing components and their supporting services + models now live in the new library; admin/designer/explorer files remain in UI.Core. Phase 2 is the gate before any user-story phase begins.

---

## Phase 3: User Story 1 — PWA gains access to the rich user-facing experience (Priority: P1) 🎯 MVP

**Goal**: The Sorcha.Citizen.Wallet PWA can reference the shared library and successfully render at least one core user-facing component end-to-end, proving the integration is real and not merely a successful build.

**Independent Test**: Run the PWA locally, navigate to a proof page that renders `SorchaFormRenderer` and `IdCardLayout`, confirm both components render without runtime exceptions and respond to user input.

### Implementation

- [ ] T026 [US1] Edit `src/Apps/Sorcha.Citizen.Wallet/Sorcha.Citizen.Wallet.csproj` to add `<ProjectReference Include="..\Sorcha.UI\Sorcha.UI.Components.User\Sorcha.UI.Components.User.csproj" />` to the existing `<ItemGroup>` of ProjectReferences.
- [ ] T027 [US1] Edit `src/Apps/Sorcha.Citizen.Wallet/Program.cs` to call `builder.Services.AddSorchaUserComponents(builder.Configuration)` after the existing service registrations. Add the corresponding `using Sorcha.UI.Components.User.Extensions;` at the top of the file.
- [ ] T028 [US1] Create `src/Apps/Sorcha.Citizen.Wallet/Pages/_SharedComponentsProof.razor` (filename underscore-prefixed so it's not a public-facing route) that:
   - `@page "/dev/shared-components-proof"`
   - Renders a minimal `SorchaFormRenderer` with a hard-coded JSON schema (e.g., single text field)
   - Renders an `IdCardLayout` with a sample `IdCardLayoutConfig` value
   - Imports the appropriate `@using` directives for the migrated namespaces
- [ ] T029 [US1] Run `dotnet build src/Apps/Sorcha.Citizen.Wallet/Sorcha.Citizen.Wallet.csproj`. **Required outcome**: zero errors.
- [ ] T030 [US1] Run the PWA locally (`dotnet run --project src/Apps/Sorcha.AppHost` or the wallet's own launch profile) and navigate to `/wallet/dev/shared-components-proof`. **Required outcome**: both components render visibly, the form accepts text input, no browser-console errors.
- [ ] T031 [US1] Commit with message `feat(122): wire Sorcha.Citizen.Wallet to Sorcha.UI.Components.User`.

**Checkpoint**: PWA references the shared library and renders user-facing components from it. User Story 1 satisfied — independently testable.

---

## Phase 4: User Story 2 — PWA stays lean and installable (Priority: P1)

**Goal**: The PWA's published bundle is verifiably free of admin/designer/explorer/blueprint-authoring component assemblies and their heavy transitive dependencies (`Z.Blazor.Diagrams`, `YamlDotNet`). Bundle hygiene is codified as an automated check rather than a one-time manual inspection.

**Independent Test**: Build the PWA, run `scripts/check-pwa-bundle.ps1`, and confirm the script exits 0 with each assertion passing.

### Implementation

- [ ] T032 [US2] Create `scripts/check-pwa-bundle.ps1` that:
   - Lists `.wasm` and `.dll` files under `src/Apps/Sorcha.Citizen.Wallet/bin/Debug/net10.0/wwwroot/_framework/`
   - Asserts NO file name matches `Blazor.Diagrams*` (case-insensitive)
   - Asserts NO file name matches `YamlDotNet*`
   - Asserts NO file name matches `Sorcha.UI.Core*`
   - Asserts AT LEAST ONE file name matches `Sorcha.UI.Components.User*`
   - Writes a summary table of assertion outcomes to stdout
   - Exits with code 0 on all-pass, non-zero on any failure
- [ ] T033 [US2] Run `dotnet build src/Apps/Sorcha.Citizen.Wallet/Sorcha.Citizen.Wallet.csproj` then `.\scripts\check-pwa-bundle.ps1`. **Required outcome**: all four assertions pass, exit code 0.
- [ ] T034 [US2] Create `specs/122-shared-user-components/bundle-diff.md` documenting the PWA assembly listing before and after the feature. For each added assembly, note the user-facing component requirement that justifies it (or flag it for review). For each removed assembly (if any — unlikely since PWA didn't reference UI.Core before), note the cleanup.
- [ ] T035 [US2] Wire `scripts/check-pwa-bundle.ps1` into the CI pipeline. Identify the GitHub Actions workflow that builds Sorcha.Citizen.Wallet (likely `.github/workflows/build-and-test.yml` or similar) and add a post-build step `pwsh ./scripts/check-pwa-bundle.ps1` immediately after the wallet's build job. Fail the workflow on non-zero exit.
- [ ] T036 [US2] Commit with message `feat(122): bundle hygiene script + CI gate for Sorcha.Citizen.Wallet`.

**Checkpoint**: Bundle hygiene is automatic and CI-enforced. User Story 2 satisfied — bundle weight regressions can no longer slip past unnoticed.

---

## Phase 5: User Story 3 — Web app keeps working unchanged (Priority: P1)

**Goal**: After all the file moves, the Sorcha.UI web app family renders every user-facing flow identically to its pre-feature behaviour. No regressions; no missing components; no styling glitches.

**Independent Test**: Full existing test suite for the Sorcha.UI family passes with zero test-code changes. A representative manual walkthrough of web user flows shows no visible difference from pre-feature behaviour.

### Implementation

- [ ] T037 [US3] Wire concrete service registrations into `src/Apps/Sorcha.UI/Sorcha.UI.Components.User/Extensions/ServiceCollectionExtensions.cs` `AddSorchaUserComponents()`: register `IFormSchemaService`, `ReviewSummaryDataSource`, `IPersonaService`, `ICredentialApiService`, `IQrPresentationService`, the persona autofill resolver, and the address-lookup service. Remove the now-duplicate registrations from UI.Core's existing service-registration extension method, replacing them with a `services.AddSorchaUserComponents(configuration);` call. Web app hosts get the same services automatically via UI.Core's transitive call chain.
- [ ] T038 [US3] Run the full Sorcha.UI test suite: `dotnet test tests/Sorcha.UI.Core.Tests/Sorcha.UI.Core.Tests.csproj` and `dotnet test tests/Sorcha.UI.E2E.Tests/Sorcha.UI.E2E.Tests.csproj --no-build` (the E2E tests already run inside Docker per existing project conventions; the `--no-build` flag matches usage memory). **Required outcome**: every previously-passing test still passes, zero test-source-code modifications.
- [ ] T039 [US3] Manual walkthrough — launch the AssuredIdentity walkthrough end-to-end via `walkthroughs/AssuredIdentity/run.ps1 -Profile gateway`. **Required outcome**: walkthrough completes successfully (citizen onboarding, credential issuance, presentation, verification) with no missing components, no script errors, no visual regressions vs. pre-feature behaviour.
- [ ] T040 [US3] Commit (no source changes from this phase beyond T037) with message `feat(122): wire AddSorchaUserComponents() in extension method; web app behaviour verified unchanged`.

**Checkpoint**: Web app is unchanged for users. User Story 3 satisfied — regression-safety bar met.

---

## Phase 6: User Story 4 — Developers extend once and see it everywhere (Priority: P2)

**Goal**: A developer adding a new user-facing component places it once in the shared library and both shells benefit. The library boundary is documented such that the "where does this component go?" question has an unambiguous answer.

**Independent Test**: A developer reads `Sorcha.UI.Components.User/README.md` and the `quickstart.md` worked-examples table, then places a new test component correctly on the first attempt without senior-developer review.

### Implementation — Inverse migration (PWA-grown components elevated to the library)

- [x] T041 [US4] Move `src/Apps/Sorcha.Citizen.Wallet/Components/ConsentSheet.razor` → `src/Apps/Sorcha.UI/Sorcha.UI.Components.User/Components/Presentation/ConsentSheet.razor`. **Namespace deviation**: post-F123, `Sorcha.UI.Components.User` carries `RootNamespace=Sorcha.UI.Core`, so the actual landing namespace is `Sorcha.UI.Core.Components.Presentation` (not the spec's `Sorcha.UI.Components.User.Components.Presentation`). Used `git mv`.
- [x] T042 [US4] [P] Move `CredentialPickerDialog.razor` to `Sorcha.UI.Core.Components.Presentation` (same namespace deviation as T041). Used `git mv`.
- [x] T043 [US4] [P] Move `NoMatchingCredentialDialog.razor` to `Sorcha.UI.Core.Components.Presentation` (same namespace deviation). Used `git mv`.
- [x] T044 [US4] Updated `Sorcha.Citizen.Wallet/_Imports.razor` (+ `@using Sorcha.UI.Core.Components.Presentation` and `@using Sorcha.UI.Core.Models.Presentation`). Records `ParsedPresentationRequest`, `CachedCredential`, `CredentialMatch` moved from `Sorcha.Citizen.Wallet/Services/Presentation/Models.cs` → `Sorcha.UI.Components.User/Models/User/Presentation/PresentationModels.cs` (namespace `Sorcha.UI.Core.Models.Presentation`); 4 service files (`ICredentialCache`, `IndexedDbCredentialCache`, `InMemoryCredentialCache`, `ISyncService`) swapped their using; 2 engine files (`IPresentationEngine`, `PresentationEngine`) added an extra using (engines stay PWA-side); 2 test files updated.
- [x] T045 [US4] `dotnet build Sorcha.Citizen.Wallet.csproj` 0 errors; `scripts/check-pwa-bundle.ps1` PASSED (107 assemblies, forbidden absent, Sorcha.UI.Components.User present).

### Implementation — Documentation

- [x] T046 [US4] Component-library `README.md` shipped in F122 PR #657 (Phase 5+6 follow-up) — predates this PR. Already covers the consume / add-new / decide-where-it-belongs workflows.
- [x] T047 [US4] Added two-paragraph pointer in `CLAUDE.md` directly below the F123 audience-convention block.
- [x] T048 [US4] Added "Shared user-facing component library (Feature 122)" section to `.claude/skills/sorcha-ui/SKILL.md` with placement rule + bundle-hygiene gate reference.
- [x] T049 [US4] Commit message: `feat(122 US4): inverse-migrate PWA consent/picker dialogs to shared library; document boundary`.

**Checkpoint**: Inverse migration complete, library boundary documented in three places (library README, CLAUDE.md, sorcha-ui skill). User Story 4 satisfied — developer-experience outcome shipped.

---

## Phase 7: Polish & Cross-Cutting Concerns

**Purpose**: New test project for the migrated components, final documentation updates, PR preparation.

### Test project

- [ ] T050 Create `tests/Sorcha.UI.Components.User.Tests/Sorcha.UI.Components.User.Tests.csproj` mirroring the existing `tests/Sorcha.UI.Core.Tests/` csproj pattern. PackageReferences: `xunit`, `xunit.runner.visualstudio`, `Microsoft.NET.Test.Sdk`, `bunit`, `FluentAssertions`, `Moq` (matching existing test-project conventions per memory).
- [ ] T051 [P] Move existing form-renderer bUnit tests from `tests/Sorcha.UI.Core.Tests/` to `tests/Sorcha.UI.Components.User.Tests/Components/Forms/`. Update only the `@using` directives and the test project's csproj `<ProjectReference>` lines.
- [ ] T052 [P] Move existing credential-component tests from `tests/Sorcha.UI.Core.Tests/` to `tests/Sorcha.UI.Components.User.Tests/Components/Credentials/`. Update `@using` only.
- [ ] T053 [P] Move existing wallet-component tests (TransactionLifecycleTicks, ReceiptProofCard) from `tests/Sorcha.UI.Core.Tests/` to `tests/Sorcha.UI.Components.User.Tests/Components/Wallet/`. Update `@using` only.
- [ ] T054 [P] Move existing participant-component tests from `tests/Sorcha.UI.Core.Tests/` to `tests/Sorcha.UI.Components.User.Tests/Components/Participants/`. Update `@using` only.
- [ ] T055 Add `tests/Sorcha.UI.Components.User.Tests/Components/Presentation/ConsentSheetTests.cs` — bUnit smoke tests verifying basic render, parameter binding, and `OnAccepted` / `OnDeclined` callback invocation for the inverse-migrated `ConsentSheet`. (Test naming: `MethodName_Scenario_ExpectedBehavior`.)
- [ ] T056 [P] Add `tests/Sorcha.UI.Components.User.Tests/Components/Presentation/CredentialPickerDialogTests.cs` — bUnit tests for `CredentialPickerDialog`.
- [ ] T057 [P] Add `tests/Sorcha.UI.Components.User.Tests/Components/Presentation/NoMatchingCredentialDialogTests.cs` — bUnit tests for `NoMatchingCredentialDialog`.
- [ ] T058 Run `dotnet test tests/Sorcha.UI.Components.User.Tests/Sorcha.UI.Components.User.Tests.csproj --collect:"XPlat Code Coverage"`. **Required outcome**: ≥85% line coverage on the new test project per Sorcha Constitution IV; all tests pass.

### Final integration + PR

- [ ] T059 Run `dotnet build` from the solution root with `/warnaserror` (or just inspect output) to confirm zero new compiler warnings introduced by the migration. Pre-existing warnings (the XML-doc warnings in `Sorcha.ServiceClients.Http`, the MUD0002 in Citizen.Wallet's `Enrol.razor`) are unchanged and acceptable.
- [ ] T060 Update `docs/reference/project-structure.md` to add `Sorcha.UI.Components.User/` under `src/Apps/Sorcha.UI/` with a one-line description of its role.
- [ ] T061 Update `MEMORY.md` Current Branch section to record the feature outcome: branch `122-shared-user-components` complete, library extracted, PWA references it, bundle hygiene CI gate in place.
- [ ] T062 Walkthrough validation — run `walkthroughs/AssuredIdentity/run.ps1 -Profile gateway` one final time end-to-end as the integration smoke test before opening the PR.
- [ ] T063 Open PR against master via `gh pr create --fill` with title `feat(122): shared user-facing UI component library` and body describing the 5-commit narrative + the user-story-by-user-story acceptance evidence. Wait for `claude-review`, `Run discoverability checks`, `link-check`, and build-and-test (with the new bundle-hygiene gate) to pass. Squash-merge.

**Checkpoint**: Feature complete, PR merged, all six success criteria satisfied (SC-001 PWA renders shared components, SC-002 100% user-facing coverage, SC-003 bundle hygiene, SC-004 existing tests unchanged, SC-005 boundary documented for future contributors, SC-006 PWA bundle increase justified by user-facing payload only).

---

## Dependencies & Execution Order

### Phase Dependencies

- **Setup (Phase 1, T001–T005)**: No dependencies — can start immediately.
- **Foundational (Phase 2, T006–T025)**: Depends on Setup completion. **Blocks every user-story phase.** Phase 2 is a single atomic commit; do not start any T026+ before T025 has committed.
- **US1 (Phase 3, T026–T031)**: Depends on Foundational completion.
- **US2 (Phase 4, T032–T036)**: Depends on US1 completion (the PWA must actually build with the new reference before we can verify its bundle).
- **US3 (Phase 5, T037–T040)**: Depends on Foundational completion. Independent of US1 / US2 in principle, but T037 (DI extension method) is also referenced indirectly by US1's T027, so practically US3 must complete after US1.
- **US4 (Phase 6, T041–T049)**: Depends on US1 + US3 completion (inverse migration touches wallet pages that have already been wired to the library, and documentation references the working library boundary).
- **Polish (Phase 7, T050–T063)**: Depends on US1 + US2 + US3 + US4. PR opens at the end.

### Parallel Opportunities Within Phases

**Phase 2 (Foundational)** — high parallel content. Tasks T007, T008, T009 are independent folder moves and can run in parallel. T011, T012, T013, T014 (services) are independent folder moves. T015–T020 (models) are independent folder moves. T021 (Authentication audit) is sequential due to per-file decisions. T022 (UI.Core csproj edit) and T023 (build verification) must come after every move. T024 (tests) and T025 (commit) come last.

**Phase 6 (US4)** — T042 and T043 can run in parallel with T041 (three independent file moves). T046, T047, T048 are independent documentation updates.

**Phase 7 (Polish)** — T051, T052, T053, T054 (test-file moves) are independent. T056, T057 (new bUnit tests) can run in parallel with T055.

### Parallel Example: Foundational Phase File Moves

```bash
# After T006 (Forms move — large, do alone first), the following can run in parallel:
Task: "T007 — Move Credentials components"
Task: "T008 — Move Wallet components"
Task: "T009 — Move Participants components"
Task: "T011 — Move Services/Forms"
Task: "T012 — Move Services/Persona"
Task: "T013 — Move Services/Credentials"
Task: "T014 — Move Services/AddressLookup"
Task: "T015 — Move Models/Actions"
Task: "T016 — Move Models/Common"
Task: "T017 — Move Models/Credentials"
Task: "T018 — Move Models/Forms"
Task: "T019 — Move Models/Participants"
Task: "T020 — Move Models/Wallet"
# Then T010 (selective Shared move) and T021 (Authentication audit) sequentially,
# then T022 (csproj edit), T023 (build), T024 (tests), T025 (commit).
```

---

## Implementation Strategy

### MVP First (User Story 1 Only)

1. Complete Phase 1: Setup (T001–T005)
2. Complete Phase 2: Foundational (T006–T025) — the structural move
3. Complete Phase 3: User Story 1 (T026–T031) — PWA renders a shared component
4. **STOP and VALIDATE**: confirm the proof page renders end-to-end
5. Demo if ready — this is enough to validate the architectural premise; subsequent stories harden and broaden the outcome

### Incremental Delivery

1. Setup + Foundational → library exists, web app still works, PWA can reference it
2. + US1 → PWA renders a real shared component (MVP)
3. + US2 → bundle hygiene codified, CI gate in place
4. + US3 → web app verified unchanged (regression bar met)
5. + US4 → inverse migration done, boundary documented for future contributors
6. + Polish → tests properly homed, PR ready

### Single-Developer Strategy (most realistic for this codebase)

Run sequentially in priority order. The structural move (Phase 2) is one atomic commit, so the parallel opportunities inside it are about ordering work *within* the commit rather than parallel-merging branches. A typical session:

- Session 1: Phase 1 + start Phase 2 (do the folder moves in the order above, use git status to confirm each move is clean)
- Session 2: Finish Phase 2 (T022–T025 — csproj, build, test, commit)
- Session 3: Phase 3 + Phase 4 (US1 + US2 — wallet wiring and bundle hygiene)
- Session 4: Phase 5 + Phase 6 (US3 verification + US4 inverse migration + docs)
- Session 5: Phase 7 polish + PR

---

## Notes

- [P] tasks operate on different files with no dependencies between them.
- [Story] label maps each task to its user story (US1/US2/US3/US4) for traceability against `spec.md` acceptance scenarios.
- Use `git mv` for every file relocation so renames stay tracked and the PR diff is reviewable.
- **Preserve namespaces during the migration.** This is the single most important policy — it's what keeps the six host apps building without consumer-side changes (FR-005, FR-009).
- After the migration, future net-new components added to the library should use `Sorcha.UI.Components.User.*` namespaces — naming convergence is a gradual process, not a Feature 122 deliverable.
- Bundle-hygiene CI gate (T035) is the long-term safeguard. If a future PR re-introduces designer-grade deps, that gate fails and the team is alerted before the regression ships.
- The five-commit narrative in `plan.md` R8 maps to these phases: commit 1 = Phase 1 (T005), commit 2 = Phase 2 (T025), commit 3 = Phase 3 (T031), commit 4 = Phase 6 inverse migration (T049), commit 5 = Phase 7 polish + CI wiring (consolidated with T063 squash). Phases 4 and 5 (US2 / US3 verification + DI wiring) can fold into commits 3 and 4 by topic.
- Avoid: starting any T026+ task before T025 has committed (Phase 2 is atomic); changing component visual behaviour during the move (out of scope; trigger a separate spec for any redesign); introducing new third-party packages.
