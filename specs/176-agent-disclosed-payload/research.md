# Phase 0 Research: Autonomous agent decides on disclosed application data

**Feature**: 176-agent-disclosed-payload | **Date**: 2026-07-07

## Problem restated

The autonomous agent (`Sorcha.Agent`) evaluates its external checks against `PendingAction.PreviousPayload`,
which it maps from the `/api/actions/pending` response. That response (`PendingActionSummary`) carries
`PrepopulatedPayload` (a Feature-104 form-prefill seed — empty for the AIAS verify action) and `DataSchema`,
**not** the disclosed prior-action application data. So the agent decides on an empty payload; every check
defaults to `false`. Confirmed live (n1, 2026-07-07): a diagnostic log showed
`External checks evaluated … (from payload fields: [])`, and "ZZ99 9ZZ" was approved.

## Key facts established (codebase, cited)

- **The disclosed-data endpoints are already CONTRACTED but NOT IMPLEMENTED.**
  `IBlueprintServiceClient.GetDisclosedDataAsync()` → `GET /api/workflows/{instanceId}/actions/{actionId}/disclosures`
  and `GetActionDetailsAsync()` → `GET /api/actions/{actionInstanceId}`
  (`src/Common/Sorcha.ServiceClients.Http/Blueprint/BlueprintServiceClient.cs:362-368`,
  `IBlueprintServiceClient.cs:149-154`). Consumed by the MCP participant tools
  (`Sorcha.McpServer/Tools/Participant/DisclosedDataTool.cs`, `ActionDetailsTool.cs`). The blueprint-service
  has **no handler** for these routes yet.
- **Disclosure resolution exists but is private.** `ActionExecutionService.ApplyDisclosuresAsync`
  (`src/Services/Sorcha.Blueprint.Service/Services/Implementation/ActionExecutionService.cs:1722-1783`) calls
  `_executionEngine.ApplyDisclosures(data, action)` (JSON-Pointer disclosure rules) and resolves participant →
  wallet, returning `Dictionary<walletAddress, Dictionary<field, value>>` — the fields disclosed to each
  recipient wallet.
- **The pending endpoint already resolves the caller's wallets without a `wallet_address` claim.**
  `ActionEndpoints` (`ActionEndpoints.cs:27-114, 178-184`) resolves the caller's owned wallets via a Wallet
  Service fallback when the token omits `wallet_address` (F136 consumer/service tokens omit wallet binding).
  `EfCoreInstanceStore.GetPendingActionsByWalletAsync(wallet)` runs per resolved wallet.
- **The agent's wallet IS the disclosure recipient.** At AIAS provisioning the `verification-analyst`
  participant placeholder is bound to the agent's wallet (provision log: "Patched verification-analyst ->
  ws11q…"). So "disclosed to `verification-analyst`" == "disclosed to the agent's wallet". Resolving the
  caller's wallet and applying disclosure for it yields exactly the view the agent needs.
- **The agent authenticates as a user (email/password), not a bare service token.**
  `Sorcha.Agent/Auth/AgentAuthService.cs:36-90` — so the same wallet-resolution the pending endpoint uses
  applies. (A future move to a pure service token would need wallet resolution from `platform_user_id`/`org` —
  noted as a follow-up, not a blocker here.)
- **Fail-closed pattern already exists.** `RulesDecisionEngine` (`RulesDecisionEngine.cs:26-31, 80-95`) holds
  when `_rulesRequireChecks` is true but no check facts are available (#1077). The same shape extends to
  "hold when the disclosed payload is unavailable".

## Decision: **Design A — implement the already-contracted disclosed-data query endpoint; the agent fetches it per pending action**

Implement the disclosed-data endpoint that `IBlueprintServiceClient.GetDisclosedDataAsync()` and the MCP
`DisclosedDataTool` already expect, in the blueprint-service. It:
1. requires authentication (`.RequireAuthorization()`), resolves the **caller's** wallet(s) via the same
   Wallet-Service fallback `ActionEndpoints` uses;
2. reconstructs the instance's accumulated data for the target action from its sealed transactions;
3. applies the blueprint's disclosure rules for the caller's wallet(s) (reusing the `ApplyDisclosures`
   engine call, extracted into a shared, injectable resolver so it is no longer private to
   `ActionExecutionService`);
4. returns the disclosed prior-action payload (keyed by prior action) for that participant.

The agent's inbox/decision layer fetches this per pending action and populates `PendingAction.PreviousPayload`
before the checks run; if the fetch fails or returns empty when data is required, the agent **holds**.

### Rationale

- **It is the intended contract.** The client interface + MCP tools already target these routes; we are
  filling an implemented-elsewhere gap, and the human MCP participant surface and the agent share one endpoint
  and one disclosure code path (no divergence).
- **Correct disclosure semantics.** Disclosure is keyed by the *recipient* participant's wallet. Design A
  applies disclosure for the caller's wallet — which is the recipient here — so the agent sees exactly what
  `verification-analyst` is entitled to and nothing more (FR-006, FR-010).
- **Separation of concerns.** Disclosed data is an on-demand read query, kept out of the high-frequency
  `/api/actions/pending` poll and distinct from the Feature-104 `PrepopulatedPayload` form-prefill.
- **Reuses existing machinery** (`ApplyDisclosures`, wallet-resolution fallback) rather than inventing new
  disclosure logic — lower risk, DAD-model-faithful.

### Alternatives considered

- **Design B — enrich `/api/actions/pending` with the disclosed payload.** Rejected: (1) conflates
  Feature-104 form-prefill with disclosed prior-action data in one field; (2) forces disclosure resolution
  into the hot polling path for every poll even when unused; (3) muddies the "keyed by recipient wallet"
  semantics by piggy-backing on the "wallets the caller owns" query. The single-round-trip benefit does not
  outweigh the architectural cost, and it would not be reusable by the MCP participant tools that already
  expect the dedicated endpoint.
- **Add a participant/wallet claim to the agent's token.** Rejected as unnecessary and F136-contradicting —
  the wallet-service fallback already resolves the caller's wallets server-side.

### Consequences / follow-ups

- Extract the disclosure resolution out of `ActionExecutionService` into a shared resolver so both the
  execution path and the new endpoint use one implementation (no behaviour fork).
- Finalise the field-name correction already staged on the branch (`PollingInboxListener` read
  `payload`/`schema`; the API serialises `prepopulatedPayload`/`dataSchema`) — but the agent will now source
  `PreviousPayload` from the disclosed-data fetch, so the mapping change becomes moot for `PreviousPayload`
  and remains only for `Schema` (`dataSchema`).
- Pure-service-token agents (no user login) are out of scope; if adopted later, wallet resolution from
  `platform_user_id`/`org_id` is the extension point.

## Unknowns resolved

| Unknown | Resolution |
|---|---|
| Does a disclosed-projection endpoint exist? | Contracted client-side + MCP, **not implemented** server-side. Implement it. |
| How is per-participant disclosure computed? | `ApplyDisclosures` engine call + participant→wallet resolution (`ActionExecutionService.ApplyDisclosuresAsync`); extract to a shared resolver. |
| Can the agent be identified as its participant? | Yes — via the caller's-wallet resolution the pending endpoint already uses; the agent's wallet is the `verification-analyst` recipient. |
| Where does fail-closed live? | `RulesDecisionEngine` hold path (#1077); extend to "disclosed payload unavailable". |
