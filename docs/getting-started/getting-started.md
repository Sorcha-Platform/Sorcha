# Getting Started with Sorcha

This guide takes you from nothing to a running Sorcha instance and your first workflow. Sorcha runs as a set of containers orchestrated by Docker Compose; the fastest way in is the one-line installer.

## Prerequisites

- **Docker** — Docker Desktop (macOS/Windows) or Docker Engine + Compose v2 (Linux). This is the only hard requirement to *run* Sorcha.
- **git** — used by the installer to fetch the repository.
- Ports **80**, **443**, and **8080** free on the host.
- (Only if you want to *build/develop* the code, not just run it: the **.NET 10 SDK**.)

## Install

### Option A — one line (recommended)

Downloads Sorcha, asks a few setup questions, generates your `.env` (including a JWT signing key), pulls the images, starts every service, and bootstraps the platform:

**macOS / Linux / WSL / Git Bash**
```bash
curl -fsSL https://raw.githubusercontent.com/Sorcha-Platform/Sorcha/master/scripts/install.sh | bash
```

**Windows PowerShell** (needs Git for Windows or WSL for the bash step)
```powershell
irm https://raw.githubusercontent.com/Sorcha-Platform/Sorcha/master/scripts/install.ps1 | iex
```

Add `--quiet` to accept all defaults without prompts (handy for CI). To review the script before running it, see the “Prefer to read it first?” note in the [project README](../../README.md#try-it-in-one-line).

### Option B — manual

```bash
git clone https://github.com/Sorcha-Platform/Sorcha.git
cd Sorcha

# Interactive setup — checks prerequisites, generates .env, pulls images, starts services
./scripts/sorcha-setup.sh

# …or bare-bones, using the shipped defaults:
cp .env.example .env
docker compose up -d
```

Either way, on success the gateway comes up. Verify:

```bash
curl -s http://localhost/api/health   # every service should report Healthy
```

## First login

| What | Where |
|------|-------|
| **Main UI** | http://localhost/app |
| **Blueprint Designer** | http://localhost/app/designer/blueprint |
| **API Gateway** | http://localhost/ |
| **API docs (Scalar)** | http://localhost/scalar/ |
| **Health** | http://localhost/api/health |
| **Aspire dashboard** (traces/logs/metrics) | http://localhost:18888 |

Default administrator (created on first run — **change it for anything but local dev**):

| Field | Value |
|-------|-------|
| Email | `admin@sorcha.local` |
| Password | `Dev_Pass_2025!` |

See [`guides/AUTHENTICATION-SETUP.md`](../guides/AUTHENTICATION-SETUP.md) for production auth configuration.

## Your first blueprint

A **blueprint** is a multi-party workflow: participants, actions, per-action schemas, and routing between them.

1. Open the **Designer** at http://localhost/app/designer/blueprint. It's a rail-driven **Describe → Understand → Rehearse → Go live** flow — describe the workflow in plain language, refine the actions/schemas, rehearse it, then publish. Go-live is gated by a server-side rehearsal pass.
2. Prefer to hand-author? Blueprints are JSON/YAML — see [`guides/blueprints/blueprint-format.md`](../guides/blueprints/blueprint-format.md) and the ready-made templates in [`blueprints/`](../../blueprints/). The `blueprint-builder` skill generates them too.
3. Publish the blueprint to a **register** (a distributed ledger), then participants complete actions in sequence — each signed by their wallet, validated, routed, and recorded immutably.

For a full worked end-to-end run, pick a scenario in [`walkthroughs/`](../../walkthroughs/README.md).

## Developing the code (optional)

To debug with breakpoints, run the platform through **.NET Aspire** instead of Compose:

```bash
dotnet run --project src/Apps/Sorcha.AppHost
```

This starts every service (on their HTTPS Aspire ports, e.g. Blueprint `7000`, Wallet `7001`, Register `7290`, Tenant `7110`) plus the Aspire dashboard at http://localhost:18888. The full port map is in [`PORT-CONFIGURATION.md`](PORT-CONFIGURATION.md).

```bash
dotnet restore && dotnet build      # build the solution
dotnet test                         # run the test suite
dotnet format                       # format code
```

## Common operations

```bash
docker compose ps                   # service status
docker compose logs -f <service>    # follow a service's logs (e.g. blueprint-service)
docker compose down                 # stop (keep data in volumes)
docker compose down -v              # stop and delete all data
```

## Troubleshooting

- **Port 80/443/8080 already in use** — free the port, or remap the gateway in `docker-compose.yml` (`ports:`) and re-run.
- **HTTPS listener won't bind** — only affects port 443; HTTP on 80 still works. Generate a dev cert to enable HTTPS (see [`DOCKER-QUICK-START.md`](DOCKER-QUICK-START.md)).
- **A service is unhealthy** — `docker compose logs <service>`; PostgreSQL/MongoDB may need a few seconds to initialise on first start.
- More: [`../guides/TROUBLESHOOTING.md`](../guides/TROUBLESHOOTING.md).

## Next steps

- [`docs/architecture.md`](../architecture.md) — how the platform fits together, by service and data flow.
- [`docs/reference/API-DOCUMENTATION.md`](../reference/API-DOCUMENTATION.md) — full REST/gRPC reference.
- [`walkthroughs/README.md`](../../walkthroughs/README.md) — interactive end-to-end demos.
- The **CLI**: `dotnet run --project src/Apps/Sorcha.Cli -- --help` (see [`src/Apps/Sorcha.Cli/README.md`](../../src/Apps/Sorcha.Cli/README.md)).

## Getting help

- Search the [docs](../README.md) and existing [GitHub issues](https://github.com/Sorcha-Platform/Sorcha/issues).
- Open a new issue with clear reproduction steps and environment details.
