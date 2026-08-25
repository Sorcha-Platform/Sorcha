# Implementation Plan: Blueprint Definition Identity

**Branch**: `195-blueprint-definition-identity` | **Date**: 2026-08-24 | **Spec**: [spec.md](./spec.md)

**Input**: Feature specification from `/specs/195-blueprint-definition-identity/spec.md`

**Issues**: #1563 (the decision), #1566, #1567, #1568, #1570.
**Design**: `docs/superpowers/specs/2026-08-24-blueprint-lifecycle-design.md`.
**Evidence**: `docs/superpowers/specs/2026-08-24-blueprint-lifecycle-current-state-FINDINGS.md`.

## Summary

Make the **publication transaction the identity of a blueprint definition**. An instance pins to that
transaction id; a starting action chains from that same transaction. Anchor and pin become one value
because they are one fact, so #1563 stops existing rather than being fixed, and the four copies of the
`blueprint-publish-{registerId}-{blueprintId}` derivation all delete — they exist only because
`PublishedBlueprint` never recorded the transaction id it was published as.

Around that: resolve the pin at **submit** as well as at seal (#1567); collapse the two publish paths
so content-addressing does not trade a silent drop for a silent fork (#1570); fix the behavioural
signature's coverage and guard it by reflection (#1566); and make amendment one honest upgrade path
with a derived ordinal (#1568).

## Technical Context

**Language/Version**: C# 14 / .NET 10

**Primary Dependencies**: System.Text.Json (canonicalisation), `Sorcha.Blueprint.Models`,
`Sorcha.Blueprint.Engine`, `Sorcha.Register.Models`, StackExchange.Redis (validator + engine caches),
EF Core / Postgres (Blueprint drafts), MongoDB (register ledger)

**Storage**: register ledger (MongoDB, authoritative); Blueprint Postgres (drafts — node-local,
non-durable by design); Redis (validator blueprint cache, engine action-resolver cache); in-memory
published-blueprint store (a cache of the ledger, rebuilt by recovery)

**Testing**: xUnit v3 4.x under **MTP mode** (`global.json`) — `--filter-class "*Name*"`,
`--project x.csproj`; `--collect` is dead. FluentAssertions 8.x, Moq 4.20.x.

**Target Platform**: Linux containers (Docker / Aspire); the canonicaliser must not preclude
browser-wasm consumers of `Sorcha.Blueprint.Models`

**Project Type**: Distributed services — Blueprint Service, Register Service, Validator Service, plus
shared leaves

**Performance Goals**: Canonicalisation is on the publish path only (not per-submission). Definition
resolution on the submit path must stay cache-first; the new ledger fallback is a last resort, not a
hot path.

**Constraints**: No migration — pre-release, register wipe authorised. Deployment **order** within the
development sequence remains load-bearing (R-012). Deploy scope includes `register-service`.

**Scale/Scope**: 5 issues, 3 services + 2 shared projects, ~21 tasks across 5 phases.

## Constitution Check

*GATE: passed before Phase 0; re-checked after Phase 1 design. No violations.*

| Principle | Assessment |
|---|---|
| **I. Microservices-First** | **Strengthened.** The change removes a formula duplicated across Blueprint and Register Services and gives it one producer. No new coupling: Blueprint learns the id from the Register Service's existing synchronous response. No upward dependency — the canonicaliser lands in a shared leaf, consumed downward. |
| **II. Security First** | **Strengthened.** A definition's identity becomes self-anchoring: the id *is* the digest, so a tampered payload cannot match its own transaction id. Replaces a separately-sealed `contentHash` compared by a second code path. Fails closed — an unresolvable pin refuses the submission rather than substituting a definition. |
| **III. API Documentation** | Endpoint surface changes are additive/renaming; XML docs and `.WithSummary()`/`.WithDescription()` required on every touched endpoint. Contracts in `contracts/`. |
| **IV. Testing** | >85% on new code. Every guard **mutation-tested** with a named killing test (R-015). Live acceptance run required — a green suite is explicitly not sufficient for this feature. |
| **V. Code Quality** | Nullable enabled, async I/O, DI throughout, no Release warnings. Deletes more than it adds (four derivation copies, two dead version fields, one publish path). |
| **VI. Blueprint Standards** | Unchanged — blueprints stay JSON documents. This feature governs how a published one is *identified*, not how it is authored. |
| **VII. Domain-Driven Design** | Ubiquitous language extended deliberately: **Blueprint** (the thing), **Definition** (one published version), **Publication** (the transaction that records it). "Version" is demoted to a display label, which is the point. No renaming of Action / Participant / Disclosure / Publish. |
| **VIII. Observability** | Existing `pin_fallback` / `pin_mismatch` counters retained and become the acceptance signal. Structured logging on every refusal path — a definition that stops resolving must be diagnosable, since that is precisely the failure this feature exists to make impossible. |

**Complexity note:** the feature *reduces* concept count (seven version concepts to four) and deletes
duplicated derivations. Nothing in the Complexity Tracking table.

## Project Structure

### Documentation (this feature)

```text
specs/195-blueprint-definition-identity/
├── plan.md              # This file
├── spec.md              # Feature specification
├── research.md          # Phase 0 — 15 decisions, 2 corrections recorded
├── data-model.md        # Phase 1
├── quickstart.md        # Phase 1 — the live acceptance run
├── contracts/           # Phase 1
│   ├── publication-identity.md
│   ├── definition-resolution.md
│   └── blueprint-definitions.openapi.yaml
├── checklists/
│   └── requirements.md
└── tasks.md             # Phase 2 (/speckit.tasks)
```

### Source Code (repository root)

```text
src/
├── Common/
│   ├── Sorcha.Blueprint.Models/
│   │   ├── Canonical/BlueprintCanonicalJson.cs        # NEW — the canonicaliser (one home)
│   │   ├── BlueprintPublicationId.cs                  # NEW — the preimage + hash (Register-only caller)
│   │   ├── BlueprintCacheKey.cs                       # param meaning: execDefHash → publicationTxId
│   │   ├── Blueprint.cs                               # DELETE VersionMajor / VersionMinor
│   │   └── Forms/FormKeywordClassifier.cs             # unchanged (unknown x-* stays behavioural)
│   ├── Sorcha.Register.Models/
│   │   └── Transactions/RoutingDecision.cs            # field → blueprintDefinitionTxId (+ signable rebuild)
│   └── Sorcha.ServiceClients.Http/
│       └── Register/BlueprintContentHash.cs           # DELETE — absorbed by the publication id
├── Core/
│   └── Sorcha.Blueprint.Engine/
│       └── Implementation/ExecutableDefinitionHasher.cs  # coverage fix; job narrowed to the rehearsal gate
└── Services/
    ├── Sorcha.Register.Service/
    │   └── Program.cs                                  # sole producer of the publication id
    ├── Sorcha.Blueprint.Service/
    │   ├── Program.cs                                  # PublishedBlueprint.PublicationTxId; derived ordinal;
    │   │                                               # DELETE the instance-creation publish branch
    │   ├── Services/Implementation/ActionResolverService.cs        # pin required; both caches keyed by it
    │   ├── Services/Implementation/ActionExecutionService.cs       # anchor read, not computed
    │   ├── Services/Implementation/BlueprintRecoveryService.cs     # dedupe + verify by publication id
    │   └── Endpoints/BlueprintFromPublishedEndpoint.cs             # same-id amend; source by publication id
    └── Sorcha.Validator.Service/
        └── Services/ValidationEngine.cs                # + register fallback arm on pinned resolution

tests/
├── Sorcha.Blueprint.Models.Tests/          # canonicaliser + golden vector + publication id
├── Sorcha.Blueprint.Engine.Tests/          # hasher coverage + reflection guard
├── Sorcha.Blueprint.Service.Tests/         # resolution, recovery, amend, ordinal
├── Sorcha.Validator.Service.Tests/         # pinned resolution + ledger fallback + refusal
└── Sorcha.Register.Service.Tests/          # publication id production, idempotency, two-register distinctness

walkthroughs/
└── VersionPinning/                          # EXTEND — F194's harness is the live acceptance run
```

**Structure Decision.** The canonicaliser and the publication-id preimage live in
**`Sorcha.Blueprint.Models`** — a shared leaf both the Register Service and the tests can reference,
with no service dependency and no libsodium P/Invoke, so it stays loadable everywhere the models are.
The *caller* is restricted to the Register Service by an architecture gate (R-004), not by placement:
placing it somewhere only Register can see would make the golden-vector test unable to reach it.

## Implementation phases

Sequenced so that each phase is independently verifiable and the riskiest ordering constraint (the
canonicaliser underpinning everything) is discharged first.

### Phase A — Canonical form and identity *(no behaviour change)*

The canonicaliser, the publication-id preimage, and the golden-vector test. Nothing consumes them yet.
**Everything downstream is defined in terms of this**, so it lands alone and green before anything
else moves. Decides the number-normalisation question left open in R-002.

### Phase B — One producer, one writer *(#1563 core + #1570)*

Register Service becomes the sole producer of the publication id. `PublishedBlueprint` gains
`PublicationTxId`. The instance-creation publish branch is deleted. The four derivation copies delete.
A behavioural republish now genuinely reaches the ledger — the first point at which the headline
symptom is fixed.

### Phase C — Resolution by publication *(#1563 downstream + #1567)*

The pin becomes the publication txId end to end: `RoutingDecision` field, instance pin, the anchor
read (not computed), the validator's register fallback arm, and the submit-path resolution with the
pin required in `GetBlueprintAsync` and both caches. **This is the phase that makes the participant-
facing guarantee true**, and #1567 is separable within it — it can be verified on its own.

### Phase D — Signature coverage and version hygiene *(#1566 + #1568)*

Hasher coverage fix with the reflection guard; behavioural signature's job narrowed to the rehearsal
gate; amend becomes same-id republish; the ordinal becomes derived; dead version fields deleted.

### Phase E — Live acceptance

Re-genesis, then the positive checks from the design document's "What proves it, live" section, via an
extended `walkthroughs/VersionPinning` harness. **Not optional and not substitutable by the suite** —
every defect this feature addresses is silent.

## Risks and how each is discharged

| Risk | Discharge |
|---|---|
| Canonical form changes later and silently re-identifies every definition | Golden-vector test over a fixed fixture, failing on any of the six degrees of freedom — including a `[JsonPropertyName]` rename (R-003) |
| The id gets recomputed somewhere else, drifting from the producer | Architecture gate: no `SHA-256` over a blueprint outside the Register Service call path. Same shape as the existing derivation-contexts and error-code gates |
| `RoutingDecision` rename rides the wire unauthenticated | Already guarded — `RoutingDecisionSigningCoverageTests` is reflection-driven and fails on a property it cannot mutate (R-007) |
| Old validator + new producer refuses every submission | Deploy validator first; wipe removes the installed-base case but not the development-sequence one (R-012) |
| A live check passes vacuously | Two are known-vulnerable — presentational republish and two-register distinctness. Both must execute the counterfactual (R-015) |
| The static action-index cache serves a foreign definition | It is keyed by bare id today; must carry the pin or be removed (R-006) |
| Scope creep into the validation surfaces | #1558 and #1569 are explicitly out of scope and shippable separately |

## Out of scope

- **#1558** — validation surface reconciliation (two algorithms under one name).
- **#1569** — the anonymous full-definition read endpoint.
- Migrating a running instance forward (F194 D1, unchanged).
- Any platform-level multi-party gate on publishing (F194 D2/D3, unchanged).
