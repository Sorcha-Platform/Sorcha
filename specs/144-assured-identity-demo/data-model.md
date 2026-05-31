# Phase 1 Data Model: Assured Identity Demo Environment

This feature stores no database state. Its "data model" is a small set of **config + state files** the toolkit reads and writes, plus the conceptual entities they represent. All files are JSON; secrets are never among them.

---

## Entity 1 — Installation (node inventory entry)

A participating node. Collection lives in `demo-nodes.json` (gitignored; `demo-nodes.example.json` committed).

| Field | Type | Required | Notes |
|---|---|---|---|
| `id` | string | yes | Stable handle used by `-IssuerNode` / `-SubscriberNode` (e.g. `tiny`, `n1`). Unique within the inventory. |
| `role` | enum `issuer` \| `subscriber` | yes | Default/expected role. A node may be re-selected into either role at run time; this records the inventory's intent. |
| `gateway` | string (URL) | yes | API gateway base URL the toolkit calls (e.g. `http://tiny:8090`, `https://n1.sorcha.dev`). |
| `installationName` | string | yes | The installation's JWT `InstallationName` (e.g. `tiny.sorcha.dev`). Documents the trust boundary; toolkit does not mint tokens with it. |
| `rendezvousCapable` | bool | no (default false) | Whether this node accepts inbound reverse streams (public/rendezvous). Drives expectations, not behaviour. |
| `adminEmail` | string | no | Operator's sysadmin login on this node (password from `deploy/keys.env`, never here). Defaults to the seeded `admin@sorcha.local`. |

**Validation**: unique `id`; `gateway` a well-formed URL; at least one `issuer` and one `subscriber` reachable for a full demo. Loader fails fast with a clear message on duplicate id / malformed URL / missing required field.

---

## Entity 2 — Issuing Authority

The provisioned identity-assurance organisation. Not a file of its own — its identity is the `-AgencyName` value, and its concrete artefacts are recorded in the Demo State Record. Conceptual fields:

| Field | Source | Coherence rule |
|---|---|---|
| `agencyName` | `-AgencyName` (single source) | MUST equal the org name, register name, published-participant org name, and blueprint `x-review.header.issuerName`. |
| `organizationId` | created/reused on issuer node | one per agency name on a node. |
| `issuerWalletAddress` | created/reused | stable across renames → keeps the credential-issuer DID valid. |
| `analyst` | created/reused | Tier-3 Consumer user + wallet + published participant; the identity the approval agent acts as. |
| `registerId` | created/reused (advertised, DevMode) | the advertised register subscribers replicate. |
| `blueprintId` | published from template | carries the injected `issuerName`. |

**State (provision lifecycle)**: `Absent → Provisioning → Provisioned`. Idempotent re-provision: `Provisioned → (probe) → Reused` or `Provisioned → (rename) → Reprovisioned`. Reset: `Provisioned → Absent`.

---

## Entity 3 — Approval Agent Configuration

The chosen approval mechanism for a run. Rendered from a tokenised template into a working actor config (gitignored, written next to `state.json`).

| Field | Type | Notes |
|---|---|---|
| `mode` | enum `rules` \| `ai` \| `human` | from `-AgentMode`; default `rules`. |
| `actsAs` | analyst identity | wallet address + org + register, substituted from `state.json`. |
| `renderedConfigPath` | path | the materialised `analyst.<mode>.json` (for `rules`/`ai`). |
| `process` | tracked child (rules/ai) | launched `sorcha-agent run`; absent for `human`. |
| `aiGuardrail` | object (ai only) | `{ decisionWaitSeconds: 90, onTimeout: "surface-status" }`. |

**State**: `Unconfigured → Rendered → Running` (rules/ai) or `Unconfigured → InstructionsPrinted` (human). For `ai`: `Running → DecisionPending → (Decided | TimedOut→status-surfaced)`.

---

## Entity 4 — Demo State Record (`state.json`)

Per-run record of provisioned artefacts, enabling idempotency and reset. Written by `New-IssuingAuthority`/`Connect-Subscriber`, read by all four commands.

| Field | Type | Notes |
|---|---|---|
| `schemaVersion` | int | bump on shape change. |
| `issuerNodeId` | string | inventory id used as issuer. |
| `agencyName` | string | the single-source name. |
| `organizationId` | string | issuer org. |
| `issuerWalletAddress` | string | stable identity anchor. |
| `analystEmail` / `analystWallet` | string | approval-agent identity. |
| `registerId` | string | advertised register. |
| `blueprintId` | string | published blueprint. |
| `agentMode` | enum | last selected mode. |
| `subscribers` | array | one entry per connected subscriber: `{ nodeId, orgId, subscriptionId, status, lastReadyAt }`. |
| `provisionedAt` / `updatedAt` | iso8601 | audit. |

**Validation**: a present `state.json` whose `registerId` cannot be read on the issuer node marks the record **stale** → triggers R5 reconciliation rather than blind reuse.

---

## Derived: Readiness Verdict (computed, not stored)

`Get-DemoStatus` and `Connect-Subscriber`'s gate compute a verdict per (subscriber, register):

| Input signal | Source | Ready value |
|---|---|---|
| subscription | `GET /api/organizations/{orgId}/register-subscriptions/{registerId}` | `Active` |
| replication | `GET /api/registers/{id}/sync-state` | `CaughtUp` |
| service availability | `GET /api/registers/{id}/blueprints/published` | target blueprint present |
| approver (issuer) | tracked process / instructions | `rules`/`ai` running, or `human` acknowledged |

**Verdict** = `Ready` iff all subscriber signals true on at least one subscriber AND issuer reachable AND approver present; else `NotReady(reasonList)`. SC-007 requires this verdict to predict tester success in 100% of acceptance checks.

---

## File inventory (all gitignored except `*.example.json` / templates)

| File | Written by | Read by | Secret? |
|---|---|---|---|
| `demo-nodes.json` | operator (from example) | all commands | no (secrets in `deploy/keys.env`) |
| `deploy/keys.env` | operator (existing) | all commands | yes — never committed |
| `state.json` | provision/connect | all commands | no |
| `analyst.<mode>.json` (rendered) | `New-IssuingAuthority` | `sorcha-agent` | no (key via `ANTHROPIC_API_KEY` env) |
