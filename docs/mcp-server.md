---
title: Sorcha MCP Server
description: Connection guide and worked-example session for the Sorcha MCP server — sorcha_-prefixed tools across admin, designer, participant, and citizen slices, plus its reference resources and guided prompts.
standards: [OAuth 2.0]
last_updated: 2026-09-07
---

# Sorcha MCP Server

The Sorcha Model Context Protocol (MCP) server is the entry point that lets an AI agent **act** on the platform — not just read about it. It exposes tools across four role slices — admin, designer, participant, and citizen — all driven by the same workflows that human operators and SDK callers use.

If you have the platform running, the manifest at `/.well-known/mcp.json` (relative to your gateway host) is the canonical machine-readable description: name, version, transports, authentication, per-slice counts, and a link to the flat tool catalogue. This document is the human companion. For a runnable, copy-pasteable walkthrough grounded directly in the tool source, see the [External-Agent MCP Quickstart](./guides/mcp-agent-quickstart.md).

## Overview

Sorcha is programmable proof infrastructure for multi-party workflows. Every action is wallet-signed, every record is Merkle-chained on an immutable register, every disclosure is cryptographically bounded. The MCP server lets an AI agent drive any of those workflows — issue a verified credential, design a blueprint, submit a participant action — without requiring the agent to learn each REST endpoint individually.

What you get from connecting:

- **A `sorcha_`-prefixed tool per operation**, across admin, designer, participant, and citizen (consumer-tier) slices — including three lifecycle tools (`sorcha_register_create`, `sorcha_blueprint_publish`, `sorcha_instance_create`) that close the loop from an empty workspace to a running, ledger-backed workflow. Each tool's `[Description]` attribute names what it does *and* when an agent should call it versus a sibling. The live roster changes over time — don't hand-count from this doc; `GET /api/mcp/tools` (or a live `tools/list`) and [`src/Apps/Sorcha.McpServer/README.md`](../src/Apps/Sorcha.McpServer/README.md) are the sources that can't drift.
- **Reference resources and guided prompts.** `sorcha://schema/blueprint`, worked-example blueprints, a glossary, and the caller's own registers/instances are servable as MCP resources — no tool call needed. Three prompts (`sorcha_two_party_exchange`, `sorcha_issue_credential`, `sorcha_prove_to_regulator`) return a step-by-step brief for the most common flows. See [Resources](#resources) and [Prompts](#prompts) below.
- **JWT-bearer auth.** The same JWT used for direct API calls works for MCP. One token, two surfaces.
- **Two transports.** Stdio for local agent hosts (Claude Desktop, your own CLI agent), and **Streamable HTTP** for hosted agents (cloud orchestrators, server-side workflow engines) — served stateless behind the gateway's `/mcp` route, so it scales horizontally with no session affinity. (The manifest labels this transport `http+sse` — that's the FR-014 wire name, kept for compatibility; the endpoint speaks Streamable HTTP.)

## Connecting

### Stdio (local agents)

Stdio is the right transport for an AI agent running on the same machine as the MCP server — for example, an IDE-embedded coding assistant, a Claude Desktop plugin, or a local CLI agent.

```jsonc
// claude_desktop_config.json or equivalent
{
  "mcpServers": {
    "sorcha": {
      "command": "dotnet",
      "args": ["run", "--project", "src/Apps/Sorcha.McpServer"],
      "env": {
        "SORCHA_JWT_TOKEN": "eyJhbGciOi..."
      }
    }
  }
}
```

For Docker:

```bash
docker-compose run mcp-server --jwt-token <token>
```

### HTTP+SSE (hosted agents)

HTTP+SSE is the right transport for a cloud-hosted agent (no local process control) or a server-side workflow engine that wants to stream tool results.

```bash
curl -N \
  -H "Authorization: Bearer eyJhbGciOi..." \
  -H "Accept: text/event-stream" \
  https://<your-host>/mcp/sse
```

The exact `http+sse` URL is in the manifest's `transports[1].url` field.

## Authentication

### JWT acquisition flow

Every tool call requires a JWT. The Tenant Service is the JWT authority. An external agent authenticates
as a human-owned (or agent-operated) account with the `password` grant — the same way a person signs in:

```bash
curl -s -X POST https://<your-host>/api/tenant/auth/login \
  -H "Content-Type: application/json" \
  -d '{"email":"<account-email>","password":"<account-password>"}' \
  | jq -r '.accessToken'
```

`POST /api/service-auth/token` accepts the same `password` grant (plus `refresh_token`) if you'd rather
speak OAuth2 form/JSON fields directly. **`client_credentials` is not available on either the public
gateway or that endpoint** — service-token minting moved to the internal-only
`POST /api/internal/service-auth/token` after a real external-facing signing-oracle finding (issue #1397,
PR #1407). The API Gateway has no route for `/api/internal/*`, so that endpoint is unreachable from
outside the platform's Docker network — an external agent cannot reach it no matter what credentials it
holds. If your agent needs to act as a specific workflow participant, it authenticates as that
participant's own account with a password, not a service client secret.

Pass the resulting token as `SORCHA_JWT_TOKEN` for stdio or as a `Bearer` header for Streamable HTTP.

A helper script wraps the same call: `./scripts/get-jwt-token.sh -e <email> -p <password>` (development environments).

### Token scoping

Tokens carry the platform-org context. The slice an agent can drive depends on the token's claims:

| Slice | Required claim | Typical caller |
|---|---|---|
| admin | `role: SystemAdmin` or `role: Administrator` | Operator agent, observability orchestrator |
| designer | platform-user with org membership | Workflow-authoring agent, blueprint-design assistant |
| participant | platform-user with participant binding to a workflow instance | End-user agent acting on behalf of a participant |
| citizen | consumer-tier token (F136) — no role claim required | An agent acting on behalf of an end-user's own wallet, devices, credentials, or persona |

A token without admin claims will receive 401 / 403 from admin tools and should fall through to the slice it does have access to. The manifest does not pre-filter — the server enforces at call time.

## Role slices

### Admin

Use this slice when an agent has admin scope and needs to inspect or operate a running instance. Tools include `sorcha_health_check` (aggregated platform health), `sorcha_metrics` (snapshot of platform metrics), `sorcha_log_query` and `sorcha_audit_query` (structured retrieval), `sorcha_peer_status` and `sorcha_validator_status` (P2P + consensus visibility), `sorcha_register_stats` (per-register statistics), `sorcha_tenant_create` / `sorcha_tenant_list` / `sorcha_tenant_update` (tenancy management), `sorcha_user_list` / `sorcha_user_manage` / `sorcha_token_revoke` (user and credential management). It also covers register federation (subscribe/unsubscribe/sync-state/relationship) and credential lifecycle (offer/suspend/reinstate/revoke/refresh). This list is illustrative, not exhaustive — it's the largest slice by tool count; see the per-slice table in [`src/Apps/Sorcha.McpServer/README.md`](../src/Apps/Sorcha.McpServer/README.md) for the current breakdown.

`sorcha_user_list` and `sorcha_user_manage` are **org-scoped** — both require an `organizationId`
argument, matching the real `api/organizations/{organizationId}/users` routes (MCP-P0, 2026-09-05;
the earlier flat `api/users` shape these tools targeted was never mapped by any service).
`sorcha_user_manage`'s action vocabulary is `Suspend | Reactivate | Unlock | ChangeRole` — there is
no `Activate` / `Deactivate` / `Lock` / `AddRole` / `RemoveRole` endpoint to call.

### Designer

Use this slice for an agent designing or refining workflows. Tools include `sorcha_blueprint_create` / `sorcha_blueprint_get` / `sorcha_blueprint_list` / `sorcha_blueprint_update` / `sorcha_blueprint_validate` / `sorcha_blueprint_simulate` / `sorcha_blueprint_export` (the full blueprint authoring lifecycle), `sorcha_schema_generate` and `sorcha_schema_validate` (JSON-Schema work), `sorcha_jsonlogic_test` (rule expression sandbox), `sorcha_disclosure_analysis` (selective-disclosure rule audit), `sorcha_workflow_instances` (running instances of a blueprint), `sorcha_blueprint_publish` (put a draft blueprint live on a register), `sorcha_instance_create` (start a workflow instance) and `sorcha_register_create` (create the register a workflow writes to). The last two of those are listed here because they are designer-workflow steps, but both carry the admin role — see the role note below.

`sorcha_register_create` is the one tool on this slice that **carries the `sorcha:admin` role, not `sorcha:designer`**, and the one that **requires a real person's confirmation**. The role is not a policy choice: `POST /api/registers/initiate` sits behind the Register Service's `CanManageRegisters` policy (`org_id` plus `Administrator` or `SystemAdmin`), and MCP entitlement matches roles exactly — `sorcha:admin` does not imply `sorcha:designer`. Entitling it on the designer role would let a plain designer clear every local gate, interrupt a person for approval, and only then collect an opaque 403; nobody may be asked to approve something that cannot succeed. A plain designer therefore does not see the tool at all. It runs the two-phase owner-attestation ceremony (initiate → sign → finalize) as a single call, and asks the caller's MCP client to put the decision to a human via elicitation *before* it touches the register service — the pending registration has a hard five-minute TTL, so a person's thinking time must not be spent against it. A client that does not declare the `elicitation` capability (form mode) is refused with `Status: ApprovalRequired` and nothing is created. The owning wallet is the caller's own organisation wallet, resolved from the token's `org_id` claim rather than taken as an argument, and the attestation is signed under `sorcha:register-attestation` — the organisation's governance key. Neither value is selectable by the agent: a wrong one produces a working register whose governance roster records a key the validator will never match, silently. If the organisation has no wallet yet the tool says so and stops (#1525) — an org admin must create and link one, because the recovery phrase is shown once and must reach a person. That message is emitted **only** when the organisation was read successfully and genuinely carries no `walletAddress`; a Tenant read that failed (401 / 403 / 404 / 500 all collapse to the same null in the typed client) reports plainly that the organisation could not be read and that nothing was created, rather than sending an admin off to create a wallet that may already exist.

`sorcha_blueprint_publish` carries **`sorcha:admin`** for the same reason, and it is worth stating separately because the endpoint's policy *looks* satisfiable without it. `POST /api/blueprints/{id}/publish` sits behind the Blueprint Service's `CanPublishBlueprints` policy, which accepts a `can_publish_blueprint=true` claim **or** the `Administrator` / `SystemAdmin` role — but the Tenant Service's `TokenService` never emits that claim, so an admin role is the only way through in practice. (`SystemAdmin` was added in this task: the policy previously accepted only the literal `Administrator`, so a SystemAdmin passed the MCP entitlement — `McpRoleNormalizer` maps `SystemAdmin` to `sorcha:admin` — and was then refused deterministically by the middleware. That refusal reaches the tool as the same null as every other failure, and the null path calls `RecordFailure`, so **three attempts tripped the availability breaker and disabled every Blueprint MCP tool** behind a false "service is currently unavailable". The widening aligns the policy with the shared `RequireAdministrator` and the Register Service's `CanManageRegisters`, both of which already accepted either role.) A second, register-scoped gate follows it: `PublishGate` requires an `Owner` / `Admin` / `Designer` entry for the caller on the target register's governance roster, matched on wallet address or org id rather than on JWT roles, so MCP entitlement cannot assert it locally at all.

The tool's real subject is the **rehearsal soft gate**. `PublishGate` blocks every publish whose executable-definition hash has no matching `RehearsalPass`, so an agent's freshly authored blueprint always hits `409 REHEARSAL_REQUIRED`. The rehearsal is the only *behavioural* check a definition gets before it goes live — it is what would catch, for example, a blueprint that issues a credential to a declined applicant (#1551). The tool therefore attempts the publish with **no** override, and on the 409 asks the caller's MCP client to put the decision to a person, naming the blueprint, the register, what has never been executed, and that the waiver is audited against their account. It sends `override: { confirm: true, reason }` **only** on an explicit approval; a decline returns `Status: RehearsalRequired` and a client that cannot elicit returns `Status: ApprovalRequired`, and neither publishes anything. Nobody is asked to approve something that cannot succeed, and that property comes from the server's own ordering rather than a local pre-check: `PublishGate` evaluates the governance hard gate first, so a caller without register publish rights is refused on the first, override-free attempt — long before the elicit. The same ordering is what lets the tool be precise about failures: the typed client collapses 403, 404, the 400 a publish-validation failure produces, 5xx and a transport fault into one `null`, so a first-attempt failure says plainly which of those it cannot distinguish, while a failure on the override retry can rule authorisation out — the 409 proved governance had already passed.

`sorcha_blueprint_diff` has been **deleted** (#1607; withdrawn from the surface MCP-P0, 2026-09-05) — no `/diff`
endpoint exists on any service to repoint it to, and an advertised-but-broken tool is worse than no
tool. Tracked for removal of the now-dead client method: issue #1607.

### Participant

Use this slice for an agent acting on behalf of an end-user participant in a running workflow. Tools include `sorcha_inbox_list` (actions awaiting this participant), `sorcha_action_details` / `sorcha_action_validate` / `sorcha_action_submit` (the action lifecycle), `sorcha_wallet_info` (wallet/address lookup), `sorcha_register_query` and `sorcha_transaction_history` (read-side ledger access), `sorcha_disclosed_data` (decrypt payloads disclosed to this participant), `sorcha_workflow_status` (instance progress).

`sorcha_action_details` takes **both an instance id and an action id** (`instanceId`, `actionId`) —
it reads `GET /api/instances/{instanceId}/actions/{actionId}`, not a bare action id. `sorcha_action_validate`
takes `blueprintId`, `actionId`, and `dataJson`, posting to `POST /api/execution/validate`; it validates
against the blueprint's **latest published definition**, not an instance's pinned one (issue #1606) —
an agent checking a running instance's own pinned version should not treat a pass here as final.

Signing an action is **implicit inside `sorcha_action_submit`** — there is no separate "sign, then submit"
step for an agent to orchestrate. A `sorcha_wallet_sign` tool exists in source
(`src/Apps/Sorcha.McpServer/Tools/Participant/WalletSignTool.cs`) but is deliberately **not registered**
(spec 139 T029): it is intentionally omitted from `[McpServerToolType]` discovery, so it never reaches
`/api/mcp/tools`, the manifest catalogue, or a live `tools/list`. Direct signing is a high-risk operation
reserved for a dedicated, security-reviewed wave. Don't call it or document a manual sign-then-submit step.

### Citizen (consumer tier)

Use this slice for an agent acting on behalf of an end-user's own wallet, devices, credentials, or persona — gated on a consumer-tier token (F136), not a role claim. Tools include `sorcha_my_credentials`, `sorcha_my_devices` / `sorcha_my_device_rename` / `sorcha_my_device_revoke`, `sorcha_my_persona`, `sorcha_my_presentations`, `sorcha_my_invitations`, and `sorcha_pending_applications`.

## Resources

Resources are read directly — no tool call, no round trip, no rate-limit spend. The server was tools-only (zero resources) until this surface was added; a cold-start authoring A/B measured `sorcha://schema/blueprint` as the single intervention that closed the authoring gap.

| URI | Returns |
|---|---|
| `sorcha://schema/blueprint` | The embedded blueprint JSON Schema. Accurate and current for what it documents (participants, actions, data schemas, disclosure groups, action-level `condition` routing) — but **incomplete, not wrong**: it does not yet define `routes`, `isStartingAction`, `credentialRequirements`, `credentialIssuanceConfig`, `rejectionConfig`, `requiredPriorActions`, or `instanceReference`. Read this before writing any blueprint JSON. |
| `sorcha://examples/{name}` | A complete, working blueprint the walkthrough suite actually executes, using `routes` + `isStartingAction` — the constructs the schema above doesn't yet define. Names: `assured-identity` (credential issuance with selective disclosure and `credentialIssuanceConfig`), `encryption-at-rest` (encrypted payloads and disclosure groups), `ping-pong` (the minimal two-party exchange). |
| `sorcha://glossary` | What register, blueprint, action, participant, disclosure group, docket, `publicationTxId` and `execDefHash` mean. |
| `sorcha://registers` | The caller's visible registers (their organisation's, plus system registers), newest first, as JSON. Capped at 50 — the response carries `count` and `truncated`, so a register's absence from the list is **not** evidence it doesn't exist when `truncated` is true. |
| `sorcha://instances` | The workflow instances visible to the calling identity right now, as JSON. |

Both live-state resources (`sorcha://registers`, `sorcha://instances`) require authentication and return a `note` explaining why when the caller isn't signed in, rather than an empty list that reads as "there are none".

The blueprint-schema gap is tracked as issue #1609 — the schema has drifted from the current model and needs a currency gate; the three worked examples are the reference for the missing constructs until it's fixed, complementary to the schema rather than a correction of it.

## Prompts

Prompts are guided recipes: each returns a step-by-step brief naming the resources to read and the lifecycle tools to call, in order. A prompt never calls a tool itself — the agent still drives every step.

| Prompt | Guides |
|---|---|
| `sorcha_two_party_exchange` | Setting up a two-party data exchange with selective disclosure, from an empty workspace to a running instance. |
| `sorcha_issue_credential` | Issuing a verifiable credential from an issuer organisation to a subject via an action-level `credentialIssuanceConfig`, including the OID4VCI offer path (`sorcha_credential_offer`) when the subject holds a standards-compliant external wallet rather than a Sorcha participant identity. |
| `sorcha_prove_to_regulator` | Assembling verifiable proof of a sealed transaction — inclusion proof, verification bundle, and any data the regulator is entitled to see — for a regulator or auditor. |

## Human approval

Two of the lifecycle tools above — `sorcha_register_create` always, `sorcha_blueprint_publish` only on an unrehearsed definition — put the decision to a real person via MCP elicitation rather than letting the agent decide alone: creating a register is irreversible and establishes the keys that authorise every later administrative change, and publishing an unrehearsed definition skips the only *behavioural* check a blueprint gets before it goes live. `IHumanApproval` / `ElicitationHumanApproval` (`src/Apps/Sorcha.McpServer/Services/`) is the **only** place `ElicitAsync` is called, and it resolves to **three** client states, not two:

| State | When | What the agent sees |
|---|---|---|
| Not supported | The connecting client never declared the `elicitation` capability (form mode) at `initialize` | Refused before anything is created — connect with a client that supports elicitation, or perform this step in the Sorcha UI |
| Refused | A person explicitly declined, or the client dismissed the request without a choice — **including a client that declares the capability but auto-cancels every request when running headlessly** (Claude Code in `-p` mode does exactly this) | Refused — nothing changed |
| Approved | An explicit `accept` | The **only** outcome that proceeds |

Declaring the `elicitation` capability at `initialize` is therefore not a promise a person will actually be asked — a headless client can declare it and still auto-refuse every request. Both tools fail closed in every environment, with no bypass flag.

## Worked example — a participant agent driving the TradeFinance walkthrough

The TradeFinance walkthrough (`walkthroughs/TradeFinance/`) demonstrates a four-party workflow: supplier issues an invoice, buyer accepts, lender prices financing, payment settles. Below is a sketch of an MCP-driven session running it end-to-end. The full transcript will land alongside this doc in a follow-up commit.

```
1. sorcha_inbox_list             → returns the action awaiting the supplier participant
2. sorcha_action_details         → reads the action's input schema + disclosure rules
                                    (takes both the instance id and the action id)
3. sorcha_action_submit          → submits the supplier's invoice; signing happens
                                    implicitly inside this call (see "Participant" above)
   ─ buyer participant ─
4. sorcha_inbox_list             → returns the buyer's pending action
5. sorcha_action_submit          → buyer accepts
   ─ lender participant ─
6. sorcha_register_query         → reads the chain of accepted-invoice records
7. sorcha_action_submit          → lender posts a financing offer
   ─ verification ─
8. sorcha_workflow_status        → confirms the workflow reached its terminal state
9. sorcha_transaction_history    → audits every signed transition for the lender's records
```

A complete walkthrough transcript with input/output payloads will be added to `walkthroughs/TradeFinance/AGENTS.md` (see the `mcp-server` GitHub topic on this repository for related work).

## Where to read more

- **[External-Agent MCP Quickstart](./guides/mcp-agent-quickstart.md)** — the runnable, copy-pasteable companion to this page: node selection, manifest, JWT, connecting, and a source-grounded step-by-step tool sequence.
- **`/.well-known/mcp.json`** — live machine-readable manifest.
- **`/api/mcp/tools`** — flat catalogue with one entry per tool (name, category, short description). The full per-tool description (≥ 2 sentences with disambiguation per FR-017) lives on the running MCP server and is returned by the standard MCP `list_tools` request.
- **`STANDARDS.md`** — the standards posture every tool indirectly relies on (BIP32/39/44 wallet keys, ML-DSA signatures, OpenID4VC issuance, etc.).
- **[Architecture overview](./architecture.md)** — full system architecture.
- **[Quickstart](./quickstart.md)** — agent-runnable setup.
- **`walkthroughs/TradeFinance/`** and **`walkthroughs/AssuredIdentity/`** — runnable end-to-end demonstrations of the patterns the MCP tools drive.
