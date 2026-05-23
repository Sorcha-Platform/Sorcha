# Tasks: Cross-node submission round-trip (Stage 5)

**Input**: Design documents from `/specs/137-cross-node-submission/`
**Prerequisites**: plan.md, spec.md, research.md, data-model.md, contracts/, quickstart.md

**Tests**: Included — the spec's SC-005 makes unit + single-node integration tests a delivery gate (the cross-node E2E is Tier 2, deferred to the machine with the genesis key).

**Organization**: By user story. US1 (P1) = submission seals on the owner; US2 (P2) = approved application returns a credential to the local wallet; US3 (P3) = restart-free register pickup.

Component map (from plan.md): C1 blueprint resolution · C2 event-driven recovery · C3 open-participant key delivery · C4 fan-out config · C5 mirror-instance submission fix.

## Format: `[ID] [P?] [Story] Description`

- **[P]**: parallelizable (different files, no incomplete dependency)
- Paths are repo-relative.

---

## Phase 1: Setup

- [ ] T001 Confirm branch `137-cross-node-submission` and design artifacts are present; no new project — brownfield extension of existing services per `specs/137-cross-node-submission/plan.md`.
- [ ] T002 [P] Add the `walkthroughs/AssuredIdentity/blueprints/assured-identity.json` worked-example blueprint scaffold (starting action + analyst decision + credential-issuance to `SorchaLocalWallet`), WITHOUT the `holderKeys` field yet (added in US2/T026).

---

## Phase 2: Foundational (blocking prerequisites)

**⚠️ Complete before user-story phases.** Light for this brownfield feature — shared test harness + telemetry only.

- [ ] T003 [P] Create the single-node integration test harness base for cross-node behaviours (published-only blueprint fixture, mirror-instance fixture) under `tests/Sorcha.Blueprint.Service.Tests/CrossNode/`.
- [ ] T004 [P] Register the OTel instruments used by later phases on the appropriate meters (recovery-on-event, key-resolution outcome, fan-out attempt) — stubs in `Sorcha.Blueprint.Service`/`Sorcha.Wallet.Service` metrics types so phases can record without re-wiring.

---

## Phase 3: User Story 1 — Submission reaches and seals on the owner (P1) 🎯 MVP

**Goal**: A citizen on a replica creates an instance from a replicated (published-only) blueprint and submits the starting action, which fans out to and seals on the owner node.

**Independent test**: On a replica with the register replicated, create an instance and submit the starting action; verify a sealed docket appears on the owner and replicates back — no service restart.

### Implementation (C1, C4)

- [ ] T005 [US1] Make `CreateInstance` published-store-aware in `src/Services/Sorcha.Blueprint.Service/Program.cs` (~:1873): on `IBlueprintStore.GetAsync` miss, fall back to `IPublishedBlueprintStore.GetVersionsAsync(blueprintId)` latest-by-`PublishedAt`; return a typed "register syncing" result (not bare 400) when neither resolves.
- [ ] T006 [US1] Gate `PublishBlueprintToRegisterAsync` (~:1890) on node-ownership via the already-injected `registerClient.GetLocalRelationshipAsync(registerId)` — skip the publish unless `IsOwner == true`; treat null relationship as not-owner (skip, do not hard-fail).
- [ ] T007 [P] [US1] Configure `IPeerServiceClient` BaseAddress on the Blueprint Service so F108 `DistributeTransactionAsync` reaches the peer service (resolve the per-node config key; `src/Services/Sorcha.Blueprint.Service` service-client wiring + appsettings). Removes the `BaseAddress must be set` warning.

### Tests (C1, C4, C5-seal)

- [ ] T008 [P] [US1] Unit: `CreateInstance` resolves a published-only blueprint (draft store empty) → instance created, no 400; in `tests/Sorcha.Blueprint.Service.Tests/`.
- [ ] T009 [P] [US1] Unit: publish-gate — `IsOwner=false` ⇒ `PublishBlueprintToRegisterAsync` NOT called; `IsOwner=true` ⇒ called; null relationship ⇒ skipped; in `tests/Sorcha.Blueprint.Service.Tests/`.
- [ ] T010 [US1] Integration (single-node, in `tests/Sorcha.Blueprint.Service.Tests/CrossNode/`): create instance from published-only blueprint + submit starting action → fan-out attempted (peer client invoked) and the local-origin starting tx is accepted by the validator path (F103 starting-action wallet-check skip + signature verify). Asserts FR-005/006/007.

**Checkpoint**: US1 independently testable — the write hop crosses nodes.

---

## Phase 4: User Story 2 — Approved application returns a credential to the local wallet (P2)

**Goal**: The analyst on the owner approves the sealed application; a credential bound + encrypted to the citizen's keys is delivered to the citizen's local wallet automatically.

**Independent test**: Given a sealed application carrying the citizen's holder keys, drive the analyst approval; the `AssuredIdentityCredential` appears in the citizen's local wallet, decryptable only by them, with no manual key entry.

### C5 — mirror-instance submission fix (locked: Fix 1a + 2a)

- [ ] T011 [US2] Emit `NextActionId` in the authoritative tx-metadata projection in `src/Services/Sorcha.Validator.Service/Services/DocketBuildTriggerService.cs` (~:593-608) — the next action id implied by the sealed tx's routing.
- [ ] T012 [US2] Seed the mirror's `CurrentActionIds` from the now-populated `NextActionId` in `src/Services/Sorcha.Blueprint.Service/Services/Implementation/InstanceMirrorReconstructor.cs` (~:264,273).
- [ ] T013 [US2] Make `ActionExecutionService.ExecuteAsync` mirror-aware in `src/Services/Sorcha.Blueprint.Service/Services/Implementation/ActionExecutionService.cs` (~:1016/1626): when the instance `IsReadOnlyMirror`, advance via `UpdateMirrorAsync` (register-driven) instead of the guarded `UpdateAsync`. Do NOT relax the store guard (Fix 2b rejected).
- [ ] T014 [P] [US2] Unit: with `NextActionId` populated, the mirror seeds `CurrentActionIds`; a mirror-targeted submission advances without throwing the read-only guard; `DocketBuildTriggerService` regression for `NextActionId`. In `tests/Sorcha.Blueprint.Service.Tests/` + `tests/Sorcha.Validator.Service.Tests/`.

### C3 server — bound issuance + recipient-key precedence

- [ ] T015 [US2] Add `HolderJwk` to `IssueCredentialRequest` in `src/Services/Sorcha.Wallet.Service/Endpoints/CredentialEndpoints.cs` (~:813-877) and pass it into `SdJwtService.CreateTokenAsync(holderJwk:)` (~:684-694) so the SD-JWT carries `cnf`.
- [ ] T016 [US2] Thread `HolderJwk` through `IWalletServiceClient.IssueCredentialAsync` in `src/Common/Sorcha.ServiceClients.Http/Wallet/`.
- [ ] T017 [US2] Add `CredentialIssuanceConfig.HolderKeySourceField` (JSON Pointer, default `/holderKeys/holderJwk`) in `src/Common/Sorcha.Blueprint.Models/Credentials/`.
- [ ] T018 [US2] Implement recipient-key precedence in `ActionExecutionService.IssueCredentialFromActionAsync` (`…/ActionExecutionService.cs` ~:1943-2060): (1) published participant record → (2) carried `holderKeys` via `TryResolveJsonPointer` → (3) fail closed. Inject the carried `encryptionPublicKey` into `request.ExternalRecipientKeys` only when the register lookup misses (honour "published wins"); feed `holderJwk` to T015's path.
- [ ] T019 [US2] Build-time validation check: confirm `src/Core/Sorcha.Blueprint.Engine/Implementation/SchemaValidator.cs` (no `x-` strip) is not on the action-data path for the `holderKeys` field; if it is, apply the same strip used at `ValidationEngine.cs:1860-1892`.
- [ ] T020 [P] [US2] Unit: `cnf` is set when `holderJwk` supplied; recipient precedence (published→carried→fail-closed); fail-closed issues NO credential. In `tests/Sorcha.Wallet.Service.Tests/` + `tests/Sorcha.Blueprint.Service.Tests/`.

### C3 client — derived-key field capture

- [ ] T021 [US2] Add `ControlTypes.HolderKey` enum member in `src/Common/Sorcha.Blueprint.Models/Control.cs` (~:193, mirror `PostcodeLookup`).
- [ ] T022 [US2] Map `format == "sorcha-holder-key"` → `ControlTypes.HolderKey` in `src/Apps/Sorcha.UI/Sorcha.UI.Components.User/Services/User/Forms/FormSchemaService.cs` (~:376-399).
- [ ] T023 [US2] Dispatch `ControlTypes.HolderKey` → `HolderKeyRenderer` in `src/Apps/Sorcha.UI/Sorcha.UI.Components.User/Components/Forms/ControlDispatcher.razor` (~:65).
- [ ] T024 [US2] New Wallet-Service endpoint `GET /api/v1/wallet/holder-keys` (consumer-tier) in `src/Services/Sorcha.Wallet.Service/Endpoints/CitizenWalletEndpoints.cs` returning holder JWK + X25519 pubkey + algorithm, per `contracts/holder-keys-endpoint.openapi.yaml`; add the X25519 public-key accessor on `HolderKeyService` (`…/HolderKeyService.cs`). OpenAPI summary/description + XML docs.
- [ ] T025 [US2] New `HolderKeyRenderer.razor` in `src/Apps/Sorcha.UI/Sorcha.UI.Components.User/Components/Forms/Controls/` — on init, call the new endpoint (via a thin client in `src/Common/Sorcha.ServiceClients.Http/`/PWA service) and write `/holderKeys/{holderJwk,encryptionPublicKey,algorithm}` via `FormContext.SetValue`; render read-only. Per `contracts/sorcha-holder-key-field.md`.
- [ ] T026 [US2] Add the `holderKeys` (`format: sorcha-holder-key`) field to the starting action of `walkthroughs/AssuredIdentity/blueprints/assured-identity.json` and point `credentialIssuanceConfig.HolderKeySourceField` at it.
- [ ] T027 [US2] Wire the real `SorchaFormRenderer` submit path into `src/Apps/Sorcha.Wallet.Pwa/Pages/ApplicationInstance.razor` (replace the placeholder) and implement the real submission in `src/Apps/Sorcha.Wallet.Pwa/Services/Applications/IApplicationSubmissionService.cs` (replace `StubApplicationSubmissionService`).
- [ ] T028 [P] [US2] Unit: `FormSchemaService` maps the format; `HolderKeyRenderer` writes the three nested pointers from a stubbed endpoint response; `x-holder-key` passes validation. In `tests/` for `Sorcha.UI.Components.User` + blueprint models.

### Integration (US2)

- [ ] T029 [US2] Integration (single-node, `tests/Sorcha.Blueprint.Service.Tests/CrossNode/`): issue a credential to a recipient supplied **by public key** (no local wallet row) → on-register envelope wrapped to that key and decryptable; `cnf` binds the holder JWK; analyst approval advances the mirror instance. Asserts FR-012/013/014 + the C5 path.

**Checkpoint**: US1 + US2 = the full round-trip is implemented (single-node verified; cross-node deferred to Tier 2).

---

## Phase 5: User Story 3 — Restart-free register pickup (P3)

**Goal**: A register subscribed after node start yields usable blueprints without a restart.

**Independent test**: On a running replica, subscribe a register post-boot, then immediately create an instance from its blueprint — succeeds, no restart.

### Implementation (C2)

- [ ] T030 [US3] Add `RegisterEventChannels.RegisterCreated = "register:created"` in `src/Core/Sorcha.Register.Core/Events/RegisterEventChannels.cs` and replace the inline literals at `Sorcha.Register.Core/Managers/RegisterManager.cs:85` and `RegisterEventBridgeService.cs:35`.
- [ ] T031 [US3] Expose a per-register recovery entrypoint in `src/Services/Sorcha.Blueprint.Service/Services/Implementation/BlueprintRecoveryService.cs` (refactor the private `RecoverFromRegisterAsync` to open its own scope by `registerId`).
- [ ] T032 [US3] Subscribe `BlueprintRecoveryService` to `register:created` (mirror `InstanceMirrorReconstructor` subscription pattern; guard Redis-unavailable with a degrade log) and call the per-register recovery on each event; keep the periodic safety-net loop.
- [ ] T033 [P] [US3] Unit: a `register:created` event recovers exactly that register's published blueprints; Redis-unavailable degrades cleanly; the periodic safety net still recovers a missed register. In `tests/Sorcha.Blueprint.Service.Tests/`.

**Checkpoint**: US3 independently testable — removes the manual-restart workaround.

---

## Phase 6: Polish & cross-cutting

- [ ] T034 [P] Wire the OTel counters recorded in T004 at their call sites (recovery-on-event in C2, key-resolution outcome published/carried/fail-closed in C3, fan-out attempt in C4); structured logging, no interpolation.
- [ ] T035 [P] Author the Tier-2 cross-node scripted verification procedure in `walkthroughs/AssuredIdentity/` (per `quickstart.md` §Tier 2) so the live n1↔local run is turn-key on the genesis-key machine.
- [ ] T036 [P] Docs sync: add the Feature 137 surface (new endpoint, `sorcha-holder-key` field, C5 mirror-submit, recovery event) to `.claude/skills/sorcha-architecture/SKILL.md`, `docs/reference/API-DOCUMENTATION.md`, and the affected service READMEs.
- [ ] T037 [P] Update `docs/superpowers/specs/2026-05-23-cross-node-submission-design.md` with the two research corrections (C5 structural; C3 server-side autofill + cnf) referencing `research.md`, and reconcile `specs/137-cross-node-submission/spec.md` if any requirement shifted.
- [ ] T038 Full `dotnet test` green on the build machine (SC-005 Tier-1 gate); confirm no new build warnings.

---

## Dependencies & execution order

- **Setup (P1-phase)**: T001 → T002.
- **Foundational**: T003, T004 (after Setup; block user stories).
- **US1 (P1)**: T005, T006 (same file `Program.cs` — sequential), T007 [P]; tests T008/T009 [P], T010 after T005-T007.
- **US2 (P2)**: depends on US1 for a sealed application to approve. C5 (T011→T012, T013; T014) → enables analyst submit. C3 server (T015→T016, T017, T018; T019; T020) and C3 client (T021→T022→T023; T024; T025; T026; T027; T028) can proceed in parallel tracks. T029 after C5 + C3 land. Note: T013, T018 both edit `ActionExecutionService.cs` → sequential.
- **US3 (P3)**: T030 → T031 → T032; T033. Fully independent of US1/US2 (can be built any time after Foundational).
- **Polish**: T034-T038 after their source phases; T038 last.

## Parallel opportunities

- **Across stories**: US3 (T030-T033) is independent of US1/US2 — a second track can build it in parallel.
- **Within US1**: T007, T008, T009 in parallel.
- **Within US2**: the C3-server track (T015-T020) and C3-client track (T021-T028) run in parallel; C5 (T011-T014) is a third parallel track. Watch the two shared-file constraints (`ActionExecutionService.cs`: T013 & T018; `Program.cs`: T005 & T006).
- **Polish**: T034-T037 in parallel.

## Implementation strategy

- **MVP = US1** (the write hop). Ship/verify it first: a replica-origin submission seals on the owner.
- **US2** completes the user-visible value (credential returns). It is the largest phase (C3 + C5).
- **US3** is operability hardening; independently shippable.
- **Tier-1 gate (SC-005)**: T038 green + T035 committed. **Tier-2** (SC-001/002/003/004 live) runs on the separate cross-node machine.
