# Sorcha Walkthroughs

End-to-end integration tests and demos for the Sorcha platform. Each walkthrough runs against Docker services and exercises a distinct slice of functionality — from single-org wallet operations to multi-org encrypted workflows with conditional routing and verifiable credential issuance.

All walkthroughs share a PowerShell module (`modules/SorchaWalkthrough/`) for idempotent, repeatable execution. Credentials are externalized to `.secrets/passwords.json` (git-ignored).

```powershell
# Prerequisites: Docker Desktop, PowerShell 7.5+
docker-compose up -d

# First time: generate credentials
pwsh walkthroughs/initialize-secrets.ps1

# Run everything
pwsh walkthroughs/run-all.ps1

# Or run a specific walkthrough
pwsh walkthroughs/ConstructionPermit/setup.ps1
pwsh walkthroughs/ConstructionPermit/run.ps1
```

---

## Walkthrough Index

### Foundation

Verify infrastructure, UI gateway routing, and tooling integration.

| Walkthrough | What It Tests |
|-------------|--------------|
| [AdminIntegration](./AdminIntegration/) | Blazor WASM admin UI served behind the YARP API Gateway. Validates `/admin` subpath routing, nginx static file serving, SPA deep-link handling, and JWT auth in the browser. Single self-contained script. |
| [McpServerBasics](./McpServerBasics/) | MCP (Model Context Protocol) server for AI assistants (Claude Desktop, etc.). Tests JWT-based authentication, role-based tool filtering across 36 tools, and stdio transport. Single self-contained script. |

### Single-Org

One organization exercising wallets, registers, and crypto primitives.

| Walkthrough | What It Tests |
|-------------|--------------|
| [RegisterCreationFlow](./RegisterCreationFlow/) | Full register lifecycle: two-phase creation (initiate → sign attestation → finalize), CLI commands (`sorcha register create`), docket inspection, OData queries against the Register Service, and genesis transaction verification. |
| [WalletVerification](./WalletVerification/) | Multi-algorithm wallet testing across ED25519, P-256, and RSA-4096. Covers wallet creation, data signing, pre-hashed signing, and explicit signature verification. Ensures all three crypto backends produce valid, verifiable signatures. |
| [RegisterMongoDB](./RegisterMongoDB/) | MongoDB storage backend integration. Validates connection establishment, repository DI wiring, collection/index creation, and storage mode switching between InMemory and MongoDB providers. |
| [FormCoverage](./FormCoverage/) | **SorchaFormRenderer smoke test.** Single-org, 2 participants, 1 blueprint that exercises every `ControlTypes` value (Layout, Label, TextLine, TextArea, Numeric, DateTime, File, Choice, Checkbox, Selection), all three layout modes (`x-pages` wizard + `x-sections` + explicit `form.elements`), `x-width` hints, `x-rule` conditional visibility, `x-introduction`, and the `x-persona` autofill extension from Feature 092. `run.ps1 -Rounds N` loops the submit → acknowledge cycle N times as a lightweight form-pipeline smoke test. |

### Multi-Org

Multiple organizations with cross-org participants, encrypted transactions, and complex workflow routing.

| Walkthrough | What It Tests |
|-------------|--------------|
| [ConstructionPermit](./ConstructionPermit/) | **Primary multi-org integration test.** 4 organizations, 5 participants (including 2 users in the same org), per-user authentication, encrypted transactions, JSON Logic calculations (risk score, permit fee), conditional routing (high-risk → environmental review, low-risk → skip), rejection paths, and verifiable credential issuance. Three scenarios exercise the full pipeline: **A** low-risk residential (5 actions, skips environmental), **B** high-risk commercial (6 actions, triggers environmental review), **C** rejection at planning review. Each scenario creates a fresh workflow instance, authenticates each participant individually, executes actions through the async encrypted transaction pipeline (validator → docket → confirmation), and verifies instance advancement. |
| [SelfBuildHouse](./SelfBuildHouse/) | **Advanced multi-register workflow.** 6 organizations, 7 participants, **2 separate registers** with a planning permission blueprint (8 actions) and a building warrant blueprint (6 actions). Exercises cross-register verifiable credentials (3 VCs issued), credential chains (building warrant requires presenting the planning permission VC), credential-gated actions, document uploads, staged inspections (foundation → structure → final), conditional routing (protected species triggers ecological survey), rejection loops, and 5+ JSON Logic calculations. Three scenarios: standard approval, ecological branch, and rejection. |

### Agent-Driven

AI agent-operated walkthroughs using MCP Server connections for autonomous execution.

| Walkthrough | What It Tests |
|-------------|--------------|
| [TradeFinance](./TradeFinance/) | **Agent-driven multi-register workflow.** 4 organisations, 6 participants, **2 registers** with a procurement-to-pay blueprint (6 actions) and an invoice finance blueprint (4 actions). Exercises cross-register verifiable credentials (VerifiedInvoiceCredential required on Register 2), credential chains, selective disclosure under field-level encryption, AI agent coordination via MCP Server (2 Claude Code sessions on separate machines), scripted and persona execution modes, DevMode-to-FLE transition, and 5 JSON Logic calculations. Three scenarios: golden path (approved), disputed invoice (rejection + resubmission), and declined financing (low credit score). |

### HAIP (External Wallet)

Credential issuance and verification with external HAIP wallets via OpenID4VCI/OpenID4VP.

| Walkthrough | What It Tests |
|-------------|--------------|
| [AssuredIdentity](./AssuredIdentity/) | **Feature 107 — canonical citizen-identity workflow.** Acme Verification Co. issues an AssuredIdentityCredential to a public-org citizen via a polished 5-page wizard (name + DOB, address, contact, optional portrait, id-card review). Acme Licensing Co. consumes the credential in Phase 2 via HAIP OpenID4VP presentation and issues a DrivingLicenceCredential with the holder's identity carried forward. Also ships rules-mode sorcha-agent configs (`verification-analyst`, `licensing-officer`) for unattended runs, and a cross-peer smoke harness (`run-multi-peer.ps1` + `docker-compose.federation.yml`) that measures register-native delivery latency across two peers. Replaces the earlier `HaipVerifiedCitizen` + `HaipDrivingLicence` walkthroughs. |

**Playwright screenshot tests:** `tests/Sorcha.UI.E2E.Tests/Docker/HaipWalkthroughScreenshotTests.cs` captures UI state after the citizen-identity walkthrough runs — admin, issuer, and citizen views of credentials, wallets, organisations, and presentation requests. Run the AssuredIdentity walkthrough first, then execute the screenshot tests against the Docker stack.

### Advanced

Specialized infrastructure scenarios requiring additional setup beyond `docker-compose up -d`.

| Walkthrough | What It Tests |
|-------------|--------------|
| [DistributedRegister](./DistributedRegister/) | Cross-machine register replication across a 2-node P2P network. Tests peer discovery via gRPC, register advertisement and subscription (full-replica), service principal registration and revocation, cross-machine JWT using client_credentials grant, and SSL certificate generation with LAN SANs. Requires two machines on the same network. |
| [PerformanceBenchmark](./PerformanceBenchmark/) | Quantitative platform performance: payload size benchmarks (1 KB – 1 MB), sustained throughput (TPS), latency percentiles (P50/P95/P99), concurrency scaling (1–25+ workers), docket building rate, and Docker resource monitoring. Results are stored as JSON metrics for trend analysis. |

---

## How Walkthroughs Work

### setup.ps1 + run.ps1 Pattern

Most walkthroughs follow a two-script pattern:

1. **`setup.ps1`** — Creates organizations, users, wallets, participants, registers, subscriptions, and publishes the blueprint. Writes all IDs and credentials to `state.json` so `run.ps1` can execute without repeating setup.
2. **`run.ps1`** — Loads `state.json`, authenticates each participant, creates workflow instances, executes actions through the transaction pipeline, and reports pass/fail.

Both scripts are idempotent — safe to re-run. Setup detects existing resources and skips or updates.

### Parameters

| Parameter | Script | Default | Description |
|-----------|--------|---------|-------------|
| `-Profile` | setup.ps1 | `gateway` | URL profile: `gateway` (port 80), `direct` (per-service ports), `aspire` (HTTPS 7xxx) |
| `-SkipHealthCheck` | setup.ps1 | off | Skip Docker container health verification |
| `-ShowJson` | run.ps1 | off | Print full JSON request/response for debugging |
| `-Scenario` | run.ps1 | `all` | Run a specific scenario: `A`, `B`, `C`, or `all` |

### Single-Script Pattern

Foundation walkthroughs use one standalone script (no `state.json`):
- `AdminIntegration/test-admin-integration.ps1`
- `McpServerBasics/test-mcp-server.ps1`
- `RegisterMongoDB/test-mongodb-integration.ps1`

### Actor-Based Execution (sorcha-agent)

An alternative to the single-threaded `run.ps1` pattern. Each participant runs as an independent `sorcha-agent` process that autonomously discovers and responds to pending actions.

```powershell
# Setup is the same
pwsh walkthroughs/ConstructionPermit/setup.ps1

# Run with autonomous actors instead of run.ps1
pwsh walkthroughs/ConstructionPermit/run-agents.ps1
```

**How it works:**
- Each actor is defined by a JSON file (`actors/*.json`) specifying identity, connection, and decision rules
- The actor process authenticates, connects via SignalR (with polling fallback), and responds to pending actions
- Two decision modes: **rules** (JSON Logic conditions) and **ai** (Claude API with persona prompts)
- Actors can run on different machines — copy the actor JSON + `state.json` to deploy remotely

**Actor files for ConstructionPermit:** `walkthroughs/ConstructionPermit/actors/`

See `src/Apps/Sorcha.Agent/` for the agent CLI tool and `walkthroughs/ConstructionPermit/actors/README.md` for usage details.

---

## Open Participants & Late Binding (Feature 103)

Citizen-facing walkthroughs (e.g. `AssuredIdentity`)
use the **open starting action** pattern: the first authenticated wallet
to submit becomes the bound applicant for the life of the instance.
This is the right pattern for any flow where the citizen / applicant is
walking in off the street with no pre-existing participant record.

Three rules enforce the contract end-to-end:

1. The action carries `isStartingAction: true`. The validator skips the
   strict wallet check for these and the runtime late-binds the first
   submitter to the action's `Sender` participant. A second submission
   from a different wallet is rejected.
2. The participant referenced by `Action.Sender` on the open action MUST
   have `Participant.WalletAddress = null` in the published blueprint.
   Pre-baking a wallet is the foot-gun the publish-time guardrail
   `VAL_BP_010` exists to catch.
3. Walkthrough authors MUST NOT include the open participant in their
   `$walletMap`. The correct shape is to omit the citizen / applicant
   entry entirely and let the runtime late-bind:

   ```powershell
   # CORRECT for citizen-facing walkthroughs:
   $walletMap = @{
       "verification-analyst" = $analystWallet.Address
       # "citizen" intentionally absent — late-bound at runtime
   }
   ```

Credential-bootstrapped flows (Driving Licence requires a Verified
Citizen credential) layer `credentialRequirements` on the open starting
action. The HAIP presentation gate fires *before* the late-bind block,
so only credential holders can become the bound applicant.

See the `blueprint-builder` skill ("Open Participants & Late Binding"
section) for the runtime details and the `verifiable-credentials` skill
for the issuance side.

---

## run-all.ps1

Runs all walkthroughs in dependency order (Foundation → Single-Org → Multi-Org → Advanced):

```powershell
pwsh walkthroughs/run-all.ps1                    # Run everything
pwsh walkthroughs/run-all.ps1 -SkipAdvanced      # Skip DistributedRegister + PerformanceBenchmark
pwsh walkthroughs/run-all.ps1 -OnlySetup         # Run setup.ps1 only (create resources, skip execution)
pwsh walkthroughs/run-all.ps1 -Profile direct    # Use direct service ports instead of API Gateway
```

---

## Secrets

Credentials are stored in `walkthroughs/.secrets/passwords.json` (git-ignored). All walkthroughs share the platform seed admin (`admin@sorcha.local` / `Dev_Pass_2025!`). Multi-org walkthroughs add per-role credentials.

```powershell
# Generate (first time)
pwsh walkthroughs/initialize-secrets.ps1

# Regenerate (overwrite)
pwsh walkthroughs/initialize-secrets.ps1 -Force
```

Override any walkthrough's secrets via environment variable: `SORCHA_WT_SECRETS_<NAME>` (JSON string).

---

## Shared Module API

All walkthroughs import `modules/SorchaWalkthrough/SorchaWalkthrough.psm1`:

```powershell
$modulePath = Join-Path $PSScriptRoot "../modules/SorchaWalkthrough/SorchaWalkthrough.psm1"
Import-Module $modulePath -Force
```

### Console Output

| Function | Purpose |
|----------|---------|
| `Write-WtBanner $text` | Section banner (cyan) |
| `Write-WtStep $text` | Step header (yellow) |
| `Write-WtSuccess $text` | Success message (green) |
| `Write-WtFail $text` | Failure message (red) |
| `Write-WtInfo $text` | Info message (white) |
| `Write-WtWarn $text` | Warning message (yellow) |

### HTTP, Auth & JWT

| Function | Purpose |
|----------|---------|
| `Invoke-SorchaApi` | Consolidated HTTP caller with `-Method`, `-Uri`, `-Body`, `-Headers`, `-ContentType`, `-ShowJson`, `-RawResponse` |
| `Get-SorchaErrorBody $exception` | Extract HTTP error body from failed response |
| `Decode-SorchaJwt $token` | Decode JWT payload (base64url → hashtable) |

### Environment & Secrets

| Function | Purpose |
|----------|---------|
| `Initialize-SorchaEnvironment -Profile [-SkipHealthCheck]` | Docker health check + profile URL resolution → `@{ TenantUrl, RegisterUrl, WalletUrl, BlueprintUrl }` |
| `Get-SorchaSecrets -WalkthroughName` | Load credentials from `.secrets/passwords.json` |

### Auth & Users

| Function | Purpose |
|----------|---------|
| `Connect-SorchaAdmin -TenantUrl -AdminEmail -AdminPassword` | Login as platform seed admin → `@{ Token, OrganizationId, AdminUserId, Headers }` |
| `Connect-SorchaUser -TenantUrl -Email -Password -OrganizationId` | Two-step user login (login → select org) → `@{ Token, OrganizationId, UserId, Headers }` |
| `Register-SorchaPublicUser -TenantUrl -Email -Password -DisplayName` | Self-register on public org (creates PlatformUser) |
| `Confirm-SorchaUserEmail -TenantUrl -OrganizationId -UserId -Headers` | Admin-verify user email (bypasses SMTP) |
| `New-SorchaOrganization -TenantUrl -Name -Subdomain -AdminEmail -Headers` | Create private org via platform admin API |
| `Get-OrCreateUser -TenantUrl -OrganizationId -Email -DisplayName -Headers -Roles` | Idempotent user creation/addition to org |

### Wallets & Participants

| Function | Purpose |
|----------|---------|
| `New-SorchaWallet -WalletUrl -Name -Headers [-FetchPublicKey]` | Create ED25519 wallet → `@{ Address, PublicKey }` |
| `Register-SorchaParticipant -TenantUrl -WalletUrl -OrganizationId -WalletAddress -DisplayName -Headers` | Self-register participant + challenge-sign-verify wallet link |
| `Publish-SorchaParticipant -TenantUrl -OrganizationId -RegisterId -ParticipantName -OrganizationName -WalletAddress -PublicKey -Headers` | Publish participant record to register |

### Registers & Blueprints

| Function | Purpose |
|----------|---------|
| `New-SorchaRegister -RegisterUrl -WalletUrl -Name -Description -TenantId -OwnerUserId -OwnerWalletAddress -Headers` | Three-phase register creation (initiate → sign attestation → finalize) |
| `New-SorchaRegisterSubscription -TenantUrl -OrganizationId -RegisterId -RegisterName -SubscriptionType -Headers` | Subscribe org to register (Owner/Public/Invited) |
| `Publish-SorchaBlueprint -BlueprintUrl -TemplatePath -WalletMap -Headers -IdPrefix -RegisterId` | Load JSON template, patch wallet addresses, create + publish blueprint |
| `Invoke-SorchaAction -BlueprintUrl -InstanceId -ActionId -BlueprintId -SenderWallet -RegisterId -Token [-PayloadData] [-Reject] [-RejectionReason]` | Execute or reject a workflow action |

---

## Directory Structure

```
walkthroughs/
├── README.md                          # This file
├── run-all.ps1                        # Master runner
├── initialize-secrets.ps1             # Credential generator
├── .secrets/                          # Git-ignored credentials
│   └── passwords.json
├── modules/
│   └── SorchaWalkthrough/
│       ├── SorchaWalkthrough.psd1     # Module manifest
│       └── SorchaWalkthrough.psm1     # Shared module
│
├── AdminIntegration/                  # Foundation — Blazor WASM + YARP
├── McpServerBasics/                   # Foundation — MCP Server
│
├── RegisterCreationFlow/              # Single-Org — Register lifecycle + CLI
├── WalletVerification/                # Single-Org — Multi-algorithm crypto
├── RegisterMongoDB/                   # Single-Org — MongoDB backend
│
├── ConstructionPermit/                # Multi-Org — 4 orgs, routing, encryption
│   ├── config.json
│   ├── construction-permit-template.json
│   ├── setup.ps1
│   ├── run.ps1
│   ├── data/
│   │   ├── scenario-a-low-risk.json
│   │   ├── scenario-b-high-risk.json
│   │   └── scenario-c-rejection.json
│   ├── state.json                     # Generated by setup.ps1
│   └── README.md
│
├── SelfBuildHouse/                    # Multi-Org — 6 orgs, 2 registers, VCs
│
├── AssuredIdentity/                    # Feature 107 — canonical citizen identity + driving licence chain
│   ├── setup.ps1                       # Provisions Acme Verification, Acme Licensing, citizen
│   ├── run.ps1                         # Full Phase 1 + Phase 2 orchestrator
│   ├── run-phase1-identity.ps1         # AssuredIdentityCredential issuance
│   ├── run-phase2-licence.ps1          # Driving Licence credential chain
│   ├── run-agents.ps1                  # Unattended verification-analyst + licensing-officer
│   ├── run-multi-peer.ps1              # Cross-peer smoke (FR-039 — non-blocking)
│   ├── actors/                         # citizen + verification-analyst + licensing-officer
│   ├── blueprints/                     # assured-identity.json + driving-licence.json
│   ├── wallet/                         # Holder key + both credentials
│   ├── multi-peer-findings.md          # Cross-peer smoke baseline
│   └── state.json
│
├── DistributedRegister/               # Advanced — P2P replication
└── PerformanceBenchmark/              # Advanced — TPS, latency, concurrency
```

---

## Creating a New Walkthrough

1. Create directory: `walkthroughs/YourWalkthrough/`
2. Add `config.json` with name, description, category, organization details
3. Add `setup.ps1` — import module, bootstrap orgs/users/wallets, create register, publish blueprint, save `state.json`
4. Add `run.ps1` — import module, load `state.json`, authenticate users, execute scenarios, report pass/fail
5. Add secrets entry to `initialize-secrets.ps1`
6. Add entry to `run-all.ps1` walkthroughs array
7. Update this README

### config.json Template

```json
{
  "name": "YourWalkthrough",
  "description": "Brief description of what this tests.",
  "category": "foundation|single-org|multi-org|advanced",
  "organizations": [
    { "name": "Org Name", "subdomain": "org-subdomain", "role": "purpose" }
  ],
  "secretsKey": "your-walkthrough",
  "requiresRegister": true,
  "requiresParticipants": true,
  "template": "your-template.json",
  "scenarios": ["data/scenario-a.json", "data/scenario-b.json"]
}
```
