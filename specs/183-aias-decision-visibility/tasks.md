---
description: "Task list for AIAS decision integrity & visibility (feature 183)"
---

# Tasks: AIAS decision integrity & visibility

**Input**: Design documents from `/specs/183-aias-decision-visibility/`

**Prerequisites**: plan.md ✅, spec.md ✅, research.md ✅, data-model.md ✅, contracts/ ✅

**Tests**: INCLUDED — the spec explicitly mandates them (FR-014, FR-015, FR-016) and the team works TDD.

**Organization**: By user story. US1 (P1) and US2 (P2) touch disjoint code (web client vs Blueprint Service) and are independently implementable, testable, and deployable.

## Format: `[ID] [P?] [Story] Description`

- **[P]**: Can run in parallel (different files, no dependency on an incomplete task)
- **[Story]**: US1 / US2

## Path Conventions

Web app (Blazor WASM client + .NET microservices). Client work lives in `src/Apps/Sorcha.UI/Sorcha.UI.Components.User/` (RootNamespace `Sorcha.UI.Core`); server work in `src/Services/Sorcha.Blueprint.Service/`.

---

## Phase 1: Setup (Shared)

**Purpose**: Confirm the baseline before touching code.

- [ ] T001 Confirm a clean build baseline: `dotnet build` succeeds on branch `183-aias-decision-visibility` (stale DLLs cause phantom test fails).
- [ ] T002 [P] Re-read the design doc `docs/superpowers/specs/2026-07-12-aias-emailverified-claim-source-design.md` and the two contracts in `specs/183-aias-decision-visibility/contracts/` so implementation matches the agreed shapes.

---

## Phase 2: Foundational (Blocking Prerequisites)

**Purpose**: None. US1 and US2 share no new foundational code — US1 is client-only, US2 is Blueprint-Service-only, and both reuse existing seams (`AuthenticationStateProvider`, `IBlueprintInboxWriter`, `IParticipantServiceClient`, `IPlatformInboxClient`). Proceed straight to the stories.

**Checkpoint**: Baseline builds — user story implementation can begin (US1 and US2 in parallel if staffed).

---

## Phase 3: User Story 1 — A genuine applicant receives their credential (Priority: P1) 🎯 MVP

**Goal**: Carry the applicant's real `email_verified` status onto the wallet-signed submission via a reusable headless `x-claim-source` binding, so the AIAS gate is genuine and verified applicants are approved.

**Independent Test**: A verified citizen submitting a valid AIAS application (real postcode + photo) is approved and receives the credential; an unverified citizen is rejected — reproducible end-to-end without manual payload editing.

### Tests for User Story 1 ⚠️ (write FIRST, ensure they FAIL)

- [ ] T003 [P] [US1] Create `tests/Sorcha.UI.Core.Tests/Components/Forms/ClaimSourceSeederTests.cs` asserting `IClaimSourceSeeder.Resolve` against the AIAS action-1 schema: (a) principal `email_verified=true` → `{ "/emailVerified": true }`; (b) `email_verified=false` → `{ "/emailVerified": false }`; (c) claim absent → `{ "/emailVerified": false }` (fail closed); (d) a property with no `x-claim-source` → absent from the result; (e) null schema/principal → empty map. Per contracts/x-claim-source.md.

### Implementation for User Story 1

- [ ] T004 [US1] Create `src/Apps/Sorcha.UI/Sorcha.UI.Components.User/Services/User/Forms/ClaimSourceSeeder.cs` — `IClaimSourceSeeder` + pure impl: walk top-level `properties` for `x-claim-source`, read the claim from `ClaimsPrincipal`, coerce by declared `type` (boolean fail-closed; else raw string when present), return leading-slash JSON-Pointer → value. License header + XML `<summary>` docs. Make T003 pass.
- [ ] T005 [US1] Register `IClaimSourceSeeder` in DI for both hosts: the web SPA (`src/Apps/Sorcha.UI/Sorcha.UI.Core/.../ServiceCollectionExtensions`) and the PWA (`src/Apps/Sorcha.Wallet.Pwa/Extensions/ServiceCollectionExtensions.cs`).
- [ ] T006 [US1] Wire the seed pass into `src/Apps/Sorcha.UI/Sorcha.UI.Components.User/Components/Forms/SorchaFormRenderer.razor`: on `actionChanged`, resolve `AuthenticationStateProvider` + `IClaimSourceSeeder` via `IServiceProvider.GetService` (graceful skip when unregistered), read the auth state, call `Resolve`, and write each pointer into `_formContext.FormData` **only if not already set** — mirroring the persona-autofill fire-and-forget + `InvokeAsync(StateHasChanged)`.
- [ ] T007 [P] [US1] Add `"x-claim-source": "email_verified"` to the `emailVerified` property in `demos/AIAS/blueprints/aias-assured-identity.template.json` (action 1).
- [ ] T008 [US1] De-hardcode `demos/AIAS/rehearse.ps1`: remove the fixed `emailVerified = $true`; tie the approve-path value to the applicant's confirmed-email state (with a comment that this mirrors the client stamp); ADD a third case — an unverified applicant (skip `Confirm-SorchaUserEmail`) submitting with NO `emailVerified` → assert reject with the email reason + no credential (FR-014).

**Checkpoint**: `ClaimSourceSeederTests` pass; the web form carries `emailVerified` on the wire; rehearse exercises approve + unverified-reject. US1 is independently demonstrable (MVP).

---

## Phase 4: User Story 2 — A rejected applicant learns why (Priority: P2)

**Goal**: On a terminal reject that opts in via `x-decision-notice`, drop a durable F118 bell/inbox entry carrying the on-brand reason to the starting participant — fail-safe.

**Independent Test**: Cause a gate rejection; a durable notification carrying the reason appears for the applicant and survives reload/re-login; a notification-write failure never fails the decision.

### Tests for User Story 2 ⚠️ (write FIRST, ensure they FAIL)

- [ ] T009 [P] [US2] Extend `tests/Sorcha.Blueprint.Service.Tests/Services/BlueprintInboxWriterTests.cs` for `WriteDecisionAsync`: resolves recipient wallet→participant→PlatformUserId and writes `Category="Workflow"`, `Summary`==reason, `Title`, `Severity`; idempotent on retry (same deterministic `SourceEventId`); short-circuits (no throw, no write) on empty inputs / unresolved participant / unresolved PlatformUserId. Per contracts/x-decision-notice.md.
- [ ] T010 [P] [US2] Add an `ActionExecutionService` routing test in `tests/Sorcha.Blueprint.Service.Tests/Services/` asserting: a submitted action whose selected route carries `x-decision-notice` triggers exactly one `WriteDecisionAsync` with the resolved recipient + reason; a route without the annotation triggers none; and an inbox-write that throws does NOT fail the submission (fault injection). FR-016.

### Implementation for User Story 2

- [ ] T011 [US2] Surface `x-decision-notice` on the route model in `src/Services/Sorcha.Blueprint.Service/` (or `Sorcha.Blueprint.Models` where routes are modelled): parse `{recipientParticipantId, reasonField, title, severity?}` from the route JSON (severity default `"Warning"`); tolerate absence.
- [ ] T012 [US2] Add `WriteDecisionAsync` to `src/Services/Sorcha.Blueprint.Service/Services/Implementation/BlueprintInboxWriter.cs` (+ `IBlueprintInboxWriter`): reuse the existing wallet→participant→PlatformUserId resolution and the deterministic-`SourceEventId` helper `(recipientWallet, instanceId, actionId, "decision-notice")`; `Category="Workflow"`, `Summary`=reason, `DetailHref=/api/instances/{instanceId}`, `IconKey="workflow.rejected"`; XML docs; try/log/swallow. Make T009 pass.
- [ ] T013 [US2] Hook the write into `src/Services/Sorcha.Blueprint.Service/Services/Implementation/ActionExecutionService.cs`: after route resolution, for each selected route carrying `x-decision-notice`, resolve `recipientParticipantId` → wallet from the instance participant bindings (same resolution as `credentialIssuanceConfig.recipientParticipantId`) and `reasonField` from the merged payload, then call `WriteDecisionAsync`. Wrap the whole block in `try` / `LogWarning` / swallow so it never affects sealing/routing/response. Make T010 pass.
- [ ] T014 [P] [US2] Add the `x-decision-notice` block to the `rejected-terminal` route in `demos/AIAS/blueprints/aias-assured-identity.template.json` (action 2): `{recipientParticipantId:"citizen", reasonField:"/verificationNotes", title:"AIAS could not assure your identity", severity:"Warning"}`.

**Checkpoint**: Both server tests pass; a gate rejection yields a durable, reasoned bell entry for the applicant. US1 and US2 are both independently functional.

---

## Phase 5: Polish, Deploy & Verify (Cross-Cutting)

**Purpose**: Docs sync, live deploy, and the n1 verification bar.

- [ ] T015 [P] Docs sync: add the `x-claim-source` + `x-decision-notice` extensions to `.claude/skills/blueprint-builder/SKILL.md` and a short note to `.claude/skills/sorcha-architecture/SKILL.md` (F118 decision-notice writer); note the reusable form-init claim seed in `CLAUDE.md` Critical Patterns if warranted. Update the AIAS `demos/AIAS/README.md` reject-visibility paragraph.
- [ ] T016 Full targeted test run: `dotnet build` then `dotnet test tests/Sorcha.UI.Core.Tests/...` and `dotnet test tests/Sorcha.Blueprint.Service.Tests/...` (one project each); rehearse `./demos/AIAS/rehearse.ps1 -Target docker` green (approve + bad-postcode-reject + unverified-reject).
- [ ] T017 Deploy to n1 (code-only, no `down -v`): build + publish `sorcha-ui-web` and `sorcha-blueprint` images; `up -d --force-recreate --no-deps` those two (keep `-f docker-compose.smtp.yml`); **re-provision the AIAS blueprint** so the live schema carries both extensions; update `demos/AIAS/state.json` + `assure-id.config.json` with the new blueprint id; restart the F176-current local agent. Per quickstart.md.
- [ ] T018 Live verify (Chrome DevTools MCP against `https://n1.sorcha.dev/app`): HAPPY PATH — fresh citizen, verify email, submit with `EH9 1JA` + photo, capture the action-1 request and confirm `emailVerified: true` on the wire, agent approves, `AssuredIdentityCredential` delivered (SC-001/002). REJECT VISIBILITY — cause a gate rejection, confirm a durable bell/inbox entry carrying the on-brand reason, surviving reload + re-login (SC-004).
- [ ] T019 [P] Update the AIAS memory note (`aias-conference-demo.md`) that the web happy-path is now genuinely validated (superseding the misleading rehearse result) and reject visibility ships; open the follow-up issue for the deferred My Applications page + email-on-decision.

---

## Dependencies & Execution Order

### Phase Dependencies

- **Setup (Phase 1)**: no dependencies.
- **Foundational (Phase 2)**: empty — no blocker.
- **US1 (Phase 3)** and **US2 (Phase 4)**: both may start immediately after Setup and can proceed **in parallel** (disjoint code: client vs Blueprint Service). Priority order for a single implementer is US1 → US2 (US1 is the MVP).
- **Polish (Phase 5)**: after the story(ies) being shipped are done. T017/T018 require BOTH stories + the blueprint edits (T007, T014) to be on the re-provisioned blueprint.

### Within each story

- Tests first (T003 before T004–T008; T009/T010 before T011–T014). Verify they FAIL before implementing.
- US1: seeder (T004) before renderer wiring (T006); DI (T005) before the renderer can resolve it at runtime.
- US2: route model (T011) + writer (T012) before the hook (T013).

### Parallel Opportunities

- T003 (US1 test) ∥ T009, T010 (US2 tests) — different projects.
- Once tests are red: US1 (T004–T008) ∥ US2 (T011–T014) by different developers.
- Blueprint edits T007 (US1) and T014 (US2) touch the same file (`aias-assured-identity.template.json`) — do NOT mark both [P] against each other; sequence them or merge in one edit.
- T015 (docs) and T019 (memory) are [P] against code tasks.

---

## Implementation Strategy

### MVP first (US1 only)

1. Phase 1 Setup → 2. US1 (T003–T008) → 3. **STOP & VALIDATE**: rehearse approve + unverified-reject green, `emailVerified` on the wire. This alone unblocks every real applicant — shippable MVP.

### Incremental delivery

1. US1 → deploy + verify happy path (the acute fix).
2. US2 → deploy + verify reject visibility.
3. Both ride one n1 re-provision (T017) since the blueprint carries both extensions — so in practice ship together, but each is independently testable at the unit/rehearse level first.

---

## Notes

- [P] = different files, no incomplete-task dependency.
- The two blueprint edits (T007, T014) share one file — combine them when implementing both stories together.
- `dotnet build` before every `dotnet test`; `dotnet test` takes ONE project; xUnit v3 + FluentAssertions + Moq; license header + file-scoped namespaces.
- The inbox write MUST stay best-effort (FR-010) — never let it affect sealing/routing.
- Commit after each task or logical group; verify red-before-green on the test tasks.
