# Execution Guide: Feature 138 — Federation Trust Hardening

**Purpose**: How to pick up and execute this feature on a **different machine**. Everything needed travels in this branch — the SpecKit artifacts are committed, so a fresh clone has the full context.

---

## 1. Prerequisites on the new machine

- **.NET 10 SDK** and **Docker Desktop** (running).
- **Claude Code** with the SpecKit skills available (the `/speckit.*` commands) and this repo's `.specify/` toolchain (already in the repo).
- Git access to `github.com/Sorcha-Platform/Sorcha`.

```bash
git clone https://github.com/Sorcha-Platform/Sorcha.git
cd Sorcha
git fetch origin
git checkout 138-federation-trust-hardening   # the branch carries spec + plan + tasks
dotnet restore
docker-compose up -d                            # full local stack (or: dotnet run --project src/Apps/Sorcha.AppHost)
```

> PowerShell 7+ (`pwsh`) on Windows; the `.specify` scripts are under `.specify/scripts/powershell/`.

---

## 2. What's already in this branch

```
specs/138-federation-trust-hardening/
├── spec.md            # WHAT + WHY: 6 user stories (US1-3 P1, US4-5 P2, US6 P3), 22 FRs, 8 success criteria
├── plan.md            # HOW (high level): tech context, constitution check, per-US source files, build sequence
├── research.md        # Grounded decisions per story + current-code file:line integration seams
├── data-model.md      # Entities + fail-closed state transitions
├── contracts/         # Proto changes, control-tx action types, verifier contract, config keys + metrics
├── quickstart.md      # Adversarial validation per story (the acceptance bar)
├── tasks.md           # 72 ordered tasks, grouped by story  ← the execution checklist
├── checklists/requirements.md
└── EXECUTION.md       # this file
```

**Read order on arrival**: `spec.md` → `plan.md` → `tasks.md`. Pull `research.md` / `contracts/` on demand while implementing (they carry the exact file:line seams).

---

## 3. How to execute

### Option A — guided by Claude Code (recommended)

From the repo root on the new machine, in Claude Code:

```
/speckit.implement
```

This reads `tasks.md` and executes tasks in order, respecting the phase gates. Optionally run `/speckit.analyze` first for a cross-artifact consistency check (spec ↔ plan ↔ tasks) before implementing.

### Option B — manual, task by task

Work `tasks.md` top to bottom. The hard rules:

1. **Phase 1 (Setup T001–T002)** then **Phase 2 (Foundational T003–T004)** must complete before the dependent stories.
2. **Tests first**: each story lists its negative tests before implementation — write them, watch them **fail**, then implement until green. This is the FR-021 / SC-008 gate (prove the forged/unsigned/replayed variant is rejected).
3. **Build order = priority**: **US1 is the MVP** (revocation-forgery, verifier-only, smallest blast radius). Then US2, US3 (the P1 wave), then US4, US5 (P2), US6 (P3).
4. **Stop at each checkpoint** and run that story's `quickstart.md` section to validate it independently before moving on.

### MVP-only path (if time-boxed)

`Setup → T003 → US1 (T005–T016) → validate quickstart US1 → ship`. That alone closes the highest-value, lowest-effort gap.

---

## 4. Parallelization (multi-dev or multi-agent)

After Phase 2, the six stories are independent and can run in parallel:

| Owner | Story | Service surface | Tasks |
|-------|-------|-----------------|-------|
| Dev A | US1 + US5 | `Sorcha.Verifier.Engine` | T005–T016, T056–T061 |
| Dev B | US2 | `Sorcha.Peer.Service` | T017–T034 |
| Dev C | US3 | `Sorcha.Validator.Service` + `Register.Models` | T035–T049 |
| Dev D | US4 + US6 | `Sorcha.Blueprint.Service` (+ `Tenant.Service`) | T050–T055, T062–T066 |

US3 is the largest slice (15 tasks) — weight staffing there. `[P]`-marked tasks within a story (distinct files) also parallelize.

---

## 5. Project-specific gotchas (carry these)

- **EF migrations (T026)**: set `$env:ConnectionStrings__Sorcha__Postgres` to any value; do **not** use `--no-build` (produces an empty migration); avoid `migrations remove` if the DB is unreachable.
- **Fail-closed is the invariant**: every new trust decision rejects when it cannot verify. Tests assert *rejection*, never silent fallback.
- **Docker rebuilds**: use `--no-cache` after code changes; `--force-recreate` sometimes doesn't actually swap the image — verify container age, and `stop + rm + up` when it matters.
- **Branch + PR policy**: `master` is protected. Each story is a clean PR boundary — branch off `138-federation-trust-hardening` (or work on it directly), push, `gh pr create`, await review, merge. Don't commit directly to `master`.
- **Demo-mint exclusion (T067)**: the demo/JWK-registry issuer resolver must be *structurally* excluded from production composition, not flag-gated — there's a dedicated test task for this.
- **Docs sync (T068–T070)**: update the `sorcha-architecture` skill, API docs, and affected service READMEs as part of Polish — PRs without doc updates aren't approved (CLAUDE.md policy).

---

## 6. Definition of done

- `dotnet test` green; >85% coverage on new code (constitution IV).
- Every `quickstart.md` adversarial variant has a passing negative test (FR-021 / SC-008).
- `quickstart.md` executed end-to-end against the Docker stack (T072).
- All six stories validate independently at their checkpoints.

---

## 7. Backlog pointer

The **permissionless / open-membership** validation work (Sybil-resistant stake admission, bonded collateral, slashing) is the deliberately-separate follow-up: `PERM-1..PERM-5` in `.specify/tasks/deferred-tasks.md`. US3 here builds the on-chain-roster + deterministic-ejection primitives that feature will extend. Do **not** pull permissionless scope into 138.
