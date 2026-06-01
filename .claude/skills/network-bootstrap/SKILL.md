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
- `genesis-validator-key.json` (root of repo, DO NOT commit — gitignored)

**CRITICAL:** Store `genesis-validator-key.json` securely. The mnemonic controls all keys derived from this wallet.

> ⚠️ **Genesis freshness window — the codebase is contradictory; observed behaviour is LENIENT (2026-06-01, F145 session).** Two earlier notes disagreed; here is the verified state:
> - **Empirically, a stale system-register genesis bootstraps fine.** During the F145 cross-node deploy, genesis files **32h and ~3 days** old (by `signedAt`) ingested-and-sealed cleanly on the current validator (local Auto, n1 + tiny GenesisFile) — no `VAL_TIME_002`. `GenesisIngestionService` sets the submitted tx `CreatedAt = tx.Signature.SignedAt` (NOT re-stamped), so the validator does see the real old timestamp and still accepted it.
> - **The code intends genesis to be exempt.** `TransactionTypeClassifier.IsGenesisTransaction` carries an explicit comment: genesis "is ingested whenever a node bootstraps, **which may be arbitrarily long after the ceremony. It must therefore be exempt from the transaction-freshness window (VAL_TIME_002)**."
> - **BUT `ValidationEngine.cs:1596` still computes `maxAge = isGenesis ? GenesisMaxAge : MaxTransactionAge` (both default 1h)** — so a strict reading says it *should* reject. It did not in practice. This is an unresolved discrepancy (the age check is apparently bypassed/ineffective for the system-register bootstrap path). **Do not rely on the 1h window as a guarantee in either direction** until traced.
> - **Practical guidance:** you generally do NOT need to re-mint genesis just because it aged out — reusing an existing genesis file works (proven). The window, IF it bit, would guard only the **ingest-and-seal** path; a SyncOnly replica PULLING an already-sealed genesis docket verifies the sealed docket, not tx age, so it is never window-bound. If you DO regenerate the embedded genesis, it still needs a Docker Publish (~12–15 min) before deploy.

> **DevMode lives in the genesis `CryptoPolicy.DevMode` (Feature 137), not a top-level field.** Default `false` (encrypted/production) — `CryptoPolicy.CreateDefault()` serialises `"devMode": false`. When `true`, the register skips field-level payload encryption (still disclosure-filtered + routed + calculated) and stores plaintext; receivers read it directly. It is baked into the signed genesis so it **replicates to every node**. The transition is **one-way**: a register may be promoted DevMode→Normal via a crypto-policy-update control tx, never Normal→DevMode (the validator rejects re-enabling DevMode). The system register should always be DevMode off; per-workflow registers (e.g. AssuredIdentity) choose at creation. NEVER enable DevMode in a real production network.

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

### Step 3: Reset n1 (via Azure CLI)

SSH key auth to n1 is unreliable. Use `az vm run-command` for all remote operations.

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

### Step 4: Bootstrap Platform

Create the first organization, admin user, and service principal. This step runs BEFORE importing the validator key (auth chicken-and-egg).

```bash
# Create CLI profile for n1
dotnet run --project src/Apps/Sorcha.Cli -- config init \
  --profile n1 --service-url https://n1.sorcha.dev --verify-ssl true --set-active true

# Bootstrap (non-interactive)
dotnet run --project src/Apps/Sorcha.Cli -- bootstrap --non-interactive \
  --org-name "Sorcha Dev" --subdomain "sorcha-dev" \
  --admin-email "admin@sorcha.dev" --admin-name "Admin" \
  --admin-password "Dev_Pass_2026!" \
  --create-sp --sp-name "n1-automation"
```

Save the service principal client ID and secret from the output.

**Note:** The subdomain `dev` is reserved. Use `sorcha-dev` or similar.

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
validator-service finds it. Currently `AllowAnonymous`, so no auth
header needed.

**Procedure (automated — preferred):**

```powershell
# From your dev box, one command does it all:
.\scripts\n1-reset.ps1 `
  -ResourceGroup sorcha-n1-uk `
  -UpdateCompose `
  -ImportValidatorKeyPath .\genesis-validator-key.json `
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
MNEMONIC=$(python3 -c "import json; print(json.load(open('genesis-validator-key.json'))['mnemonic'])")
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

**Note on wallet address:** the address returned by `/system/recover`
will NOT match the `walletAddress` in `genesis-validator-key.json`.
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
