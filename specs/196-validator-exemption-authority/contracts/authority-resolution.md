# Contract: Exemption Authority Resolution

**Feature**: 196 · **Date**: 2026-08-28

**There is no external API contract in this feature.** No REST endpoint, no gRPC method and no
persisted shape is added or changed. Serving refusal reasons over an endpoint is explicitly out of
scope; FR-013 requires only that the distinction is *recorded*.

The contract that does exist is internal and behavioural: it says what the validator must decide,
for whom, and what it must never decide. It is written here because it is the thing a third-party
validator would one day have to conform to, and because the absence of exactly this written-down
rule is gap 3 of the seven identified in the open-source readiness review.

---

## The rule

> An administrative exemption is granted **iff** the transaction claims a known exemption kind **and**
> the signer is proved entitled to that kind on that register.

Formally, for a transaction `T` on register `R`:

```
grant(T, R) = claim(T) ≠ none
            ∧ authority(signer(T), claim(T), R) = satisfied
```

Where `signer(T)` is established by signature verification, which already runs and already proves
possession of the key. No part of `authority(...)` may read a field that a submitter can change
without invalidating a signature.

---

## Authority rules by kind

| Kind | Satisfied when | Source of truth |
|---|---|---|
| `Genesis` | the transaction identifier is the network's single genesis identifier, the register is the system register, **and** the signing key's fingerprint matches the node's trusted anchor | node trust anchor (build-time; independent configurability tracked separately) |
| `BlueprintPublish` | the signer is entitled to publish on this register | **pending research R2** — recommendation is the register's validator roster, which already carries per-node purpose-derived keys with a status and is already replicated |
| `Control` | the signer is on the register's governance roster | governance roster, reconstructed from the register's control chain |

**The genesis identifier check is necessary but not sufficient on its own.** A submitter may set the
transaction identifier to the known constant and supply their own payload with a matching payload
hash, producing a signature that verifies correctly over their own content. The anchor fingerprint is
what actually closes it. Any implementation that checks only the identifier has not implemented this
contract.

---

## Field agreement (FR-006)

Where an identifying field exists both inside the signed content and as an unsigned field alongside
it, the two MUST agree or the transaction is refused. Where no signed counterpart exists for a given
transaction kind, its absence is not a disagreement.

This exists because a transaction that describes itself one way to the rules and another way to its
own signature is the general form of the defect; closing the specific instances without closing the
form invites the next one.

---

## Failure semantics

| Situation | Required behaviour |
|---|---|
| Claim made, authority **not satisfied** | refuse the exemption; the transaction is then subject to the ordinary rules and will normally fail sender authorisation |
| Claim made, authority **not resolvable** | refuse the exemption (**fail closed**, FR-007) |
| Claim made and satisfied | grant exactly the existing six waivers — no more, no fewer |
| No claim made | unchanged behaviour; a genuine genesis signer submitting an ordinary transaction gets no waiver |

Both refusal situations MUST be recorded distinctly from an ordinary validation failure, and MUST be
distinguishable **from each other** — "you were not entitled" and "I could not tell" call for
different operator responses.

---

## Invariants this contract must not break

- **The six waivers are unchanged.** Two are load-bearing for governance quorum: approvals share a
  predecessor (a shape only the fork bypass permits) and the chain-derived sender binding would treat
  the second approver as an impostor. Narrowing either makes quorum unattainable.
- **No ledger bytes move.** No change to the genesis ceremony artefact, and no change to the
  canonical bytes of a published definition — the latter would alter every publication identifier on
  every register.
- **Sealed-docket verification is untouched.** A node pulling sealed history verifies the docket
  signature and chain; it does not re-run these rules.
- **Both publication eras validate.** Legacy publications are labelled as governance and
  disambiguated by a secondary field; authority must be evaluated against the effective kind, not the
  raw label.
