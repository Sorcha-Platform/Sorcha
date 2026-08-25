# Contract: the pin on `RoutingDecision`

**Feature**: 194 | **Status**: proposed | **Consumers**: workflow service (producer), validator
(verifier), every node's projector and rebuild service (readers)

This is the load-bearing contract of the feature. Everything else is downstream of it.

---

## Wire shape

`RoutingDecision` rides in the clear on an action transaction, both as the typed
`TransactionMetaData.RoutingDecision` on a sealed transaction and as canonical JSON under the
producer's `metadata["routingDecision"]` key before sealing.

```jsonc
{
  "completedActionId": 1,
  "nextActions": [ { "actionId": 2, "branchKey": null } ],
  "routeId": "approved-to-claim",
  "reasonCode": "postcode-not-found",
  "blueprintExecDefHash": "9f2c…64 lowercase hex…",   // NEW
  "attestation": { "kind": "SenderSigned", "signature": "base64…" }
}
```

- `blueprintExecDefHash` is **omitted from the wire when null** (`WhenWritingNull`), so a
  pre-feature transaction and a null-valued new one serialise identically and no existing signature
  is disturbed.
- Serialised with `RegisterSerializationOptions.Canonical` on both sides. Producer and validator
  must use the same options or the signature cannot verify.

---

## Signing coverage — the invariant

`ComputeSignableBytes()` returns the canonical bytes of the decision with `attestation` nulled. It
rebuilds the object **field by field**:

```csharp
var signable = new RoutingDecision
{
    CompletedActionId    = CompletedActionId,
    NextActions          = NextActions,
    RouteId              = RouteId,
    ReasonCode           = ReasonCode,
    BlueprintExecDefHash = BlueprintExecDefHash,   // NEW — MUST be here
    Attestation          = null,
};
```

> **A field present on `RoutingDecision` and absent from this rebuild rides the wire
> unauthenticated while appearing signed.** The transaction signature covers only
> `{TxId}:{PayloadHash}`, so it does not cover this. `VAL_ROUTING_002` verifies exactly what this
> method returns, and nothing else.

### The guard, and why it must be reflection-driven

**Required**: a test that enumerates `typeof(RoutingDecision).GetProperties()` and asserts that
every property other than `Attestation` demonstrably affects the output of `ComputeSignableBytes()`
— by mutating each in turn and requiring the bytes to change.

**Not acceptable**: a test listing the five field names. A hand-written list rots in the same
direction as the bug — the developer who forgets the rebuild forgets the list. F189 lost
`ValidatorEntry` to precisely this, and the XML doc warning above it did not prevent it.

**Mutation evidence required** (SC-008): remove an **existing** field (e.g. `ReasonCode`) from the
rebuild and confirm the reflection test fails and names it. A guard that has only ever been green
proves nothing.

---

## Producer obligations

| Situation | `blueprintExecDefHash` |
|---|---|
| Starting action (instance creation) | The hash of the **latest** published definition on that register at submission time. This is the moment the instance's definition is chosen. |
| Every subsequent action | The **instance's established pin**, read from the instance — never re-derived from "latest". |
| Presentation-outcome decision (F145 US6) | The instance's pin, same as any subsequent action. |
| Governance / control / genesis / participant transactions | **Absent.** These carry no `RoutingDecision` at all and are exempt from `VAL_ROUTING_*`. Do not add a pin to them. |

---

## Verifier obligations

Extends `ValidationEngine.ValidateRoutingDecisionAsync` (`VAL_ROUTING_*`), which already runs for
any forward-routing action transaction carrying a decision. No new call site.

| Condition | Outcome |
|---|---|
| Hash present and resolves to a published definition of that blueprint | Validate the action against **that** definition. |
| Hash present and resolves to nothing | Refuse — `VAL_BP_VERSION_001`. **Never fall back to latest**; that reintroduces the defect silently. |
| Hash present and differs from the instance's established pin | Refuse. A sender must not be able to move an instance onto another definition by asserting one. |
| Hash absent, transaction predates the feature | Fall back to latest, log at Warning, increment the fallback counter. |
| Hash absent, transaction is governance / control / lifecycle | Exempt, as today. |

Because the hash is inside `ComputeSignableBytes()`, `VAL_ROUTING_002`'s existing signature check
authenticates it with **no new verification code** — only the resolution behaviour above is new.

---

## Reader obligations (projector and rebuild)

`InstanceProjectionResolver.ResolveAsync` reads the hash from the sealed decision — preferring the
typed field, falling back to the `routingDecision` tracking JSON, exactly as it already does for
`routeId` and `reasonCode` — and carries it on `ProjectedTransaction`.

`InstanceProjector` (online) and `InstanceRebuildService` (rebuild) MUST take the **identical** path,
including the identical pre-feature fallback. F145 guarantees the two produce bit-identical
instances; a divergence here breaks that guarantee, and the existing parity test is what catches it.

---

## Compatibility

- **Backward**: a pre-feature sealed transaction deserialises with the field null and verifies
  against its original signature, because the field is omitted when null.
- **Forward**: a node running pre-194 code that receives a decision carrying the field will
  deserialise it (System.Text.Json ignores unknown properties by default) but will **not** include it
  in its own `ComputeSignableBytes()` rebuild — so its `VAL_ROUTING_002` check will compute different
  bytes and **refuse the transaction**. Mixed-version validation is therefore not supported. Deploy
  the validator on every sealing node before, or together with, the workflow service.
