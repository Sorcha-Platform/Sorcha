# Data Model — Feature 104 Credential Claim Action

**Date:** 2026-04-14
**Scope:** Data shapes, entities, relationships, and state transitions introduced by wave 14. All shapes are additive: no existing model is breaking-changed.

---

## Entity overview

```
Blueprint (existing)
 └─ Action (existing, unchanged)
     └─ Route (existing, + OutputMapping)         ← wave 14a
         └─ OutputMapping (new, wave 14a)

Instance (existing, + PendingActionPayloads)       ← wave 14a
 └─ PendingActionPayload (new, wave 14a)
     └─ CredentialOfferPayload (new, wave 14b)

BlueprintAction.DataSchema (existing JSON Schema)
 └─ x-credential-offer extension (new, wave 14b)
```

---

## Wave 14a — Engine primitive

### 1. OutputMapping (new, on `Route`)

A declarative mapping from JSON Pointer paths in the current action's execution result to JSON Pointer paths in the next action's prepopulated payload.

**Location:** `src/Common/Sorcha.Blueprint.Models/Route.cs`

**Shape:**

```csharp
public class Route
{
    // ... existing fields (Id, NextActionIds, Condition, IsDefault, BranchDeadline) ...

    /// <summary>
    /// Optional map from source JSON Pointer (in the current action's execution
    /// result document) to target JSON Pointer (in the next action's starting
    /// payload). Evaluated when this route fires during action execution. Absent
    /// source paths are silently skipped. Applies to every next action listed in
    /// <see cref="NextActionIds"/>.
    /// </summary>
    public Dictionary<string, string>? OutputMapping { get; set; }
}
```

**Serialization (JSON):**

```json
{
  "id": "route-approved",
  "nextActionIds": [2],
  "condition": { "==": [{ "var": "decision" }, "approved"] },
  "outputMapping": {
    "/haip/credential_offer_uri": "/credentialOffer/credential_offer_uri",
    "/haip/display":              "/credentialOffer/display",
    "/haip/expires_at":           "/credentialOffer/expires_at"
  }
}
```

**Source document shape** (the left-hand side of `OutputMapping` entries is evaluated against this):

```
{
  "/payload":       { ...submitted action payload... },
  "/calculations":  { ...engine calculate-step output... },
  "/haip":          { ...HAIP mint output, when present... }
}
```

**Validation rules:**
- Both keys and values MUST be valid JSON Pointer strings (RFC 6901). Leading `/` required.
- Target paths MUST correspond to fields declared on at least one `DataSchema` on the target action. Enforced at blueprint publish time (new validation check `VAL_BP_011`).
- Source paths are not validated at publish time (they may reference data that only exists at runtime, for example `/haip/*` which is absent for non-HAIP-minting actions).
- An `OutputMapping` with no entries is equivalent to no `OutputMapping` — no-op.

---

### 2. PendingActionPayload (new, on `Instance`)

Seed payload data attached to a pending action, written by a previous action's `OutputMapping`, read by the UI and merged by `ActionExecutionService` on action submission.

**Location:** `src/Services/Sorcha.Blueprint.Service/Models/Instance.cs`

**Shape:**

```csharp
public class Instance
{
    // ... existing fields ...

    /// <summary>
    /// Prepopulated payload data seeded per pending action ID when a previous
    /// action's Route.OutputMapping carried data forward. Keyed by action ID;
    /// value is the JSON object to merge with the action submission before
    /// validation. Empty for actions that receive no carry-forward data.
    /// Entries are removed atomically with the action's resolution (complete,
    /// reject, or expire).
    /// </summary>
    public Dictionary<int, JsonObject> PendingActionPayloads { get; set; } = new();
}
```

**Persistence:** MongoDB via `EfCoreInstanceStore`. Serialized as JSON alongside `AccumulatedData`. Plaintext at rest (see research decision 1).

**Lifecycle:**

```
(no seed)
    │
    │ Route.OutputMapping fires on previous action execution
    ▼
seed-present
    │
    │ Recipient submits action → merge → validate → seal
    │  OR
    │ Recipient rejects action → RejectionConfig.IsTerminal path
    │  OR
    │ Expiry → client-side expire endpoint
    ▼
(seed removed)
```

**Merge semantics on submission:**
1. Load `instance.PendingActionPayloads[actionId]` (returns empty object if absent).
2. Start from seed object (deep copy).
3. Apply the submitted payload: field-level merge, submitted values take precedence on key collisions. Nested objects merged recursively; nested arrays replaced wholesale (not element-merged).
4. Resulting object is what `ValidateActionDataAsync` runs against and what is sealed to the register on success.

**Concurrency:** Instance updates are already serialized through the store's existing concurrency mechanism (optimistic version token). Adding `PendingActionPayloads` does not introduce new concurrency surface.

---

### 3. RoutingResult.PendingPayloads (transient, engine-internal)

Transient carrier from the engine's `RoutingEngine` to `ActionExecutionService`. Not persisted; exists only during a single action execute call.

**Location:** `src/Core/Sorcha.Blueprint.Engine/Models/RoutingResult.cs`

**Shape:**

```csharp
public record RoutingResult(
    IReadOnlyList<int> NextActions,
    // ... existing fields ...

    /// <summary>
    /// Per-next-action prepopulated payloads derived from the matched route's
    /// <see cref="Route.OutputMapping"/>. Null when the matched route declares
    /// no mapping. Key is next action ID, value is the payload object to seed
    /// into <see cref="Instance.PendingActionPayloads"/>.
    /// </summary>
    IReadOnlyDictionary<int, JsonObject>? PendingPayloads
);
```

---

## Wave 14b — Credential claim feature

### 4. x-credential-offer schema extension (new)

A JSON Schema vendor extension marker indicating that an object field should be rendered as a credential claim card rather than a generic form input. Follows the existing pattern used by `x-page`, `x-section`, `x-persona`, and `x-file`.

**Applied to:** An object-typed field in a `DataSchema` on a blueprint action.

**Value:** `true` (or absent for non-claim fields).

**Schema example (a credential claim action's data schema):**

```json
{
  "type": "object",
  "properties": {
    "credentialOffer": {
      "type": "object",
      "x-credential-offer": true,
      "x-section": "credential",
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
    },
    "claimed_at": {
      "type": "string",
      "format": "date-time",
      "description": "Set by the client when the citizen clicks Claim"
    }
  },
  "required": ["credentialOffer"]
}
```

**Rendering contract:**
- `SorchaFormRenderer` detects `x-credential-offer: true` on any property and replaces the default object editor with `CredentialClaimCard.razor` passing the object value.
- The renderer does NOT run client-side form validation on fields inside `x-credential-offer` — they are read-only.
- The renderer's submit path calls the `OnSubmit` callback with `{ "claimed_at": "..." }` after a successful local claim (or `OnReject` after Decline).
- Engine-side validation happens on the merged payload per research decision 4; the merged payload includes the `credentialOffer` object from the seed.

---

### 5. CredentialOfferPayload (runtime shape)

The payload carried on a credential claim action. This is not a new C# type — it is the shape of the JSON that the engine seeds into `Instance.PendingActionPayloads[actionId]` via `OutputMapping`, and that `CredentialClaimCard` consumes.

**Shape (TypeScript-flavoured for clarity):**

```ts
{
  credentialOffer: {
    credential_offer_uri: string;   // REQUIRED — OpenID4VCI offer URI
    display?: {                      // OPTIONAL — DIF Credential Manifest descriptor
      title?: string;
      subtitle?: string;
      description?: string;
      issuer?: {
        name?: string;
        logo?: { uri?: string; alt?: string };
      };
    };
    expires_at?: string;             // OPTIONAL — ISO 8601 offer expiry
  };
  claimed_at?: string;               // Set by client on successful claim
}
```

**State transitions (from the citizen's perspective):**

```
         ┌──────────────────────────────────────────────────┐
         │ Action 2 pending with credentialOffer seeded     │
         └──────────────────────────────────────────────────┘
                             │
    ┌────────────────────────┼────────────────────────┬─────────────────┐
    │                        │                        │                 │
    ▼                        ▼                        ▼                 ▼
[Claim locally]        [Show QR]              [Decline]          [Expiry reached]
    │                        │                        │                 │
    │                        ▼                        │                 │
    │            [External wallet exchanges]          │                 │
    │                        │                        │                 │
    │              ┌─────────┴─────────┐              │                 │
    │              │ status = Exchanged │              │                 │
    │              └─────────┬─────────┘              │                 │
    │                        │                        │                 │
    ▼                        ▼                        ▼                 ▼
Local receive          Auto-submit               RejectionConfig    Client-side
succeeds               action with              IsTerminal=true     expire endpoint
    │                  claimed_at                     │                 │
    ▼                        │                        ▼                 ▼
Submit action               ▼                   Instance state        Action state
with claimed_at        Action sealed             → Rejected           → Failed
    │                        │                        │                 │
    └────────────────────────┴────────────────────────┴─────────────────┘
                             │
                             ▼
         PendingActionPayloads[actionId] removed
```

---

## Entities NOT changed by this feature

The following entities are referenced but not modified by wave 14, documented here to make the additive nature explicit:

- **`Action`** — no new fields. `Action.RejectionConfig` (existing) is used by claim actions to enable the Decline path.
- **`Participant`** — no new fields. Open participant late-binding (existing) provides the citizen's wallet address for sender-locking the claim action.
- **`Blueprint`** — no new top-level fields. Blueprint version is incremented by wave 14b's updates to `verified-citizen-v2.json` and `haip-driving-licence.json`.
- **`InstanceState`** enum — no new values. `Rejected` (existing) used on Decline; `Failed` (existing) used on expiry.
- **`TransactionModel`** — no new fields. The claim confirmation transaction is a normal action execution transaction with `claimed_at` in the payload.

---

## Validation rules summary

| Rule | Phase | Target | Implementation |
|------|-------|--------|----------------|
| `Route.OutputMapping` keys/values must be valid JSON Pointers (RFC 6901) | Runtime (routing eval) | Blueprint | New `JsonPointer.TryParse` check in `RoutingEngine`; bad pointers fail the route evaluation with a descriptive error |
| `Route.OutputMapping` target paths must reference schema fields on at least one next action | Publish time | Blueprint | New validation check `VAL_BP_011` in `BlueprintValidator` |
| `x-credential-offer` may only appear on object-typed schema fields | Publish time | Blueprint | New validation check `VAL_BP_012` in `BlueprintValidator` |
| `x-credential-offer` objects should declare `credential_offer_uri` as required | Publish time (warning) | Blueprint | New publish warning `WARN_BP_006` (non-blocking) |
| Submitted payload merge with seed preserves seed fields not overridden by submission | Runtime | Action execution | `ActionExecutionService.SubmitActionExecuteAsync` merge logic |
| Seed is removed on action resolution | Runtime | Action execution | `ActionExecutionService` clears `PendingActionPayloads[actionId]` atomically with state update |
| Client-side expiry transition requires action to have an `x-credential-offer` field with `expires_at` set | Endpoint-side | API | New `claim-expired` endpoint validates action shape before transitioning state |

---

## Ready for Phase 1 contracts

Data model is complete and additive. No breaking changes. Next: generate API contracts for the new endpoint (claim expire) and the updated action submission flow.
