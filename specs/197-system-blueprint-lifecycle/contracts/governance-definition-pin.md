# Contract: governance definition pin (Feature 197)

## Wire

`ControlTransactionPayload` (signed payload of governance control transactions) gains:

```json
{ "version": 1, "roster": null, "operation": { … }, "enactsProposalId": null,
  "governanceDefinitionTxId": "<64 lowercase hex — SSR publication id of register-governance-v1>" }
```

- Present only on **proposals** (pending, and Owner-override propose-and-enact). Omitted entirely — not
  `null` — everywhere else, so genesis, enactment and SSR-genesis canonical bytes are unchanged.
- Action 1 schema declares it optional (`type: string`, `pattern: ^[0-9a-f]{64}$`).

## Producer rule (Register Service)

At raise, the value is the **current** SSR publication id of `register-governance-v1`, by ledger order.
If the current publication cannot be read, the proposal is not submitted (503, retryable) — a proposal is
never raised unpinned by a post-feature producer.

## Resolution rule (one implementation: `GovernanceDefinitionPin`)

| Governance step | Governing definition |
|---|---|
| Proposal / Owner-override (own pin present) | its own pin |
| Approval (action 2) | pin of the proposal at `PreviousTransactionId` |
| Enactment | pin of the proposal named by payload `enactsProposalId` |
| Referenced proposal has no pin | legacy: current definition, counted |
| Referenced proposal unreadable | refuse |

## Validator rules

1. Pinned definition resolves via cache → Blueprint Service → transaction's register → **SSR** (new arm);
   the SSR read recomputes `BlueprintPublicationId.Compute(SSR, blueprintId, canonical)` and must equal
   the pin. Failure → `VAL_BP_VERSION_001`. No fallback to latest.
2. Raise only: pin ≠ current SSR publication → `VAL_GOV_DEF_001` ("raised under a superseded governance
   definition"). "Current" is read uncached from the local Register Service.
3. Approvals and enactments are never held to "current".

## Guarantees pinned by tests

- Existing producers' canonical payload bytes unchanged (genesis, enactment).
- v4-raised proposal → v5 published → approval validates against v4; the same approval against v5 fails.
- Removing the pin lookup (falling back to latest) reds the counterfactual test.
