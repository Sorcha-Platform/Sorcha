# Sorcha — Feature 125 Implementation (Handoff)

> Paste this into a fresh session after `/clear` (or `cat` it from this file) to bootstrap implementation of Feature 125.

You are picking up the implementation of **Feature 125 — Sorcha Wallet (Full User-Agent v1)**, the second sub-spec of the Strathcarron citizen arc. The spec, plan, research, data-model, contracts, quickstart, and 131-task list are all written and committed on branch `125-sorcha-wallet-user-agent`. Your job is to execute them.

## Step 1 — Load context

Before touching code, do these in parallel:

1. **Skills**: load `sorcha-architecture`, `sorcha-ui`, `dotnet`, `blazor`, `minimal-apis`, `scalar`, `entity-framework`, `postgresql`, `redis`, `jwt`, `signalr`, `aspire`, `docker`, `frontend-design`, `playwright`, `xunit`, `fluent-assertions`, `moq`, `walkthrough-builder`, `superpowers:test-driven-development`, `superpowers:verification-before-completion`. Note that several skills (`blazor` / `playwright` / `redis` / `sorcha-ui`) were recently updated with the four bug-class lessons from Feature 124 hotfixes (#697 IDistributedCache wiring, #698 PWA NavigateTo leading-slash, #699 nginx immutable on entry-point JS, #701 locator-without-click anti-pattern). Read those updates carefully — they are how Feature 124 surfaced production bugs that the spec architecture is now designed around.
2. **Memory**: read `MEMORY.md` (auto-loaded). Pay particular attention to `project_feature_124_pwa_assured_identity` — that's the predecessor spec's lessons. Also read user-workflow-preferences and feedback memories.
3. **Branch**: `git fetch && git checkout 125-sorcha-wallet-user-agent && git pull`. Confirm you're on the branch.
4. **Read in this order**:
   - `docs/superpowers/specs/2026-05-13-strathcarron-citizen-arc.md` — umbrella, locks Sarah as protagonist, sequences the arc's specs
   - `docs/superpowers/specs/2026-05-14-spec-2-sorcha-wallet-user-agent-design.md` — Spec 2's design rationale (the *why* and architecture detail kept out of the speckit spec)
   - `docs/superpowers/specs/2026-05-10-user-agent-unification-design.md` — predecessor design that Spec 2 supersedes (read for context)
   - `specs/125-sorcha-wallet-user-agent/spec.md` — 6 user stories with priorities (3 × P1, 3 × P2), 36 functional requirements, 10 success criteria
   - `specs/125-sorcha-wallet-user-agent/plan.md` — technical context, constitution check, project structure with file-by-file source-code layout
   - `specs/125-sorcha-wallet-user-agent/research.md` — 11 resolved research items (note R-002 IUserSigner location, R-004 persona schema delta, R-011 single-PR rename are load-bearing)
   - `specs/125-sorcha-wallet-user-agent/data-model.md` — 5 new client-side stores + 1 server-side column + IUserSigner interface contract + state machines
   - `specs/125-sorcha-wallet-user-agent/contracts/per-context-persona.openapi.yaml` — the one new server-side contract
   - `specs/125-sorcha-wallet-user-agent/quickstart.md` — runbook for the three demo beats (use for final SC verification)
   - `specs/125-sorcha-wallet-user-agent/tasks.md` — your work list (131 tasks across 9 phases)

## Step 2 — Background and outcomes

**Why this matters.** Spec 1 (Feature 124, shipped 2026-05-14 as `spec-124-complete`) delivered the first-credential welcome takeover and switched the AssuredIdentity walkthrough to PWA delivery. Spec 2 takes the now-renamed **Sorcha Wallet** (was Citizen Wallet) and grows it from credentials-only to the **full end-user agent** for any Sorcha user on the move. Three headline demo beats anchor the spec:

1. **Doorstep verification** — an elderly homeowner verifies the gas engineer's credential at her door (citizen-as-verifier; inverts the credential conversation; safety-net value proposition).
2. **Application from phone** — Sarah submits her council application via the wallet, with portrait camera capture and persona autofill, instead of using the web shell.
3. **Context switching** — Ben-the-construction-worker has Personal + Caledonian Builders Ltd memberships; one wallet, two personas.

**The ten brainstorm-locked decisions** (cannot be re-litigated):

1. Naming: rename `Sorcha.Citizen.Wallet` → `Sorcha.Wallet.Pwa`, `Sorcha.Citizen.Verifier` → `Sorcha.Verifier`. URL stays `/wallet/`. App is "Sorcha Wallet" in user copy.
2. Managed-mode = v1 default (formalises today's hybrid: server-anchored holder key + browser-local device key + delegation).
3. Self-custody opt-in deferred to v2.
4. `IUserSigner`-style seam in v1 — single abstraction; only managed implementation lands; self-custody slots in later without UI rewrites.
5. Scope = full user-agent v1 (credentials + form submission + photo/file + persona + history + devices/auth + verify).
6. Role-neutral copy; demos exercise multiple personas (Sarah / Ben / Margaret).
7. Multi-context UI = active chip with peek (the design doc § 5 has the layout).
8. Home IA = multi-section dashboard with hero **Present** + hero **Verify** above Needs-attention / Credentials / Recent bands.
9. Verify is verify — one capability, multiple shells. Wallet adds doorstep verification; `Sorcha.Verifier` desk shell stays for counter / back-office.
10. Demo narrative carries all three beats; no single headline.

**Cross-cutting invariants** (do not violate):

- The umbrella's invariants from `2026-05-13-strathcarron-citizen-arc.md` hold. Sarah remains the citizen-arc protagonist; new personas are auxiliary.
- Per-context content scoping is enforced **server-side** via JWT `org_id` claim from `/auth/switch-org`. Client-side filtering is presentation-only, NOT a security boundary.
- The shared component library (`Sorcha.UI.Components.User`) is the contract. PWA and web shell consume it. No forking; no inline reimplementation. The audit at T120 measures library-consumption ≥ 90% on the PWA.
- The four Feature 124 hotfix lessons are now codified in skill docs — re-reading them is part of Step 1. They will save you from repeating the same bug classes.
- The pre-release migration-squash rule applies to T021 (the new `PlatformUserPersona.ContextOrgId` column folds into InitialCreate).
- The verifier identity is **ephemeral per session**, not platform-registered. R-006 has the rationale.

## Step 3 — Execution rhythm

Work the 131 tasks in `specs/125-sorcha-wallet-user-agent/tasks.md` in phase order. **Group them into six PRs at the cut points the plan defines**, not one PR per phase and not one PR for everything:

| PR | Scope | Tasks | Branch off |
|---|---|---|---|
| **PR-A** | Phase 1 + Phase 2 — Setup + Foundational (rename + abstractions + persona schema) | T001–T030 | `125-sorcha-wallet-user-agent` |
| **PR-B** | Phase 5 (US3) — Home IA rebuild + multi-context UI | T066–T083 | merged PR-A |
| **PR-C** | Phase 3 (US1) — Doorstep verification | T031–T049 | merged PR-B |
| **PR-D** | Phase 4 (US2) — Application from phone | T050–T065 | merged PR-C |
| **PR-E** | Phase 6 + Phase 7 (US4 + US5) — History + Devices + Auth | T084–T103 | merged PR-D |
| **PR-F** | Phase 8 + Phase 9 (US6 + Polish) — Tour + audits + #700 closure + docs | T104–T131 | merged PR-E |

PR ordering rationale: PR-A is foundational (rename + abstractions); PR-B before PR-C because the Home IA rebuild is where US1/US2 hero actions visually land; PR-C ships the most differentiating beat first; PR-D / PR-E / PR-F layer on. PR-A is special — it MUST validate the F124 test suites pass post-rename (SC-006 baseline) before merge.

For each PR:

1. **Implement** the tasks for that PR. Atomic commits per logical group. Tests live alongside code (constitution Principle IV "Write tests alongside code").
2. **Verify before claiming done** — invoke `superpowers:verification-before-completion`. Run `dotnet build` clean, run `dotnet test` for affected projects clean, manually exercise any new UI surface. For PR-A specifically, the gate is "all F124 tests still pass" (T030 / SC-006).
3. **Commit and push**. Title: `feat(125): <PR scope>` (PR-A is `feat(125): PR-A — rename + foundations`). Body: bullets of changes + verification evidence (paste test counts) + checklist of which `tasks.md` task IDs the PR closes.
4. **Open the PR** to base `125-sorcha-wallet-user-agent` and immediately let `claude-review` (the auto-runner) review.
5. **Process review** with a hard cap of **2 rounds**:
   - **Round 1**: address every Critical / High finding. Group Minor / Nit findings into one batch — fix any that align with project style; ignore the rest with a one-line PR comment explaining why. Push as one or two commits.
   - **Round 2** (if needed): address remaining Critical / High only. Don't chase Minors a second time.
   - **After round 2**: if a Critical finding remains unresolved, raise it in the PR conversation, open a follow-up issue, and merge with the link in the merge commit.
6. **The user's standing instruction is "wait for the review then merge" — option 1 every PR.** Don't ask which path; the standing answer is wait-then-merge.
7. **Merge** (squash). Verify the merged commit on the feature branch passes the parent CI; if CI fails on the feature branch, treat it as a P0 and fix-forward before opening the next PR.
8. **Pull the feature branch locally**, branch off again for the next PR, continue.
9. **After PR-F merges** to `125-sorcha-wallet-user-agent`: open the final feature → master PR. Same review-wait-merge discipline. Tag the merge commit `spec-125-complete`.

**Stop conditions** — pause and ask the user before proceeding if:

- Any constitution gate fails post-implementation (re-evaluate against `plan.md`'s Constitution Check).
- F124 test suites start failing post-rename in PR-A — this is SC-006 and a release-gate.
- The persona migration causes existing F092 persona reads to break — the schema delta is supposed to be backward-compatible (NULL ContextOrgId = Personal context).
- You discover the umbrella decisions or the ten brainstorm decisions need amending (should not happen for Spec 2 but explicit escape hatch).
- The `IUserSigner` abstraction leaks custody-mode awareness into consuming UI — re-read R-002, the seam must stay clean.

**Don't ask permission for**:

- Trade-offs explicitly resolved in `research.md`'s 11 items.
- Style choices that follow existing Sorcha patterns (codebase is source of truth; the architecture + sorcha-ui skills carry the cheat-sheet).
- Small additions to tests beyond what `tasks.md` enumerates — coverage is better than too little.
- Adding `data-testid` attributes to new nav elements you create (the discipline from PR #701 — every nav element gets a testid).
- Closing any unresolved Minor / Nit review findings with a one-line "intentional" comment if they don't align with project conventions.

## Step 4 — When done

When PR-F merges to `125-sorcha-wallet-user-agent`:

1. Open the final PR `125-sorcha-wallet-user-agent` → `master`. Description summarises all six sub-PRs, lists the three demo beats, links to the `quickstart.md` runbook for SC-001..SC-010 verification.
2. After it merges: deploy the new images to n1.sorcha.dev (use the `n1-deploy` skill). Same caveats as Spec 1 — the wallet's URL stays `/wallet/`; existing browser caches may need clearing on return visit, but #699 prevents future caches from sticking.
3. Run the full `specs/125-sorcha-wallet-user-agent/quickstart.md` runbook end-to-end against n1. Record SC-001..SC-010 outcomes in the PR description or as a follow-up comment.
4. Tag the merge commit `spec-125-complete`.
5. Update `MEMORY.md > Current Branch` to point at the next sensible target — likely Spec 3 (enrol-inside-wizard seam) or "between specs" if no immediate continuation.
6. Close issue #685 (the F125 placeholder) and issue #700 (PWA test-coverage tracker — Phase 2 closure is in PR-F).
7. Post a one-paragraph summary back to the user: what shipped, what's in follow-up issues, what Spec 3 should consider given anything you learned along the way.

## Operational notes

- **PowerShell**: 7.5+ (`pwsh`). Never Windows PowerShell 5.1.
- **Push policy**: always push when committing.
- **Branch hygiene**: master is protected. Every change via feature branch + PR.
- **Tests**: hit >85% on new code. xUnit + FluentAssertions + Moq. Mock IndexedDB / WebCrypto via the small-interface pattern documented in the sorcha-architecture skill's Feature 114 test-patterns section.
- **Constitution principle VIII (Observability)**: T043 / T059 / T078 / T109 specify new OpenTelemetry counters — don't skip them. Structured logs on all new endpoint handlers.
- **The rename PR (PR-A)** is the one piece that, if missed or done sloppily, makes the entire next five PRs collide with master. Run it as a single atomic commit; let CI prove F124 suites pass; only then move on.
- **The persona schema migration (T020 - T021)** is load-bearing for US2 (form autofill) and US3 (per-context persona). Get the squash right per the `feedback_migration_squash` memory.
- **Three Playwright `[Demo("<beat>")]`-tagged tests** are the demo verification gate — make them executable as a group with `dotnet test --filter "Demo"`.

You have everything you need. Start by loading the skills and reading the umbrella + Spec 2 design doc + spec + plan + research + data-model + tasks in that order, then begin PR-A.

Standing instruction: always wait for `claude-review` to land before merging each PR. The user expects this without prompting.
