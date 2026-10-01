# System blueprint lifecycle — authority, currency, drift, and governance pinning

**Date:** 2026-10-01 · **Issue:** #1466 · **Context (out of scope):** #1380, #1393, #1464, #1465
**Status:** Design — awaiting review

## 1. Problem

The platform's own blueprints (`register-creation-v1`, `register-governance-v1`,
`create-organisation-v1`, `join-private-register-v1`) are published onto the system register (SSR)
and resolved from its ledger. Their lifecycle has four defects, all silent:

1. **Seeding checks existence, not content.** `SystemRegisterBootstrapper.SeedBlueprintsIfMissingAsync`
   skips any blueprint that already exists, so a node's SSR keeps genesis-day definitions forever
   while its image ships newer ones. Nothing reports the disagreement. On n1 this refused **every
   governance proposal on every register** (`VAL_SCHEMA_004` behind a `202 Accepted`) until an operator
   republished by hand (#1466).
2. **"Current" is read from an unsigned field.** `SystemRegisterService.GetBlueprintAsync` selects the
   publication with the newest `TimeStamp` — the publisher's clock, outside the signature. `Version` is
   a count with no supersession semantics.
3. **Governance resolves the latest definition, unpinned.** Governance transactions are not instances
   (a star, not a line — F189 T055), so they carry no F194 pin. A republish of `register-governance-v1`
   changes the rules for every open proposal on every register; a schema change fails all outstanding
   approvals behind a 202 — the #1466 failure, triggered by a *legitimate* upgrade.
4. **Caches are not told.** The validator's id-keyed `BlueprintCache` (Redis + L1) is not invalidated
   when a new publication seals, and on a subscriber (tiny) the publication arrives by peer sync, not
   through the publish endpoint.

Additionally, `POST /api/system-register/publish` requires only `CanManageRegisters`: any
platform-tier admin on the key-holding node can redefine a network-wide blueprint.

### Facts this design relies on

- Exactly **one** key may publish to the SSR: the embedded genesis carries a single Active
  `sorcha:blueprint-publish` entry on the SSR validator roster (the genesis validator, i.e. n1).
  `ExemptionAuthorityResolver.ResolveBlueprintPublishAsync` (F196) enforces it at seal.
- Only owner nodes seed (`BootstrapMode != SyncOnly`); subscribers replicate.
- A definition's identity is its publication transaction id (§22); for the SSR it is
  `BlueprintPublicationId.Compute(SystemRegisterId, blueprintId, canonicalJson)`.
- `ValidationEngine.ResolvePinnedBlueprintAsync` reads a pin's publication transaction from the
  **transaction's own** register. A definition published on the SSR but executed on another register
  is therefore not resolvable by that arm today.

## 2. Decisions (agreed 2026-10-01)

| # | Decision |
|---|---|
| D1 | Upgrading a system blueprint is an **explicit operator act**. No node redefines a network-wide blueprint by booting. The platform makes drift **visible**. |
| D2 | A governance proposal is judged by the governance definition **in force when it was raised** (pinned), as F194 does for instances. |
| D3 | The operator act is available only to a **SystemAdmin** (platform tier) on a node holding an Active SSR `sorcha:blueprint-publish` roster key; every refusal and publish is audited. The key stays node-custodied (offline signing is #1380's concern). |
| D4 | The pin is carried on the **proposal's signed payload**; approvals and the enactment inherit it by reference (approach A). |
| D5 | Publishing is **from the node image's catalogue only** — no raw-body publish. |

## 3. Design

### 3.1 Currency: ledger order, never timestamp

- The **current** publication of a system blueprint is the one sealed latest: highest docket number,
  then position within the docket's `TransactionIds`. `TimeStamp` is not consulted.
- `Version` = ordinal of the publication in ledger order (display only).
- Identity stays the publication id. Consequences of content identity:
  - publishing byte-identical content is a **no-op** ("already current, vN");
  - publishing a body identical to an **older** publication is **refused** ("that is v2; current is v4")
    — it would be the old publication, and the ledger never goes backwards silently. Rolling back
    means publishing a corrected (distinct) body.
- One resolver owns this rule (`SystemBlueprintCurrency`, Register Service) and is used by
  `GetBlueprintAsync`, the drift reporter, the publish command, and the
  `GetSystemRegisterBlueprintJsonAsync` read the validator uses.

### 3.2 Drift detection (every node, including SyncOnly)

At startup and every 10 minutes the Register Service computes the SSR publication id of each
catalogue blueprint shipped in its image and classifies it against the ledger:

| State | Meaning |
|---|---|
| `InSync` | the image's definition is the current publication |
| `ImageBehind` | the image's definition equals an **older** publication — the node runs an old image |
| `ImageAhead` | the image's definition has never been published — an operator publish is pending |
| `Missing` | the blueprint is not on the ledger (owner seeds; subscriber awaits sync) |

`ImageBehind` vs `ImageAhead` is decidable because publication history is content-keyed.

Surfaces:

- `LogWarning` per drifted blueprint at startup;
- health check `system-blueprints` → `Degraded` when any blueprint is `ImageBehind` or `ImageAhead`,
  or `Missing` on an owner node after bootstrap (the node works; it is not Unhealthy). `Missing` on a
  subscriber that has not finished syncing is `Healthy`;
- gauge `sorcha_system_blueprint_drift{blueprint,state}` on the `Sorcha.Register` meter;
- `GET /api/system-register/drift` (gated as §3.3) returning, per blueprint: state,
  current publication id + version, image publication id, and the matching historical version when
  `ImageBehind`.

Seeding is unchanged in kind: the owner seeds only `Missing` blueprints. Nothing republishes
automatically.

### 3.3 The operator command

`POST /api/system-register/publish` is replaced (clean break, pre-release) by a catalogue publish:

```
POST /api/system-register/blueprints/{blueprintId}/publish
{ "dryRun": false, "expectedCurrent": "<publicationTxId | null>" }
```

- **Authorization:** the existing `RequireSystemAdmin` policy composed with
  `RequirePlatformAudience` (§13). An org `Administrator` and any consumer-tier token are refused.
  The same pair gates `GET /api/system-register/drift`.
- **Precondition:** this node holds an Active SSR validator-roster entry under
  `sorcha:blueprint-publish`. Otherwise refused with a reason — never submitted to be refused at seal.
- **Source:** the definition is loaded from this node's image catalogue
  (`blueprints/templates/{id}.json`); the request carries no definition. Unknown id → 404.
- **Consequence of catalogue-only (D5):** the only way to live-test a changed system blueprint is to
  build an image containing it. The previous practice of publishing "the version under test" through the
  raw-body endpoint (sorcha-architecture skill, F189 T054 note) ends; that note is updated with this
  change.
- **Refusals:** `InSync` (no-op, 200 with "already current"), `ImageBehind` (would roll back, 409),
  `expectedCurrent` mismatch (409 — optimistic concurrency between operators), no roster key (403).
- **`dryRun`:** returns the drift state and current/candidate publication ids; submits nothing.
- **Success:** 202 with the publication transaction id. The drift report reads `InSync` once sealed.
- **Audit:** every refusal is recorded through the #1648 refusal audit
  (`RefusalAuditActions.SystemBlueprintPublish`, new constant); every successful publish is recorded as
  an audit event (blueprint, previous → new publication id, node). Metric
  `sorcha_system_blueprint_publish_total{outcome}`.
- **CLI:** `sorcha system-register drift` and
  `sorcha system-register publish <id> [--dry-run] [--expected-current <txid>]`. CLI DTOs are covered by
  `Sorcha.Cli.ContractTests` (§18).

The validator rule is unchanged: F196's BlueprintPublish authority already requires the roster key.

### 3.4 Governance pinning

**Carrier.** `ControlTransactionPayload` gains `governanceDefinitionTxId` (string, the SSR publication id
of `register-governance-v1`). It is inside the signed payload, so it is covered by the transaction
signature and the payload hash; no field-by-field signable-bytes rebuild is involved.

**Producers.** `/governance/propose` and the Owner-override propose-and-enact path stamp the **current**
publication id (§3.1) at raise. `GovernanceApprovalActionSubmitter` and the enactment producer do not
change — they inherit by reference.

**Single resolver.** `GovernanceDefinitionPin` (in `Sorcha.Validator.Core`, beside the T079 verifier)
answers "which definition governs this governance transaction?":

- proposal → its own payload's `governanceDefinitionTxId`;
- approval → the pin of the proposal it chains from (`PreviousTransactionId`);
- enactment → the pin of the proposal named by `EnactsProposalId`.

Every call site in `ValidationEngine` that resolves the governance blueprint (schema and conformance
validation) goes through it. `RightsEnforcementService` does **not** resolve a definition today — it
discriminates governance by blueprint id and recounts approvals against the roster — so it is
unaffected; no new coupling is introduced there.

The Owner-override propose-and-enact is one transaction that is both proposal and enactment: its own
payload's pin applies (a transaction carrying a pin is governed by it; only a transaction without one
follows a reference).

**Validator rules.**

1. *The pin must resolve.* `ResolvePinnedBlueprintAsync` gains an **SSR arm**: for a definition
   published on the system register, read the publication transaction from the SSR and recompute the id
   with `SystemRegisterId`; refuse on mismatch. Unresolvable → `VAL_BP_VERSION_001` (fail closed).
   Never fall back to latest. Note that a refused proposal is not resubmitted — it must be **re-raised**
   as a new transaction once the node's SSR replica has caught up.
2. *At raise, the pin must be current.* A proposal whose pin is a resolvable but superseded publication
   is refused with **`VAL_GOV_DEF_001`** ("raised under a superseded governance definition"). This stops
   a member choosing an older, laxer definition. Approvals and enactments are **not** held to "current" —
   they follow their proposal; that is the point.

**Legacy.** A proposal carrying no pin (raised before deploy) resolves latest, as F194 did, and
increments `sorcha_governance_definition_pin_fallback`. Acceptance: zero for proposals raised after
deploy.

**Contract.** Action 1's `dataSchemas` in `register-governance-v1` gains a required
`governanceDefinitionTxId`; `GovernanceControlPayloadContractTests` holds payload and schema in step.

**Audit surface.** `GET /api/registers/{id}/governance/proposals/{proposalId}` reports the governing
definition's publication id and version.

### 3.5 Caching

- Pinned resolution is content-keyed (`…:{id}:{publicationTxId}`) and cannot be stale; all governance
  validation moves onto it.
- The remaining "latest" consumer is the current-at-raise check. The validator subscribes to
  `docket:confirmed` for the SSR and, for each sealed `BlueprintPublish` transaction, evicts that
  blueprint id from L1 and L2. Every node receives this event, including a subscriber that got the
  docket by replication.
- Existing TTLs (15 min L2 / 5 min L1) remain as a backstop only.

## 4. Rollout

1. Deploy `validator-service` (accepts pinned and unpinned proposals; SSR arm; eviction).
2. Deploy `register-service` (drift reporting, new command, pin stamping).
3. Operator publishes the new `register-governance-v1` via the command.

**Open item to settle in planning (before code):** whether the currently-published governance
definition rejects unknown properties on action 1. If it does, step 2 must not stamp the pin until step 3
has sealed (stamp only when the current definition declares the field), or steps 2 and 3 are ordered
accordingly. Pre-release re-genesis remains available but the design does not depend on it.

**Second item to verify in planning:** whether `join-private-register-v1` instances (published on the
SSR, executed on other registers) already fail F194 pinned resolution for the reason in §1 Facts. If so,
the SSR arm (§3.4) fixes them too and the plan should include a regression test.

## 5. Testing

Every guard is tested against the counterfactual it prevents, and mutation-checked.

- **Currency:** fixture where the later-sealed publication carries the *earlier* timestamp — it must be
  current. Reverting to timestamp order must red.
- **Drift:** all four states; `ImageBehind` vs `ImageAhead` distinguished.
- **Command:** consumer-tier refused; non-SystemAdmin refused; node without roster key refused; each
  refusal writes an audit entry; `InSync` no-op; `ImageBehind` refused; `expectedCurrent` mismatch
  refused; `dryRun` submits nothing.
- **Pin is load-bearing:** proposal raised under v4 → v5 published → approval validates against v4; the
  same approval resolved as latest must **fail**. Removing the pin lookup must red.
- **Validator:** superseded pin at raise → `VAL_GOV_DEF_001`; unresolvable → `VAL_BP_VERSION_001`; SSR
  arm refuses a payload that does not reproduce its id; enactment resolves via `EnactsProposalId`.
- **Eviction:** a sealed SSR `BlueprintPublish` evicts the id; removing the subscriber must red.
- **Live gate (n1 + tiny):** both report `InSync`; raise a proposal, publish a new governance definition
  mid-flight, approve → enacts; the next proposal pins the new definition; tiny's drift report flips with
  no manual step; pin-fallback counter reads zero.

## 6. Out of scope

- Automatic republish on startup (D1).
- Offline / ceremony signing of the publish key, and service-principal signing authority (#1380).
- Governing the SSR itself (#1464, #1465).
- Service scope enforcement (#1393).

## 7. Documentation to update with the implementation

**The old route is a clean break.** `POST /api/system-register/publish` is referenced by:
`src/Services/Sorcha.Register.Service/README.md`, `.claude/skills/sorcha-architecture/SKILL.md` (F189
T054 / #1466 remediation notes), and historical specs (`specs/057-*`, `specs/059-*`, `specs/133-*`,
`specs/189-*/tasks.md`). No `src/`, CLI or walkthrough code calls it. The README and skill are
updated; historical specs are left as the record of their time.

Register Service README (system-register endpoints, drift), Validator Service README (SSR arm,
`VAL_GOV_DEF_001`), `docs/reference/API-DOCUMENTATION.md`, the `sorcha-architecture` skill (F189 section:
governance pinning; seeding note), CLAUDE.md §22/§23 cross-reference if the currency rule warrants a
pattern entry, and `.specify/MASTER-TASKS.md`.
