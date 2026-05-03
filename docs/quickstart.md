---
title: Sorcha Quickstart
description: Run a Sorcha instance locally, verify the install, and call the credential-issuance endpoint. Agent-runnable end to end.
standards: [OAuth 2.0]
last_updated: 2026-05-04
---

# Sorcha Quickstart

This is the agent-runnable setup path. A fresh Linux VM with Docker Engine ≥ 24 and PowerShell 7.5+ should reach a healthy gateway in under 15 minutes following only this document. Every prerequisite is named with a version constraint; every silent-failure mode in the setup script has been replaced with an explicit non-zero exit and a remediation hint.

## Prerequisites

| Tool | Minimum | Install |
|---|---|---|
| Docker Engine | 24.0 | <https://docs.docker.com/engine/install/> (Linux) · Docker Desktop for macOS / Windows |
| Docker Compose v2 | 2.0 | Bundled with Docker Desktop. On Linux: <https://docs.docker.com/compose/install/linux/>. **The v1 standalone `docker-compose` is end-of-life and the setup script rejects it.** |
| OpenSSL **or** Python 3 | any | <https://www.openssl.org/source/> · <https://www.python.org/downloads/>. Used to generate the JWT signing key. |
| Git (optional) | 2.30 | <https://git-scm.com/downloads>. Required only to clone this repo. |
| PowerShell (optional) | 7.5 | <https://learn.microsoft.com/powershell/scripting/install/installing-powershell>. Required only to run `walkthroughs/`. |

Three TCP ports must be free on the host: **80**, **443**, **8080**. The setup script probes them and exits non-zero with a remediation hint if any are bound.

## Setup

```bash
git clone https://github.com/Sorcha-Platform/Sorcha.git
cd Sorcha
./scripts/sorcha-setup.sh
```

The script:

1. **Checks every prerequisite** in one pass. On failure, every gap is reported and the script exits non-zero with a single-line message of the form `[sorcha-setup] missing prerequisite: <name> (≥ <version>); install via <link>`.
2. **Asks configuration questions** (admin email, password, tenant name). Use `--quiet` to accept defaults non-interactively.
3. **Writes `.env`** with a freshly-generated JWT signing key.
4. **Pulls images** for every service.
5. **Starts services** via `docker compose up -d`.
6. **Waits for `/api/health`** to return 200 (timeout 60s). On timeout the script exits non-zero rather than printing a misleading success summary.

On success the final line is:

```
[sorcha-setup] success — gateway reachable at http://localhost. Verify with: curl -s http://localhost/api/health
```

## Verify your installation

```bash
curl -s http://localhost/api/health
```

Expected JSON shape (exact field values vary):

```json
{
  "status": "Healthy",
  "totalDuration": "00:00:00.0234567",
  "services": {
    "gateway": "Healthy",
    "blueprint-service": "Healthy",
    "wallet-service": "Healthy",
    "register-service": "Healthy",
    "tenant-service": "Healthy",
    "peer-service": "Healthy",
    "validator-service": "Healthy"
  }
}
```

If every entry under `services` is `Healthy`, the gateway is live and the underlying stack is up.

For the AI-discoverable surface, also try:

```bash
curl -s http://localhost/.well-known/openapi.json | head -20
curl -s http://localhost/.well-known/mcp.json
curl -s http://localhost/llms.txt
```

## Common failures

| Symptom | Likely cause | Fix |
|---|---|---|
| `[sorcha-setup] missing prerequisite: docker` | Docker not on `PATH` | Install per the table above and re-source your shell so `docker` is found. |
| `[sorcha-setup] missing prerequisite: docker-daemon` | Docker is installed but the daemon isn't running | Start Docker Desktop (Win/macOS) or `sudo systemctl start docker` (Linux). |
| `[sorcha-setup] missing prerequisite: docker-compose-v2` | Only the v1 standalone `docker-compose` is on `PATH` | Install Compose v2 (the `docker compose` plugin). v1 is past EOL. |
| `[sorcha-setup] missing prerequisite: port-80-free` | Another process is bound to port 80 | `sudo ss -tlnp \| grep :80` to identify, then stop that process. Common culprits: Apache, nginx, IIS. |
| `Gateway did not become healthy in the allotted window` | One service is stuck or its dependencies aren't ready | `docker compose logs -f` to see which service is failing. Database container often needs more time on slow disks; re-run the script after a minute. |
| `curl: (7) Failed to connect to localhost port 80` | Setup script reported success but gateway is down | Check `docker compose ps` — gateway is `Up` but unhealthy is the most common shape. `docker compose restart gateway` clears most transient cases. |
| Walkthrough scripts fail with `pwsh: command not found` | PowerShell 7.5+ is not installed | Install per the optional row above. PowerShell is only needed for the `walkthroughs/` end-to-end demos; the gateway and services run without it. |

## Next steps

- **Agent integration.** Read `docs/mcp-server.md` for the MCP connection guide. The 36 tools across admin / designer / participant slices are how an AI agent drives the platform.
- **Walkthroughs.** `walkthroughs/TradeFinance/run.ps1` and `walkthroughs/AssuredIdentity/run-agents.ps1` are runnable end-to-end demonstrations of the cryptographic-proof workflows the platform is designed for.
- **Deeper reading.** `STANDARDS.md` for the standards posture, `docs/architecture.md` (Phase 8 of spec 117) for the system architecture, `llms.txt` for the LLM-led entry point.

## Reporting setup-script issues

If the script fails in a way not covered above, capture the failing command and exit code with:

```bash
./scripts/sorcha-setup.sh 2>&1 | tee sorcha-setup.log
```

and open an issue at <https://github.com/Sorcha-Platform/Sorcha/issues> attaching `sorcha-setup.log` and the output of `docker compose ps`.
