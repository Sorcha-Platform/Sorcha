# Organization Ping-Pong Walkthrough

**Purpose:** Full-stack demonstration: org creation, participants, wallets, register, blueprint publish, and 20 round-trip ping-pong executions.
**Date Created:** 2026-02-09
**Status:** ✅ Complete
**Prerequisites:** Docker Desktop running, `docker-compose up -d`

---

## Overview

This walkthrough exercises the entire Sorcha pipeline end-to-end using Docker services. It creates an organization with 3 participants, provisions real wallets, creates a signed register, publishes the ping-pong blueprint, and executes 40 actions (20 round-trips) across two wallet addresses.

This builds on the simpler [PingPong](../PingPong/) walkthrough by adding real wallets, a real register (with 2-phase signed creation), and organization bootstrapping.

## Files in This Walkthrough

| File | Purpose |
|------|---------|
| `README.md` | This documentation |
| `test-org-ping-pong.ps1` | Main PowerShell script (10 phases) |

## Quick Start

```powershell
# 1. Start Docker services
docker-compose up -d

# 2. Wait for services to become healthy (~30 seconds)
Start-Sleep -Seconds 30

# 3. Run the walkthrough
./walkthroughs/OrganizationPingPong/test-org-ping-pong.ps1

# 4. Run with Aspire (for debugging with breakpoints)
./walkthroughs/OrganizationPingPong/test-org-ping-pong.ps1 -Profile aspire

# 5. Quick test with fewer round-trips
./walkthroughs/OrganizationPingPong/test-org-ping-pong.ps1 -RoundTrips 3 -ShowJson
```

## Parameters

| Parameter | Default | Description |
|-----------|---------|-------------|
| `-Profile` | `gateway` | `gateway` (port 80), `direct` (native ports), `aspire` (HTTPS) |
| `-AdminEmail` | `designer@pingpong.local` | Bootstrap admin email |
| `-AdminPassword` | `PingPong_2025!` | Bootstrap admin password |
| `-AdminName` | `Blueprint Designer` | Admin display name |
| `-OrgName` | `Ping-Pong Demo Corp` | Organization name |
| `-OrgSubdomain` | `pingpong-demo` | Organization subdomain |
| `-RoundTrips` | `20` | Number of ping-pong round-trips |
| `-ShowJson` | `$false` | Show request/response JSON bodies |
| `-SkipCleanup` | `$false` | Reserved for future cleanup logic |

## Service URLs by Profile

| Profile | Gateway | Tenant | Blueprint | Register | Wallet |
|---------|---------|--------|-----------|----------|--------|
| `gateway` | `http://localhost` | via gateway | via gateway | via gateway | via gateway |
| `direct` | `http://localhost` | `:5450` | `:5000` | `:5380` | via gateway* |
| `aspire` | `https://localhost:7082` | `:7110` | `:7000` | `:7290` | via gateway* |

\* Wallet Service has no external Docker port and is always accessed through the API Gateway.

## What This Walkthrough Does (10 Phases)

| Phase | Name | Description |
|-------|------|-------------|
| 0 | Prerequisites | Verify Docker is running and services are healthy |
| 1 | Bootstrap Organization | Create org + admin user, get JWT token (409 fallback to login) |
| 2 | Create Participants | Add 2 users to the organization (Alpha and Beta) |
| 3 | Create Wallets | Create 3 ED25519 wallets (designer, alpha, beta) |
| 4 | Create Register | 2-phase register creation: initiate, sign attestation, finalize |
| 5 | Load Blueprint | Load ping-pong template, patch wallet addresses, create blueprint |
| 6 | Publish Blueprint | Publish blueprint (expect cycle warnings for looping workflow) |
| 7 | Create Instance | Create workflow instance linked to register |
| 8 | Execute Round-Trips | Run N ping-pong rounds (2 actions per round) |
| 9 | Verify & Summary | Confirm instance is active, display results |

## Auth Model

The walkthrough uses a pragmatic authentication approach:

- **Bootstrap** creates the admin user (the "designer") with a password
- **Two additional users** are created in the organization for the record
- **All API calls** use the admin JWT token with distinct wallet addresses

This is realistic because the Blueprint Service validates JWT roles, not wallet-to-user binding. Future enhancement: enforce wallet ownership verification at the service level (see `.specify/MASTER-TASKS.md`).

## Expected Output

```
================================================================================
  Organization Ping-Pong Full-Stack Walkthrough
================================================================================

Profile: gateway (API Gateway on port 80)
...

================================================================================
  Phase 1: Bootstrap Organization
================================================================================
[i] Bootstrapping organization 'Ping-Pong Demo Corp'...
[OK] Organization bootstrapped successfully
[i] Organization ID: a1b2c3d4-...
...

================================================================================
  Phase 8: Execute 20 Ping-Pong Round-Trips (40 actions total)
================================================================================
  [Round  1/20] Ping OK -> Pong OK
  [Round  2/20] Ping OK -> Pong OK
  ...
  [Round 20/20] Ping OK -> Pong OK

[OK] All 40 actions executed successfully in 12.3s

================================================================================
  Organization Ping-Pong Walkthrough Results
================================================================================

  Organization:  Ping-Pong Demo Corp
  Participants:  3 (1 designer + 2 basic)
  Wallets:       3 (ED25519)
  Register:      a1b2c3d4e5...
  Blueprint:     ping-pong-org-20260209... (published)
  Round-trips:   20/20 completed
  Total actions: 40/40 (20 pings + 20 pongs)
  Duration:      18.5s

  -----------------------------------------------
  Steps:   8/8 passed
  Actions: 40/40 succeeded
  -----------------------------------------------

  RESULT: PASS - Full-stack pipeline verified!
```

## Troubleshooting

| Issue | Cause | Fix |
|-------|-------|-----|
| `Docker is not running` | Docker Desktop not started | Start Docker Desktop |
| `API Gateway health check returned error` | Services still starting up | Wait 30s, retry |
| `Bootstrap conflict (409)` | Organization already exists | Script auto-falls back to login |
| `Wallet creation failed` | Wallet Service not reachable | Check `docker-compose logs wallet-service` |
| `Register creation failed` | Register Service or signing issue | Check `docker-compose logs register-service` |
| `Action execution failed` | Blueprint not published or bad payload | Check response body (use `-ShowJson`) |
| `Aspire SSL errors` | Self-signed certificate | May need `-SkipCertificateCheck` in PowerShell 7+ |

## Re-running

The script is designed to be idempotent:
- Bootstrap falls back to login on 409 Conflict
- Participant creation tolerates duplicates
- Wallets, registers, blueprints, and instances all use timestamp-based unique IDs
- Each run creates fresh resources

## Related Documentation

- [PingPong Walkthrough](../PingPong/) - Simpler version without wallets/register
- [BlueprintStorageBasic](../BlueprintStorageBasic/) - Docker bootstrap basics
- [CLAUDE.md](../../CLAUDE.md) - Project guide
- [docs/PORT-CONFIGURATION.md](../../docs/getting-started/PORT-CONFIGURATION.md) - Port assignments
