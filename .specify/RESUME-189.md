# Resume prompt — Feature 189, after the merge

Paste everything below the line into a fresh session.

---

Continue the Feature 189 register-governance workstream. **It is merged and live** — this is
follow-on work, not a rescue.

**Load first:** the `sorcha-architecture` skill, and read its **"Register governance (Feature 189)"**
section — it carries the shipped three-transaction model and the traps, and it is the single best
starting point. Recall the memory `f189-approval-surface-workstream` for state and what remains.
`seam-bugs-nothing-verifies-the-join` and `feedback_verification_discipline` carry the failure modes
this feature keeps producing. Load `n1-deploy` if you deploy.

## Where things stand

Merged to master 2026-08-08 as `60d3e990` (PR #1383). 68/96 tasks. n1 runs **CI-built** images.
Propose → approve → enact is **live-proven end to end** on n1, and both live gates pass:

- **T085 substitution** — an approval signed over validator-A is refused `400 SignatureInvalid`
  against a proposal naming validator-B, while the same approver signing the *stored* operation is
  accepted `202`.
- **T086 no regression** — a single-owner register still returns `200`, one propose-and-enact
  transaction, unattended.

Spec Kit artefacts: `specs/189-org-signed-governance/` (tasks.md is current).
Live evidence and a falsification table: `specs/189-org-signed-governance/LIVE-TEST-RUNBOOK.md`.

## Pick up one of these

Roughly in order of value. They are independent; do not batch them.

1. **T079/T080/T081 — validator-side `authorisation`.** Today only the submitting Register Service
   verifies the accountability block. A node receiving a *replicated* approval re-verifies nothing, so
   accountability is verified once rather than per-node. Structurally safe (it rides in the payload and
   is never mistaken for a roster claim), but it is the gap that matters most. T081 records
   `authMethod` on the ledger record so a register can require a minimum standard.
2. **T043/T046 — proposal visibility.** Terminal outcomes with reasons; `GET /proposals` with a status
   filter, and a detail endpoint. **Status must be derived from sealed content, never stored** — a
   stored status is a second source of truth that drifts from the ledger.
3. **The CLI auth blocker (see below), then T082 live.**
4. **T083/T084** — PWA signing surface; console review surface. The console must render the governance
   **diff** (roster before/after), not a JSON blob: approving what you cannot read defeats FR-027 in
   the human rather than in the protocol.
5. **US3 (T054-T057)**, then **US4 (T058-T063)** — the system-register ownership transfer is the
   feature's real acceptance test, and it needs a fresh genesis ceremony plus a coordinated re-genesis
   of n1 **and** tiny inside the 1-hour `VAL_TIME_002` window, followed immediately by an AIAS
   re-provision. Do not start it casually.

## The blocker you will hit first if you touch the CLI

`sorcha auth login` posts an OAuth2 **password grant** to `/api/service-auth/token`. This deployment
needs the two-step `POST /api/auth/login` → `POST /api/auth/select-org` (with the returned
`platform_login_token` and org `00000000-0000-0000-0000-000000000001`). So the CLI cannot authenticate
against n1 at all, `sorcha governance approve` has never been live-exercised, and T082's premise — a
scriptable approver — is not met.

Live runs were driven by a throwaway harness at `.governance-livetest/` (gitignored) that references
`Sorcha.Register.Models` so it gets `GovernanceApprovalStatement.ComputeDigest` for free. **Never
rebuild that canonicalisation by hand** — that is how you produce a digest nothing can verify. Seeded
admin is `admin@sorcha.local` / `Dev_Pass_2025!`.

## Testing standard — this feature has earned it

Across two live runs this feature produced **eleven** defects that thousands of passing tests missed.
The most recent three were found by a single live run of an already-merged-quality branch.

1. **Budget one live run per feature and treat it as part of the work.** A green suite predicts
   nothing here.
2. **Mutation-verify every new guard.** Revert the fix, watch the *named* test go red, restore. A guard
   written after the code has never run red and may be vacuous — and watch for the case that still
   passes: in the encoder fix, the plain-ASCII input passed either way, which is exactly the false
   reassurance that let it ship.
3. **Prefer reflection- or serialisation-driven tests** where a type's shape matters. Hand-written
   field lists rot silently.
4. **`200`/`202` is accepted, never enacted.** Check the docket and the validator verdict, never the
   response body.
5. **Confirm the genesis is in docket 0 before testing** any governance operation. Before it seals,
   `roster == null` admits everything — that race has already produced a false PASS here.
6. **Test against an ordinary register, never the SSR.** It is unique by design and can neither confirm
   nor refute general behaviour.
7. **A red CI check is a hypothesis.** Before merging on the flaky convention, check whether the branch
   touches the failing project at all, then re-run the failed job on the same SHA. Same code, different
   outcome is the proof.
8. **If a fix cannot be proven, revert it and document the dead end** — a better deliverable than a
   plausible change.

## Working agreement

Branch + PR, never commit to master. Stage explicit paths — never `git add -A`; this tree carries
unrelated untracked work. Verify by executing and quote the output; do not report anything as done on
"should work". `dotnet build` before `dotnet test`; `dotnet test` takes ONE project. Deploying
governance means **both** `register-service` and `validator-service` — the enactment gate is in the
Validator, and shipping one produces a half-built chain whose symptoms point at the wrong component.
Tell me when you need a decision rather than assuming one.
