# Session Resume Note — 2026-05-09

**For:** picking up Feature 119 work on a fresh machine after the 2026-05-08/09 session that took the implementation from spec → end-to-end-walkthrough-passing.

This file is committed to the branch so the resume context travels with the code, independent of machine-local auto-memory.

---

## Status at session close

- **Branch:** `119-presentation-seal-ordering`, tip `db9bd6de` (this file may push the tip past that — check `git log -1`).
- **Draft PR #584** open against master: <https://github.com/Sorcha-Platform/Sorcha/pull/584>
- **Walkthrough verified ONCE end-to-end** on the previous machine. AssuredIdentity Phase 1 + Phase 2 complete in 2:00. Step 6 reports `Action 3 ready (waited 2s)` — the FR-015 advancement queued in the seal coordinator drains correctly when the outcome tx seals.
- **Three races closed.** Two designed (VAL_CHAIN_001 outcome-before-initiated-seal; VAL_BP_003 next-action-races-outcome). One newly discovered + fixed (VAL_BP_003 reflexive-route on outcome→initiated chain — was previously masked by VAL_CHAIN_001 firing first).

## Read these in order

1. **`EXECUTION-DEVIATIONS.md`** in this folder — the forensic trail. Three deviations recorded: (1) the `Sorcha.Storage.InMemory.Redis` test double prescribed in research R8 doesn't actually exist; (2) the `sorcha-architecture` skill drop-in (sandbox-blocked during executor agent, applied manually after); (3) the newly-discovered VAL_BP_003 reflexive bug + the forced-into-being validator-side fix. The "Resolution 2026-05-09" subsection on deviation 3 carries the trace of why option A (Blueprint-only) was impossible — three failed attempts on the dead-code `BuiltTransaction.ToTransactionModel()` path before pivoting to option B (validator carve-out at `Sorcha.Validator.Service/Services/ValidationEngine.cs`).
2. **`spec.md`** in this folder — feature spec. The non-goal "Validator chain rules (VAL_CHAIN_001, VAL_CHAIN_FORK, VAL_BP_003) are unchanged" no longer holds. Update before marking PR ready, or call out in PR body.
3. **`docs/superpowers/specs/2026-05-08-feature-111-chain-races-design.md`** — original design doc.
4. **`.claude/skills/sorcha-architecture/SKILL.md`** — search for "Seal-aware ordering (Feature 119)". Carries the cross-cutting pattern + the validator carve-out note.

## Resume checklist (do in order)

### A — Bring up a fresh local Docker stack and run the formal 10× walkthrough

This is the SC-119-001 verification. The single-run pass on the previous machine is **not 10/10 evidence**. Use the procedure in `quickstart.md` step 2.

**Stale containers will bite you.** At minimum these need to be rebuilt before the walkthrough:

- `tenant-service` — must have PR #580's `HaipServicePrincipalId` seed in `DatabaseInitializer`. Symptom if stale: HAIP service-token request gets HTTP 401.
- `haip-service` — must have `IServiceAuthClient` registered. Symptom if stale: `Unable to resolve service for type 'IServiceAuthClient'` on PresentationCallbackRelay activation.
- `validator-service` — must have the Feature 119 VAL_BP_003 carve-out. Symptom if stale: `VAL_BP_003: Action 2 is not reachable from action 2 via blueprint routes` after the outcome tx submits.
- `blueprint-service` — must have the Feature 119 seal coordinator + the DI captive-dependency fix (`a31619e5`). Symptom if stale: blueprint-service crash-loops on startup with `InvalidOperationException: Cannot consume scoped service ... from singleton 'IPresentationSealCoordinator'`.

**`docker compose up -d --force-recreate` is unreliable** when the image tag stays the same — it sometimes claims success without actually replacing the container. Always verify with `docker ps --format '{{.Status}} | {{.Image}}'` that Status age is seconds, not days. If a container shows old Status, force the swap explicitly:

```powershell
docker compose stop <service>
docker compose rm -f <service>
docker compose up -d --no-deps <service>
```

This bit during the previous session — burned ~30 minutes against stale code that I assumed was new.

### B — Update spec.md to reflect the validator change

`spec.md` non-goal "Validator chain rules ... are unchanged" no longer holds. The carve-out is documented in `EXECUTION-DEVIATIONS.md` and should be reflected in the spec's Scope section. Either revise the non-goal or remove it. Mention in the PR body too.

### C — Decide what to do about the deferred items

`EXECUTION-DEVIATIONS.md` deviation 1 lists the deferred test work: T009 obligations 6-8 (sweeper recovery, TTL fail, restart safety), T017-T021 (US2 observability + integration), T029 (restart-safety integration test). All are blocked on a real-Redis fixture pattern that doesn't exist in the codebase yet — `RegisterEventBridgeServiceTests` is the closest analog.

Two paths:

- **Land them in this PR.** Build a small real-Redis test fixture mirroring the `RegisterEventBridgeServiceTests` shape, port the deferred tests onto it, ship as part of #584.
- **Open a follow-up issue and ship #584 with them deferred.** PR body should explicitly call out the gap.

### D — After A/B/C: `gh pr ready 584` to flip from draft

### E — SEPARATE FOLLOW-UP (do AFTER #584 merges, not in this branch): validator rulebase audit

Feature 119 exposed that VAL_CHAIN_001 was masking a latent VAL_BP_003 reflexive bug — the validator's chain rules have hidden coupling that could bite again next time the chain shape changes. Specific things to look at:

- Classify all `VAL_BP_*` and `VAL_CHAIN_*` rules as chain-integrity vs workflow-integrity vs lifecycle-terminal-exempt.
- Introduce an `IsLifecycleTransaction` helper in `ValidationEngine.cs` (around lines 1275-1290) alongside `IsRejectionTransaction` / `IsParticipantTransaction` / `IsControlTransaction`. Then refactor the carve-out at the new line ~1148 to use it.
- Audit `DocketBuildTriggerService.cs:591` — the unconditional `t.ActionId → MetaData.ActionId` projection. It owns persistence, so any future "skip this metadata field for tx type X" must be applied here, NOT in Blueprint Service. Worth a comment block to document this constraint.
- Document or delete the dead-code `BuiltTransaction.ToTransactionModel()` path in `ITransactionBuilderService.cs:711`. It's only called by `MongoDocumentMapper`, NOT on the production write path. Three iterations of fix attempts during this session went into editing it before realising. Either rename to make the limited scope obvious (e.g. `ToMongoTransactionModel`), delete entirely if MongoDB write path is also redundant, or add a header comment block warning the next person.

## Pre-existing failure modes to ignore

Per CLAUDE.md and the project's accumulated notes, these are not Feature 119's concern:

- `IssueCredentialStatusListUrlGuardTests` x3 failures
- `Blueprint.Service.Tests` ConfigurationBinder NRE x81
- `Validator.Service.Tests` x30
- `build-and-test` CI flake (issue #511 — recurring genesis-test flake; rerun `gh run rerun <id> --failed` if it hits)

## Branch + PR conventions

- Master is protected — never push directly. Feature branch + PR only.
- Commits should carry the `Co-Authored-By: Claude Opus 4.7 (1M context) <noreply@anthropic.com>` trailer.
- Stage files explicitly by name. **Never `git add -A` or `git add .`**.
- Do NOT commit `.claude/settings.local.json` (user-local) or `docs/strategic-context-for-claude.md` (pre-existing untracked).

## Side-effect rebuilds done in the previous session (carry forward)

These services were rebuilt + recreated during the previous session and have local image content for the latest master + Feature 119:

- `tenant-service`, `haip-service`, `validator-service`, `blueprint-service`

These are still 4 days old (from the previous master tip) and may need refresh for cleanliness:

- `register-service`, `wallet-service`, `peer-service`, `api-gateway`, `ui-web`, `citizen-wallet`, `citizen-verifier`

They didn't surface bugs in the verification, but you may want to refresh the whole stack before merge.
