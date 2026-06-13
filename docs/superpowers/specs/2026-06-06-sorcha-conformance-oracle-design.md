# Sorcha Conformance Oracle (SCO) — Design

**Date:** 2026-06-06
**Status:** Design approved (brainstorming) — pending speckit spec
**Author:** brainstorming session (Stuart + Claude)

## Problem

Sorcha has 143 spec'd features across 7 backend services, ~20 actor-based walkthroughs, and 11,088 tests — yet the v1-readiness assessment (2026-06-05, see `MEMORY.md` → v1-readiness-assessment) found that **the platform's correctness under realistic conditions is unproven**: CI green validates only unit/in-memory paths (9 integration-test projects are excluded from CI by design), and the recurring "seal-window" bug family (#119, #787, #814, #917, #585) shows the distributed seal/consensus/replication paths break in ways unit mocks never expose.

We want **one "ultimate proof"** — a test that exercises *every* feature of Sorcha at least once and, for each, asserts that the platform's actual guarantees hold. It is both the confirmation instrument ("does our solution work?") and, as a side effect, a live v1 correctness dashboard.

## Decisions (locked during brainstorming)

| # | Question | Decision |
|---|----------|----------|
| 1 | Primary purpose | **Correctness proof** — coverage of every feature is the axis, but each touch makes a real assertion of a guarantee, not just "it ran" |
| 2 | Top-level structure | **Invariant oracle + coverage** — define global invariants once; drive operations that touch every feature; re-assert ALL invariants after every operation |
| 3 | Substrate | **Layered: abstract model + fault-injection harness** — model proves design correctness; harness proves the running implementation conforms. Model buildable first. |
| 4 | Treatment of known gaps | **Two-tier: gate + tracked-red** — MUST-hold-today invariants gate CI; known-gap invariants are tracked-red (expected-fail + issue link) and form the live v1 backlog |
| 5 | Coverage scope | **Backend capability correctness + thin surface smoke** — full correctness depth on backend capabilities; presence-check smoke over UI/PWA/CLI/MCP surfaces |
| 6 | Model realization | **A + formal consensus core** — C# executable reference model + property-based driver as the backbone; TLA+ applied surgically to the consensus/seal core |

The scenario(s) may be **synthetic/contrived** (engineered to hit every capability), not necessarily realistic business workflows — but the proof itself is a usable, runnable instrument.

## Architecture

Two declarative pillars (the "what") feed a four-stage execution pipeline (the "how"), producing a two-tier verdict.

```
   Capability Registry            Invariant Catalogue
   (coverage axis —               (the oracle —
    every feature)                 tagged Gate/Aspirational)
        |  every capability → ≥1 op       | every op re-checks ALL invariants
        |  every invariant → ≥1 capability|
        v                                 v
              Operation Driver (the alphabet)
   (a) curated synthetic mega-sequence → hits every capability
   (b) property-based generator → randomized ops + fault events
        |                                       |
        v                                       v
   Reference Model            conform     Fault-Injection Harness
   (pure C#, no I/O)        <--------->    (Testcontainers/Aspire; clock,
   + TLA+ consensus core      same ops      crashes, slow/partitioned peers,
   (TLC, exhaustive)                        concurrent seals)
   = design correctness                    = implementation correctness
                                                 |  + Thin Surface Smoke
                                                 v
                          Two-Tier Verdict
              • Gate: all Gate invariants green + 100% capability
                coverage + TLA+ core passes  → CI-blocking
              • Tracked-red: aspirational fails w/ issue links
                → live v1 correctness dashboard
```

**Binding rule — bidirectional completeness:** every capability maps to ≥1 operation (no untested feature), and every invariant maps to ≥1 capability that can violate it (no dead invariant). Fully traceable both ways.

### Components

1. **Capability Registry** — single source-of-truth manifest of every backend capability + every UI/PWA/CLI/MCP surface, each linked to invariants and operations.
2. **Invariant Catalogue** — global invariants as predicates, each tagged `Gate` or `Aspirational(#issue)`.
3. **Operation Driver** — the operation alphabet; curated mega-sequence (coverage) + property-based generator (exploration incl. faults).
4. **Reference Model** — pure in-memory executable "spec Sorcha"; invariants checked after every op → design correctness.
5. **Formal consensus core (TLA+)** — exhaustive model-check of chain integrity + consensus safety + seal ordering.
6. **Fault-Injection Harness** — replays the same ops against the real stack under adversarial conditions; invariants checked on observable state → implementation correctness.
7. **Two-Tier Verdict** — Gate result (CI gate) + Tracked-red list (v1 dashboard).

## Pillar A — Capability Registry

The 143 spec folders dedupe to **~70–90 distinct capabilities across 13 domains** (many specs are iterations/fixes/UI-polish of one capability). Entry schema:

```
CapabilityId    e.g. CAP-LEDGER-007
Name            "Docket seal chains to predecessor"
Domain          Register | Ledger | Consensus | Crypto | Payload | Credentials |
                Identity | Tenancy | Blueprint | Trust | Replication | Platform |
                CitizenWallet | Surface
OwningService   Register | Validator | Wallet | Tenant | Blueprint | Peer | Gateway
Specs[]         spec folders subsumed (traceability back to the 143)
Surface         Backend | UI | PWA | CLI | MCP
Status          Implemented | Stub | Deferred
Invariants[]    invariants this capability can exercise/violate
Operations[]    driver ops that exercise it
```

**The 13 domains:** Register lifecycle & genesis · Ledger & docket sealing · Consensus & validator · Cryptography & wallets · Payload encryption & content types · Verifiable credentials & presentations · Identity & auth · Multi-tenant & org topology · Blueprint & workflow engine · Trust & verification · Replication & P2P · Platform services (email/notifications/storage/cache/rate-limit/MCP/CLI) · Citizen Wallet PWA. Surfaces are flagged `Surface` and get thin-smoke, not deep correctness.

**Coverage rule:** CI fails if any `Implemented` capability has zero exercising operations. `Stub`/`Deferred` capabilities are registered too and map to aspirational invariants — so the registry also enumerates the correctness debt.

## Pillar B — Invariant Catalogue

Each invariant is a predicate checked after **every** operation, on both model and real system. Entry schema:

```
InvariantId         e.g. I-CHAIN
Statement           plain-English + formal predicate
Family              Chain | Crypto | Disclosure | Authz | Lifecycle | Idempotency |
                    ConsensusSafety | Replication | Replay | Durability | StorageFailFast
DAD                 Disclosure | Alteration | Destruction | n/a
Tier                Gate | Aspirational
Issue               #issue (if aspirational/gap)
ModelCheck          predicate over model state
HarnessCheck        projection of observable system state + predicate
Capabilities[]      capabilities that exercise this invariant
```

| ID | Invariant (plain) | DAD | Tier |
|----|----|----|----|
| **I-CHAIN** | Every sealed tx links to a sealed predecessor; docket N hash-chains to N−1; height monotonic; no two dockets at one height; no sealed tx dropped | A | Gate |
| **I-CRYPTO** | Every tx/docket/vote/credential/delegation signature verifies vs declared key; issuer→holder→device chain valid; tamper ⇒ rejected | A | Gate |
| **I-DISCLOSE** | SD-JWT presentation reveals only consented claims; payload decryptable only by authorized; no plaintext outside DevMode | D | Gate |
| **I-AUTHZ** | JWT tier/audience boundary enforced (consumer/platform/service/enrol); admin ⇒ platform+role; /internal ⇒ service; no escalation | n/a | Gate |
| **I-LIFECYCLE** | Instance / credential / presentation (F111 timebound) state machines take only spec-legal transitions | n/a | Gate |
| **I-IDEM** | Retried writes don't double-apply; same-number+same-hash docket = idempotent, divergent = rejected (#814); welcome-email-once | A | Gate |
| **I-STORAGE-FF** | Audited storage interfaces are persistent in Prod/Staging (F113 fail-fast) | n/a | Gate |
| **I-CONSENSUS** | No conflicting dockets sealed at a height under concurrent proposers; votes verified end-to-end incl. upstream collector; abandoned-docket txs always return to pool | A | Aspir. (B-01, #787) |
| **I-CONVERGE** | Replicated register converges; SyncOnly replica SSR == owner SSR; survives node loss | D | Aspir. (#917, #585) |
| **I-REPLAY** | A sealed tx cannot be re-applied (monotonic per-sender sequence) | A | Aspir. (B-02) |
| **I-DURABLE** | A sealed docket survives an immediate node crash (w:majority + journal) | D | Aspir. (Mongo w:1) |
| **I-AUDIT** | Validator reconstructs historical control-blueprint config to validate old dockets | A | Aspir. (ControlBlueprintVersionResolver stub) |
| **I-REVOKE** | Org-key / credential revocation propagates and is enforced | n/a | Aspir. (DID-revocation stub) |

**DAD mapping:** I-CHAIN/I-IDEM/I-REPLAY = **Alteration**; I-DISCLOSE = **Disclosure**; I-CONVERGE/I-DURABLE = **Destruction**. The catalogue is a machine-checkable encoding of Sorcha's founding security claim, plus the crypto/authz/lifecycle invariants that protect it. The 7 Gate invariants are the no-regression floor; the 6 Aspirational ones are the executable v1 correctness backlog.

## Execution pipeline

### 3a. Operation Driver
One operation set (`CreateRegister`, `SubmitTx`, `BuildDocket`, `Seal`, `CastVote`, `IssueCredential`, `Present`, `Revoke`, `Replicate`, `EnrolDevice`, `InviteParticipant`, `CrashNode`, `AdvanceClock`, `PartitionPeer`, …), each tagged with the capabilities it exercises, runnable against both model and harness. Two modes:
- **Curated mega-sequence** — deterministic scripted run touching every `Implemented` capability ≥1, ordered as a synthetic council-universe storyline so it reads.
- **Property-based generator** (CsCheck) — randomized op+fault sequences with shrinking; failing sequences minimized and pinned as regression seeds.

### 3b. Reference Model (design correctness)
Pure in-memory `ISorchaModel` (no I/O) implementing only legal transitions; re-checks the entire Invariant Catalogue after every op. Fast (xUnit), cheapest place to catch design-level violations.

### 3c. Formal consensus core (TLA+/TLC)
Chain-integrity + consensus-safety + seal-ordering subset (`I-CHAIN`, `I-CONSENSUS`, `I-CONVERGE`) modeled in TLA+, exhaustively model-checked within bounds (≤3 validators, ≤4 dockets, concurrent proposers, message reorder/loss). The only layer that *proves* (not samples) safety in the #119/#787/#917 space. A sync test asserts the C# model's transition table matches the TLA+ actions.

### 3d. Fault-Injection Harness (implementation correctness)
Replays the same op sequences against the real stack (Testcontainers/Aspire: real Mongo/Postgres/Redis + multiple validator/peer instances) with controllable clock, induced crashes, slow/partitioned peers, forced concurrent seals. After each op, projects observable state (ledger reads, API responses, OTel signals) and checks the Invariant Catalogue; where feasible asserts model⇄implementation refinement. This is where Aspirational invariants get teeth and where the "distributed paths only tested locally" gap closes.

### 3e. Thin Surface Smoke
One presence-check per `Surface` capability (CLI command → expected ledger effect; PWA enrol → device registered; `/.well-known/*` served). No deep UI behavior.

### 3f. Two-Tier Verdict
- **Gate** (CI-blocking): all `Gate` invariants hold across the model run; TLA+ core passes; harness happy-path + bounded-fault run green; **100% of `Implemented` capabilities covered**.
- **Tracked-red** (dashboard, non-blocking): each `Aspirational` invariant's pass/fail + issue link — the live v1 backlog. Red→green = a gap genuinely closed; a Gate invariant red = release-stopper.

One run, two readings.

## Build order (model-first; value at every phase)

| Phase | Deliverable | Infra | Notes |
|---|---|---|---|
| **P0 — Pillars** | Capability Registry (143 → ~70–90 capabilities) + Invariant Catalogue, declarative | None | The abstract coverage map + executable spec; supersedes the stale May-16 roadmap; Aspirational set = v1 backlog |
| **P1 — Reference Model** | Pure C# `ISorchaModel` + curated mega-sequence + invariant predicates; 100% capability coverage at model level | None | The "ultimate proof" in abstract runnable form; design correctness from day one |
| **P2 — Property-based driver** | CsCheck generator over the model; shrinking; pinned seed corpus | None | Flushes design-level races the scripted path misses |
| **P3 — Formal consensus core** | TLA+ spec of I-CHAIN/I-CONSENSUS/I-CONVERGE + TLC check + C#↔TLA+ sync test | TLA+ | Exhaustive safety proof of the #119/#787/#917 space; parallel with P2 |
| **P4 — Fault harness** | Conformance harness: black-box happy path (reuse walkthroughs), then fault injection (clock, crashes, slow/partitioned peers, concurrent seals) | Testcontainers/Aspire | Implementation correctness; **subsumes deferred #585 / roadmap Q-03 cross-node harness** |
| **P5 — Surfaces + verdict + CI** | Thin UI/PWA/CLI/MCP smoke; two-tier verdict report; CI gate + tracked-red dashboard | reuse P4 | The complete gated instrument |

**Character:** P0→P1→P2 are pure-abstract (no infra — land fast, low risk). P3 parallelizes. P4→P5 are the heavy lift and *are* the cross-node test infra the assessment flagged as the highest-leverage v1 investment.

**Stopping points:** after P0 — coverage map + correctness backlog; after P2 — full design-correctness proof with coverage; after P4 — implementation-correctness under faults.

**Readiness linkage:** P0–P2 suffice to make a credible *controlled single-validator v1* claim. P4 is what flips the multi-validator/durability/replication Aspirational invariants from tracked-red to gate-able — i.e. what would let you make a credible *public multi-validator v1* claim.

## Non-goals
- Not a replacement for the existing unit suite or walkthroughs — it sits above them and reuses the walkthrough infra in P4.
- Not deep UI/UX behavioral testing (surfaces get presence-smoke only).
- Not a realistic business-scenario demo (scenarios are synthetic, engineered for coverage).
- Does not itself *fix* any correctness gap — it makes gaps loud and gate-able.

## Open questions for spec phase
- Exact capability count after dedup (P0 will produce the authoritative list).
- TLA+ bound sizes for tractable TLC runtime.
- Whether P4 runs nightly (heavy) vs per-PR (gate subset) — likely gate subset per-PR, full fault matrix nightly.
- Where the C# model + harness projects live in the solution (`tests/Sorcha.ConformanceOracle.*`?).
