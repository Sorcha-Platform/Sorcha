---
title: Sorcha MCP Server
description: Connection guide and worked-example session for the Sorcha MCP server — 36 tools across admin, designer, and participant slices.
standards: [OAuth 2.0]
last_updated: 2026-05-04
---

# Sorcha MCP Server

The Sorcha Model Context Protocol (MCP) server is the entry point that lets an AI agent **act** on the platform — not just read about it. It exposes 36 tools across three role slices, all driven by the same workflows that human operators and SDK callers use.

If you have the platform running, the manifest at `/.well-known/mcp.json` (relative to your gateway host) is the canonical machine-readable description: name, version, transports, authentication, per-slice counts, and a link to the flat tool catalogue. This document is the human companion.

## Overview

Sorcha is cryptographic proof infrastructure for multi-party workflows. Every action is wallet-signed, every record is Merkle-chained on an immutable register, every disclosure is cryptographically bounded. The MCP server lets an AI agent drive any of those workflows — issue a verified credential, design a blueprint, submit a participant action — without requiring the agent to learn each REST endpoint individually.

What you get from connecting:

- **36 tools.** 13 admin, 13 designer, 10 participant. Each tool's `[Description]` attribute names what it does *and* when an agent should call it versus a sibling.
- **JWT-bearer auth.** The same JWT used for direct API calls works for MCP. One token, two surfaces.
- **Two transports.** Stdio for local agent hosts (Claude Desktop, your own CLI agent), HTTP+SSE for hosted agents (cloud orchestrators, server-side workflow engines).

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

Every tool call requires a JWT. The Tenant Service is the JWT authority; obtain a token via the service-auth endpoint:

```bash
curl -X POST https://<your-host>/api/service-auth/token \
  -H "Content-Type: application/json" \
  -d '{
    "clientId": "<your-client-id>",
    "clientSecret": "<your-client-secret>"
  }'
```

The response carries `{ "token": "...", "expiresAt": "..." }`. Pass the token as `SORCHA_JWT_TOKEN` for stdio or as a `Bearer` header for HTTP+SSE.

A helper script lives at `scripts/get-jwt-token.sh` for development environments.

### Token scoping

Tokens carry the platform-org context. The slice an agent can drive depends on the token's claims:

| Slice | Required claim | Typical caller |
|---|---|---|
| admin | `role: SystemAdmin` or `role: Administrator` | Operator agent, observability orchestrator |
| designer | platform-user with org membership | Workflow-authoring agent, blueprint-design assistant |
| participant | platform-user with participant binding to a workflow instance | End-user agent acting on behalf of a participant |

A token without admin claims will receive 401 / 403 from admin tools and should fall through to the slice it does have access to. The manifest does not pre-filter — the server enforces at call time.

## Role slices

### Admin (13 tools)

Use this slice when an agent has admin scope and needs to inspect or operate a running instance. Tools include `health_check` (aggregated platform health), `metrics` (snapshot of platform metrics), `log_query` and `audit_query` (structured retrieval), `peer_status` and `validator_status` (P2P + consensus visibility), `register_stats` (per-register statistics), `tenant_create` / `tenant_list` / `tenant_update` (tenancy management), `user_list` / `user_manage` / `token_revoke` (user and credential management).

### Designer (13 tools)

Use this slice for an agent designing or refining workflows. Tools include `blueprint_create` / `blueprint_get` / `blueprint_list` / `blueprint_update` / `blueprint_validate` / `blueprint_simulate` / `blueprint_diff` / `blueprint_export` (the full blueprint authoring lifecycle), `schema_generate` and `schema_validate` (JSON-Schema work), `json_logic_test` (rule expression sandbox), `disclosure_analysis` (selective-disclosure rule audit), `workflow_instances` (running instances of a blueprint).

### Participant (10 tools)

Use this slice for an agent acting on behalf of an end-user participant in a running workflow. Tools include `inbox_list` (actions awaiting this participant), `action_details` / `action_validate` / `action_submit` (the action lifecycle), `wallet_info` and `wallet_sign` (cryptographic operations), `register_query` and `transaction_history` (read-side ledger access), `disclosed_data` (decrypt payloads disclosed to this participant), `workflow_status` (instance progress).

## Worked example — a participant agent driving the TradeFinance walkthrough

The TradeFinance walkthrough (`walkthroughs/TradeFinance/`) demonstrates a four-party workflow: supplier issues an invoice, buyer accepts, lender prices financing, payment settles. Below is a sketch of an MCP-driven session running it end-to-end. The full transcript will land alongside this doc in a follow-up commit.

```
1. inbox_list                    → returns the action awaiting the supplier participant
2. action_details                → reads the action's input schema + disclosure rules
3. wallet_sign                   → signs the action payload (the supplier's invoice)
4. action_submit                 → submits the signed action to the register
   ─ buyer participant ─
5. inbox_list                    → returns the buyer's pending action
6. wallet_sign + action_submit   → buyer accepts
   ─ lender participant ─
7. register_query                → reads the chain of accepted-invoice records
8. action_submit                 → lender posts a financing offer
   ─ verification ─
9. workflow_status               → confirms the workflow reached its terminal state
10. transaction_history          → audits every signed transition for the lender's records
```

A complete walkthrough transcript with input/output payloads will be added to `walkthroughs/TradeFinance/AGENTS.md` (see the `mcp-server` GitHub topic on this repository for related work).

## Where to read more

- **`/.well-known/mcp.json`** — live machine-readable manifest.
- **`/api/mcp/tools`** — flat catalogue with one entry per tool (name, category, short description). The full per-tool description (≥ 2 sentences with disambiguation per FR-017) lives on the running MCP server and is returned by the standard MCP `list_tools` request.
- **`STANDARDS.md`** — the standards posture every tool indirectly relies on (BIP32/39/44 wallet keys, ML-DSA signatures, OpenID4VC issuance, etc.).
- **[Architecture overview](./architecture.md)** — full system architecture.
- **[Quickstart](./quickstart.md)** — agent-runnable setup.
- **`walkthroughs/TradeFinance/`** and **`walkthroughs/AssuredIdentity/`** — runnable end-to-end demonstrations of the patterns the MCP tools drive.
