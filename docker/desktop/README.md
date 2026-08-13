# Sorcha Desktop Node

A self-contained, single-machine Sorcha deployment sized for **~10–20 users**.
Pulls the published `sorchadev/*` images — **no source tree or .NET SDK required**
on the target machine, only a container runtime.

## What's in this folder

| File | Purpose |
|------|---------|
| `docker-compose.desktop.yml` | Standalone, image-only stack (core + citizen wallet). |
| `.env.desktop.example` | Template config. Copy to `.env` (or let the script generate it). |
| `postgres-init.sql` | Creates the per-service Postgres databases on first boot. |

The deploy scripts live one level up: `scripts/deploy-desktop.ps1` / `scripts/deploy-desktop.sh`.

## Prerequisites

A container runtime with Compose support. **No Docker Desktop licence required** —
use either:

- **[Rancher Desktop](https://rancherdesktop.io)** (recommended) — free, ships the
  real `docker` CLI + Compose.
- **[Podman](https://podman.io) / [Podman Desktop](https://podman-desktop.io)** —
  use `podman compose` with the docker-compose provider (plain `podman-compose`
  may not honour the `depends_on` health gates).

**Resources:** 6 GB RAM / 4 vCPU minimum, 12–16 GB / 6–8 vCPU comfortable.
20 GB free disk (images ~3 GB + data volumes that grow with use).
Under WSL2/Podman-machine, set the **VM's** RAM allocation accordingly — that cap,
not host RAM, is what the containers get.

## Quick start

```powershell
# Windows
pwsh ./scripts/deploy-desktop.ps1
```
```bash
# Linux / macOS
./scripts/deploy-desktop.sh
```

The script will: detect the runtime → check resources → generate `.env` with fresh
secrets (first run) → pull images → start the stack → wait for health → bootstrap →
print the access URLs.

Then browse to **http://localhost:8880/app**.

## Scope

Included: gateway, blueprint, wallet, register, tenant, peer, validator, haip, web UI,
citizen-wallet PWA, reference verifier, plus Postgres / MongoDB / Redis.

Omitted (vs the developer stack) to save ~1.5 GB RAM: the Aspire observability
dashboard and the MCP agent server. Telemetry export is off.

## Common operations

```bash
./scripts/deploy-desktop.sh --tag 2.412.1     # pin a specific image version
./scripts/deploy-desktop.sh --skip-bootstrap  # bring up only, bootstrap later
./scripts/deploy-desktop.sh --down            # stop (keeps data)
./scripts/deploy-desktop.sh --down --purge    # stop and DELETE all data volumes
```

To update to newer images: re-run the script (it `pull`s then recreates changed
containers). Data in the named volumes is preserved.

## Security notes (before exposing beyond localhost)

- The gateway is **HTTP-only on :8880**. Front it with a TLS-terminating reverse
  proxy (Caddy / nginx) if reachable off the machine.
- `.env` holds the JWT signing key, DB passwords, and the per-service
  `*_SERVICE_SECRET` ServiceAuth credentials — it's generated `chmod 600`
  and must never be committed.
- `INSTALLATION_NAME` is fixed at first boot (it's baked into issued tokens).
  Changing it later invalidates existing tokens.
- `Platform__AllowAdminVerifiedUserCreation` is enabled to let bootstrap create a
  pre-verified admin; turn it off for a hardened deployment.

## Federation (optional)

This node runs standalone by default. To join an existing Sorcha network, set
`SEED_PEER_*` in `.env` and re-deploy — the peer service dials the seed on boot.

## Known limitation — admin bootstrap

The system register auto-bootstraps. Provisioning the **first admin org/user** is
still best-effort: the script delegates to the repo `bootstrap-sorcha` flow when
present, otherwise it points you at the UI registration page. The bootstrap CLI is
beta (see `scripts/README-BOOTSTRAP.md`); wire your confirmed tenant registration
endpoint into the script's `bootstrap_node()` / `Invoke-Bootstrap` to fully automate.
