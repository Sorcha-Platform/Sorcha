# Data Model: Blueprint Design Lifecycle Overhaul (142)

New and changed models. Most domain objects (Blueprint, Action, Participant, Route, Disclosure, Register, GovernanceRoster) already exist and are reused unchanged unless noted. "Persisted" = durable store; "Transient" = client/session state.

---

## Persisted (Blueprint Service, Postgres — via F113 `IStorageRegistrationLog`)

### RehearsalPass
Records that a full rehearsal succeeded for a specific executable definition. Backs the server-side soft gate (D4/FR-032).

| Field | Type | Notes |
|---|---|---|
| `Id` | Guid | PK |
| `BlueprintId` | string | The draft/service identity |
| `ExecDefHash` | string | Canonical hash of the executable definition (D7) at rehearsal time |
| `RehearsedAt` | DateTimeOffset | UTC completion time |
| `RehearsedByPlatformUserId` | Guid | Who ran it |
| `SandboxRegisterId` | string | Where it ran (audit/debug) |

- **Uniqueness**: latest pass per `(BlueprintId, ExecDefHash)` is what the gate checks; older passes may be retained for history.
- **Lifecycle**: created on successful full rehearsal; never mutated. A publish checks for a row matching the publishing version's `ExecDefHash`.

### PublishOverride
Audit record when an authorised user publishes despite no matching `RehearsalPass` (D4/FR-032).

| Field | Type | Notes |
|---|---|---|
| `Id` | Guid | PK |
| `BlueprintId` | string | |
| `Version` | int | The published version |
| `RegisterId` | string | Target live register |
| `ExecDefHash` | string | Of the published version |
| `OverriddenByPlatformUserId` | Guid | Must hold register publish-governance authority |
| `OverriddenAt` | DateTimeOffset | UTC |
| `Reason` | string? | Optional free text |

- **Lifecycle**: append-only audit; never deleted.

---

## Persisted (Register Service / existing stores — reused with small additions)

### Register (existing — additions)
- Add/surface `Advertise` (visibility public/private) on the register **read** response so the Go-live detail card can show it (D6). Existing `DevMode` is already readable.
- Sandbox registers carry `Metadata["sandbox"] = "true"` (and an owning-org marker) so they can be excluded from the Go-live picker and normal listings (D1).

### Published Blueprint version (existing)
- Reused for the amend loop: a clone derives a new draft from a published version, carrying lineage (prior version + target register). No schema change to the published record itself; version increment uses the existing publish/version mechanism.

---

## Transient — Rehearsal session (Blueprint Service, in-memory/Redis for the run; not a ledger record)

### Rehearsal
Represents one in-progress or completed rehearsal.

| Field | Type | Notes |
|---|---|---|
| `RehearsalId` | Guid | |
| `BlueprintId` | string | |
| `ExecDefHash` | string | Snapshot at start |
| `Mode` | enum `DryRun` \| `Full` | |
| `SandboxRegisterId` | string? | Full mode only |
| `RoleWallets` | map role→walletAddress | Full mode: ephemeral per-role sandbox wallets (D2); discarded on reset |
| `CurrentActingRole` | string | The role the admin is currently "acting as" |
| `Steps` | list of `RehearsalStep` | Ordered walk-through state |
| `Outcome` | enum `InProgress` \| `Passed` \| `Abandoned` \| `Failed` | `Passed` (Full) writes a `RehearsalPass` |
| `Log` | list of `RehearsalEvent` | Plain-language events (provisioned, gate-passed, sealed, routed, delivered) |

### RehearsalStep
| Field | Type | Notes |
|---|---|---|
| `ActionId` | int | |
| `ActingRole` | string | |
| `Status` | enum `Pending` \| `Current` \| `Done` | |
| `SubmittedPayload` | object? | What the role submitted |
| `RoutingOutcome` | object? | Next action(s) chosen |
| `DisclosureOutcome` | object? | Who-sees-what for the step |

- **Dry-run**: `Steps` driven by the in-WASM engine; no `SandboxRegisterId`, no `RoleWallets`, credential steps marked "checked in full rehearsal".
- **Reset/delete**: discards the `Rehearsal` + its `RoleWallets`; the sandbox register persists (D1).

---

## Transient — Designer workspace (UI client state, `DesignerContext` — extended)

### LifecycleState (extends existing DesignerContext)
Drives the rail; not persisted server-side (the *gate* truth is the server `RehearsalPass`, D4).

| Field | Type | Notes |
|---|---|---|
| `CurrentStage` | enum `Describe`\|`Understand`\|`Rehearse`\|`GoLive` | |
| `RehearsalPassedForCurrentExecDef` | bool | Mirrors server `RehearsalPass` for the current `ExecDefHash`; drives the Go-live UI lock |
| `ExecDefHash` | string | Recomputed client-side on edit (D7) — change vs last pass re-locks |
| `AmendContext` | object? | When amending: source published version + target register (D10) |
| existing: `Blueprint`, `Validation`, `ChatSessionId`, `ActiveActionId`, `IsManualCursor`, `IsDirty` | | unchanged |

### JourneyViewModel (derived, read-only)
A projection of the current Blueprint for the Understand canvas (FR-006/007/009). Not authored or persisted.

| Field | Type | Notes |
|---|---|---|
| `Steps` | list of `JourneyStep` | One per Action, in flow order |
| per step: `Role` (participant display + colour), `Title`, `PlainSummary`, `Badges` (e.g. `MustProve:{type}`, `Issues:{type}`), `Detail` (disclosure summary, decision, form reference) | | Derived from participants, `isStartingAction`, `credentialRequirements`, `credentialIssuanceConfig`, `disclosures`, `routes` |

### FormLayout (authored onto the Blueprint — not a new entity)
Form-layout authoring (D8) writes **standard `x-*` keywords** onto the Action `dataSchemas`. There is no separate layout record: the Blueprint JSON is the single source of truth. The presentational/behavioural split (D7) governs which keywords feed the `ExecDefHash`.

---

## Relationships

- `RehearsalPass.ExecDefHash` ↔ the publishing Blueprint version's `ExecDefHash` — the join the server gate checks (D4).
- `Rehearsal (Full, Passed)` → writes one `RehearsalPass`.
- `PublishOverride` exists **only** when a publish proceeded without a matching `RehearsalPass`.
- `Rehearsal.SandboxRegisterId` → the per-org sandbox `Register` (reused; D1).
- `LifecycleState.ExecDefHash` (client) is computed by the same canonicaliser as the server (D7) so the UI lock and server gate agree.

## Validation rules (from requirements)

- Rehearse is unavailable while the Blueprint has blocking validation errors (Edge Cases; engine `ValidateAsync`).
- A `RehearsalPass` is valid for a publish **iff** its `ExecDefHash` equals the publishing version's `ExecDefHash` (FR-024/FR-032); presentational-only changes preserve the hash (D7/Q4).
- Publish refuses (no record written) when the caller lacks register governance rights (FR-027/D5), independent of the rehearsal gate.
- Sandbox registers MUST NOT appear as Go-live targets (D1/SC-008).
