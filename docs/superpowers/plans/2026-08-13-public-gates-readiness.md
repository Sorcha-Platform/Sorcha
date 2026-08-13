# Public Gates Readiness — Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Make Sorcha safe and legible enough to invite external humans and autonomous agents to use, test, and give feedback on — across self-host, the shared n1 node, docs, and MCP — without the maintainer babysitting it.

**Architecture:** Five workstreams. WS0 (Safety Gate) is blocking: no public invitation until it is verified green by *live re-execution*, not by a passing unit suite. WS1–WS4 touch disjoint files and can be dispatched in parallel to different agents once WS0 is underway.

**Tech Stack:** .NET 10 / C# 14, ASP.NET Core Minimal APIs, YARP (API Gateway), xUnit v3 + FluentAssertions + Moq, PowerShell gate scripts, GitHub Actions, Docker Compose.

## Global Constraints

- License header on every new `.cs` file: `// SPDX-License-Identifier: MIT` then `// Copyright (c) 2026 Sorcha Contributors`.
- File-scoped namespaces; `dotnet build` before running tests.
- `dotnet test` takes ONE project at a time. xUnit v3; MTP filter via `-- --filter-class "*Name*"`.
- Test naming: `MethodName_Scenario_ExpectedBehavior`.
- Never `git add -A` — stage explicit paths (the working tree may carry unrelated untracked files).
- Branch + PR for every change; never commit to `master`. One logical change per PR.
- Commit trailer: `Co-Authored-By: Claude Fable 5 <noreply@anthropic.com>`.
- CI gate scripts live in `scripts/check-*.ps1`, are wired via a per-gate `.github/workflows/*-gate.yml`, use a repo-root dotfile allowlist that fails closed if missing, and signal violations with `exit 1`.
- Rate-limit config binds the `"RateLimiting"` section to `RateLimitSettings` (flat int fields); `.Bind()` is a partial override, so a Production file need only set the fields it tightens.

---

## Workstream index

- **WS0 — Safety Gate (BLOCKING):** Tasks 1–6
- **WS1 — First-Run Experience:** Tasks 7–9
- **WS2 — Agent / AI On-Ramp:** Tasks 10–12
- **WS3 — Feedback Loop:** Tasks 13–14
- **WS4 — Presentability Polish:** Tasks 15–17
- **WS-FINAL — Public invitation (gated on WS0 green + WS1 + WS3):** Task 18

---

# WS0 — Safety Gate (BLOCKING)

Verification standard for this workstream: a task is done when the *live behaviour* changes, not when a test compiles green. The #1397 lesson is that ~2,500 passing tests missed an internet-reachable signing oracle.

---

### Task 1: Remove `/api/service-auth/token` from the public gateway route

The cheapest, lowest-risk kill-switch for #1397 step 1. Services authenticate by calling the Tenant Service *directly* inside the Docker network (`tenant-service:8080`), not through the public gateway, so removing the public route does not break inter-service auth. `/api/internal/*` already works this way — it has no gateway route and falls through to the UI catch-all → 404.

**Files:**
- Modify: `src/Services/Sorcha.ApiGateway/appsettings.json` (the `service-auth` route, ~line 358, in `ReverseProxy.Routes`)
- Test: `tests/Sorcha.ApiGateway.Tests/` (locate the existing route-config test project; if none exists, this task's verification is the live curl in Step 4 only — do NOT invent a test project)

**Interfaces:**
- Produces: no code symbols. The observable contract is that `/api/service-auth/{**catch-all}` is no longer proxied to `tenant-cluster` from the gateway.

- [ ] **Step 1: Read the current route block and its neighbours**

Read `src/Services/Sorcha.ApiGateway/appsettings.json` around the `service-auth` route:
```json
"service-auth": { "ClusterId": "tenant-cluster", "Match": { "Path": "/api/service-auth/{**catch-all}" } },
```
Note it also covers `/api/service-auth/token/delegated` and `/api/service-auth/rotate-secret`. Confirm whether any of those three are legitimately called by an *external* client (they are not for the current deployment shape — services call the Tenant Service directly). If uncertain, grep the repo for `service-auth/rotate-secret` and `service-auth/token/delegated` external callers before removing.

- [ ] **Step 2: Delete the `service-auth` route entry**

Remove the entire `"service-auth": { ... }` object from `ReverseProxy.Routes`. Leave the `tenant-cluster` cluster definition intact (other routes use it).

- [ ] **Step 3: Build the gateway**

Run: `dotnet build src/Services/Sorcha.ApiGateway`
Expected: build succeeds (config-only change; a malformed JSON would fail `ValidateOnStart`).

- [ ] **Step 4: Verify locally (or note the n1 verification)**

Bring the stack up (or deploy to a scratch node) and run:
```bash
curl -s -o /dev/null -w '%{http_code}\n' -X POST http://localhost/api/service-auth/token \
  --data-urlencode grant_type=client_credentials --data-urlencode client_id=service-blueprint \
  --data-urlencode client_secret=blueprint-service-secret --data-urlencode scope=register:write
```
Expected: `404` (was `200`). Then confirm the stack still comes up healthy — inter-service auth uses the internal network, so all services should report healthy in the Aspire dashboard / `docker compose ps`.

- [ ] **Step 5: Commit**

```bash
git add src/Services/Sorcha.ApiGateway/appsettings.json
git commit -m "fix: [1397] - Take /api/service-auth/token off the public gateway route

Services authenticate against the Tenant Service directly inside the Docker
network, so the public gateway route was pure attack surface: it accepted the
committed dev client secrets and minted service-tier tokens to anyone. Removed
the route; it now falls through to the UI catch-all → 404 externally, the same
way /api/internal/* already does."
```

---

### Task 2: Restrict system-wallet signing to the Validator service principal

Independent second kill-switch for #1397 step 2. Even with a valid service token, the oracle's power comes entirely from signing with the `validator:*`-owned system wallet (the docket-signing / SSR-owner key). Service tokens legitimately bypass ownership for *user* wallets (Blueprint signs the issuing org's wallet during issuance), so we cannot remove the bypass wholesale — we narrow it: a `validator:*`-owned wallet may only be signed by the Validator principal.

**Files:**
- Modify: `src/Services/Sorcha.Wallet.Service/Endpoints/WalletEndpoints.cs` (the `isService` branch inside `SignTransaction`, ~lines 869–896)
- Test: `tests/Sorcha.Wallet.Service.Tests/` (find the endpoint/authorization test folder used by existing wallet-sign tests)

**Interfaces:**
- Consumes: the service-tier JWT claims. The Validator principal's identity in the token must be confirmed in Step 1 (candidate: `client_id` claim == `service-validator`, or `sub` == the `ValidatorServicePrincipalId` GUID). Use whichever the token actually carries — verify, do not guess.
- Produces: `SignTransaction` returns `403 Forbid` when a non-Validator service token targets a wallet whose `Owner` starts with `validator:`.

- [ ] **Step 1: Confirm how the Validator principal is identifiable in the token**

Read `src/Services/Sorcha.Tenant.Service/Services/TokenService.cs` (`GenerateServiceTokenAsync`, ~lines 169–220) and confirm which claim carries the principal's `client_id`/`ServiceName`. Read `DatabaseInitializer.cs` (~lines 389–465) for the exact `ClientId` string of the Validator principal (`service-validator`) and `ValidatorServicePrincipalId`. Pick the claim that is present and stable. Record the exact claim type + value you will match on.

- [ ] **Step 2: Write the failing test**

In the wallet-sign test file, add (adjust namespaces/fixtures to match the existing sign tests):
```csharp
[Fact]
public async Task SignTransaction_NonValidatorServiceToken_SystemWallet_Returns403()
{
    // A service token that is NOT the Validator principal (e.g. service-blueprint)
    // must not sign a validator:*-owned system wallet.
    var context = BuildServiceHttpContext(clientId: "service-blueprint");
    var systemWalletAddress = SeedSystemWallet(owner: "validator:local-validator");

    var result = await WalletEndpoints.SignTransaction(
        systemWalletAddress, ValidSignRequest(), context, WalletManagerMock.Object, Logger, default);

    result.Should().BeOfType<ForbidHttpResult>();
}

[Fact]
public async Task SignTransaction_ValidatorServiceToken_SystemWallet_Proceeds()
{
    var context = BuildServiceHttpContext(clientId: "service-validator");
    var systemWalletAddress = SeedSystemWallet(owner: "validator:local-validator");

    var result = await WalletEndpoints.SignTransaction(
        systemWalletAddress, ValidSignRequest(), context, WalletManagerMock.Object, Logger, default);

    result.Should().NotBeOfType<ForbidHttpResult>();
}
```
If the existing tests call the handler through a different seam (e.g. a WebApplicationFactory integration test), mirror that seam instead of calling the static handler directly.

- [ ] **Step 3: Run the tests to verify they fail**

Run: `dotnet test tests/Sorcha.Wallet.Service.Tests -- --filter-class "*WalletEndpoints*"` (or the correct class filter)
Expected: the non-Validator test FAILS (currently the service branch skips all checks and proceeds).

- [ ] **Step 4: Implement the narrowed check**

In `SignTransaction`, inside the existing `if (isService)` handling (the block that currently skips ownership), add before proceeding to the signing call. Use the claim confirmed in Step 1:
```csharp
if (isService)
{
    var wallet = await walletManager.GetWalletAsync(address, cancellationToken);
    if (wallet is not null && wallet.Owner is not null &&
        wallet.Owner.StartsWith("validator:", StringComparison.Ordinal))
    {
        // System wallets hold the docket-signing / SSR-owner key. Only the Validator
        // service principal may sign with them; any other service token targeting one
        // is the #1397 oracle and is refused.
        var clientId = context.User.Claims.FirstOrDefault(c => c.Type == "client_id")?.Value;
        if (!string.Equals(clientId, "service-validator", StringComparison.Ordinal))
        {
            logger.LogWarning(
                "SEC-AUDIT: service principal {ClientId} attempted to sign system wallet {Wallet}",
                clientId, address);
            return Results.Forbid();
        }
    }
}
```
Note: this adds a `GetWalletAsync` call on the service path. If the handler already fetches the wallet later, hoist that fetch rather than duplicating it.

- [ ] **Step 5: Run the tests to verify they pass**

Run: `dotnet test tests/Sorcha.Wallet.Service.Tests -- --filter-class "*WalletEndpoints*"`
Expected: both new tests PASS; existing sign tests still PASS (user-tier ownership and legitimate Blueprint issuance on user wallets are unaffected — those wallets are not `validator:*`-owned).

- [ ] **Step 6: Commit**

```bash
git add src/Services/Sorcha.Wallet.Service/Endpoints/WalletEndpoints.cs tests/Sorcha.Wallet.Service.Tests/
git commit -m "fix: [1397] - System-wallet signing is Validator-principal-only

Service tokens legitimately bypass ownership for user wallets (issuance), but a
validator:*-owned system wallet holds the docket-signing/SSR-owner key. Any
service principal other than service-validator targeting one is the #1397
oracle. Narrowed the service bypass; other service tokens now get 403."
```

---

### Task 3: Design + fix the per-deploy service-secret model

The committed dev secrets (`blueprint-service-secret`, …) are live only under `ASPNETCORE_ENVIRONMENT=Development`; in Production the seeder generates random secrets. BUT the compose client-secret literals are coupled to the Development seed, so flipping to Production breaks inter-service auth unless secrets are generated per-deploy and injected into *both* the seed and each service's client config. This is the "proper" fix behind Tasks 1–2; it is more involved and must not regress a running node.

**Files:**
- Read first: `src/Services/Sorcha.Tenant.Service/Data/DatabaseInitializer.cs` (~lines 389–481), `src/Services/Sorcha.Tenant.Service/Services/ServiceAuthService.cs`, `docker-compose.yml` (the 8 `ServiceAuth__ClientSecret` lines: 240, 298, 397, 452, 517, 588, 718, 754), `scripts/sorcha-setup.sh` (`write_env_file` ~384–442, `generate_jwt_key` ~119–129)
- Modify: `docker-compose.yml`, `scripts/sorcha-setup.sh`, possibly `DatabaseInitializer.cs`
- Create: a short design note `docs/superpowers/specs/2026-08-13-service-secret-model.md` before coding

- [ ] **Step 1: Trace the secret flow and write the design note**

Answer, in the note, with file:line evidence: (a) In Production, how does a service learn the random secret the seeder generated — is there a path, or is Production seeding actually unfinished? (b) Does compose's `ServiceAuth__ClientSecret` feed the *client* side (outbound auth) while `DatabaseInitializer` feeds the *server* side (what's accepted)? (c) The minimal change that makes both sides read the same per-deploy value. Recommend one approach: **generate 8 secrets in `sorcha-setup.sh`, write them to `config/.env` as `*_SERVICE_SECRET` vars, change the 8 compose lines to `${..._SERVICE_SECRET}`, and have `DatabaseInitializer` seed from those env vars (not literals, not random) in every environment.** Get maintainer sign-off on the note before Step 2 (this touches a running node's auth).

- [ ] **Step 2: Implement per Step 1's approved approach**

Follow the note. Add `generate_service_secret()` beside `generate_jwt_key()`; write the 8 vars in `write_env_file`; parameterise the 8 compose lines; make `DatabaseInitializer` read `ServiceAuth__ClientSecret`-equivalent env per principal. Keep the committed compose values as non-functional placeholders (or `${VAR}` with no default) so a bare `git clone && docker compose up` without a generated `.env` fails loudly rather than silently shipping known secrets.

- [ ] **Step 3: Verify inter-service auth still works end to end**

Fresh `.env` via `sorcha-setup.sh`, stack up, run a walkthrough that exercises issuance (Blueprint → Wallet sign). Expected: completes. Then confirm the old literal secret is dead:
```bash
curl ... client_secret=blueprint-service-secret ...   # expect 401 (secret no longer valid)
```

- [ ] **Step 4: Commit**

```bash
git add docs/superpowers/specs/2026-08-13-service-secret-model.md docker-compose.yml scripts/sorcha-setup.sh src/Services/Sorcha.Tenant.Service/Data/DatabaseInitializer.cs
git commit -m "fix: [1397] - Per-deploy service secrets; committed literals no longer valid"
```

---

### Task 4: Production rate-limit configuration

No `appsettings.Production.json` exists anywhere; n1 runs the 100k/min dev defaults. Add production rate limits so a public node self-limits abuse.

**Files:**
- Create: `src/Services/Sorcha.Tenant.Service/appsettings.Production.json`, and one per other public-facing service that hosts abusable endpoints (Wallet, Register, Blueprint, ApiGateway). Start with Tenant (auth) + Wallet (sign) + Gateway; extend if a burst test finds a gap.

**Interfaces:**
- Produces: layered config picked up automatically when `ASPNETCORE_ENVIRONMENT=Production`. `.Bind()` is partial, so only tightened fields are set.

- [ ] **Step 1: Create the Tenant Service production file**

`src/Services/Sorcha.Tenant.Service/appsettings.Production.json`:
```json
{
  "RateLimiting": {
    "PlatformAuthPermitLimit": 10,
    "PlatformAuthQueueLimit": 0,
    "TotpPermitLimit": 5,
    "TotpQueueLimit": 0,
    "ApiPermitLimit": 600,
    "ApiQueueLimit": 0,
    "AuthenticationPermitLimit": 60,
    "AuthenticationQueueLimit": 0
  }
}
```

- [ ] **Step 2: Create the Wallet Service production file**

`src/Services/Sorcha.Wallet.Service/appsettings.Production.json`:
```json
{
  "RateLimiting": {
    "StrictTokenLimit": 60,
    "StrictTokensPerPeriod": 10,
    "StrictReplenishmentPeriodSeconds": 1,
    "StrictQueueLimit": 0,
    "ApiPermitLimit": 600,
    "ApiQueueLimit": 0
  }
}
```

- [ ] **Step 3: Create the API Gateway production file**

`src/Services/Sorcha.ApiGateway/appsettings.Production.json` with the same `ApiPermitLimit`/`AuthenticationPermitLimit`/`StrictTokenLimit` tightening (gateway-level limiter policies mirror the service names). Match the field set the gateway's limiter actually references (read its `Program.cs` limiter registration first; only set fields that exist).

- [ ] **Step 4: Build each modified service**

Run: `dotnet build src/Services/Sorcha.Tenant.Service` (and Wallet, Gateway).
Expected: succeeds. `ValidateOnStart` requires all limits > 0 — do not set any field to 0 except the documented `*QueueLimit` fields (queue 0 = immediate 429, which is intended).

- [ ] **Step 5: Verify a burst throttles (live)**

Deploy to a scratch node / n1 with `ASPNETCORE_ENVIRONMENT=Production` and burst a public endpoint:
```bash
for i in $(seq 1 30); do curl -s -o /dev/null -w '%{http_code} ' -X POST https://<node>/api/auth/login -d '{"email":"x","password":"y"}' -H 'Content-Type: application/json'; done; echo
```
Expected: `429` appears after ~10 requests (PlatformAuth policy).

- [ ] **Step 6: Commit**

```bash
git add src/Services/Sorcha.Tenant.Service/appsettings.Production.json src/Services/Sorcha.Wallet.Service/appsettings.Production.json src/Services/Sorcha.ApiGateway/appsettings.Production.json
git commit -m "feat: [OPS] - Production rate limits for public-facing services"
```

---

### Task 5: n1 reset / re-genesis runbook (self-healing)

Capture one documented procedure to wipe → re-genesis n1 (coordinated with tiny inside the `VAL_TIME_002` window) → re-provision AIAS → `rehearse.ps1` green, including the T069 test-register cleanup (#1403). Decide on-demand vs scheduled.

**Files:**
- Create: `docs/operations/n1-reset-runbook.md`

- [ ] **Step 1: Draft the runbook from the existing skills**

Assemble from the `network-bootstrap`, `n1-deploy`, and `demo-deploy` skills the exact command sequence: `docker compose down -v` → re-genesis ceremony → import validator key → bring up → coordinate tiny (`~/sorcha-test`, gateway :8090, SSH-only) inside the 1-hour window → AIAS re-provision → `rehearse.ps1 -Target n1`. Include the #1403 register-cleanup sweep. Write it so an agent with the three skills can execute it cold.

- [ ] **Step 2: Execute it once, end to end**

Run the runbook against n1. Record the `rehearse.ps1` green result and the timestamp in the runbook as proof-of-execution.

- [ ] **Step 3: Write the cadence decision**

Add a short "Cadence" section: on-demand (after observed abuse) vs scheduled (e.g. weekly cron). Recommend on-demand initially, revisit once traffic exists. If scheduled, note where the cron would live (the `prodexec`/orchestrator box or a GitHub Actions scheduled workflow hitting n1).

- [ ] **Step 4: Commit**

```bash
git add docs/operations/n1-reset-runbook.md
git commit -m "docs: [OPS] - n1 reset/re-genesis runbook, executed once green"
```

---

### Task 6: Secret-scan CI gate

No secret scanning exists. Add a `check-secrets.ps1` gate in the established ratchet style so no usable secret pattern lands again.

**Files:**
- Create: `scripts/check-secrets.ps1`, `.github/workflows/secrets-gate.yml`, `.secrets-allowlist` (repo root)
- Test: a deliberate fake secret to prove the gate reds, then removed

- [ ] **Step 1: Write the gate script**

`scripts/check-secrets.ps1`, following the `check-no-snackbar.ps1` template (header with exit-code doc; `param([string]$RepoRoot = (Resolve-Path "$PSScriptRoot/..").Path)`; `Set-StrictMode`; load `.secrets-allowlist` failing closed if missing; scan `src/`, `docker-compose*.yml`, `scripts/`, excluding `obj|bin`; ratchet on stale allowlist entries; `exit 1` on violation). Patterns to flag:
```powershell
$patterns = @(
    '(?i)(client_?secret|service[-_]secret|api[-_]?key|password)\s*[:=]\s*["'']?[A-Za-z0-9_\-]{8,}',
    '-----BEGIN (RSA |EC |OPENSSH )?PRIVATE KEY-----'
)
```
Seed `.secrets-allowlist` with the now-parameterised compose lines only if Task 3 has not yet neutralised them; a from-scratch scanner ideally needs no grandfathering, so prefer an empty allowlist once Task 3 lands.

- [ ] **Step 2: Write the workflow**

`.github/workflows/secrets-gate.yml`, copying `snackbar-gate.yml`:
```yaml
name: secrets-gate
on:
  pull_request:
    paths:
      - "src/**"
      - "scripts/**"
      - "docker-compose*.yml"
      - ".secrets-allowlist"
      - "scripts/check-secrets.ps1"
      - ".github/workflows/secrets-gate.yml"
  workflow_dispatch:
jobs:
  no-committed-secrets:
    runs-on: ubuntu-latest
    timeout-minutes: 5
    steps:
      - uses: actions/checkout@v4
      - name: Run secret-scan gate
        shell: pwsh
        run: ./scripts/check-secrets.ps1
```

- [ ] **Step 3: Prove it reds on a planted secret**

Add a line like `ApiKey: AKIAABCDEFGH12345678` to a scratch file under `src/`, run `./scripts/check-secrets.ps1`, expect `exit 1` and the file reported. Remove the planted line; re-run; expect `exit 0`.

- [ ] **Step 4: Commit**

```bash
git add scripts/check-secrets.ps1 .github/workflows/secrets-gate.yml .secrets-allowlist
git commit -m "feat: [CI] - Secret-scan gate in the ratchet style"
```

- [ ] **Step 5: Document it**

Add a one-line entry to the CI-gates list in `CLAUDE.md` (§ pattern list) and `CONTRIBUTING.md` if it enumerates gates.

---

# WS1 — First-Run Experience

### Task 7: `SECURITY.md`

**Files:** Create: `SECURITY.md` (repo root)

- [ ] **Step 1: Write the policy**

Create `SECURITY.md` with: supported scope (this is pre-release demo software; n1 is a shared sandbox), a coordinated-disclosure contact (email — confirm the address with the maintainer; do not invent one), expected response window, and an explicit "do not run production secrets against n1 / do not treat n1 data as private" note. Reference #1397 as an example of the kind of report valued.

- [ ] **Step 2: Link + verify**

Add a "Security" link to `README.md`. Push the branch and confirm GitHub's repo "Security policy" shows detected/green.

- [ ] **Step 3: Commit**

```bash
git add SECURITY.md README.md
git commit -m "docs: Add SECURITY.md with coordinated-disclosure policy"
```

---

### Task 8: The golden-path walkthrough

One canonical, executable, end-to-end walkthrough: install → sign in → instantiate a blueprint → complete one action → see the immutable record. Reuse the actor-based walkthrough framework so it can't silently rot.

**Files:**
- Create: `walkthroughs/GoldenPath/` (config.json + setup.ps1 + run.ps1 following the README's 7-step recipe)
- Modify: `README.md` (point "Try it in one line" at it), `walkthroughs/README.md` (index entry)

- [ ] **Step 1: Pick the simplest existing walkthrough as the base**

Read `walkthroughs/README.md` and `walkthroughs/RegisterCreationFlow/run.ps1`. Choose the minimal single-org happy path. Do NOT author a new blueprint if an existing sample covers it — reuse.

- [ ] **Step 2: Build the GoldenPath launcher**

Create `walkthroughs/GoldenPath/` per the recipe: `config.json`, idempotent `setup.ps1`, `run.ps1` that drives the five steps and asserts the final immutable record exists (a transaction/docket query returning the sealed action).

- [ ] **Step 3: Run it green against a fresh stack**

Run: `./walkthroughs/GoldenPath/run.ps1`
Expected: completes with the final assertion passing. Capture the run output.

- [ ] **Step 4: Wire it into the README**

Update `README.md` so the one-liner's "when it finishes" line links to `walkthroughs/GoldenPath/` as the first thing to try. Add the index row to `walkthroughs/README.md`.

- [ ] **Step 5: Commit**

```bash
git add walkthroughs/GoldenPath/ README.md walkthroughs/README.md
git commit -m "docs: GoldenPath walkthrough — zero to one sealed action, executable"
```

---

### Task 9: Seed sample content on a fresh stack

A newly installed stack should present at least one explorable workflow without the user authoring anything.

**Files:**
- Read first: how the installer / first-run seeds data (grep the setup script and any `DatabaseInitializer`/seed path for blueprint seeding). Modify the existing seed seam; do not invent a parallel one.

- [ ] **Step 1: Locate the seed seam**

Grep for where sample blueprints/data are (or aren't) seeded on first run. Determine whether GoldenPath's `setup.ps1` (Task 8) already leaves explorable content, in which case this task is just ensuring it runs as part of the default install.

- [ ] **Step 2: Wire a minimal demo seed into the default install**

Make a fresh `sorcha-setup.sh` (non-`--quiet` and `--quiet`) leave one demo blueprint + one instance visible in the UI. Prefer invoking GoldenPath's setup over duplicating seed logic.

- [ ] **Step 3: Verify on a clean install**

Fresh clone → install → open `/app`. Expected: at least one workflow is visible without any authoring.

- [ ] **Step 4: Commit**

```bash
git add <the seed/setup files touched>
git commit -m "feat: Seed one explorable workflow on a fresh install"
```

---

# WS2 — Agent / AI On-Ramp

### Task 10: Refresh `llms.txt` + add `AGENTS.md`

**Files:** Modify: `llms.txt`, `docs/llms-full.txt`. Create: `AGENTS.md` (repo root).

- [ ] **Step 1: Regenerate `llms.txt` against current features**

Read the current `llms.txt` (3 months stale) and `docs/llms-full.txt`. Update the feature list, entry points, and links to match the current README + `docs/reference/`. Remove dead links (cross-check against `docs/` structure).

- [ ] **Step 2: Write `AGENTS.md`**

Create root `AGENTS.md` pointing an autonomous agent at: the external-agent MCP quickstart (Task 11), the GoldenPath walkthrough (Task 8), the issue templates (Task 13), and the "what's real vs demo" honesty page (Task 16). Keep it short and imperative — an agent's first read.

- [ ] **Step 3: Verify links resolve**

Check every link in `AGENTS.md` and `llms.txt` resolves to a real path/URL (the repo has a `link-check` CI job — ensure it passes).

- [ ] **Step 4: Commit**

```bash
git add llms.txt docs/llms-full.txt AGENTS.md
git commit -m "docs: Refresh llms.txt; add AGENTS.md agent entry point"
```

---

### Task 11: External-agent MCP quickstart

**Files:** Create: `docs/guides/mcp-agent-quickstart.md`. Modify: `README.md` (AI-integration row links to it).

- [ ] **Step 1: Read the MCP server surface**

Read the MCP server README / `src/Apps/Sorcha.McpServer` for how an external agent points it at a node, authenticates (JWT), and which of the 36 tools drive a workflow. Note the auth flow an *external* agent uses (not the internal service path).

- [ ] **Step 2: Write the quickstart with a runnable example**

Document: connect the MCP server to a node (self-host or n1), authenticate, and drive one workflow to completion, with a copy-pasteable example (the `docker-compose run mcp-server --jwt-token <token>` path from CLAUDE.md is the starting point). End with what success looks like (a sealed action).

- [ ] **Step 3: Run the example against a fresh stack**

Execute the documented steps end to end. Expected: a workflow completes via MCP tools. Fix the doc to match reality.

- [ ] **Step 4: Commit**

```bash
git add docs/guides/mcp-agent-quickstart.md README.md
git commit -m "docs: External-agent MCP quickstart, verified end to end"
```

---

### Task 12: Machine-pickable task surface (issue hygiene for agents)

The agent-facing payoff of WS4-3. Ensure `agent-friendly`-labelled issues carry inputs + done-criteria so an agent can pick one cold.

- [ ] **Step 1: Define the bar**

Write, in `CONTRIBUTING.md`, what an `agent-friendly` issue must contain: affected files/area, inputs, explicit done-criteria, how to verify. This is consumed by Task 17's labelling.

- [ ] **Step 2: Commit**

```bash
git add CONTRIBUTING.md
git commit -m "docs: Define the agent-friendly issue bar"
```

---

# WS3 — Feedback Loop

### Task 13: Issue templates

**Files:** Create: `.github/ISSUE_TEMPLATE/bug_report.yml`, `feature_request.yml`, `feedback.yml`, `config.yml`.

- [ ] **Step 1: Write the bug report form**

`.github/ISSUE_TEMPLATE/bug_report.yml` (GitHub issue-forms schema) with required fields: what happened / expected, entry point used (dropdown: self-host / n1 / MCP / docs), version (from the UI footer or `/.well-known/openapi.json` `info.version`), repro steps. Shape so an agent fills it correctly.

- [ ] **Step 2: Write feature_request + feedback forms**

`feature_request.yml` (problem, proposed solution, who benefits) and `feedback.yml` (first-impressions: what entry point, what worked, where you got stuck).

- [ ] **Step 3: Route open-ended questions to Discussions**

`.github/ISSUE_TEMPLATE/config.yml`:
```yaml
blank_issues_enabled: false
contact_links:
  - name: Questions & open-ended feedback
    url: https://github.com/Sorcha-Platform/Sorcha/discussions
    about: For questions and discussion that aren't a specific bug or feature request.
```

- [ ] **Step 4: Verify the chooser renders**

Push the branch; open "New issue" on GitHub; confirm the three forms + the Discussions link appear.

- [ ] **Step 5: Commit**

```bash
git add .github/ISSUE_TEMPLATE/
git commit -m "feat: Issue templates (bug/feature/feedback) + Discussions routing"
```

---

### Task 14: Seed Discussions

Not a code change — a GitHub configuration task for the maintainer or an agent with repo admin.

- [ ] **Step 1: Ensure categories + pinned post**

Confirm Discussions categories (Q&A, Ideas, Show-and-tell, Feedback) exist. Write a pinned post: "How to give feedback, what we're looking for, what's demo-grade" that links to `SECURITY.md`, the GoldenPath walkthrough, and the honesty page (Task 16). Provide the post body as a file `docs/community/discussions-welcome.md` so it's version-controlled and the maintainer can paste it.

- [ ] **Step 2: Commit the source of the pinned post**

```bash
git add docs/community/discussions-welcome.md
git commit -m "docs: Source text for the pinned Discussions welcome post"
```

---

# WS4 — Presentability Polish

### Task 15: README / docs external-reader pass

- [ ] **Step 1: Read as a newcomer**

Read `README.md` and top-level `docs/` as someone with zero context. List every internal codename, unexplained acronym, or dead link. Verify links against the `link-check` CI job locally if possible.

- [ ] **Step 2: Fix**

Explain or remove codenames; ensure the DAD model and the four entry points are graspable in the first screen; fix dead links.

- [ ] **Step 3: Commit**

```bash
git add README.md docs/
git commit -m "docs: README external-reader pass"
```

---

### Task 16: "What's real vs demo" honesty page

**Files:** Create: `docs/reference/maturity-and-limitations.md`. Modify: `README.md`, `AGENTS.md` (link it).

- [ ] **Step 1: Write it plainly**

State what is production-shaped vs demo-grade, and known limitations testers should know: #1380 (org governance key is platform-custodied), the rate-limit posture, replication needs an explicit `full-replica` subscription (not automatic), n1 is a shared wipe-able sandbox. Cross-reference `docs/reference/development-status.md`.

- [ ] **Step 2: Link + commit**

```bash
git add docs/reference/maturity-and-limitations.md README.md AGENTS.md
git commit -m "docs: What's real vs demo — maturity & known limitations"
```

---

### Task 17: Backlog labelling for external contributors

- [ ] **Step 1: Curate + label**

Pick ~10 open issues suitable for outside contributors; bring each up to the Task 12 bar (inputs + done-criteria, editing the issue body); label `good first issue` / `help wanted` / `agent-friendly`. Document the label meanings in `CONTRIBUTING.md`.

- [ ] **Step 2: Commit the CONTRIBUTING update**

```bash
git add CONTRIBUTING.md
git commit -m "docs: Document contributor issue labels"
```

---

# WS-FINAL

### Task 18: Public invitation (GATED)

**Do not start until:** WS0 Tasks 1–6 verified green by live re-execution, AND Task 7 (SECURITY.md) + Task 8 (GoldenPath) + Task 13 (issue templates) merged.

- [ ] **Step 1: Re-run the #1397 repro against n1 from the public internet**

Run the exact repro from issue #1397. Expected: 404/401/403 — not a signature. Attach the evidence to #1397 and close it.

- [ ] **Step 2: Confirm the abuse posture**

Burst-test a public endpoint on n1 (Task 4 Step 5) → 429. Confirm the reset runbook (Task 5) has one recorded green execution.

- [ ] **Step 2: Announce**

Publish the pinned Discussions post, update the README with an "Open for testing" note pointing at n1 + the GoldenPath + the feedback channels. This is the terminal task.

---

## Self-Review (completed at authoring)

**Spec coverage:** WS0-1→Tasks 1/2/3; WS0-2→Task 4; WS0-3→Task 5; WS0-4→Task 6; WS1-1→7; WS1-2→8; WS1-3→9; WS2-1→10; WS2-2→11; WS2-3→12; WS3-1→13; WS3-2→14; WS4-1→15; WS4-2→16; WS4-3→17; the gated public invitation→18. All spec sections mapped.

**Known deliberate non-placeholder gaps** (facts that require a live node or maintainer input, not inventable): the exact claim identifying the Validator principal (Task 2 Step 1 — verify before coding); the disclosure email (Task 7 — confirm, don't invent); the Production secret-flow resolution (Task 3 Step 1 — a design step by design, since the recon showed Production seeding may be unfinished). These are marked as verify/confirm steps, not TODOs.

**Sequencing:** WS0 blocks Task 18. Within WS0, Tasks 1 and 2 are independent kill-switches (either alone breaks the chain) and should land first; Task 3 is the deeper root-cause fix behind them. WS1–WS4 touch disjoint files and parallelise across agents.
