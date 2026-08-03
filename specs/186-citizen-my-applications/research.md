# Phase 0 Research: Citizen "My Applications" View

**Feature**: 186 | **Date**: 2026-08-02 | **Against**: `master` `5cbf465a` + branch `186-citizen-my-applications`

Everything below was verified by reading source, not inferred from the issue or from prior specs. Five findings contradict what the issue's design note assumed; three of them change what has to be built.

---

## R1 — The existing client method is not a working data layer

**Decision**: Retarget `IWorkflowService.GetMyWorkflowsAsync` at a new endpoint and rewrite `WorkflowInstanceViewModel` to match. Do not build on the current pairing.

**Finding**: `GetMyWorkflowsAsync` calls `GET /api/instances`, whose handler (`InstanceReadEndpoints.ListInstances`) returns **raw `Instance` models**. The client's view model does not match that shape:

| `WorkflowInstanceViewModel` | Server actually sends | Result |
|---|---|---|
| `[JsonPropertyName("id")] InstanceId` | `id` | binds |
| `BlueprintId`, `CreatedAt`, `UpdatedAt` | same names | binds |
| `Status` (string, default `"active"`) | `state` — **and as an integer** | never binds; silently reads `"active"` |
| `BlueprintName` | not sent | `""` |
| `CurrentActionName`, `CurrentStepNumber`, `TotalSteps` | not sent | `null` / `0` |
| `ParticipantCount` | `participantWallets` (a dictionary) | `0` |

So the method yields an id and two timestamps. A *rejected* application would render as `active`.

**Enum serialisation confirmed**: no `JsonStringEnumConverter` and no `ConfigureHttpJsonOptions` anywhere in `Sorcha.Blueprint.Service`, so `InstanceState` goes out as `0`–`4`.

**Rationale**: the issue's note treats this method as a ready data layer whose only defect was the claim bug fixed in #1355. It is not. #1355 was necessary — before it, consumer-tier citizens got an empty page because the handler read the `wallet_address` claim their tokens omit — but the shape mismatch is a second, independent defect that would have produced a blank-looking page even after the fix.

**Alternatives considered**: keeping the view model and mapping server-side onto its exact names. Rejected — it bakes in `Status`-vs-`state` confusion, and the view model is missing fields the page needs anyway.

---

## R2 — `IsRejection` is never set by production code

**Decision**: Do not attempt to make `InstanceState.Rejected` reachable. Derive the citizen-facing outcome from the recorded decision instead (see R3).

**Finding**: `ProjectedTransaction.IsRejection` defaults to `false` and **nothing in `src/` ever passes `true`**. `InstanceProjectionResolver.ResolveAsync` — the single constructor of `ProjectedTransaction` for both the online projector and the rebuild service — does not set it. The only place it is ever `true` is `InstanceProjectionTests.Project_Rejection_ReachesRejectedTerminalState`, which builds a `ProjectedTransaction` by hand.

The fold at `InstanceProjection.ApplyInPlace` is therefore correct and proven, and unreachable:

```
if (tx.IsRejection)  ->  instance.State = InstanceState.Rejected;   // never taken in production
```

This is the seam-bug shape exactly: both sides individually correct, the join unverified, silent, and only visible by executing the real path.

**Why the gap exists**: the one thing that builds a "rejection transaction" — `TransactionBuilderService.BuildRejectionTransactionAsync` — writes metadata `{ type: "rejection", rejectedTransactionHash, reason, timestamp }` and carries **no blueprint id, no instance id, no action id**. `ResolveAsync` requires all three and returns `null` without them, so the projector classifies it as not-instance-scoped and skips it. The transaction cannot reach the fold even in principle.

`InstanceState.Rejected` *is* still set imperatively at `ActionExecutionService:1525` when a blueprint action declares a `RejectionConfig` marked terminal. That is the path Feature 145 intends to delete.

**Alternatives considered**: fixing the resolver to set `IsRejection`. Rejected for this feature — the rejection transaction is not instance-scoped, so the fix is not a one-liner, it belongs to Feature 145's retirement of the imperative advance, and R3 gives a better answer that works for the flows citizens actually meet.

---

## R3 — A refusal is a route, not a state

**Decision**: The outcome shown to the citizen is derived from the recorded decision: the taken route's `x-decision-notice` and its severity. Instance state alone is insufficient.

**Finding**: Under Feature 184 a refusal is expressed as **taking a route that declares `x-decision-notice`** — see `ReactionDispatcher.DispatchDecisionNoticeAsync`, which resolves the notice from the taken route and writes the citizen's inbox entry with its `Title`, resolved message, and `Severity` (default `Warning`).

When such a route terminates the branch, `ApplyInPlace` sees an empty next-action set and assigns `InstanceState.Completed`. A refused application and an approved one are then **indistinguishable by state**.

This is what makes projecting `RouteId` load-bearing rather than merely convenient: it is the only thing that lets a reader find the taken route in the replicated blueprint and recover whether the ending was favourable. `RoutingDecision.RouteId` exists precisely for this, and is included in `ComputeSignableBytes`, so it is signed.

**Consequence for the spec**: added FR-027 and an edge case. Without it the page would tell a refused citizen their application "completed".

**Alternatives considered**: treating any notice-bearing terminal route as a rejection regardless of severity. Rejected — a notice can legitimately carry good news (`Severity: "Success"`); severity is the discriminator the blueprint author already controls.

---

## R4 — Where each display field actually comes from

**Decision**: Resolve server-side, with the fallbacks below.

| Field | Source | Fallback |
|---|---|---|
| `blueprintTitle` | `Instance.Metadata["BlueprintTitle"]`, set at creation (`Program.cs:2324`) | blueprint lookup by id; then the id itself |
| `instanceReference` | `Instance.Metadata["instanceReference"]`, set at first action (`ActionExecutionService:1032`) | omitted |
| `state` | `Instance.State`, rendered as its enum name | — |
| `outcome` | derived per R3 from route + notice severity | falls back to `state` |
| `decisionReason` | `route.DecisionNotice.ResolveMessage(reasonCode)` | empty string means "no reason" — omit the field |
| step position | blueprint action list vs `CurrentActionIds` | omitted when terminal |
| `needsYou` | current action's sender participant bound to one of the caller's wallets | `false` |

**Note on the title fallback**: instances created by the projector (`InstanceProjection.Project`) never populate `Metadata["BlueprintTitle"]` — only the imperative creation path does. The blueprint lookup is therefore load-bearing, not defensive.

**Note on `ResolveMessage`**: it returns `FallbackMessage ?? string.Empty`, so an unknown code yields the notice's declared fallback if there is one and an empty string otherwise. Empty must be treated as "no reason" and the field omitted — never rendered as blank text, and never replaced with invented wording (FR-013).

---

## R5 — Persistence: one migration, squashed into `InitialCreate`

**Decision**: Add `DecisionRouteId` and `DecisionReasonCode` as typed nullable columns **inside the
existing `InitialCreate` migration**, with its `.Designer.cs` and `BlueprintDbContextModelSnapshot`
kept in step. The service keeps exactly one migration.

**Finding**: `Sorcha.Blueprint.Service` has one migration, `20260528205017_InitialCreate`, and
`LastAppliedTxId` — added long after that date — appears **inside it**. `Program.cs:704` applies
migrations on startup when a connection string is present.

**What this costs, stated plainly**: amending an applied migration is invisible to any database that
already recorded it. `MigrateAsync` compares MigrationIds, sees `InitialCreate` present, and does
nothing — the new columns never appear. That is not theoretical: a first attempt shipped the columns
this way and the first live call returned `42703: column i.DecisionReasonCode does not exist` against
a three-week-old dev database.

**Why it is nonetheless right here**: the platform is pre-release with **no installations to migrate**.
Under that condition a single, readable `InitialCreate` is worth more than an accreting chain, and any
environment is brought up to date by recreating it rather than by migrating it. The rule that follows
is explicit: **while pre-release, every schema change is folded into `InitialCreate`, and an existing
database is reset rather than migrated.** `LastAppliedTxId` sits in the same migration on the same
terms, so nothing about it needs separate treatment.

**Verified rather than assumed**: `dotnet ef migrations script --idempotent` names exactly one
migration, and applying it to a scratch database produced `DecisionReasonCode`, `DecisionRouteId` and
`LastAppliedTxId` on `Instances` with a single `__EFMigrationsHistory` row.

**The hazard to guard, unchanged by any of the above**: `EfCoreInstanceStore.UpdateAsync` copies model
to entity through a **hand-written field list**. A field missing from it is written in memory, reported
as saved, and silently lost — what happened to `LastAppliedTxId`. `InMemoryInstanceStore.UpdateAsync`
stores by reference, so the suite exercised the only implementation that structurally cannot exhibit
the fault. The existing whole-model round-trip test is the guard and must be extended, not supplemented.

**Alternatives considered**: a separate additive migration, which is what a released product would need
and what the first attempt used. Rejected on the instruction that pre-release migrations stay squashed:
with no other installations, the chain buys nothing and costs readability. Also considered riding in the
`Metadata` dictionary (precedent exists — `TenantId` is smuggled through it by
`SerializeMetadataWithTenant`); rejected as extending a workaround for a known defect (#1350) rather
than a pattern.

## R6 — Endpoint placement

**Decision**: New `GET /api/me/applications` and `GET /api/me/applications/{instanceId}` in Blueprint Service. Leave `/api/instances` and `/api/instances/{id}` untouched.

**Finding**: `/api/me/*` is the established personal-scope convention — `MeInboxEndpoints`, `PersonaEndpoints`, `TwoFactorChannelEndpoints`, and a `/api/me` group in `ParticipantEndpoints`, all in Tenant Service. Blueprint Service has none yet; this adds the first.

Consumers of the existing endpoints, confirmed by grep:

- `GET /api/instances` — only `WorkflowService.GetMyWorkflowsAsync` (broken per R1) and the CLI (broken per R7). Nothing that works depends on its shape.
- `GET /api/instances/{id}` — **the PWA's `ApplicationInstance.razor` depends on it.** Reshaping it would break a working citizen surface.

**Alternatives considered**: reshaping `/api/instances` in place, since neither consumer currently works. Rejected — the detail endpoint beside it *is* load-bearing for the PWA, and having the pair disagree in shape is worse than adding a purpose-built sibling. A new group also states the intent honestly: this is a citizen projection, not a raw model read.

---

## R7 — Pre-existing CLI mismatch (out of scope, raise separately)

**Finding**: `IBlueprintServiceClient.ListInstancesAsync` is declared as

```csharp
[Get("/api/instances")]
Task<List<BlueprintInstance>> ListInstancesAsync([Query] string? blueprintId, ...);
```

against a handler that returns `{ items, totalCount, pageNumber, pageSize }` and binds no `blueprintId`. Two independent wire defects under CLAUDE.md pattern #18.

Untouched here because `/api/instances` is untouched (R6), so this feature neither causes nor worsens it. To be raised as its own issue.

---

## R8 — Surfaces and navigation, as they actually are

- **`/my-workflows`** is a redirect stub to `/new-submissions`, with a TODO to delete it. Reclaimable; retargeting it is a one-line change.
- **Web has no per-instance page.** `IInboxDetailRouter`'s doc comment says so explicitly, and `DefaultInboxDetailRouter` refuses `/api/*` hrefs — so decision notices currently render non-navigable on the web. The PWA registers `WalletInboxDetailRouter`; the web registers nothing and gets the refusing default.
- **PWA already has both** `/applications` (F154 catalogue, "Start something") and `/applications/{InstanceId:guid}` (`ApplicationInstance.razor`). Its `FloatingTabBar` has five fixed destinations with no free slot. This is why the feature is web-only this pass.
- **Web nav** "My Activity" runs New Submission → Pending Actions → My Wallet → My Credentials → My Transactions → Encryption Operations. No "what did I submit" entry.
- **`/activity` exists on both hosts** (F169 `ActivityFeed`, shared component rendering inbox entries) but is **not in the web nav**. It is where decision wording lives today.
- **i18n** is four JSON files under `Sorcha.UI.Components.User/wwwroot/i18n/` (`en`, `de`, `es`, `fr`), read via `Loc.T("key")`. `nav.pendingActions` appears in all four.
- **`/my-actions` carries `[Authorize]` and nothing more** — wallet-scoped, not role-scoped, so it serves citizens and analysts from one page. Confirms the constraint that only its label may change.
