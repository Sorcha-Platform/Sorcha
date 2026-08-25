# Sorcha Walkthroughs

End-to-end integration tests and demos for the Sorcha platform. Each walkthrough runs against Docker services and exercises a distinct slice of functionality — from single-org wallet operations to multi-org encrypted workflows with conditional routing, verifiable credential issuance, file uploads, AI-agent autonomy, and cross-machine P2P replication.

All walkthroughs share a PowerShell module (`modules/SorchaWalkthrough/`) for idempotent, repeatable execution. Credentials are externalised to `.secrets/passwords.json` (git-ignored). Most walkthroughs follow a `setup.ps1` + `run.ps1` pattern, save state to `state.json`, and are safe to re-run.

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

| Category | Walkthroughs |
|----------|--------------|
| Foundation | AdminIntegration · McpServerBasics |
| Single-Org | RegisterCreationFlow · WalletVerification · RegisterMongoDB · FormCoverage · HealthDeclaration |
| Multi-Org Workflows | ConstructionPermit · SelfBuildHouse · PropertyInspection · PayloadTests |
| Credential Issuance & Reuse | AssuredIdentity · ForestryCertification · CyberEssentialsUac |
| Agent-Driven | TradeFinance |
| Distributed (Multi-Node) | DistributedRegister · PingPongN1 |
| Performance | PerformanceBenchmark |

A shared **`council/`** demo universe (fictional Strathcarron Council, Scotland) provides reusable orgs, places, and personas for ConstructionPermit, SelfBuildHouse, and PropertyInspection. See `council/README.md`.

---

## Foundation

Verify infrastructure, UI gateway routing, and tooling integration. Single-script tests, no `state.json`.

### [AdminIntegration](./AdminIntegration/)

**Scenario.** Validate that the Sorcha.Admin Blazor WASM application can be served behind the YARP API Gateway at the `/admin` subpath, with deep-links, static assets, and JWT-authenticated API calls all working through the gateway.

**Technical capabilities.** YARP reverse-proxy routing for SPA + API on a shared origin, nginx static-file serving with subpath base-href rewriting, JWT bearer auth flowing from a Blazor WASM client through the gateway to backend services.

**Benefit.** Proves that operators can host the platform behind a single ingress without exposing per-service ports — a pre-requisite for production deployment behind any load balancer or CDN.

### [McpServerBasics](./McpServerBasics/)

**Scenario.** Boot the platform, mint a JWT for a platform user, run the Sorcha MCP (Model Context Protocol) Server with that token, and exercise role-filtered tools through stdio. Designed to be the canonical "Claude Desktop talks to Sorcha" smoke test.

**Technical capabilities.** JWT-based authentication for AI assistants, role-based tool filtering across 36 MCP tools (admin, operator, consumer slices), stdio transport, refresh-token handling.

**Benefit.** Sorcha exposes its full surface area — registers, blueprints, instances, wallets, credentials — to AI assistants in a single secure, role-aware contract. This walkthrough proves the contract works end-to-end against real services rather than stubs.

---

## Single-Org

One organisation exercising wallets, registers, crypto primitives, and form rendering — the focused unit tests of the walkthrough family.

### [RegisterCreationFlow](./RegisterCreationFlow/)

**Scenario.** Walk the full register lifecycle through the Sorcha CLI: two-phase creation (initiate → sign attestation → finalise), genesis transaction processing, OData inspection of the resulting docket, and validation against multiple cryptographic backends.

**Technical capabilities.** Two-phase register creation with cryptographic attestation, genesis docket sealing, CLI ergonomics (`sorcha register create`), OData querying against `Sorcha.Register.Service`, multi-algorithm support (`-Algorithm ED25519|NISTP256|RSA4096`).

**Benefit.** Anyone learning the platform can build and inspect their first ledger in under a minute, and verify that every signing algorithm Sorcha advertises actually round-trips through register creation.

### [WalletVerification](./WalletVerification/)

**Scenario.** Create wallets across all three supported algorithms, sign arbitrary data, sign pre-hashed digests, verify signatures, and use a freshly created wallet to attest a register-creation transaction end-to-end.

**Technical capabilities.** Multi-algorithm HD wallets (ED25519, NIST P-256, RSA-4096), data and pre-hashed signing endpoints, signature verification, integration of Wallet Service with Register Service via the canonical attestation flow.

**Benefit.** The cryptographic foundation of the platform is exercised in isolation — if this walkthrough is green, every downstream feature that relies on signing has a known-good baseline.

### [RegisterMongoDB](./RegisterMongoDB/)

**Scenario.** Switch the Register Service backend from in-memory to MongoDB, restart, and prove that connection establishment, repository DI wiring, collection/index creation, and CRUD round-trips all work against a real Mongo container.

**Technical capabilities.** `Sorcha.Register.Storage.MongoDB` repository, smart storage selection driven by connection strings, the storage-registration log audit trail (Feature 113), index creation on first start.

**Benefit.** Regression-locks the only durable storage backend for the Register Service. Pairs with the storage-audit fail-fast: if the audit is misconfigured, this walkthrough is the first to fail.

### [FormCoverage](./FormCoverage/)

**Scenario.** Single org, two participants, one blueprint deliberately constructed to render every kind of form control Sorcha supports. The submitter walks the form, the recipient acknowledges. `run.ps1 -Rounds N` repeats the cycle so the form pipeline can be soaked.

**Technical capabilities.** Every `ControlTypes` value (Layout, Label, TextLine, TextArea, Numeric, DateTime, File, Choice, Checkbox, Selection); all three layout modes (`x-pages` wizard, `x-sections`, flat `form.elements`); `x-width` hints, `x-rule` conditional visibility, `x-introduction` copy blocks, `x-persona` autofill (Feature 092).

**Benefit.** Every UI change to `SorchaFormRenderer` can be eyeball-verified in one run. Catches layout regressions before they reach the workflow walkthroughs.

### [HealthDeclaration](./HealthDeclaration/)

**Scenario.** A demo clinic onboards a single patient, who completes a multi-page health declaration covering medical history, current medications, allergies, and consent. The form exercises the layout extensions added in Feature 091.

**Technical capabilities.** Single-org, single-participant baseline; `x-pages` wizard layout; `x-sections` grouping; `x-rule` conditional visibility (e.g. show "list medications" only when "taking medications = Yes"); `x-introduction` per-section guidance; `x-width` responsive hints. Idempotent setup designed to drop onto any node — including `n1.sorcha.dev` via `-Profile n1`.

**Benefit.** The smallest multi-page form demo in the platform. Useful for sales/UX walkthroughs where the audience cares about the form experience, not the consensus mechanics. Doubles as the n1 smoke test for the form pipeline.

---

## Multi-Org Workflows

Multiple organisations with cross-org participants, encrypted transactions, conditional routing, file uploads, and verifiable credential issuance.

### [ConstructionPermit](./ConstructionPermit/)

**Scenario.** Stoniebridge Construction submits plans for a new development to Strathcarron Council. The application is routed through Murchison Engineering for structural assessment, the council's Planning Officer for review, optionally Heatherbank Environmental for environmental review, the council's Building Control Inspector, and finally back to the Planning Officer for permit issuance — producing a **Building Permit Verifiable Credential**. Three scenarios drive the branching: **(A)** low-risk residential skips environmental review, **(B)** high-risk commercial triggers it, **(C)** Planning Officer rejects.

**Technical capabilities.** 4 organisations, 5 participants (2 sharing one org), per-user authentication, encrypted transactions through the validator → docket → confirmation pipeline, JSON-Logic calculations (risk score, permit fee), conditional routing, rejection paths, end-of-flow VC issuance, both `run.ps1` (scripted) and `run-agents.ps1` (autonomous actor) execution modes.

**Benefit.** This is the platform's primary multi-org integration test. If a feature touches workflow execution, encryption, or routing, this walkthrough proves the change against a realistic public-sector permit process.

### [SelfBuildHouse](./SelfBuildHouse/)

**Scenario.** A member of the public builds a house in Scotland: planning permission application (Register 1) → planning approval → building warrant application (Register 2, gated on the planning VC) → staged inspections (foundation → structure → final) → completion certificate. Three scenarios cover the standard approval, the ecological-survey branch (protected species detected), and a rejection loop.

**Technical capabilities.** 6 organisations, 7 participants, **two separate registers**, **cross-register VC chains** (Planning Permission VC presented as a prerequisite on Register 2; Building Warrant VC presented for the Completion Certificate), credential-gated actions, document uploads, staged inspections, conditional routing, 5+ JSON-Logic calculations, rejection loops.

**Benefit.** Demonstrates the platform's most complete capability stack in one workflow — particularly how verifiable credentials issued on one register become unforgeable prerequisites on another, modelling the real legal sequencing of construction approvals.

### [PropertyInspection](./PropertyInspection/)

**Scenario.** A council tenant in fictional Strathcarron Council reports a maintenance problem with photo evidence. The Housing Officer triages, allocates a contractor, and issues a **JobAssignmentCredential** that the tenant uses to verify the operative's identity at the door before granting access. The contractor completes the repair with photo evidence and a delivery note. The Housing Officer reviews and either accepts or sends back for rework. Emergency-severity jobs require a Building Inspector sign-off before tenant satisfaction is confirmed and a **ServiceCompletionCredential** is issued.

**Technical capabilities.** File uploads (Feature 085) with photo evidence at two stages, **mid-flow VC issuance and verification** (issued at action 1, verified by holder at action 2), Consumer Persona autofill (Feature 092), conditional routing on severity, **cyclic rework** (action 4 → 3 and action 5 → 3), operative-verification rejection forcing re-allocation.

**Benefit.** Models a real safeguarding control — verifying the identity of a worker entering a vulnerable resident's home — using cryptographic credentials instead of laminated photo cards. Also the canonical demo for cyclic rework and mid-flow credential gating.

### [PayloadTests](./PayloadTests/)

**Scenario.** Sender Corp uploads encrypted file payloads to Receiver Corp through a minimal two-action transfer blueprint. Multiple file sizes (1KB → 40MB) exercise chunking, multi-session continuity, and download reassembly.

**Technical capabilities.** Feature 085 chunked encrypted file upload via Blueprint Service (`POST /api/file-chunks`), decrypted file download via Wallet Service (`GET /api/v1/wallets/{address}/files/download`), FLE-compatible file fields, validator enforcement of file-field encryption rules. Includes `cross-node-setup.ps1` for two-node runs and `stress-test.ps1` for sustained-load profiling.

**Benefit.** Targeted regression coverage for the file-attachment subsystem. Detects breakage in chunking, key wrapping, or reassembly without dragging in the workflow complexity of the larger demos.

---

## Credential Issuance & Reuse

Walkthroughs whose primary purpose is issuing a verifiable credential and proving it composes into other flows.

### [AssuredIdentity](./AssuredIdentity/)

**Scenario.** Acme Verification Co. issues an **AssuredIdentityCredential** to a public-org citizen via a polished 5-page wizard (name + DOB, address, contact, optional portrait, id-card review). In Phase 2, Acme Licensing Co. consumes that credential via HAIP OpenID4VP presentation and issues a **DrivingLicenceCredential** with the holder's verified identity carried forward.

**Technical capabilities.** Feature 107's canonical citizen-identity workflow; the `x-review` schema extension for the id-card page; `x-file.capture` + `x-file.embedAs` for camera capture and client-side token-image resize; client-bound DoB picker (`formatMaximum: "today"`); server-side portrait-size gate (`WARN_CRED_PORTRAIT_OVERSIZE_001`); full HAIP OpenID4VCI issuance; **open-participant late binding** (the citizen is not pre-baked in `$walletMap` — the first authenticated wallet to submit becomes the bound applicant); rules-mode `sorcha-agent` configs (`verification-analyst`, `licensing-officer`) for unattended runs; cross-peer smoke harness (`run-multi-peer.ps1` + `docker-compose.federation.yml`).

**Benefit.** The reference implementation for citizen-facing flows. Demonstrates how an identity-verification provider can act as the trust root for downstream credentials (driving licence today; benefits, professional registration, age verification tomorrow) without ever sharing PII directly between issuers — only signed, selectively disclosable credentials. Replaces the older `HaipVerifiedCitizen` / `HaipDrivingLicence` walkthroughs.

> **Playwright screenshot tests.** `tests/Sorcha.UI.E2E.Tests/Docker/HaipWalkthroughScreenshotTests.cs` captures admin, issuer, and citizen views after this walkthrough has run.

### [ForestryCertification](./ForestryCertification/)

**Scenario.** Highland Timber Supplies submits a timber batch (species, volume, forest of origin, chain-of-custody evidence) to Forestry Certification, an independent auditor. The auditor reviews and either issues a **ForestProductDPPCredential** with verified sustainability claims or declines with a reason. The DPP credential is portable: re-running Trade Finance against the same stack lets the buyer apply preferential terms for verifiably-sustainable products.

**Technical capabilities.** 2 orgs, 2 actions, single register; the Sales Manager is an **open participant** late-bound at submission; SD-JWT VC issuance with 12 selectively disclosable claims (9 of which are flagged `disclosable` so a downstream verifier presents only what it needs); auditor-overrides-supplier on safety-critical fields (e.g. embodied carbon); 365-day credential validity window; cross-walkthrough composition with TradeFinance via shared org subdomain (`highland-timber`).

**Benefit.** Demonstrates the **Digital Product Passport** pattern: a credential that travels with goods through downstream commercial workflows, cryptographically proving sustainability claims without re-doing the audit. The smallest end-to-end VC walkthrough in the suite — a useful copy-paste starting point for new credential types.

### [CyberEssentialsUac](./CyberEssentialsUac/)

**Scenario.** A cyber assessor evaluates a subject organisation's User Access Control (UAC) posture against the IASME Requirements for IT Infrastructure v3.3, mints a **CyberEssentialsUacPosture** credential on a compliant result, and the subject organisation immediately presents that credential to an insurer to request cover. Three test runs cover the full threat model: **(1)** compliant evidence — posture credential issued, insurer quote returned; **(2)** non-compliant evidence (`adminMfaEnforced=false`) — route-gated issuance withheld, issue action unreachable; **(3)** mid-cycle revocation (n1-only) — revoking the credential after issuance blocks re-presentation via FailClosed (HTTP 400). A fourth run (`run-haip-sd.ps1`) exercises genuine OID4VCI issuance and OID4VP selective disclosure: 4 of 10 claims disclosed on the wire, 6 withheld; a negative test confirms the verifier rejects when a withheld claim is required.

**NOT formal Cyber Essentials certification.** The credential attests that evidence was captured and evaluated by the assessor on the assessment date. It does not constitute, replace, or simulate the NCSC/IASME certification scheme.

**Technical capabilities.** 3 organisations, 5 actions across 2 blueprints, 1 register; assessor-owned register with subject-org and insurer subscriptions; **open starting actions** (assessor on Blueprint A, subject-org on Blueprint B); JSON Logic compliance gate (`computedCompliant`) routing to credential issuance or terminal non-compliance record; **issuer-pinned trust policy** (`did-allowlist` with the assessor's `did:sorcha:org:<walletAddress>` substituted at publish time); **FailClosed revocation check**; HAIP service principal + `client_credentials` grant; `sorcha-agent` OID4VCI receive + OID4VP selective-disclosure present; script-injected credential presentation (the agent cannot auto-present `SorchaInternal` credentials — see `actors/README.md`).

**Benefit.** Demonstrates the platform's posture-credential pattern in a regulated-sector context: a credential that attests evidence rather than a binary pass/fail, consumed downstream as an access gate with revocation as a live enforcement mechanism. The HAIP variant is the canonical proof that SD-JWT selective disclosure is wire-genuine, not a server-side filter.

---

## Agent-Driven

AI-agent–operated walkthroughs using the Sorcha MCP Server for autonomous execution. Each participant runs as an independent `sorcha-agent` process that authenticates, connects via SignalR (with polling fallback), and responds to pending actions on its own.

### [TradeFinance](./TradeFinance/)

**Scenario.** A realistic SME procurement-to-pay cycle plus invoice financing. **Register 1** (owned by the buyer): purchase order → supplier acknowledgement → delivery confirmation → goods received → invoice → buyer approval, issuing a **VerifiedInvoiceCredential**. **Register 2** (owned by the funder): credential presentation → credit-bureau assessment → funder evaluation (advance/fee auto-calculated via JSON Logic) → approval, issuing a **TradeFinanceCredential**. Three scenarios: golden path (approved), disputed invoice (rejection + resubmission), declined financing (low credit score).

**Technical capabilities.** 4 organisations, 6 participants, **2 registers**, cross-register VC dependency, **field-level encryption with selective disclosure** (the supplier's confidential cost breakdown is visible to the Finance Director but not the buyer), revocation policy `FailClosed` (revoking the invoice credential auto-rejects any in-flight financing request), 5+ JSON-Logic calculations, **AI-agent coordination via MCP Server** (two Claude Code sessions on separate machines coordinate **entirely through register replication** with no side channels), persona-driven kickoff (`once` trigger fires the starting Raise-PO action; `interval` trigger generates 20 randomised invoices for soak testing), DevMode-to-FLE transition for staged demos.

**Benefit.** The platform's most comprehensive single demo. Proves three of Sorcha's hardest claims simultaneously: cryptographically-verifiable cross-organisational data flow, AI agents acting as principals (not orchestrators), and revocation as an enforceable gate rather than an advisory signal. Pairs with ForestryCertification to show DPP-driven rate uplift.

---

## Distributed (Multi-Node)

Walkthroughs that exercise cross-machine peer replication. Require additional setup beyond `docker-compose up -d`.

### [DistributedRegister](./DistributedRegister/)

**Scenario.** Create a register on machine A, advertise it to the peer network, have machine B discover the advertisement via heartbeat, subscribe full-replica, and verify that genesis docket and subsequent transactions replicate. The included `sync-test.ps1` runs a four-phase Feature-071 P2P sync regression: connectivity, discovery, subscription, finalisation.

**Technical capabilities.** Peer discovery via gRPC, register advertisement and subscription (full-replica), service-principal registration and revocation for cross-machine service-to-service auth, `client_credentials` grant flow, SSL certificate generation with LAN SANs, streaming relay, heartbeat advertisements, docket finalisation, live streaming, resilience under restart.

**Benefit.** The canonical proof that Sorcha is a distributed ledger and not just a clustered web app. Without a green run here, no claim about decentralisation, replication, or multi-validator operation can be trusted.

### [PingPongN1](./PingPongN1/)

**Scenario.** Two orgs on two machines (local Docker behind NAT and `n1.sorcha.dev` on Azure) share one public register hosted on n1. Pong Corp on n1 owns the register and creates a blueprint instance; Ping Labs (local) subscribes full-replica; both sides exchange ping-pong actions over P2P. The register lives on n1 because local is NAT'd — pulls run in the direction NAT allows.

**Technical capabilities.** Cross-machine register subscription with FullReplica, advertisement → heartbeat discovery, genesis docket pull-through, blueprint publish + instance creation across nodes, both-sides participant publishing on a shared register. Tracks the platform's progress on Features 071 and 108 (NAT-traversal / register-local relationships).

**Benefit.** The most realistic cross-network test in the suite — one node on the public internet, one behind a home/office NAT, talking only over the documented P2P paths. This walkthrough is also where the team has historically pinned reverse-direction replication issues; its `README.md` documents Findings A and B and the fixes that resolved them.

---

## Performance

### [PerformanceBenchmark](./PerformanceBenchmark/)

**Scenario.** Quantitative benchmark of the Register Service: payload-size sweeps (1 KB – 1 MB), sustained throughput (TPS), latency percentiles (P50/P95/P99), concurrency scaling (1–25+ workers), docket-building rate, and Docker resource monitoring. Results are written as JSON metrics for trend analysis.

**Technical capabilities.** NBomber-style throughput driver, percentile latency tracking, parameterised concurrency, docker-stats sampling, JSON-result archival for regression comparison.

**Benefit.** The only walkthrough that produces *numbers* rather than pass/fail. Used for regression trend analysis between releases and as the baseline for any "is the change faster?" question.

---

## How Walkthroughs Work

### setup.ps1 + run.ps1 pattern

Most walkthroughs follow a two-script pattern:

1. **`setup.ps1`** — Creates organisations, users, wallets, participants, registers, subscriptions, and publishes the blueprint. Writes all IDs and credentials to `state.json` so `run.ps1` can execute without repeating setup.
2. **`run.ps1`** — Loads `state.json`, authenticates each participant, creates workflow instances, executes actions through the transaction pipeline, and reports pass/fail.

Both scripts are idempotent — safe to re-run. Setup detects existing resources and skips or updates.

### Single-script pattern

Foundation walkthroughs use one standalone script (no `state.json`):
- `AdminIntegration/test-admin-integration.ps1`
- `McpServerBasics/test-mcp-server.ps1`
- `RegisterMongoDB/test-mongodb-integration.ps1`

### Common parameters

| Parameter | Script | Default | Description |
|-----------|--------|---------|-------------|
| `-Profile` | setup.ps1 | `gateway` | URL profile: `gateway` (port 80), `direct` (per-service ports), `aspire` (HTTPS 7xxx), `n1` (n1.sorcha.dev) |
| `-SkipHealthCheck` | setup.ps1 | off | Skip Docker container health verification |
| `-Force` | setup.ps1 | off | Recreate state even if existing state.json validates |
| `-ShowJson` | run.ps1 | off | Print full JSON request/response for debugging |
| `-Scenario` | run.ps1 | `all` | Run a specific scenario (`A`, `B`, `C`, `golden-path`, `decline`, …) |

### Actor-based execution (`sorcha-agent`)

An alternative to the single-threaded `run.ps1` pattern. Each participant runs as an independent `sorcha-agent` process that autonomously discovers and responds to pending actions.

```powershell
pwsh walkthroughs/ConstructionPermit/setup.ps1
pwsh walkthroughs/ConstructionPermit/run-agents.ps1
```

- Each actor is defined by a JSON file (`actors/*.json`) specifying identity, connection, and decision rules.
- The actor process authenticates, connects via SignalR (with polling fallback), and responds to pending actions.
- Two decision modes: **rules** (JSON Logic conditions) and **ai** (Claude API with persona prompts).
- Actors can run on different machines — copy the actor JSON + `state.json` to deploy remotely.

See `src/Apps/Sorcha.Agent/` for the agent CLI tool.

### Persona-driven agents (Feature 110)

Agents support **personas** — JSON files that let an agent *initiate* a workflow rather than only react. A persona declares a trigger (`once` or `interval`), a target (blueprint + instance + action), and a payload template.

- **One-shot kickoff.** A `once` trigger fires a starting action on agent launch. TradeFinance uses `procurement-mgr-kickoff.persona.json` so `run-agents.ps1` produces a Raise-PO submission with no manual step.
- **Scenario data generation.** An `interval` trigger with `maxIterations` and optional `until` timestamp fires varied payloads repeatedly. `walkthroughs/TradeFinance/personas/invoice-generator.persona.json` generates 20 invoices with randomised amounts and currencies.

Payload tokens, resolved per fire: `${now}`, `${uuid}`, `${counter}`, `${random.int(min,max)}`, `${random.decimal(min,max,precision)}`, `${random.choice([…])}`. A string that is exactly `"${token}"` preserves typed JSON; embedded tokens like `"INV-${counter}"` produce string interpolation.

See [`specs/110-agent-persona-mode/quickstart.md`](../specs/110-agent-persona-mode/quickstart.md).

---

## Open Participants & Late Binding (Feature 103)

Citizen-facing walkthroughs (`AssuredIdentity`, `ForestryCertification`, `PropertyInspection`) use the **open starting action** pattern: the first authenticated wallet to submit becomes the bound applicant for the life of the instance. Three rules enforce the contract:

1. The action carries `isStartingAction: true`. The validator skips the strict wallet check for these and the runtime late-binds the first submitter to the action's `Sender` participant. A second submission from a different wallet is rejected.
2. The participant referenced by `Action.Sender` on the open action MUST have `Participant.WalletAddress = null` in the published blueprint. Pre-baking a wallet is the foot-gun the publish-time guardrail `VAL_BP_010` exists to catch.
3. Walkthrough authors MUST NOT include the open participant in `$walletMap`:

   ```powershell
   $walletMap = @{
       "verification-analyst" = $analystWallet.Address
       # "citizen" intentionally absent — late-bound at runtime
   }
   ```

Credential-bootstrapped flows (e.g. Driving Licence requires a Verified Citizen credential) layer `credentialRequirements` on the open starting action. The HAIP presentation gate fires **before** the late-bind block, so only credential holders can become the bound applicant.

See the `blueprint-builder` skill ("Open Participants & Late Binding") and the `verifiable-credentials` skill.

---

## Strathcarron Council Demo Universe

A shared fictional Scottish council area used across `ConstructionPermit`, `SelfBuildHouse`, and `PropertyInspection`. Reusable orgs (Strathcarron Council, Stoniebridge Construction, Murchison Engineering, Heatherbank Environmental, Caledonian Water), places (Carronbridge SC4, Dalreoch SC6, Invercarron SC2, Loch Morach), and roles. No real councils, utility companies, or identifiable organisations are used.

See `walkthroughs/council/README.md` and `walkthroughs/council/setup-council.ps1`.

---

## run-all.ps1 — the regression suite

The **core** suite is sixteen steps and is the platform's end-to-end regression check. It is what
"16/16" means in `MASTER-TASKS.md` and in the node-state notes.

```powershell
pwsh walkthroughs/run-all.ps1 -Profile n1                  # the sixteen-step core suite against n1
pwsh walkthroughs/run-all.ps1 -Profile n1 -AuthGapMs 1000  # faster, where RATELIMIT_* is raised
pwsh walkthroughs/run-all.ps1 -GatewayUrl http://tiny:8090 # any node, no profile needed
pwsh walkthroughs/run-all.ps1 -Profile n1 -StartAt 9       # resume after fixing one step
pwsh walkthroughs/run-all.ps1 -Suite legacy                # the older, unmaintained walkthroughs
pwsh walkthroughs/run-all.ps1 -OnlySetup                   # provision only
```

Every step writes a transcript to `walkthroughs/.run-logs/` (gitignored) plus a `summary.json`.
Nothing aborts on the first failure.

### Four things the runner has to get right — each has produced a wrong verdict before

1. **An exit code is not a verdict.** `ConstructionPermit/run-agents.ps1` prints `ERROR (exit 1)`
   for every agent and still exits 0; `TradeFinance/setup.ps1` has printed a raw HTTP 500 and
   exited 0. A run once scored a step PASS with all five agents dead against the wrong host. Steps
   are judged on the exit code **and** on failure markers found in the transcript.
2. **ConstructionPermit and SelfBuildHouse use `run.ps1 -Scenario all`, NOT `run-agents.ps1`.** The
   agent launchers hard-code `actors/*.json` whose `gatewayUrl` is literally `http://localhost`,
   and only three of ConstructionPermit's five actors have a `-remote` variant — so they cannot
   target a remote node at all. The scenario runners read their URLs from `state.json`. That is
   where "3/3" comes from. `CyberEssentialsUac/run-agents.ps1` is safe despite the name: it spawns
   no agents and drives the API itself.
3. **`Sorcha.Agent` is pre-built** before any agent-spawning step. Five concurrent `dotnet run`
   invocations race to build the same assembly and all of them die on the file lock — reported as
   `The build failed` inside each agent's own log, while the launcher still exits 0.
4. **Order is load-bearing, twice.** ConstructionPermit runs first because its setup enables the
   Public org node-wide and three of the seven walkthroughs never enable it themselves (on a fresh
   database the difference is a wall of 403s that read as permissions problems). And
   `run-suspension` must precede `run-revocation`, because revocation is terminal by design and
   consumes the only ACTIVE credential — suspension then fails on the innocent script.

### The suite passing is not the whole verdict

Features 194/195 degrade to the **old behaviour** rather than to an error, so the positive check is
the counter, and no walkthrough can see it:

```bash
docker logs sorcha-blueprint-service 2>&1 | grep -c 'pre-Feature-194 fallback'   # expect 0
```

A rejection scenario currently makes that non-zero — see **#1576**.

### `-AuthGapMs`

The shared module throttles every `/auth/` call, defaulting to 8s because that suits the shipped
rate limits. A node with `RATELIMIT_AUTH_PERMIT` raised (n1 runs 1200/min) can go far lower.
⚠ Lowering it compresses the timeline and **has surfaced latent races the slow default hid** — the
second-resolution blueprint-id collision (PR #1577) was found exactly this way. That is a feature,
but expect it to find things.

---

## Secrets

Credentials are stored in `walkthroughs/.secrets/passwords.json` (git-ignored). All walkthroughs share the platform seed admin (`admin@sorcha.local` / `Dev_Pass_2025!`). Multi-org walkthroughs add per-role credentials.

```powershell
pwsh walkthroughs/initialize-secrets.ps1            # Generate (first time)
pwsh walkthroughs/initialize-secrets.ps1 -Force     # Regenerate (overwrite)
```

Override any walkthrough's secrets via environment variable: `SORCHA_WT_SECRETS_<NAME>` (JSON string).

---

## Shared Module API

All walkthroughs import `modules/SorchaWalkthrough/SorchaWalkthrough.psm1`:

```powershell
$modulePath = Join-Path $PSScriptRoot "../modules/SorchaWalkthrough/SorchaWalkthrough.psm1"
Import-Module $modulePath -Force
```

### Console output

| Function | Purpose |
|----------|---------|
| `Write-WtBanner $text` | Section banner (cyan) |
| `Write-WtStep $text` | Step header (yellow) |
| `Write-WtSuccess $text` | Success message (green) |
| `Write-WtFail $text` | Failure message (red) |
| `Write-WtInfo $text` | Info message (white) |
| `Write-WtWarn $text` | Warning message (yellow) |

### HTTP, auth & JWT

| Function | Purpose |
|----------|---------|
| `Invoke-SorchaApi` | Consolidated HTTP caller with `-Method`, `-Uri`, `-Body`, `-Headers`, `-ContentType`, `-ShowJson`, `-RawResponse` |
| `Get-SorchaErrorBody $exception` | Extract HTTP error body from failed response |
| `Decode-SorchaJwt $token` | Decode JWT payload (base64url → hashtable) |

### Environment & secrets

| Function | Purpose |
|----------|---------|
| `Initialize-SorchaEnvironment -Profile [-SkipHealthCheck]` | Docker health check + profile URL resolution → `@{ TenantUrl, RegisterUrl, WalletUrl, BlueprintUrl }` |
| `Get-SorchaSecrets -WalkthroughName` | Load credentials from `.secrets/passwords.json` |

### Auth & users

| Function | Purpose |
|----------|---------|
| `Connect-SorchaAdmin -TenantUrl -AdminEmail -AdminPassword` | Login as platform seed admin → `@{ Token, OrganizationId, AdminUserId, Headers }` |
| `Connect-SorchaUser -TenantUrl -Email -Password -OrganizationId` | Two-step user login (login → select org) → `@{ Token, OrganizationId, UserId, Headers }` |
| `Register-SorchaPublicUser -TenantUrl -Email -Password -DisplayName` | Self-register on public org (creates PlatformUser) |
| `Confirm-SorchaUserEmail -TenantUrl -OrganizationId -UserId -Headers` | Admin-verify user email (bypasses SMTP) |
| `New-SorchaOrganization -TenantUrl -Name -Subdomain -AdminEmail -Headers` | Create private org via platform admin API |
| `Get-OrCreateUser -TenantUrl -OrganizationId -Email -DisplayName -Headers -Roles` | Idempotent user creation/addition to org |

### Wallets & participants

| Function | Purpose |
|----------|---------|
| `New-SorchaWallet -WalletUrl -Name -Headers [-FetchPublicKey]` | Create ED25519 wallet → `@{ Address, PublicKey }` |
| `Register-SorchaParticipant -TenantUrl -WalletUrl -OrganizationId -WalletAddress -DisplayName -Headers` | Self-register participant + challenge-sign-verify wallet link |
| `Publish-SorchaParticipant -TenantUrl -OrganizationId -RegisterId -ParticipantName -OrganizationName -WalletAddress -PublicKey -Headers` | Publish participant record to register |

### Registers & blueprints

| Function | Purpose |
|----------|---------|
| `New-SorchaRegister -RegisterUrl -WalletUrl -Name -Description -TenantId -OwnerUserId -OwnerWalletAddress -Headers` | Three-phase register creation (initiate → sign attestation → finalize) |
| `Get-SorchaRegisterByName -RegisterUrl -Name -Headers` | Idempotent register lookup (avoids orphan registers on re-run) |
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
│       ├── SorchaWalkthrough.psd1
│       └── SorchaWalkthrough.psm1
├── council/                           # Shared Strathcarron demo universe
│
├── AdminIntegration/                  # Foundation — Blazor WASM + YARP
├── McpServerBasics/                   # Foundation — MCP Server
│
├── RegisterCreationFlow/              # Single-Org — Register lifecycle + CLI
├── WalletVerification/                # Single-Org — Multi-algorithm crypto
├── RegisterMongoDB/                   # Single-Org — MongoDB backend
├── FormCoverage/                      # Single-Org — Every form control
├── HealthDeclaration/                 # Single-Org — Form layout extensions (Feature 091)
│
├── ConstructionPermit/                # Multi-Org — 4 orgs, routing, encryption, VC
├── SelfBuildHouse/                    # Multi-Org — 6 orgs, 2 registers, VC chain
├── PropertyInspection/                # Multi-Org — file uploads, mid-flow VC, cyclic rework
├── PayloadTests/                      # Multi-Org — chunked encrypted file transfer
│
├── AssuredIdentity/                   # Credential — Feature 107 citizen identity + driving licence
├── ForestryCertification/             # Credential — Digital Product Passport (composes into TradeFinance)
├── CyberEssentialsUac/                # Credential — UAC posture VC, route-gated issuance, FailClosed revocation, HAIP SD
│
├── TradeFinance/                      # Agent-Driven — procurement-to-pay + invoice finance, FLE, MCP agents
│
├── DistributedRegister/               # Distributed — P2P replication
├── PingPongN1/                        # Distributed — local ↔ n1.sorcha.dev cross-NAT
│
└── PerformanceBenchmark/              # Performance — TPS, latency, concurrency
```

---

## Creating a New Walkthrough

1. Create directory: `walkthroughs/YourWalkthrough/`
2. Add `config.json` with name, description, category, organisation details
3. Add `setup.ps1` — import module, bootstrap orgs/users/wallets, create register, publish blueprint, save `state.json`
4. Add `run.ps1` — import module, load `state.json`, authenticate users, execute scenarios, report pass/fail
5. Add secrets entry to `initialize-secrets.ps1`
6. Add entry to the `$core` array in `run-all.ps1` (if part of the regression suite), choosing `Kind`: `setup` (receives the target + -Force), `run` (reads URLs from state.json), or `runp` (needs the target passed explicitly)
7. Update this README

### config.json template

```json
{
  "name": "YourWalkthrough",
  "description": "Brief description of what this tests.",
  "category": "foundation|single-org|multi-org|credential|agent|distributed|performance",
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
