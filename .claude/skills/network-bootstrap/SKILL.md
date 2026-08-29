---
name: network-bootstrap
description: |
  Bootstraps the Sorcha network: genesis ceremony, n1.sorcha.dev deployment, validator key import, and platform setup.
  Use when: Running genesis ceremony, resetting n1, deploying to n1.sorcha.dev, importing validator keys, bootstrapping the platform, starting the sorcha-dev network, or troubleshooting genesis bootstrap failures.
allowed-tools: Read, Edit, Write, Glob, Grep, Bash
---

# Network Bootstrap Skill

Covers the full lifecycle of bootstrapping a Sorcha network node: genesis ceremony, remote deployment, validator key import, and platform bootstrap. The primary target is `n1.sorcha.dev` (Azure VM), but the procedures apply to any deployment.

## Prerequisites

- .NET 10 SDK (for CLI)
- Azure CLI (`az`) logged in
- Docker Compose files committed to `master`
- GitHub Actions Docker Publish workflow configured
- n1 VM provisioned (see `scripts/n1-deploy.ps1`)

## End-to-End Bootstrap Procedure

### Step 1: Genesis Ceremony (Offline, Local)

Generate the pre-signed genesis block and validator key. No services needed.

```bash
dotnet run --project src/Apps/Sorcha.Cli -- system-register create --network-id sorcha-dev
```

**Outputs:**
- `src/Common/Sorcha.Register.Models/Resources/system-register-genesis.json` (embedded in assembly)
- `temp/genesis-validator-key.json` (the gitignored `/temp` dir — DO NOT commit)

**CRITICAL:** Store `temp/genesis-validator-key.json` securely, then destroy it after import. The mnemonic controls all keys derived from this wallet — not just the genesis signing key.

> The ceremony writes it into the gitignored `/temp` rather than the repo root. The root default left
> a `genesis-validator-key.json.bak-pre471` sitting untracked for weeks, and `.gitignore` matched only
> the exact filename — which a `.bak-*` suffix defeats. `.gitignore` now globs
> `genesis-validator-key*` as well, so the family is excluded wherever it lands.

> **Genesis freshness window — RESOLVED (2026-06-01, F145 session; static trace).** The 1h window is the intended, enforced, unit-tested behaviour. Genesis is **NOT exempt** — by design it gets a *separate, short* freshness window:
> - **`ValidateTiming` applies `GenesisMaxAge` (default 1h) to genesis** (vs `MaxTransactionAge`/1h for live txs), rejecting with `VAL_TIME_002` when `CreatedAt` is older. This is a deliberate **replay guard**: a stale-but-accepted genesis lets an attacker seed/hijack a bootstrapping node (rationale in `ValidationEngineConfiguration.GenesisMaxAge` + `ValidateTiming`). Unit-tested: `ValidationEngineTests` VAL_TIME_002 genesis cases (too-old → reject; within-window → pass).
> - **All validator validation paths hit it.** `GenesisIngestionService` submits genesis to the validator (`CreatedAt = SignedAt`, real timestamp); every validate path — unverified-pool poller (`ValidateBatchAsync`), `TransactionReceiver`, `DocketConfirmer`, `ConsensusEngine` — calls `ValidateTransactionAsync` → `ValidateTiming`. No genesis bypass exists in the pipeline. The earlier "leniency" note referenced **pre-current code** (old `ValidationEngine.cs:1596`); the stale `IsGenesisTransaction` comment that claimed "must be exempt" has been corrected.
> - **Practical guidance:** for the **ingest-and-seal** path (local Auto bootstrap), mint + deploy + bootstrap **within the hour** or genesis is rejected `VAL_TIME_002` — re-mint if it has aged out. A **SyncOnly replica PULLING an already-sealed genesis docket** verifies the sealed docket's validator signature + chain, **not** the genesis tx's age, so it is never window-bound. Regenerating the embedded genesis still needs a Docker Publish (~12–15 min) before deploy. (If an aged genesis is ever observed ingesting-and-sealing on current code, treat as a new bug — check for a `GenesisMaxAge` config override and confirm the path isn't a pulled sealed docket.)

> **DevMode lives in the genesis `CryptoPolicy.DevMode` (Feature 137), not a top-level field.** Default `false` (encrypted/production) — `CryptoPolicy.CreateDefault()` serialises `"devMode": false`. When `true`, the register skips field-level payload encryption (still disclosure-filtered + routed + calculated) and stores plaintext; receivers read it directly. It is baked into the signed genesis so it **replicates to every node**. The transition is **one-way**: a register may be promoted DevMode→Normal via a crypto-policy-update control tx, never Normal→DevMode (the validator rejects re-enabling DevMode). The system register should always be DevMode off; per-workflow registers (e.g. AssuredIdentity) choose at creation. NEVER enable DevMode in a real production network.
>
> **Promotion is `POST /api/registers/{id}/disable-dev-mode`, and it is asynchronous.** It submits the crypto-policy control tx through the Validator; `Register.DevMode` flips on each node when that tx **seals into a docket**, not when the call returns (poll `GET /api/registers/{id}`). Until 2026-08-06 this endpoint wrote the control tx straight to Mongo, so it never sealed and the flag never flipped while still returning `200 {"status":"submitted"}` — if you are on an older image, verify the tx actually landed in a docket's `TransactionIds` before believing the promotion happened. `PUT /api/registers/{id}/devmode` (a bidirectional direct flag flip that bypassed the one-way guard) has been **removed**. Note that promotion is **not retrospective**: payloads sealed while the register was in DevMode stay plaintext permanently.

### Step 2: Embed Genesis and Push to CI

The genesis file is an embedded resource in `Sorcha.Register.Models`. After the ceremony updates it in-place, commit and push to trigger Docker Publish:

```bash
git checkout -b chore/genesis-ceremony
git add src/Common/Sorcha.Register.Models/Resources/system-register-genesis.json
git commit -m "chore: run fresh genesis ceremony for sorcha-dev network"
git push -u origin chore/genesis-ceremony
gh pr create --fill && gh pr merge --squash --auto
```

Wait for the Docker Publish workflow to complete (rebuilds ALL images since `Common/` changed):

```bash
gh run list --workflow=docker-publish.yml --branch=master --limit 1
```

### Step 3: Reset n1 — **use SSH; the Azure route is DEAD (verified 2026-08-29)**

> ⚠ **`az vm run-command` no longer reaches n1.** `sorcha-n1-vm` exists in **neither** subscription on
> this account, and the `sorcha-n1-uk` resource group is **empty** — while n1 serves normally at
> 51.105.7.135. The VM has been moved, recreated outside these subscriptions, or is no longer
> Azure-hosted. Every `az vm` command below will fail with `ResourceNotFound`.
>
> **SSH works and is currently the only route**, despite the note this skill used to carry:
> ```bash
> ssh sorcha@51.105.7.135 "cd /opt/sorcha && docker compose ... "
> ssh tiny "cd /home/stuart/sorcha-test && docker compose ... "
> ```
>
> ⚠ **`az account show` reads the LOCAL CACHE** and will report a healthy session when there is none.
> Prove a session with `az account get-access-token`, never `account show`. And the duplicate-account
> crash (azure-cli #20168) is cleared with
> `az config set core.enable_broker_on_windows=false` then `az logout` / `az login --use-device-code`.

> ⚠ **`docker-compose.tiny.yml` is NODE-LOCAL and is NOT in the repo.** Curling it from master returns
> a 404 **body**, and `curl -o` writes that over the real file. Refresh only repo-tracked compose
> files, and use `curl -f` so a 4xx fails instead of overwriting. A good copy lives at
> `/home/stuart/sorcha-demo/docker-compose.tiny.yml`; the `.bak-*` in `sorcha-test` is a stale
> Feature-143 shape that would put tiny into `GenesisFile` mode and split the network.

> ⚠ **Walkthrough `state.json` is NODE state.** After a wipe each `walkthroughs/*/state.json` still
> points at orgs and users that no longer exist and the walkthrough **401s during org bootstrap**.
> Clear them with the node, or the suite fails for reasons that look like platform defects.

The historical Azure instructions are retained below for the day the VM returns to a subscription
this account can see.

SSH key auth to n1 was once unreliable; `az vm run-command` was the workaround.

**Check VM is running:**

```bash
az vm get-instance-view --resource-group sorcha-n1-uk --name sorcha-n1-vm \
  --query "instanceView.statuses[1].displayStatus" --output tsv
```

**Start VM if auto-shutdown stopped it:**

```bash
az vm start --resource-group sorcha-n1-uk --name sorcha-n1-vm
```

**Tear down and wipe all data:**

```bash
az vm run-command invoke --resource-group sorcha-n1-uk --name sorcha-n1-vm \
  --command-id RunShellScript --scripts \
  "cd /opt/sorcha && docker compose -f docker-compose.yml -f docker-compose.n1.yml -f docker-compose.ports.yml down -v --remove-orphans"
```

**Update compose files from GitHub:**

```bash
az vm run-command invoke --resource-group sorcha-n1-uk --name sorcha-n1-vm \
  --command-id RunShellScript --scripts '
cd /opt/sorcha
REPO_RAW="https://raw.githubusercontent.com/Sorcha-Platform/Sorcha/master"
curl -sL "$REPO_RAW/docker-compose.yml" -o docker-compose.yml
curl -sL "$REPO_RAW/docker-compose.n1.yml" -o docker-compose.n1.yml
curl -sL "$REPO_RAW/docker-compose.ports.yml" -o docker-compose.ports.yml
curl -sL "$REPO_RAW/docker-compose.seed.yml" -o docker-compose.seed.yml
curl -sL "$REPO_RAW/docker/postgres-init.sql" -o docker/postgres-init.sql
curl -sL "$REPO_RAW/scripts/n1-setup-remote.sh" -o n1-setup-remote.sh
chmod +x n1-setup-remote.sh
'
```

**Pull images and start (seed mode for a fresh network):**

`docker-compose.n1.yml` defaults to `SystemRegister__BootstrapMode: SyncOnly`,
which is correct for replica nodes but will leave the seed waiting forever
for a peer that doesn't exist. For the very first node of a new network,
stack `docker-compose.seed.yml` on top — it flips the register service to
`GenesisFile` so it ingests the embedded trust anchor and bootstraps the
governance blueprints. Drop the seed override on subsequent restarts
(local storage will find the system register before either path runs).

```bash
az vm run-command invoke --resource-group sorcha-n1-uk --name sorcha-n1-vm \
  --command-id RunShellScript --scripts '
cd /opt/sorcha
COMPOSE_FILES="-f docker-compose.yml -f docker-compose.n1.yml -f docker-compose.seed.yml -f docker-compose.ports.yml"
docker compose $COMPOSE_FILES pull
docker volume create sorcha_wallet-encryption-keys 2>/dev/null || true
docker run --rm -v sorcha_wallet-encryption-keys:/data alpine chown -R 1654:1654 /data
docker compose $COMPOSE_FILES up -d
'
```

Replica nodes and routine n1 restarts omit `-f docker-compose.seed.yml`.

### Step 4: Platform bootstrap is AUTOMATIC — there is no manual bootstrap step (verified 2026-06-02)

`DatabaseInitializer` in tenant-service **auto-seeds the platform on startup**. The moment the full stack is healthy (i.e. as part of Step 5's "bring up the rest of the stack") it has already created: the System Admin Org (`00000000-…-0001` "Sorcha Local" / subdomain `sorcha-local`), the Public Org (`00000000-…-0002` "Sorcha Public" / `public`), the admin `PlatformUser` + `UserIdentity` + org membership, `PlatformSettings`, and the system-register Owner subscription.

The seeded admin is **`admin@sorcha.local` / `Dev_Pass_2025!`** (`DatabaseInitializer.DefaultAdminPassword`; override via the `Seed:AdminPassword` config key). Docker Compose runs Tenant Service with `ASPNETCORE_ENVIRONMENT=Development`, so this dev-default is what n1/tiny actually seed today. If Tenant Service is ever run with `ASPNETCORE_ENVIRONMENT=Production` or `Staging` and `Seed:AdminPassword` is unset, `AdminPasswordResolver` fails the startup closed instead of seeding the known literal (issue #1409). Log signals: `DatabaseInitializer … Admin UserIdentity created`, `… PlatformSettings seeded`, `… Owner subscription to system register`.

⚠ **Do NOT run the CLI `sorcha bootstrap` against a freshly-seeded node.** The admin already exists, so it 500s with `duplicate key value violates unique constraint "UQ_PlatformUser_Email"` — but only AFTER committing a **stray org** in an earlier SaveChanges. (Earlier revisions of this doc told you to bootstrap with `admin@sorcha.dev` / `Dev_Pass_2026!` and a "Sorcha Dev" org — that account never existed; the startup seed owns the credentials now. The old "auth chicken-and-egg, bootstrap before import" framing is also obsolete — there's no manual bootstrap, and Step 5's import is independent of it.)

Tenant tables live in the **`public`** schema (NOT `tenant`). If a stray org got created by a mistaken `bootstrap`, delete it once you've confirmed nothing references it:

```bash
docker exec sorcha-postgres psql -U sorcha -d sorcha_tenant \
  -c "DELETE FROM public.\"Organizations\" WHERE \"Subdomain\"='<subdomain-you-passed>';"
```

### Step 5: Import Validator Key — must use the SYSTEM wallet endpoint

**CRITICAL: ordering matters.** The validator-service eagerly calls
`CreateOrRetrieveSystemWalletAsync` on first signing operation, and that
endpoint will silently **generate a fresh wallet** if none exists for
`Validator__ValidatorId`. That fresh wallet's pubkey is not in the genesis
roster, so dockets cannot seal. Recovery from this state requires manual
SQL surgery on `wallet."Wallets"` plus a tear-down — much easier to get
the order right the first time.

**Order:**
1. Bring up `wallet-service` only (not the full stack)
2. Wait for healthy
3. POST the mnemonic to `/api/v1/wallets/system/recover` (PR #465)
4. Bring up the rest of the stack

Do NOT use `/api/v1/wallets/recover` — that creates a USER wallet under
the caller's owner/tenant, which `CreateOrRetrieveSystemWalletAsync` does
not see. (This was the trap that wasted a session diagnosing in
April 2026; see issue #461.)

**The endpoint to use:** `POST /api/v1/wallets/system/recover` — added in
PR #465. Recovers a wallet directly into `tenant=system,
owner=validator:{validatorId}, name=system-wallet-{validatorId}` so
validator-service finds it.

> **⚠ NO LONGER `AllowAnonymous` (Feature 147 / review H1, PR #930).** The endpoint now requires
> the `CanRecoverSystemWallet` policy — a **service-tier token** (`token_type=service` + the
> installation's `:service` audience) OR a platform-tier Administrator. An unauthenticated POST
> (as `n1-setup-remote.sh` and the manual curl below still do) returns **HTTP 401** and recovers
> nothing → genesis can't seal. Until the tooling is fixed, mint a short-lived service token
> offline from the node's `JWT_SIGNING_KEY` (UTF8 key bytes; issuer `urn:sorcha:{installation}`,
> audience `{installation}:service`) and send it as `Authorization: Bearer` — see the n1-deploy
> skill's "Import genesis validator key" note for the copy-paste HS256 mint recipe (verified live
> 2026-06-04: 401 anonymous → 201 with the service token). The `HTTP 000` note below still applies
> to genuinely-anonymous *older* images; on current images a 401 is the auth gate, not a lost reply.

**Procedure (automated — preferred):**

```powershell
# From your dev box, one command does it all:
.\scripts\n1-reset.ps1 `
  -ResourceGroup sorcha-n1-uk `
  -UpdateCompose `
  -ImportValidatorKeyPath .\temp\genesis-validator-key.json `
  -Yes
```

The script handles the full sequence: tear down with `-v`, bring up wallet-service alone, wait for healthy, POST the mnemonic to `/api/v1/wallets/system/recover` over the compose network, bring up the rest of the stack, then shred the uploaded key file. Mnemonic is never echoed to logs and never appears in process args (passed on stdin to a one-shot curl container).

`-ValidatorId` defaults to `local-validator` and must match `Validator__ValidatorId` on the validator-service.

**Procedure (manual fallback — only if the script can't run):**

```bash
# Step 5a: tear down with volumes wiped
ssh sorcha@<n1-ip> 'cd /opt/sorcha && docker compose \
  -f docker-compose.yml -f docker-compose.n1.yml -f docker-compose.ports.yml \
  down -v'

# Step 5b: bring up wallet-service ALONE
ssh sorcha@<n1-ip> 'cd /opt/sorcha && docker compose \
  -f docker-compose.yml -f docker-compose.n1.yml -f docker-compose.ports.yml \
  up -d wallet-service'

# Step 5c: wait healthy
until ssh sorcha@<n1-ip> "docker ps --filter name=sorcha-wallet-service --format '{{.Status}}'" | grep -q healthy; do sleep 5; done

# Step 5d: import via curl on the sorcha network. The wallet-service
# container has no shell, so docker exec sh won't work — use a one-shot
# curl container on the same compose network.
MNEMONIC=$(python3 -c "import json; print(json.load(open('temp/genesis-validator-key.json'))['mnemonic'])")
ssh sorcha@<n1-ip> "docker run --rm --network=sorcha_sorcha-network curlimages/curl:latest \
  -sk -X POST http://wallet-service:8080/api/v1/wallets/system/recover \
  -H 'Content-Type: application/json' \
  -d '{\"validatorId\":\"local-validator\",\"mnemonic\":\"$MNEMONIC\",\"algorithm\":\"ED25519\"}'"
# Expected response: {"address":"ws11q…"}

# Step 5e: bring up the rest of the stack
ssh sorcha@<n1-ip> 'cd /opt/sorcha && docker compose \
  -f docker-compose.yml -f docker-compose.n1.yml -f docker-compose.ports.yml \
  up -d'
```

**`HTTP 000` from the import almost always means it SUCCEEDED (verified 2026-06-02).** The one-shot curl can exit non-zero (lost response / blip) even though wallet-service processed the POST and logged `system/recover responded 201`. The automated `n1-setup-remote.sh` treats `000` as fatal (`set -euo pipefail`) and **aborts before bringing up the rest of the stack**. Do NOT `down -v` and retry — wallet-service-alone can't auto-generate a `validator:` wallet, so the recovered wallet is real. Verify with `docker exec sorcha-postgres psql -U sorcha -d sorcha_wallet -tc 'select "Owner" from wallet."Wallets" where "Owner" like '"'"'validator:%'"'"';'` (or grep the wallet-service log for `responded 201`), then just run Step 5e (`up -d`) to finish.

**Note on wallet address:** the address returned by `/system/recover`
will NOT match the `walletAddress` in `temp/genesis-validator-key.json`.
That's expected — the address is derived from a different BIP path than
the docket-signing key. The roster check uses the
`m/44'/0'/0'/0/102` (`sorcha:docket-signing`) pubkey, which IS the same
across the two derivations from the same mnemonic.

**409 Conflict behaviour:** the endpoint refuses to overwrite an
existing system wallet. If you see `409 Conflict`, validator-service
already auto-generated the wrong wallet. Recovery: stop register +
validator services, delete `wallet."Wallets"` rows where
`"Owner"='validator:local-validator'` (plus any `WalletAddresses`
referencing them), then redo step 5. Or just `down -v` and restart
from step 5a.

### Step 6: Restart Register Service

After importing the validator key, restart the register service to retry genesis ingestion:

```bash
az vm run-command invoke --resource-group sorcha-n1-uk --name sorcha-n1-vm \
  --command-id RunShellScript --scripts \
  "cd /opt/sorcha && docker compose -f docker-compose.yml -f docker-compose.n1.yml -f docker-compose.ports.yml restart register-service"
```

### Step 7: Verify

Check the register service logs for successful bootstrap:

```bash
az vm run-command invoke --resource-group sorcha-n1-uk --name sorcha-n1-vm \
  --command-id RunShellScript --scripts \
  "docker logs sorcha-register-service 2>&1 | grep -iE 'genesis|bootstrap|seeded|completed'"
```

Expected output includes:
- `Genesis transaction accepted by Validator Service`
- `Auto-creating system register for genesis docket`
- `Blueprint register-creation-v1 seeded successfully`
- `System register bootstrap completed successfully`

**Also verify the genesis docket (docket 0) actually CONTAINS the genesis control transaction** — an empty docket 0 silently breaks SyncOnly replication (see troubleshooting "Empty genesis docket 0"):

```bash
# docket 0 must have a non-empty TransactionIds carrying the genesis tx
ssh sorcha@51.105.7.135 'docker exec sorcha-mongodb mongosh -u sorcha -p sorcha_dev_password --authenticationDatabase admin --quiet --eval "
db=db.getSiblingDB(\"sorcha_register_aebf26362e079087571ac0932d4db973\");
db.dockets.find({}).sort({TimeStamp:1}).forEach(d=>print(\"State=\"+d.State+\" nTx=\"+(d.TransactionIds?d.TransactionIds.length:0)));"'
# GOOD: first docket nTx>=1 (genesis control tx in docket 0).
# BAD:  first docket nTx=0 (empty docket 0) — genesis landed in docket 1, replication will fail.
```

## Troubleshooting

Consult `references/troubleshooting.md` for common bootstrap failures and fixes.

## Key Files

| File | Purpose |
|------|---------|
| `src/Apps/Sorcha.Cli/Commands/SystemRegisterCommands.cs` | Genesis ceremony CLI |
| `src/Common/Sorcha.Register.Models/Resources/system-register-genesis.json` | Embedded genesis |
| `src/Services/Sorcha.Register.Service/Services/SystemRegisterBootstrapper.cs` | 4-step bootstrap |
| `src/Services/Sorcha.Register.Service/Services/GenesisIngestionService.cs` | Load, verify, submit |
| `scripts/n1-deploy.ps1` | Full n1 VM deployment |
| `scripts/n1-reset.ps1` | Reset n1 (requires SSH) |
| `docker-compose.n1.yml` | n1-specific overrides |

## Infrastructure Reference

| Resource | Value |
|----------|-------|
| Azure RG | `sorcha-n1-uk` (NOT `sorcha-n1`) |
| VM Name | `sorcha-n1-vm` |
| Public IP | `51.105.7.135` |
| Compose path on VM | `/opt/sorcha/` |
| DockerHub images | `sorchadev/*:latest` |
| API Gateway (n1) | Port 8880 (behind Caddy TLS) |
| Public URL | `https://n1.sorcha.dev` |
