# Tasks: Citizen "My Applications" View

**Feature**: 186 | **Branch**: `186-citizen-my-applications`

Red before green throughout. `[P]` = parallelisable with its siblings.

## Phase A — Projection (server)

- **A1** RED `InstanceProjectionResolverTests`: a sealed tx whose clear metadata carries a `RoutingDecision` with `routeId` + `reasonCode` resolves to a `ProjectedTransaction` carrying both. **Test through the resolver, not a hand-built record** — that gap is why R2 survived.
- **A2** RED `InstanceProjectionTests`: `ApplyInPlace` stamps `DecisionRouteId`/`DecisionReasonCode`; a later fold carrying no decision **clears** them; batch `Project` and incremental `Apply` agree (determinism/rebuild parity).
- **A3** GREEN: `ProjectedTransaction` gains `RouteId`/`ReasonCode` (defaulted, so existing call sites compile); resolver passes them; `Instance` gains the two fields; `ApplyInPlace` stamps and clears.

## Phase B — Persistence (server)

- **B1** RED: extend `EfCoreInstanceStoreUpdateRoundTripTests` so the whole-model round trip covers the new fields.
- **B2** GREEN: `InstanceEntity` + `BlueprintDbContext` + amended `InitialCreate` (+ `.Designer.cs`, model snapshot) + **the hand-written copy list in `EfCoreInstanceStore.UpdateAsync`**.

## Phase C — Read surface (server)

- **C1** RED `MeApplicationEndpointsTests`: participation gate; `state` as a **string**; outcome derivation table (data-model §5) including no-decision, unresolvable-route, no-notice, adverse-severity, benign-severity; `decisionReason` omitted when `ResolveMessage` returns empty; reason **code** never on the wire; `needsYou` true/false; dedupe across two wallets; deterministic order; empty page for an unresolvable caller; indistinguishable refusal on detail.
- **C2** GREEN: `MyApplicationDto` (summary + detail), `MyApplicationProjector`, `MeApplicationEndpoints`, mapped in `Program.cs` with OpenAPI summaries.
- **C3** Mutation-test the outcome guard: invert the severity check, confirm a test goes red, restore.

## Phase D — Client plumbing

- **D1** `WorkflowInstanceViewModel` rewritten to the DTO; `IWorkflowService`/`WorkflowService` retargeted at `/api/me/applications` plus a detail method.
- **D2** [P] `WebInboxDetailRouter` + registration in `Sorcha.UI.Web.Client/Program.cs`; retire the web half of the `IInboxDetailRouter` stopgap comment.

## Phase E — Pages

- **E1** RED bUnit `MyApplicationsTests`: loading, empty, populated, refused-with-reason, `needsYou` CTA present, CTA absent when not needed.
- **E2** GREEN `MyApplications.razor` at `/my-applications`, with `data-testid` on every asserted element.
- **E3** `MyApplicationDetail.razor` at `/my-applications/{InstanceId}`.
- **E4** `MyWorkflows.razor` redirect retargeted to `my-applications`.

## Phase F — Navigation

- **F1** `nav.myApplications` added and `nav.pendingActions` → `nav.workQueue` across `en`/`de`/`es`/`fr`.
- **F2** `MainLayout.razor` nav entry + rename; `MyActions.razor` heading; `StatusFooter.razor` link text. Route, scope and behaviour of `/my-actions` unchanged.

## Phase G — E2E and close-out

- **G1** `MyApplicationsPage` page object + `Docker/MyApplicationsTests` (`Category=MyApplications`): nav entry present, page loads clean, rename rendered, `/my-workflows` lands on the list.
- **G2** Docs: Blueprint Service README, `docs/reference/API-DOCUMENTATION.md`, `.specify/MASTER-TASKS.md`.
- **G3** Full `dotnet build`; run every touched test project.
- **G4** Raise the two split-out issues: email-on-decision, and the CLI `ListInstancesAsync` wire mismatch (research R7).
