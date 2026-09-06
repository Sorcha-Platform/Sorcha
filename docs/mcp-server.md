---
title: Sorcha MCP Server
description: Connection guide and worked-example session for the Sorcha MCP server — sorcha_-prefixed tools across admin, designer, participant, and citizen slices.
standards: [OAuth 2.0]
last_updated: 2026-09-05
---

# Sorcha MCP Server

The Sorcha Model Context Protocol (MCP) server is the entry point that lets an AI agent **act** on the platform — not just read about it. It exposes tools across four role slices — admin, designer, participant, and citizen — all driven by the same workflows that human operators and SDK callers use.

If you have the platform running, the manifest at `/.well-known/mcp.json` (relative to your gateway host) is the canonical machine-readable description: name, version, transports, authentication, per-slice counts, and a link to the flat tool catalogue. This document is the human companion. For a runnable, copy-pasteable walkthrough grounded directly in the tool source, see the [External-Agent MCP Quickstart](./guides/mcp-agent-quickstart.md).

## Overview

Sorcha is programmable proof infrastructure for multi-party workflows. Every action is wallet-signed, every record is Merkle-chained on an immutable register, every disclosure is cryptographically bounded. The MCP server lets an AI agent drive any of those workflows — issue a verified credential, design a blueprint, submit a participant action — without requiring the agent to learn each REST endpoint individually.

What you get from connecting:

- **A `sorcha_`-prefixed tool per operation**, across admin, designer, participant, and citizen (consumer-tier) slices. Each tool's `[Description]` attribute names what it does *and* when an agent should call it versus a sibling. The live roster changes over time — don't hand-count from this doc; `GET /api/mcp/tools` (or a live `tools/list`) and [`src/Apps/Sorcha.McpServer/README.md`](../src/Apps/Sorcha.McpServer/README.md) are the sources that can't drift.
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

Use this slice for an agent designing or refining workflows. Tools include `sorcha_blueprint_create` / `sorcha_blueprint_get` / `sorcha_blueprint_list` / `sorcha_blueprint_update` / `sorcha_blueprint_validate` / `sorcha_blueprint_simulate` / `sorcha_blueprint_export` (the full blueprint authoring lifecycle), `sorcha_schema_generate` and `sorcha_schema_validate` (JSON-Schema work), `sorcha_jsonlogic_test` (rule expression sandbox), `sorcha_disclosure_analysis` (selective-disclosure rule audit), `sorcha_workflow_instances` (running instances of a blueprint).

`sorcha_blueprint_diff` has been **withdrawn from the surface** (MCP-P0, 2026-09-05) — no `/diff`
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
