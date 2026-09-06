# MCP agent experience — design and evidence

**Date:** 2026-09-05
**Status:** Accepted; P0 in implementation
**Plans:** `docs/superpowers/plans/2026-09-05-mcp-p0-restore-surface.md` (P1/P2 plans follow once P0 lands)

## Why

Sorcha is being positioned so that external AI agents building systems of cooperation
find it and choose it. The MCP server is the surface those agents meet. This document
records what an external agent actually experiences today, measured rather than assumed,
and the decisions taken in response.

## How the evidence was gathered

Four independent methods, so no conclusion rests on a single reading:

1. **Two controlled cold-start experiments.** A fresh agent with no Sorcha knowledge, given
   only `llms.txt` and the tool catalogue, asked to set up a two-party data exchange with
   selective disclosure and a regulator-facing record. The second run added exactly one
   file — `blueprint.schema.json` — as the only changed variable.
2. **Live protocol probing** of `https://n1.sorcha.dev/mcp` (manifest, discovery documents,
   `initialize`, `tools/list`, and `tools/call` across three role categories).
3. **Container log inspection** on n1 for root cause.
4. **Static coverage sweep** of all 66 tools against the routes actually mapped in
   `src/Services/Sorcha.*.Service`, with a sample independently re-verified by hand.

## What was found

### The surface is advertised and non-functional

Every `tools/call` on the public endpoint returns `"An error occurred invoking 'X'."`
`initialize` and `tools/list` succeed, which is why it went unnoticed for 6+ days. Two
independent causes:

- **A stdio/HTTP registration seam.** `Program.cs` registers `IMcpSessionService` in the
  stdio branch and aliases `ICallerContext` to it. The HTTP branch registers only
  `ICallerContext -> HttpCallerContext`. Eleven tools still constructor-inject
  `IMcpSessionService`, so they cannot be activated over HTTP at all.
  **Measured: in all eleven, the dependency is declared, assigned, and never used.** The
  correct fix is therefore deletion, not registration — a stdio-shaped singleton has no
  place in a stateless HTTP server.
- **`ServiceAuth:ClientId not configured`.** `AddServiceClients` registers
  `IServiceAuthClient -> ServiceAuthClient` unconditionally, and that class throws **from
  its constructor** when the key is absent (`ServiceAuthClient.cs:60-61`).

**The second cause must not be fixed by adding secrets.** The MCP server is designed to
forward the *caller's* bearer (`CallerTokenForwardingHandler`, and the compose comment:
"backends are reached via the gateway so the forwarded caller token is authorized by the
platform (not by anonymous service-to-service trust)"). Giving it `ServiceAuth__ClientId`
and a secret would grant it ambient service-principal authority the design explicitly
refuses, adjacent to the open concern in #1380. A constructor that throws makes an
*optional* dependency mandatory for every host; that is the actual defect.

### Ten tools call routes that do not exist

Verified by hand for `/api/inbox`, bare `/api/workflows`, `/api/workflows/{id}`,
`/api/actions/{id}` and `/api/registers/{id}/data` — none are mapped. The pattern matters
more than the count: `sorcha_inbox_list`, `sorcha_action_details`, `sorcha_action_validate`
and `sorcha_workflow_status` — **the entire participant discovery loop** — all target
absent routes. Only `sorcha_action_submit` is correctly wired, and its own description
tells agents to use `sorcha_inbox_list` to obtain the ids it needs.

`sorcha_blueprint_diff` is different in kind: no `/diff` endpoint exists anywhere, so there
is nothing to repoint it at.

### Nothing tested it

`HttpTransportIntegrationTests` drives exactly one JSON-RPC `initialize`, to prove the auth
gate passes. It never calls a tool. This is the third instance in one working session of a
test exercising the path production does not fail on (after #1573's `Form.Schema` fixtures
and Feature 196's genesis fixtures). **The gate is the deliverable, not the fixes.**

### The server is tools-only

`initialize` reports `capabilities: {logging, tools}`. There are zero resources and zero
prompts in the codebase; `WithToolsFromAssembly()` is the only registration. Tools are
verbs. Resources are the nouns, prompts the recipes.

**The A/B settles that this is not theoretical.** Agent 1 could not express selective
disclosure at all — the platform's central promise, unreachable. Agent 2, given only
`blueprint.schema.json`, wrote it correctly and with judgement, withholding tenant contact
details from the contractor and job costs from the association. Its own assessment: *"the
schema is the only file that let me write syntactically real JSON rather than a
plausible-looking guess."*

Both runs scored **3/10** end-to-end. The schema closed the authoring gap and did nothing
for the lifecycle gap — which cleanly separates a documentation problem from a missing
capability problem.

### Measured tool-schema quality (57 role-scoped tools)

| Property | Count |
|---|---:|
| Parameters | 149 |
| ... with an `enum` | 0 |
| ... with a `format` | 0 |
| ... with an `example` | 0 |
| ... typed as `object` | 0 |
| Tools with `outputSchema` | 0 |
| Tools with `annotations` | 0 |
| `tools/list` payload | ~14.8k tokens |

Everything is a bare string, including `blueprintJson` and `dataJson` — the arguments
carrying all the meaning. No `outputSchema` means an agent cannot know what returns. No
annotations means it cannot distinguish `sorcha_transaction_revoke` from
`sorcha_register_query` without reading prose, which is a safety property when the agent is
choosing unsupervised. `ServerInstructions` — the one free-text teaching field — is eight
lines restating role names.

### An agent cannot complete the loop

| Step | Tool | Endpoint that exists |
|---|---|---|
| Create a register | none | `POST /api/registers/initiate` + `/finalize` (typed methods exist, unused) |
| Publish blueprint to register | none | `POST /api/registers/{id}/blueprints/publish` (typed method exists, uncalled) |
| Start a workflow instance | none | `POST /api/instances/` (no typed method at all) |

### The identity model is incoherent

- `sorcha:participant` gates **zero** tools; the nine participant tools are tier-gated only.
- Tools still return "Access denied. This tool requires the `sorcha:participant` role" — a
  message the code can no longer produce.
- The role normaliser recognises `admin|administrator|systemadmin`, `designer|...`,
  `participant|user|member`. The platform's five real roles are `SystemAdmin`,
  `Administrator`, `Designer`, `Auditor`, `Consumer`. **`Auditor` and `Consumer` match
  nothing** — and `Consumer` is the platform's name for a citizen participant.
- That normaliser is **duplicated** in `HttpCallerContext.cs:157` and
  `McpSessionService.cs:196` — two homes for one rule, against the codebase's own doctrine.

### Discovery is half-built

The manifest is well-formed and a genuine asset. But an unauthenticated `initialize`
returns 401 with an empty body and a bare `WWW-Authenticate: Bearer` — no
`resource_metadata`. `/.well-known/oauth-protected-resource` is absent.
`/.well-known/oauth-authorization-server` resolves but is the **OpenID4VCI** issuer
advertising only `pre-authorized_code`, which cannot mint an MCP token — worse than absent,
because it sends a spec-compliant client down a path that cannot work. The manifest's
`acquisition_url` points at a shell script.

Two catalogues also disagree: `/api/mcp/tools` (public, pre-auth, 65 terse one-liners,
hand-maintained in the gateway's `appsettings.json`) versus `tools/list` (57 rich,
role-scoped), against 66 in source. `ToolCatalogueProvider`'s own comment admits the drift
risk and defers the CI gate.

## Decisions

1. **Delete the unused `IMcpSessionService` dependency** from the eleven tools rather than
   registering it for HTTP. It is dead in all eleven; registering it would preserve a
   stdio-shaped coupling in a stateless server.
2. **Make `ServiceAuthClient` fail at use, not at construction.** Do not give the MCP server
   service-principal credentials. Caller-token forwarding is the intended and safer path.
3. **Repoint the broken tools at routes that exist**; where none exists
   (`sorcha_blueprint_diff`), remove the tool from the advertised surface. An advertised
   tool that cannot work is worse than an absent one, because an agent plans around it.
4. **Gate the surface with a test that calls every tool.** No fix in P0 is durable without
   it, and its absence is what allowed all of the above.
5. **Serve resources and prompts** (P1). Measured as the highest yield per unit of work.
6. **Close the lifecycle** with create-register, publish-blueprint, start-instance and
   grant-access tools (P1). Without these the ceiling stays at 3/10 whatever else improves.
7. **Enrich schemas, unify the catalogue, publish OAuth protected-resource metadata** (P2).
8. **Defer tool-surface trimming** (P3). ~14.8k tokens on connect is real but is not what
   stops anyone today.

## Scope boundaries

- Validation honesty (#1573 / #1605 / #1606) stays where it is. An agent never reaches
  submission today, so it cannot yet be bitten by it.
- `sorcha_wallet_sign` remains deliberately unregistered (spec 139 T029).
- Org wallet creation remains human-gated by design (#1525). The cold-start agent
  understood and respected that handoff; it is not a defect.
