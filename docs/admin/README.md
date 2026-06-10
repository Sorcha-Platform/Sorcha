# System Administrator Guide

This guide covers deploying, configuring, scaling, and managing a Sorcha Decentrailised Register instance.

Sorcha is a decentralised register platform for secure, multi-participant data flow orchestration. It runs as 7 microservices plus supporting infrastructure (PostgreSQL, MongoDB, Redis), orchestrated via Docker Compose with .NET Aspire for observability.

## Contents

| Guide | Purpose |
|-------|---------|
| [Prerequisites & Sizing](prerequisites-sizing.md) | Hardware, software, and network requirements |
| [Installation & First Run](installation-first-run.md) | Docker deployment and bootstrap |
| [Configuration Reference](configuration-reference.md) | Complete environment variable reference |
| [Scaling & High Availability](scaling-high-availability.md) | Horizontal scaling and replication |
| [Monitoring & Observability](monitoring-observability.md) | Dashboard, health checks, logging |
| [Administration](administration.md) | User management, backup, security |
| [Troubleshooting](troubleshooting.md) | Common issues and diagnostics |
| [Upgrade & Migration](upgrade-migration.md) | Version upgrades and database migrations |

## Quick Reference

- **Default admin:** `admin@sorcha.local` / `Dev_Pass_2025!` (change immediately)
- **API Gateway:** `http://localhost:80`
- **API Documentation:** `http://localhost/openapi`
- **Admin Dashboard:** `http://localhost/admin/dashboard` (requires SystemAdmin role)
- **Health checks:** `http://localhost/{service}/health`

## Architecture Overview

```
                         ┌──────────────────┐
                         │   API Gateway    │
                         │   (YARP, :80)    │
                         └────────┬─────────┘
              ┌──────────┬────────┼────────┬──────────┬───────────┐
              v          v        v        v          v           v
        ┌──────────┐┌─────────┐┌────────┐┌────────┐┌──────────┐┌──────┐
        │Blueprint ││ Wallet  ││Register││ Tenant ││Validator ││ Peer │
        │ Service  ││ Service ││Service ││Service ││ Service  ││Svc   │
        └────┬─────┘└────┬────┘└───┬────┘└───┬────┘└────┬─────┘└──┬───┘
             │           │         │         │          │         │
        ┌────v─────┐┌────v────┐┌───v────┐┌───v────┐    │         │
        │ MongoDB  ││Postgres ││MongoDB ││Postgres│  Redis    Redis
        │          ││         ││        ││        │
        └──────────┘└─────────┘└────────┘└────────┘
```

## Service Summary

| Service | Default Port | Purpose |
|---------|-------------|---------|
| API Gateway | 80 | YARP reverse proxy, TLS termination |
| Blueprint Service | 5000 | Workflow management, SignalR |
| Wallet Service | internal | Crypto operations, HD wallets |
| Register Service | 5380 | Distributed ledger, OData |
| Tenant Service | 5450 | Multi-tenant auth, JWT issuer |
| Validator Service | 5800 / 5801 | Consensus, chain integrity |
| Peer Service | 50051 (gRPC) | P2P network communication |
| HAIP Service | internal | OpenID4VCI / HAIP credential issuance surface |
| Aspire Dashboard | 18888 | Observability (traces, logs, metrics) |

## Compose Files & Deployment Tooling

### Repo-root compose files

| File | Purpose | Use in production? |
|------|---------|-------------------|
| `docker-compose.yml` | Primary stack — all services, infrastructure, MCP server (profile `tools`). Development defaults throughout. | Base file; requires a production override. |
| `docker-compose.debug.yml` | Overlay that raises log levels to `Debug` for auth, JWT, and per-service namespaces. | Dev/troubleshooting only. |
| `docker-compose.federation.yml` | Two-peer federation overlay (Feature 107) — second full stack (`peer-b`) on the same Docker host for cross-peer smoke tests. | Dev/testing only. |
| `docker-compose.infrastructure.yml` | Infrastructure only (Redis, PostgreSQL, MongoDB) — no application services. Useful for running services from IDE/Aspire against real databases. | Dev only. |
| `docker-compose.localdev.yml` | Extends validator `GenesisMaxAge` to 30 days so a clean local start accepts an older genesis block without a rebuild. | Dev only. |
| `docker-compose.multinode.yml` | Multi-replica overlay for Feature 118 cross-replica SignalR hub correctness tests. Brings up two Blueprint Service replicas behind YARP. | Testing only. |
| `docker-compose.n1.yml` | Override for `n1.sorcha.dev` — sets `INSTALLATION_NAME`, pulls pre-built DockerHub images, configures peer identity. TLS handled by host Caddy. | Deployable (used in production for n1). |
| `docker-compose.ports.yml` | Port override companion to `n1.yml` — moves gateway off 80/443 to 8880:8080 so Caddy can handle TLS on the standard ports. | Used with `n1.yml` in production. |
| `docker-compose.seed.yml` | Switches `register-service` to `GenesisFile` bootstrap mode for the very first node of a new network. Drop after first successful start. | Deployable (seed node only, one-shot). |
| `docker-compose.sync-from-n1.yml` | Configures `peer-service` to bootstrap from `n1.sorcha.dev:50051` and sets `register-service` to `SyncOnly` bootstrap so it waits for peer replication rather than self-seeding. | Deployable (secondary/replica nodes). |

### deploy/ tree

| Path | Purpose | Use in production? |
|------|---------|-------------------|
| `deploy/n1/docker-compose.n1.yml` | Two-installation demo variant (Feature 143) — n1 as public subscriber with its own genesis. | Demo / two-install scenario. |
| `deploy/tiny/docker-compose.tiny.yml` | Two-installation demo variant — `tiny.sorcha.dev` as NAT'd register owner/issuer with separate installation. | Demo / two-install scenario. |
| `deploy/tiny-fresh/` | Fresh genesis artefacts (genesis JSON + validator key) for a new `tiny` installation. | Setup artefacts only. |
| `deploy/twoinstall-issuer.ps1` | PowerShell orchestration script — provisions the issuer side (Acme Verification Co., analyst, register, blueprint) against the `tiny` node. | Demo orchestration. |
| `deploy/twoinstall-citizen-n1.ps1` | PowerShell orchestration script — runs the citizen/subscriber side against the `n1` node in the two-installation demo. | Demo orchestration. |

## Related Documentation

- [Port Configuration](../getting-started/PORT-CONFIGURATION.md) -- Complete port assignments
- [Bootstrap Credentials](../getting-started/BOOTSTRAP-CREDENTIALS.md) -- Default credentials
- [Authentication Setup](../guides/AUTHENTICATION-SETUP.md) -- JWT configuration
- [Architecture Reference](../reference/architecture.md) -- System architecture diagrams
