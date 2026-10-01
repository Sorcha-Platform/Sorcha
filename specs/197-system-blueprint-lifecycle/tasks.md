# Tasks: System Blueprint Lifecycle (Feature 197)

**Input**: `specs/197-system-blueprint-lifecycle/` — plan.md, spec.md, research.md, data-model.md, contracts/, quickstart.md
**Design**: `docs/superpowers/specs/2026-10-01-system-blueprint-lifecycle-design.md`
**Issue**: #1466

## How to execute (subagent-driven)

- **One implementer at a time**, on this branch, in task order. Each task = its source file(s) + its test
  file, then `dotnet build` and the named test project(s) green, then **commit** (`feat: [197 TNNN] - …`,
  stage explicit paths only — never `git add -A`).
- **Every guard has a counterfactual.** Each task states a *Mutation check*: temporarily make that change,
  confirm the named test goes **red**, revert. Report the red in the hand-back. A guard whose test stays
  green under its mutation is not done.
- Tests: xUnit v3 (MTP): `dotnet test --project tests/<Project>/<Project>.csproj --filter-class "*Name*"`.
- Conventions: SPDX header (CLAUDE.md §7), file-scoped namespaces, `typeof(X).FullName!`, structured logs
  (no interpolation), `.WithSummary/.WithDescription`, never `.Produces<object>` on new endpoints
  (produces-object-gate), derivation contexts via `SorchaDerivationPaths` (§15).
- Absolute repo root: `C:\Projects\Sorcha`. Paths below are repo-relative.

## Format: `[ID] [P?] [Story] Description`

---

## Phase 1: Setup

- [x] T001 Register the new meter `Sorcha.SystemBlueprints` in the OpenTelemetry export list in `src/Common/Sorcha.ServiceDefaults/Extensions.cs` (beside `metrics.AddMeter("Sorcha.Provenance")`). Add a constant `SystemBlueprintMetrics.MeterName = "Sorcha.SystemBlueprints"` in new file `src/Services/Sorcha.Register.Service/Services/SystemBlueprintMetrics.cs` (instruments added in later tasks). If `tests/Sorcha.ServiceDefaults.Tests` has a test enumerating exported meters, extend it. *Mutation check:* remove the `AddMeter` line → that test (if it exists) reds; otherwise state "no meter test exists" in the hand-back.

---

## Phase 2: Foundational (blocks all stories)

- [x] T002 Create `src/Common/Sorcha.Register.Models/SystemBlueprintCatalog.cs`: `public static class SystemBlueprintCatalog` with `IReadOnlyList<string> Ids` = `register-creation-v1`, `register-governance-v1`, `create-organisation-v1`, `join-private-register-v1` (this order — seed order) and `GovernanceBlueprintId => GovernanceBlueprint.BlueprintId`. Test `tests/Sorcha.Register.Models.Tests/SystemBlueprintCatalogTests.cs`: order pinned; contains `GovernanceBlueprint.BlueprintId`; every id has a file `blueprints/templates/{id}.json` (locate repo root by walking up to `Sorcha.sln`/`.git`). *Mutation check:* drop `join-private-register-v1` → template-coverage + order test red.

- [x] T003 Extract catalogue loading from `SystemRegisterBootstrapper.LoadBlueprintFromCatalog` into new `src/Services/Sorcha.Register.Service/Services/SystemBlueprintCatalogSource.cs` (`ISystemBlueprintCatalogSource`: `JsonElement? TryLoad(string id)`, `string? TryComputePublicationId(string id)` = `BlueprintPublicationId.Compute(SystemRegisterConstants.SystemRegisterId, id, BlueprintCanonicalJson.Canonicalise(json))` — use the SAME canonicalisation `SystemRegisterService.PublishBlueprintAsync` uses, so the id equals what a publish would produce). Change `SystemRegisterBootstrapper.SeedBlueprintsIfMissingAsync` to iterate `SystemBlueprintCatalog.Ids` and use the source; register the source as a singleton in `Program.cs`. Test `tests/Sorcha.Register.Service.Tests/Services/SystemBlueprintCatalogSourceTests.cs`: the id computed by the source equals the `TransactionId` that `SystemRegisterService.PublishBlueprintAsync` submits for the same template (capture the submission via a mocked `IValidatorServiceClient`). *Mutation check:* compute over the raw (non-canonical) JSON → equality test red.

- [x] T004 Create `src/Services/Sorcha.Register.Service/Services/SystemBlueprintCurrency.cs`: pure `static IReadOnlyList<TransactionModel> OrderByLedger(IEnumerable<TransactionModel> publications, Func<ulong, DocketHeader?> docketLookup, ILogger)` — sort by `DocketNumber` asc, ties by index of `TxId` in `DocketHeader.TransactionIds`; publications with null `DocketNumber` are excluded and logged (structured). Plus `Current(...)` (last) and `VersionOf(txId, ...)` (1-based ordinal). `TimeStamp` is never read. Test `tests/Sorcha.Register.Service.Tests/Services/SystemBlueprintCurrencyTests.cs`: (a) later-sealed publication with an EARLIER `TimeStamp` is current; (b) two in one docket ordered by `TransactionIds` index, not insertion order or timestamp; (c) null-docket excluded; (d) versions 1..n. *Mutation check:* order by `TimeStamp` → (a) red; ignore in-docket index → (b) red.

- [x] T005 Wire T004 into `src/Services/Sorcha.Register.Service/Services/SystemRegisterService.cs`: `GetBlueprintAsync`, `GetAllBlueprintsAsync` and the version computation in `PublishBlueprintAsync` use `SystemBlueprintCurrency` (docket lookup via the existing register/transaction managers or `IReadOnlyRegisterRepository.GetDocketAsync`, cached per call). Add `GetPublicationsAsync(blueprintId)` returning the ledger-ordered publications (needed by drift). Test `tests/Sorcha.Register.Service.Tests/Services/SystemRegisterServiceCurrencyTests.cs` with the in-memory repository: a fixture with two sealed publications where the later docket has the earlier timestamp → `GetBlueprintAsync` returns the later-docket one with `Version == 2`. Existing `SystemRegisterBlueprintPollutionTests` must stay green. *Mutation check:* restore `OrderByDescending(TimeStamp)` in `GetBlueprintAsync` → red.

**Checkpoint:** "current" is ledger-ordered everywhere the validator and register read it.

---

## Phase 3: User Story 1 — Drift is visible on every node (P1) 🎯 MVP

**Goal:** each node classifies its catalogue vs the ledger in four states and surfaces it.
**Independent test:** quickstart steps 1–2.

- [x] T006 [US1] Create `src/Services/Sorcha.Register.Service/Services/SystemBlueprintDriftReporter.cs`: enum `SystemBlueprintDriftState { InSync, ImageBehind, ImageAhead, Missing, Unknown }`, record `SystemBlueprintDriftEntry` (fields per data-model.md), pure `static Classify(string? imagePublicationId, IReadOnlyList<TransactionModel> orderedPublications)`, and `ISystemBlueprintDriftReporter.ComputeAsync(ct)` over `SystemBlueprintCatalog.Ids` using `ISystemBlueprintCatalogSource` + `SystemRegisterService.GetPublicationsAsync`; any read exception for a blueprint → `Unknown` for that entry (never `InSync`). Test `tests/Sorcha.Register.Service.Tests/Services/SystemBlueprintDriftReporterTests.cs`: all five states incl. `ImageBehind` reports the matching older version; thrown read → `Unknown`. *Mutation check:* classify "matches any publication" as `InSync` → ImageBehind test red; swallow exception as `InSync` → Unknown test red.

- [x] T007 [US1] Create `src/Services/Sorcha.Register.Service/Services/SystemBlueprintDriftMonitor.cs`: `BackgroundService` computing at startup (after the system register exists — reuse the bootstrapper's readiness signal or poll `GetRegisterAsync`) and every 10 minutes (`IOptions`-configurable `SystemBlueprints:DriftIntervalMinutes`); holds the latest report (`ISystemBlueprintDriftSnapshot`, singleton); logs one structured `LogWarning` per non-`InSync` entry; publishes observable gauge `sorcha_system_blueprint_drift{blueprint,state}` (value 1 for the current state) on `SystemBlueprintMetrics.MeterName`. Register in `src/Services/Sorcha.Register.Service/Program.cs`. Test `tests/Sorcha.Register.Service.Tests/Services/SystemBlueprintDriftMonitorTests.cs` (fake reporter, `TimeProvider`): snapshot populated after first run; warning logged for drift and not for in-sync; gauge emits via `MeterListener`. *Mutation check:* skip the log for `ImageAhead` → red.

- [x] T008 [US1] Create `src/Services/Sorcha.Register.Service/Services/SystemBlueprintsHealthCheck.cs` (`IHealthCheck`, name `system-blueprints`) mapping the snapshot per data-model.md "Health status mapping" — `Missing` is Healthy on `BootstrapMode.SyncOnly`, Degraded on owner after bootstrap; no snapshot yet → Healthy with description "not yet computed". Register via `AddHealthChecks().AddCheck<…>("system-blueprints")` in `Program.cs`. Test `tests/Sorcha.Register.Service.Tests/Services/SystemBlueprintsHealthCheckTests.cs`: one case per mapping row; never `Unhealthy`. *Mutation check:* treat SyncOnly `Missing` as Degraded → red; return Healthy for `Unknown` → red.

- [x] T009 [US1] Add `GET /api/system-register/drift` in `src/Services/Sorcha.Register.Service/Endpoints/SystemRegisterEndpoints.cs` returning the snapshot (compute on demand if none yet) per `contracts/system-register-operator.openapi.yaml`; `.RequireAuthorization("RequireSystemAdmin", "RequirePlatformAudience")` on this endpoint (the group's `CanManageRegisters` stays for other routes); state serialised by the platform SorchaJson KebabCaseLower convention (`"image-ahead"`; ruling 2026-10-01); `.Produces<SystemBlueprintDriftReport>`. Test `tests/Sorcha.Register.Service.Tests/Endpoints/SystemRegisterDriftEndpointTests.cs`: SystemAdmin+platform → 200 with `"state":"image-ahead"` string; org `Administrator` → 403; consumer-tier SystemAdmin → 403; unauthenticated → 401. *Mutation check:* remove `RequirePlatformAudience` → consumer test red.

**Checkpoint:** US1 shippable alone.

---

## Phase 4: User Story 2 — Governance changes keep their definition (P1)

**Goal:** proposals pin the governance definition; approvals/enactment inherit; validator enforces.
**Independent test:** quickstart steps 3, 5, 6 + the v4/v5 counterfactual test (T015).

- [x] T010 [US2] Add to `ControlTransactionPayload` in `src/Common/Sorcha.Register.Models/GovernanceModels.cs`: `[JsonPropertyName("governanceDefinitionTxId")] [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] public string? GovernanceDefinitionTxId { get; init; }` with XML doc (signed, proposals only, why omit-when-null). Test `tests/Sorcha.Register.Models.Tests/Governance/ControlTransactionPayloadPinWireTests.cs`: a genesis-shaped and an enactment-shaped payload serialised with `CanonicalJsonOptions` are byte-identical to fixed expected strings captured BEFORE the change (write the expected strings first, from the pre-change serialisation) and contain no `governanceDefinitionTxId`; a proposal with a pin round-trips. *Mutation check:* remove the `JsonIgnore` → byte-identity test red.

- [x] T011 [US2] Declare the field in action 1 of `blueprints/templates/register-governance-v1.json`: `"governanceDefinitionTxId": { "type": "string", "pattern": "^[0-9a-f]{64}$", "description": "…" }`, NOT in `required`. Update `tests/Sorcha.Register.Models.Tests/GovernanceControlPayloadContractTests.cs` so its maximal proposal sets the pin (the bidirectional checks then cover it); add a case: the sealed n1 golden vectors (`Fixtures/Governance/*-sealed-n1.json`, no pin) still conform; a pin that is not 64 lowercase hex is refused by `ValidatorSchemaEvaluation`. If any other test pins the template's content/hash and fires, regenerate it only after confirming this change is the cause. *Mutation check:* add the field to `required` → golden-vector conformance red; drop it from `properties` → "every emitted field is declared" red.

- [x] T012 [US2] Create `src/Common/Sorcha.Register.Models/GovernanceDefinitionPin.cs` (beside `GovernanceAuthorisationValidator.cs`), pure, no I/O: `static PinResolution Resolve(int actionId, ControlTransactionPayload? ownPayload, ControlTransactionPayload? referencedProposal, bool referencedProposalReadable)` returning `Pinned(txId, IsRaise)` / `Legacy(step)` / `Unresolvable(reason)` per the table in `contracts/governance-definition-pin.md`; own pin wins over a reference (owner-override). Use `GovernanceBlueprint` action-id constants. Test `tests/Sorcha.Register.Models.Tests/Governance/GovernanceDefinitionPinTests.cs`: every table row; owner-override (own pin + `EnactsProposalId` null + roster set) → `Pinned(own, IsRaise: true)`; approval → proposal's pin with `IsRaise: false`. *Mutation check:* check the reference before the own pin → owner-override test red; return `Legacy` for an unreadable proposal → red.

- [x] T013 [US2] Producer: create `src/Services/Sorcha.Register.Service/Services/GovernanceDefinitionPinSource.cs` (`IGovernanceDefinitionPinSource.GetCurrentAsync(ct)` → current SSR publication id of `SystemBlueprintCatalog.GovernanceBlueprintId` via `SystemRegisterService.GetBlueprintAsync`, uncached; null if unreadable). In `src/Services/Sorcha.Register.Service/Program.cs` set `GovernanceDefinitionTxId` at the two proposal construction sites (pending proposal ≈ line 2632; Owner-override ≈ line 2740 — locate by `new ControlTransactionPayload`); if the source returns null, return `Results.Problem(503, "governance definition unavailable")` and submit nothing. Do NOT touch `GovernanceEnactmentService` or `RegisterCreationOrchestrator`. Test `tests/Sorcha.Register.Service.Tests/Governance/GovernanceProposalPinStampTests.cs`: the submitted payload (captured from mocked `IValidatorServiceClient`) carries the current id for both paths; source null → 503 and no submission; `GovernanceEnactmentService.BuildEnactmentPayload` output has no pin. *Mutation check:* stamp only on the pending path → owner-override assertion red.

- [x] T014 [US2] Validator SSR resolution arm in `src/Services/Sorcha.Validator.Service/Services/ValidationEngine.cs` `ResolvePinnedBlueprintAsync`: after the transaction's-own-register read misses (null tx/payload) and `registerId != SystemRegisterConstants.SystemRegisterId`, read the tx from the SSR via `_registerClient.GetTransactionAsync(SystemRegisterId, definitionTxId)`, decode as the existing arm does, and verify `BlueprintPublicationId.ComputeFromDefinition(SystemRegisterId, blueprintId, json) == definitionTxId`; on mismatch log error and return null; on success cache with `SetDefinitionAsync`. Update the "no system-register arm" comment. Test `tests/Sorcha.Validator.Service.Tests/Services/ValidationEngineSystemRegisterPinTests.cs`: definition present only on the SSR → resolves; SSR payload tampered → null (→ `VAL_BP_VERSION_001`); present on own register → SSR never queried. *Mutation check:* delete the arm → first test red; skip the id recompute → tamper test red.

- [x] T015 [US2] In `ValidationEngine.ValidateSchemaAsync` (≈ line 624, where `carriedPin = ReadCarriedExecDefHash(transaction)`): when `TransactionTypeClassifier.IsGovernanceActionTransaction(transaction)`, derive the pin via `GovernanceDefinitionPin.Resolve` — parse the tx's own payload as `ControlTransactionPayload` (actions 1/record-control), and for an approval fetch the proposal at `PreviousTransactionId`, for an enactment the proposal at payload `EnactsProposalId`, via `_registerClient.GetTransactionAsync(registerId, …)`. `Pinned` → `ResolveBlueprintAsync(…, pin, …)`; `Unresolvable` → fatal `VAL_BP_VERSION_001`; `Legacy` → latest + increment counter `sorcha_governance_definition_pin_fallback{step}` on the existing `Sorcha.Validator` meter. Test `tests/Sorcha.Validator.Service.Tests/Services/ValidationEngineGovernancePinTests.cs` — **the key counterfactual**: definitions v4 and v5 of `register-governance-v1` where v5's action-2 schema refuses an approval payload v4 accepts; proposal pinned to v4; with v5 current, the approval validates; the SAME approval with the pin lookup bypassed (latest) fails `VAL_SCHEMA_*`. Plus: legacy proposal → counter incremented; unreadable proposal → `VAL_BP_VERSION_001`. *Mutation check:* revert to `ReadCarriedExecDefHash` for governance → counterfactual test red.

- [x] T016 [US2] `VAL_GOV_DEF_001` at raise. Add `Task<string?> GetSystemRegisterBlueprintPublicationIdAsync(string blueprintId, CancellationToken)` to `IRegisterServiceClient` / `src/Common/Sorcha.ServiceClients.Http/Register/RegisterServiceClient.cs` (reads `SystemRegisterEntry.PublicationTransactionId` from `GET api/system-register/blueprints/{id}`, uncached, null on non-2xx). In `ValidationEngine.ValidateSchemaAsync`, when the resolution is `Pinned(IsRaise: true)` and the pin ≠ current id (current non-null) → fatal `VAL_GOV_DEF_001` ("raised under a superseded governance definition"), declared as a local const beside the validator's other codes (§16: no second project names it). Current unreadable → fatal `VAL_BP_VERSION_001`-style refusal, not acceptance. Approvals/enactments are never compared to current. Test in `tests/Sorcha.Validator.Service.Tests/Services/ValidationEngineGovernancePinTests.cs` (extend T015's file): superseded raise → `VAL_GOV_DEF_001`; current raise → passes; approval of a v4 proposal after v5 current → passes; client test in `tests/Sorcha.ServiceClients.Tests` (or the existing RegisterServiceClient test file) for the new method. *Mutation check:* apply the check to approvals → approval test red; remove the check → superseded test red.

- [x] T017 [US2] Proposal audit view: in `src/Services/Sorcha.Register.Service/Services/GovernanceProposalViewService.cs` add `GoverningDefinitionTxId` (string?) and `GoverningDefinitionVersion` (int?, via `SystemBlueprintCurrency`) to `GovernanceProposalView`; null + `GoverningDefinitionLegacy: true` for an unpinned proposal. Change the `/proposals/{proposalId}` endpoint's `.Produces<object>` to `.Produces<GovernanceProposalView>` only if that does not change the wire (metadata-only); update `.produces-object-allowlist` count downward if so. Test `tests/Sorcha.Register.Service.Tests/Governance/GovernanceProposalViewDefinitionTests.cs`. *Mutation check:* read the definition from the latest publication instead of the proposal's pin → red.

**Checkpoint:** US2 complete; pinning enforced end-to-end in unit tests.

---

## Phase 5: User Story 3 — Safe operator publish (P2)

**Goal:** catalogue-only, SystemAdmin-only, audited publish + CLI.
**Independent test:** quickstart step 4 + refusal checks.

- [x] T018 [US3] Remove `POST /api/system-register/publish` from `src/Services/Sorcha.Register.Service/Endpoints/SystemRegisterEndpoints.cs` (and the `PublishBlueprintRequest`/`PublishBlueprintResponse` types if they become unused); delete/replace tests that exercised it. In `SystemRegisterService.PublishBlueprintAsync` keep the signature but have bootstrap seeds pass `publishedBy: "system"` + `seedReason: "bootstrap"` (unchanged) — operator publishes will pass the operator id (T020). Test `tests/Sorcha.Register.Service.Tests/Endpoints/SystemRegisterLegacyPublishRemovedTests.cs`: `POST /api/system-register/publish` → 404/405. *Mutation check:* re-map the old route → red.

- [x] T019 [US3] Create `src/Services/Sorcha.Register.Service/Services/SystemBlueprintPublishService.cs` (`ISystemBlueprintPublishService.PublishAsync(blueprintId, dryRun, expectedCurrent, operatorId, ct)` → `PublishDecision`): order = not in catalogue → `NotFound`; this node's `sorcha:blueprint-publish` public key (from the system wallet signing service for `SorchaDerivationPaths.BlueprintPublish`) not Active on the SSR validator roster (`GetCurrentRosterAsync(SSR).ControlRecord.Validators`, match via `GovernanceKeyMatcher`) → `NoPublishingKey`; classify via drift reporter → `InSync` ⇒ `Noop`, `ImageBehind` ⇒ `RefusedRollback`; `expectedCurrent` provided and ≠ current ⇒ `RefusedConcurrency`; `dryRun` ⇒ `DryRun` (submit nothing); else `SystemRegisterService.PublishBlueprintAsync(id, catalogueJson, publishedBy: operatorId, metadata { seedReason = "operator" })` ⇒ `Submitted(txId)`. Counter `sorcha_system_blueprint_publish_total{outcome}`. Test `tests/Sorcha.Register.Service.Tests/Services/SystemBlueprintPublishServiceTests.cs`: one test per outcome; dryRun and every refusal assert the validator client was NOT called. *Mutation check:* drop the `ImageBehind` branch → rollback test red (it would submit); drop the roster check → no-key test red.

- [x] T020 [US3] Add `POST /api/system-register/blueprints/{blueprintId}/publish` in `SystemRegisterEndpoints.cs` per `contracts/system-register-operator.openapi.yaml`: `.RequireAuthorization("RequireSystemAdmin", "RequirePlatformAudience")`; operator id from `platform_user_id` (fallback `sub`); map decisions → 200 (`DryRun`/`Noop`), 202 (`Submitted`), 403 (`NoPublishingKey`), 404, 409 with `reason` `rollback|concurrency` (problem+json); for every refusal call `IRefusalAuditClient` with a new constant `RefusalAuditActions.SystemBlueprintPublish = "system-blueprint.publish"` (add to `src/Common/Sorcha.ServiceClients.Http/Audit/RefusalAuditReport.cs` where the other actions live), org from the caller's token, best-effort (never changes the response); authorisation refusals (403 from policy) are audited via the same helper in an endpoint filter or are covered by the existing refusal path — state which in the hand-back. Structured success log `SystemBlueprintPublished {BlueprintId} {PreviousTxId} {NewTxId} {Operator}`. Typed `.Produces<SystemBlueprintPublishResult>`. Test `tests/Sorcha.Register.Service.Tests/Endpoints/SystemRegisterPublishEndpointTests.cs`: consumer-tier → 403; org Administrator → 403; SystemAdmin without key → 403 + audit report captured; rollback/concurrency → 409 + audit; dryRun → 200 no submission; happy → 202. *Mutation check:* drop `RequirePlatformAudience` → consumer test red; skip the audit call → audit assertion red.

- [x] T021 [US3] CLI: add `SystemRegisterDriftCommand` (`sorcha system-register drift`) and `SystemRegisterPublishCommand` (`sorcha system-register publish <blueprintId> [--dry-run] [--expected-current <txid>]`) to `src/Apps/Sorcha.Cli/Commands/SystemRegisterCommands.cs`, wired into the `system-register` parent like the existing four; DTOs mirror the server JSON names exactly; table output via Spectre.Console; non-2xx prints the problem `detail`. Test `tests/Sorcha.Cli.Tests/Commands/SystemRegisterOperatorCommandsTests.cs`: option parsing; 409 surfaces the reason; dry-run sends `dryRun:true`. *Mutation check:* send `dry_run` instead of `dryRun` → request-body test red.

- [x] T022 [US3] Contract test: add the T021 DTO ↔ server type pairs (`SystemBlueprintDriftReport`/`SystemBlueprintDriftEntry`, `SystemBlueprintPublishResult`, publish request) to `tests/Sorcha.Cli.ContractTests` per its existing pattern (CLAUDE.md §18); baseline must stay empty. *Mutation check:* rename a CLI DTO property's JSON name → contract test red.

**Checkpoint:** US3 complete.

---

## Phase 6: User Story 4 — Upgrades propagate without manual steps (P2)

- [x] T023 [US4] Create `src/Services/Sorcha.Validator.Service/Services/SystemBlueprintCacheEvictionService.cs`: `BackgroundService` subscribing to `RegisterEventChannels.DocketConfirmed` via optional `IEventSubscriber` (copy the `RegisterMonitoringBootstrap` pattern, incl. the null-subscriber no-op); for a `DocketConfirmedEvent` with `RegisterId == SystemRegisterConstants.SystemRegisterId`, call `IBlueprintCache.RemoveAsync(id)` for each `SystemBlueprintCatalog.Ids`; counter `sorcha_system_blueprint_cache_evictions_total` on `Sorcha.Validator`; failures logged, never thrown. Register as hosted service where `RegisterMonitoringBootstrap` is registered. Test `tests/Sorcha.Validator.Service.Tests/Services/SystemBlueprintCacheEvictionServiceTests.cs`: SSR docket → 4 `RemoveAsync` calls; non-SSR docket → none; subscription is registered on start (assert the subscriber mock received `DocketConfirmed`). *Mutation check:* filter on the wrong register id / drop the subscription → red.

---

## Phase 7: Polish, docs, live gate

- [x] T024 [P] Update `src/Services/Sorcha.Register.Service/README.md`: remove the old publish row; document drift endpoint, operator publish (auth, catalogue-only, dryRun, expectedCurrent, refusals), ledger-order currency + `Version` meaning, `system-blueprints` health check, `Sorcha.SystemBlueprints` metrics, pin stamping on proposals.
- [x] T025 [P] Update `src/Services/Sorcha.Validator.Service/README.md`: SSR pin-resolution arm, governance pinning in schema validation, `VAL_GOV_DEF_001`, pin-fallback counter, cache eviction on SSR dockets.
- [x] T026 [P] Update `docs/reference/API-DOCUMENTATION.md` (system-register section: removed route, two new endpoints) and CLI docs in `src/Apps/Sorcha.Cli/README.md` (two commands).
- [x] T027 [P] Update `.claude/skills/sorcha-architecture/SKILL.md` F189 section: replace the "A node's seeded system blueprints are never updated" remediation (docker cp + raw publish + redis DEL + recreate) with the F197 operator flow; add a "Governance pinning (F197)" subsection (carrier, inheritance, `VAL_GOV_DEF_001`, SSR arm, omit-when-null bytes trap); amend the T054 note "publish the version under test" (now: build an image). Update `.claude/skills/blueprint-builder/SKILL.md` only if it references the removed route.
- [x] T028 Update `.specify/MASTER-TASKS.md` (F197 entry 🚧→✅ with PR) and `docs/reference/development-status.md`.
- [x] T029 Full `dotnet build` (0 warnings in touched projects) and `dotnet test` solution-wide; run the CI gate scripts locally: `scripts/check-produces-object.ps1`, `scripts/check-error-code-contract.ps1`, `scripts/check-derivation-contexts.ps1`, `scripts/check-secrets.ps1`. Fix, don't allowlist.
- [x] T030 Live gate on n1 + tiny (deploy pre-approved — use the `n1-deploy` skill; validator-service BEFORE register-service on both nodes): run `quickstart.md` "Live gate" steps 1–8 and record evidence (drift output, tx ids, docket containing the enactment, metric reading) in the PR. The test proposal must be raised on a fresh ordinary register with two-org quorum (CLAUDE.md F189 notes: confirm genesis is in docket 0 first; 202 ≠ enacted — verify the docket).
- [x] T031 Open the PR (`gh pr create`), link #1466, include live-gate evidence; merge on green; comment on #1466 with the outcome; update memory `next-system-blueprint-brainstorm.md`.

---

## Dependencies & order

- T001 → T002 → T003 → T004 → T005 (foundation, strictly sequential).
- US1: T006 → T007 → T008 → T009 (needs T003, T005).
- US2: T010 → T011 → T012 → T013 (needs T005) → T014 → T015 → T016 → T017.
- US3: T018 → T019 (needs T006) → T020 → T021 → T022.
- US4: T023 (needs T002 only).
- Polish: T024–T027 [P] after their stories; T028 → T029 → T030 → T031.

Single-implementer execution order: T001…T031 as numbered. (US2/US3/US4 are independent after Phase 2;
the [P] marks only matter if the one-implementer rule is ever relaxed — it is not, per the standing
"never two implementer agents on one checkout" rule.)

## Implementation strategy

MVP = Phases 1–3 (drift visible). US2 is the other P1 and is what makes US3's upgrades safe, so US3 must
not be deployed before US2. Ship as one PR after T030; intermediate commits keep the build green.
