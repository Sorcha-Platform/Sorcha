# Kickoff — Blueprint lifecycle: the rules, written down and made true

**Date:** 2026-08-24
**Predecessor:** Feature 194 (blueprint version pinning), issue #1559, PRs #1561/#1562/#1564/#1565
**Shape of the work:** investigation and design first, then a decision, then targeted change.

Paste everything below the line into a fresh session.

---

Load these skills before anything else:

```
/blueprint-builder        — authoring surface, validation codes, publish rules, the x-* extensions
/sorcha-architecture      — F142 lifecycle + exec-def hash, F145 ledger-derived instances,
                            F184 RoutingDecision, F194 version pinning
/n1-deploy                — deploy scope, the traps, how to verify a deploy honestly
/walkthrough-builder      — the live harness, cadence rules, and the conformance-check pattern
/superpowers:brainstorming — this is design work before it is implementation work
```

Also read memory: `f194-version-pinning-live`, `feedback_verification_discipline`,
`seam-bugs-nothing-verifies-the-join`, `designer-cold-probe-findings`,
`migration-policy-release-switch`, `n1-current-state`.

**Read these first — they are the evidence base, and they were written from verified code and live
runs, not from assumption:**

- `specs/194-blueprint-version-pinning/` — spec, **research.md** (11 decisions, several of them
  corrections to earlier readings), plan, contracts, tasks
- `docs/superpowers/specs/2026-08-23-blueprint-version-pinning-design.md` (§7 rollout was corrected
  during implementation — read the correction note)
- Issues **#1563**, **#1558**, **#1370**, **#1466**

---

## What you are investigating, in one sentence

**A blueprint's lifecycle — authoring, validation, publication, versioning, execution, amendment,
recovery — has grown by accretion, and nobody has written down the rules; the goal is to establish
what they actually are, decide what they should be, and close the gap.**

## Why this is not a documentation exercise

Feature 194 spent a session inside this lifecycle and found that several of its load-bearing
mechanisms are **accidents rather than decisions**. Each was invisible until something depended on
it. The list below is evidence, not speculation — every item was verified against source or observed
on n1 on 2026-08-23.

### 1. "Version" means at least four different things

| Thing | Where | Stable? |
|---|---|---|
| `Blueprint.Version` | the model, a settable `int` defaulting to 1 | author-editable, means nothing |
| `PublishedBlueprint.Version` | assigned `versions.Count + 1` at publish | **re-derived from scratch on every recovery** |
| `ExecDefHash` (F194) | content hash of the executable definition | stable by construction |
| `contentHash` | sealed on the publish transaction, over the whole JSON | stable, but includes presentation |

F194 made the exec-def hash the pin and demoted the ordinal to a display label. It did **not**
reconcile the other three, and `contentHash` vs `ExecDefHash` remain two content hashes of the same
artefact with different inclusion rules and different owners.

### 2. A definition lives in three homes with three different durabilities

- **Draft** — Postgres `BlueprintDrafts`, node-local, editable, and **preferred by
  `ActionResolverService.GetBlueprintAsync`** over anything published.
- **Published** — `InMemoryPublishedBlueprintStore`, a singleton, **lost on every restart** and
  rebuilt by `BlueprintRecoveryService` from the register.
- **On the ledger** — the publish transaction, the only durable copy.

Nothing states which is authoritative for what. F194 had to decide, per call site, and the answer it
reached (*the execution path resolves the pinned published definition; authoring surfaces resolve
latest*) is currently expressed only in F194's code and comments.

### 3. The register holds ONE definition per blueprint id — and the reason is an accident (#1563)

`blueprint-publish-{registerId}-{blueprintId}` is the publish transaction id. It is version-blind,
so a republish deduplicates to the same transaction and is **silently dropped**: `200` returned,
`Successfully published blueprint … to register` logged, no transaction written.

⚠ **That same id is also the anchor a starting action chains from**
(`ActionExecutionService.ComputeBlueprintPublishTxId`, called at `:459`). One identifier, two jobs,
two homes for the derivation. Content-addressing the register side alone makes every new instance
fail `VAL_CHAIN_001` — this was prototyped, found, and reverted rather than shipped.

**This is the single biggest open question in the lifecycle**: what does a starting action anchor
to, and should the anchor and the publication record be the same transaction at all?

### 4. Two publish paths that do different things

- `PublishService.PublishAsync` — validates, flattens `$ref`s, snapshots, hashes, caches, pushes.
- Instance creation (`Program.cs`, the owner branch) — pushes the **draft** straight to the register
  with different serializer options and **no flattening**.

So the same blueprint can reach the ledger in two different shapes depending on which path ran.

### 5. Publish-time validation is two surfaces that disagree, and the stricter one is not the gate

`validate_blueprint` (chat) and `POST /publish` enforce overlapping-but-different rule sets — the
`blueprint-builder` skill documents the split and the history (#1548). **#1558 is still open: a
blueprint that can never advance past its starting action is accepted onto a register**, because
unreachability is a warning, not an error.

### 6. Hand-maintained projections of `TransactionMetaData` keep losing fields (#1370)

The docket→register projection **exists twice with different field coverage**, and the second copy
silently drops `RoutingDecision` and `InstanceId`. F194 has just added a field to exactly that type,
so the blast radius of #1370 is now larger. Related and confirmed live: an out-of-date
`register-service` **silently dropped the new pin from the typed metadata** while it survived in the
tracking JSON — the deploy scope in F194's design was wrong, and nothing failed loudly.

### 7. Dead machinery that looks alive

`IBlueprintVersionResolver` (deleted by F194) had **zero** production callers and would have
returned a version number attached to the latest definition. `InstanceIdentity.Derive` — F145's
deterministic instance id — still has **zero callers**; instance identity is a GUID minted by
`POST /instances`. Both are the shape where the next person builds on something that was never
wired.

---

## Decisions already taken — do not relitigate

These are settled and the investigation should build on them, not reopen them:

1. **An in-progress instance always runs the definition it started on.** Migrating a running
   instance forward is out of scope (F194 D1).
2. **Publishers upgrade freely.** No platform-level multi-party gate; where sign-off is wanted it is
   authored as a governance blueprint (F194 D2/D3).
3. **The pin is the executable-definition hash, not the ordinal** (F194 D4).
4. **Pre-release migration policy**: fold schema changes into `InitialCreate`, recreate databases;
   this inverts at release (CLAUDE.md §19, #1365).

---

## What the investigation has to produce

### A. The rules, written down

A single authoritative description of the blueprint lifecycle covering, at minimum:

- **Authoring** — draft, chat/designer, template seeding, the fluent API, and which of these are
  first-class vs legacy.
- **Validation** — the two surfaces, which rules each enforces, which are errors vs warnings, and
  *why*. Reconcile or justify every divergence.
- **Publication** — what a publish means, what reaches the ledger, what the F142 gates
  (governance-hard, rehearsal-soft) guarantee, and what the two publish paths should collapse to.
- **Identity and versioning** — the four "version" concepts above, reduced to the smallest set that
  is actually needed, with one owner each.
- **Execution** — which definition an action is validated against, and by whom (engine at submit,
  validator at seal), including the pin.
- **Amendment** — the F142 `from-published` clone loop and its lineage metadata.
- **Recovery and durability** — what survives a restart, what survives a re-genesis, and what is
  reconstructible from the ledger versus lost.

State, for each stage, **what is true today** and **what should be true** — separately. Do not blur
them; the gap is the deliverable.

### B. A decision on the anchor (#1563)

The blocker. Options to evaluate, at least:

- Content-address the publish transaction **and** the anchor, with the derivation in **one** home
  (a shared leaf — CLAUDE.md §15/§16 exist because this class of value keeps getting two homes).
- Separate the publication record from the chain anchor entirely.
- Anchor a starting action on the instance's pinned definition's publication.

Each needs: what breaks, what migrates, and what an already-anchored instance does.

### C. A list of the accidents worth keeping

Not everything above is a defect. Some are reasonable and merely undocumented. Say which, and why —
a rule written down is worth as much as a rule changed.

### D. Issues filed or updated

At minimum #1563 (with the decision), #1558, #1370. File anything new the investigation surfaces
rather than carrying it in prose.

---

## Definition of done

- A design document under `docs/superpowers/specs/` covering A–C above, written from **verified
  code and live observation**, with file:line citations for every claim about current behaviour.
- The `blueprint-builder` and `sorcha-architecture` skills updated wherever the investigation finds
  them stale or wrong — in **both** directions (they have been wrong optimistically and
  pessimistically before).
- `MASTER-TASKS.md` updated.
- A decision recorded on #1563 with its migration story, even if implementation is deferred.
- **If you change behaviour**: every guard mutation-tested, and the live check run on n1. A green
  suite is not evidence — see below.

---

## Learnings from the F194 session — carry these forward

### What worked, and is worth repeating

- **Verifying the design against source before writing the spec.** It found five things the design
  did not have, two of which changed the shape of the work. The design was written carefully and
  was still incomplete; that is normal, not a criticism of it.
- **Writing the reflection-driven guard BEFORE adding the field it guards.** The discriminating
  mutation — add the property, omit it from `ComputeSignableBytes` — failed **exactly one** test
  while all 391 others stayed green, *including* the two hand-written per-field tests. That is the
  whole argument for reflection over a list, demonstrated rather than asserted.
- **Mutation-testing every guard.** Six mutations, each killing a named test, each recorded. Two of
  them changed the design (see below).
- **Reading logs instead of theorising.** Every real answer in the live phase came from a log line
  or a Mongo query. Every wrong turn came from inference.
- **Letting the tool answer.** When the EF migration failed, `migrations add` a throwaway probe and
  reading what EF generated settled in one step what two rounds of guessing had not.
- **Reverting a fix rather than shipping it.** The #1563 one-liner looked right, built clean, and
  would have broken every new instance.

### What cost time, and should not be repeated

- **I hand-wrote an EF snapshot instead of generating it.** CLAUDE.md §19 says update `Designer.cs`
  AND `ModelSnapshot.cs`; I did, and the deploy still failed — because writing them is not enough if
  what you write is not what EF would **generate**. `dotnet ef migrations has-pending-model-changes`
  is the cheap pre-deploy check. **Add it to the deploy runbook.**
- **The container reported HEALTHY while durable storage was broken.** `Program.cs` catches
  `MigrateAsync` failure and logs it. Health status proves nothing about migrations; read the log.
- **I re-keyed an entire cache before understanding why it was keyed that way.** It broke 40 tests,
  and that was a *design signal*, not churn: system blueprints have no instance and therefore no
  pin, so an id-keyed tier is genuinely required. Reverted and replaced with two explicitly-named
  key shapes.
- **One of my own assertions passed VACUOUSLY** — "instance B is refused the v1-shaped payload" was
  green because B was too early to accept anything, not because it was refused. A test that can pass
  by timing accident is not testing what it claims. Be most suspicious of a green assertion in a run
  where its siblings are red.
- **I discarded HTTP error bodies for three diagnostic cycles.** `400 (Bad Request)` is not a
  diagnosis. Capture the body from the first attempt.
- **Two of my own research findings were wrong**, both corrected by executing rather than reading:
  the "dead resolver" had zero callers rather than one (a grep matched a field name, not a type),
  and the cache re-key was a wrong design rather than mere churn. **Notes go stale in both
  directions, including notes written an hour ago.**

### Traps banked, live-verified

- **`-WaitForSeal` waits for the SEAL, not the FOLD.** The `AwaitingInbox` gate is mandatory between
  actors under F145; without it you get `400 "Action N is not a current action"`, which at the
  status line is indistinguishable from a schema refusal.
- **A docket-write 409 (#814 integrity guard) puts the docket builder on a ~10-minute retry.** A 90s
  seal wait times out on transactions that do seal. Do not read a seal timeout as a refusal.
  ⚠ These 409s were observed on n1 on 2026-08-23 and their cause is **not established** — the new
  validator went in 15 minutes before the first one, and rolling both validator and blueprint back
  together is the untried counterfactual. Worth settling early.
- **Deploy scope for anything touching `TransactionMetaData` includes `register-service`** — it
  persists and serves that type.
- **`Publish-SorchaBlueprint` always MINTS a new blueprint id** and cannot republish; use
  `PUT /blueprints/{id}` then `POST /publish`.
- Module signatures that each cost a cycle: `Connect-SorchaUser` needs `-OrganizationId`;
  `Confirm-SorchaUserEmail` takes `-UserId`; `New-SorchaWallet` needs `-FetchPublicKey`;
  `Register-SorchaParticipant` needs `-WalletUrl`; `BlueprintUrl` already ends in `/api`;
  `$Args` is a reserved PowerShell automatic variable.

---

## Standing rules that will bite you if ignored

- **Verify by executing.** This lifecycle's defects are all silent: they degrade to plausible
  behaviour, not to errors. Absence of errors is never evidence. Find the positive check — for F194
  it was `pin_fallback` reading zero, not the absence of exceptions.
- **Dry-run a proposed rule against the shipped blueprint corpus before writing the C#** — and know
  its limit: doing exactly that on #1548 validated a rule against blueprints that *exist* and still
  missed a shape none of them had. The live run caught it.
- **`dotnet test` is MTP mode** (global.json): `--filter-class "*Name*"`, `--project x.csproj`;
  `--collect` is dead. Solution-wide local runs report contention failures — judge per project.
- **Use the Write/Edit tools for `.ps1` and `.cs`** — bash heredocs mangle PowerShell backticks and
  `$vars`, and a silently-unapplied patch costs a cycle. Verify the edit landed before running.
- **n1**: the gateway `authentication` limiter is a sliding 1-minute per-IP window; delete
  `walkthroughs/**/state.json` after any node wipe; an `/execute` 202 means accepted, **not sealed**;
  never select a credential by type; `admin@sorcha.local` is multi-org.
- **Confirm before deploying to n1 or opening PRs** — outward-facing and hard to reverse.

---

## Where to start

1. Read the evidence base above. Do not re-derive it; **do** spot-check it — it has been wrong
   before, in both directions.
2. Build the "what is true today" half of deliverable A by reading code, with citations. Expect to
   find more accidents; the seven above are what one feature happened to walk into.
3. Only then brainstorm the "what should be true" half. `/superpowers:brainstorming` first — this is
   a design decision with several defensible answers, not a bug with one.
4. Take the #1563 decision explicitly, because most of the rest depends on it.
