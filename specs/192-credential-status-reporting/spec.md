# Feature 192 — Credential status reporting: suspension is not revocation

**Status:** ✅ COMPLETE — drafted 2026-08-18, gates settled A1/B2/C1/D2 the same day, implemented
**Branch:** `feature/192-credential-status-reporting`
**Origin:** #1482 read-side gap, surfaced while sizing the work after PRs #1491 / #1492 / #1495
**Depends on:** #1491 (purposes split), #1492 (IETF projection), #1495 (every purpose evaluated) — all merged

---

## Problem

Both specifications treat suspension and revocation as **different statuses**. Sorcha now *stores*
and *evaluates* them separately, but still **reports** them as one: every set status becomes
`TrustFailureReason.Revoked`.

W3C Bitstring Status List:
> `revocation` — "Used to cancel the validity of a verifiable credential. **This status is not reversible.**"
> `suspension` — "Used to temporarily prevent the acceptance of a verifiable credential. **This status is reversible.**"

IETF Token Status List distinguishes them as values: `0x01` INVALID, `0x02` SUSPENDED.

The reporting chain collapses that distinction at the first step and never recovers it:

| Step | Location | What it carries |
|---|---|---|
| 1 | `IStatusListChecker.CheckAsync` | `StatusListBit` — `NotSet` / `Set` / `Unknown` |
| 2 | `TrustEvaluator.CheckStatusAsync` | `Set` → `TrustDecision.Reject(TrustFailureReason.Revoked, …)` |
| 3 | `CredentialVerifier.MapFailureReason` | `TrustFailureReason.Revoked` → `CredentialFailureReason.Revoked` |
| 4 | `HaipPresentationVerifier` | branches on `TrustFailureReason.Revoked` |
| 5 | `MdocPresentationVerifier` | sets `StatusCheckResult = "Revoked"` |

Step 1 is where it is lost: `StatusListBit` is a tri-state that cannot express three statuses. The
IETF checker already reads a multi-bit entry and then collapses it — `ReadBit` returns `Set` if *any*
bit in the entry group is set, so `0x02` SUSPENDED and `0x01` INVALID are indistinguishable by
construction.

The current message admits the conflation in a string literal:
`"Credential is revoked or suspended."` (now `"Credential status '{purpose}' is set."` after #1495,
which names the purpose in the log but does not change the reason).

### Why this matters, concretely

A suspended credential is **refused** today, which is correct — this is not a security hole. It is a
**truthfulness** problem, and it has two consequences:

1. **A holder is told the wrong thing.** Revocation is terminal; suspension is reversible and usually
   short. Telling someone their credential was *revoked* when it was *suspended* is materially
   misleading — they may re-apply from scratch, or abandon a service they would have regained access
   to in a day. This is the same failure F186 fixed for refused applications, where a refused citizen
   was told their application "completed".
2. **A verifier cannot make a proportionate decision.** A desk verifier may reasonably want to say
   "come back tomorrow" for a suspension and "this credential is void" for a revocation. Today both
   arrive as `Revoked`.

### Prior incidents at this seam

Every one silent, both sides individually correct, found only by live execution or by sizing the next
piece of work:

1. **#1491** — suspension written to the revocation list, so a suspended credential was *advertised*
   as revoked, and reinstate cleared a revocation bit the spec says can never clear.
2. **#1492** — the IETF rail declared `bits: 2` over a 1-bit array, so a conformant reader took entry
   N from bits 2N..2N+1 and invented a status for a credential nobody touched.
3. **#1495** — splitting the purposes gave a credential two status entries while the reader still
   returned one, so a suspended-but-not-revoked credential **passed verification**. Fixing the
   *reason* had removed the *enforcement*.

This feature is the fourth and last piece of the same seam: the statuses are now stored, encoded and
evaluated correctly, and only the reporting is still lossy.

---

## User stories

### US1 — A verifier can tell a suspension from a revocation

**As** a verifier (HAIP desk, mdoc reader, or a credential-gated workflow action)
**I want** the refusal to say which status applied
**So that** I can respond proportionately instead of treating every set status as terminal.

Acceptance:

- A credential whose **suspension** entry is set is refused with a reason distinct from revocation.
- A credential whose **revocation** entry is set is refused as revoked, unchanged from today.
- A credential with **both** set reports **revoked** — revocation is terminal in both specs, and
  reporting the reversible status would imply it could come back.
- The IETF rail distinguishes `0x01` from `0x02` rather than collapsing both to "set".
- An unresolvable status is still `RevocationUnavailable`, unchanged — "I could not tell" is a third
  thing and must not be folded into either.

### US2 — A holder is told the truth about their own credential

**As** a citizen whose credential has been suspended
**I want** to be told it is suspended and may be reinstated
**So that** I do not treat a temporary pause as permanent.

Acceptance:

- The inbox / decision notice for a suspension says suspended, not revoked, and says it may be lifted.
- A revocation keeps its current, terminal wording.
- Wording is authored once and reused; no surface hand-rolls its own string.

---

## Decision gates — **SETTLED 2026-08-18: A1 / B2 / C1 / D2**

Stuart took all four recommendations. Kept below as written, because the reasoning is the record of
why the code looks the way it does.

Two things the implementation learned that the gates did not anticipate:

- **D2 was worth it for the reason claimed.** Retiring `StatusListBit` broke 12 call sites and the
  build stayed clean in `src/` — every break was a test. There was also a SECOND, identically-named
  `StatusListBit` inside the HAIP service, whose only purpose was to be translated to the engine's;
  both were tri-states, so the SUSPENDED value the IETF checker had just decoded had nowhere to go.
  That parallel type is gone.
- **B2 found less than expected in one place and more in another.** No surface renders decline
  reasons as prose, so there was no revocation wording to correct — but `MapReason` is a chain of
  equality tests, and a suspension fell through all of them to `VerifierError` ("the verifier
  broke"). Meanwhile `CitizenInboxProjector` excludes the Active→Suspended transition *by design*,
  so a holder is never told about a suspension at all (**#1498**, filed, not folded in).

### Gate A — Is SUSPENDED always a refusal?

- **A1 — Always refuse.** Suspension means "temporarily prevent acceptance", so acceptance is never
  correct. Simplest, matches both specs' plain reading.
- **A2 — Policy-controlled.** A `TrustPolicy` could permit suspended credentials for low-assurance
  uses (e.g. a read-only view) while refusing them for anything transactional.

*Recommendation: **A1**. A2 invents a policy surface with no current caller, and "temporarily
prevent the acceptance" is not ambiguous. If a real case for A2 appears later it can be added
without re-opening this.*

### Gate B — How far does the distinction travel to the holder?

- **B1 — Verifier-visible only.** Distinct `TrustFailureReason`; holder-facing text unchanged.
- **B2 — Through to the holder.** Distinct reason *and* distinct inbox / decision-notice wording.

*Recommendation: **B2**, on the F186 precedent — that feature exists because a refused citizen was
being told their application "completed". This is the same class of untruth. B1 fixes the machine
and leaves the person misinformed.*

### Gate C — Does `RevocationCheckPolicy` need a suspension analogue?

Today one policy (`FailClosed` / `FailOpen`) governs what happens when status **cannot be resolved**.
It says nothing about which statuses are disqualifying.

- **C1 — No.** Keep one policy for resolution failure; Gate A settles disqualification.
- **C2 — Yes.** Add a per-purpose policy so an issuer can say "revocation is fail-closed, suspension
  is fail-open".

*Recommendation: **C1**, and rename nothing. C2 multiplies states (2 purposes × 2 policies) for a
case nobody has asked for, and a fail-open suspension is hard to justify — it means "accept a
credential the issuer paused".*

### Gate D — Widen `StatusListBit`, or introduce a richer type?

- **D1 — Widen the enum.** Add `Suspended` to `StatusListBit`. Cheapest; every `switch` must be
  revisited, and the name then lies (it is no longer a bit).
- **D2 — New type.** `CredentialStatusValue { Valid, Invalid, Suspended, Unresolved }` on the
  `IStatusListChecker` seam, with `StatusListBit` retired.

*Recommendation: **D2**. `StatusListBit` is already a poor name for a tri-state and would be a worse
one for a four-state. The seam has exactly two implementations and one consumer, so the rename is
contained — and a new type forces every call site to be looked at rather than silently falling into
a `default:` arm.*

---

## Out of scope

- **The stored representation.** Sorcha stores the W3C shape (a list per purpose) and projects the
  IETF view; #1491/#1492 settled that and it does not change here.
- **The `statusMessage` vocabulary.** W3C makes `statusMessage` mandatory when `statusSize > 1`. We
  never emit `statusSize > 1` on the W3C rail, so it stays untested and unimplemented. Note it if
  that ever changes.
- **IETF `0x03`+ application-specific values.** Reserved by the spec; Sorcha has no use for them.
- **`refresh` as a status purpose.** W3C defines it; we neither issue nor consume it.

---

## Migration posture

No stored-data change and no schema change. Credentials already carry one entry per purpose (#1491)
and the evaluator already reads all of them (#1495); this feature only changes what is *reported*
when one is set.

The one compatibility question is **`CredentialFailureReason`**, which is verifier-facing.

⚠ **The compiler will not help here, and that is the trap.** Neither downstream consumer uses an
exhaustive `switch` — both test for equality:

```csharp
// HaipPresentationVerifier:273
if (decision.FailureReason == TrustFailureReason.Revoked) { … }

// MdocPresentationVerifier:76
result.StatusCheckResult = verify.Trust?.FailureReason == TrustFailureReason.Revoked ? "Revoked" : null;
```

So adding `Suspended` compiles cleanly and **silently falls to the else branch**. In the mdoc case
that means `StatusCheckResult = null` — "no status problem" — for a credential the platform just
refused. Today a suspension at least reports `"Revoked"`; done carelessly, this feature would make it
report *nothing*, which is strictly worse and would pass CI.

T002 exists to find these before T007 adds the member, and T008 to update them. Treat a green build
after T007 as meaningless until T008 is done.

---

## References

- W3C Bitstring Status List — https://www.w3.org/TR/vc-bitstring-status-list/
- IETF Token Status List — https://datatracker.ietf.org/doc/html/draft-ietf-oauth-status-list
- `credential-status-list-specs` memory — both specs side by side, and the rules that fall out
- PRs #1491, #1492, #1495 — the three preceding fixes at this seam
- F186 — the precedent for telling a person the truth about a refusal
- F135 — the unified `ITrustEvaluator` / `IStatusListChecker` seam this modifies
