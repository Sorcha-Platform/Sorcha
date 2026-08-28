# Validator exemption authority — design note (#1591)

**Date:** 2026-08-28 · **Read at:** `cfc2e48aa` · **Status:** design only, no code changed.
**Issue:** #1591 · **Related:** validation-engine open-source review, F189 T054, C-VAL 2026-07-29.

---

## 1. The defect, precisely

`TransactionTypeClassifier.IsGenesisOrControlTransaction` grants six exemptions —
action-schema validation, blueprint conformance (**including `VAL_BP_002` sender
authorisation**), routing-decision attestation, crypto policy, sequence replay, and
(via the persisted `TransactionType`) fork detection — on either of two conditions:

```csharp
transaction.BlueprintId == "genesis"                                    // arm 1
|| transaction.Metadata["Type"] in {Genesis, Control, BlueprintPublish} // arm 2
```

Neither is signed.

**What the signature actually covers** (`ValidationEngine.cs:790`,
`ITransactionBuilderService.cs:597`):

```csharp
var signedData = $"{transaction.TransactionId}:{transaction.PayloadHash}";
```

and in the builder `TxId` = SHA-256(canonical payload), `payloadHash = txId` — the *same
value*. The signed bytes are `h:h`. The signature covers the payload body and nothing
else: not `RegisterId`, not `SenderWallet`, not `SequenceNumber`, not `BlueprintId`, not
`ActionId`, not `Metadata`.

`Metadata` is not merely unsigned — it is assembled **after** signing.
`ToTransactionSubmission(signResult, ...)` takes the signature as an argument and then
builds `submissionMetadata` as an explicit whitelist. Some entries are documented as
deliberately unsigned operator aids (`Program.cs:2504`).

**Reachability:** `TransactionDistributionGrpcService.SubmitTransaction`
(`Sorcha.Peer.Service`) deserialises a peer's `submission_json` verbatim — `Metadata`
included — and hands it to the local validator. The class carries no `[Authorize]`, the
gRPC mapping adds none, and the service registers no fallback policy, so the surface is
gated by network reachability alone. *(Code-verified; not live-verified.)*

**Asymmetry — this is the finding:**

| claimed value | waives six | compensating authority check | net effect |
|---|---|---|---|
| `Control` | yes | **yes** — `RightsEnforcementService.IsGovernanceTransaction:663` keys on the same string and applies the roster check | a *trade*, strictly worse for an attacker |
| `Genesis` | yes | **no** | nothing substituted |
| `BlueprintPublish` | yes | **no** | nothing substituted |

The genesis trust anchor does **not** cover this. `GenesisSignatureVerifier` is called only
from `GenesisIngestionService` / `SystemRegisterBootstrapper` — the *bootstrap ingestion*
path. The validator never checks a transaction claiming `Type="Genesis"` against the
compiled-in anchor; it trusts the string.

---

## 2. Why "just sign the metadata" is not the fix

It is the right instinct and it closes one of two attacks:

| | attack | signing metadata fixes it? |
|---|---|---|
| **(a)** transit tampering — a relaying peer rewrites `Metadata["Type"]="Genesis"` onto someone else's transaction to strip six rules | **yes, completely** |
| **(b)** origin forgery — an attacker builds their *own* transaction, sets `Type="Genesis"`, signs it with their own key | **no.** The signature is valid — it is their transaction |

(b) is the severity of #1591. The exemption waives `VAL_BP_002`, *sender authorisation
itself*; once granted, nothing asks whether that wallet may claim genesis. Signing makes a
claim **attributable**. It does not make it **authorised**. The exemption needs the second
property.

There is also a structural cost to signing the metadata bag: it inverts the assembly order
(the Wallet Service would sign data the Blueprint Service composes later) and freezes a
structure deliberately designed as a late whitelist projection.

### And the payload route is not universally available

The obvious follow-on — mirror the C-VAL precedent and move the discriminator into the
signed payload (`SignedPayloadType()` / `HasUncorroboratedLifecycleMetadata()`) — **cannot
be applied to two of the three values**:

- **`BlueprintPublish`** — the signed payload *is the canonical blueprint definition*
  (`Program.cs:2040`). Adding a discriminator property changes the canonical bytes and
  therefore **every publication id on every register** (CLAUDE.md pattern 22). Forbidden.
- **`Genesis`** — the signed payload is a pre-signed offline-ceremony artefact. Adding a
  field means a new ceremony, a new anchor, and a re-genesis of every node. Not
  proportionate to this fix.
- **`Control`** — `ControlTransactionPayload` could carry one, and it is the value that
  least needs it (the roster check already applies).

**So the payload route is available exactly where it is least needed.** That inverts the
plan: authority checking is not the complement to the fix, it *is* the fix.

---

## 3. The principle

> Stop asking the transaction **what it claims to be**. Ask whether **the signer is
> entitled to this exemption on this register**.

The signer is already signed material — the signature verifies over the payload hash with
the submitter's key, and `VAL_SIG_*` already proves possession. So authority is derivable
without touching any payload, without a ceremony, and without moving a single ledger byte.

This also satisfies `TransactionTypeClassifier`'s own stated rule — *"The reasoning inverts
the moment this is used to grant anything"* — by removing the grant-from-unsigned-claim
shape entirely rather than patching one key.

---

## 4. The design

### C1 — `Genesis`: bind to the compiled-in anchor

Grant the genesis exemption only when **all** hold:

1. `transaction.TransactionId == GenesisSignatureVerifier.ComputeGenesisTxId()` — this is
   `SHA-256("genesis-{SystemRegisterId}")`, a **compile-time constant**. There is exactly
   one valid genesis transaction id, ever.
2. `transaction.RegisterId == SystemRegisterConstants.SystemRegisterId`.
3. The signing public key's fingerprint matches the node's trusted genesis anchor
   fingerprint.

(1) and (2) are free. (3) is the real check and the real work: `INodeTrustAnchor` currently
lives in `Sorcha.Register.Service/Provenance/` and must become reachable from the Validator
Service. **No ceremony change, no re-genesis.**

Note that (1) alone is insufficient: an attacker may set `TransactionId` to the constant and
supply their own payload with a matching `PayloadHash`, producing a valid self-signature.
(3) is what actually closes it.

### C2 — `BlueprintPublish`: bind to the register's control key

Grant only when the signer is the register's control-derivation wallet
(`SorchaDerivationPaths.RegisterControl` — already recorded on the submission as
`Metadata["SystemWalletAddress"]`, but that value must be *resolved from the register*, not
read from the transaction). The validator already resolves rosters in
`RightsEnforcementService`, so the resolution seam exists.

### C3 — `Control`: make the compensating check load-bearing, not coincidental

Today two independent code paths key off the same string and happen to agree. Restructure
so the exemption is granted **only when the roster check has been applied and passed** —
one decision, not two that must be kept in step. Behaviour is unchanged; the coupling
becomes explicit and a future edit to one cannot silently unhook the other.

### D — Close the `BlueprintId` route and the field-substitution gap

`IsGenesisOrControlTransaction`'s first arm needs no metadata at all: setting the
submission-level `BlueprintId = "genesis"` buys the same six exemptions. Fold arm 1 into
C1's conditions.

Separately, `blueprintId` / `actionId` / `instanceId` exist **both** inside the signed
payload and as unsigned submission fields; the validator reads the unsigned copies and
never compares them. Add the cross-check where a signed counterpart exists (action
transactions). *(This sub-finding is code-verified only — unlike the metadata route it has
not been verified by execution.)*

### What must NOT change

**Do not withdraw the exemptions.** `TransactionTypeClassifier:48-70` records that two of
the six are load-bearing for governance quorum: every approval sets `PreviousTransactionId`
to the same proposal (N children of one parent — a shape only the fork bypass permits), and
`VAL_BP_002`'s chain-derived binding would treat the second approver as an impostor.
Withdrawing either makes quorum unattainable. This work changes **who may claim** an
exemption, never **what an exemption does**.

---

## 5. Test obligations

Guards written after the fact pass vacuously unless proved otherwise, so each needs a
counterfactual in the same run:

1. **Per-route refusal** — for each of `Genesis`, `Control`, `BlueprintPublish`, and the
   `BlueprintId="genesis"` route: an unauthorised wallet claiming it is refused
   `VAL_BP_002`, *and* a control assertion in the same test that the unauthorised wallet is
   refused without the claim (so the test cannot pass by refusing everything).
2. **Positive path** — the genuine genesis / publish / governance transaction still seals.
   This is the regression that matters; C2 in particular can lock out real publishes.
3. **Mutation test** — revert each check individually and confirm the matching guard goes
   red. A guard that stays green against its own removal is not a guard.
4. **Not `IHashProvider`-mocked.** #1587's tests mocked the hash provider to `byte[32]`, so
   every hash compared equal by construction and the defect was invisible. Use real hashing.
5. **Sealed-history compatibility** — prove that transactions already sealed on n1 and tiny
   satisfy the new conditions. Expected to pass (genesis was signed by the ceremony key;
   publishes by the control derivation), but expectation is not evidence, and any
   re-validation path (mempool replay, resync) must be exercised.

**Live verification is part of done**, on both nodes: a real publish, a real governance
propose→approve→enact, and a SyncOnly replica pull. Merged is not proven.

---

## 6. Sizing

Three services (Validator, Register, Peer), one shared component to relocate
(`INodeTrustAnchor`), a security-critical change to the validation contract, a
compatibility question against already-sealed data on two live nodes, and a live
multi-node acceptance run. This is not a single-PR change.

**Recommendation: full speckit — `specify` → `plan` → `tasks` → `implement`.**
