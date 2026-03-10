# Prerequisites & Sizing

This guide covers the hardware, software, and network requirements for deploying Sorcha.

## Hardware Sizing

| Deployment | CPU | RAM | Disk | Concurrent Users |
|------------|-----|-----|------|------------------|
| Development | 2 cores | 4 GB | 20 GB | 1-10 |
| Small (Pilot) | 4 cores | 8 GB | 50 GB | 10-100 |
| Medium (Team) | 8 cores | 16 GB | 100 GB | 100-500 |
| Large (Production) | 16+ cores | 32+ GB | 500+ GB | 500+ |

### Per-Service Resource Estimates

| Service | CPU (min) | RAM (min) | Notes |
|---------|-----------|-----------|-------|
| API Gateway | 0.25 vCPU | 256 MB | Stateless reverse proxy |
| Blueprint Service | 0.5 vCPU | 512 MB | Workflow engine, SignalR connections |
| Wallet Service | 0.5 vCPU | 512 MB | Crypto operations are CPU-intensive |
| Register Service | 0.5 vCPU | 512 MB | Ledger read/write, OData queries |
| Tenant Service | 0.25 vCPU | 256 MB | Auth/JWT issuance |
| Validator Service | 0.5 vCPU | 512 MB | Chain validation, consensus |
| Peer Service | 0.25 vCPU | 256 MB | gRPC P2P networking |
| PostgreSQL | 0.5 vCPU | 1 GB | Wallet + Tenant databases |
| MongoDB | 0.5 vCPU | 1 GB | Register + Blueprint storage |
| Redis | 0.25 vCPU | 256 MB | Cache, sessions, SignalR backplane |
| Aspire Dashboard | 0.25 vCPU | 256 MB | Telemetry collection and UI |

**Total minimum (all services):** ~4 vCPU, ~5.5 GB RAM

For production deployments, allocate 2-3x the minimum values and monitor actual usage to right-size.

### Disk Space Considerations

| Component | Growth Rate | Notes |
|-----------|-------------|-------|
| PostgreSQL | Low-moderate | User accounts, wallet metadata |
| MongoDB | Moderate-high | Ledger transactions, blueprints (grows with usage) |
| Redis | Low | Volatile cache, session data |
| Docker images | ~2 GB total | All Sorcha service images |
| Logs | Variable | Depends on log level and retention |

## Software Prerequisites

### Required

| Software | Version | Purpose |
|----------|---------|---------|
| Docker Desktop | 4.x+ | Container runtime |
| Docker Compose | v2+ (bundled with Docker Desktop) | Service orchestration |
| git | 2.x+ | Clone repository |

### Optional (for Aspire/development mode)

| Software | Version | Purpose |
|----------|---------|---------|
| .NET 10 SDK | 10.0+ | Build from source, run Aspire AppHost |
| PowerShell 7+ | 7.x+ | Setup and utility scripts |

### Operating System Support

| OS | Docker Mode | Notes |
|----|-------------|-------|
| Windows 11 | Docker Desktop (WSL2) | Recommended for development |
| Windows 10 | Docker Desktop (WSL2 or Hyper-V) | WSL2 preferred |
| macOS 12+ | Docker Desktop | Apple Silicon (ARM64) supported |
| Ubuntu 22.04+ | Docker Engine | Recommended for production |
| RHEL/CentOS 9+ | Docker Engine or Podman | SELinux may require configuration |

## Network Requirements

### Ports

The following ports must be available on the host machine:

| Port | Service | Direction | Required |
|------|---------|-----------|----------|
| 80 | API Gateway (HTTP) | Inbound | Yes |
| 443 | API Gateway (HTTPS) | Inbound | Production only |
| 5432 | PostgreSQL | Internal/Debug | Configurable via `POSTGRES_PORT` |
| 27017 | MongoDB | Internal/Debug | Configurable via `MONGODB_PORT` |
| 6379 (mapped 16379) | Redis | Internal/Debug | Configurable via `REDIS_PORT` |
| 18888 | Aspire Dashboard | Internal/Admin | Configurable via `ASPIRE_UI_PORT` |
| 4317 | OTLP gRPC | Internal | Configurable via `OTLP_GRPC_PORT` |
| 4318 | OTLP HTTP | Internal | Configurable via `OTLP_HTTP_PORT` |
| 50051 | Peer gRPC | Inbound (P2P) | Only if P2P enabled |

### Firewall Rules

**Minimum for operation (internal use):**
- Allow inbound TCP port 80 (HTTP) from client networks
- Allow outbound HTTPS (443) for Docker image pulls

**For production with TLS:**
- Allow inbound TCP port 443 (HTTPS) from client networks
- Block direct access to database ports (5432, 27017, 16379) from external networks

**For peer-to-peer networking:**
- Allow inbound TCP port 50051 (or configured `PEER_GRPC_PORT`) for gRPC peer connections
- Allow outbound to seed node endpoints (e.g., `n0.sorcha.dev:443`)

### DNS Requirements

**Development:** No DNS required. Services are accessed via `localhost`.

**Production:**
- A domain name pointing to the API Gateway host (e.g., `sorcha.example.com`)
- Optional: Separate subdomains for individual services if exposed directly
- DNS resolution between Docker containers is handled automatically by Docker's internal DNS

### TLS/SSL Requirements

**Development:** Self-signed certificates are generated automatically. No action required.

**Production:**
- A valid TLS certificate for the API Gateway domain
- Certificate must be in PFX format for Kestrel, or terminate TLS at a load balancer
- Minimum TLS 1.2 recommended
- Certificate files are mounted via Docker volumes at `/https/`

## Pre-Installation Checklist

Before starting installation, verify:

- [ ] Docker Desktop is installed and running
- [ ] Docker Compose v2 is available (`docker compose version`)
- [ ] Required ports are available (`netstat -tulpn` or `netstat -ano`)
- [ ] At least 4 GB RAM available for containers
- [ ] At least 20 GB free disk space
- [ ] Git is installed (`git --version`)
- [ ] Outbound HTTPS access is available (for pulling Docker images)
- [ ] (Production) TLS certificate is available
- [ ] (Production) DNS is configured
- [ ] (Production) Firewall rules are in place
