# Data Model: Decentralised decision notice + reason codification

No persistent schema changes. Two in-flight shapes change, and one existing durable row is written by a
new caller.

---

## 1. `RoutingDecision` (carrier) — `Sorcha.Register.Models/Transactions/RoutingDecision.cs`

Rides the transaction's **clear** metadata. Sender-signed; verified at seal by `VAL_ROUTING_002`.

| Field | Type | Status | Notes |
|---|---|---|---|
| `completedActionId` | `int` | existing | The action this transaction completes. |
| `nextActions` | `ActionRef[]` | existing | Full next-action set; empty = terminal. |
| **`routeId`** | `string?` | **NEW** | The id of the route the sender actually took. Null on pre-feature transactions and on presentation-outcome decisions. |
| **`reasonCode`** | `string?` | **NEW** | Non-sensitive decision code. Set only when the taken route declares an `x-decision-notice` with a `reasonCodeField` that resolves. |
| `attestation` | `Attestation?` | existing | `SenderSigned` + signature over `ComputeSignableBytes()`. |

**Invariant (load-bearing):** `ComputeSignableBytes()` rebuilds an attestation-free copy field by field.
`routeId` and `reasonCode` MUST be copied into that object, or they ride the wire unauthenticated while
appearing signed. Directly test-asserted.

**Compatibility:** both fields are optional. A transaction sealed before this feature deserializes with
both null and simply produces no notice.

---

## 2. `DecisionNotice` (annotation) — `Sorcha.Blueprint.Models/Route.cs`

Carried on a `Route` as the `x-decision-notice` JSON extension.

| Field | Type | Status | Notes |
|---|---|---|---|
| `recipientParticipantId` | `string` | existing | Resolved to a wallet via `Instance.ParticipantWallets`. |
| `title` | `string` | existing | Inbox entry title. |
| `severity` | `string?` | existing | Defaults to `Warning`. |
| ~~`reasonField`~~ | ~~`string`~~ | **REMOVED** | The free-text JSON Pointer. Clean break — no free text may reach clear metadata. |
| **`reasonCodeField`** | `string?` | **NEW** | JSON Pointer into the submitted payload for the reason **code** (e.g. `/reasonCode`). |
| **`reasons`** | `Dictionary<string,string>?` | **NEW** | Code → citizen-facing message. The citizen-facing copy lives in the blueprint — the replicated, shared contract. |
| **`fallbackMessage`** | `string?` | **NEW** | Used when the code is absent or not present in `reasons`. |

Message resolution on the recipient's node: `reasons[reasonCode] ?? fallbackMessage ?? ""`.

---

## 3. `RoutingResult` (engine) — `Sorcha.Blueprint.Engine/Models/RoutingResult.cs`

| Field | Type | Status | Notes |
|---|---|---|---|
| **`matchedRouteId`** | `string?` | **NEW (top-level)** | Set on every matched-route path — conditional, default, and **terminal**. The existing per-next-action `RoutedAction.MatchedRouteId` is unchanged but is absent for terminal routes (there are no next actions), which is why the top-level field is needed. |

The service-local `RoutingResult` (`ActionExecutionService.cs`) gains the same field, mapped in
`EvaluateRoutingAsync`.

---

## 4. Inbox entry (durable, unchanged shape)

Written to Tenant `public.InboxEntries` via the existing `IPlatformInboxClient` — now from the
`ReactionDispatcher` rather than the inline execution path.

| Field | Value |
|---|---|
| `PlatformUserId` | Resolved by `BlueprintInboxWriter.ResolveRecipientPlatformUserIdAsync`: participant registry first, else the sending wallet's `Owner` (a consumer wallet's `Owner` **is** the PlatformUserId). |
| `Category` | `Workflow` |
| `Severity` | From the annotation (default `Warning`). |
| `Title` | From the annotation. |
| `Summary` | The **resolved message** (`reasons[code]` / fallback). |
| `IconKey` | `workflow.rejected` |
| `CorrelationKey` | `decision:{instanceId}:{actionId}` |
| `SourceEventId` | Deterministic on `("decision-notice", wallet, instanceId, actionId)` — Tenant-side idempotency. |

---

## 5. Reaction claim (transient)

Existing `IAtomicDistributedCache` key, new kind:

```
react:{sealedTxId}:decision-notice:{recipientWallet}    (SET-NX, 7-day TTL)
```

Two independent guards against duplicates: **entitlement** (only the node hosting the recipient's wallet
proceeds — `GetWalletAsync` is local-only) and **idempotency** (this claim — replay, restart, rebuild).
