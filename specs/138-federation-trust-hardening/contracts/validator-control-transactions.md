# Contract: Validator Control Transactions & Vote Authority (US3)

**Service**: `Sorcha.Validator.Service` | **Sealed via**: register control transactions (MongoDB chain)

## Vote authority contract

`ConsensusEngine.ValidateVotesAsync` (currently `:459-500`) MUST derive authority from the **sealed roster** reconstructed from `RegisterControlRecord.Validators`, not from the `ValidatorRegistry` cache.

```
counted(vote) ⇔
    key(vote) ∈ sealedRoster.ActiveValidators        # FR-010
  ∧ signatureValid(vote)
  ∧ ¬doubleVote(vote, thisRound)
```
- A vote whose signing key is absent from the sealed roster contributes **zero** quorum weight on every honest node (SC-004), regardless of cache contents (FR-014).
- The cache MAY accelerate lookups but is **derived**; on cache↔seal divergence, the seal is authoritative.

## Admission contract

`RegisterPolicy.CreateDefault()` default `RegistrationMode` = **`Consent`** (was `Public`). New validators enter `Pending` and require an approval recorded in the sealed roster before any vote counts (FR-011). Public mode remains selectable explicitly by the register owner.

## New control-transaction action types

### `control.validator.eject`
Sealed, deterministic ejection. Any honest node observing the same evidence produces an identical transaction ⇒ convergent roster state.
```json
{
  "action": "control.validator.eject",
  "validatorId": "<did/address>",
  "reason": "Equivocation",
  "evidence": {
    "slot": "<registerId:docketNumber>",
    "conflictingVotes": [
      { "docketHash": "<hashA>", "signature": "<sigA>" },
      { "docketHash": "<hashB>", "signature": "<sigB>" }
    ]
  },
  "observedAt": "<iso8601>"
}
```
Effect on seal: roster entry `Active → Ejected`, `EjectionRef` = this tx id. Source: emitted by `BadActorDetector` on detected double-vote (replaces the in-memory-only log + manual `RevokeValidatorAsync`).

### `control.validator.liveness-violation`
```json
{
  "action": "control.validator.liveness-violation",
  "validatorId": "<did/address>",
  "acceptedTxRef": "<txId>",
  "deadline": "<iso8601>",      // acceptTime + DocketTimeoutSeconds
  "observedAt": "<iso8601>"     // > deadline + skew
}
```
Effect on seal: `Active → Ejected` (`reason = LivenessTimeout`).

## Invariants

- **Determinism** (SC-005): ejection outcome is a pure function of sealed state + observed evidence — no operator action, identical across honest nodes.
- **Quorum guard**: ejection that would drop `ActiveValidators` below workable quorum MUST surface the condition (ties to `GOV-5`), not silently brick the register.
- **No manual path required**: `RevokeValidatorAsync` may remain for operator override, but automatic detection→ejection MUST require zero human action (SC-005).
