---
description: "Task list for Feature 106 — Register-native credential delivery"
---

# Tasks: Register-native credential delivery

**Input**: Design documents from `/specs/106-register-native-credentials/`
**Prerequisites**: plan.md, spec.md, research.md, data-model.md, contracts/ (all present)

**Tests**: Tests are included in this task list. Sorcha's constitution (principle IV) mandates >85% coverage for new code, and the feature touches security-sensitive paths (credential encryption, state machine invariants, cross-node trust). Test tasks are grouped immediately after the implementation they exercise so TDD is possible but not enforced at the story-level — some tasks are implementation-led with tests following directly after.

**Organization**: Tasks are grouped by the five user stories from `spec.md` so each story is independently implementable, testable, and deployable. User Story 1 (cross-node holder) and User Story 2 (single-node demo) are both P1 — they share the same implementation surface but have different verification gates.

## Format: `- [ ] [TaskID] [P?] [Story?] Description`

- **[P]**: Can run in parallel with other [P] tasks (different files, no dependencies on incomplete tasks)
- **[Story]**: `[US1]`, `[US2]`, `[US3]`, `[US4]`, `[US5]` — maps to the spec's user stories
- Setup, Foundational, and Polish phases: no story label
- Every task has an absolute or repo-relative file path

## Path conventions

This feature touches five existing Sorcha projects. All paths are relative to the Sorcha repository root at `C:\Projects\Sorcha\`. No new top-level directories.

- **Blueprint Service**: `src/Services/Sorcha.Blueprint.Service/`
- **Wallet Service**: `src/Services/Sorcha.Wallet.Service/`
- **Shared blueprint models**: `src/Common/Sorcha.Blueprint.Models/`
- **Shared wallet domain**: `src/Core/Sorcha.Wallet.Portable/`
- **Client UI**: `src/Apps/Sorcha.UI/Sorcha.UI.Core/` and `src/Apps/Sorcha.UI/Sorcha.UI.Web.Client/`
- **Tests**: `tests/Sorcha.UI.Core.Tests/`, `tests/Sorcha.Wallet.Core.Tests/` (healthy projects; see Complexity Tracking in plan.md for why we're not writing tests in the broken `Sorcha.Blueprint.Service.Tests` project)
- **Walkthroughs**: `walkthroughs/HaipVerifiedCitizen/`

---

## Phase 1: Setup (Shared Infrastructure)

**Purpose**: Feature 106 extends existing Sorcha services. Setup is minimal because there are no new projects or external dependencies to bring in.

- [ ] T001 Create the feature working branch `106-register-native-credentials` — already done by `/speckit.specify`. Verify `git branch --show-current` returns the feature branch before starting any code work.
- [ ] T002 Confirm local single-node Sorcha stack is healthy by running `docker-compose up -d` and verifying all 13 containers report `healthy` via `docker ps --format '{{.Names}} {{.Status}}'`. Needed as the primary dev + integration-test target during implementation.
- [ ] T003 [P] Verify `n1.sorcha.dev` remote target is reachable and carries the Feature 103/104 fix chain PRs #285, #286, #287, #288, #290 — inspect via `git log origin/master --oneline -20` or a direct curl against `https://n1.sorcha.dev/api/health`. Feature 106 implementation depends on that chain being live.

**Checkpoint**: Development environment confirmed, feature branch checked out.

---

## Phase 2: Foundational (Blocking Prerequisites)

**Purpose**: Data model additions and shared enum extensions that every downstream story needs. These MUST complete before any user story phase can begin.

**⚠️ CRITICAL**: No user story work can begin until this phase is complete.

### Data model — enum extensions

- [ ] T004 [P] Extend `CredentialIssuanceConfig.TargetAudience` enum with a new value `SorchaLocalWallet = 2` in `src/Common/Sorcha.Blueprint.Models/Credentials/CredentialIssuanceConfig.cs`. Add an XML doc comment explaining the delivery mode. Do NOT change default or renumber existing values.
- [ ] T005 [P] Extend `CredentialStatus` enum with `PendingAcceptance = 4` and `Declined = 5` values in `src/Core/Sorcha.Wallet.Portable/Domain/Entities/CredentialEntity.cs`. Add XML doc comments to both new values citing the state machine transitions from `data-model.md` §2.
- [ ] T006 [P] Add `IsReadOnlyMirror` boolean property to `InstanceEntity` in `src/Services/Sorcha.Blueprint.Service/Data/Entities/InstanceEntity.cs` with default `false`. Do not expose the setter publicly — internal set only.
- [ ] T007 [P] Add the corresponding `IsReadOnlyMirror` field to the domain model `Instance` in `src/Services/Sorcha.Blueprint.Service/Models/Instance.cs` (or wherever the domain instance lives in the Blueprint Service — check current file layout).

### EF migration

- [ ] T008 Create EF migration `20260415_AddReadOnlyMirrorColumn` under `src/Services/Sorcha.Blueprint.Service/Data/Migrations/` via `dotnet ef migrations add AddReadOnlyMirrorColumn --project src/Services/Sorcha.Blueprint.Service --context BlueprintDbContext`. Depends on T006. Verify the migration adds the column with default `false` and the reverse operation drops cleanly.
- [ ] T009 Apply the migration against the local dev database by running `dotnet ef database update --project src/Services/Sorcha.Blueprint.Service --context BlueprintDbContext` OR rebuild the blueprint-service container if migrations run at startup. Depends on T008.

### Shared validation rule registration

- [ ] T010 Register the new publish-time validation rule codes `VAL_BP_CRED_001`, `VAL_BP_CRED_002` (warning), and `VAL_BP_CRED_003` in the Blueprint Service's validation error code constants file. File location: find the existing `ValidationErrorCodes` class in `src/Services/Sorcha.Blueprint.Service/Models/` or equivalent. Add XML doc comments for each new code. Do NOT implement the validation logic here — that's in Wave 1 (US1 phase).

### Unit tests for the data model additions

- [ ] T011 [P] Write unit test `CredentialStatusStateMachineTests.cs` in `tests/Sorcha.Wallet.Core.Tests/` covering every transition in `data-model.md` §2 invariants INV-1 through INV-4. Each test asserts either the transition succeeds or throws `InvalidOperationException`. Depends on T005.
- [ ] T012 [P] Write unit test `TargetAudienceEnumSerialisationTests.cs` in `tests/Sorcha.UI.Core.Tests/` that round-trips a blueprint JSON containing `"targetAudience": "SorchaLocalWallet"` through `JsonSerializer` with `JsonStringEnumConverter` and asserts the enum value maps correctly in both directions. Depends on T004.
- [ ] T013 [P] Write unit test `CredentialStatusEnumSerialisationTests.cs` in `tests/Sorcha.Wallet.Core.Tests/` that round-trips `CredentialEntity` JSON with `Status = PendingAcceptance` and `Status = Declined`. Depends on T005.

**Checkpoint**: Data model is ready. All user story phases can now start in parallel, but in practice US1 should land first as the MVP.

---

## Phase 3: User Story 1 — Holder receives a credential cross-node (Priority: P1) 🎯 MVP

**Goal**: A citizen on node B submits a Verified Citizen application to a register shared with node A, node A's assessor approves, and within 30 seconds the credential appears in the citizen's pending credentials inbox on node B — without any direct communication between node B and node A.

**Independent Test**: Run Scenario 2 from `quickstart.md` (federated two-node quickstart). Verify:
- Sealed issuance transaction replicates from node A to node B via peer sync
- Node B's Wallet Service extracts and persists the credential as `PendingAcceptance`
- Node B's Blueprint Service reconstructs the instance mirror
- Holder's MyActions and MyCredentials PENDING surfaces both show the pending item
- Holder's Accept click seals an accept transaction back to the register
- Node A's Blueprint Service observes the accept and transitions the instance to Completed
- No HTTP request from node B to node A throughout the flow

Because User Story 1 and User Story 2 share the same implementation surface (cross-node correctness is a superset of single-node correctness), landing US1 automatically completes the MVP for US2. The verification tests differ — US1 uses the two-node docker-compose shape; US2 uses the single-node default.

### Wave A — Engine branch for `SorchaLocalWallet` (binds FR-001, FR-002, FR-003, FR-004, VAL_BP_CRED_001-003, VAL_RUNTIME_CRED_001-003)

- [ ] T014 [US1] Add the publish-time blueprint validation for `VAL_BP_CRED_001` (recipient participant must resolve) in the Blueprint Service's publish validator. File: `src/Services/Sorcha.Blueprint.Service/Services/Implementation/` (find the existing publish-validator file — likely `BlueprintValidator.cs` or `PublishService.cs`). Add a test-coverage entry in the validator's unit test file. Depends on T010.
- [ ] T015 [US1] Add the publish-time blueprint warning `WARN_BP_CRED_002` (recipient disclosure missing) in the same file as T014. Warning-only — MUST NOT block publish. Add a test asserting a blueprint without the explicit disclosure produces the warning but still publishes successfully.
- [ ] T016 [US1] Add the `SorchaLocalWallet` execution branch in `src/Services/Sorcha.Blueprint.Service/Services/Implementation/ActionExecutionService.cs` `ExecuteAsync` method. Implementation sketch: when `action.CredentialIssuanceConfig.TargetAudience == SorchaLocalWallet`, resolve the recipient wallet pubkey via `IWalletServiceClient.GetWalletAsync`, mint the credential via `IHaipCredentialMinter.MintCredentialAsync`, build a `DisclosureGroup` with `Recipients = [recipientWalletAddress]` and the `credential-offer-v1` payload shape, encrypt via `IEncryptionPipelineService.EncryptDisclosedPayloadsAsync`, seal into the action's disclosures under the `/credential` pointer. Depends on T004, T014. Full contract: `contracts/credential-issuance-config.md` §Runtime engine dispatch.
- [ ] T017 [US1] Runtime error handling: introduce `VAL_RUNTIME_CRED_001` (recipient wallet not resolvable), `VAL_RUNTIME_CRED_002` (credential mint failed), `VAL_RUNTIME_CRED_003` (encryption failed) error codes. When any fires, the instance MUST remain in its pre-execution state so the caller can retry. Add these codes to the runtime error code constants and surface them via `ValidationResult` shape. Depends on T016.
- [ ] T018 [P] [US1] Unit test `ActionExecutionServiceSorchaLocalWalletTests.cs` in `tests/Sorcha.UI.Core.Tests/` (not Blueprint.Service.Tests due to pre-existing breakage — see plan.md Complexity Tracking). Mock the minter + encryption pipeline + wallet service client; assert `ExecuteAsync` with a `SorchaLocalWallet` action calls them in the expected order with the expected inputs; assert the resulting transaction carries a `/credential` disclosure; assert the three runtime error codes fire on the right failure paths. Depends on T016, T017.
- [ ] T019 [US1] Publish-time validation unit tests for `VAL_BP_CRED_001` and `WARN_BP_CRED_002` in the same test file as T018. Depends on T014, T015.

### Wave B — Wallet Service inbound credential detection (binds FR-005, FR-006, FR-007, FR-008)

- [ ] T020 [P] [US1] Create `IInboundCredentialDetector` interface in `src/Services/Sorcha.Wallet.Service/Services/Interfaces/IInboundCredentialDetector.cs`. Full signature from `contracts/inbound-credential-detection.md` §Interface. Include the `InboundCredentialExtract` record type in the same file or a sibling.
- [ ] T021 [US1] Implement the default `InboundCredentialDetector` in `src/Services/Sorcha.Wallet.Service/Services/Implementation/InboundCredentialDetector.cs`. Dependencies: `IRegisterServiceClient`, `IEncryptionPipelineService`, `IWalletManager`, `ICredentialRepository`, `ILogger<InboundCredentialDetector>`, `InboundCredentialDetectorMetrics`. Behaviour: fetch transaction → find recipient disclosure group → decrypt via wallet manager → parse `credential-offer-v1` shape → dedup by credential id → return extract or null. MUST NOT throw — catch-all around the whole body, log warning, return null. Depends on T020.
- [ ] T022 [P] [US1] Create `InboundCredentialDetectorMetrics.cs` in `src/Services/Sorcha.Wallet.Service/Services/Implementation/` defining the six OpenTelemetry counters + one histogram from `contracts/inbound-credential-detection.md` §Metrics. Use the existing `NotificationMetrics.cs` as the shape reference. Depends on T020.
- [ ] T023 [US1] Extend `NotificationDeliveryService.DeliverAsync` in `src/Services/Sorcha.Wallet.Service/Services/Implementation/NotificationDeliveryService.cs` with "Step 2b" (between user resolution and preference check) that calls `_inboundCredentialDetector.TryExtractAsync(...)`, persists the returned extract as a `CredentialEntity` with `Status = PendingAcceptance` via the existing `ICredentialRepository.AddAsync` path (NOT the new `PatchStatusAsync` from T026 — inbound is an insert, not a transition), and enriches the subsequent `InboundActionEvent` with the new `CredentialOfferId`. Exact code shape: `contracts/inbound-credential-detection.md` §Hook point. Depends on T021, T024. If `ICredentialRepository` does not already expose `AddAsync`, add that as an additive task inside Wave B before T023.
- [ ] T024 [US1] Add `CredentialOfferId` (nullable string) to `InboundActionEvent` in wherever the shared DTO lives — check `src/Services/Sorcha.Wallet.Service/Models/` or `src/Common/Sorcha.ServiceClients.Http/`. Depends on nothing specific but needed by T023.
- [ ] T025 [US1] Register `IInboundCredentialDetector` + `InboundCredentialDetectorMetrics` in the Wallet Service DI container in `src/Services/Sorcha.Wallet.Service/Program.cs`. Follow the existing pattern for `NotificationDeliveryService`. Depends on T021, T022.

### Wave C — Wallet Service credential repository + PATCH endpoint (binds FR-013, FR-014, FR-015, INV-1 through INV-4)

- [ ] T026 [US1] Add `PatchStatusAsync` method to `ICredentialRepository` in `src/Services/Sorcha.Wallet.Service/Repositories/ICredentialRepository.cs` (or wherever the interface lives). Signature from `contracts/credential-status-enum.md` §Repository signature. Implementation enforces INV-1 through INV-4 — throws `InvalidOperationException` on disallowed transitions. Depends on T005.
- [ ] T027 [US1] Implement `PatchStatusAsync` in the concrete credential repository class (find via `grep -rn "class.*CredentialRepository" src/Services/Sorcha.Wallet.Service/`). Add EF upsert logic that enforces the state machine. Depends on T026.
- [ ] T028 [US1] Add the `?status=` query parameter to `GET /api/v1/wallets/{walletAddress}/credentials` in `src/Services/Sorcha.Wallet.Service/Endpoints/CredentialEndpoints.cs`. Default: `Active` when omitted (preserves existing behaviour). Accept values: enum names + literal `"All"`. Depends on T005.
- [ ] T029 [US1] Add `PATCH /api/v1/wallets/{walletAddress}/credentials/{credentialId}` endpoint in the same file. Request body: `{ status: CredentialStatus }`. Calls `credentialRepository.PatchStatusAsync`. Returns 200 with the updated entity on success, 409 Conflict on invalid transitions (catch `InvalidOperationException` and map). Depends on T027.
- [ ] T030 [US1] Emit `CredentialStatusChangedEvent` on the SignalR `events:wallet` hub when a successful transition occurs. Shape from `contracts/credential-status-enum.md` §SignalR notification shape. Location: the same PATCH handler as T029. Depends on T029.
- [ ] T031 [P] [US1] Unit tests for `PatchStatusAsync` in `tests/Sorcha.Wallet.Core.Tests/`: happy path (PendingAcceptance → Active), happy path (PendingAcceptance → Declined), idempotent no-op (Active → Active), invalid transition (Active → PendingAcceptance throws), invalid transition (Declined → Active throws), row not found returns null. Depends on T027.
- [ ] T032 [P] [US1] Unit tests for `InboundCredentialDetector` in `tests/Sorcha.Wallet.Core.Tests/`: happy path extraction, false positive (no recipient disclosure), decrypt failure, duplicate credential, malformed payload shape, dependency throws (never propagates). Uses mocks of `IRegisterServiceClient`, `IEncryptionPipelineService`, `IWalletManager`, `ICredentialRepository`. Depends on T021.

### Wave D — Blueprint Service instance mirror reconstructor (binds FR-010, FR-011, FR-012, FR-018, FR-019)

- [ ] T033 [US1] Add `UpdateMirrorAsync` method to `IInstanceStore` in `src/Services/Sorcha.Blueprint.Service/Storage/IInstanceStore.cs`. Also add `CreateMirrorAsync` for the initial create path. Both methods are marked `internal` — only the reconstructor should call them. Use `InternalsVisibleTo` to expose to the test project. Depends on T006.
- [ ] T034 [US1] Implement `CreateMirrorAsync` and `UpdateMirrorAsync` in `src/Services/Sorcha.Blueprint.Service/Storage/EfCoreInstanceStore.cs`. `CreateMirrorAsync` inserts a row with `IsReadOnlyMirror = true`. `UpdateMirrorAsync` advances an existing mirror row (advance-only — must not reset `CurrentActionIds`). Depends on T033.
- [ ] T035 [US1] Add the precondition check to the existing `EfCoreInstanceStore.UpdateAsync` method: before applying the update, load the existing entity and check `IsReadOnlyMirror`; if true and the caller is not the reconstructor pathway, throw `InvalidOperationException` with the message from `contracts/instance-mirror-reconstructor.md` §Read-only mirror write guard. Depends on T034.
- [ ] T036 [US1] Create `InstanceMirrorReconstructor` background service in `src/Services/Sorcha.Blueprint.Service/Services/Implementation/InstanceMirrorReconstructor.cs`. Extends `BackgroundService`. Subscribes to Redis `docket:confirmed` pub/sub channel. Full shape in `contracts/instance-mirror-reconstructor.md` §Interface. Depends on T034, and on `IWalletServiceClient.GetWalletsByOwnerAsync` from PR #288 (already merged).
- [ ] T037 [US1] Implement the reconstruction logic per `contracts/instance-mirror-reconstructor.md` §Reconstruction rules: trust check on `ValidatorConfirmations >= 1`, locally-owned wallet check, create-or-advance, idempotency on replay. Depends on T036.
- [ ] T038 [P] [US1] Create `InstanceMirrorMetrics.cs` alongside the reconstructor with the OpenTelemetry counters from `contracts/instance-mirror-reconstructor.md` §Metrics. Depends on T036.
- [ ] T039 [US1] Register `InstanceMirrorReconstructor` as a hosted service in `src/Services/Sorcha.Blueprint.Service/Program.cs` via `AddHostedService<InstanceMirrorReconstructor>()`. Follow the existing `TransactionLifecycleEventBridge` registration pattern. Depends on T037, T038.
- [ ] T040 [P] [US1] Unit tests for `InstanceMirrorReconstructor` in `tests/Sorcha.UI.Core.Tests/` (not Blueprint.Service.Tests): happy path create, happy path advance, skip on no local wallet, skip on unconfirmed tx, blueprint missing logs warning and skips, replay is idempotent. Mock `IRegisterServiceClient`, `IWalletServiceClient`, `IBlueprintCache`, `IInstanceStore`. Depends on T037.
- [ ] T041 [P] [US1] Unit test for the `UpdateAsync` write guard in the same test file: attempt to call `UpdateAsync` on a mirror row from a non-reconstructor code path → assert `InvalidOperationException` is thrown. Depends on T035.

### Wave E — UI surfaces (binds FR-009, holder visibility)

- [ ] T042 [US1] Wire the `MyCredentials` PENDING tab to `?status=PendingAcceptance` filter in `src/Apps/Sorcha.UI/Sorcha.UI.Web.Client/Pages/MyCredentials.razor`. The tab already exists (screenshot 08 from the debug trace); it just needs the data binding. Use the existing credentials client from `Sorcha.UI.Core/Services/Credentials/`. Depends on T028.
- [ ] T043 [US1] Update the MyActions dispatch path in `src/Apps/Sorcha.UI/Sorcha.UI.Web.Client/Pages/MyActions.razor` — no change to the dispatch logic itself, but verify that when a pending action's backing data is populated by a mirror-reconstructed instance, the existing `CredentialOfferSchemaResolver.TryResolve` still returns a valid `CredentialOfferInfo`. This may require enriching the `PendingActionSummary` projection in `EfCoreInstanceStore.GetPendingActionsByWalletAsync` to include mirror data. Depends on T037.
- [ ] T044 [US1] Client-side accept/decline orchestration in `src/Apps/Sorcha.UI/Sorcha.UI.Core/Services/Credentials/` (new file `CredentialAcceptOrchestrator.cs` or extension to an existing file). Implements the parallel two-call flow from `contracts/holder-accept-reject-api.md` §Client-side orchestration. Handles partial failures per the contract's reconciliation section. Depends on T029, T042.
- [ ] T045 [US1] Wire the `CredentialClaimCard`'s Accept and Decline buttons to the orchestrator from T044. The card already exists from wave 14b and already works end-to-end (verified in the debug trace screenshots 06-08). File: `src/Apps/Sorcha.UI/Sorcha.UI.Core/Components/Credentials/CredentialClaimCard.razor`. Depends on T044.
- [ ] T046 [US1] Handle the `InboundActionEvent.CredentialOfferId` field in the SignalR event subscriber in `src/Apps/Sorcha.UI/Sorcha.UI.Core/Services/Events/` (find via `grep -rn "InboundActionEvent" src/Apps/Sorcha.UI/`). When non-null, refresh both MyActions and MyCredentials views. Depends on T024, T042.

### Wave F — Integration and verification tests

- [ ] T047 [US1] Playwright E2E test `HaipVerifiedCitizenRegisterNativeTests.cs` in `tests/Sorcha.UI.E2E.Tests/`: fresh public-user signup, wallet create, Action 1 submit, CLI-driven assessor approve, assert pending credential appears in MyCredentials PENDING tab within 30 seconds, click CLAIM CREDENTIAL, assert transition to ACTIVE tab. Depends on all Wave A-E tasks. **SC-002 / SC-003 assertion**: the test MUST use a wall-clock `Stopwatch` started when the assessor's approval API call returns, polling the pending-credentials endpoint at short intervals, and assert `stopwatch.Elapsed <= TimeSpan.FromSeconds(30)` when the credential first appears. Similarly, a second stopwatch started when the holder clicks CLAIM CREDENTIAL MUST assert `<= TimeSpan.FromSeconds(30)` on the issuer's instance-state-completed observation. Prose "within 30 seconds" is not enough — these are explicit assertions keyed to SC-002 and SC-003.
- [ ] T048 [US1] Integration test for the full engine → detector → mirror → UI pipeline on single-node docker-compose (test doubles as Wave F gate for User Story 2 as well). Runnable via `dotnet test tests/Sorcha.UI.E2E.Tests/ --filter "Category=Feature106_SingleNode"`. Depends on T047. **FR-017 signature verification assertion**: after the holder clicks CLAIM CREDENTIAL (or DECLINE), the test MUST fetch the resulting Action 3 execute (or reject) transaction from the register via `IRegisterServiceClient.GetTransactionAsync`, then verify the transaction signature using the holder wallet's public key via `IWalletUtilities.VerifySignature` (or whichever signature-verification primitive the existing engine uses at `ActionExecutionService.cs`). The assertion confirms the accept/reject transaction is cryptographically attributable to the holder's wallet and only the holder's wallet — it cannot be forged by a third party or by the issuer. Failure of this check is a CRITICAL test failure — the engine's existing signature verification path is the load-bearing control for FR-017 and this test is the only explicit verification.

**Checkpoint**: User Story 1 is complete. The full federated cross-node credential flow works end-to-end. User Story 2 is implicitly satisfied because single-node is a degenerate case of cross-node.

---

## Phase 4: User Story 2 — Single-node demo (Priority: P1)

**Goal**: The same flow works identically on a single-node `docker-compose up` deployment. Signup → submit → approve → accept → credential active, all without special configuration or single-node-specific code paths.

**Independent Test**: Run Scenario 1 from `quickstart.md` (single-node end-to-end quickstart). Verify every step produces the same user-visible behaviour as the cross-node case.

**Note**: User Story 2 is implicitly covered by User Story 1's implementation — single-node is a strict degenerate case. This phase exists to explicitly verify the demo path doesn't regress and to keep User Story 2's acceptance scenarios independently testable.

### Verification tasks

- [ ] T049 [US2] Run Scenario 1 of `quickstart.md` against a fresh `docker-compose up` after Wave F's tests pass. Verify every step in the quickstart completes successfully, screenshot the MyCredentials Active tab showing the credential, attach screenshots to the PR.
- [ ] T050 [P] [US2] Verify the single-node Playwright test from T047 passes without any cross-node-specific configuration. Should be a no-op because T047 was authored for single-node; this task exists to confirm the test suite is resilient.
- [ ] T051 [US2] Regression smoke: run the existing `HaipVerifiedCitizen/setup.ps1` and `HaipVerifiedCitizen/run.ps1` walkthroughs on a fresh docker-compose. Wave 14b script-based flow must still pass. (Note: these scripts use the CLI, not the browser — they verify the underlying engine end-to-end.)

**Checkpoint**: User Story 2 verified. Single-node and federated paths share the same code and both work.

---

## Phase 5: User Story 3 — External mobile wallet path preserved (Priority: P1)

**Goal**: The existing `HaipExternalWallet` target audience continues to work unchanged. No regression on the wave 14b walkthroughs, no behavioural change for external QR-scan workflows.

**Independent Test**: Run Scenario 3 from `quickstart.md` (external wallet path regression). The `HaipDrivingLicence` walkthrough must pass end-to-end without modification.

### Verification tasks

- [ ] T052 [US3] Verify no code in `src/Services/Sorcha.Blueprint.Service/Services/Implementation/ActionExecutionService.cs` `HaipExternalWallet` branch has been altered as a side-effect of Wave A's new branch. Diff the file against master baseline; only the new branch should be additive.
- [ ] T053 [US3] Run `walkthroughs/HaipDrivingLicence/setup.ps1` and `walkthroughs/HaipDrivingLicence/run.ps1` unchanged. Assert the walkthrough passes with no errors. If any step fails, T016's implementation touched shared code paths it should have left alone — investigate and fix.
- [ ] T054 [P] [US3] Regression test `HaipExternalWalletRegressionTests.cs` in `tests/Sorcha.UI.E2E.Tests/`: a Playwright test that runs the HaipDrivingLicence flow through the UI (not the CLI walkthrough). Depends on T053.
- [ ] T055 [US3] Update the `HaipDrivingLicence` blueprint template metadata to include a comment explicitly stating it uses `HaipExternalWallet` delivery mode and is the canonical example of that mode. File: `walkthroughs/HaipDrivingLicence/blueprints/driving-licence.json`.

**Checkpoint**: External wallet path verified. Both delivery modes coexist without regression.

---

## Phase 6: User Story 4 — Holder refuses or misses a credential (Priority: P2)

**Goal**: A holder explicitly declines a pending credential (or lets it expire) without leaving the issuer's instance in a dangling state. Decline is auditable and retained locally; expiry is driven by the credential's own `notValidAfter`.

**Independent Test**: Run the decline-path variant from Scenario 1 of `quickstart.md`. Verify the declined credential is retained in the wallet store with `Status = Declined` and the issuer's instance transitions to Rejected. Separately, issue a credential with a short embedded validity and let it expire; verify both sides reach a clean terminal state.

### Implementation tasks (most are already covered by US1's implementation — this phase primarily adds verification gates and UX polish)

- [ ] T056 [US4] Verify the existing blueprint engine rejection path (`RejectionConfig.IsTerminal = true` on Action 3) still works against a `SorchaLocalWallet` action — run the existing engine rejection tests. The engine's rejection machinery should be oblivious to the credential delivery mode. If tests don't exist, add a minimal unit test. Depends on T045.
- [ ] T057 [US4] UI polish on the `CredentialClaimCard` DECLINE button — verify it correctly calls the orchestrator's decline path from T044. Likely already correct; this is an explicit verification task. Depends on T045.
- [ ] T058 [P] [US4] Playwright test `CredentialDeclineFlowTests.cs` in `tests/Sorcha.UI.E2E.Tests/`: fresh instance, approve, navigate to pending credential, click DECLINE, assert local status transitions to Declined and the credential is visible in a "declined" filter view, assert issuer's instance closes as Rejected via a direct API check. Depends on T057.
- [ ] T059 [US4] Expiry check: the passive expiry rule (`PendingAcceptance → Expired` based on `notValidAfter`) is computed on read. Verify the credentials list endpoint respects this by writing a test that mints a credential with a past `notValidAfter`, stores it as `PendingAcceptance`, then reads it back and asserts the status is reported as `Expired`. Depends on T028.
- [ ] T060 [US4] Hard-delete path verification: confirm `DELETE /api/v1/wallets/{walletAddress}/credentials/{id}` still works for a declined credential. The endpoint already exists — this is a regression check, not new implementation. Depends on T029.

**Checkpoint**: Decline path complete. Audit trail retained, issuer instance closed cleanly, expiry handled by credential metadata.

---

## Phase 7: User Story 5 — Blueprint authors adopt register-native by default (Priority: P2)

**Goal**: Blueprint authors writing new credential-issuing workflows reach for `SorchaLocalWallet` as the default, guided by updated documentation and walkthrough examples. External wallet path remains documented for mobile flows.

**Independent Test**: A blueprint author unfamiliar with the feature reads `blueprint-builder` skill documentation, writes a new credential-issuing blueprint, and the credential delivery works end-to-end on first deploy without encountering OpenID4VCI terminology.

### Documentation and walkthrough migration

- [ ] T061 [US5] Update `walkthroughs/HaipVerifiedCitizen/blueprints/verified-citizen.json` Action 2 to use `"targetAudience": "SorchaLocalWallet"`. Remove the `outputMapping` from the approval route (the credential now rides in Action 2's disclosure, not Action 3's prepopulated payload). Action 3's dataSchema becomes an empty object. Depends on T016.
- [ ] T062 [US5] Update `walkthroughs/HaipVerifiedCitizen/setup.ps1` and `run.ps1` if any logic relied on inspecting `HaipExternalWallet`-specific response fields. Most should work unchanged because the walkthrough uses the CLI `Invoke-SorchaAction` helper which returns a generic action-execute response. Depends on T061.
- [ ] T063 [US5] Run the updated `HaipVerifiedCitizen` walkthrough against single-node docker-compose: `pwsh walkthroughs/HaipVerifiedCitizen/setup.ps1; pwsh walkthroughs/HaipVerifiedCitizen/run.ps1`. Assert it passes end-to-end. The holder side will land the credential in the PENDING tab; run.ps1 may need a final step to call the PATCH endpoint + Action 3 execute to complete the accept flow (matching the CLI walkthrough style). Depends on T061.
- [ ] T064 [P] [US5] Update `.claude/skills/blueprint-builder/SKILL.md` — replace the wave 14b three-action credential-claim example in the "Credential Claim Actions" section with a two-action example using `SorchaLocalWallet`. Keep a second example showing the `HaipExternalWallet` path for external wallets. Depends on T061.
- [ ] T065 [P] [US5] Update `.claude/skills/walkthrough-builder/SKILL.md` — add a note about the new MyCredentials PENDING tab being the expected holder landing page for on-platform credential flows, distinct from the external-wallet QR path. Depends on T061.
- [ ] T066 [P] [US5] Update `CLAUDE.md` "HAIP pipeline" section to distinguish on-platform vs external-wallet delivery paths. Reference the spec and design doc. Depends on T061.
- [ ] T067 [P] [US5] Update `docs/reference/API-DOCUMENTATION.md` to include the new PATCH endpoint and the status query parameter on GET credentials. Depends on T029.
- [ ] T068 [P] [US5] Add a short `README.md` to `walkthroughs/HaipVerifiedCitizen/` explaining which delivery mode the walkthrough uses and why. Depends on T061.
- [ ] T069 [US5] Create a new walkthrough `walkthroughs/HaipDrivingLicence/` README that explicitly labels it as "the canonical example of the HaipExternalWallet delivery mode" so authors who need external-wallet flows have a clear reference. Depends on T055.

**Checkpoint**: Documentation and walkthroughs reflect the new default. New blueprint authors get the register-native path automatically.

---

## Phase 8: Polish & Cross-Cutting Concerns

**Purpose**: Final verification, cross-cutting concerns, and cross-node hardening.

### Cross-node verification (the primary value of this feature)

- [ ] T070 Create `docker-compose.federation.yml` at the repo root. Defines two Sorcha nodes (`sorcha-a-*` and `sorcha-b-*`) with their own internal networks, bridged via a peer service shared channel, each exposing its own ports (node A on 8880, node B on 8881). Model after the `DistributedRegister` walkthrough pattern. This is the load-bearing artefact for the User Story 1 acceptance test.
- [ ] T071 Run Scenario 2 of `quickstart.md` (federated two-node) against the new `docker-compose.federation.yml`. Capture the cross-node observability checklist output (logs from both nodes during the flow). Attach to the PR. Depends on T070, T047. **SC-002 / SC-003 federated assertion**: record a wall-clock timestamp on node A when the assessor's Action 2 approval returns, and on node B when the credential first appears in the holder's PENDING tab. Delta MUST be `<= 30 seconds` for the run to count as passing. Repeat the same measurement for the accept round-trip (holder click on node B → issuer instance Completed on node A). If any run exceeds the target, re-run up to 5 times and compute the 95th percentile per SC-002's "95% of runs" clause. Document the observed latencies in the PR comment.
- [ ] T072 If T071 surfaces any latency or correctness issues that fail the SC-002 / SC-003 30-second target, open a follow-up task in `MASTER-TASKS.md` under Theme 6 (P2P Network & Consensus) rather than blocking Feature 106 — the peer sync timing is outside this feature's scope, and the feature's design is correct even if peer sync tuning is needed.

### Observability and metrics

- [ ] T073 [P] Verify the new metrics from T022 (`inbound_credential_detected_total`) and T038 (`instance_mirror_reconstructed_total`) appear in the Aspire dashboard when running locally. No implementation work — just a verification screenshot.
- [ ] T074 [P] Add structured log events for successful credential reception to the existing Sorcha log catalogue so operators know what to grep for when investigating credential delivery issues.

### Documentation updates

- [ ] T075 [P] Update `docs/reference/development-status.md` with Feature 106 as shipped.
- [ ] T076 [P] Update `.specify/MASTER-TASKS.md` — mark Feature 106 as complete, cross-reference the deployed state on n1.
- [ ] T077 [P] Update `MEMORY.md` (auto-memory) with the key architectural takeaway: "Feature 106 shipped — credential issuance on-platform uses recipient-encrypted disclosures via `SorchaLocalWallet` target audience. External-wallet path via `HaipExternalWallet` remains for mobile QR flows. Design doc: docs/superpowers/specs/2026-04-14-register-native-credential-delivery-design.md."

### Final polish

- [ ] T078 Code review pass for the entire PR using the `superpowers:code-reviewer` agent. Address any high-priority findings before merge.
- [ ] T079 Run `dotnet build --force` at the repo root. Zero errors, zero new warnings above the baseline.
- [ ] T080 Run `dotnet format` across modified files. Commit any formatting adjustments as a separate commit for review clarity.
- [ ] T081 Squash merge PR to master with `gh pr merge --squash` once all tasks above are complete and verified.
- [ ] T082 Trigger the `docker-publish.yml` workflow (happens automatically on master merge). Wait for completion and verify all updated images are published.
- [ ] T083 Deploy to n1 following the `network-bootstrap` skill's routine-deploy path: `az vm run-command invoke ... 'cd /opt/sorcha && docker compose -f docker-compose.yml -f docker-compose.n1.yml -f docker-compose.ports.yml pull && docker compose ... up -d'`.
- [ ] T084 Run the quickstart Scenario 1 against n1 as the final live verification gate. Screenshot the full flow from signup → pending credential → accept → active credential. Attach to a "Feature 106 live" comment on the PR.

**Checkpoint**: Feature 106 shipped. Single-node, federated, and external-wallet paths all verified working end-to-end against n1.

---

## Dependencies & Execution Order

### Phase dependencies

- **Phase 1 (Setup)**: No dependencies, starts immediately.
- **Phase 2 (Foundational)**: Depends on Phase 1 complete. **BLOCKS** all user story phases.
- **Phase 3 (US1)**: Depends on Phase 2 complete. This is the MVP — every other user story is either a subset (US2, US3) or an enhancement (US4, US5).
- **Phase 4 (US2)**: Verification only — runs against the same implementation as US1. Can run in parallel with US1's verification tasks.
- **Phase 5 (US3)**: Verification only for the external-wallet regression path. Can run in parallel with US1 once US1's Wave A is complete.
- **Phase 6 (US4)**: Light implementation (decline path polish) + verification. Can start once US1 is complete.
- **Phase 7 (US5)**: Documentation + walkthrough migration. Depends on US1 implementation but not on US4.
- **Phase 8 (Polish)**: Depends on US1 through US7 complete. Gates the merge.

### User story dependencies (within Phase 3+)

- **US1 → US2**: US2's tasks are verification-only against US1's implementation. No blocking dependency beyond "US1 must have landed the engine branch".
- **US1 → US3**: Regression verification path. Does not touch US1's implementation; US3 just confirms nothing broke.
- **US1 → US4**: Decline path is a minor extension of US1's implementation. The engine's rejection protocol and the client's decline handler both already exist (wave 14b); US4 is mostly verification tasks.
- **US1 → US5**: Documentation and walkthrough migration; depends on the engine branch being live.

### Within each user story

- **Wave A → Wave B → Wave C → Wave D → Wave E → Wave F** within US1. Each wave has internal parallelism but waves are strictly ordered.
- **Tests follow implementation within each wave**, not strictly TDD — the task list groups test tasks immediately after the implementation they exercise.

### Parallel opportunities

- All `[P]`-marked tasks in Phase 2 (foundational) can run in parallel — T004, T005, T006, T007, T011, T012, T013 are all in different files.
- Within Wave A of US1, T018 and T019 can run in parallel (both are test files).
- Wave B's T020, T022 can run in parallel (different files); T021 depends on T020.
- Wave C's T031 and T032 can run in parallel (both tests, different files).
- Wave D's T038 and T040, T041 can all run in parallel.
- Wave E's T042 and T043 can run in parallel.
- Phase 5 (US3) verification tasks T052, T054 can run in parallel.
- Phase 7 (US5) documentation tasks T064, T065, T066, T067, T068 can all run in parallel — all different files.
- Polish phase T073, T074, T075, T076, T077 can all run in parallel.

### Parallel example: User Story 1 Wave A

```bash
# Once T016 (the engine branch) is done, these can run in parallel:
Task: "T018 [P] [US1] ActionExecutionService SorchaLocalWallet unit tests"
Task: "T019 [US1] Publish-time validation unit tests"

# Wave B kick-off (can start as soon as T020 + T024 are done):
Task: "T020 [P] [US1] Create IInboundCredentialDetector interface"
Task: "T022 [P] [US1] Create InboundCredentialDetectorMetrics"
```

---

## Implementation Strategy

### MVP First (User Story 1)

1. Complete Phase 1: Setup (T001-T003)
2. Complete Phase 2: Foundational (T004-T013) — **CRITICAL, blocks all stories**
3. Complete Phase 3: User Story 1 (T014-T048) — the full cross-node implementation
4. **STOP and VALIDATE**: Run the federated quickstart (Scenario 2 of quickstart.md)
5. Deploy/demo if ready

At this checkpoint, the feature is functionally complete. User Stories 2-5 are all variants and/or documentation of US1's same implementation.

### Incremental Delivery

1. Phase 1 + 2 → Foundation ready.
2. Phase 3 (US1) → MVP complete: cross-node credential delivery works end-to-end. Deploy/demo.
3. Phase 4 (US2) → verification: single-node path confirmed non-regressed.
4. Phase 5 (US3) → verification: external-wallet path confirmed non-regressed.
5. Phase 6 (US4) → decline flow polished and verified.
6. Phase 7 (US5) → walkthrough default flipped, documentation updated, new authors onboarded to the new pattern.
7. Phase 8 (Polish) → final cross-node verification, metrics, docs, code review, merge, deploy to n1.

### Parallel team strategy

If more than one developer is available:

- **Developer 1**: Waves A + D of US1 (engine branch + mirror reconstructor — the Blueprint Service surface).
- **Developer 2**: Waves B + C of US1 (inbound detector + credential PATCH endpoint — the Wallet Service surface).
- **Developer 3**: Wave E of US1 (UI wiring + Playwright E2E).
- After Phase 3 is done, any developer can pick up Phase 4-7 in parallel.

Otherwise, for a solo implementation, the waves inside US1 must be sequential: A → B → C → D → E → F, because each later wave integrates with surfaces the earlier waves produce.

---

## Notes

- **[P] tasks = different files, no dependencies** — either independent test files or independent source files whose completion order doesn't affect correctness.
- **[Story] label maps task to its user story** for traceability against `spec.md`.
- **Each user story should be independently completable and testable** — US1 is the MVP; US2-US5 are all variants and verifications of US1's implementation.
- **Tests are written alongside or immediately after implementation**, not strictly TDD. Writing tests first is encouraged but not required.
- **Commit after each task or logical group.** Aim for reviewable atomic commits.
- **Stop at any checkpoint to validate the story independently.** MVP is the natural first stop.
- **Avoid**: vague tasks, same-file conflicts, cross-story dependencies that break independence. All tasks above have explicit file paths and cite their binding requirements.
- **Pre-existing test project rot**: New tests MUST NOT be written in `tests/Sorcha.Blueprint.Service.Tests/` or `tests/Sorcha.Validator.Service.Tests/` — both projects have pre-existing compile failures. Route all new unit tests through `tests/Sorcha.UI.Core.Tests/`, `tests/Sorcha.Wallet.Core.Tests/`, or `tests/Sorcha.UI.E2E.Tests/`. See plan.md Complexity Tracking.

## Task count summary

- **Phase 1 (Setup)**: 3 tasks
- **Phase 2 (Foundational)**: 10 tasks
- **Phase 3 (US1 — MVP)**: 35 tasks (T014-T048)
- **Phase 4 (US2 — single-node verification)**: 3 tasks
- **Phase 5 (US3 — external wallet regression)**: 4 tasks
- **Phase 6 (US4 — decline path polish)**: 5 tasks
- **Phase 7 (US5 — docs + walkthrough migration)**: 9 tasks
- **Phase 8 (Polish + merge + deploy)**: 15 tasks

**Total: 84 tasks** across 8 phases, 5 user stories, and 6 implementation waves.

**Parallel opportunities identified**: 26 tasks marked `[P]` can run in parallel with their peers within the same wave/phase.

**Independent test gates**:

- US1: Scenario 2 of quickstart.md (federated two-node)
- US2: Scenario 1 of quickstart.md (single-node)
- US3: Scenario 3 of quickstart.md (external wallet)
- US4: Decline-path variant of Scenario 1
- US5: Blueprint-author-writing-new-blueprint dry run against updated SKILL.md

**Suggested MVP scope**: Phase 1 + Phase 2 + Phase 3 (User Story 1). Once those 48 tasks are complete, the feature is demonstrably working end-to-end and the remaining 36 tasks are verifications, polish, documentation, and deployment.
