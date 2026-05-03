# T011 — `docker-compose.yml` topology comment audit

**Spec**: 117-ai-discoverability · **Task**: T011 · **Date**: 2026-05-02

## Finding

`docker-compose.yml` lines 1–30 contain four comment blocks:

| Lines | Subject |
|---|---|
| 1–2 | SPDX header + copyright |
| 4–9 | OpenTelemetry / `x-otel-env` anchor explanation |
| 11–19 | JWT shared config / `x-jwt-env` anchor explanation |
| 21–30+ | Platform connection-string cascade explanation |

**No service-topology comment block is present** within the first 30 lines (or anywhere else in the file). FR-034 requires a comment block naming every service, its port, and a one-line description of its purpose, within the first 30 lines.

## Implication for T093

T093 (Phase 7 US5) needs to inject a new comment block. Two placement options:

1. **Top of file** (after the SPDX header, before the `x-otel-env` anchor at line 4) — most discoverable for an AI agent reading the file top-to-bottom.
2. **Just before the `services:` key** — most contextually adjacent to what it describes.

**Recommendation**: option (1). The spec says "within the first 30 lines"; placing the topology block at the top guarantees it.

## Topology block content (sketch for T093)

The block should match the service inventory in `CLAUDE.md` § Architecture. Approximate shape (T093 will refine when it lands):

```yaml
# Sorcha service topology (Docker mode):
#   gateway      :80    YARP API gateway, anonymous /.well-known/* + Scalar /openapi
#   blueprint    :5000  Workflow management, SignalR notifications
#   wallet       (int)  Crypto operations, HD wallets (BIP32/39/44)
#   register     :5290  Distributed ledger, OData query surface
#   tenant       :5110  Multi-tenant auth, JWT issuer, Participant Identity
#   peer         :5002  P2P network, gRPC inter-peer transport
#   validator    (int)  Consensus, chain integrity
#   mcp-server   (int)  MCP tool surface (36 tools across 3 categories)
#   postgres     :5432  Relational store
#   mongodb      :27017 Document store
#   redis        :6379  Cache + SignalR backplane
#   aspire-dashboard :18888 Telemetry UI
```

T093 should source authoritative ports/purposes from `docker-compose.yml` itself (the `ports:` blocks) and from `docs/getting-started/PORT-CONFIGURATION.md`, not from this audit.

## Status

**Phase 7 has no blocker from this audit** — the change is a pure additive comment, no code or compose-graph rework needed.
