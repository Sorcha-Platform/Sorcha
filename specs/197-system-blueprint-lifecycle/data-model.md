# Data Model: System Blueprint Lifecycle (F197)

No new persisted entities. Changes are one ledger-payload property, one schema property, and read models.

## SystemBlueprintCatalog (static, `Sorcha.Register.Models`)

| Member | Value |
|---|---|
| `Ids` | `register-creation-v1`, `register-governance-v1`, `create-organisation-v1`, `join-private-register-v1` (seed order preserved) |
| `GovernanceBlueprintId` | alias of `GovernanceBlueprint.BlueprintId` |

Single home for the list used by the bootstrapper, drift reporter and validator eviction. Replaces the
inline array in `SystemRegisterBootstrapper.SeedBlueprintsIfMissingAsync`.

## ControlTransactionPayload (+1 property)

| Property | JSON | Type | Rule |
|---|---|---|---|
| `GovernanceDefinitionTxId` | `governanceDefinitionTxId` | `string?` | `JsonIgnore(WhenWritingNull)`. Set only on proposals (pending and owner-override). 64 lowercase hex — an SSR publication id of `register-governance-v1`. Null on genesis, enactment, SSR genesis ⇒ their canonical bytes are unchanged. |

Signed: inside the payload, covered by the payload hash and transaction signature.

## Governance definition schema (action 1, `register-governance-v1.json`)

`properties.governanceDefinitionTxId`: `{ "type": "string", "pattern": "^[0-9a-f]{64}$" }` — **not** in
`required` (sealed golden vectors predate it).

## PublicationOrder (read model, Register Service)

| Field | Source |
|---|---|
| `DocketNumber` | `TransactionModel.DocketNumber` |
| `PositionInDocket` | index of `TxId` in `DocketHeader.TransactionIds` |

Comparison: `DocketNumber`, then `PositionInDocket`. `TimeStamp` never consulted. Null docket ⇒ excluded
from currency, logged.

## SystemRegisterEntry (existing, semantics change)

- Selection of the current entry: ledger order (was: newest `TimeStamp`).
- `Version`: 1-based ordinal of the publication in ledger order among that blueprint's publications.
- `PublishedBy`: operator's platform user id for operator publishes; `"system"` for bootstrap seeds.

## SystemBlueprintDriftState (enum)

`InSync` · `ImageBehind` · `ImageAhead` · `Missing` · `Unknown` (SSR unreadable this cycle — never reported
as `InSync`).

## SystemBlueprintDriftEntry (read model, `GET /api/system-register/drift`)

| Field | Type | Notes |
|---|---|---|
| `blueprintId` | string | |
| `state` | `SystemBlueprintDriftState` | serialised by name in the platform kebab-case wire form (`in-sync`, `image-behind`, `image-ahead`, `missing`, `unknown`) |
| `currentPublicationTxId` | string? | null when `Missing`/`Unknown` |
| `currentVersion` | int? | |
| `imagePublicationTxId` | string? | `BlueprintPublicationId.Compute(SSR, id, canonical(catalogue))`; null if the image lacks the file |
| `imageMatchesVersion` | int? | set only for `ImageBehind` |
| `checkedAt` | DateTimeOffset | |

Classification: image id == current id → `InSync`; image id ∈ older publications → `ImageBehind`;
publications exist but none match → `ImageAhead`; no publication → `Missing`; read failure → `Unknown`.

## Health status mapping (`system-blueprints`)

| Condition | Status |
|---|---|
| all `InSync` | Healthy |
| any `ImageBehind` / `ImageAhead` | Degraded |
| any `Missing` on an owner node after bootstrap completed | Degraded |
| `Missing` on a SyncOnly node | Healthy |
| any `Unknown` | Degraded |

## GovernanceDefinitionPin (pure, `Sorcha.Register.Models`)

Input: the governance step's action id, its own parsed `ControlTransactionPayload` (null for approvals),
and the referenced proposal's parsed payload (fetched by the caller). Output: `PinResolution`:

| Case | Result |
|---|---|
| step's own payload carries a pin (proposal, owner-override) | `Pinned(ownPin, isRaise: true)` |
| approval (action 2) → proposal via `PreviousTransactionId` | `Pinned(proposal.pin, isRaise: false)` or `Legacy` if the proposal has none |
| enactment (`EnactsProposalId` set) | `Pinned(proposal.pin, isRaise: false)` or `Legacy` |
| referenced proposal unreadable/missing | `Unresolvable(reason)` → refuse |
| proposal without pin | `Legacy` |

Precedence: own pin wins over a reference (covers owner-override, which is both raise and enact).

## Validation outcomes

| Code | When | Fatal |
|---|---|---|
| `VAL_GOV_DEF_001` | `isRaise` and pin ≠ current SSR publication of `register-governance-v1` (pin resolvable) | yes |
| `VAL_BP_VERSION_001` (existing) | pin cannot be resolved (incl. SSR arm id mismatch) | yes |

## Metrics

| Instrument | Meter | Tags |
|---|---|---|
| `sorcha_system_blueprint_drift` (gauge, 1 per blueprint) | `Sorcha.SystemBlueprints` | `blueprint`, `state` |
| `sorcha_system_blueprint_publish_total` (counter) | `Sorcha.SystemBlueprints` | `outcome` ∈ published, noop, refused_rollback, refused_concurrency, refused_no_key, refused_auth, not_found |
| `sorcha_governance_definition_pin_fallback` (counter) | `Sorcha.Validator` | `step` ∈ proposal, approval, enactment |
| `sorcha_system_blueprint_cache_evictions_total` (counter) | `Sorcha.Validator` | — |

`Sorcha.SystemBlueprints` is a NEW meter and must be added to the export list in
`Sorcha.ServiceDefaults/Extensions.cs` (`metrics.AddMeter(...)`) or it is silently not exported.
Validator instruments join the existing `Sorcha.Validator` meter.
