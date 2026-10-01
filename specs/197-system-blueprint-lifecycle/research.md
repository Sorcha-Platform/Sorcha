# Phase 0 Research: System Blueprint Lifecycle (F197)

All five pre-design questions (R1–R5) are resolved. Citations are to the source as of 2026-10-01.

---

## R1 — Does the published governance definition reject unknown properties?

**Decision:** No. The pin can be stamped by producers **before** the new governance definition is
published. Rollout order is not constrained by schema tolerance.

**Evidence:** `blueprints/templates/register-governance-v1.json` declares no `additionalProperties` or
`unevaluatedProperties` at any depth, on any action. JSON Schema's default admits unknown properties.
The file has not changed since #1390 (commit `02d37e621`); n1 and tiny were re-genesised on
2026-09-19 from that content, so the published definition on both nodes equals the repo file.

**Design consequence:** the field is declared **optional** in action 1's schema (see R5-e: the sealed n1
golden vectors lack it and must still conform). Omitting it buys a submitter nothing — an unpinned
proposal resolves the *current* definition, which is exactly what a pinned one must name.

---

## R2 — Do `join-private-register-v1` instances fail F194 pinned resolution?

**Decision:** Moot today — no instances exist. The defect is real but latent; the SSR resolution arm built
for governance (R5) closes it. No separate task.

**Evidence:**
- Nothing submits against it: `RegisterInvitationService.cs` contains no blueprint/instance submission;
  `specs/master/tasks.md:231` (T066 "invitation acceptance creates blueprint instance") is unchecked.
  The only references are the seed list, a comment, the template, and a template-shape test.
- If instances existed: `ResolvePinnedBlueprintAsync` (`ValidationEngine.cs:2617`) would miss in the cache,
  miss at the Blueprint Service (system blueprints are rejected with `no_provenance`,
  `ValidationEngine.cs:2756-2758`), and read the publication tx from the **instance's** register
  (`:2657`) where it does not exist → `VAL_BP_VERSION_001`.

**Alternatives considered:** a dedicated fix + test now — rejected (YAGNI; no producer exists). The SSR arm
is generic (keyed on "the pin is not on this register but is on the SSR"), so the future instance path
inherits it.

---

## R3 — Ledger order for SSR blueprint publications

**Decision:** Order by `TransactionModel.DocketNumber`, then by index within
`DocketHeader.TransactionIds` for publications sharing a docket. A publication with a null
`DocketNumber` (should not occur — the register stores sealed transactions only) is excluded from
"current" and logged.

**Evidence:**
- `SystemRegisterService.GetBlueprintTransactionsAsync` (`:485-512`) fetches `BlueprintPublish` +
  `Control` transactions, filters with `BlueprintPublicationFilter.IsPublication`, orders by `TimeStamp`.
  `GetBlueprintAsync` (`:180-205`) and `GetAllBlueprintsAsync` (`:150-161`) both order/count by
  `TimeStamp`.
- `TransactionModel.DocketNumber` (`ulong?`, `TransactionModel.cs:55`) is set in `WriteDocket`
  (`Register.Service/Program.cs:1767`) before insert.
- `DocketHeader.TransactionIds` (`DocketHeader.cs:54`) gives in-docket order;
  `IReadOnlyRegisterRepository.GetDocketAsync(registerId, docketId)` (`:62`) reads it.
- Existing docket-ordering precedent: `GovernanceRosterService.cs:405`, `RosterAsOfResolver.cs:164` — both
  docket-only; **no** existing code breaks ties by position. The new resolver is the first.
- The validator reads "current" via `GET api/system-register/blueprints/{id}`
  (`RegisterServiceClient.cs:1028` → `SystemRegisterEndpoints.cs:214-228` → `GetBlueprintAsync`), whose
  `SystemRegisterEntry` already carries `PublicationTransactionId`. Fixing `GetBlueprintAsync` fixes the
  validator's view with no client change.

**Alternatives considered:** a dedicated sequence number on publications — rejected: it would be a new
unsigned or newly-signed field, while docket position is already sealed and identical on every node.

---

## R4 — Do replicas emit `docket:confirmed`?

**Decision:** Yes. The validator subscribes to `docket:confirmed`, filters to the SSR, and evicts the by-id
cache entries of the system catalogue's blueprints.

**Evidence:**
- Single publish site: the tail of `WriteDocket` (`Register.Service/Program.cs:1983-1992`), payload
  `DocketConfirmedEvent { RegisterId, DocketId, TransactionIds, Hash, TimeStamp }`
  (`RegisterEvents.cs:46-53`).
- Every docket write goes through `WriteDocket`, including peer replication
  (`Peer.Service/Replication/DocketFinalizationService.cs:179`) — so a replica publishes on its own bus.
  The idempotent re-write branch also publishes.
- The validator subscribes to no register event except `register:relationship-changed`
  (`RegisterMonitoringBootstrap.cs:34/62`, optional `IEventSubscriber`) — that is the pattern to copy.
- `BlueprintCache.RemoveAsync(blueprintId)` (`:401`) removes L1 + the Redis **by-id** key only and
  broadcasts invalidation; it leaves by-definition (content-keyed) entries alone — exactly right.
- TTL is absolute (15 min Redis / 5 min L1), not sliding — a backstop only.

**Eviction granularity decision:** on any sealed SSR docket, evict the by-id entry for **every** id in the
system catalogue (four ids). SSR dockets are rare (publications only — the SSR is not governed yet,
#1464/#1465), so precision buys nothing and avoids a per-transaction fetch to learn which blueprint a
transaction published. The id list moves to one home, `SystemBlueprintCatalog` in
`Sorcha.Register.Models`, shared by the bootstrapper, the drift reporter and the validator.

**"Current at raise" is read uncached:** the `VAL_GOV_DEF_001` check asks the local Register Service
directly for the current publication (proposals are rare). It therefore never depends on eviction timing;
eviction serves the legacy latest-resolution path (FR-019, FR-022).

---

## R5 — Governance producers and resolution

### (a) Producers of `ControlTransactionPayload`

| Site | Builds | Pin? |
|---|---|---|
| `Register.Service/Program.cs:2632` | pending proposal | **stamp** |
| `Register.Service/Program.cs:2740` | Owner-override propose-and-enact | **stamp** |
| `GovernanceEnactmentService.cs:318` | enactment (`EnactsProposalId` set) | no — inherits |
| `RegisterCreationOrchestrator.cs:770` | register genesis | no |
| `Cli/Commands/SystemRegisterCommands.cs:187` | SSR genesis (pre-signed, offline) | no |

Both stamping sites go through `SubmitGovernanceControlAsync` (`Program.cs:2522`); the stamp is applied
where the payload is constructed, not in the shared submitter (genesis/enactment must not get one).

### (b) Payload type — and a trap

`ControlTransactionPayload` (`GovernanceModels.cs:376-466`): `version`, `roster`, `operation`,
`enactsProposalId`; `CanonicalJsonOptions` writes **nulls explicitly**.

**Decision:** the new property is `[JsonPropertyName("governanceDefinitionTxId")]` with
`[JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]`. Rationale: genesis, enactment and the
pre-signed SSR genesis all serialise this type; an always-written null would change their canonical bytes
and therefore their payload hashes. Omitted-when-null keeps every existing producer byte-identical, which
a test pins.

### (c) Where the validator resolves the governance blueprint

Only `ValidateSchemaAsync` (`ValidationEngine.cs:625`) resolves it for governance: the Control exemption is
withdrawn for `TransactionTypeClassifier.IsGovernanceActionTransaction` (`:528-530`), and the pin passed is
`ReadCarriedExecDefHash` (`:2552-2574`) — the `routingDecision` metadata, which governance never sets →
null → latest. Routing (`:1121`) and conformance (`:1532`) are skipped for governance by the exemption.

**Decision:** in `ValidateSchemaAsync`, when the transaction is a governance action transaction, the pin
comes from `GovernanceDefinitionPin` instead of `ReadCarriedExecDefHash`. Nothing else in the engine
changes. Action 4 (enactment) declares no `dataSchemas` and is skipped by FR-006 today; it still resolves
through the pin for consistency, at no behavioural cost.

### (d) References

- Approval → proposal: `PreviousTransactionId = proposalId` (`GovernanceApprovalActionSubmitter.cs:132`).
- Enactment → proposal: payload `EnactsProposalId` (read at `RightsEnforcementService.cs:281`); its
  `PreviousTransactionId` is the roster head, **not** the proposal.
- The validator fetches the referenced proposal through `IRegisterServiceClient.GetTransactionAsync`
  (already injected in `ValidationEngine`).

### (e) Contract test

`tests/Sorcha.Register.Models.Tests/GovernanceControlPayloadContractTests.cs` compares a maximal proposal
payload with action 1's schema bidirectionally and checks the sealed n1 golden vectors
(`Fixtures/Governance/*-sealed-n1.json`) conform. Adding the property requires adding it to the schema
(optional, `type: string`, `pattern` of 64 lowercase hex); the golden vectors must still conform — which is
why the field is **not** `required`.

---

## Other decisions taken during research

- **Success audit:** a successful publish is recorded on the **ledger** itself — the publication
  transaction's `publishedBy` (today the literal `"system"`) becomes the operator's platform user id, with
  `seedReason = "operator"` — plus a structured log line and the publish metric. Refusals use the existing
  #1648 refusal audit (`IRefusalAuditClient`, already registered by `AddServiceClients`). No new Tenant
  endpoint is introduced. The ledger is the immutable record; the audit log is for refusals a person must
  act on.
- **`VAL_GOV_DEF_001` stays validator-local** (§16): no second project names it.
- **Authorization:** `RequireSystemAdmin` + `RequirePlatformAudience` (both exist in
  `AuthorizationPolicyExtensions`).
- **CLI:** new `SystemRegisterDriftCommand` and `SystemRegisterPublishCommand` beside the existing four in
  `SystemRegisterCommands.cs`; their DTOs enter `Sorcha.Cli.ContractTests`.
