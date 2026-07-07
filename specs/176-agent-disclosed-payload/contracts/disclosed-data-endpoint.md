# Contract: Disclosed prior-action data endpoint + agent consumption

**Feature**: 176-agent-disclosed-payload | **Date**: 2026-07-07

## 1. Blueprint-service endpoint (NEW — fills an already-contracted client route)

### `GET /api/workflows/{instanceId}/actions/{actionId}/disclosures`

Returns the prior-action data disclosed to the **calling participant** for the given action.

- **Also acceptable / align to existing client**: `GET /api/workflows/{instanceId}/disclosures`
  (instance-wide) — whichever matches `IBlueprintServiceClient.GetDisclosedDataAsync()` /
  `BlueprintServiceClient.cs:362-368`. The existing client + MCP `DisclosedDataTool` are the contract
  authority; implement to them.

**Auth**: `.RequireAuthorization()` (authenticated). The caller's wallet(s) are resolved from the token,
falling back to the Wallet Service when the token has no `wallet_address` claim — identical to
`ActionEndpoints.cs:178-184`. Disclosure is applied for the resolved caller wallet(s).

**Path params**: `instanceId` (string), `actionId` (int).

**200 response** (`DisclosedActionData`, aligned to the existing client type):

```jsonc
{
  "instanceId": "59ad957b-...",
  "actionId": 2,
  "registerId": "a4f3ac58...",
  "recipientResolved": true,
  "disclosedFields": {
    "name":    { "givenName": "Ada", "familyName": "Lovelace", "fullName": "Ada Lovelace" },
    "address": { "line1": "10 Downing St", "postcode": "SW1A 2AA", "country": "GB" },
    "email":   { "email": "ada@example.test" },
    "emailVerified": true,
    "portrait": { "tokenImageBase64": "…" }
  }
}
```

**Behavioural requirements**:
- `disclosedFields` contains **only** fields disclosed to the caller's participant (`ApplyDisclosures` result).
  No undisclosed field is present (FR-006/FR-010).
- When the caller is **not** a disclosure recipient for the action → `recipientResolved=false` and
  `disclosedFields` empty (drives the agent's fail-closed hold). Return 200 with the empty view (not 403) so
  the agent can distinguish "no disclosure" from "auth failure"; **or** 404/403 per the existing client's
  expectation — match the client. (Decide in tasks against the client's error handling.)
- Same view regardless of register encryption/dev-mode.
- Documented with `.WithSummary()` / `.WithDescription()`; response model has XML docs (Constitution III).

**Reuse**: disclosure computed by the extracted `IActionDisclosureResolver` (from
`ActionExecutionService.ApplyDisclosuresAsync`), so the endpoint and the execution path share one
implementation.

## 2. Shared resolver (NEW seam)

### `IActionDisclosureResolver`

```csharp
/// Resolves the fields of an instance's prior action(s) disclosed to a given wallet.
public interface IActionDisclosureResolver
{
    /// Returns the disclosed fields for {instanceId, actionId} as seen by {walletAddress}.
    Task<DisclosedActionData> ResolveAsync(
        string registerId, string instanceId, int actionId,
        IReadOnlyCollection<string> callerWallets, CancellationToken ct);
}
```

`ActionExecutionService` is refactored to depend on this resolver (no behaviour change; covered by tests to
prevent drift).

## 3. Client (`IBlueprintServiceClient`)

`GetDisclosedDataAsync(instanceId, actionId?)` — confirm the existing signature + return type; the endpoint is
written to match it. No new client method unless the existing one's shape needs a documented adjustment.

## 4. Agent consumption (`Sorcha.Agent`)

- **Fetch**: for each discovered pending action, before deciding, call `GetDisclosedDataAsync(instanceId,
  actionId)` and set `PendingAction.PreviousPayload = disclosedFields`.
- **Mapping cleanup**: `PollingInboxListener` maps `dataSchema` (not `schema`); `PreviousPayload` now comes
  from the disclosed-data fetch, not the pending summary.
- **Fail-closed** (`RulesDecisionEngine`, mirroring the #1077 `_rulesRequireChecks` pattern):
  - if the disclosed-data fetch **fails** → `hold` ("Disclosed application data unavailable; held for manual review");
  - if `recipientResolved=false` / `disclosedFields` empty **and** the rules require data → `hold`;
  - never approve/reject on an empty payload.
- **Explainability** (FR-008): keep the structured log of evaluated check facts
  (`External checks evaluated … {facts} (from payload fields: […])`).

## 5. Acceptance (maps to spec Success Criteria)

| Contract behaviour | Spec |
|---|---|
| Invalid application (bad postcode) → agent rejects, no credential | SC-001, SC-003, US1-AS1 |
| Valid application → agent approves, credential delivered | SC-002, US1-AS2 |
| Endpoint returns only disclosed fields | FR-006/FR-010 |
| Disclosed data unavailable → agent holds, no decision | SC-004, US2 |
| Evaluated check facts retrievable/logged | SC-005, US3 |
| End-to-end regression (valid + invalid) runs unattended | SC-006 |
