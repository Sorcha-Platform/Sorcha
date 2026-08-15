# Port Configuration Guide

**Status:** Active · sourced from `docker-compose.yml` and the AppHost

This is the canonical port reference. The **Docker external** ports are the host ports published by
`docker-compose.yml` (the runtime source of truth). The **Aspire HTTPS** ports are the HTTPS
endpoints the AppHost / `launchSettings.json` expose when you run with `.NET Aspire`; confirm the
live values in the Aspire Dashboard.

## Service ports

| Service | Docker external (host) | Container internal | Aspire HTTPS |
|---------|------------------------|--------------------|--------------|
| **API Gateway** | `80` (HTTP), `443` (HTTPS) | 8080 / 8443 | `7082` |
| **Blueprint Service** | `5000` | 8080 | `7000` |
| **Register Service** | `5380` | 8080 | `7290` |
| **Tenant Service** | `5450` | 8080 | `7110` |
| **Validator Service** | `5800` (HTTP), `5801` (gRPC) | 8080 / 8081 | `7004` |
| **Peer Service** | `50051` (gRPC) | 5000 | `7002` |
| **Wallet Service** | _internal only_ (no published port) | 8080 | `7001` |
| **HAIP Service** | _internal only_ (no published port) | 8080 | — |
| **UI (Web)** | `5400` (HTTP), `5401` (HTTPS) | 8080 / 8443 | — |
| **Wallet PWA** | `7400` | 8080 | — |
| **Verifier** | `7401` | 8080 | — |

> Wallet and HAIP have no published host port by design — they are reached **through the API
> Gateway** (or service-to-service on the internal Docker network). Override any host port with the
> matching environment variable (e.g. `REGISTER_PORT`, `TENANT_PORT`, `GATEWAY_HTTP_PORT`).

| Service | Docker external (host) | Container internal | Purpose |
|---------|------------------------|---------------------|---------|
| **Tenant Service — workload mTLS listener** | _internal only_ (no published port) | `8443` | Feature 191 (#1420) service-to-service certificate token mint. Additive to the normal Tenant listener; activates only when `ServiceAuth:Mtls:ServerCertificate` + `ServiceAuth:Mtls:TrustBundle` are configured. Never published to the host — reached only on the internal Docker network by other services' `ServiceAuth:MtlsTokenAddress` (default `https://tenant-service:8443`). Override with `ServiceAuth:Mtls:Port`. |

## Infrastructure ports

| Service | Docker external (host) | Purpose |
|---------|------------------------|---------|
| **PostgreSQL** | `5432` | Relational store |
| **MongoDB** | `27017` | Document store (ledger) |
| **Redis** | `16379` (→ 6379 in-container) | Cache, rate-limit, SignalR backplane |
| **Aspire Dashboard** | `18888` | Observability dashboard |
| **OTLP gRPC / HTTP** | `4317` / `4318` | Telemetry ingestion |

## Samples / demo consumers

Samples in `samples/` are application-specific demos that consume the platform's public APIs only;
they are **not** platform services and are not in the root `docker-compose.yml`. Each ships its own
compose overlay.

| Sample | Port | Invocation |
|--------|------|------------|
| **Strathcarron Portal** | `STRATHCARRON_PORTAL_PORT` (demo council portal) | `docker compose -f docker-compose.yml -f samples/strathcarron-portal/docker-compose.yml up -d` |

---

## Environments

### Docker Compose (default)

Everything runs behind the **API Gateway at `http://localhost`** (port 80). The Gateway routes by
path — e.g. `/api/auth/*` and `/api/service-auth/*` → Tenant, `/api/blueprints/*` → Blueprint,
`/api/registers/*` → Register, `/app` → UI. You normally only need the Gateway URL:

```bash
docker-compose up -d

# Auth through the Gateway
curl -X POST http://localhost/api/auth/login -H "Content-Type: application/json" \
  -d '{"email":"admin@sorcha.local","password":"Dev_Pass_2025!"}'
```

Direct (bypass the Gateway) for debugging:

```
http://localhost:5450   Tenant      http://localhost:5800   Validator (HTTP)
http://localhost:5000   Blueprint   localhost:5801          Validator (gRPC)
http://localhost:5380   Register    localhost:50051         Peer (gRPC)
http://localhost:5400   UI (Web)    http://localhost:18888  Aspire Dashboard
```

### .NET Aspire (AppHost)

HTTPS with self-signed dev certs; per-service endpoints exposed for breakpoint debugging.

```bash
dotnet run --project src/Apps/Sorcha.AppHost
```

```
https://localhost:7110  Tenant       https://localhost:7290  Register
https://localhost:7000  Blueprint    https://localhost:7004  Validator
https://localhost:7001  Wallet       https://localhost:7002  Peer
https://localhost:7082  API Gateway  http://localhost:18888  Aspire Dashboard
```

### Production

Services sit behind the API Gateway / a reverse proxy on `443` (TLS). Expose only the Gateway
publicly; keep service and infrastructure ports on the internal network.

---

## Client configuration

The CLI and admin UI select a connection **profile** (`local` / `docker` / `production`) carrying
the URLs above.

```bash
sorcha config get activeProfile
sorcha config set activeProfile docker
sorcha --profile docker organization list
```

---

## Troubleshooting

**Port already in use**

```bash
# Windows
netstat -ano | findstr :<PORT>
taskkill /PID <PID> /F
# Linux/macOS
lsof -i :<PORT> ; kill -9 <PID>
```

Or override the host port via the service's environment variable (e.g. `REGISTER_PORT=5381`).

**SSL trust (Aspire)**

```bash
dotnet dev-certs https --trust
```

**Cannot connect** — check, in order: container is running (`docker ps`), correct port for your
environment (Docker vs Aspire), correct protocol (HTTP vs HTTPS), and the service is healthy
(`/health`).

---

## See also

- [Development Status](../reference/development-status.md)
- [Architecture Overview](../architecture.md)
- [API Documentation](../reference/API-DOCUMENTATION.md)
