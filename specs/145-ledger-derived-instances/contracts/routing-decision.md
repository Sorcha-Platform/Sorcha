# Contract: RoutingDecision (transaction-carried, validated)

## Shape (on `TransactionMetaData`, clear)

```jsonc
"routingDecision": {
  "completedActionId": 1,
  "nextActions": [ { "actionId": 2 } ],        // FULL set; [] = branch terminates
  "attestation": {
    "kind": "SenderSigned",                     // v1
    "signature": "<sender-wallet sig over canonical(decision without attestation)>"
  }
}
```

## Producer (Blueprint Service / Engine)
- The Engine routing evaluation produces the **complete** `nextActions` (no collapse to one).
- `ActionExecutionService` assembles `RoutingDecision` and signs it with the sender wallet (the tx signer).
- Serialized canonically (`RegisterSerializationOptions.Canonical`), `[JsonPropertyName]`-stable.

## Validator (at seal — `ValidationEngine`)
- **VAL_ROUTING_001** — reject unless every `nextActions[i].actionId` is a route-graph successor of `completedActionId` in the published blueprint (terminal `[]` valid only where allowed).
- **VAL_ROUTING_002** — reject unless `attestation` verifies AND satisfies the register's `routingAttestation` policy. v1: verify the sender signature over the canonical decision. `validator-reeval` / `proof` ⇒ reject "unsupported attestation strength" (until v2/v3).
- The validator does NOT decrypt payload or re-evaluate the condition in v1.
- `DocketBuildTriggerService` carries the validated decision through the seal (replaces `ResolveNextActionId`).

## Consumer (every node's `InstanceProjector`)
- Reads `routingDecision.nextActions` to advance `currentActionIds`. No decryption needed.

## Backwards-compat
- None. `TransactionMetaData.NextActionId` is removed (pre-release clean break).
