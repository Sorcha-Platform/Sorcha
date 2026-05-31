# Quickstart: verifying Ledger-Derived Workflow Instances

How to confirm the model works, mapped to success criteria. Two layers: deterministic unit/integration checks (no live cluster) and the cross-node E2E (the standing two-node demo).

## Unit / integration (deterministic, no cluster)

```bash
dotnet test --filter "FullyQualifiedName~LedgerDerivedInstances"
```

- **Projection determinism (SC-001)** — feed the same sealed docket stream to the projector in different orders / with a mid-stream restart → identical instance state every time.
- **Full-set routing (SC-005)** — a multi-branch route seals with all branches in `RoutingDecision.nextActions`; the projector advances all of them.
- **Routing validation (SC-007)** — a decision whose `nextActions` aren't route-graph successors, or with a bad signature, is rejected (`VAL_ROUTING_001/002`).
- **Rebuild parity (SC-003)** — `RebuildAsync(instanceId)` equals the materialized view; deleting/corrupting the view and rebuilding restores it.
- **Reaction idempotency (SC-004)** — replay a sealed credential-issuing tx N times + restart the dispatcher → exactly one credential; a non-entitled node performs none.
- **Single submit contract (SC-006)** — owner-node and subscriber-node submits return the same response contract and reach the same projected state.

## Cross-node E2E (the standing demo)

```powershell
Import-Module demos/AssuredIdentity/AssuredIdentityDemo.psm1
New-IssuingAuthority -IssuerNode tiny      # owner
Connect-Subscriber  -SubscriberNode n1     # subscriber
# tester applies on n1; the autonomous agent on tiny approves
```

- **Autonomous cross-node loop (SC-002)** — the applicant submits on n1; **both** nodes project the instance to Action 2 identically; the autonomous agent on tiny **discovers** Action 2 via `/api/actions/pending` (no mirror, no manual approval) and approves; the credential is delivered to the citizen on n1. The whole loop runs with no manual step.
- Verify instance state is byte-identical (modulo disclosure-scoped data) on tiny and n1 after each seal.

## Clean break (SC-008)

```powershell
pwsh scripts/check-ledger-derived-clean-break.ps1
```
- Reports zero occurrences of `InstanceMirrorReconstructor`, `IsReadOnlyMirror`, `Create/UpdateMirrorAsync`, `ApplyInstanceStateChanges`, the `LocallyOwned` submit branch, `NextActionId` singular hint, and the topology ownership heuristic.

## Acceptance checkpoints → SC map

| Check | SC |
|---|---|
| same docket stream → identical state across nodes/orders | SC-001 |
| autonomous two-node loop, agent discovers + approves, credential delivered | SC-002 |
| rebuild == materialized; corrupt → restored | SC-003 |
| replay/restart → exactly one credential | SC-004 |
| multi-branch route preserves all branches | SC-005 |
| owner vs subscriber submit identical | SC-006 |
| forged/inconsistent decision rejected at seal | SC-007 |
| clean-break gate green + demo green | SC-008 |
