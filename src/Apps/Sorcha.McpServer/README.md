# Sorcha MCP Server

A Model Context Protocol (MCP) server for the Sorcha decentralised register platform. This server enables AI assistants like Claude Desktop to interact with Sorcha's Blueprint, Register, Wallet, and other services through a standardized protocol.

## Overview

The MCP server provides role-based access to Sorcha platform operations through a set of tools organized by user role:

- **Administrator (`sorcha:admin`)**: platform health, logs, metrics, org/user admin, register federation, credential lifecycle, presentations
- **Designer (`sorcha:designer`)**: blueprint creation, validation, simulation, versioning
- **Participant (`sorcha:participant`)**: inbox, actions, transactions, wallet operations
- **Citizen (consumer tier)**: self-service wallet, devices, credentials, persona — gated on a consumer-tier token (F136), not a role claim

## Features

- **JWT Authentication**: Secure access using JWT bearer tokens from Tenant Service
- **Role-Based Authorization**: Tools are filtered based on user's assigned roles
- **Rate Limiting**: Protects backend services from excessive API calls
- **Audit Logging**: Tracks all tool invocations for security and compliance
- **Service Discovery**: Automatically connects to Sorcha backend services
- **Two Transports** (spec 139): stdio for local AI-assistant integration, and **stateless Streamable HTTP** served by `mcp-server-http` behind the gateway's `/mcp` route — probe `GET /.well-known/mcp.json` for the per-installation URL
- **Discoverability**: the gateway serves `/.well-known/mcp.json` (manifest: version, transports, real JWT issuer/audience for the installation) and `/api/mcp/tools` (flat catalogue). Both are guarded against drift by `ManifestIntegrityTests`

## Running with Docker

### Using docker-compose

The MCP server is included in the docker-compose configuration with the `tools` profile:

```bash
# Run MCP server with JWT token
docker-compose run mcp-server --jwt-token <your-jwt-token>

# Or use environment variable
SORCHA_JWT_TOKEN=<your-jwt-token> docker-compose run mcp-server
```

### Building the Docker image

```bash
# Build the image
docker-compose build mcp-server

# Run interactively
docker-compose run --rm mcp-server --jwt-token <token>
```

## Running Locally (Development)

```bash
# Navigate to project directory
cd src/Apps/Sorcha.McpServer

# Run with JWT token
dotnet run -- --jwt-token <your-jwt-token>

# Or set environment variable
export SORCHA_JWT_TOKEN=<your-jwt-token>
dotnet run
```

## Configuration

The MCP server uses standard .NET configuration with the following sources (in order of precedence):

1. Command-line arguments (`--jwt-token`)
2. Environment variables (prefix: `SORCHA_`)
3. `appsettings.{Environment}.json`
4. `appsettings.json`

### Key Configuration Sections

#### Service Clients

```json
{
  "ServiceClients": {
    "BlueprintService": {
      "Address": "http://blueprint-service:8080"
    },
    "RegisterService": {
      "Address": "http://register-service:8080"
    },
    "WalletService": {
      "Address": "http://wallet-service:8080"
    },
    "TenantService": {
      "Address": "http://tenant-service:8080"
    },
    "ValidatorService": {
      "Address": "http://validator-service:8080"
    }
  }
}
```

#### Rate Limiting

MCP rate limits use the centralised `RateLimiting` section from `RateLimitSettings` in ServiceDefaults:

```json
{
  "RateLimiting": {
    "McpPerUserRequestsPerMinute": 100,
    "McpPerTenantRequestsPerMinute": 1000,
    "McpAdminToolsRequestsPerMinute": 50
  }
}
```

Default values are very relaxed for development. Tighten these in production `appsettings.Production.json`.

## Getting a JWT Token

Use the utility script to get a JWT token:

**PowerShell:**
```powershell
.\scripts\get-jwt-token.ps1 -Email "admin@sorcha.local" -Password "Admin123!"
```

**Bash:**
```bash
./scripts/get-jwt-token.sh -e admin@sorcha.local -p Admin123!
```

Or get it manually via API:
```bash
curl -X POST http://localhost/api/tenant/auth/login \
  -H "Content-Type: application/json" \
  -d '{"email":"admin@sorcha.local","password":"Admin123!"}'
```

## Integration with Claude Desktop

To use the MCP server with Claude Desktop:

1. Obtain a JWT token (see above)
2. Configure Claude Desktop's MCP settings to launch the server:

```json
{
  "mcpServers": {
    "sorcha": {
      "command": "docker-compose",
      "args": ["run", "--rm", "mcp-server", "--jwt-token", "<your-token>"],
      "cwd": "/path/to/sorcha"
    }
  }
}
```

## Available Tools

**76 registered tools**, auto-discovered from `[McpServerToolType]` classes. Do not hand-count from
this README — the authoritative catalogue is `GET /api/mcp/tools` (or a live `tools/list`), and
`ManifestIntegrityTests` fails the build if the gateway catalogue or `server.json` drifts from the
served set.

| Slice | Tools | Surface |
|---|---|---|
| Admin | 36 | health, logs, metrics, org/user admin + audit, platform settings, register stats/subscribe/sync/federation/governance-roster (#1647), validator control, credential lifecycle (offer/suspend/reinstate/revoke/refresh), presentations |
| Designer | 18 | blueprint create/validate/simulate/publish/export, rehearsal start/step/get (#1691), schema + template management, instance + register creation. `sorcha_register_create` and `sorcha_blueprint_publish` sit in this slice but carry the ADMIN role — the category is the workflow slice, not the entitlement |
| Participant | 14 | inbox, pending actions, action submission, transactions, wallet ops, bounded event wait (`sorcha_await_condition`, #1707) |
| Citizen | 8 | self-service wallet, devices (list/rename/revoke), credentials, persona |

`tools/list` on a live session is **tier-filtered** (F136): a platform-tier token sees ~68 of 76;
consumer-only tools require a consumer-tier token. Two further tools exist in source but are
deliberately unregistered — no `[McpServerToolType]` on the class, so the assembly scan never
discovers them: `sorcha_wallet_sign` (T029 — signing stays in the Wallet Service) and
`sorcha_blueprint_diff` (deleted in #1607 — no `/diff` endpoint ever existed anywhere; issue
#1607 tracks removing the now-dead client method it would have called).

## Lifecycle Tools (P1)

Three tools close the gap MCP-P0 restoration left: no tool could create a register, publish a
blueprint, or start an instance, so an agent could reach every read/participant surface but never
complete a workflow end to end.

| Tool | Slice | Backing endpoint(s) | Notes |
|---|---|---|---|
| `sorcha_register_create` | Designer (needs ADMIN role) | `GET /api/organizations/{id}` (owning-wallet lookup) → `POST /api/registers/initiate` → `POST /api/v1/wallets/{address}/sign` → `POST /api/registers/finalize` | The two-phase owner-attestation ceremony (initiate → sign → finalize) run as one call, signing with the organisation's *governance* key (`SorchaDerivationPaths.RegisterAttestation`, slot 100). The organisation owns that wallet, so the caller signs under a delegation scoped to that context (#1643): the admin who created the wallet holds one automatically, and any other admin needs `sorcha wallet access grant … --context sorcha:register-attestation`. A refusal at the sign step is reported as exactly that, with nothing created. Creating a register is irreversible and establishes the keys that authorise every later administrative change, so it **requires** a person to confirm it interactively before anything is created. |
| `sorcha_rehearsal_start` | Designer | `POST /api/blueprints/{id}/rehearsals` | #1691 — starts a full F142 rehearsal of a draft on the organisation's devMode **sandbox** register (provisioned on first use), with a stand-in wallet per participant. A rehearsal driven to `Passed` is the only thing that records the `RehearsalPass` `sorcha_blueprint_publish`'s soft gate checks for, so a rehearsed draft publishes **without** a human override. Before this tool every MCP publish needed the override (cold-start runs #4, #5, #7). No human gate: nothing leaves the sandbox. A `409` lists the draft's blocking validation errors in `validationErrors`. |
| `sorcha_rehearsal_step` | Designer | `POST /api/blueprints/{id}/rehearsals/{rehearsalId}/steps` | Submits the current action, signed as its participant's stand-in wallet, then waits (up to 90 s server-side) for the sandbox to seal it. The payload's values choose the route, so pick them for the path to be proven. A step that ran and **failed** is a 200 with `outcome: Failed` — the tool surfaces the rehearsal log's last `Error` entry as the reason. Input it can check itself (GUID, integer action id, JSON-object payload) is refused locally, because a step that reaches the server and fails ends the rehearsal. A timeout is reported as *unknown*, pointing at `sorcha_rehearsal_get`, never as a failure. |
| `sorcha_rehearsal_get` | Designer | `GET /api/blueprints/{id}/rehearsals/{rehearsalId}` | Reads outcome, steps, current action and log without changing anything — the recovery path after a step timeout. Rehearsal sessions are held in memory by the Blueprint Service instance that started them and do not survive its restart; a `404` says so. |
| `sorcha_blueprint_publish` | Designer (needs ADMIN + register-governance role) | `POST /api/blueprints/{id}/publish` | Always attempts the publish with no override first. A blueprint whose executable-definition hash has no matching rehearsal (F142 `RehearsalPass`) hits the soft `409 REHEARSAL_REQUIRED` gate — only then does this tool ask a person whether to publish anyway, proceeding only on an explicit `accept`. Governance (hard gate) is checked before rehearsal (soft gate), so nobody is asked to approve a publish that cannot succeed. Rehearse first with `sorcha_rehearsal_start` / `sorcha_rehearsal_step` and the gate never fires. `sorcha_blueprint_simulate` and `sorcha_disclosure_analysis` are dry runs and never record a pass — their descriptions now say so, because run #8's agent believed they did. |
| `sorcha_participant_publish` | Participant (needs ADMIN in the caller's org) | `POST /api/organizations/{orgId}/participants/publish` | Publishes a participant record binding a blueprint role to a wallet on a register, for the CALLER'S organisation (taken from the token, never an argument). This is what authorises that role to send actions (validator `VAL_BP_002` Tier 2) and what its disclosures are encrypted to. ⚠ The record's own id is a GUID, so a role resolves by the record's NAME — the tool publishes the role id as the name. Publishing is a register transaction: the binding is not effective until it seals, and the result says so. A two-party exchange needs one session per organisation (#1664). |
| `sorcha_participant_list` | Participant | `GET /api/registers/{registerId}/participants` | Lists which roles are bound to which wallets on a register: how a caller confirms a record has sealed before submitting that role's action. Active records only unless `includeRevoked`. |
| `sorcha_await_condition` | Participant | reuses `sorcha_participant_list` / `sorcha_workflow_status` / `sorcha_transaction_history`'s own reads (`GetRegisterAsync`, `GetPublishedParticipantsAsync`, `GetWorkflowStatusAsync`, `GetTransactionsByInstanceIdAsync`, `GetTransactionAsync`) | #1707 — a bounded, server-side wait so an agent no longer needs a human to relay a ledger event. Blocks the calling MCP request up to `timeoutSeconds` (default 25, hard max 55 — margin under the ~60s call timeout most MCP clients apply) while polling roughly once a second for one `condition`: `ParticipantActive` (a role becomes Active on a register), `InstanceReachesAction` (an instance reaches a given `actionId` as current, or completes when `actionId` is omitted), or `TransactionSeals` (a transaction acquires a docket number). Returns a tri-state `Outcome` — `Met`, `NotYet` (call again — not a failure), or `Unreachable` (the instance already advanced past the awaited action, was rejected/timed-out/cancelled, or completed without reaching it — waiting further is futile) — rather than a boolean, so a caller can tell "not yet" from "never". One tool with a `condition` discriminator rather than three, since the three conditions share one shape and #1685's discoverability risk is inconsistent parameter NAMES, not parameter count; parameter names (`workflowInstanceId`, `registerId`, `transactionId`) match the tools above exactly. Stateless-safe — the whole wait lives inside one request/one poll loop, compatible with `WithHttpTransport(o => o.Stateless = true)`. |
| `sorcha_whoami` | Participant (any tier) | none — reads the caller's own token, plus `GET /api/organizations/{id}` best-effort for the name | #1705 — the organisation the session acts for (id + name), its roles in platform and `sorcha:*` form, tier, token expiry, and the tools it may call. The two user ids are reported separately and never merged: `platformUserId` (account-wide) is null when the token omits it, not filled in from `sub` (the per-org `UserIdentity` id). Run #8's agent spent four calls inferring its organisation from an audit entry. |
| `sorcha_instance_create` | Designer | `POST /api/instances/` | Starts a running instance from a blueprint already published to a register, returning the `instanceId` that `sorcha_action_submit` has always required but nothing on the surface produced until now. No human gate — starting an instance is not irreversible the way creating a register or an unrehearsed publish is. |
| `sorcha_register_governors` | Admin | `GET /api/registers/{id}/governance/roster` | #1647 — answers "who governs this register?": every named role holder (Owner/Admin/Designer/Auditor) on the register's governance roster, not just the caller's own derived standing (that's `sorcha_register_relationship`). Built for the moment a governance action or `sorcha_blueprint_publish` is refused with a 403 — call this to find who can approve instead of decoding a genesis transaction over SSH (the cold-start run that motivated this issue). `NotFound` means no *sealed* roster (unknown id, or genesis still sealing), never "does not exist"; a 4xx from the Register Service is reported as a refusal and does not trip the availability tracker — only a 5xx or transport failure does (#1673). Same entitlement as `sorcha_register_relationship` (ADMIN role): both sit behind the Register Service's `CanReadTransactions` policy. |

`sorcha_register_create` and `sorcha_blueprint_publish` need organisation-administrator authority —
not the designer role — even though both sit in the Designer workflow slice: their backing
endpoints' own policies (`CanManageRegisters`, `CanPublishBlueprints`) accept `Administrator` /
`SystemAdmin`, and `ToolEntitlements.IsPermitted` matches roles exactly, so entitling either on
`sorcha:designer` would offer a tool a plain designer could never actually complete.

## Resources

Five resources — the server was tools-only (zero resources) until this branch. A cold-start
authoring A/B measured the blueprint schema resource as the single intervention that closed the
authoring gap.

| URI | MIME type | What it returns |
|---|---|---|
| `sorcha://schema/blueprint` | `application/schema+json` | The embedded blueprint JSON Schema. Describes both routing mechanisms (the `routes` array and action-level `participants`/`condition` — neither is legacy), `isStartingAction`, the full credential surface (`credentialRequirements`, `credentialIssuanceConfig`, `trustPolicy`, `claimMappings`, etc.), `rejectionConfig`, `requiredPriorActions`, and `instanceReference`. Kept current against the models by a reflection-based CI gate (`BlueprintSchemaCurrencyTests`). |
| `sorcha://examples/{name}` | `application/json` | A complete, working blueprint the walkthrough suite actually executes, using `routes` + `isStartingAction` — read alongside the schema above to see those constructs used together in a real, publishable definition. Names: `assured-identity` (credential issuance with selective disclosure and `credentialIssuanceConfig`), `encryption-at-rest` (encrypted payloads and disclosure groups), `ping-pong` (the minimal two-party exchange). |
| `sorcha://glossary` | `text/markdown` | What register, blueprint, action, participant, disclosure group, docket, `publicationTxId` and `execDefHash` mean. |
| `sorcha://registers` | `application/json` | The caller's visible registers (their organisation's, plus system registers), newest first. Capped at 50 — the response carries `count` and `truncated`, so a register's absence from the list is **not** evidence it doesn't exist when `truncated` is true. |
| `sorcha://instances` | `application/json` | The workflow instances visible to the calling identity right now. |

`sorcha://schema/blueprint`'s gap is tracked as issue #1609: the schema has drifted from the current
model (no `routes` definition, no credential surface) and needs a currency gate. The three worked
examples are the reference for those constructs until it's fixed — complementary to the schema, not
a correction of it.

## Prompts

Three guided recipes. Each returns a step-by-step brief naming the resources to read and the
lifecycle tools to call, in order — none of them call a tool itself.

| Prompt | Guides |
|---|---|
| `sorcha_two_party_exchange` | Setting up a two-party data exchange with selective disclosure, from an empty workspace to a running instance. |
| `sorcha_issue_credential` | Issuing a verifiable credential from an issuer organisation to a subject via an action-level `credentialIssuanceConfig`, including the OID4VCI offer path (`sorcha_credential_offer`) for a subject holding a standards-compliant external wallet rather than a Sorcha participant identity. |
| `sorcha_prove_to_regulator` | Assembling verifiable proof of a sealed transaction — inclusion proof, verification bundle, and any data the regulator is entitled to see — for a regulator or auditor. |

Every prompt repeats the human-approval reminder below, so an agent reading only one of them still
gets the full picture.

## Human Approval Model

Register creation, and publishing a blueprint that has never been rehearsed, put the decision to a
real person via MCP elicitation. `IHumanApproval` / `ElicitationHumanApproval`
(`src/Apps/Sorcha.McpServer/Services/`) is the **only** place a question is put to a client.

**The elicitation travels as a Multi Round-Trip Request (MRTR), never `ElicitAsync` (#1622).** The
HTTP transport is stateless, and there SDK 2.2.0 disables every server-to-client request and leaves
`McpServer.ClientCapabilities` null for every client — which is why a capability check once refused
Claude Code permanently. Instead, the first round of the tool call *throws* `InputRequiredException`
carrying the question; the client puts it to a person and re-sends the same tool call with the
answer. Two consequences for any tool that asks:

- **The whole tool method runs again on the second round**, so everything before the approval point
  must be side-effect free.
- **No `catch (Exception)` may swallow `InputRequiredException`** — rethrow it first, or the person
  is never asked.

The returned `requestState` is a digest of exactly what the person was shown: an answer with no
state, or with state for a different question (another register name, another storage mode), is
refused rather than applied. MRTR needs protocol revision `2026-07-28`; Claude Code 2.1.272
negotiates it over HTTP.

There are **three client states, not two**:

| State | When | Outcome |
|---|---|---|
| `NotSupported` | The client cannot carry an MRTR elicitation (a protocol revision before `2026-07-28` against the stateless transport) | Refused before anything is created — "connect with a client that supports it, or perform this step in the Sorcha UI" |
| `Refused` | A person declined, the client dismissed the request, the form came back without the confirm box set, or the answer was not for this exact question — **including a client that supports elicitation but auto-cancels every request when running headlessly** (Claude Code in `-p` mode does exactly this) | Refused — nothing changed |
| `Approved` | An explicit `accept` with the confirm box set | The **only** outcome that permits the operation |

Supporting elicitation is not a promise a person will actually be asked. Fail closed everywhere, in
every environment, with no bypass flag.

## Security

- **JWT Validation**: All requests validate JWT tokens against configured authority
- **Role-Based Access**: Tools are filtered by user roles in JWT claims
- **Rate Limiting**: Prevents abuse through configurable rate limits
- **Audit Trail**: All tool invocations are logged with user context
- **Secure Defaults**: Minimal permissions, explicit grants required

### Caller-token forwarding — a standing constraint

**The MCP server authorises by forwarding the caller's bearer token; it must never be given
`ServiceAuth__*` credentials.** Every tool call carries the caller's own JWT through to the backend
service it calls, so the service enforces exactly the caller's tier/role — not the MCP server's own.
Configuring `ServiceAuth:ClientId`/`ServiceAuth:ClientSecret` (or the certificate-mode equivalent)
on this host would grant it ambient service-principal authority independent of who is calling it,
which is precisely the elevation the caller-forwarding design exists to refuse. Do not add
`ServiceAuth__*` env vars or config to any MCP server deployment (Docker, Aspire, or otherwise).

This is why `Sorcha.ServiceClients.Http.Auth.ServiceAuthClient` resolves `ServiceAuth:ClientId`
(and the legacy secret) **lazily, at first use** (`RequireClientId()`/`RequireClientSecret()`,
called from `RefreshTokenAsync`) rather than in its constructor. The constructor used to throw
`InvalidOperationException("ServiceAuth:ClientId not configured")` unconditionally — which is
correct for a host that mints its own service tokens, but made the dependency mandatory for every
host, including the MCP server, which never configures it by design. That made `AddServiceClients`
itself throw at startup, so the MCP server could not resolve **any** typed client and every tool
call failed before it ever reached the network — one of the two root causes behind the tool surface
being completely dead for 6+ days (MCP-P0, 2026-09-05). Moving the fail-fast out of the
constructor let every tool ACTIVATE, and the fail-fast stays loud for hosts that DO need a service
token — it just moved from "at construction" to "at first attempted use".

Moving it, on its own, did **not** make the tools work. Every typed client calls
`ServiceClientAuthHelper.SetAuthHeaderAsync` before **every** request, which called
`GetTokenAsync` unconditionally; the token cache is empty on the first call, so it reached
`RequireClientId()` and threw anyway — and each typed client's own `catch (Exception)` swallowed
that throw into a `null` return. The tool then reported a generic "failed to retrieve…" to the
agent having never opened a socket. Roughly **50 of the 64 tools** were still dead after
activation was fixed, and the symptom was now *less* diagnostic than before, because nothing
named `ServiceAuth:ClientId` any more.

So the second half of the fix is `IServiceAuthClient.HasNoCredentialsConfigured`, and
`SetAuthHeaderAsync` skips the token demand when it is true — leaving the `Authorization` header
for `CallerTokenForwardingHandler` to stamp with the caller's own bearer. It is set **only** when
the host holds no credential material at all: no `ServiceAuth:ClientId`, no
`ServiceAuth:ClientSecret`, no workload certificate. That keeps two cases distinguishable which
must never be collapsed:

| Host | `HasNoCredentialsConfigured` | Behaviour |
|------|------------------------------|-----------|
| MCP server (no `ServiceAuth:*` at all) | `true` | Token demand skipped; caller's bearer forwarded; the request is actually made. |
| Any Sorcha service (configured) | `false` | Unchanged — acquires and attaches its service token exactly as before. |
| A service configured **incompletely** (id without secret) | `false` | Still throws `InvalidOperationException` out of `GetTokenAsync`, unswallowed by the helper. |

Collapsing the second and third rows into the first would turn a fail-closed credential check into
a fail-open one. The property is deliberately phrased negatively so that `default(bool)` — a test
double, or a future implementation that forgets the member — lands on the demanding, fail-closed
path. `tests/Sorcha.ServiceClients.Tests/Helpers/ServiceClientAuthHelperTests.cs` pins all three
rows, and `tests/Sorcha.McpServer.Tests/Infrastructure/HttpModeInvocationTests.cs` invokes real
tools through the production-shaped container against an unroutable address and requires a
**transport** failure — a credential failure fails that assertion.

## Development

### Project Dependencies

- `ModelContextProtocol` + `ModelContextProtocol.AspNetCore` (v1.4.1, centrally pinned in `Directory.Packages.props`) - MCP C# SDK
- `Sorcha.ServiceClients` - Backend service communication
- `Sorcha.ServiceDefaults` - Shared configuration
- `FluentValidation` - Input validation
- `System.IdentityModel.Tokens.Jwt` - JWT authentication

### Adding New Tools

1. Create a class under `Tools/<Slice>/` marked `[McpServerToolType]`
2. Mark the tool method `[McpServerTool(Name = "sorcha_...")]` with a `[Description]` of **at least two sentences** (FR-017 — the catalogue test checks the name, reviewers check the prose)
3. Enforce the caller's tier/role inside the tool via `ICallerContext` (tools are dispatch-filtered per tier, and each tool re-checks — defence in depth)
4. Add the tool name to BOTH the gateway `appsettings.json` `McpManifest` catalogue and the repo-root `server.json` — `ManifestIntegrityTests` fails the build until all three agree
5. Point the tool at a route a service actually maps. `scripts/check-mcp-routes.ps1` (CI: `mcp-routes-gate`) extracts every `api/…` request path a `[McpServerToolType]` class issues — inline against `HttpClient` **and** inside the typed `Sorcha.ServiceClients*` methods it calls — reduces both sides to a route family (query dropped, route parameters collapsed to `*`), and fails when **the service that tool addresses** maps no such family. Ownership is derived from the `SorchaService.<X>` the typed client or endpoint field resolves, so a same-named route in a different service does not count as a match. Nothing else verifies that join: an unmapped path compiles fine and reaches the agent as a generic "failed to retrieve", so a permanently broken tool reads as a transient outage. The same gate also requires every tool path to match an API-Gateway YARP route **to the owning service's cluster** (#1675) — the MCP server reaches every service through the gateway, and a path the service maps but the gateway does not route falls through to the UI catch-all and 404s bodiless (it found `api/validators/{id}/count` unrouted on its first run). Known-broken families are ratcheted in `.mcp-routes-allowlist`, which may only shrink
6. Make the tool's response DTO agree with the shape that route SENDS. `scripts/check-mcp-response-shapes.ps1` (CI: `mcp-response-shapes-gate`) collects every private/internal DTO declared inside a `[McpServerToolType]` class, pairs it with the type its route produces (`.Produces<T>()`, a typed result, or the shape an inline `Results.Ok(...)` returns), and reports any DTO property the server has no matching property for — honouring `[JsonPropertyName]` on both sides. **It walks nested element types, not just the outer envelope**: `sorcha_tenant_list` was wrong at both levels, and the element-level half (asking for `organizationId` when the server sends `id`) survived the envelope fix and was caught only by a human reading the server DTO. Nothing else verifies this join — a property the server never sends does not throw, it binds to null/0 and the agent gets a well-formed, empty, confident answer ("Retrieved 0 tenant(s)" next to `"totalCount": 27`, with the route gate green throughout). Unresolvable server shapes (a handler declared `Task<IResult>` returning an anonymous object) and not-yet-fixed mismatches are ratcheted in `.mcp-response-shapes-allowlist`, which may only shrink

   **It also asks whether each property can be READ, not only whether it exists.** A name can agree perfectly and still be unreadable: `RegisterSummaryInfo.Status` was named right and typed `string`, and `GET /api/registers/` sends `1`, so the whole list threw and the client's catch-all returned an empty one — both consumers reported "0 registers" against a node holding five (#1613). Whether an enum reaches the wire as a number or a name is derived from source, in precedence order: the property's `[JsonConverter]`, then the enum type's, then whether the owning service calls `SorchaJson.Configure` — **only Tenant and Wallet do**, so every other service emits bare integers. There is no platform-wide convention to assume; one `Register` sends `"purpose":"System"` beside `"status":1`.

   **The gate also covers `Sorcha.ServiceClients.Http`,** whose DTOs are shared by the MCP server, the UI, the CLI and the services, and which nothing scanned before — that is the surface #1613 fell through. Each `ReadFromJsonAsync<T>` is paired with the nearest preceding api path in the same method, which is a direct join rather than a guess, and a pairing is acted on only when most of the DTO's properties land on the candidate. DTOs whose endpoint declares no named response type are counted in the summary rather than allowlisted; **the way to check one is to give its endpoint a real `.Produces<T>()`** — 94 endpoints still declare `.Produces<object>`, and while one does, no gate can pair a client DTO against it.

Example:

```csharp
[McpServerToolType]
public sealed class CreateBlueprintTool
{
    private readonly IMcpAuthorizationService _authService;
    // ... other injected dependencies (typed service clients, IServiceAvailabilityTracker, ILogger)

    public CreateBlueprintTool(IMcpAuthorizationService authService, /* ... */)
    {
        _authService = authService;
    }

    [McpServerTool(Name = "sorcha_blueprint_create")]
    [Description("Creates a new blueprint from a complete JSON definition and returns the assigned " +
        "blueprint ID, version, and counts of participants and actions. Call this when you need to " +
        "register a brand-new multi-party workflow definition; use sorcha_blueprint_update instead " +
        "when revising an existing blueprint by ID.")]
    public async Task<BlueprintCreateResult> CreateBlueprintAsync(
        string blueprintJson, CancellationToken cancellationToken = default)
    {
        // Step 3: re-check entitlement inside the tool (defence in depth) — the dispatch filter
        // already narrowed the surface, but this tool must not trust that alone.
        if (!_authService.CanInvokeTool("sorcha_blueprint_create"))
        {
            return new BlueprintCreateResult { Status = "Unauthorized", /* ... */ };
        }

        // Implementation
    }
}
```

## Testing

```bash
# Run unit tests
dotnet test tests/Sorcha.McpServer.Tests

# Run with test coverage
dotnet test tests/Sorcha.McpServer.Tests --collect:"XPlat Code Coverage"
```

## Troubleshooting

### "JWT token is required"

Ensure you provide the JWT token via `--jwt-token` argument or `SORCHA_JWT_TOKEN` environment variable.

### "Service unavailable"

Check that required backend services are running and accessible:

```bash
# Verify services are up
docker-compose ps

# Check service logs
docker-compose logs -f blueprint-service
```

### Connection refused

Verify service addresses in configuration match Docker network DNS names (e.g., `http://blueprint-service:8080`).

## License

SPDX-License-Identifier: MIT
Copyright (c) 2026 Sorcha Contributors
