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
- `genesis-validator-key.json` (root of repo, DO NOT commit)

**CRITICAL:** Store `genesis-validator-key.json` securely. The mnemonic controls all keys derived from this wallet.

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

### Step 5: Import Validator Key

The CLI `import-validator-key` command has console issues in non-interactive contexts. Use the API directly instead:

```bash
# Get service principal token
TOKEN=$(curl -s --ssl-no-revoke -X POST "https://n1.sorcha.dev/api/service-auth/token" \
  -H "Content-Type: application/x-www-form-urlencoded" \
  -d "grant_type=client_credentials&client_id=<SP_CLIENT_ID>&client_secret=<SP_SECRET>" \
  | python -c "import sys,json; print(json.load(sys.stdin)['access_token'])")

# Import validator wallet from genesis mnemonic
curl -s --ssl-no-revoke -X POST "https://n1.sorcha.dev/api/wallet/wallets/recover" \
  -H "Content-Type: application/json" \
  -H "Authorization: Bearer $TOKEN" \
  -d '{"mnemonicWords":[<WORDS_FROM_genesis-validator-key.json>],"name":"genesis-validator-sorcha-dev","algorithm":"ED25519"}'
```

The mnemonic words come from `genesis-validator-key.json`. The root wallet address will differ from the genesis validator address — this is expected. The Wallet Service derives the docket-signing key internally.

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
