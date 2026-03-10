# System Administrator Guide

This guide covers deploying, configuring, scaling, and managing a Sorcha distributed ledger instance.

Sorcha is a distributed ledger platform for secure, multi-participant data flow orchestration. It runs as 7 microservices plus supporting infrastructure (PostgreSQL, MongoDB, Redis), orchestrated via Docker Compose with .NET Aspire for observability.

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
| Aspire Dashboard | 18888 | Observability (traces, logs, metrics) |

## Related Documentation

- [Port Configuration](../getting-started/PORT-CONFIGURATION.md) -- Complete port assignments
- [Bootstrap Credentials](../getting-started/BOOTSTRAP-CREDENTIALS.md) -- Default credentials
- [Authentication Setup](../guides/AUTHENTICATION-SETUP.md) -- JWT configuration
- [Architecture Reference](../reference/architecture.md) -- System architecture diagrams
