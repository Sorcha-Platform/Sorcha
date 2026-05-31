# Command Contracts: Assured Identity Demo Toolkit

The toolkit exports four PowerShell commands from `demos/AssuredIdentity/AssuredIdentityDemo.psm1`. Each contract gives parameters, behaviour, success output, idempotency, and exit/return semantics. Commands consume **existing** Sorcha HTTP endpoints (see plan §"Grounded integration signals") and the existing `sorcha-agent` CLI; they add no endpoints.

Common: all commands take `-NodesFile` (default `./demo-nodes.json`) and `-StateFile` (default `./state.json`); honour `$ErrorActionPreference='Stop'`; emit `Write-Wt*` progress; never print secrets.

---

## `New-IssuingAuthority` (issuer node) — FR-001/002/003/005, FR-010/011/012/013

**Parameters**

| Name | Type | Default | Notes |
|---|---|---|---|
| `-IssuerNode` | string | first inventory entry with `role=issuer` | inventory `id`. |
| `-AgencyName` | string | `"Strathcarron Identity Authority"` | single source of agency identity (FR-002). |
| `-AgentMode` | `rules`\|`ai`\|`human` | `rules` | approval mechanism (FR-010). |
| `-Force` | switch | off | bypass reuse and recreate (still reconciles stale state). |

**Behaviour**
1. Load + validate inventory; resolve issuer node + sysadmin creds (from `deploy/keys.env`).
2. **Idempotency probe** (FR-003, R5): look for existing org / advertised register / published blueprint; reconcile a stale subscription-vs-missing-register desync. Reuse unless `-Force`.
3. Provision (or reuse): enable public org, create/verify verification-admin (Tier-2) + analyst (Tier-3) + wallets + participants, create advertised **DevMode** register, publish analyst participant.
4. **Publish blueprint** from `blueprints/assured-identity.template.json` with `{{issuerName}}` ← `-AgencyName` and analyst wallet mapped.
5. **Agent** (FR-011): render `analyst.<mode>.json` from template (`{{...}}` ← state); `rules`/`ai` → launch `sorcha-agent run` (ai checks `ANTHROPIC_API_KEY`, sets the decision-wait guardrail per R6); `human` → print approval instructions.
6. Write/merge `state.json`.

**Success output**: a summary object `{ issuerNode, agencyName, organizationId, registerId, blueprintId, agentMode, agentRunning }`.

**Exit/return**: throws (non-zero) on unrecoverable error; returns the summary on success. Idempotent re-run with same args → same IDs, no duplicates (SC-003).

---

## `Connect-Subscriber` (subscriber node) — FR-004/008, readiness gate R4

**Parameters**

| Name | Type | Default | Notes |
|---|---|---|---|
| `-SubscriberNode` | string | first inventory entry with `role=subscriber` | inventory `id`. |
| `-RegisterId` | string | `state.json.registerId` | the advertised register to subscribe to. |
| `-TimeoutSeconds` | int | 120 | readiness poll cap (> observed ≤60s recovery window). |

**Behaviour**
1. Resolve subscriber node + its public org + sysadmin creds.
2. Discover the advertised register on the issuer and `POST /api/organizations/{orgId}/register-subscriptions` (idempotent: reuse an `Active` subscription; reconcile a stale one).
3. **Readiness gate** — poll until ALL hold or timeout: subscription `Active` ∧ `sync-state == CaughtUp` ∧ target blueprint present in `/blueprints/published`. Bounded backoff.
4. Append/update the `subscribers[]` entry in `state.json` with `status` + `lastReadyAt`.

**Success output**: `{ subscriberNode, orgId, subscriptionId, status: Ready|NotReady, reasons[] }`.

**Exit/return**: success only when the gate reports `Ready`; on timeout returns `NotReady` with `reasons[]` (e.g. `recovery-in-progress`) — a soft, retryable outcome, not a crash. Repeatable across N subscribers (FR-008).

---

## `Reset-Demo` — FR-017

**Parameters**

| Name | Type | Default | Notes |
|---|---|---|---|
| `-Scope` | `issuer`\|`subscriber`\|`all` | `all` | what to reset. |
| `-Node` | string | — | required when scope=subscriber (which one). |
| `-Confirm` | switch | on | destructive; require explicit confirmation. |

**Behaviour**: return the target to a clean pre-provision state per the documented reset recipe — issuer: demo wallets, non-system register Mongo DB(s), `state.json`; subscriber: its `OrganizationRegisterSubscriptions` rows for the demo register, replicated register state, its `subscribers[]` entry. Stop any tracked `sorcha-agent` process. Idempotent (resetting an already-clean node is a no-op success).

**Success output**: `{ scope, node, removed[] }`.

**Exit/return**: throws on partial failure with a clear list of what was/wasn't cleaned.

---

## `Get-DemoStatus` — FR-018, SC-007

**Parameters**

| Name | Type | Default | Notes |
|---|---|---|---|
| `-Verbose` | switch | off | per-signal detail. |

**Behaviour**: for the issuer and every `subscribers[]` entry, gather: container/service reachability (gateway health), subscription status, `sync-state`, blueprint-published presence, and approver state (tracked process alive / human-acknowledged). Compute the Readiness Verdict (data-model "Derived").

**Success output**: a table + an overall `{ verdict: Ready|NotReady, perNode[], reasons[] }`. The verdict MUST match actual tester success (SC-007).

**Exit/return**: always returns the verdict object (querying is non-destructive); non-zero exit only on inability to read the inventory/state.
