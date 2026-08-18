# Feature 192 — Tasks

**Status:** ⛔ BLOCKED on decision gates A–D in spec.md — do not start T003 onward until they are settled
**Branch:** `feature/192-credential-status-reporting`

Legend: 📋 pending · 🚧 in progress · ✅ done · ⛔ blocked on a gate

Tasks are written for **D2** (a new `CredentialStatusValue` type) and **B2** (distinction reaches the
holder), the recommended answers. If Stuart picks D1 or B1 instead, T003–T005 and T012–T014 change
shape — everything else stands.

---

## Preflight — cheap, unblocked, do first

- 📋 **T001** Re-read `TrustEvaluator.CheckStatusAsync`, `BitstringStatusListChecker.CheckBitAsync`
  and `IetfTokenStatusListChecker.ReadBit` side by side and confirm the reporting-chain table in
  spec.md still matches source. Three PRs have landed at this seam since it was written; verify
  rather than trust it.
- 📋 **T002** Enumerate every consumer of `TrustFailureReason.Revoked` and `CredentialFailureReason.Revoked`
  and record the list in the PR body. Expected in-tree: `CredentialVerifier.MapFailureReason`,
  `HaipPresentationVerifier`, `MdocPresentationVerifier`. **If any consumer does an exhaustive
  `switch`, adding a member is a breaking change for it** — that is what this task is looking for.

## US1 — A verifier can tell a suspension from a revocation — ⛔ Gates A + D

TDD order. T003 must be RED before T005.

- ⛔ **T003** Write `SuspensionIsReportedDistinctlyTests` in `tests/Sorcha.Blueprint.Engine.Tests/Credentials/`.
  Mint a real signed SD-JWT carrying one entry per purpose (the factory already supports this via
  `credentialStatusClaim`, added in #1495) and drive it through `CredentialVerifier` with a
  per-purpose checker. Assert the failure reason for suspension is **not** the revocation reason.
  **Verify RED first** and record the output in the PR body — per the standing rule, a guard written
  after the feature never ran RED and proves nothing until it has.
- ⛔ **T004** Add `CredentialStatusValue { Valid, Invalid, Suspended, Unresolved }` in
  `Sorcha.Blueprint.Engine.Credentials`. Change `IStatusListChecker.CheckAsync` to return it and
  **delete `StatusListBit`** — do not leave both. A parallel type is how the W3C/IETF conflation
  survived in the first place.
- ⛔ **T005** Update the two implementations:
  - `BitstringStatusListChecker` — it already reads the list's `statusPurpose`; map a set bit to
    `Invalid` or `Suspended` from that purpose rather than to a bare `Set`.
  - `IetfTokenStatusListChecker.ReadBit` — return the **entry value** (`0x00`/`0x01`/`0x02`) instead
    of collapsing to set/not-set. This is the read-side half of #1492 and the reason the gap exists.
- ⛔ **T006** Add `TrustFailureReason.Suspended` and map it in `TrustEvaluator.CheckStatusAsync`.
  **Both-set must report revoked** — assert it explicitly; revocation is terminal in both specs and
  reporting the reversible status would imply the credential could come back.
- ⛔ **T007** Add `CredentialFailureReason.Suspended` and extend `CredentialVerifier.MapFailureReason`.
- ⛔ **T008** Update `HaipPresentationVerifier` and `MdocPresentationVerifier` per the T002 list.
  `MdocPresentationVerifier` writes `StatusCheckResult` as a bare string — decide and document what a
  suspension puts there, because that is a verifier-visible wire value.
- ⛔ **T009** Re-run T003; verify GREEN.
- ⛔ **T010** **Mutation check.** Map `Suspended` back to the revocation reason and confirm T003 fails
  and nothing else does. Then map every status to `Suspended` and confirm the revocation tests fail.
  Two directions, because a mapping test can pass by accident in one.
- ⛔ **T011** Full suites: Blueprint.Engine, Blueprint.Service, Haip.Service, ServiceClients.

## US2 — A holder is told the truth — ⛔ Gate B (skip entirely if B1)

- ⛔ **T012** Find where a credential-gated refusal reaches the holder (inbox writer / decision
  notice) and confirm it currently surfaces the revocation wording for a suspension. **Do not assume
  it does** — confirm, and if the suspension case never reaches the holder at all, say so and stop:
  the story is then already satisfied and T013/T014 are unnecessary.
- ⛔ **T013** Author the suspension wording once, alongside the existing revocation wording. It must
  say the credential is suspended and **may be reinstated** — the reversibility is the whole point of
  telling the holder anything different.
- ⛔ **T014** Wire it, and add a test asserting a suspended credential produces the suspension wording
  and a revoked one the terminal wording. Same shape as F186's decision-notice tests.

## Close-out

- ⛔ **T015** Live-verify on n1: issue a credential, **suspend** it, present it, and confirm the
  refusal names suspension rather than revocation — then **reinstate** it and confirm it is accepted
  again. The reinstate leg is the one that proves the reversibility is real end to end, and it is the
  leg no unit test covers.
  - n1 must be carrying #1491/#1492/#1495 first; as of 2026-08-18 it carries only #1490.
- ⛔ **T016** Update `.specify/MASTER-TASKS.md`, the `verifiable-credentials` skill, and the
  `credential-status-list-specs` memory — the "Still open" section of that memory is what this
  feature closes.

---

## Notes for whoever picks this up

- **The enforcement already works.** A suspended credential is refused today (#1495). This feature
  changes only what is *reported*, so a regression here is a truthfulness bug, not a security one —
  but see the next point before relaxing about it.
- **Do not narrow the refusal while renaming it.** #1495 exists because fixing the *reason* removed
  the *enforcement*: an over-broad rule was replaced by a correct one that stopped catching a case
  the broad one caught incidentally. When touching `CheckStatusAsync`, keep a test that a suspended
  credential is still **refused**, not merely refused *differently*.
- **Check for callers.** #1495's root cause was a helper written for "callers that must evaluate
  every purpose" and wired to none. After adding `CredentialStatusValue`, grep that every arm is
  actually reachable from production code.
