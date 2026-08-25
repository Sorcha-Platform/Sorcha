# Sorcha Blueprint Service

**Version**: 1.0.0
**Status**: Production Ready (100% Complete)
**Framework**: .NET 10.0
**Architecture**: Microservice

---

## Overview

The **Blueprint Service** is the workflow orchestration engine of the Sorcha platform, managing the complete lifecycle of multi-participant data flow blueprints from design to execution. It coordinates selective data disclosure, conditional routing, and cryptographic transaction signing through integration with the Wallet and Register services.

This service acts as the central hub for:
- **Blueprint lifecycle management** (create, publish, version, execute)
- **Action orchestration** for multi-party workflows
- **Real-time notifications** for workflow state changes
- **Transaction coordination** with cryptographic signing and blockchain storage

### Key Features

- **Blueprint Management**: Full CRUD operations for blueprint definitions with JSON Schema validation
- **Publishing & Versioning**: Publish blueprints to specific registers with immutable version tracking
- **Action Workflows**: Submit, retrieve, validate, and reject actions with state management
- **Portable Execution Engine**: Client-side and server-side execution of JSON Logic calculations, routing rules, and disclosure policies
- **Real-time Notifications**: SignalR hub (`/hubs/blueprint`) for live action status updates with Redis backplane
- **Wallet Integration**: Automatic transaction signing and payload encryption/decryption
- **Register Integration**: Blockchain transaction storage with distributed ledger guarantees
- **File Attachments**: Upload and download support for action-related documents
- **Template System**: JSON-e based blueprint templates with parameter substitution
- **Execution Helpers**: Validation, calculation, routing, and disclosure endpoints for client applications
- **AI Chat Designer**: SignalR-based conversational blueprint builder with 13 AI tools, standardised schema library (26 schemas), and Verified Credential support

### AI Chat Designer Tools

The AI blueprint designer uses these tools through the Anthropic API:

| Tool | Purpose |
|------|---------|
| `search_schemas` | Query the standardised schema library (26 schemas across 7 categories) |
| `use_standard_schema` | Apply a schema's fields + form layout to a blueprint action |
| `search_templates` | Query the blueprint template catalogue |
| `create_blueprint` | Create a new blueprint with title and description |
| `add_participant` | Add a participant to the workflow |
| `remove_participant` | Remove a participant |
| `add_action` | Add a workflow step with data fields |
| `update_action` | Modify an existing action |
| `set_disclosure` | Configure data visibility rules |
| `add_routing` | Add conditional routing logic |
| `require_credential` | Require a Verified Credential to perform an action |
| `issue_credential` | Issue a Verified Credential on action completion |
| `validate_blueprint` | Check blueprint validity |

---

## Architecture

### Components

```
Blueprint Service
├── Controllers/Endpoints
│   ├── Blueprints API (CRUD, publish, versions)
│   ├── Actions API (submit, retrieve, reject)
│   ├── Templates API (template management)
│   ├── Schemas API (schema browsing)
│   ├── Execution API (helpers)
│   └── Files API (attachments)
├── SignalR Hubs
│   ├── BlueprintHub (/hubs/blueprint — thin-signal notifications, F118)
│   └── ChatHub (/hubs/chat — AI designer streaming)
├── Execution Engine
│   ├── Sorcha.Blueprint.Engine (portable library)
│   ├── JSON Schema validator
│   ├── JSON Logic evaluator
│   └── Disclosure processor
├── Stores (pluggable backend; persistent in Production)
│   ├── IBlueprintStore / IPublishedBlueprintStore
│   ├── IActionStore (F113-audited — fails fast on in-memory in Prod/Staging)
│   └── IInstanceStore (ledger-derived projections, F145)
└── External Integrations
    ├── Wallet Service (signing, encryption)
    └── Register Service (transaction storage)
```

### Data Flow

```
Client → Blueprint API → [Create/Publish Blueprint]
      ↓
Client → Action API → [Submit Action]  ──►  202 Accepted (single async path)
      ↓
Execution Engine → [Validate, Calculate, Route, Disclose]
      ↓
Wallet Service → [Sign Transaction]
      ↓
Register Service → [Seal on Ledger]
      ↓
InstanceProjector (every node) → [Fold sealed docket → advance instance] → SignalR notify
```

> **Feature 145 — ledger-derived instances.** A workflow instance is a deterministic
> projection of the sealed register. Action submission always returns **`202 Accepted`**
> (`isAsync`, empty `nextActions`); the submitter never advances instance state. The
> single `InstanceProjector` folds each sealed action transaction on **every** node, so
> all nodes derive identical state, and it fires the `action-available` / `workflow-completed`
> notifications post-fold. There is no origin/mirror split and no synchronous-advance response.
> See `specs/145-ledger-derived-instances/contracts/submission-response.md`.

---

## Quick Start

### Prerequisites

- **.NET 10 SDK** or later
- **Docker Desktop** (for Redis)
- **Git**

### 1. Clone and Navigate

```bash
git clone https://github.com/Sorcha-Platform/Sorcha.git
cd Sorcha/src/Services/Sorcha.Blueprint.Service
```

### 2. Set Up Configuration

The service uses `appsettings.json` for configuration. For local development, defaults are pre-configured.

### 3. Start Dependencies

Start Redis for caching and SignalR backplane:

```bash
docker run -d -p 6379:6379 --name redis redis:latest
```

### 4. Run the Service

```bash
dotnet run
```

Service will start at:
- **HTTPS (Aspire)**: `https://localhost:7000`
- **HTTP (Docker)**: `http://localhost:5000`
- **Scalar API Docs**: `https://localhost:7000/scalar`
- **SignalR Hub**: `/hubs/blueprint` (reached via the gateway / service host)

---

## Configuration

### appsettings.json Structure

```json
{
  "Logging": {
    "LogLevel": {
      "Default": "Information",
      "Microsoft.AspNetCore": "Warning"
    }
  },
  "AllowedHosts": "*",
  "ConnectionStrings": {
    "Redis": "localhost:6379"
  },
  "ServiceUrls": {
    "WalletService": "https://localhost:7084",
    "RegisterService": "https://localhost:7085"
  },
  "OpenTelemetry": {
    "ServiceName": "Sorcha.Blueprint.Service",
    "ZipkinEndpoint": "http://localhost:9411"
  }
}
```

### Environment Variables

For production deployment:

```bash
# Redis connection
CONNECTIONSTRINGS__REDIS="your-redis-connection-string"

# External service URLs
SERVICEURLS__WALLETSERVICE="https://wallet.sorcha.io"
SERVICEURLS__REGISTERSERVICE="https://register.sorcha.io"

# Observability
OPENTELEMETRY__ZIPKINENDPOINT="https://zipkin.yourcompany.com"
```

---

## API Endpoints

### Blueprint Management

| Method | Endpoint | Description |
|--------|----------|-------------|
| GET | `/api/blueprints/` | Get all blueprints (paginated) |
| GET | `/api/blueprints/{id}` | Get blueprint by ID |
| POST | `/api/blueprints/` | Create new blueprint |
| PUT | `/api/blueprints/{id}` | Update existing blueprint |
| DELETE | `/api/blueprints/{id}` | Delete blueprint (soft delete) |
| POST | `/api/blueprints/{id}/publish` | Publish blueprint to register |
| GET | `/api/blueprints/{id}/versions` | Get all published versions |
| GET | `/api/blueprints/{id}/versions/{version}` | Get specific version |

### Action Management

| Method | Endpoint | Description |
|--------|----------|-------------|
| GET | `/api/actions/{wallet}/{register}/blueprints` | Get available blueprints |
| GET | `/api/actions/{wallet}/{register}` | Get actions (paginated) |
| GET | `/api/actions/{wallet}/{register}/{tx}` | Get action details |
| POST | `/api/actions/` | Submit an action |
| POST | `/api/actions/reject` | Reject a pending action |

### Workflow Instances

| Method | Endpoint | Description |
|--------|----------|-------------|
| GET | `/api/instances/` | List instances for the authenticated user's wallet (paginated, status filter) |
| GET | `/api/me/applications` | Citizen "My Applications" projection — see below (Feature 186) |
| POST | `/api/instances/` | Create a new workflow instance |
| GET | `/api/instances/{instanceId}` | Get a workflow instance by ID |
| GET | `/api/instances/{instanceId}/actions/{actionId}` | Consumer-readable action schema for one action (P0 fix, `fix/pwa-p0-claim-and-camera`) — see below |
| POST | `/api/instances/{instanceId}/actions/{actionId}/execute` | Execute an action with full orchestration |
| POST | `/api/instances/{instanceId}/actions/{actionId}/reject` | Reject a pending action |

**`GET /api/instances/{instanceId}/actions/{actionId}`** is the narrow, instance-scoped read the Wallet
PWA uses to render the form for a citizen's current action. It exists because the authoring endpoint
`GET /api/blueprints/{id}` (Feature 147) is deliberately restricted to service/platform-tier callers —
a consumer-tier citizen token always 403s there. This endpoint sits on the same `CanExecuteBlueprints`
group but adds its own participant gate — at least one of the caller's resolved wallets must be in the
instance's `ParticipantWallets`, resolved via the shared `ParticipantWalletResolver` (`wallet_address`
claim fast path, else Wallet-Service-by-owner fallback — the same seam `GET /api/actions/pending` and
the Feature 176 disclosures endpoint use, since consumer-tier tokens never carry `wallet_address`,
Feature 136) — and returns only the form-relevant subset of the action (`InstanceActionSchemaResponse`:
title, form layout, data schemas, calculations, and this action's own credential requirements/issuance
config) — never routing rules, other participants, or any other action's content. See
`docs/reference/API-DOCUMENTATION.md` for the full response shape and exclusion list.

> **Note:** the participant-check gap this section used to record on `GET /api/instances/{instanceId}`
> is **closed** — issue #1182 added `InstanceParticipantGate` to all three instance reads.

### My Applications (Feature 186 / #1163)

The citizen-facing read surface: *what did I submit, and what happened to it?* Lives under
`/api/me/*`, the platform's personal-scope convention.

| Method | Endpoint | Description |
|--------|----------|-------------|
| GET | `/api/me/applications` | The caller's own applications, newest-first, terminal ones included |
| GET | `/api/me/applications/{instanceId}` | One application with its step timeline |

A **sibling** of `/api/instances`, not a reshaping of it — the Citizen Wallet PWA binds
`GET /api/instances/{id}`, so that group keeps its raw-model shape.

Each row carries the service title, human-readable reference, lifecycle state **as a name**, step
position, a `needsYou` marker, and — where the taken route declared an `x-decision-notice` — the
citizen-facing decision title and reason, resolved from the blueprint's own catalogue via the same
`DecisionNotice.ResolveMessage` the inbox dispatcher uses. The internal reason code is never returned.

**`outcome` is not the same field as `state`, and the difference is the point.** Under Feature 184 a
refusal is expressed as taking a route that declares a decision notice — not as a distinct instance
state. When such a route ends the branch, the fold sees an empty next-action set and assigns
`Completed`, so a refused application and an approved one are indistinguishable by state alone.
`Instance.DecisionRouteId` (projected by `InstanceProjection.ApplyInPlace` from the signed clear
metadata) is what lets `MyApplicationProjector` recover the difference and report `NotApproved`.

`needsYou` fails closed: a terminal application, an unresolvable blueprint, or an absent participant
binding all yield `false`, so the page cannot offer an action that turns out not to be takeable
(issue #1268).

### Template System

| Method | Endpoint | Description |
|--------|----------|-------------|
| GET | `/api/templates/` | Get all published templates |
| GET | `/api/templates/{id}` | Get template by ID |
| POST | `/api/templates/` | Create or update template |
| DELETE | `/api/templates/{id}` | Delete template |
| POST | `/api/templates/evaluate` | Evaluate template with parameters |
| POST | `/api/templates/{id}/validate` | Validate template parameters |
| GET | `/api/templates/{id}/examples/{exampleName}` | Evaluate template example |

### Execution Helpers

| Method | Endpoint | Description |
|--------|----------|-------------|
| POST | `/api/execution/validate` | Validate action data against schema |
| POST | `/api/execution/calculate` | Apply JSON Logic calculations |
| POST | `/api/execution/route` | Determine routing destinations |
| POST | `/api/execution/disclose` | Apply disclosure rules |

### File Management

| Method | Endpoint | Description |
|--------|----------|-------------|
| GET | `/api/files/{wallet}/{register}/{tx}/{fileId}` | Download file attachment |

### Schema Browsing

| Method | Endpoint | Description |
|--------|----------|-------------|
| GET | `/api/schemas/` | Get available schemas |

### SignalR Hub

| Hub | Endpoint | Events |
|-----|----------|--------|
| BlueprintHub | `/hubs/blueprint` | Thin-signal (Feature 118): opaque IDs + timestamps only (e.g. action lifecycle, instance advanced) — fetch detail via REST; see `IBlueprintHubClient` |
| ChatHub | `/hubs/chat` | AI designer streaming (exempt from thin-signal) |

### Pending Action Notifications (Feature 062)

| Method | Endpoint | Description |
|--------|----------|-------------|
| GET | `/api/actions/pending` | Get pending actions (paginated, urgency filter, blueprintId filter) |
| GET | `/api/actions/pending/count` | Get pending action count (for badge display) |
| GET | `/api/actions/open-starting` | Starting actions awaiting their open (Feature 103) participant — `blueprintId` **required**, optional `registerId` |

**`/pending` means "assigned to you"; `/open-starting` means "waiting for somebody" (issue #1446).**
An action whose sender is a Feature 103 open participant is bound to no wallet until the first
qualifying submitter late-binds, so it belongs in nobody's personal list. It used to appear in the
list of every *other* participant on the instance (a tenant's "Report Problem" listed seven times as
the housing officer's work) while the citizen who could actually perform it — not in
`ParticipantWallets` until they submit — saw nothing.

The two surfaces **partition** every current action: `EfCoreInstanceStore.IsActionForWallet` excludes
exactly the case `ActionEndpoints.IsUnboundOpenSender` publishes, and a test asserts the XOR. An
action on neither would be unreachable; on both, the noise returns.

`/open-starting` requires `blueprintId` deliberately — that is what makes it a deliberate question
("which instances of the service I operate are waiting to be started?") rather than an unbounded feed
of every open instance on the node. Its authorization is the group's plain authenticated check,
matching `InstanceParticipantGate.IsAwaitingOpenParticipant`, which already lets any authenticated
caller read `GET /api/instances/{id}` while it awaits its open participant.

**NotificationConfig on Action Model:** The `Action` class in `Sorcha.Blueprint.Models` now includes a `NotificationConfig` property that defines per-action notification behavior (summary template, urgency rules, deadline).

**Utilities:**
- **SummaryTemplateRenderer** — Renders human-readable notification summaries from action metadata using configurable templates
- **UrgencyCalculator** — Computes notification urgency (Low/Medium/High/Critical) based on deadline proximity and action configuration

**EventsHubNotificationBridge Enhancements:** The bridge now enriches inbound action notifications with summary text, urgency level, and deadline information before delivery. It also persists `ActivityEvent` records for notification history tracking.

### Disclosed Prior-Action Data Query (Feature 176)

| Method | Endpoint | Description |
|--------|----------|-------------|
| GET | `/api/workflows/{instanceId}/actions/{actionId}/disclosures` | Prior-action data disclosed to the calling participant, for the action being decided |
| GET | `/api/workflows/{instanceId}/disclosures` | Instance-wide form (anchored on the instance's current action) |

The read-side of the DAD disclosure model. Reconstructs each required prior action's caller-decryptable
view from the instance's sealed transactions (identical for encrypted and dev-mode registers) and clamps
it to exactly the caller participant's entitlement — no undisclosed field is ever returned. Authenticated;
the caller's wallet(s) are resolved from the `wallet_address` claim or the Wallet Service fallback (the
same path `/api/actions/pending` uses), and `X-Delegation-Token` (when supplied) unwraps disclosure-group
keys on encrypted registers. Returns `recipientResolved: false` with an empty view when the caller is not a
recipient. Backed by the shared `IActionDisclosureResolver` (also used by `ActionExecutionService`), so the
execution and query paths share one disclosure implementation. Consumed by the autonomous `Sorcha.Agent`
(it sets each pending action's previous payload from `disclosedFields` before running its checks, and holds
fail-closed when the fetch is unavailable) and by the MCP `sorcha_disclosed_data` participant tool.

### File Chunk Submission (Feature 085)

| Method | Endpoint | Description |
|--------|----------|-------------|
| POST | `/api/file-chunks` | Submit an encrypted file chunk (staged upload) |

**Purpose:** Supports large encrypted file attachments by accepting chunks individually before they are assembled and committed as part of a blueprint action transaction.

**Request Body:**
```json
{
  "uploadId": "string",
  "chunkIndex": 0,
  "totalChunks": 5,
  "encryptedData": "base64-encoded-chunk",
  "checksum": "sha256-hex"
}
```

**Response:** `202 Accepted` — chunk acknowledged and staged. Final assembly occurs when all `totalChunks` chunks are received.

**Rate Limiting:** `RateLimitPolicies.Strict` (wallet operations policy).

**Auth:** JWT Bearer required.

For full API documentation with request/response schemas, open **Scalar UI** at `https://localhost:7000/scalar`.

---

## Verified Citizen v2 Subsystems (Feature 103)

Three Blueprint Service components ship with Feature 103. They cooperate
to support open citizen-facing actions, schema reuse via JSON Schema
`$ref`, and credential issuance from nested primitive payloads.

### CoreSchemaSeedService

Hosted background service that seeds the Sorcha core identity primitives
(`PersonName/v1`, `DateOfBirth/v1`, `EmailAddress/v1`, `PostalAddress/v1`)
into the schema store on startup if they are not already present. Seed
documents live under
`src/Services/Sorcha.Blueprint.Service/CoreSchemas/v1/*.schema.json` and
are embedded resources, so the service runs without any external file
mount. Future versions add new files alongside existing ones — the
seeder is idempotent and never overwrites a stored schema.

### SchemaRefResolver

Publish-time JSON Schema `$ref` flattener. When `PublishService`
processes an action's `dataSchemas`, the resolver walks the schema tree
and inlines any `$ref` that points to a `https://schemas.sorcha.dev/core/*`
primitive, producing a single self-contained schema document for the
runtime. Inlining at publish time means the runtime never has to chase
external `$ref` URLs — `JsonSchema.Net` evaluation runs against a fully
materialised tree. The `BlueprintRefResolutionContext` tracks visited
URIs to defend against accidental cycles and missing primitives are
reported as publish-time validation errors with the same shape as
schema-validation errors.

### InstanceBindingCache

Redis-backed cache that pins the late-bound applicant wallet to an open
starting action's `Sender` participant. The first qualifying submitter
wins and is recorded for the lifetime of the instance; subsequent
submissions from a different wallet are rejected with the standard
`wallet not authorized` validation error. The cache uses a dedicated
`sorcha:binding:{instanceId}:{actionId}` keyspace with a 24-hour TTL
and emits OpenTelemetry metrics under `sorcha.binding_cache.*` for
hit-rate and read latency observability. A publish-time guardrail
(`VAL_BP_010` in the Validator Service) rejects blueprints where the
participant referenced by an open starting action has a non-null
`WalletAddress` — the foot-gun this whole feature exists to prevent.

### Late-binding claim mapping

`ActionExecutionService.BuildClaimsFromMappings` walks `ClaimMapping.SourceField`
JSON Pointer paths (with RFC 6901 `~1`/`~0` escape decoding) so a
credential with claims like `givenName`, `familyName`, `dateOfBirth` can
source from a nested submission payload like `{ "name": { "givenName": "..." } }`.
The same helper feeds both the internal Sorcha issuance path and the
HAIP external-wallet path, so the two stay in sync. Missing claim
sources are logged at Warning level — silently dropped claims would
otherwise produce credentials with fewer attributes than the action
promised.

---

## Full Rehearsal reads its outcome from the ledger, not the response (Feature 142 / F145)

`RehearsalOrchestrationService.SubmitStepAsync` used to decide success with
`terminalSuccess = response.IsComplete`, and drive step routing from
`response.NextActions`. **Post-Feature-145 neither field can carry that information.**

`ActionExecutionService` returns 202 on every accepted submission and hard-codes
`IsComplete = false` and `NextActions = []` — the real routing goes only onto the
on-chain `RoutingDecision`, for the `InstanceProjector` to fold when the docket
seals. There is no `IsComplete = true` anywhere in `src/`; the only one in the
repo was a **test fixture**, so the rehearsal tests passed while the feature was
structurally incapable of working.

Consequences before this fix, for **every** blueprint (not only `x-claim-source`
ones):

- `RehearsalPass` was never written, so `PublishGate`'s soft gate never returned
  `Proceed` — publish was **override-only**, permanently.
- the walk-through could never advance past step 1, because the next action ids
  never arrived.

**Now the projection is the source of truth.** After `ExecuteAsync` returns, the
step waits for the `InstanceProjector` to fold *its own* transaction — matching
on `Instance.LastAppliedTxId`, the projector's idempotency watermark, so a
previous step's projection is never mistaken for this one's — then reads the
result:

| Projection | Rehearsal outcome |
|---|---|
| `State == Completed` (no current actions remain) | **Passed** → records the `RehearsalPass` that unlocks Go live |
| `State == Rejected` | **Failed**, named as a terminal rejection |
| still `Active` | in progress; next steps taken from `CurrentActionIds` |
| never folded within the timeout | **Failed**, named as a *sealing delay* — explicitly "not a blueprint problem" — and **no pass is recorded** |

The timeout defaults to **90s with a 1s poll**, mirroring the proven walkthrough
helper `Wait-SorchaActorReady`; the validator's docket-build threshold is 5-10s,
so that is generous rather than tight. Both are constructor parameters so tests
run in milliseconds.

> **`LastAppliedTxId` must actually reach the database.** `EfCoreInstanceStore.UpdateAsync`
> copies the model onto the tracked entity **by hand, field by field**, and this one was
> missing from that list until 2026-07-30. Nothing failed loudly:
> `InstanceProjection.ApplyInPlace` sets `LastTransactionId` and `LastAppliedTxId` on
> adjacent lines, so the instance still advanced correctly and only this column stayed
> NULL. Live on n1 every instance — rehearsal and AIAS alike — showed
> `LastTransactionId` populated and `LastAppliedTxId` NULL.
>
> Two things depended on it and **both failed open**:
>
> 1. **The projector's replay guard.** `InstanceProjector` re-reads the instance from the
>    store before every fold, and `InstanceProjection.Apply` skips a transaction only when
>    `LastAppliedTxId == tx.TxId`. Read back as NULL that never matched, so a redelivered
>    `docket:confirmed` re-applied an already-folded transaction — inflating
>    `CompletedActionCount` and, on out-of-order redelivery, rewinding `CurrentActionIds`
>    to an earlier action's next-set.
> 2. **This rehearsal wait**, which is why the go-live gate stayed unearnable in any
>    Postgres deployment even after the projection change above.
>
> **Why no test caught it:** `InMemoryInstanceStore.UpdateAsync` stores the model **by
> reference** (`_instances[instance.Id] = instance`), so every field round-trips for free.
> The EF Core store is the only `IInstanceStore` with a hand-written copy list, so the
> in-memory suite could never see the difference — and the EF Core store had no
> round-trip test at all. A test that exercises only the reference-semantics store proves
> nothing about the one deployments actually run.
>
> `EfCoreInstanceStoreUpdateRoundTripTests` guards it against the **EF Core** store
> specifically, and deliberately asserts over the **whole model by reflection** rather
> than this one field — the defect is the hand-maintained copy list, so the next property
> added to `Instance` can be dropped the same way.

**This works because sandbox-register dockets seal automatically.** Register
creation embeds the local validator on the roster when none is supplied
(`RegisterCreationOrchestrator`) and fires `register:relationship-changed`, which
the validator's `RegisterMonitoringBootstrap` consumes to enrol the register for
monitoring. No separate enrolment step, and no operator action.

A step that returns `AwaitingPresentation` is left in progress and does **not**
wait — there is no seal pending, the action is blocked on an external credential
presentation.

The named-timeout failure is deliberate: an unverified pass would be worse than
no pass, and a designer must be able to tell a slow node from a broken blueprint.

## Full Rehearsal executes as its own initiator (Feature 142 / Issue #1284)

`RehearsalOrchestrationService.SubmitStepAsync` calls the real
`IActionExecutionService.ExecuteAsync` for every step, so a rehearsal exercises
the SAME execution pipeline a live submission does — including any
`x-claim-source` bindings (Issue #1264) declared on the action's `dataSchemas`.
Those bindings resolve their value from `IPlatformUserClaimsClient`, keyed off
the caller's `platform_user_id` claim — there is no other way to know *whose*
live value to read.

The call used to pass `caller: null` (a leftover from when rehearsal only
needed to skip wallet-ownership validation for the sandbox wallets it mints
itself). `ActionExecutionService` fails closed rather than default a value it
cannot vouch for, so a null caller made it throw for ANY action with an
`x-claim-source` binding — Full Rehearsal could never pass for such a
blueprint, which includes the AIAS assured-identity template. Go-live was
reachable only via the audited override.

The fix builds a synthetic `ClaimsPrincipal` from the rehearsal session's own
initiator (`RehearsalSession.StartedByPlatformUserId`, captured at
`StartFullAsync`) and passes that as `caller` instead. This is semantically
the right behaviour, not a workaround: **rehearsal means "walk this workflow
as me"**, so claim-source bindings should resolve the real live values of the
person running the rehearsal — which also makes the rehearsal a truthful dry
run of what go-live will stamp. The synthetic principal carries two claims:
`platform_user_id` (read by claim-source resolution) and a `NameIdentifier`
("sub") of the same value — required because a non-null caller now also
activates `ValidateWalletOwnershipAsync` (SEC-006), which a null caller always
skipped outright; the principal deliberately omits `org_id` so that check
short-circuits to "no participant-based validation" instead of querying the
Participant Service for a sandbox wallet that was never given a real
participant profile (Feature 103 open-participant walk-in path).

**The initiator id is `PlatformUser.Id`, not `UserIdentity.Id`.**
`RehearsalEndpoints.ResolvePlatformUserId` reads the `platform_user_id` claim
(Feature 136; on every human-tier token) and falls back to `sub` only for
tokens minted before that claim existed. The two are different GUIDs:
`RegistrationService` generates a `PlatformUser` and a `UserIdentity`
independently, linked only by a FK, and they coincide **only** for the seeded
default admin (`DatabaseInitializer` reuses `WellKnownIds.DefaultAdminUserId`
for both). Reading `sub` therefore works on a freshly seeded dev box and 404s
for every real user — `IPlatformUserClaimsClient.ResolveAsync` finds no matching
`PlatformUser` and the step dies with "Could not confirm your account details
with the platform", trading #1284's exception for a new one on exactly the
blueprints this fix targets. The destination field is named
`RehearsedByPlatformUserId`, so `PlatformUser.Id` was always the intent.
Guarded by `RehearsalInitiatorIdentityTests`.

Reaching a terminal state is a separate concern, handled by the projection
reconciliation described in the preceding section — the response's
`IsComplete` / `NextActions` are no longer read at all.

---

## Development

### Project Structure

Persistence is the **Store pattern** (EF Core over PostgreSQL `sorcha_blueprint`, with in-memory fallbacks) — there is **no** `Repositories/` layer and no in-memory `BlueprintRepository`; those never existed here. This matches the Architecture → Stores section above (`IBlueprintStore` / `IActionStore` / `IInstanceStore`, F113-audited).

```
Sorcha.Blueprint.Service/
├── Program.cs                      # Service entry point, DI, endpoint mapping
├── Endpoints/                      # Minimal-API endpoint groups (blueprints, actions, files, instance-action schema, …)
├── Hubs/
│   ├── BlueprintHub.cs             # SignalR notifications (thin-signal, Feature 118)
│   └── ChatHub.cs                  # AI-designer chat stream
├── Services/                       # Business logic (ChatOrchestration, SchemaRefResolver, StatusListManager,
│                                   #   InstanceBindingCache, CoreSchemaSeed, AnthropicProvider, …)
├── Storage/                        # The Store seams + implementations:
│   ├── IBlueprintStore.cs   / EfCoreBlueprintStore.cs   / InMemoryBlueprintStore.cs
│   ├── IActionStore.cs      / EfCoreActionStore.cs      / InMemoryActionStore.cs
│   ├── IInstanceStore.cs    / EfCoreInstanceStore.cs    / InMemoryInstanceStore.cs
│   └── EfCoreTemplateStore.cs, EfCoreRehearsalPassStore.cs, EfCorePublishOverrideStore.cs, …
├── Data/
│   ├── BlueprintDbContext.cs       # EF Core context → PostgreSQL sorcha_blueprint
│   ├── Entities/                   # Persistence entities
│   └── Migrations/
├── Models/                         # Request/response DTOs
├── Templates/                      # Blueprint templates
└── appsettings.json

External libraries: Sorcha.Blueprint.Models (shared models), Sorcha.Blueprint.Engine (portable execution), Sorcha.Blueprint.Fluent (fluent API).
```

### Running Tests

```bash
# Run all Blueprint Service tests
dotnet test tests/Sorcha.Blueprint.Service.Tests

# Run with coverage
dotnet test tests/Sorcha.Blueprint.Service.Tests --collect:"XPlat Code Coverage"

# Watch mode (auto-rerun on changes)
dotnet watch test --project tests/Sorcha.Blueprint.Service.Tests
```

### Code Coverage

**Current Coverage**: ~85%
**Tests**: 37 integration tests
**Lines of Code**: ~1,600 LOC

```bash
# Generate coverage report
dotnet test tests/Sorcha.Blueprint.Service.Tests --collect:"XPlat Code Coverage"
reportgenerator -reports:**/coverage.cobertura.xml -targetdir:coverage -reporttypes:Html
```

Open `coverage/index.html` in your browser.

---

## Integration with Other Services

### Wallet Service Integration

The Blueprint Service integrates with the Wallet Service for:
- **Transaction Signing**: Automatically sign transactions before blockchain submission
- **Payload Encryption**: Encrypt sensitive action payloads
- **Payload Decryption**: Decrypt received action data

**Communication**: HTTP REST API
**Endpoints Used**: `/api/v1/wallets/{address}/sign`, `/api/v1/wallets/{address}/encrypt`, `/api/v1/wallets/{address}/decrypt`

### Register Service Integration

The Blueprint Service integrates with the Register Service for:
- **Transaction Storage**: Submit signed transactions to the blockchain
- **Transaction Retrieval**: Query transaction history
- **Blueprint Publishing**: Associate blueprints with specific registers

**Communication**: HTTP REST API
**Endpoints Used**: `/api/registers/{registerId}/transactions`

### SignalR Client Example

```typescript
import * as signalR from "@microsoft/signalr";

const connection = new signalR.HubConnectionBuilder()
    .withUrl("https://localhost:7000/hubs/blueprint")
    .build();

connection.on("TransactionConfirmed", (transactionId, status) => {
    console.log(`Transaction ${transactionId} confirmed with status: ${status}`);
});

await connection.start();
```

---

## Security Considerations

### Authentication

- **Current**: Development mode (no authentication required)
- **Production**: JWT bearer token authentication required (issued by Tenant Service)

### Authorization

- Action submission requires proof of wallet ownership (signature verification)
- Blueprint publishing restricted to wallet owners
- File downloads restricted to action participants

### Data Protection

- Sensitive payloads encrypted using Wallet Service
- Selective disclosure enforced through disclosure rules
- Transaction signatures prevent tampering

### Sorcha-wallet presentation callback (Feature 127) — server-side session only

`POST /api/presentations/callbacks/sorcha-wallet/{requestId}` (`RequireConsumerAudience`) is reached by any
signed-in citizen, so its request body must never be allowed to influence *which* credential requirement is
being checked. `SorchaWalletPresentationConsumer.VerifyAsync` therefore **always** rebuilds the
`VerifierSession` (nonce, required `vct`, required claims, verifier `client_id`) from the server-side
pending-presentation row (`PresentationInitiationContext`, persisted at initiation) — never from the caller's
JSON. `SorchaWalletVerificationPayload`, the wire shape the callback deserializes, carries only `VpToken` and
`DelegationCredential`; it deliberately has no `Session` field. It used to — an optional `session` object that,
when present, was used verbatim — which let an authenticated citizen post their own session (a weaker
`RequiredVct`, an emptied `RequiredClaims` gate, an attacker nonce) and satisfy any credential gate with any
held credential (G2, fixed 2026-07-30; see `.specify/MASTER-TASKS.md` and the `sorcha-architecture` skill's
"Credential gates (Feature 127)" section for the full writeup). Do not reintroduce a client-supplied session
field on this or any future presentation-callback payload.

### Secrets Management

- Wallet Service connection requires service principal credentials (stored in Azure Key Vault or environment variables)
- Redis connection string should use TLS in production

---

## Deployment

### .NET Aspire (Development)

The Blueprint Service is registered in the Aspire AppHost:

```csharp
var blueprintService = builder.AddProject<Projects.Sorcha_Blueprint_Service>("blueprint-service")
    .WithReference(redis);
```

Start the entire platform:

```bash
dotnet run --project src/Apps/Sorcha.AppHost
```

Access Aspire Dashboard: `http://localhost:15888`

### Docker

```bash
# Build Docker image
docker build -t sorcha-blueprint-service:latest -f src/Services/Sorcha.Blueprint.Service/Dockerfile .

# Run container
docker run -d \
  -p 5000:8080 \
  -e ConnectionStrings__Redis="redis:6379" \
  -e ServiceUrls__WalletService="http://wallet-service:8080" \
  -e ServiceUrls__RegisterService="http://register-service:8080" \
  --name blueprint-service \
  sorcha-blueprint-service:latest
```

### Azure Deployment

Deploy to Azure Container Apps with:
- **Redis Cache**: Azure Cache for Redis
- **Secrets**: Azure Key Vault for service credentials
- **Observability**: Application Insights integration

---

## Observability

### Logging (Serilog + Seq)

Structured logging with Serilog:

```csharp
Log.Information("Blueprint {BlueprintId} published to register {RegisterId}", blueprintId, registerId);
```

**Log Sinks**:
- Console (structured output via Serilog)
- OTLP → Aspire Dashboard (centralized log aggregation)

### Tracing (OpenTelemetry + Zipkin)

Distributed tracing with OpenTelemetry:

```bash
# View traces in Zipkin
open http://localhost:9411
```

**Traced Operations**:
- HTTP requests
- Wallet Service calls
- Register Service calls
- SignalR connections

### Metrics (Prometheus)

Metrics exposed at `/metrics`:
- Request count and latency
- Action submission rate
- Blueprint publish rate
- SignalR connection count

---

## Troubleshooting

### Common Issues

**Issue**: SignalR hub connection fails
**Solution**: Ensure Redis is running and accessible. Check `ConnectionStrings:Redis` in appsettings.json.

```bash
# Test Redis connectivity
docker exec -it redis redis-cli ping
```

**Issue**: Wallet Service integration error
**Solution**: Verify Wallet Service is running and `ServiceUrls:WalletService` is correct.

```bash
# Test Wallet Service health
curl https://localhost:7084/api/health
```

**Issue**: Blueprint validation fails
**Solution**: Ensure blueprint JSON matches the JSON Schema definition. Use `/api/schemas/` to browse available schemas.

**Issue**: File upload fails
**Solution**: Check file size limits (default: 10 MB). Increase in `appsettings.json`:

```json
{
  "Kestrel": {
    "Limits": {
      "MaxRequestBodySize": 52428800
    }
  }
}
```

### Debug Mode

Enable detailed logging:

```json
{
  "Logging": {
    "LogLevel": {
      "Default": "Debug",
      "Sorcha.Blueprint.Service": "Trace"
    }
  }
}
```

---

## Contributing

### Development Workflow

1. **Create a feature branch**: `git checkout -b feature/your-feature`
2. **Make changes**: Follow C# coding conventions
3. **Write tests**: Maintain >85% coverage
4. **Run tests**: `dotnet test`
5. **Format code**: `dotnet format`
6. **Commit**: `git commit -m "feat: your feature description"`
7. **Push**: `git push origin feature/your-feature`
8. **Create PR**: Reference issue number

### Code Standards

- Follow [C# Coding Conventions](https://docs.microsoft.com/en-us/dotnet/csharp/fundamentals/coding-style/coding-conventions)
- Use async/await for I/O operations
- Add XML documentation for public APIs
- Include unit tests for all business logic
- Use dependency injection for testability

---

## Resources

- **Specification**: [.specify/specs/](https://github.com/Sorcha-Platform/Sorcha/tree/master/.specify/specs)
- **API Reference**: [Scalar UI](https://localhost:7000/scalar)
- **Architecture**: [docs/architecture.md](../../../docs/architecture.md)
- **Development Status**: [docs/development-status.md](../../../docs/reference/development-status.md)
- **Portable Engine**: [src/Core/Sorcha.Blueprint.Engine](../../Core/Sorcha.Blueprint.Engine/)
- **OpenAPI Spec**: `https://localhost:7000/openapi/v1.json`

---

## License

Apache License 2.0 - See [LICENSE](https://github.com/Sorcha-Platform/Sorcha/blob/master/LICENSE) for details.

---

**Last Updated**: 2026-07-21
**Maintained By**: Sorcha Contributors
**Status**: ✅ Production Ready (100% Complete)
