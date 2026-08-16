# External-Agent MCP Quickstart

This is the runnable companion to [`docs/mcp-server.md`](../mcp-server.md) — read that first for the
full transport / authentication / role-slice reference. This page is one concrete, copy-pasteable path
an **external** autonomous agent (not a Sorcha internal service, not a human clicking through the UI)
can follow from "I have a node address" to "I drove a workflow action to a sealed transaction."

Everything below is grounded in the current MCP server source
(`src/Apps/Sorcha.McpServer/Tools/`) and the discoverability endpoints it serves — not aspirational.
Where the source and an older doc disagreed, this page follows the source (see "Known doc drift" at
the bottom).

## Before you start — pick a node

| Option | Command |
|---|---|
| Self-hosted (your own Docker stack) | Follow the [one-line installer](../../README.md#try-it-in-one-line) or [`docs/quickstart.md`](../quickstart.md) first — you need a running gateway at `http://localhost` before anything below works. |
| Shared sandbox | `https://n1.sorcha.dev` — a live, public, periodically-wiped demo node. No setup required, but see [`SECURITY.md`](https://github.com/Sorcha-Platform/Sorcha/blob/master/SECURITY.md) before you put anything sensitive through it. |

The examples below use `http://localhost` for a self-hosted stack. Swap in `https://n1.sorcha.dev` (or
your own node's origin) throughout.

## Step 1 — Read the manifest

`GET /.well-known/mcp.json` is a live, per-installation description of how to connect — it is the
authoritative source for the exact token issuer/audience and the deployed transport URL, so prefer it
over hardcoding values from this doc:

```bash
curl -s http://localhost/.well-known/mcp.json | jq
```

```jsonc
{
  "name": "Sorcha MCP Server",
  "transports": [
    { "type": "stdio", "command": "dotnet", "args": ["run", "--project", "src/Apps/Sorcha.McpServer"] },
    { "type": "http+sse", "url": "http://localhost/mcp" }
  ],
  "authentication": {
    "type": "jwt-bearer",
    "issuer": "urn:sorcha:sorcha",
    "audience": "sorcha:platform",
    "acquisitionUrl": "http://localhost/api/tenant/auth/login"
  },
  "toolCatalogueUrl": "http://localhost/api/mcp/tools"
}
```

(`http+sse` is the wire name kept for backward compatibility — the endpoint actually speaks
Streamable HTTP. See `docs/mcp-server.md` for the transport-selection rationale.)

## Step 2 — Get a JWT

Every tool call needs a bearer JWT from the Tenant Service — the same token you'd use for a direct
REST call.

**Use the `password` grant, not `client_credentials`.** The public gateway's
`POST /api/service-auth/token` endpoint only accepts the `password` and `refresh_token` grant types.
`client_credentials` service-token minting is deliberately **not** reachable from outside the
platform — it moved to an internal-only route after a real external-facing signing-oracle finding
(issue #1397, see [`SECURITY.md`](https://github.com/Sorcha-Platform/Sorcha/blob/master/SECURITY.md)). If your agent needs to act as a specific
workflow participant, it authenticates as that participant's human-owned (or agent-operated) account
with a password, exactly like a person would.

```bash
curl -s -X POST http://localhost/api/tenant/auth/login \
  -H "Content-Type: application/json" \
  -d '{"email":"admin@sorcha.local","password":"Dev_Pass_2025!"}' \
  | jq -r '.accessToken'
```

On a fresh self-hosted stack, the setup script prints an admin account you can use immediately (see
README → [Default Credentials](../../README.md#default-credentials) — **change it before exposing the
node**). On `n1.sorcha.dev`, sign up for your own account rather than trying to reuse someone else's.

A helper script wraps the same call:

```bash
./scripts/get-jwt-token.sh -e admin@sorcha.local -p 'Dev_Pass_2025!'
```

Which tools your token can call depends on its claims (role for admin/designer, participant binding
for participant tools, consumer-tier for citizen tools) — see the token-scoping table in
[`docs/mcp-server.md`](../mcp-server.md#authentication).

## Step 3 — Point the MCP server at the node

**Local stdio (the `docker-compose run` path from `CLAUDE.md`):**

```bash
docker-compose run --rm mcp-server --jwt-token <your-jwt-token>
```

This is the right shape when your agent host controls the local process (a CLI agent, an
IDE-embedded assistant, Claude Desktop on the same machine as the Docker stack). It talks stdio —
one connection at a time.

**Remote HTTP (the shape for a hosted/cloud agent, e.g. talking to `n1.sorcha.dev`):**

```bash
curl -N \
  -H "Authorization: Bearer <your-jwt-token>" \
  -H "Accept: text/event-stream" \
  http://localhost/mcp
```

Use the exact URL from the manifest's `transports[].url` (Step 1) rather than assuming `/mcp` —
it's derived per-deployment.

## Step 4 — Discover the tools

```bash
curl -s http://localhost/api/mcp/tools | jq
```

Returns one entry per tool: name, category, and short description. The live count and exact roster
change over time (new tools land regularly) — **don't hand-count from any document, including this
one**; the catalogue endpoint and a live `tools/list` MCP request are the only two sources that can't
drift. Every tool name is prefixed `sorcha_` (e.g. `sorcha_inbox_list`, `sorcha_action_submit`) — this
guide uses the real prefixed names throughout.

`tools/list` on a live session is tier-filtered: a platform-tier token sees the admin + designer +
participant tools it has role claims for; a consumer-tier token sees only the citizen slice.

## Step 5 — Drive one workflow to completion

This assumes a workflow **instance already exists** and has an action waiting on the participant your
agent's token represents — for example, one started via the Blueprint Designer UI, the CLI, or a
prior designer-slice MCP session (`sorcha_blueprint_create` + publishing to a register — see
[Blueprint Quick Start](../getting-started/blueprint-quick-start.md) if you need to create the
workflow too). The [README's Invoice Approval example](../../README.md#1-define-a-blueprint) is a
minimal two-participant blueprint you can use as the target.

```
1. sorcha_inbox_list                        → list actions assigned to this participant.
                                               Returns actionInstanceId, workflowInstanceId,
                                               actionId (numeric), actionTitle, status.

2. sorcha_action_details(actionInstanceId)  → the action's JSON input schema, prompt copy, and
                                               any upstream data disclosed to this participant.

3. sorcha_action_submit(workflowInstanceId, actionId, dataJson)
                                             → submits the payload, completes the action, and
                                               advances the workflow. Signing happens implicitly
                                               inside this call — there is no separate "sign, then
                                               submit" step for an agent to orchestrate. (Direct
                                               signing exists in source as sorcha_wallet_sign but is
                                               deliberately unregistered — spec 139 T029 — so it is
                                               not part of the served tool surface.)

4. sorcha_workflow_status(workflowInstanceId)
                                             → confirms the instance advanced: which actions
                                               completed, which are now pending, and for whom.

5. sorcha_transaction_history(...)          → the signed, chained record of what was just
                                               submitted — the audit trail, not the live view.
```

For a two-participant blueprint (like the README example), a second agent session — authenticated as
the approver — repeats steps 1-3 against the action that `sorcha_action_submit` in step 3 triggered,
then step 4 shows the instance in its terminal state.

## What success looks like

- `sorcha_action_submit`'s response includes a `transactionId` and, if the workflow continues, a
  `nextActions` list naming the action(s) it triggered.
- `sorcha_workflow_status` on the same `workflowInstanceId` shows the action you just completed is no
  longer pending, and (once every action in the blueprint has completed) the instance in its terminal
  state.
- `sorcha_transaction_history` shows the new transaction, sealed and chained — the cryptographic,
  immutable evidence that the action happened. That sealed transaction, not the HTTP 200 from the
  submit call, is what "the action really committed" means on Sorcha (see the DAD model in the
  [README](../../README.md#the-dad-security-model): *Alteration* is exactly this — every data change
  is a signed transaction on an immutable ledger).

## Verify

**This sequence has not been executed end to end as part of writing this doc** — there is no live
stack available in the environment that produced it. It is derived directly from the registered MCP
tool source (`src/Apps/Sorcha.McpServer/Tools/Participant/*.cs`), cross-checked against
`docs/mcp-server.md` and the tool table in `src/Apps/Sorcha.McpServer/README.md`. Running this guide
against a fresh Docker stack — confirming each curl/tool call returns what's shown above, and that a
real transaction seals — is the acceptance step for this doc. If a step doesn't match reality when you
run it, fix this page (see the Documentation Sync Policy in `CLAUDE.md`) rather than working around it
silently.

## Troubleshooting

| Symptom | Likely cause |
|---|---|
| `401`/`403` from a tool call | Your token's tier or role doesn't cover that tool — see the scoping table in `docs/mcp-server.md`. |
| "JWT token is required" from the MCP server process | Token wasn't passed via `--jwt-token` or `SORCHA_JWT_TOKEN`. |
| Connection refused | Services aren't up yet — `docker-compose ps`, then `docker-compose logs -f <service>`. See [`docs/quickstart.md`](../quickstart.md) for the full failure-mode list. |
| `sorcha_action_submit` succeeds but the workflow doesn't advance | Check the response's `nextActions` — an empty list on a non-terminal action usually means a routing condition wasn't met, not that the submission failed. |

## Known doc drift found while writing this guide

Two things this guide deliberately does **not** repeat from `docs/mcp-server.md`, because the current
tool source disagrees with it:

- Tool names there are shown unprefixed (`inbox_list`, `action_submit`); the actual registered names
  all carry a `sorcha_` prefix (`sorcha_inbox_list`, `sorcha_action_submit`).
- Its worked example shows an explicit `wallet_sign` step before submitting an action. That tool
  (`sorcha_wallet_sign`) exists in source but is intentionally unregistered (spec 139 T029) and is not
  part of the served surface — `sorcha_action_submit` signs implicitly.

## Where to read more

- [`docs/mcp-server.md`](../mcp-server.md) — full transport, authentication, and role-slice reference.
- [`docs/quickstart.md`](../quickstart.md) — agent-runnable setup against a clean Docker host.
- [`walkthroughs/McpServerBasics/`](../../walkthroughs/McpServerBasics/) — a scripted stdio smoke test
  (auth + connect + a health-check tool call) this guide's Steps 2-4 are grounded in.
- [`src/Apps/Sorcha.McpServer/README.md`](../../src/Apps/Sorcha.McpServer/README.md) — the source-level
  reference, including the current tool count by slice (don't hand-count; it says the same).
