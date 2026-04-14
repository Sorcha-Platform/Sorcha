# Wave 14 — Credential Claim Action Design

**Feature:** 103 (Verified Citizen v2) — Wave 14
**Date:** 2026-04-14
**Status:** Design — awaiting user review before planning
**Depends on:** Wave 13 (`HaipLocalReceiveService`, `CredentialOfferQrCard`) merged as PR #281
**Supersedes:** Wave 13 direct QR dialog approach for HAIP credential issuance inside blueprint flows

---

## Goal

Deliver credential offers minted during blueprint execution to the **intended recipient** (the citizen), not to the action sender (the assessor), so the credential lands in the recipient's local wallet with their explicit consent and a full audit trail on the register.

## Context & Motivation

Wave 13 shipped `HaipLocalReceiveService` and a "Receive on this device" button on `CredentialOfferQrCard`, enabling local claiming of HAIP credential offers. However, the QR dialog opens in the **action sender's browser session** (the assessor), because `ActionExecutionService` returns the minted offer in the `ActionExecuteResult` directly to whoever submitted the action. This has two problems:

1. **Semantic mismatch.** The credential is meant for the citizen. The assessor only approved the issuance. Landing the credential in the assessor's wallet is mechanically possible but wrong.
2. **Crypto leak risk.** The OpenID4VCI `pre_authorized_code` is a bearer token. Whoever redeems it first binds their key to the resulting SD-JWT VC via the `cnf` claim. If the assessor's browser holds the code and the assessor clicks Claim, the credential is cryptographically tied to the **assessor's** wallet key, not the citizen's.

The right shape is: the credential offer must reach the citizen's session and only the citizen's wallet should be able to redeem it.

## Key Decisions (from brainstorm)

### Decision 1 — Credential delivery is a blueprint action, not a side channel

**Chosen:** Add a third action (action ID 2, "Claim Credential") to credential-issuing blueprints. The claim action is sender-locked to the same open participant as action 0, which is already late-bound to the citizen's wallet. The offer reaches the citizen by appearing in their `MyActions` queue as a pending action with prepopulated payload.

Action IDs in this spec use the 0-indexed blueprint convention: action 0 is the starting action (citizen's application), action 1 is the assessor's approval, action 2 is the citizen's claim.

**Rejected: server-side credential offer inbox.** A separate MongoDB collection keyed by recipient wallet with a new `/api/credentials/pending` endpoint and new "Inbox" tab in `MyCredentials`. Duplicates `MyActions` as a second "pending work for this wallet" surface, bypasses the register's audit trail, and requires recipient-locking to be reinvented inside the inbox service instead of inherited from blueprint sender binding. Also requires new SignalR push wiring instead of reusing `MyActions`'s existing refresh mechanism.

**Why the action approach wins:** recipient-locking is free (blueprint sender binding + late-bind already enforce it); durability is free (offer lives as persisted blueprint instance state, so offline citizens can claim later); audit trail is free (action 3 sealing writes to the register); no new subsystem.

### Decision 2 — Claim executes client-side in the citizen's browser

**Chosen:** `HaipLocalReceiveService` (wave 13) is reused as-is, triggered from the claim action's renderer when the citizen clicks the Claim button.

**Rejected: server-side claim provisioning.** Blueprint.Service would hold the pre-auth code briefly, mint the proof-of-possession JWT using the Wallet Service, and write the SD-JWT VC directly to the citizen's credential store. More robust against the citizen closing the tab mid-claim but requires server-side session management of HAIP tokens, coordination between Blueprint.Service and Wallet Service for key usage, and additional trust boundary work. Deferred — can be added later for non-blueprint issuance flows without breaking the client-side path.

### Decision 3 — Standards-aligned payload shape (OpenID4VCI + DIF Credential Manifest)

**Chosen:** Action 3's payload carries a single object field `credentialOffer` with sub-properties that mirror OpenID4VCI (`credential_offer_uri`, snake_case, per spec) and DIF Credential Manifest (`display` descriptor shape for human-readable metadata). The `x-credential-offer: true` schema extension marks the field as "render me as a credential claim card."

**Rejected: Sorcha-specific field names.** Inventing `offerUri`/`issuerName`/`purpose` would work but makes the payload opaque to any wallet or tooling that knows OpenID4VCI. Standards alignment costs nothing and buys interop.

**Rejected: W3C VC Data Model 2.0 for offer-time data.** VC Data Model describes the credential *after* issuance, not the offer. Not applicable to action 3's payload.

**Rejected: fetching issuer metadata on render instead of embedding `display`.** Would be the purest OpenID4VCI-native path but adds a network round trip on every render, gives the blueprint author no control over the purpose string or localized copy, and breaks if the issuer is briefly unreachable. `display` embedded in the payload is the pragmatic choice. Falling back to issuer metadata fetch is still possible if `display` is absent, so the degrade-gracefully path stays open.

### Decision 4 — Retry-friendly failure semantics

**Chosen:** If `HaipLocalReceiveService` fails mid-claim (network, HAIP issuer down, transient 5xx, local wallet store write failure), action 2 stays in `Pending` so the citizen can retry. Offer expiry (the `expires_at` timestamp has passed) transitions the action to `Failed` without consuming it. Successful claim completes action 2 with a minimal `{"claimed_at": "..."}` confirmation payload sealed to the register.

**Rejected: one-shot semantics.** Failure completes the action with an error outcome and the citizen cannot retry. Hostile UX for transient failures that are common in the real world (phone signal drops during QR scan, HAIP service bounces).

### Decision 5 — Two-PR split (engine primitive, then feature)

**Chosen:** Wave 14 ships as two PRs. **Wave 14a** adds the engine's `OutputMapping` primitive and `Instance.PendingActionPayloads` storage with zero user-facing features and full engine test coverage. **Wave 14b** adds `x-credential-offer` schema extension, `CredentialClaimCard` component, and the Verified Citizen v2 blueprint v3 update that uses them.

**Rejected: single PR.** Mixing a new engine primitive with a new UI feature makes review harder and couples two independently-useful changes. The engine primitive is valuable on its own for future blueprints that need any form of action-to-action data carry-forward; it deserves focused review and tests independent of credentials.

## Engine Primitive — Prepopulated Action Payloads (Wave 14a)

The blueprint engine does not today have a "previous action populates next action's payload" mechanism. After an action executes, `ActionExecutionService` removes the completed action from `Instance.CurrentActionIds` and adds next action IDs, but next actions have no associated payload until their sender submits one. Wave 14a adds this primitive.

### Contract additions

**`Sorcha.Blueprint.Models.Route`** gains an optional field:

```csharp
public class Route
{
    // ... existing fields ...

    /// <summary>
    /// Optional map of JSON Pointer expressions describing which fields from the
    /// current action's execution result should be carried forward as prepopulated
    /// payload data for each next action. Keys are "/path/in/current/result",
    /// values are "/path/in/next/action/payload". Evaluated per next action ID
    /// when routing fires.
    /// </summary>
    public Dictionary<string, string>? OutputMapping { get; set; }
}
```

The "current action's execution result" source document is the union of:

- The submitted action payload (`action.payload`)
- Calculated values from the engine's calculate step (`action.calculations`)
- HAIP mint output when present (`action.haip` → `{ credential_offer_uri, offer_id, expires_at, display }`)

Exact shape of this source document is documented in the engine implementation and locked by tests.

**`Sorcha.Blueprint.Engine.Models.RoutingResult`** gains a transient carrier:

```csharp
public record RoutingResult(
    IReadOnlyList<int> NextActions,
    // ... existing fields ...
    IReadOnlyDictionary<int, JsonObject>? PendingPayloads // NEW
);
```

`RoutingEngine` evaluates `Route.OutputMapping` for each next action in the matched route and populates `PendingPayloads[nextActionId]` with a JSON object built from the mapping. If a route has no `OutputMapping`, `PendingPayloads` is null and behavior is unchanged.

**`Sorcha.Blueprint.Service.Models.Instance`** gains a persisted field:

```csharp
public class Instance
{
    // ... existing fields ...

    /// <summary>
    /// Prepopulated payload seed per pending action ID, written by routing when
    /// a previous action's Route.OutputMapping carried data forward. Empty for
    /// actions that receive no carry-forward data. UI reads from this when
    /// rendering the action; ActionExecutionService merges it with the submitted
    /// payload on execute (submission wins on conflict).
    /// </summary>
    public Dictionary<int, JsonObject> PendingActionPayloads { get; set; } = new();
}
```

When a pending action is completed (successfully or otherwise), its entry is removed from `PendingActionPayloads`.

### Execution flow changes in `ActionExecutionService`

1. After `ExecutionEngine.ExecuteAsync` returns a `RoutingResult`, merge `routingResult.PendingPayloads` into `instance.PendingActionPayloads` for each next action ID.
2. When loading a pending action for rendering (existing code path that surfaces actions in `MyActions`), include `PendingActionPayloads[actionId]` in the returned action view model.
3. On `SubmitActionExecuteAsync`, merge the prepopulated payload with the submitted payload before validation. Submitted payload fields take precedence on any JSON Pointer collision. For the credential claim action, there is no collision — the citizen only supplies `claimed_at`, which is outside the `credentialOffer` sub-tree.
4. Remove the consumed entry from `PendingActionPayloads` atomically with the action completion write.

### UI read path (Wave 14a scope)

`MyActions.razor` and the pending action API surface the prepopulated payload on the action view model. `ActionWorkspace` seeds the form's initial state from it. For existing form-rendered actions with no `OutputMapping` upstream, nothing changes. For actions with a seed payload, form fields corresponding to seed keys are pre-filled (and, per the `x-readonly` extension if present, may be non-editable).

Wave 14a ships this path but does not yet ship a consumer that uses it visibly — the first visible consumer is wave 14b's credential claim card.

### Encryption

Pending action payloads sit on the Instance in MongoDB alongside `AccumulatedData`. The credential offer contains the OpenID4VCI `pre_authorized_code`, a short-lived bearer token (typical validity: minutes to hours). While not a long-lived secret, it warrants at-rest encryption on the same channel as the encrypted action payloads already use. Wave 14a inherits whatever mechanism `AccumulatedData` uses today; if that is plaintext, wave 14a encrypts `PendingActionPayloads` using the existing per-recipient payload encryption path. Exact mechanism confirmed during planning against current `AccumulatedData` handling.

### Test coverage (Wave 14a)

- Unit tests on `RoutingEngine`: output mapping with no mapping (no-op), mapping with one field, mapping with nested paths, mapping when source field is absent (skip, not error), mapping with multiple next actions in one route (each gets its own evaluated payload), conditional route with mapping (mapping only fires if condition matches).
- Unit tests on `ActionExecutionService`: next action receives seeded payload, UI view model includes prepopulated data, submitted payload merges with seed (submission precedence), consumed seed is removed on completion, seed is retained if action execution fails.
- Integration test: two-action blueprint where action 1 writes `{ "carried": "value" }` into action 2's `/seeded/value`. Execute action 1, assert action 2's pending state has the seeded payload, execute action 2, assert success.

## Credential Claim Feature (Wave 14b)

Wave 14b is the first consumer of the wave 14a primitive. All changes are additive to UI and to the Verified Citizen v2 blueprint.

### Schema extension — `x-credential-offer`

New optional field extension on blueprint action `dataSchemas`. When set on an object field, `SorchaFormRenderer` renders the `CredentialClaimCard` component instead of a generic object editor. The field's value must conform to the shape below.

```json
{
  "credentialOffer": {
    "type": "object",
    "x-credential-offer": true,
    "properties": {
      "credential_offer_uri": {
        "type": "string",
        "format": "uri",
        "description": "Canonical OpenID4VCI offer URI (openid-credential-offer://...)"
      },
      "display": {
        "type": "object",
        "description": "DIF Credential Manifest-style display descriptor",
        "properties": {
          "title":       { "type": "string" },
          "subtitle":    { "type": "string" },
          "description": { "type": "string" },
          "issuer": {
            "type": "object",
            "properties": {
              "name": { "type": "string" },
              "logo": {
                "type": "object",
                "properties": {
                  "uri": { "type": "string", "format": "uri" },
                  "alt": { "type": "string" }
                }
              }
            }
          }
        }
      },
      "expires_at": { "type": "string", "format": "date-time" }
    },
    "required": ["credential_offer_uri"]
  }
}
```

Snake_case on `credential_offer_uri` and `expires_at` matches the OpenID4VCI spec verbatim. Camel/PascalCase is used everywhere else in Sorcha schemas; this is a deliberate exception for the two fields that have direct OpenID4VCI counterparts, so anyone grepping for the spec terms finds them. `display` follows DIF Credential Manifest naming. The outer wrapper field name (`credentialOffer`) stays camelCase consistent with Sorcha conventions.

### `CredentialClaimCard` component

New Blazor component `src/Apps/Sorcha.UI/Sorcha.UI.Web.Client/Components/Credentials/CredentialClaimCard.razor`. Wraps wave 13's `CredentialOfferQrCard` and adds:

- **Header block** sourced from `display`: title, subtitle, description, issuer name + logo.
- **Primary action — Claim credential**: invokes `HaipLocalReceiveService.ReceiveLocallyAsync` with the current citizen's wallet address (known from authentication context, not from the payload). On success: snackbar, status transitions to Exchanged, auto-submits action 2 with `{ "claimed_at": <ISO-8601 now> }` confirmation payload, action completes, citizen is navigated to `MyCredentials`.
- **Secondary action — Decline**: action 2 is cancelled via a blueprint-level cancel path (exact mechanism decided in planning — options are a `rejected: true` confirmation payload routed to a terminal "declined" action, or marking the action cancelled without consuming the offer; the two have different register audit semantics).
- **Show QR alternative**: button labelled "Scan with external wallet" that reveals the `CredentialOfferQrCard`'s QR view in place. External wallets scan the QR and run the standard HAIP flow independently; the Sorcha UI cannot directly observe completion of an external-wallet claim, but it can still poll the HAIP offer status endpoint (wave 13 already does this in `CredentialOfferQrCard`) and transition the action to complete when the offer status reaches `Exchanged`.

The card uses only data from the payload (`credentialOffer` object) for display. It takes the wallet address from the authenticated session, not from the payload, so the address cannot be spoofed by a malicious blueprint author.

### Renderer wiring

`SorchaFormRenderer` detects `x-credential-offer: true` on an object field and swaps in `CredentialClaimCard` in place of the default object editor. This follows the existing pattern for `x-persona`, `x-file`, etc. When the card's primary action completes, the renderer treats it as a form submission with the confirmation payload and the normal submit flow takes over (including the `ActionExecutionService` round trip that seals action 3 to the register).

Form-level validation is skipped for fields under `x-credential-offer`: the citizen does not edit them, they are content-for-display.

### Retry semantics (wired in `CredentialClaimCard`)

- Claim fails with network/5xx/transient error: snackbar shows the error, Claim button re-enables, action stays in `Pending`. Citizen can click again.
- Offer expiry reached (`expires_at < now`): card shows expired state, Claim disabled, snackbar explains what happened. The action transitions to `Failed` via a client-side status update request (or a new engine path — decided in planning, see Open Questions). The citizen cannot retry a fresh claim from within action 2; a new application starts from action 0.
- Claim succeeds but action 2 submit fails (instance sealing error): the credential is already in the local wallet (from `HaipLocalReceiveService.ReceiveLocallyAsync`), so the user-visible outcome is success. A background retry re-submits action 2. Worst case the action remains pending with the credential already claimed; this is recoverable via an ops tool and is explicitly acceptable for v1.

### Verified Citizen v2 blueprint v3

The Verified Citizen v2 blueprint (`examples/templates/verified-citizen-v2.json`) is updated to add a third action:

```
Action 0: Submit application               (sender: applicant, open/late-bind, form)
Action 1: Review and issue credential      (sender: government-assessor, form + HAIP mint trigger)
          Route → Action 2 with OutputMapping:
             "/haip/credential_offer_uri" → "/credentialOffer/credential_offer_uri"
             "/haip/display"              → "/credentialOffer/display"
             "/haip/expires_at"           → "/credentialOffer/expires_at"
Action 2: Claim credential                 (sender: applicant, x-credential-offer renderer)
```

The existing Verified Citizen v2 blueprint from wave 7 onwards has two actions (0 and 1). Wave 14b adds action 2 and re-publishes as a new version. The HAIP Driving Licence blueprint receives the same treatment since it shares the issuance pattern.

`ActionExecutionService`'s HAIP minting code path, which today writes the minted offer into the response returned to the assessor, is unchanged in its mint behaviour. For blueprints whose action 1 routes to an `x-credential-offer` action 2, the HAIP mint output is **additionally** exposed to the routing source document at `/haip/*` where `OutputMapping` picks it up. Blueprints that do not use the claim-action pattern keep receiving the minted offer in the response payload exactly as today, unchanged for backward compatibility.

### Test coverage (Wave 14b)

- Playwright E2E: full Verified Citizen v2 flow. Citizen submits action 0, assessor approves action 1, citizen sees action 2 in MyActions with credential claim card, clicks Claim, credential appears in MyCredentials, action 2 completes, register shows the sealed completion.
- Unit tests on the renderer: `x-credential-offer` triggers card mount, payload with missing `display` falls back to minimal rendering, payload with expired `expires_at` shows expired state.
- Unit tests on `CredentialClaimCard`: Claim button wires through to `HaipLocalReceiveService`, success path submits confirmation, failure path keeps action 2 pending, Decline button cancels without consuming the offer.
- Existing HaipVerifiedCitizen walkthrough updated to drive the new three-action flow and verify the credential reaches the citizen's wallet rather than the assessor's.

## Sequencing and PRs

**PR 1 — Wave 14a (engine primitive)**

- `Route.OutputMapping` field
- `RoutingResult.PendingPayloads` transient carrier
- `RoutingEngine` output mapping evaluation
- `Instance.PendingActionPayloads` persisted field
- `ActionExecutionService` seed/merge/clear flow
- Pending action view model surfaces seeded payload
- Engine test suite (unit + integration)
- Zero user-visible changes; existing blueprints unaffected because `OutputMapping` is null everywhere

**PR 2 — Wave 14b (credential claim feature)**

- `x-credential-offer` schema extension and renderer handler
- `CredentialClaimCard` component (header + Claim + Decline + QR-for-external)
- Verified Citizen v2 blueprint v3 with action 2
- HAIP Driving Licence blueprint v2 with equivalent action
- `ActionExecutionService` HAIP mint source-document wiring at `/haip/*`
- Playwright E2E coverage
- Walkthrough updates (HaipVerifiedCitizen + HaipDrivingLicence)

## Out of scope (explicit non-goals)

- **Server-side claim provisioning.** Deferred. When non-blueprint credential issuance is needed, server-side claim can be added as an alternative executor without touching the client-side path.
- **JSON Logic expressions in `OutputMapping`.** V1 is pure JSON Pointer source→target. Expression support can be added later if a blueprint needs to compute a carried value.
- **Retrofit of existing blueprints to use `OutputMapping`.** It is a new primitive; existing blueprints keep working unchanged. Retrofits happen only when a blueprint actively needs the capability.
- **External-wallet claim completion telemetry.** When the citizen scans the QR with an external wallet, the Sorcha UI polls HAIP offer status (wave 13 mechanism) to transition action 2 to complete. Beyond status-polling, no additional telemetry.
- **Credential claim notifications via SignalR push.** Not in v1. Action 2 appears in `MyActions` on the next natural page load or refresh. SignalR push for new pending actions can be added as a cross-cutting improvement — it's valuable for many flows, not just credential claim, and does not belong in the wave 14 scope.
- **Non-Sorcha citizen recipients.** The design assumes the citizen has a Sorcha wallet because they started the blueprint from the Sorcha UI. Out-of-band claim by a citizen who is not yet a Sorcha user is a separate future design.
- **Migration of wave 13's dialog-based QR flow for non-claim-action callers.** Any blueprint that does not use the `x-credential-offer` action-based path continues to receive the minted offer in the response payload exactly as today. Wave 13's `CredentialOfferQrDialog` remains for those callers.

## Open questions (to resolve during planning, not blocking the spec)

- **`PendingActionPayloads` encryption.** Decide against the current state of `AccumulatedData` encryption.
- **Decline semantics on the register.** Cancel action vs. terminal "declined" routing action. Each has distinct audit implications.
- **Expiry transition mechanism.** Client-side status update vs. engine-side scheduled check. Client-side is simpler but misses expiries for offline citizens. Either is acceptable for v1; the decision affects a small amount of code.
- **Action 2's form validation behaviour.** Confirm that skipping validation on `x-credential-offer` fields does not break the existing form submit plumbing.

## Acceptance criteria

1. A new blueprint can declare `Route.OutputMapping` and have data carried from one action's result into the next action's prepopulated payload.
2. `Instance.PendingActionPayloads` persists the seeded data across page reloads and service restarts.
3. The Verified Citizen v2 blueprint has three actions (0, 1, 2). After the assessor approves action 1, the citizen sees action 2 in their `MyActions` queue with a credential claim card showing the issuer, credential type, purpose, and expiry.
4. The citizen clicks "Claim credential", the credential is received by `HaipLocalReceiveService`, lands in the citizen's local wallet, appears in `MyCredentials`, and action 2 is sealed to the register as completed with a `claimed_at` timestamp.
5. The assessor's wallet never contains a Verified Citizen credential claimed via this flow (the assessor's browser never holds the pre-auth code).
6. Claim failure (network error, HAIP issuer 5xx) leaves action 2 in `Pending` and the citizen can retry.
7. Offer expiry transitions action 2 to `Failed` and a fresh application must be started to retry.
8. External-wallet scan of the embedded QR completes the flow by driving the same status transition, without the credential passing through the Sorcha wallet.
9. The `HaipVerifiedCitizen` and `HaipDrivingLicence` walkthroughs pass end-to-end against both local Docker and n1.sorcha.dev.

## File touch points (indicative, firmed up in planning)

**Wave 14a**

- `src/Common/Sorcha.Blueprint.Models/Route.cs` — `OutputMapping` field
- `src/Core/Sorcha.Blueprint.Engine/Routing/RoutingEngine.cs` — mapping evaluation
- `src/Core/Sorcha.Blueprint.Engine/Models/RoutingResult.cs` — `PendingPayloads`
- `src/Services/Sorcha.Blueprint.Service/Models/Instance.cs` — `PendingActionPayloads`
- `src/Services/Sorcha.Blueprint.Service/Services/Implementation/ActionExecutionService.cs` — seed/merge/clear
- `src/Services/Sorcha.Blueprint.Service/Endpoints/` — pending action view model includes seed
- `tests/Sorcha.Blueprint.Engine.Tests/` — new routing tests
- `tests/Sorcha.Blueprint.Service.IntegrationTests/` — two-action carry-forward integration test

**Wave 14b**

- `src/Apps/Sorcha.UI/Sorcha.UI.Web.Client/Components/Credentials/CredentialClaimCard.razor` — new
- `src/Apps/Sorcha.UI/Sorcha.UI.Core/Components/Forms/SorchaFormRenderer.razor.cs` (or equivalent) — `x-credential-offer` handler
- `src/Services/Sorcha.Blueprint.Service/Services/Implementation/ActionExecutionService.cs` — HAIP mint writes to source doc at `/haip/*`
- `examples/templates/verified-citizen-v2.json` — v3 with action 2
- `examples/templates/haip-driving-licence.json` — v2 with equivalent action
- `walkthroughs/HaipVerifiedCitizen/` — updated flow
- `walkthroughs/HaipDrivingLicence/` — updated flow
- `tests/Sorcha.UI.E2E.Tests/Docker/CredentialClaimTests.cs` — Playwright E2E
- `tests/Sorcha.UI.Core.Tests/` — renderer and card unit tests

---

**Next step:** user reviews this spec, then invoke the `writing-plans` skill to produce the phased implementation plan.
