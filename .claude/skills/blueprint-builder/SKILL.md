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
| Participants | **Min 2 required** — publish fails with `Blueprint must have at least 2 participants`. Each has `id`, `name`. `id` is referenced by `action.sender`. A single-action blueprint (e.g. a credential gate) still needs a second participant: give it a **disclosure and no action of its own**, which satisfies the rule without adding a cadence step to every submission. **Leave `walletAddress` null for citizen-facing or credential-bootstrapped roles** — see Open Participants below. |
| Actions | Sequential IDs starting at 0. One must have `isStartingAction: true`. Starting actions are **open by design** — anyone may submit; the first sender is bound to the participant for the rest of the instance. |
| Routes | Define flow between actions. `nextActionIds: []` = workflow completion |
| DataSchemas | JSON Schema for action payload. `IEnumerable<JsonDocument>` in C# |
| Conditions | JSON Logic expressions for conditional routing |
| Calculations | JSON Logic for computed values (e.g., `requiresApproval`) |
| Cycles | Allowed with warning. Set `metadata.hasCycles = "true"` |
| InstanceReference | Optional human-readable instance id (e.g. `CP-RIV-14-A7K3`) generated from the starting action's payload. See below. |

## Instance Reference

Define `instanceReference` to give each workflow instance a human-readable id instead of a bare GUID:

```jsonc
"instanceReference": {
  "prefix": "CP",
  "components": [
    { "field": "/projectName", "transform": "FirstWord", "chars": 3 },
    { "field": "/siteAddress", "transform": "FirstWord", "chars": 3 }
  ]
}
```

- `prefix` — 1–5 uppercase alpha chars identifying the workflow type.
- `components` — 1–5 field extractions from the **starting action's** schema.
- `transform` — `FirstWord` (split on space, take first) or `Truncate` (first N chars). Output is uppercased.
- A 4-char uniqueness hash is appended automatically.

> ⚠ **The reference is public metadata.** Any field value you reference here is visible **in plaintext** on the instance, outside the encrypted disclosure groups. Never build one from a name, date of birth, or any other identifying value — pick a project/site/case field.

## Open Participants & Late Binding

`isStartingAction: true` already encodes "open" semantics end-to-end. There is **no separate `openSubmission` flag and no `bindingPolicy` block** — `IsStartingAction` is the open flag, and `credentialRequirements` is the gate. Use these correctly and the runtime does the rest.

### What the runtime already does for starting actions

| Stage | Behaviour | Source |
|---|---|---|
| Validator (chain) | Starting actions accept any wallet — strict participant check is skipped | `ValidationEngine.cs:~1352` (`IsStartingAction` branch) |
| Submission gate | Starting actions are exempt from the "must be a current action" check | `ActionExecutionService.cs:~209` |
| Chain anchor | A starting action with no prior tx auto-chains from the blueprint publish tx (each instance forks the blueprint) | `ActionExecutionService.cs:~375` |
| Late binding | First sender's wallet is bound to the participant role on the Instance and persisted; **immutable thereafter** (re-bind throws) | `ActionExecutionService.cs:~419-452` |
| Credential gate | If `credentialRequirements` are present, they are enforced before binding (HAIP external presentation or internal Sorcha verifier) | `ActionExecutionService.cs:~253-269` |

> Line numbers are indicative (verified 2026-06); they drift — grep the method bodies if a citation misses.

### Author rules

1. **Participants targeted by a starting action MUST have `walletAddress` null** in the published blueprint. Do not pre-fill the wallet at publish time. The strict-equality check (`ActionExecutionService.cs:~236-248`) only fires when `walletAddress` is set, so a baked-in wallet *defeats* late binding and rejects every real submitter.
2. **All other participants** (case officers, assessors, internal roles) should have a known `walletAddress` at publish time — they are not open.
3. **Credential-bootstrapped flows** (e.g. "Driving Licence" requires a `AssuredIdentityCredential` to start) belong on the starting action's `credentialRequirements`, not on a new flag. The runtime gates the open submission on credential possession before binding the participant.
4. **Once bound, the binding is canonical for that instance.** Subsequent actions resolve disclosures, recipients, and credential issuance targets via `instance.ParticipantWallets[participantId]`, not via the blueprint's null wallet.

### Open citizen application (Assured Identity Phase 1 pattern)

```jsonc
{
  "participants": [
    { "id": "citizen", "name": "Citizen", "organisation": "Public" }       // walletAddress OMITTED
    { "id": "analyst", "name": "Verification Analyst", "walletAddress": "ws1..." }
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

### Credential-bootstrapped application (Driving Licence pattern — Assured Identity Phase 2)

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
          "type": "https://sorcha.dev/vc/assured-identity/v1",
          "presentationSource": "HaipExternalWallet",
          "requiredClaims": [ { "claimName": "givenName" }, { "claimName": "dateOfBirth" } ]
        }
      ],
      "dataSchemas": [ /* licence-specific fields only — identity comes from the credential */ ]
    }
  ]
}
```

The applicant doesn't authenticate as a pre-existing identity; they prove they hold an AssuredIdentityCredential and *that* fact binds them as the applicant. The HAIP presentation pipeline runs before the late-bind block.

### Common foot-guns

- **Don't pre-bind the citizen wallet in walkthroughs.** A `walletMap[citizen] = someWallet` at publish time will lock the participant to that single wallet and reject every real public submitter with `"Wallet X is not authorized to execute action 1. This action requires participant 'citizen' with wallet 'Y'."` Strip the open participants out of your wallet map.
- **Don't rely on starting-action open semantics for sensitive roles.** If the starting participant should be restricted, *either* set their `walletAddress` (closed) *or* attach `credentialRequirements` (gated). Open + no requirements = anyone with a JWT can become that participant.
- **Re-binding is immutable.** Once `instance.ParticipantWallets[citizen]` is set, attempting to submit again from a different wallet throws. If a workflow needs an applicant to "swap identity", that is a new instance.

## Reusable Schema Components (Sorcha core library)

> **Status: Shipped (Feature 103).** The resolver (`SchemaRefResolver.cs`, prefix `https://schemas.sorcha.dev/core/`, child-wins layout override) and the startup seeder (`CoreSchemaSeedService.cs`) are live; the five catalog primitives exist on disk under `blueprints/schemas/sorcha-core/*.json`; and `PublishService.PublishAsync` flattens `$ref`s (`FlattenActionSchemas`) before validation, surfacing `SchemaRefResolutionException` as a publish error. Blueprints SHOULD prefer `$ref` to a core component over inlining identity primitives. Design spec: `docs/superpowers/specs/2026-04-13-verified-citizen-v2-design.md`.

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

## Blueprint Validation Codes

Publish-time validation runs in **`Sorcha.Blueprint.Service`** (`PublishService.ValidateBlueprint`) — **not** `Sorcha.Validator.Service` (that service does transaction-chain validation, the `VAL_*` runtime codes). Errors block publication; warnings publish but surface in the response.

> **Two validation surfaces, one table.** The coded errors below are emitted in full by the AI-chat `validate_blueprint` tool (`BlueprintToolExecutor`). The HTTP `/publish` path enforces the structural rules (`VAL_BP_010/011/012`, `WARN_BP_006`, recipient + cycle checks, plus the credential codes `VAL_BP_CRED_001/003`) but emits some as plain-text messages, and currently does **not** enforce `INVALID_TITLE` / `INVALID_DESCRIPTION` (title/description length) — those fire only in the chat tool. Reconciling the two surfaces is a tracked follow-up; until then treat the chat tool as the stricter gate.

| Code | Severity | Trigger |
|------|----------|---------|
| `MIN_PARTICIPANTS` | error | Fewer than 2 participants |
| `MIN_ACTIONS` | error | Zero actions |
| `INVALID_TITLE` | error | Title missing or `<3` chars |
| `INVALID_DESCRIPTION` | error | Description missing or `<5` chars |
| `INVALID_PARTICIPANT_REF` | error | An action's sender (or routing target) does not match any `participant.id` |
| `VAL_BP_010` | error | Starting action's `sender` participant has a non-null `walletAddress` — defeats open submission |
| `VAL_BP_011` | error | An `outputMapping` target pointer's top-level field is not declared on any next action's schema |
| `VAL_BP_012` | error | `x-credential-offer: true` on a non-object field |
| `VAL_BP_CRED_003` | error | An action reachable from a `SorchaLocalWallet` issuing action via its routes is not terminal. The claim/decline card must end the workflow. |
| `VAL_BP_CRED_004` | error | A declared `vct` is not an absolute URI (SD-JWT VC requires a URI). Emitted only by the publish path. |
| `INVALID_CREDENTIAL_RECIPIENT` | warning | `credentialIssuanceConfig.recipientParticipantId` references an unknown participant |
| `OPEN_CREDENTIAL_ISSUER` | warning | `credentialRequirements[].trustPolicy` is null or has no `sources` (any issuer accepted) — usually too permissive. (Pre-F135 this keyed off an empty `acceptedIssuers`, now removed.) |
| `WARN_BP_CRED_005` | warning | An action declares `credentialIssuanceConfig` with **no `issuanceCondition`** but routes on a decision (a conditional route, or >1 route). Minting precedes routing, so the credential is minted and delivered on the reject path too (#1551). |
| `NO_DISCLOSABLE_SET` | warning | A `credentialIssuanceConfig` has claim mappings but no `disclosable` list. A null set is **expanded to every claim name** at signing time — it does not mean "none" (#1550). Emitted by the chat `validate_blueprint`. |
| `WARN_BP_006` | warning | An `x-credential-offer` object should declare `credential_offer_uri` in its `required` list |
| `NO_ROUTING_DEFINED` | error | A multi-action blueprint declares **no routing of any kind** — no `routes` and no legacy `participants` conditions — so nothing can advance past the starting action. |
| `DUPLICATE_PARTICIPANT_ID` | error | Two participants share an `id`. `action.sender` resolves by id, so a duplicate cannot be disambiguated (#1548). |
| `STARTING_ACTION_NO_ROUTES` | error | A starting action declares no `routes` while the blueprint has other actions — the workflow can never advance past it (#1548). |
| `UNREACHABLE_ACTION` | error | An action is not reachable from any starting action by `routes` or `rejectionConfig.targetActionId` (#1548). |
| `NO_TERMINAL_PATH` | warning | No action ends the workflow: every action routes onward and none declares an empty `nextActionIds`. Set `metadata.hasCycles = "true"` if the loop is intentional (#1548). |
| `NO_STARTING_ACTION` | warning | No action marked `isStartingAction: true` |
| Cycle warning | warning | Cyclic route detected — publish proceeds; set `metadata.hasCycles = "true"` for clarity |

> ⚠ **`NO_ROUTING_DEFINED` is checked FIRST and is not subject to the gate below.** A live designer
> run produced a multi-action blueprint with **no routing at all**; the gate skipped every check, the
> validator reported "no errors, no warnings", and it then **published** — the publish path reports
> unreachability only as a warning. Corpus-testing the rule against 45 shipped blueprints could not
> have caught it, because none had that shape.
>
> ⚠ **The four reachability checks are gated on the blueprint using route-based routing at all** —
> i.e. at least one action declares a non-empty `routes` array. Legacy and platform-driven blueprints
> (`complex-sme-invoice-finance`, `register-governance-v1`) declare no routes on any action and are
> advanced by other means; flagging them would be a false positive. Verified against all 45 shipped
> blueprints — 0 flagged — while still catching the designer's real defect (a *partially* routed
> blueprint: starting action with `routes: null`, everything else looping back to it).

**Runtime issuance codes** (these fail a *submission*, not a publish — they surface as an `InvalidOperationException` from `ActionExecutionService` and are deliberately re-thrown rather than swallowed):

| Code | Trigger |
|------|---------|
| `VAL_RUNTIME_CRED_002` | The `SorchaLocalWallet` mint failed or returned null — check Wallet Service logs. |
| `VAL_RUNTIME_CRED_004` | No delivery key for the recipient: no published participant record **and** no carried encryption key. Fails closed (FR-012 / SC-004). |
| `VAL_RUNTIME_CRED_005` | `holderKeySourceField` is configured but no holder JWK resolved from the submission, so the credential can't be bound (FR-014). |

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

### Route with OutputMapping (Payload Carry-Forward) — Feature 104

A route MAY carry data from the current action's execution result into the next action's **prepopulated payload** via `outputMapping`. This is a general-purpose primitive — any blueprint can use it to hand data off between actions without the recipient re-entering it.

```json
{
  "id": "approved-to-claim",
  "nextActionIds": [3],
  "condition": { "==": [{ "var": "verificationDecision" }, "approved"] },
  "outputMapping": {
    "/haip/credential_offer_uri": "/credentialOffer/credential_offer_uri",
    "/haip/credential_type":      "/credentialOffer/credential_type",
    "/haip/expires_at":           "/credentialOffer/expires_at"
  }
}
```

**How it works:**
- Keys are JSON Pointers into the **source document** produced by the current action's execution. Available sub-trees:
  - `/payload/*` — the submitted action payload
  - `/calculations/*` — values produced by the engine's calculate step
  - `/haip/*` — HAIP credential offer output (`credential_offer_uri`, `offer_id`, `expires_at`, `credential_type`) when the current action declared `credentialIssuanceConfig.targetAudience = HaipExternalWallet`
- Values are JSON Pointers into the **target** — the next action's prepopulated starting payload. Intermediate object nodes are created as needed.
- Absent source paths are **silently skipped** (not an error) — authors can map optional fields without adding conditionals.
- The seed is merged with the recipient's submission on execute — submitted fields win on key collision.
- Seed persists across page reloads and is cleared atomically when the action resolves (complete, reject, or expire).

**Publish-time validation:**
- `VAL_BP_011` — every target JSON Pointer's top-level field MUST exist in at least one `DataSchema` of at least one next action. Writing to fields that aren't declared on the receiving action is a publish error.
- Both source and target pointers MUST begin with `/` (RFC 6901).

## Route Precedence

Route-based routing (via `Action.Routes`) takes precedence over legacy condition-based routing (via `Action.Participants`). Always use `routes` for new blueprints.

## Calculations (JSON Logic)

Per-action computed values evaluated by the engine after schema validation, before routing. Calculations are referenced from routing conditions and `outputMapping` source paths under `/calculations/*`.

```jsonc
{
  "id": 0,
  "title": "Submit",
  "calculations": {
    "requiresApproval": { ">": [{ "var": "amount" }, 10000] },
    "isOverseas":       { "!=": [{ "var": "country" }, "GB"] }
  },
  "routes": [
    { "id": "exec",    "condition": { "var": "requiresApproval" }, "nextActionIds": [2] },
    { "id": "manager", "isDefault": true, "nextActionIds": [1] }
  ]
}
```

Key names are unrestricted; values are JSON Logic expressions referencing payload fields via `{ "var": "fieldName" }`. Calculations from prior actions listed in `requiredPriorActions` are merged into scope at routing time.

### Engine + nested field access (verified 2026-06-02)

The evaluator is **json-everything's `Json.Logic`** (`src/Core/Sorcha.Blueprint.Engine/Implementation/JsonLogicEvaluator.cs`), implementing jsonlogic.com semantics. Two consequences worth knowing when authoring gates:

- **`var` supports nested dot-paths.** The submitted payload is serialised to a `JsonNode` preserving its object nesting, so `{ "var": "mfa.adminMfaEnforced" }` resolves against a nested `{ "mfa": { "adminMfaEnforced": true } }` payload — you do **not** need to flatten the schema for the gate. Array indices work too (`{ "var": "items.0.price" }`). This is the same nested resolution that `claimMappings` `sourceField` JSON Pointers use, just expressed in JSON Logic dot-notation rather than RFC 6901.
- **Standard operators are available**, including `and`, `or`, `==`, `!=`, `>`, `>=`, `<`, `<=`, `if`, `in` (array membership / substring), and arithmetic. Compose multi-condition compliance gates directly — e.g. an issuance gate that ANDs several nested booleans and an OR'd password-policy branch:

```jsonc
"calculations": {
  "computedCompliant": {
    "and": [
      { "==": [{ "var": "mfa.adminMfaEnforced" }, true] },
      { "==": [{ "var": "offboarding.staleAccounts" }, 0] },
      { "or": [
        { "and": [{ "in": [{ "var": "passwordPolicy.approach" }, ["mfa+8","lockout+8"]] }, { ">=": [{ "var": "passwordPolicy.minLength" }, 8] }] },
        { "and": [{ "==": [{ "var": "passwordPolicy.approach" }, "denylist+12"] }, { ">=": [{ "var": "passwordPolicy.minLength" }, 12] }] }
      ] }
    ]
  }
}
```

**Gating credential issuance — use `issuanceCondition`, and know why routing is not enough.**

**Minting runs *before* routing.** An action that declares `credentialIssuanceConfig` mints
whenever that action is **reached** — a `nextActionIds: []` reject route stops the credential being
handed onward, but the credential has already been minted **and delivered** to
`recipientParticipantId`. Confirmed live (#1551) by an A/B of two blueprints differing only in
`issuanceCondition`: with it a `Fail` decision issued nothing; without it, a credential landed in
the rejected applicant's wallet. Three shipped blueprints had exactly this shape.

Two different topologies, and only one of them can be fixed by routing:

| Topology | How to withhold |
|---|---|
| The issuance config sits on the **decision action itself** (approve/reject recorded here) | **`issuanceCondition` only.** Routing cannot help — the action is reached on both paths, so the mint has already happened by the time the route is evaluated. |
| The issuance config sits on a **separate downstream action** the false path never reaches | Route-gating works, because the issuing action is genuinely never reached. `issuanceCondition` is still worth adding as defence in depth. |

```jsonc
"credentialIssuanceConfig": {
  "vct": "https://sorcha.dev/vc/contractor-certification/v1",
  "issuanceCondition": { "==": [{ "var": "decision" }, "Pass"] }   // falsy ⇒ NO mint
}
```

`issuanceCondition` (Feature 176) is JSON Logic over the **submitted action data**. Falsy ⇒ no
credential is minted and the workflow routes onward normally. It **fails closed**: a condition that
cannot be evaluated skips issuance.

**`WARN_BP_CRED_005` catches the dangerous shape at publish time** — an action with a
`credentialIssuanceConfig`, no `issuanceCondition`, and either a conditional route or more than one
route. A single unconditional route is genuinely unconditional issuance and stays quiet.

**Route on the *computed* value, not a submitter-supplied flag** — compute it in `calculations`
first, so a payload cannot claim compliance it did not earn. That integrity point is independent of
the gating question above and applies to both.

A nested-var gate flowing into a route condition is exercised end-to-end by the **CyberEssentialsUac** walkthrough (`walkthroughs/CyberEssentialsUac/ce-uac-assessment-template.json`).

## Required Prior Actions

By default, the engine reconstructs accumulated state from only the immediately preceding action. To make data from earlier actions available for routing or `outputMapping`, list them in `requiredPriorActions`:

```jsonc
{
  "id": 5,
  "requiredPriorActions": [1, 3]
}
```

The engine fetches and decrypts those transactions at execution time and merges their disclosed fields into the routing scope.

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

## Render Formats — which schema shape produces which control

You almost never name a control directly. `FormSchemaService.AutoGenerateForm` **infers** one from the property's schema, and `ControlDispatcher` renders it. This is the whole mapping — first match wins, top to bottom:

| Schema shape | Control | Notes |
|---|---|---|
| `type: object` + `format: "sorcha-holder-key"` | `HolderKey` | Read-only. Writes `/holderJwk`, `/encryptionPublicKey`, `/algorithm` under the field. See Holder & device keys below. |
| `type: object` + `format: "sorcha-device-key"` | `DeviceKey` | Read-only. Writes **this device's** signing JWK as the SD-JWT `cnf` (#1195 Phase 1). |
| `enum` present | `Selection` | Any type. **An enum always becomes `Selection`** — see the `Choice` note below. |
| `type: string` + `x-address-lookup: true` | `PostcodeLookup` | Falls back to plain text when no lookup provider is configured. |
| `format: "date"` or `"date-time"` | `DateTime` | |
| `format: "file-reference"` or `"binary"` | `File` | `file-reference` is Sorcha's attachment format (F085) and carries `x-file`; `binary` is the OpenAPI byte convention. Both route here — without one of them a file field silently renders as a text box. |
| `type: number` / `integer` | `Numeric` | |
| `type: boolean` | `Checkbox` | |
| `type: string` + `maxLength > 500` | `TextArea` | **`maxLength` is the only thing that produces a textarea.** |
| anything else | `TextLine` | Default. |
| `type: object` (no special format) | `Layout` | Recurses into child properties — this is how the core primitives render nested inputs. |

Two controls exist but are **not reachable from schema inference**: `Label` and `Choice`. They only appear if an action supplies an explicit `form` control tree instead of relying on auto-generation. If you want a multi-select, an `enum` gives you `Selection`, not `Choice`.

Every control honours `x-rule` for conditional display, and `TextLine` / `PostcodeLookup` derive keyboard hints (`autocapitalize` / `autocorrect` / `spellcheck` / `inputmode`) from the schema: a field with a `pattern`, a machine `format`, an `enum`, or `x-address-lookup` suppresses autocapitalisation, because a phone keyboard's guess corrupts a machine-checked value (issue #1278).

### Holder & device keys — required for credential delivery

If an action issues a `SorchaLocalWallet` credential to an **open participant** (a citizen with no published participant record), the recipient's delivery keys must ride the submission. Declare the field:

```jsonc
"holderKeys": {
  "type": "object",
  "title": "Your holder key",
  "format": "sorcha-holder-key"       // ← the functional trigger
}
```

…and point the issuance config at it with `holderKeySourceField` (see Credential Issuance). The renderer fills it read-only from the citizen's wallet; the citizen types nothing.

> **Omitting either side fails closed — no credential is issued.** With `holderKeySourceField` set and no resolvable holder JWK, issuance throws `VAL_RUNTIME_CRED_005`; with no delivery key resolvable from either a published participant record or the carried keys, `VAL_RUNTIME_CRED_004`. Both propagate and **fail the whole submission** rather than minting an unbound credential. That is deliberate (F137 FR-012/FR-014, SC-004).

> ⚠ `x-holder-key: { "required": true }` appears in the shipped AIAS and AssuredIdentity blueprints, copied from the F137 contract example where it is described as "optional config". **Nothing reads it** — no renderer, no validator (`SchemaValidator` strips it with the generic `x-` strip), no test. It does **not** make the field required; that comes from the schema's `required` array, and in both blueprints `holderKeys` is *not* in it. Treat it as decoration; prefer adding `holderKeys` to `required` if you want the form to block early rather than failing at issuance.

## Form UX Layout

Beyond JSON Schema's basic shape, Sorcha extends action data schemas with `x-` keywords that drive form rendering. Use these inline on dataSchemas, or rely on transclusion when `$ref`-ing a core component (see Reusable Schema Components).

### Wizard pages (`x-pages`)

Split a single Sorcha action's form into multiple wizard pages. Each page is one screen with Next/Back navigation, but **all pages submit together as one signed transaction**. A new Action is required only when the **sender** changes.

```jsonc
{
  "type": "object",
  "x-pages": [
    {
      "title": "Eligibility",
      "x-sections": [{ "title": "Check eligibility", "fields": ["propertyOwner", "workType"] }]
    },
    {
      "title": "About You",
      "x-sections": [{ "title": "Your details", "fields": ["givenName", "familyName", "dateOfBirth"] }]
    },
    {
      "title": "Check Your Answers",
      "description": "Review before submitting."
    }
  ],
  "properties": { /* ... */ }
}
```

The final page conventionally titled "Check Your Answers" cues the renderer to show a summary view rather than collecting new fields.

### Sections (`x-sections`)

Group fields under a heading on a single page. `layout: "horizontal"` arranges fields side-by-side; default is vertical.

```jsonc
"x-sections": [
  { "title": "Address", "layout": "horizontal", "fields": ["line1", "town", "postcode"] }
]
```

### Introduction (`x-introduction`) and width (`x-width`)

`x-introduction` renders Markdown copy at the top of the form (or page). `x-width` controls form max-width: `"narrow"` (480px), `"normal"` (720px, default), `"wide"` (960px), `"full"` (no max).

```jsonc
{
  "type": "object",
  "x-introduction": "Tell us about the work you plan to do. **Most applications take 5 minutes.**",
  "x-width": "narrow",
  "properties": { /* ... */ }
}
```

### Persona autofill (`x-persona`)

Bind a property to a Sorcha persona attribute (Feature 092). When the citizen has filled their profile, recognised fields auto-populate with a cream tint and a `self` provenance tick. Edit releases the autofill claim.

```jsonc
"properties": {
  "applicantEmail": { "type": "string", "format": "email", "x-persona": "defaultEmail" },
  "dateOfBirth":    { "type": "string", "format": "date", "x-persona": "dateOfBirth" }
}
```

Use `"x-persona": false` on a property whose name *would* match a heuristic but should never autofill (e.g. `nextOfKinEmail`). Without an explicit `x-persona`, the inference allowlist applies (`format: email` → defaultEmail, `format: tel` → defaultPhone, `dateOfBirth`/`dob`/`birthDate` → dateOfBirth, postal-address shape → defaultAddress).

### Postcode address lookup (`x-address-lookup`)

Set `"x-address-lookup": true` on a `postcode`-typed string field to enable the lookup control. Most blueprints get this for free by `$ref`-ing `https://schemas.sorcha.dev/core/PostalAddress/v1`.

### File uploads (`x-file`)

Mark a property as a file reference with `format: "file-reference"` and an `x-file` extension. The runtime handles transparent chunking (≤4MB chunks), per-chunk HKDF key derivation, and recipient key wrapping.

```jsonc
"sitePhoto": {
  "type": "string",
  "format": "file-reference",
  "x-file": {
    "accept": ["image/jpeg", "image/png"],
    "maxSizePerFile": "16MB",
    "maxChunks": 10,
    "capture": "user",
    "embedAs": "image-token-jpeg-240x320"
  }
}
```

`capture: "user"` requests the front-facing camera on mobile; `embedAs` triggers the client-side resizer to produce a base64 token at `{fieldPointer}/tokenImageBase64` alongside the chunked original. Full chunking/encryption pipeline lives in the **sorcha-architecture** skill — *Stored Data Transactions API*.

**`framing` (issue #1277)** — optional post-capture review overlay for portrait fields. Renders the photo the citizen just captured inside an oval + head-height guides so they can check it before submitting, with a Retake:

```jsonc
"x-file": {
  "capture": "user",
  "embedAs": "image-token-jpeg-240x320",
  "framing": { "ovalWidthPct": 62, "headTopPct": 8, "headBottomPct": 82 }
}
```

Percentages of the frame. Omit the block for the ICAO default (head ≈70–80% of frame height). Every malformed value degrades to that default — a missing block, a non-object, a string where a number belongs, an inverted band, out-of-range percentages. It is **guidance, never a gate**: nothing rejects a photo, because face geometry can't be judged reliably in a browser and a wrong rejection stops the citizen submitting at all.

> **Sizing is not your problem, but silent loss was.** The `embedAs` resizer steps JPEG quality down and then, if still oversize, steps *dimensions* down (to a 120×160 floor) before giving up. The server independently drops an oversized `portrait` claim at a ≤27,000-char base64 bound. Both surface to the citizen now; before #1277 the claim was dropped silently and the credential issued portrait-less.

### `x-slider` — bounded integer slider

A numeric property carrying an `x-slider` object renders as a slider instead of a spin box.

```jsonc
"sharedPasswordCount": {
  "type": "integer",
  "title": "How many of your accounts share a password?",
  "minimum": 0,
  "maximum": 10,
  "x-slider": { "step": 1, "minLabel": "None", "maxLabel": "10 or more" }
}
```

Range comes from the standard `minimum` / `maximum` keywords, NOT from inside `x-slider` — they
are real JSON Schema keywords, so the validator enforces the range server-side and a hand-crafted
submission cannot post an out-of-range value. `x-slider` carries only `step` and the optional
`minLabel` / `maxLabel` end captions.

Dispatch is opt-in: an integer field WITHOUT `x-slider` still renders as a numeric input.
An untouched slider submits its seeded value (the `minimum`), so the field is never absent.

**`minimum`/`maximum` are mandatory in practice, even though the inference branch does not
enforce it.** A slider field with `x-slider` but no declared range dispatches to `ControlTypes.Slider`
same as any other, but `SliderRenderer` refuses to invent a range — it renders a warning in place
of the control and leaves the field unset, rather than silently seeding and submitting a
fabricated 0–10 answer the citizen never gave. Always declare both bounds alongside `x-slider`.

### Account-derived field (`x-claim-source`) — Feature 183, **rewritten server-side by issue #1264**

Bind a form field to a named platform claim on the authenticated principal, so the value rides the wallet-signed payload **even when the field is on no page**. Headless (no control, no `x-page` placement needed) and reusable.

```jsonc
"emailVerified": {
  "type": "boolean",
  "readOnly": true,
  "x-claim-source": "email_verified"   // ← resolved SERVER-SIDE at submission
}
```

**Runtime (current): the server resolves it, not the client.** `ActionExecutionService` step 6a-bis discovers every binding via `ClaimSourceBindings.Discover(actionDef.DataSchemas)`, reads the caller's **live** values through `IPlatformUserClaimsClient`, and **overwrites whatever the client sent**.

> ⚠ **Do not reintroduce client-side seeding.** Feature 183 originally seeded these bindings in the browser from the JWT, so the value was only ever as fresh as the token the client happened to hold. A citizen's token was minted at signup with `email_verified: false`; they verified nine minutes later; the application they submitted five minutes after that was auto-rejected on the stale `false` (issue #1264, UT-001 — a **wrongful rejection**). The client-side `ClaimSourceSeeder` was deleted as a clean break. Verifying updates server state but cannot rewrite an issued token, which is why resolution has to happen at submission.

Three consequences worth authoring around:

- **The server wins.** A client-supplied value for a bound field is always discarded. Don't build UI that lets the applicant edit one.
- **It fails the submission, not the field.** If the caller carries no usable `platform_user_id`, or the claims client is unregistered, or the live read fails, the submission is **refused** — it does not fall back to the token, and it does not default a boolean to `false`. Signing `false` writes an irreversible wrongful rejection; a refused submission is retryable. (Same precedence rule as the step-8a-bis `presentedCredential` binding.)
- **It asserts platform vouching.** Declaring a binding means "the platform stands behind this value". Only use it for that — not as a convenience prefill (that's `x-persona`).

### Decision notice (`x-decision-notice`) — Feature 183, codified in Feature 184

Make an autonomous decision visible to the applicant. On a **route**, declare that taking it writes a durable bell/inbox entry (F118) to a named participant — so a rejected applicant learns WHY, across sessions and devices.

```jsonc
// on a reject route (nextActionIds: [])
"x-decision-notice": {
  "recipientParticipantId": "citizen",     // resolved via instance participant bindings
  "reasonCodeField": "/reasonCode",        // JSON Pointer to the non-sensitive CODE in the payload
  "title": "AIAS could not assure your identity",
  "severity": "Warning",                    // optional; defaults to Warning
  "reasons": {                              // code → the citizen-facing message
    "postcode-not-found": "AIAS could not locate that address on any map. …",
    "profanity":          "AIAS does not assure identities described in such… colourful terms. …",
    "email-unverified":   "AIAS needs a verified email before it can assure you. …"
  },
  "fallbackMessage": "Your application was not approved."   // unknown / absent code
}
```

**The notice fires on the RECIPIENT's node, not the decider's.** The `ReactionDispatcher` writes it as that node folds the sealed decision transaction — entitlement-gated (only the node hosting the recipient's wallet acts) and idempotent. That is the whole reason the reason is **codified**: on the applicant's node a background fold holds no delegation token and cannot decrypt the payload, so the reason must arrive as a code on the transaction's clear metadata (carried on the sender-signed `RoutingDecision`, alongside the taken route's id) and be resolved to text from the blueprint — which every node holds.

Authoring rules:
- **Declare the code field on the deciding action's `dataSchema`**, with an `enum` of the valid codes, so a typo in an agent rules file fails validation instead of silently degrading to the fallback.
- **Codes are public.** Every node holding the register can read them. Name the *class* of reason — never applicant data, never prose.
- **Citizen-facing copy lives in the blueprint** (`reasons`). The decider's own prose (e.g. `verificationNotes`) stays on the ledger as the audit record; it is not the delivery mechanism.
- Reject-only in practice: approval is already surfaced (claim action-available + credential-received). Terminal and non-terminal routes both work.
- Fail-safe throughout — a notice failure never affects sealing, routing, or the folded instance.

Full contract: `specs/184-decision-notice-decentralised/contracts/x-decision-notice-extension.md`. Runtime detail: the **sorcha-architecture** skill.

### Review summary (`x-review`)

Mark a wizard page as a read-only summary that renders as a credential id-card preview.

```jsonc
{
  "title": "Review your details",
  "x-review": {
    "layout": "id-card",
    "editable": true,
    "header": {
      "issuerName": "Acme Verification Co.",
      "credentialName": "Assured Identity",
      "colourTheme": "identity-navy"
    }
  }
}
```

Watermark states (Draft/Pending/Issued/None), stacked-cards behaviour for `credentialRequirements + credentialIssuanceConfig` actions, and portrait capture details live in the **sorcha-architecture** skill — *Cross-Cutting Pattern: Review Summary*.

## Credential Requirements & Issuance

### Requiring a credential to perform an action

```jsonc
"credentialRequirements": [
  {
    "type": "https://sorcha.dev/vc/assured-identity/v1",
    "presentationSource": "HaipExternalWallet",
    "trustPolicy": {
      "sources": [
        { "kind": "did-allowlist", "allowedIssuers": ["did:sorcha:org:ws1abc..."] }
      ]
    },
    "requiredClaims": [
      { "claimName": "givenName" },
      { "claimName": "dateOfBirth" }
    ],
    "revocationCheckPolicy": "FailClosed",
    "description": "You must be a verified citizen to start a driving licence application."
  }
]
```

- `presentationSource: "SorchaInternal"` (default) matches against the holder's on-platform Sorcha credentials.
- `presentationSource: "HaipExternalWallet"` runs the OpenID4VP `direct_post` flow (Feature 098) — required for citizen-facing services that accept external wallets and for credential-bootstrapped open submissions.
- `trustPolicy` replaced the removed `acceptedIssuers` field (F135). A null/empty `trustPolicy` accepts any issuer (`OPEN_CREDENTIAL_ISSUER` warning at publish time); a `did-allowlist` source is the direct equivalent of the old issuer list, and a `register` source trusts register-resolved issuers. Full shape: the F135 section of the `sorcha-architecture` skill. **Do not write `acceptedIssuers`** — it is silently ignored.
- Multiple requirements are AND-combined.
- `type` above is the canonical, **case-sensitive** VCT URI (`Sorcha.CitizenWallet.Abstractions.Constants.VctUris`, e.g. `VctUris.AssuredIdentityV1`) — for a platform-catalogued credential type it must match the issuer's `vct` exactly (`Ordinal` comparison). A missed URI/casing means the requirement silently never matches.

### Alternative credentials — `anyOfGroup` (Feature 181 US2)

Requirements on the same action that share an `anyOfGroup` tag are **alternatives**: presenting any
ONE of them satisfies the action. Requirements with no tag are each independently required (AND).

```jsonc
"credentialRequirements": [
  { "type": "https://sorcha.dev/vc/passport/v1",        "anyOfGroup": "identity-document" },
  { "type": "https://sorcha.dev/vc/driving-licence/v1", "anyOfGroup": "identity-document" },
  { "type": "https://sorcha.dev/vc/proof-of-address/v1" }          // no tag ⇒ ALSO required
]
```

That maps to a DCQL `credential_sets` option. Once any group exists, the mapper emits an explicit
required singleton set per ungrouped ask, so AND-requiredness survives the presence of
`credential_sets` — see `RequirementDcqlMapper`.

⚠ The F127 `SorchaWallet` consumer is still single-credential, so a multi-credential ask verifies on
the HAIP rail today.

### Issuing a credential on action completion

```jsonc
"credentialIssuanceConfig": {
  "credentialType": "PlanningPermissionCredential",
  "vct": "https://sorcha.dev/vc/planning-permission/v1",
  "displayName": "Planning Permission",
  "claimMappings": [
    { "claimName": "applicantName", "sourceField": "/applicantName" },
    { "claimName": "siteAddress",   "sourceField": "/siteAddress"   }
  ],
  "recipientParticipantId": "applicant",
  "expiryDuration": "P5Y",
  "registerId": "planning-decisions",
  "disclosable": ["applicantName", "siteAddress"],
  "usagePolicy": "Reusable",
  "targetAudience": "SorchaLocalWallet"
}
```

- `vct` (new) is the canonical, case-sensitive absolute URI written to `claims["vct"]` — the SD-JWT VC's **sole** type identifier (SD-JWT VC §3.2.2.1; Sorcha no longer emits a `type` claim). Always set it from `VctUris` for a platform-catalogued type — don't hand-write the URI.
- `displayName` (new) is the authored human card label (e.g. "Planning Permission"). When omitted, the card falls back to `Humanize(vct)` — which reads badly for a URI (`Humanize("https://.../planning-permission/v1")` → "V1"), so set it explicitly for any URI-`vct` credential.
- `credentialType` is now a short-name/fallback/readable id, written only when `vct` is omitted. It is no longer the matching identity.
- `targetAudience: "SorchaLocalWallet"` (Feature 106): the engine seals an X25519-wrapped, AEAD-encrypted SD-JWT VC into the action transaction; the credential peer-replicates and is detected by the holder's Wallet Service regardless of node. Default for on-platform issuance.
- `targetAudience: "HaipExternalWallet"` (Feature 104): mints an OpenID4VCI offer instead of writing to a wallet. **MUST be paired with a separate Claim action** carrying `x-credential-offer` and `outputMapping` from the issuing route — see Credential Claim Actions below.
- `targetAudience: "SorchaInternal"` is **deprecated** — bypasses the register and breaks on multi-node deployments. Always prefer `SorchaLocalWallet`.
- `usagePolicy: "LimitedUse"` requires `maxPresentations: <int>`.
- `expiryDuration` is ISO 8601 (`P5Y`, `P365D`, `PT24H`); omit for non-expiring credentials.

> **Designer surface (#1550).** `issue_credential` now **requires `vct`** (SD-JWT VC makes it the
> credential's only type claim, so a credential without one is unmatchable), and additionally accepts
> **`disclosable`** and **`holderKeySourceField`** — neither of which it could set before, so an
> AI-authored credential was always fully disclosable and could never be delivered to an open,
> late-bound recipient. `validate_blueprint` now also emits `WARN_BP_CRED_005` and
> `NO_DISCLOSABLE_SET`, so the author is told at authoring time rather than at Go-live.

Five further properties, all optional and all used by the shipped AIAS blueprint or its siblings:

- **`holderKeySourceField`** (Feature 137) — JSON Pointer to the parent of the recipient's carried delivery keys, written by a `sorcha-holder-key` field on a starting action (conventionally `/holderKeys/holderJwk`; the `encryptionPublicKey` + `algorithm` siblings are derived from the same parent). Set it and the issuer binds the credential to the carried holder JWK (SD-JWT `cnf`) and, for an open-participant recipient with no published participant record, wraps the on-register AEAD envelope to the carried encryption key. Resolution precedence is **published participant record → carried keys → fail closed**. Leaving it null keeps pre-137 behaviour: no `cnf` binding, no carried-key fallback. **Required in practice for cross-node / open-participant issuance.**
- **`issuanceCondition`** (Feature 176) — JSON Logic over the submitted action data, e.g. `{"==": [{"var": "decision"}, "approved"]}`. When it evaluates falsy, **no credential is minted** and the workflow routes onward normally. This is what lets a *single* decision action carry a `credentialIssuanceConfig` and still have a clean reject route — you do not need separate approve/reject actions. Fails closed: a condition that cannot be evaluated skips issuance. (Distinct from `rejectionConfig`, which is about a participant rejecting *inbound* data — see Rejection Configuration.)
- **`displayConfig`** — card presentation (colours, logo, layout) for the issued credential.
- **`format`** — credential wire format. Only `SdJwtVc` exists today; there is **no mdoc/mDL issuance**, which is the blocker for ISO 18013-5 proximity presentation.
- **`trustAnchor`** — defaults to `Register`.

⚠ **`claimMappings` fail SILENTLY, and the warning names your pointer rather than the real cause.**
A `sourceField` that resolves to nothing is dropped and the credential mints **without that claim** —
no error, no failed action, a sealed transaction and a delivered (or undeliverable) credential:

```
Claim mapping source '/subjectName' has no value in action data; dropping claim 'subjectName'
```

The obvious reading is "my JSON Pointer is wrong". It usually is not. The pointers are resolved
against the **prior action's decrypted payload**, so anything that stops that payload being decrypted
empties every mapping at once. The most common cause is that the recipients were skipped because the
participants' public keys are not on the register, which leaves the payload with no disclosure-group
envelope. Tell them apart by the *count*: one dropped claim is a pointer bug; **all of them dropped
together is a decryption or key-resolution failure upstream** — check the blueprint-service log for
`recipient skipped` before touching the schema. (Found while building `walkthroughs/CredentialLifecycle`,
2026-08-18; see the **walkthrough-builder** skill → "PUBLISH participants onto the register".)

A credential with no claims still satisfies a requirement that only checks its *type*, so a gate can
accept an empty credential. If the claims carry meaning, put them in `requiredClaims` on the
consuming action so the gate fails loudly instead.

#### SorchaLocalWallet citizen-PWA worked example (Feature 114 US4)

Composes `SorchaLocalWallet` issuance with **Open Participants & Late Binding** to deliver a credential to a walk-in citizen who has no pre-existing participant record. The applicant has `walletAddress: null`; the first authenticated submitter is late-bound to the participant for the life of the instance.

```jsonc
{
  "title": "Assured Identity (PWA delivery)",
  "participants": [
    { "id": "applicant", "walletAddress": null },
    { "id": "verifier",  "walletAddress": "ws1qta..." }
  ],
  "actions": [
    { "id": 1, "isStartingAction": true, "sender": "applicant",
      "schemaRef": "AssuredIdentityApplication/v1" },
    { "id": 2, "sender": "verifier", "schemaRef": "VerifierDecision/v1" },
    { "id": 3, "sender": "verifier",
      "credentialIssuanceConfig": {
        "credentialType": "AssuredIdentityCredential",
        "vct": "https://sorcha.dev/vc/assured-identity/v1",
        "displayName": "Assured Identity",
        "targetAudience": "SorchaLocalWallet",
        "recipientParticipantId": "applicant",
        "claimMappings": [
          { "claimName": "givenName",   "sourceField": "/1/payload/givenName" },
          { "claimName": "familyName",  "sourceField": "/1/payload/familyName" },
          { "claimName": "dateOfBirth", "sourceField": "/1/payload/dateOfBirth" }
        ],
        "disclosable": ["givenName", "familyName", "dateOfBirth"],
        "expiryDuration": "P5Y"
      } }
  ]
}
```

- The citizen applicant must be omitted from walkthrough `$walletMap` — see "Open Participants & Late Binding". `VAL_BP_010` fires at publish time if you accidentally pre-bake a wallet on a starting action's `Sender`.
- Once the credential-issuance transaction is sealed, Wallet Service's `InboundCredentialDetector` decrypts it, persists a `CredentialEntity`, and `ICitizenInboxProjector` writes a `CitizenCredentialEventLog` row + emits `WalletHub.CredentialAvailable(credentialId)` to the citizen's PlatformUser group. The PWA `Pages/Index.razor` subscribes via `CitizenWalletHubConnection` and fires `SyncService.SyncNowAsync()`. Closed-PWA delivery still works — the next `/sync` call drains the same event log.
- Stacked-card review/credential preview: layering `x-review` on the verifier's review action with `credentialRequirements` + `credentialIssuanceConfig` produces the standard stacked-cards rendering.
- Architectural detail and the projector chain live in `.claude/skills/sorcha-architecture/SKILL.md` § "Citizen Wallet PWA (Feature 114)" → "Citizen credential push (US4)".

## Rejection Configuration

Defines what happens when a participant rejects the inbound data on an action.

```jsonc
"rejectionConfig": {
  "targetActionId": 1,
  "targetParticipantId": "applicant",
  "requireReason": true,
  "isTerminal": false
}
```

- `targetActionId` — action to route to on rejection.
- `targetParticipantId` (optional) — overrides the target action's default sender; useful when bouncing back to a different participant than the one who originally submitted.
- `requireReason` (default `true`) — rejections must include a reason string.
- `isTerminal` — when `true`, rejection ends the workflow in a `Rejected` state instead of routing. Used by the credential claim card's Decline button.

If `rejectionConfig` is omitted, rejection is not allowed for the action.

## Credential Claim Actions (Feature 104 — wave 14b)

When a blueprint **issues a HAIP credential**, the credential offer must reach the **recipient**, not the issuing action sender. The correct pattern is a three-action shape:

```
Action 1: Applicant submits data        (sender: applicant — open, late-bound)
Action 2: Issuer reviews, mints offer    (sender: issuer; declares credentialIssuanceConfig)
          → route.outputMapping carries /haip/* into action 3's payload seed
Action 3: Applicant claims credential    (sender: applicant; same participant as action 1)
          → uses x-credential-offer schema extension
```

The claim action renders as a **CredentialClaimCard** in the applicant's *My Actions* queue with Claim / Scan-with-external-wallet / Decline buttons. Clicking Claim calls `HaipLocalReceiveService` to redeem the pre-authorized code against the citizen's local Sorcha wallet. Scan-QR reveals an embedded QR for external HAIP wallets. Decline seals an `InstanceState.Rejected` transaction via `RejectionConfig.IsTerminal = true`.

**Why this shape** (not the wave 13 assessor-side QR dialog):
- Cryptographic correctness: the OpenID4VCI `pre_authorized_code` is a bearer token; whoever redeems it binds the credential to *their* key via the `cnf` claim. Landing the code in the assessor's browser binds the credential to the wrong wallet. Routing it through the claim action via `outputMapping` + participant late-binding ensures only the applicant's wallet can redeem.
- Recipient-locked for free: action 3's sender is the same open participant as action 1, already late-bound to the citizen's wallet. No extra authz logic required.
- Durable and auditable: the offer persists as seeded payload state; the claim is sealed to the register as a normal action transaction with a `claimed_at` timestamp.
- Reuses existing infrastructure: My Actions queue, open-participant late-binding, rejection config — no new notification channel.

### Action 3 schema — `x-credential-offer` extension

Mark a top-level **object** field with `"x-credential-offer": true`. The UI renderer detects the extension on a pending action and swaps in the credential claim card instead of the default form. The blueprint declares the shape; the previous action's `outputMapping` seeds the values.

```json
{
  "id": 3,
  "title": "Claim your Verified Citizen credential",
  "description": "Your credential is ready. Click Claim to store it in your Sorcha wallet, or scan the QR code to load it into an external HAIP wallet.",
  "sender": "citizen",
  "requiredPriorActions": [2],
  "dataSchemas": [
    {
      "type": "object",
      "properties": {
        "credentialOffer": {
          "type": "object",
          "title": "Credential Offer",
          "x-credential-offer": true,
          "properties": {
            "credential_offer_uri": {
              "type": "string",
              "format": "uri",
              "description": "Canonical OpenID4VCI offer URI"
            },
            "credential_type": { "type": "string" },
            "expires_at":      { "type": "string", "format": "date-time" },
            "offer_id":        { "type": "string" }
          },
          "required": ["credential_offer_uri"]
        },
        "claimed_at": {
          "type": "string",
          "format": "date-time",
          "title": "Claimed at",
          "description": "Set by the client when the citizen clicks Claim"
        }
      },
      "required": ["credentialOffer"]
    }
  ],
  "rejectionConfig": {
    "targetActionId": 0,
    "isTerminal": true,
    "requireReason": false
  },
  "disclosures": [
    { "participantAddress": "citizen", "dataPointers": ["/*"] }
  ],
  "routes": [
    {
      "id": "claimed-terminal",
      "nextActionIds": [],
      "isDefault": true,
      "description": "Credential claimed — workflow complete"
    }
  ]
}
```

### Action 2 — route mapping

The issuing action (action 2) must route **conditionally** to the claim action on approval and terminate on rejection. Declare both routes and include `outputMapping` only on the approval route:

```json
"routes": [
  {
    "id": "approved-to-claim",
    "nextActionIds": [3],
    "condition": { "==": [{ "var": "verificationDecision" }, "approved"] },
    "description": "Approved — hand the minted credential to the applicant",
    "outputMapping": {
      "/haip/credential_offer_uri": "/credentialOffer/credential_offer_uri",
      "/haip/credential_type":      "/credentialOffer/credential_type",
      "/haip/expires_at":           "/credentialOffer/expires_at",
      "/haip/offer_id":             "/credentialOffer/offer_id"
    }
  },
  {
    "id": "rejected-terminal",
    "nextActionIds": [],
    "isDefault": true,
    "description": "Rejected — workflow ends with no credential issued"
  }
]
```

Action 2 still declares `credentialIssuanceConfig` with `targetAudience: HaipExternalWallet` exactly as before — the engine mints the offer pre-routing and exposes it under `/haip/*` in the routing source document so the mapping can pick it up.

### Publish-time validation for claim actions

- **VAL_BP_012** (error): `x-credential-offer: true` may only appear on **object**-typed schema fields. Scalar or array fields are rejected.
- **WARN_BP_006** (non-blocking warning): an `x-credential-offer` object should declare `credential_offer_uri` in its `required` list — the claim card cannot render without the URI, so declaring it required fails fast at publish time.

### Foot-guns

- **Don't pre-bake a wallet on the claim action's sender participant** — action 3's sender must be the same open-participant as action 1 (the applicant). If the blueprint pre-binds a wallet to that participant, the late-binding mechanism breaks and `VAL_BP_010` fires at publish time.
- **Don't forget the conditional on action 2's approval route** — if action 2 unconditionally routes to the claim action even on rejection, the citizen gets a claim card for a credential that was never approved.
- **Display strings are the blueprint author's job, not the engine's** — the engine only exposes protocol fields (`credential_offer_uri`, `credential_type`, `expires_at`, `offer_id`) under `/haip/*`. Title / subtitle / issuer name come from the action's `title`, the blueprint's participants, and the `credential_type` value. Localise them in the blueprint JSON.
- **The claim card is the whole action surface** — don't mix other form fields at the top level of action 3's schema. Keep the schema to `credentialOffer` (object, `x-credential-offer: true`) and an optional `claimed_at`. Additional fields would render as a normal form beneath the card, which is almost always wrong.

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

## Disclosure Rules

Every action MUST declare at least one `disclosure`. Each disclosure binds a participant to a list of JSON Pointer paths that participant can read on the action's payload.

```jsonc
"disclosures": [
  { "participantAddress": "applicant", "dataPointers": ["/*"] },
  { "participantAddress": "case-officer", "dataPointers": ["/applicantName", "/dateOfBirth", "/siteAddress"] },
  { "participantAddress": "public-registry", "dataPointers": ["/decision", "/issuedAt"] }
]
```

**Rules:**
- The sender of an action always needs `/*` on their own submitted data — they're the author.
- Default to **minimal disclosure**. Share only what each participant needs to act.
- Sensitive fields (NI numbers, bank details, medical data, contact info) should be restricted by default. Approvers may need a summary, not the full document.
- Use `/*` only when a participant genuinely needs to see everything.
- Field-level encryption to participant wallets (X25519 wrap + XChaCha20-Poly1305) is automatic — do not add explicit encryption config to the blueprint.

`participantAddress` accepts a participant `id` from the blueprint's `participants` list. The runtime resolves it to the participant's wallet at execution time (or to the late-bound wallet for open participants).

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
| `src/Services/Sorcha.Blueprint.Service/Services/TemplateSeedService.cs` | Startup template seeding (`CoreSchemaSeedService.cs` seeds the `$ref` core-schema catalog) |

## See Also

- [patterns](references/patterns.md) - Blueprint design patterns and examples
- [workflows](references/workflows.md) - Publishing and execution workflows

## Related Skills

- **sorcha-architecture** — Cross-cutting feature patterns: Stored Data file uploads (chunking + encryption pipeline), Review Summary id-card watermark states + portrait capture, Timebound Presentation Lifecycle (Feature 111 — Initiated/Outcome/Abandoned events), HAIP credential issuance/presentation internals, Open Participants late-binding runtime details.
- **dotnet** — .NET 10 / C# 14 patterns
- **minimal-apis** — Blueprint Service endpoint definitions
- **xunit** — Testing blueprint validation
- **blazor** — Template library UI pages
