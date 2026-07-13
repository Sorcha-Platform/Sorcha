# AIAS decision notice — decentralised delivery + reason codification

**Date:** 2026-07-13
**Status:** Approved (design)
**Follow-up to:** Feature 183 (`specs/183-aias-decision-visibility/`, design `2026-07-12-aias-emailverified-claim-source-design.md`)
**Related:** F145 (ledger-derived instances — `InstanceProjector` / `ReactionDispatcher`), F118 (durable inbox), F176 (disclosed payload)

---

## 1. Problem

F183 US2 shipped the `x-decision-notice` route annotation: when an autonomous agent rejects an
application, the applicant should get a durable, reasoned bell/inbox entry saying *why*. It does not
reach the citizen. Two coupled defects, both root-caused live on n1 (2026-07-13):

1. **Recipient resolution dead-ends for late-bound citizens.** `BlueprintInboxWriter.WriteDecisionAsync`
   resolved the recipient wallet through the participant registry, which returns 404 for an open /
   late-bound public-org citizen — they have no participant record. *(Already fixed on branch
   `fix/183-decision-notice-citizen-recipient`, commit `dedb339c`: fall back to the sending wallet's
   owner. A consumer wallet's `Owner` **is** the PlatformUserId. `GetWalletAsync` is local-only, so the
   resolver is inherently cross-node-safe — it skips rather than misfires. Reused unchanged.)*

2. **The notice fires on the wrong node.** It is hooked inline in `ActionExecutionService.ExecuteAsync`,
   which runs only on the node that processed the **agent's** submission — the issuer/register-owner
   node. In a federated (DAD) deployment the citizen's account and inbox live on **their** node. The
   default assumption must be that the citizen is *not* co-located with the agent.

A third problem falls out of fixing (2): the reason is currently free text (`verificationNotes`) inside
the action payload. On the citizen's node a background reaction has **no delegation token** and cannot
decrypt an encrypted disclosure group; and `IStateReconstructionService` / `IActionDisclosureResolver`
are prior-action-scoped, so they don't cleanly return the *completed* reject action's own payload.
Copying the free text into clear metadata would leak analyst prose into public register metadata.

## 2. Shape of the fix

Fire the notice from the **inbound sealed transaction as the citizen's own node folds it**, and carry a
non-sensitive **reason code** — resolved to citizen-facing text from the replicated **blueprint**.

```
agent's node                          register (replicated)              citizen's node
────────────                          ─────────────────────              ──────────────
ActionExecutionService                                                   InstanceProjector
  routes → reject route                                                    folds sealed tx
  builds RoutingDecision       ──▶  sealed tx carries                 ──▶  ReactionDispatcher
    routeId, reasonCode               RoutingDecision                        reads routeId+reasonCode
    signs it                          (sender-signed,                        looks route up in the
  submits                             VAL_ROUTING_002-verified)              replicated blueprint
                                                                             ↓
InstanceProjector folds too                                                x-decision-notice
ReactionDispatcher skips                                                     reasons[code] → message
  (citizen wallet not local)                                                ↓
                                                                           entitlement gate fires
                                                                           → durable inbox entry
```

## 3. The carrier — signed `RoutingDecision`

`RoutingDecision` (`Sorcha.Register.Models/Transactions/`) already: rides the transaction's **clear**
metadata, is **sender-signed** (`Attestation.Signature` over `ComputeSignableBytes()`), is **verified at
seal** by the validator (`VAL_ROUTING_002`), is **projected onto the sealed tx** by
`DocketBuildTriggerService`, and is **read by the shared `InstanceProjectionResolver`** on every node.

It gains two optional fields:

```jsonc
RoutingDecision {
  "completedActionId": 2,
  "nextActions": [],
  "routeId": "rejected-terminal",       // NEW — the route the sender actually took
  "reasonCode": "postcode-not-found",   // NEW — non-sensitive decision code
  "attestation": { "kind": "SenderSigned", "signature": "…" }   // signs over the above
}
```

Both fall inside `ComputeSignableBytes()` by construction (it is the attestation-free canonical
serialization of the record), so they are tamper-evident with **zero new validator code** and reach every
node with **zero new plumbing**.

Why not a separate clear-metadata key: the transaction signature covers only
`{TransactionId}:{PayloadHash}` (`ValidationEngine.cs:712`) — raw `TrackingData` is **not** tamper-evident.
A relaying node could rewrite an unsigned reason. `RoutingDecision` is the ecosystem's existing attested
clear-metadata carrier; the reason code is a fact about the decision the sender made, so it belongs there.

**Why `routeId` and not next-action-set matching:** two routes on the same action can share a next-action
set and differ only by condition (and the reject route's set is empty, colliding with any other terminal
route). Matching by set would need condition re-evaluation against a payload the citizen's node cannot
read. The producer knows exactly which route it took — so it says so, and the consumer needs no
inference at all.

## 4. Reason codification

`DecisionNotice` (`Sorcha.Blueprint.Models/Route.cs`) — clean break, no free-text path:

```jsonc
"x-decision-notice": {
  "recipientParticipantId": "citizen",
  "reasonCodeField": "/reasonCode",                  // NEW — pointer into the submitted payload
  "title": "AIAS could not assure your identity",
  "severity": "Warning",
  "reasons": {                                       // NEW — code → citizen-facing message
    "postcode-not-found": "AIAS could not locate that address on any map. …",
    "profanity":          "AIAS does not assure identities described in such… colourful terms. …",
    "email-unverified":   "AIAS needs a verified email before it can assure you. …"
  },
  "fallbackMessage": "Your application was not approved."   // NEW — unknown/absent code
}
```

`reasonField` (the free-text JSON Pointer) is **removed**.

This puts the citizen-facing copy in the **blueprint** — the shared, replicated, public contract — and
leaves the **decision** with the agent. The agent's rules file emits a code alongside its prose:

```jsonc
// demos/AIAS/agent/assure-id.rules.json
{ "decision": "rejected",
  "reasonCode": "postcode-not-found",
  "verificationNotes": "AIAS could not locate that address on any map. …" }   // stays: ledger/audit
```

`verificationNotes` remains on the ledger (disclosed to the citizen and the analyst, per the action's
existing `disclosures`) as the audit record. It is simply no longer the *delivery* mechanism for the
notice. No `Sorcha.Agent` code change is needed — the rules `payload` object is merged verbatim into the
submission; the action's `dataSchema` gains a `reasonCode` property.

Message resolution on the citizen's node is `reasons[reasonCode] ?? fallbackMessage` — no decryption, no
payload access, no delegation token, identical on DevMode and encrypted registers.

## 5. Components

### 5.1 Producer — `ActionExecutionService` (agent's node)

- Step 10d already builds + signs the `RoutingDecision`. It now also sets `RouteId` from the taken route
  and, when that route carries an `x-decision-notice` with a `reasonCodeField`, resolves that JSON Pointer
  from the submitted payload into `ReasonCode`.
- The inline **9-notice** block (`DecisionNoticeDispatcher.DispatchAsync` + the `SafeEvaluateCondition`
  callback) is **deleted**, along with `DecisionNoticeDispatcher` and its tests. Its route-match logic is
  superseded by the carried `routeId`.
- The presentation-outcome `RoutingDecision` builder (`BuildPresentationRoutingDecisionAsync`) leaves both
  new fields null — presentation outcomes carry no decision notice.

### 5.2 Engine — matched-route identity survives terminal routes

`RoutingEngine.BuildRoutingResult` returns `RoutingResult.Complete()` for a route with an empty
`nextActionIds`, discarding `route.Id`. The reject route **is** that case. Fix: lift `MatchedRouteId` to
the top-level engine `RoutingResult` and set it on every matched-route path (conditional, default,
terminal). The service-local `RoutingResult` carries it through `EvaluateRoutingAsync`.

`RoutedAction.MatchedRouteId` already exists per-next-action and is left alone.

### 5.3 Consumer — `ReactionDispatcher` (citizen's node)

`DispatchAsync(Instance, string sealedTxId, ct)` becomes `DispatchAsync(Instance, TransactionModel tx, ct)`
— the projector already holds the sealed transaction, so no register re-fetch is needed. New dependency:
`IActionResolverService` (to read the replicated blueprint).

A new decision-notice reaction runs **before** the terminal/active branching (so a notice on a
non-terminal route works too):

1. `InstanceProjectionResolver.ResolveRoutingDecision(tx.MetaData, logger)` → null or no `RouteId` ⇒ return.
2. Blueprint → completed action (`decision.CompletedActionId`) → route with `Id == decision.RouteId`.
3. `route.DecisionNotice` null ⇒ return.
4. Recipient wallet = `instance.ParticipantWallets[notice.RecipientParticipantId]`; unbound ⇒ return.
5. `ShouldFireAsync(tx.TxId, "decision-notice", wallet)` — the **existing** gate:
   - **Entitlement**: `IWalletServiceClient.GetWalletAsync` is local-only, so only the node hosting the
     citizen's wallet proceeds. The agent's node folds the same tx and skips (cross-node dedup).
   - **Idempotency**: `IAtomicDistributedCache.TrySetIfAbsentAsync` on
     `react:{sealedTxId}:decision-notice:{wallet}` (replay / restart / rebuild dedup).
6. Message = `notice.Reasons?[decision.ReasonCode] ?? notice.FallbackMessage ?? ""`.
7. `INotificationService.NotifyDecisionAsync(...)` → `BlueprintInboxWriter.WriteDecisionAsync(...)`, which
   resolves the recipient PlatformUserId (participant registry → else wallet owner) and writes the durable
   F118 entry (`Category=Workflow`, `IconKey=workflow.rejected`, `Summary`=message).

Metric: `reaction_dispatched_total{kind="decision-notice"}` on the existing `Sorcha.Blueprint.Reactions`
meter, plus the existing entitlement/idempotent skip counters.

The whole dispatch stays inside the dispatcher's existing try/log/swallow — a notice failure never
disturbs the committed projection.

### 5.4 Projector

`InstanceProjector` passes `tx` (which it already has) to `DispatchAsync` at both call sites.

### 5.5 Demo artefacts

- `demos/AIAS/blueprints/aias-assured-identity.template.json` — action 2's `dataSchema` gains a
  `reasonCode` string property (enum of the three codes); the `rejected-terminal` route's
  `x-decision-notice` swaps `reasonField` for `reasonCodeField` + `reasons` + `fallbackMessage`.
- `demos/AIAS/agent/assure-id.rules.json` — each reject rule's payload gains its `reasonCode`.

## 6. Error handling

Every step is fail-safe and skip-quiet, in keeping with the existing reaction contract:

| Condition | Behaviour |
|---|---|
| No `RoutingDecision`, or no `routeId` on it | No notice (pre-feature txs, non-decision actions). |
| Route id not found in the blueprint | Log debug, no notice (blueprint version drift). |
| Route carries no `x-decision-notice` | No notice — the normal case for most routes. |
| Recipient participant not bound to a wallet | Log debug, no notice. |
| Citizen wallet not hosted on this node | Entitlement skip (the expected cross-node case, metered). |
| Reaction already fired for this `(tx, kind, wallet)` | Idempotent skip (metered). |
| `reasonCode` absent or not in `reasons` | `fallbackMessage`. |
| Inbox write fails | Logged, swallowed — projection and sealing unaffected. |

## 7. Testing

TDD, unit-first; the live n1 run is the acceptance gate.

- **Models** — `RoutingDecision` canonical round-trip carries `routeId`/`reasonCode`; they are inside
  `ComputeSignableBytes()`; absent fields deserialize to null (pre-feature txs).
- **Engine** — a matched **terminal** route yields `MatchedRouteId`; conditional and default routes too.
- **Producer** — the taken route's id is stamped; `reasonCode` is resolved from `reasonCodeField`; a route
  with no notice leaves `reasonCode` null; a missing pointer leaves it null.
- **Consumer** — `ReactionDispatcher` fires a decision notice for the entitled recipient; skips when the
  wallet is not local (cross-node); is idempotent on replay; resolves an unknown code to `fallbackMessage`;
  no-ops when the route carries no notice, when the route id is unknown, and when the recipient is unbound.
- **Recipient resolver** — existing `BlueprintInboxWriterTests` (participant-first, wallet-owner fallback,
  consumer-wallet owner-is-PlatformUserId) already cover `dedb339c`; unchanged.
- **Live (n1, DevMode single-node — the citizen's node IS the n1 node)** — drive a reject via the web path
  and via `demos/AIAS/rehearse.ps1`; Chrome-DevTools confirms a durable bell/inbox entry carrying the
  codified reason, surviving reload and re-login. True multi-node (tiny + n1) is the stretch validation.

## 8. Non-goals

- **No validator rule** for `routeId` / `reasonCode`. The attestation signature already covers them, and
  the sender is the decision authority — there is nothing for a third party to independently check.
- **No client change.** The F118 bell drawer renders the entry as-is.
- **No email-on-decision, no "My Applications" history page.** Tracked on issue #1163.
- **Nested `reasonCodeField` pointers** work (the resolver walks the payload), but the AIAS code field is
  top-level; no new schema-extension surface is added.
