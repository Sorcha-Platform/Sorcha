---
title: Sorcha Architecture
description: How the Sorcha platform produces verifiable cryptographic evidence for multi-party workflows, by service, by data flow, and by published standard.
standards:
  - BIP32
  - BIP44
  - W3C Verifiable Credentials Data Model 2.0
  - HAIP 1.0
  - OpenID4VCI
  - OpenID4VP
last_updated: 2026-08-22
---

# Sorcha Architecture

Sorcha is programmable proof infrastructure for multi-party workflows. Every participant action is signed by a wallet they control. Every register entry is Merkle-chained to the entry before it. Every disclosure is bounded by a per-recipient symmetric key — the platform itself cannot read what it was not given the key for. The result is a system whose trust comes from evidence each party can verify independently, not from belief in a platform operator.

This document is the architectural overview. It is the entry point for an AI agent, an integrator, or a reviewer who has not seen the codebase before. It pairs with [`STANDARDS.md`](../STANDARDS.md) (the standards-compliance source of truth) and [`docs/security-model.md`](security-model.md) (selective disclosure, post-quantum posture, honest gaps).

## Architecture in One Paragraph

Eight services, each with a single responsibility. **Blueprints** define multi-step workflows with JSON-Schema-validated payloads and conditional routing — the process logic. **Wallets** hold BIP32/39/44 hierarchical-deterministic keys and sign every action — the identity and accountability layer. **Registers** are append-only ledgers with Merkle-chained dockets — the tamper-evident record. The **Validator** runs quorum consensus to seal transactions. The **Peer** service replicates state across participants over gRPC, with no central authority. **Tenant** handles multi-tenant isolation, JWT issuance, and platform-org topology. The **API Gateway** (YARP) is the single external surface and the home of the well-known discoverability endpoints. The **HAIP Service** is the boundary to the OpenID4VC / EUDI / GOV.UK Wallet ecosystem. Every protocol, data format, and cryptographic primitive is a published standard. Nothing is proprietary.

## Service Topology

```
┌─────────────┐     ┌─────────────────┐     ┌──────────────────┐
│  Sorcha UI  │────▶│   API Gateway   │────▶│  Blueprint Svc   │
│  (Blazor)   │     │     (YARP)      │     │  (Workflows)     │
└─────────────┘     └────────┬────────┘     └────────┬─────────┘
                             │                       │
                    ┌────────┴────────┐     ┌────────┴────────┐
                    │                 │     │                 │
              ┌─────▼─────┐    ┌──────▼─────┐    ┌────────────▼┐
              │  Wallet   │    │  Register  │    │  Validator  │
              │  Service  │    │  Service   │    │   Service   │
              └─────┬─────┘    └──────┬─────┘    └─────────────┘
              │PostgreSQL │    │  MongoDB   │    │    Redis    │
              └───────────┘    └────────────┘    └─────────────┘
```

| Service | Responsibility | Persistent store |
|---|---|---|
| Blueprint | Workflow definitions, instance state, schema validation, SignalR notifications | PostgreSQL + Redis |
| Register | Append-only ledger of signed transactions, Merkle-dockets, OData query | MongoDB + Redis |
| Wallet | BIP32/39/44 keys, signing operations, citizen wallet PWA backend | PostgreSQL + Redis |
| Tenant | Multi-tenant auth, JWT issuance, participant identity, register invitations, transactional email | PostgreSQL + Redis |
| Validator | Quorum consensus over transaction batches, chain-integrity verification | Redis (reads the Register's Mongo for the governance roster) |
| Peer | gRPC peer-to-peer replication, register sync, no central coordinator | PostgreSQL + Redis (reads the Register's Mongo system-register) |
| API Gateway | YARP reverse proxy, rate limiting, well-known endpoints, CORS surface | Redis (rate-limit / data-protection) |
| HAIP | OpenID4VCI issuer + OpenID4VP verifier — the boundary to the wallet ecosystem | Redis only (no database) |

Every HTTP service is fronted by the gateway; Wallet and HAIP have no host-published port (reached only through the gateway), and Peer additionally exposes a gRPC port for the cross-node mesh. Port assignments and the Aspire orchestration topology live in [`docs/getting-started/PORT-CONFIGURATION.md`](getting-started/PORT-CONFIGURATION.md); the full project tree is in [`docs/reference/project-structure.md`](reference/project-structure.md).

## How an Action Becomes Evidence

A Sorcha workflow proceeds in five well-defined steps. Every step produces evidence that any party can verify without trusting the platform.

1. **Blueprint defines the workflow.** A JSON or YAML document declares the participants, the actions, the schema for each action's payload, and the routing logic between actions. The blueprint is itself signed and published to the register, so the rules of the workflow are tamper-evident.
2. **A participant submits an action.** The participant's wallet signs the action payload with a key derived through BIP32/44 from a seed they control. The signature binds the participant's identity to the exact bytes of the payload.
3. **The Blueprint Service validates and routes.** Schema is enforced before the action is accepted. Conditional routing rules decide which participant the workflow advances to next.
4. **The Register Service appends the transaction.** The transaction is hashed into a Merkle docket. The docket carries the previous-hash of the docket before it. Each docket is sealed by the Validator's quorum signature.
5. **The Peer Service replicates.** Other peers running the same register pull the new dockets and re-verify every signature and every Merkle link. There is no central coordinator; every peer is an independent verifier.

At every step the cryptographic primitive is named and the algorithm is published — see [`STANDARDS.md`](../STANDARDS.md). At every step the evidence is independently checkable — see [`docs/security-model.md`](security-model.md).

## Selective Disclosure — Architectural, Not Policy

Sorcha implements selective disclosure as an *architectural* property of the platform, not as a policy enforced on top of an open data store. The mechanism is JSON Pointer paths plus per-recipient symmetric key wrapping inside an SD-JWT VC envelope. The platform can store an action, route it, and chain its hash into the docket — but if it was not given the key for a particular field, it cannot read that field. Auditors get cryptographic disclosures, not platform-asserted views.

The implementation is in `src/Common/Sorcha.Cryptography/SdJwt/` and conforms to the W3C Verifiable Credentials Data Model 2.0 with the SD-JWT VC profile.

## Wallets and Key Derivation

Sorcha wallets are HD wallets in the BIP32/39/44 sense. A user's seed is held client-side. Service-internal keys derive deterministically from a service-internal seed under a Sorcha-specific BIP44 purpose namespace. Derivation slots are listed in `src/Common/Sorcha.Wallet.Contracts/Constants/SorchaDerivationPaths.cs` — for example, slot 108 (`sorcha:citizen-holder`) anchors the citizen wallet PWA's holder key, and slot 109 (`sorcha:citizen-status-signing`) signs status list entries.

The cryptographic implementation lives in `src/Common/Sorcha.Cryptography/`. The platform supports ED25519, P-256, RSA-4096, ML-DSA (FIPS 204), and ML-KEM (FIPS 203). Algorithm selection is per-context — see the per-feature notes in `.claude/skills/sorcha-architecture/SKILL.md`.

## The HAIP Boundary — How Sorcha Meets the Wallet Ecosystem

Sorcha does not replace GOV.UK Wallet, the EU Digital Identity Wallet, or any other government-issued or commercial holder wallet. Sorcha is the workflow infrastructure that *issues to* and *verifies from* those wallets through the OpenID for Verifiable Credentials family of standards.

The HAIP Service implements:

- **OpenID4VCI** (issuer) — credential issuance flow, SD-JWT VC and mdoc credential formats. See `docs/openid4vc-haip-integration.md`.
- **OpenID4VP** (verifier) — credential presentation flow, including cross-device QR.
- **HAIP 1.0** — the High Assurance Interoperability Profile that the EUDI Wallet and the GOV.UK Wallet are converging on.

The HAIP Service is the *only* service that talks classical-only signatures at its wire boundary. This is by HAIP 1.0 specification — the wallet ecosystem standardised on classical signature suites. Sorcha bridges this with a classical co-key derived alongside the post-quantum primary. See [`docs/security-model.md`](security-model.md) for the full PQC-vs-HAIP discussion.

## Discovery Surface for AI Agents and Integrators

Sorcha publishes a machine-readable surface so an AI agent can find, parse, and reason over the platform without out-of-band documentation:

| Endpoint or file | Purpose |
|---|---|
| `GET /.well-known/openapi.json` | Aggregated OpenAPI 3.1 document for every service routed through the gateway, with `info.x-mcp-server` and `info.x-standards` extensions |
| `GET /.well-known/openapi.yaml` | YAML form of the same document |
| `GET /.well-known/mcp.json` | MCP server manifest — transports, authentication, tool catalogue location |
| [`llms.txt`](../llms.txt) | One-screen factual summary at the repository root, llmstxt.org-conforming |
| [`docs/llms-full.txt`](llms-full.txt) | Longer machine-readable narrative with security and integration pointers |
| [`STANDARDS.md`](../STANDARDS.md) | The single source of truth for every standard the platform implements |
| [`docs/quickstart.md`](quickstart.md) | Agent-runnable setup path against a clean Docker host |
| [`docs/mcp-server.md`](mcp-server.md) | MCP server reference — connecting, authentication, role slices, worked example |

These surfaces are tested and gated by the `ai-discoverability-check` CI workflow on every pull request to `master`.

## Source-of-Truth Pointers

| Topic | File |
|---|---|
| Project tree (every directory) | [`docs/reference/project-structure.md`](reference/project-structure.md) |
| Service-by-service detail | each service's own `src/Services/Sorcha.*.Service/README.md` |
| Full REST/gRPC reference | [`docs/reference/API-DOCUMENTATION.md`](reference/API-DOCUMENTATION.md) |
| Constitutional principles | [`.specify/constitution.md`](../.specify/constitution.md) |
| Active development guidelines and patterns | [`CLAUDE.md`](../CLAUDE.md) |
| Cross-cutting feature notes (Open Participants, x-review, Citizen Wallet PWA, etc.) | `.claude/skills/sorcha-architecture/SKILL.md` |
