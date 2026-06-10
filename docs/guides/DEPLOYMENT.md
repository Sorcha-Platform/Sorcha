# Sorcha Deployment

This page routes you to the right guide for your deployment path.

## Canonical operator path (Docker Compose)

For a self-hosted deployment using Docker Compose, follow the Admin Guide in order:

1. [Prerequisites & Sizing](../admin/prerequisites-sizing.md) — hardware, software, and network requirements
2. [Installation & First Run](../admin/installation-first-run.md) — clone, configure `.env`, start services, bootstrap
3. [Configuration Reference](../admin/configuration-reference.md) — all environment variables
4. [Scaling & High Availability](../admin/scaling-high-availability.md) — horizontal scaling, Redis backplane, multi-replica patterns

Before starting services, work through the **[Production Pre-Flight Checklist](../admin/installation-first-run.md#production-pre-flight-checklist)** — it covers six gaps between the shipped development defaults and a production-ready deployment (JWT key, database passwords, per-service environment overrides, Redis authentication, peer TLS, and the admin-verified user creation flag).

For a description of every compose file and the `deploy/` tooling tree, see the
[Compose Files & Deployment Tooling](../admin/README.md#compose-files--deployment-tooling) section
in the Admin Guide.

## Azure deployment

| Guide | Purpose |
|-------|---------|
| [Azure Deployment Quick Start](azure/AZURE-DEPLOYMENT-QUICK-START.md) | End-to-end Azure Container Apps deployment |
| [Azure Database Initialization](azure/AZURE-DATABASE-INITIALIZATION.md) | PostgreSQL and MongoDB on Azure managed services |
| [Azure Custom Domain Setup](azure/AZURE-CUSTOM-DOMAIN-SETUP.md) | Custom domain and TLS via Azure Front Door / App Service |

## Backup and recovery

See the [Administration guide](../admin/administration.md) — it covers PostgreSQL, MongoDB, and Redis backup procedures and the wallet encryption key backup/restore scripts.

## Development quick start

For local development (not production):

- [Docker Quick Start](../getting-started/DOCKER-QUICK-START.md) — fastest path to a running local stack
- [Infrastructure Setup](../getting-started/INFRASTRUCTURE-SETUP.md) — databases only, services run from IDE or Aspire

## Reset a Docker deployment

```powershell
# Windows
.\scripts\reset-docker-state.ps1

# Linux/macOS
./scripts/reset-docker-state.sh
```

See the [Troubleshooting guide](../admin/troubleshooting.md) for common issues.
