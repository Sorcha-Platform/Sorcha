---
name: blueprint-builder
description: |
  Creates and maintains Sorcha blueprint JSON templates and workflow definitions.
  Use when: Building new blueprints, creating template JSON files, defining participants/actions/routes/schemas, configuring cycle detection, or troubleshooting blueprint publishing.
allowed-tools: Read, Edit, Write, Glob, Grep, Bash
---

# Blueprint Builder Skill

Sorcha blueprints define multi-participant workflows as JSON documents. Each blueprint has participants, actions (with data schemas), and routes that determine the action flow. Templates wrap blueprints with parameterization for reuse.

## Quick Start

### Minimal Blueprint (Two-Party, No Cycles)

```json
{
  "id": "my-blueprint",
  "title": "My Workflow",
  "description": "A simple two-participant workflow (min 5 chars)",
  "version": 1,
  "metadata": { "category": "demo" },
  "participants": [
    { "id": "sender", "name": "Sender", "description": "Initiates the workflow" },
    { "id": "receiver", "name": "Receiver", "description": "Receives and completes" }
  ],
  "actions": [
    {
      "id": 0,
      "title": "Submit",
      "sender": "sender",
      "isStartingAction": true,
      "dataSchemas": [
        {
          "type": "object",
          "properties": {
            "message": { "type": "string", "minLength": 1 }
          },
          "required": ["message"]
        }
      ],
      "routes": [
        { "id": "to-receiver", "nextActionIds": [1], "isDefault": true }
      ]
    },
    {
      "id": 1,
      "title": "Complete",
      "sender": "receiver",
      "dataSchemas": [
        {
          "type": "object",
          "properties": {
            "status": { "type": "string", "enum": ["accepted", "rejected"] }
          },
          "required": ["status"]
        }
      ],
      "routes": []
    }
  ]
}
```

### Cyclic Blueprint (Looping Workflow)

```json
{
  "metadata": { "hasCycles": "true" },
  "actions": [
    {
      "id": 0, "title": "Ping", "sender": "ping", "isStartingAction": true,
      "routes": [{ "id": "ping-to-pong", "nextActionIds": [1], "isDefault": true }]
    },
    {
      "id": 1, "title": "Pong", "sender": "pong",
      "routes": [{ "id": "pong-to-ping", "nextActionIds": [0], "isDefault": true }]
    }
  ]
}
```

**Cycle detection** produces warnings (not errors). Cyclic blueprints publish with `metadata["hasCycles"] = "true"`.

## Key Concepts

| Concept | Details |
|---------|---------|
| Participants | Min 2 required. Each has `id`, `name`. `id` is referenced by `action.sender`. **Leave `walletAddress` null for citizen-facing or credential-bootstrapped roles** — see Open Participants below. |
| Actions | Sequential IDs starting at 0. One must have `isStartingAction: true`. Starting actions are **open by design** — anyone may submit; the first sender is bound to the participant for the rest of the instance. |
| Routes | Define flow between actions. `nextActionIds: []` = workflow completion |
| DataSchemas | JSON Schema for action payload. `IEnumerable<JsonDocument>` in C# |
| Conditions | JSON Logic expressions for conditional routing |
| Calculations | JSON Logic for computed values (e.g., `requiresApproval`) |
| Cycles | Allowed with warning. Set `metadata.hasCycles = "true"` |

## Open Participants & Late Binding

`isStartingAction: true` already encodes "open" semantics end-to-end. There is **no separate `openSubmission` flag and no `bindingPolicy` block** — `IsStartingAction` is the open flag, and `credentialRequirements` is the gate. Use these correctly and the runtime does the rest.

### What the runtime already does for starting actions

| Stage | Behaviour | Source |
|---|---|---|
| Validator (chain) | Starting actions accept any wallet — strict participant check is skipped | `ValidationEngine.cs:1027` |
| Submission gate | Starting actions are exempt from the "must be a current action" check | `ActionExecutionService.cs:184` |
| Chain anchor | A starting action with no prior tx auto-chains from the blueprint publish tx (each instance forks the blueprint) | `ActionExecutionService.cs:297` |
| Late binding | First sender's wallet is bound to the participant role on the Instance and persisted; **immutable thereafter** (re-bind throws) | `ActionExecutionService.cs:309-332` |
| Credential gate | If `credentialRequirements` are present, they are enforced before binding (HAIP external presentation or internal Sorcha verifier) | `ActionExecutionService.cs:218-269` |

### Author rules

1. **Participants targeted by a starting action MUST have `walletAddress` null** in the published blueprint. Do not pre-fill the wallet at publish time. The strict-equality check at `ActionExecutionService.cs:196-216` only fires when `walletAddress` is set, so a baked-in wallet *defeats* late binding and rejects every real submitter.
2. **All other participants** (case officers, assessors, internal roles) should have a known `walletAddress` at publish time — they are not open.
3. **Credential-bootstrapped flows** (e.g. "Driving Licence" requires a `VerifiedCitizenCredential` to start) belong on the starting action's `credentialRequirements`, not on a new flag. The runtime gates the open submission on credential possession before binding the participant.
4. **Once bound, the binding is canonical for that instance.** Subsequent actions resolve disclosures, recipients, and credential issuance targets via `instance.ParticipantWallets[participantId]`, not via the blueprint's null wallet.

### Open citizen application (Verified Citizen pattern)

```jsonc
{
  "participants": [
    { "id": "citizen", "name": "Citizen", "organisation": "Public" }       // walletAddress OMITTED
    { "id": "assessor", "name": "Government Assessor", "walletAddress": "ws1..." }
  ],
  "actions": [
    {
      "id": 1,
      "isStartingAction": true,        // open — anyone with a wallet can submit
      "sender": "citizen",              // participant resolved by late binding
      "dataSchemas": [ /* personal details */ ],
      "routes": [{ "id": "to-review", "nextActionIds": [2], "isDefault": true }]
    },
    {
      "id": 2,
      "sender": "assessor",             // pre-bound
      "requiredPriorActions": [1],
      "credentialIssuanceConfig": { "recipientParticipantId": "citizen", ... }
    }
  ]
}
```

### Credential-bootstrapped application (Driving Licence pattern)

```jsonc
{
  "participants": [
    { "id": "applicant", "name": "Applicant", "organisation": "Public" }   // walletAddress OMITTED
    { "id": "council",   "name": "Council Officer", "walletAddress": "ws1..." }
  ],
  "actions": [
    {
      "id": 1,
      "isStartingAction": true,
      "sender": "applicant",
      "credentialRequirements": [
        {
          "type": "VerifiedCitizenCredential",
          "presentationSource": "HaipExternalWallet",
          "requiredClaims": [ { "claimName": "givenName" }, { "claimName": "dateOfBirth" } ]
        }
      ],
      "dataSchemas": [ /* licence-specific fields only — identity comes from the credential */ ]
    }
  ]
}
```

The applicant doesn't authenticate as a pre-existing identity; they prove they hold a VerifiedCitizenCredential and *that* fact binds them as the applicant. The HAIP presentation pipeline runs before the late-bind block.

### Common foot-guns

- **Don't pre-bind the citizen wallet in walkthroughs.** A `walletMap[citizen] = someWallet` at publish time will lock the participant to that single wallet and reject every real public submitter with `"Wallet X is not authorized to execute action 1. This action requires participant 'citizen' with wallet 'Y'."` Strip the open participants out of your wallet map.
- **Don't rely on starting-action open semantics for sensitive roles.** If the starting participant should be restricted, *either* set their `walletAddress` (closed) *or* attach `credentialRequirements` (gated). Open + no requirements = anyone with a JWT can become that participant.
- **Re-binding is immutable.** Once `instance.ParticipantWallets[citizen]` is set, attempting to submit again from a different wallet throws. If a workflow needs an applicant to "swap identity", that is a new instance.

## Reusable Schema Components (Sorcha core library)

> **Status:** Catalog and resolver land with the Verified Citizen v2 PR. This section is authoritative direction — once the PR ships, blueprints SHOULD prefer `$ref` to a core component over inlining identity primitives. Design spec: `docs/superpowers/specs/2026-04-13-verified-citizen-v2-design.md`.

### Why

Identity primitives (a person's name, date of birth, email, postal address) appear in every citizen-facing blueprint. Inlining the JSON Schema for them in each blueprint duplicates validation, layout, persona bindings, and address-lookup behaviour, and means each blueprint reinvents the form UX. The core library publishes them once with a stable URI and lets every blueprint `$ref` them.

### Composition

Use standard JSON Schema `$ref` with an HTTPS `$id`:

```jsonc
"properties": {
  "name":    { "$ref": "https://schemas.sorcha.dev/core/PersonName/v1" },
  "dob":     { "$ref": "https://schemas.sorcha.dev/core/DateOfBirth/v1" },
  "email":   { "$ref": "https://schemas.sorcha.dev/core/EmailAddress/v1" },
  "address": { "$ref": "https://schemas.sorcha.dev/core/PostalAddress/v1" }
}
```

The validator pipeline flattens `$ref`s at resolve time — by the time the renderer or validator sees the schema, the referenced component's properties and layout have been inlined. The same `$id` later resolves to a `did:sorcha:register:.../schemas/core/...` once register publication ships; both forms are URIs, only the resolver changes.

### Layout transclusion with override

A component carries its own `x-pages`, `x-sections`, `x-introduction`, `x-width`. By default these transclude into the consuming schema at the point of `$ref`. The consuming blueprint can override layout by declaring extensions as siblings to the `$ref` (JSON Schema 2020-12 allows siblings to `$ref`).

**Merge rule:**
- **Child wins** for `x-pages` / `x-sections` / `x-introduction` / `x-width`
- **Component wins** for `properties` / `required` / `type` (cannot be overridden inline — that would defeat reuse)

Default usage (component's own layout):
```jsonc
"address": { "$ref": "https://schemas.sorcha.dev/core/PostalAddress/v1" }
```

Override with a compact one-row layout:
```jsonc
"address": {
  "$ref": "https://schemas.sorcha.dev/core/PostalAddress/v1",
  "x-sections": [
    { "title": "Address", "layout": "horizontal", "fields": ["line1", "town", "postcode", "country"] }
  ]
}
```

### Initial component catalog

| `$id` | Properties | Notes |
|---|---|---|
| `https://schemas.sorcha.dev/core/PersonName/v1` | `givenName`, `middleName?`, `familyName`, `fullName?` | Renderer auto-derives `fullName` when omitted. |
| `https://schemas.sorcha.dev/core/DateOfBirth/v1` | `dateOfBirth: { format: date, formatMaximum: "today" }` | DoB must be in the past. |
| `https://schemas.sorcha.dev/core/EmailAddress/v1` | `email: { format: email }` | Single email. |
| `https://schemas.sorcha.dev/core/EmailAddressList/v1` | `emails: array of {email, isDefault}` | Min 1, max 5; exactly one default. |
| `https://schemas.sorcha.dev/core/PostalAddress/v1` | `line1`, `line2?`, `town`, `region?`, `postcode`, `country` | Carries `x-address-lookup: true` on `postcode`; renderer dispatches a postcode lookup control when an address-lookup provider is configured. |

### Persona bindings — declarative, not heuristic

Components declare persona bindings explicitly via `x-persona` on each property:

```jsonc
"properties": {
  "line1":    { "type": "string", "x-persona": "address.line1" },
  "town":     { "type": "string", "x-persona": "address.town" },
  "postcode": { "type": "string", "x-persona": "address.postcode" }
}
```

`PersonaAutofillResolver` reads explicit `x-persona` first; falls back to name-heuristic matching for legacy blueprints that don't declare bindings. Prefer the explicit form for any new schema — it's more precise, survives field renames, and self-documents the autofill contract.

### Date constraints — standard `formatMinimum` / `formatMaximum` with tokens

Use the JSON Schema 2020-12 standard `formatMinimum` / `formatMaximum` keywords with a small Sorcha token vocabulary:

| Token | Meaning |
|---|---|
| `today` | Current date in the user's timezone |
| `today+{N}{D|M|Y}` | N days/months/years from today |
| `today-{N}{D|M|Y}` | N days/months/years before today |

Examples:
- `DateOfBirth` — `formatMaximum: "today"` (must be in the past)
- `AppointmentDate` — `formatMinimum: "today"` (must be in the future)
- `AgeGate18` — `formatMaximum: "today-18Y"` (must be at least 18)

A single helper substitutes tokens at evaluation time. The same component shape powers past-only, future-only, and age-gated date fields.

### Don't reinvent these in your blueprint

If your blueprint asks the user for a name, date of birth, email, or postal address, **`$ref` the core component** instead of inlining the JSON Schema. You get validation, layout, persona autofill, and (for postcode) address lookup for free, and your blueprint stays short and focused on the *novel* fields it actually owns.

## Blueprint Validation Rules

1. **Participant references**: Every `action.sender` must reference a valid `participant.id`
2. **Action count**: At least 1 action required
3. **Participant count**: At least 2 participants required (enforced by `BlueprintBuilder.Build()`)
4. **Description length**: Min 5 characters
5. **Title length**: Min 3 characters
6. **Cycles**: Detected but allowed — produce warnings, not errors

## Route Types

### Default Route (Always Taken)
```json
{ "id": "always", "nextActionIds": [1], "isDefault": true }
```

### Conditional Route (JSON Logic)
```json
{
  "id": "approve-route",
  "nextActionIds": [2],
  "condition": { "==": [{ "var": "decision" }, "approved"] }
}
```

### Terminal Route (Workflow Ends)
```json
{ "id": "complete", "nextActionIds": [], "isDefault": true }
```

### Parallel Branch (Multiple Next Actions)
```json
{
  "id": "parallel-review",
  "nextActionIds": [2, 3],
  "isDefault": true,
  "branchDeadline": "P7D"
}
```

## Route Precedence

Route-based routing (via `Action.Routes`) takes precedence over legacy condition-based routing (via `Action.Participants`). Always use `routes` for new blueprints.

## DataSchema Patterns

### String Field with Validation
```json
{ "type": "string", "minLength": 1, "maxLength": 500, "title": "Message" }
```

### Integer with Minimum
```json
{ "type": "integer", "minimum": 1, "title": "Counter" }
```

### Enum (Fixed Choices)
```json
{ "type": "string", "enum": ["approved", "rejected", "escalate"], "title": "Decision" }
```

### Number with Threshold
```json
{ "type": "number", "minimum": 0, "title": "Amount" }
```

## Template Wrapper

Templates wrap blueprints for reuse with optional parameterization:

```json
{
  "id": "template-id",
  "title": "Template Title",
  "description": "What this template does (min 5 chars)",
  "version": 1,
  "category": "demo|approval|finance|supply-chain",
  "tags": ["tag1", "tag2"],
  "author": "Sorcha Team",
  "published": true,
  "template": { /* raw blueprint JSON or JSON-e template */ },
  "parameterSchema": null,
  "defaultParameters": null,
  "examples": []
}
```

### Fixed Template (No Parameters)
Set `parameterSchema: null` — the `template` field contains the raw blueprint JSON directly. Used for simple blueprints like Ping-Pong.

### Parameterized Template (JSON-e)
Uses JSON-e expressions (`$eval`, `$if`, `$flattenDeep`) in the `template` field. Requires `parameterSchema` (JSON Schema), `defaultParameters`, and `examples`.

**JSON-e expressions:**
- `{ "$eval": "paramName" }` — substitute parameter value
- `{ "$if": "condition", "then": ..., "else": ... }` — conditional inclusion
- `{ "$flattenDeep": [...] }` — flatten nested arrays (for conditional participants/actions)

## Blueprint Publishing Flow

1. `POST /api/blueprints/` — Create draft blueprint
2. `POST /api/blueprints/{id}/publish` — Publish (validates, returns warnings for cycles)
3. `POST /api/instances/` — Create instance with participant wallet mappings

### Publish Response (with cycle warning)
```json
{
  "blueprintId": "...",
  "version": 1,
  "publishedAt": "...",
  "warnings": ["Cyclic route detected: action 0 → action 1 → action 0. This blueprint will loop indefinitely unless routing conditions provide a termination path."]
}
```

## Action Execution

```
POST /api/instances/{id}/actions/{actionId}/execute
Headers: Authorization: Bearer <token>, X-Delegation-Token: <token>
Body: {
  "blueprintId": "string",
  "actionId": "string",
  "instanceId": "string",
  "senderWallet": "string",
  "registerAddress": "string",
  "payloadData": { "message": "hello", "counter": 1 }
}
```

Engine pipeline: **validate** (schema check) → **calculate** (JSON Logic) → **route** (determine next) → **disclose** (visibility rules)

## Common Patterns

### Approval Chain (Linear)
```
Submit(requester) → Review(manager) → Approve(director) → Complete
```

### Ping-Pong (Cyclic)
```
Ping(A) → Pong(B) → Ping(A) → Pong(B) → ... (indefinite)
```

### Conditional Branching
```
Submit → [amount > 10000] → Director Approval
Submit → [amount <= 10000] → Manager Approval
Both → Complete
```

## File Locations

| File | Purpose |
|------|---------|
| `examples/templates/*.json` | Built-in template JSON files |
| `src/Common/Sorcha.Blueprint.Models/` | Blueprint, Action, Route, Participant models |
| `src/Common/Sorcha.Blueprint.Models/BlueprintTemplate.cs` | Template model |
| `src/Core/Sorcha.Blueprint.Engine/` | Execution engine (validate/calculate/route/disclose) |
| `src/Core/Sorcha.Blueprint.Fluent/` | Fluent API for programmatic blueprint creation |
| `src/Services/Sorcha.Blueprint.Service/Program.cs` | PublishService, ValidateBlueprint, DetectCycles |
| `src/Services/Sorcha.Blueprint.Service/Templates/TemplateSeedingService.cs` | Startup seeding |

## See Also

- [patterns](references/patterns.md) - Blueprint design patterns and examples
- [workflows](references/workflows.md) - Publishing and execution workflows

## Related Skills

- **dotnet** - .NET 10 / C# 13 patterns
- **minimal-apis** - Blueprint Service endpoint definitions
- **xunit** - Testing blueprint validation
- **blazor** - Template library UI pages
