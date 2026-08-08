# Resume prompt — Feature 189 external approval surface

Paste everything below the line into a fresh session.

---

Continue the Feature 189 external governance approval workstream.

**Load first:** the `sorcha-architecture` skill (read its "Register governance: what signs what"
section — it is new and directly relevant), and the `n1-deploy` skill if you deploy. Recall the
memory `f189-approval-surface-workstream` for full state; `seam-bugs-nothing-verifies-the-join` and
`feedback_verification_discipline` carry the traps this feature keeps hitting.

**Where things are.** Branch `189-governance-approval-surface`, PR #1383, CI green, 52/96 tasks.
Spec Kit artefacts live in `specs/189-org-signed-governance/` (spec.md FR-025..FR-035, research.md
R-013..R-023, tasks.md Phases 8 and 9). Design doc:
`docs/superpowers/specs/2026-08-07-governance-approval-surface-design.md`. Continue the speckit
implementation from tasks.md — do not re-plan; the decisions are made and recorded.

**Your next task is T075** — approvals as action submissions of `register-governance-v1`, which
executes for the **first time** (nothing in `src/` has ever instantiated it). T041 is absorbed into
it: R-009 equates "ledger transaction" and "action submission", and the approval's `ActionId` and
payload schema come from the blueprint (T053, done). Everything else — T045 approve endpoint, clients
T082-T084, substitution gate T085 — sits behind T075.

## Testing standard for this feature — read before writing code

A green suite has proven almost nothing here. Across two live runs this feature produced **eight
defects that ~2,500 passing tests missed**, plus **two confident claims of mine that live execution
disproved**. Current state: 290 + 376 + 1054 unit tests green, six guards mutation-verified — and
**only `/propose` has live evidence behind it.**

So:

1. **Mutation-verify every new guard.** Revert the fix, confirm the test goes RED, restore. A guard
   written after the code has never run red and may be vacuous. When restoring a file from a backup,
   `mv` carries the OLD mtime and MSBuild skips the rebuild — `touch` it, or you will read a stale
   pass as a failure.
2. **Prefer reflection-driven tests over hand-written lists** where a type's shape matters. The
   approval-digest test found a fifth unbound field the design had not predicted; a hand-listed test
   would have covered the four I thought of and passed.
3. **A RED test can be a WRONG test — and sometimes the code is wrong instead.** Both happened here.
   Check the test before "fixing" the code.
4. **`200` is not `sealed`.** A governance change takes effect when its transaction lands in a
   docket. Check the docket and the validator verdict, never the response body.
5. **Confirm the genesis is in docket 0 before testing** any governance operation. Before it seals,
   `roster == null` admits everything — that race produced a false PASS in this feature already.
6. **Test against an ordinary register, never the SSR.** The system register is unique by design and
   can neither confirm nor refute general behaviour.
7. **If a fix cannot be proven, revert it and document the dead end.** That is a better deliverable
   than a plausible change — see #1384, where the "obvious" fix silently broke numeric input on a
   nullable enum.

## Live fixture already on n1

Ordinary register `cbb1fa4c1bc942b7a1f86eabcfb96ea6` (DevMode, genesis sealed in docket 0, owner is
the admin's own wallet). Admin wallet exists — created via `POST /api/v1/wallets`, the normal
first-login flow. Login is two-step: `POST /api/auth/login` → `POST /api/auth/select-org` with the
`platform_login_token` and org `00000000-0000-0000-0000-000000000001`.

n1 runs a **local branch build** of `register-service` (`f189-t092`); a `docker compose pull` reverts
it. Unmerged-branch deploy: `docker build` → `docker save | gzip` (~77MB) → `scp` → `docker load` →
retag `:latest` → `up -d --force-recreate --no-deps register-service`. Never `compose pull` after
loading. Compose service names: `sorcha-ui-web` is prefixed, `register-service` is not — a wrong name
aborts `pull` **and** `up` silently, leaving the old container up.

Two API shapes that will waste your time otherwise: `/propose` requires **numeric** enums (#1384), and
`/finalize` needs the **whole `attestationData` object** echoed back.

## Decisions already made — do not reopen

Detached signature over a canonical digest; UI and bot are two clients of one protocol.
Single-owner registers keep the unattended Owner override (so #1380 narrows, not closes). An
autonomous approver is **delegated, not unaccountable** — every approval resolves to a named
individual. Revocation is unilateral. Owner-only granting (T095, service layer — the model layer is a
zero-dependency leaf and cannot see org roles). `authMethod` recorded, not enforced. Interactive
signing windows 15 minutes (T096).

## Working agreement

Branch + PR, never commit to master. Stage explicit paths — never `git add -A`; this tree carries
unrelated untracked work. Verify by executing and quote the output; do not report anything as done on
"should work". `dotnet build` before `dotnet test`; `dotnet test` takes ONE project. Tell me when you
need a decision rather than assuming one.
