# Implementation Plan: Assured Identity Demo Environment

**Branch**: `144-assured-identity-demo` | **Date**: 2026-05-31 | **Spec**: [spec.md](./spec.md)
**Input**: Feature specification from `/specs/144-assured-identity-demo/spec.md`
**Design note**: `docs/superpowers/specs/2026-05-31-assured-identity-demo-environment-design.md`

## Summary

Build the **operability layer** that turns the already-proven cross-installation Assured Identity loop into a standing, node-agnostic demo. The deliverable is a **PowerShell provisioning toolkit** (four operations: provision an issuing authority, connect a subscriber, reset, status), driven by a **node inventory** file, that orchestrates *existing* Sorcha HTTP endpoints and the *existing* `sorcha-agent` CLI — plus parameterised agent + blueprint config templates, an operator/tester runbook, skill + memory alignment, and a final cleanup that retires the legacy walkthrough. **No new .NET service code and no new tester UI**; the tester journey runs entirely on existing product surfaces (`/new-submissions`, F128 onboarding, the Citizen Wallet PWA).

Technical approach in one line: a node-inventory-driven PowerShell module that wraps the proven `deploy/twoinstall-*.ps1` flows into idempotent, readiness-gated, parameterised commands, with pure-logic units (inventory parse, name injection, readiness predicate, idempotency reconciliation) covered by Pester and the integrated flow proven by a live green run.

## Technical Context

**Language/Version**: PowerShell 7+ (provisioning toolkit + tests). Config artefacts are JSON. Consumes existing .NET 10 services over HTTP and the existing `Sorcha.Agent` .NET CLI as a child process.
**Primary Dependencies**: existing `walkthroughs/modules/SorchaWalkthrough/SorchaWalkthrough.psm1` helpers (reused, not rebuilt); `sorcha-agent` CLI (existing, `rules`/`ai` engines); a running Sorcha platform per installation (gateway, tenant, register, blueprint, wallet, peer services). No new NuGet/package dependencies.
**Storage**: gitignored config + state files only — `demo-nodes.json` (inventory), `deploy/keys.env` (secrets, existing), per-run `state.json` (provisioned artefact IDs for idempotency/reset). No database, no migrations.
**Testing**: Pester for pure-logic units (inventory loader, agency-name injection, readiness predicate, idempotency reconciliation, status verdict). E2E "green run" on the default node pair (`tiny` issuer / `n1` subscriber) for the integrated flow (SC-001/002/003/005/006/007). No xUnit (no .NET code added).
**Target Platform**: cross-platform PowerShell — operator runs on Windows/macOS/Linux dev box; installations are Linux Docker hosts reached over HTTP(S) via the gateway.
**Project Type**: tooling / demo toolkit (scripts + config + docs alongside the repo). Not single/web/mobile.
**Performance Goals**: demo to "ready" (issuer + 1 subscriber) ≤10 min (SC-001); tester loop ≤5 min with zero transient "service unavailable" (SC-002); readiness gate absorbs the ≤60s blueprint-recovery window; default deterministic approval completes fast enough not to stall a live demo (FR-013).
**Constraints**: idempotent re-provision (FR-003); node-agnostic config (FR-006); no shared JWT signing keys across installations (FR-009); no new tester UI (FR-016); secrets never committed (Constitution II); graduation cleanup gated on a green run (FR-021).
**Scale/Scope**: 2+ installations; 1 issuer + N independent subscribers of one advertised register; single concurrent tester assumption for reset.

### Grounded integration signals (resolved during planning)

| Concern | Signal (existing endpoint) | Ready value |
|---|---|---|
| Subscribe org to advertised register | `POST /api/organizations/{orgId}/register-subscriptions` (`RequireAdministrator` + `RequirePlatformAudience`); `GET /api/organizations/{orgId}/register-subscriptions/{registerId}` | `status == Active` |
| Replication caught up | `GET /api/registers/{id}/sync-state` (F108) | `state == CaughtUp` |
| Local relationship | `GET /api/registers/{id}/local-relationship` (F108) | `isSubscriber == true` |
| Service available to citizens (absorbs ≤60s recovery) | `GET /api/registers/{id}/blueprints/published` (anonymous) | target blueprint present in `blueprints[]` |
| Instance-create failure when not yet recovered | `POST /api/instances` (`CanExecuteBlueprints`) | `409 blueprint_not_available` (the error the readiness gate exists to prevent the tester seeing) |

**Connect-Subscriber readiness predicate** = subscription `Active` **AND** sync-state `CaughtUp` **AND** target blueprint present in `/blueprints/published`.

## Constitution Check

*GATE: Must pass before Phase 0 research. Re-check after Phase 1 design.*

This feature adds **no .NET service code**, so several constitution gates are scope-N/A rather than violated. Assessment:

| Principle | Applies? | Compliance |
|---|---|---|
| I. Microservices-First | N/A | No new service; toolkit composes existing services over HTTP, no new coupling. |
| II. Security First | **Yes** | Secrets stay in gitignored `deploy/keys.env`; **each installation keeps its own JWT signing key — never shared** (FR-009); trust boundary is the register (wallet sigs + roster). Inventory + state files carry no secrets. ✅ |
| III. API Documentation | N/A | No new APIs. Toolkit consumes existing OpenAPI surfaces. |
| IV. Testing Requirements | **Adapted** | 80%+ xUnit coverage targets a .NET core lib — none added here. Proportionate equivalent: Pester unit tests for all pure-logic units + a live E2E green run as the integration gate. Deterministic units; the E2E is environment-dependent by nature. ✅ |
| V. Code Quality | **Adapted** | PowerShell, not C#. Follows existing `SorchaWalkthrough` module conventions (Write-Wt* helpers, `$ErrorActionPreference='Stop'`, SPDX header). ✅ |
| VI. Blueprint Standards | **Yes** | Blueprint stays a JSON template (`assured-identity.json`); agency name injected via token substitution at publish, not C#. ✅ |
| VII. Domain-Driven Design | **Yes** | Docs + commands use ubiquitous language (Blueprint, Action, Participant, Publish, Register, Disclosure). ✅ |
| VIII. Observability | **Adapted** | Toolkit is not a service. Operational visibility delivered by `Get-DemoStatus`; the launched `sorcha-agent` already writes a JSONL audit trail. No new OTel surface required. ✅ |

**Verdict**: PASS. No violations requiring Complexity Tracking. The single deliberate deviation — PowerShell tooling instead of a .NET service — is inherent to "an operability layer over proven transport" and is the simplest thing that delivers the feature (it promotes scripts that already work). Building this as a .NET service would be unjustified complexity.

## Project Structure

### Documentation (this feature)

```text
specs/144-assured-identity-demo/
├── plan.md              # This file
├── research.md          # Phase 0 output
├── data-model.md        # Phase 1 output (entities + file schemas + state machines)
├── quickstart.md        # Phase 1 output (operator + tester runbook seed)
├── contracts/           # Phase 1 output (command + file-format contracts)
│   ├── commands.md          # The 4 command contracts (params, behaviour, outputs, exit codes)
│   ├── demo-nodes.schema.json   # Node inventory JSON schema
│   └── demo-state.schema.json   # Per-run state.json schema
└── checklists/
    └── requirements.md  # Spec quality checklist (already passing)
```

### Source Code (repository root)

```text
demos/AssuredIdentity/                       # NEW first-class demo home (FR-019)
├── DEMO.md                                  # Operator + tester runbook (FR-020)
├── demo-nodes.example.json                  # Inventory template (real one gitignored)
├── AssuredIdentityDemo.psm1                 # The toolkit module — 4 exported commands
│   #   New-IssuingAuthority, Connect-Subscriber, Reset-Demo, Get-DemoStatus
├── lib/                                      # Internal (non-exported) helpers
│   ├── NodeInventory.ps1                     # Load/validate inventory, select by id
│   ├── AgencyNaming.ps1                      # Single-source agency-name injection
│   ├── Readiness.ps1                         # Readiness predicate (subscribe∧caughtup∧published)
│   ├── Idempotency.ps1                       # Detect/reuse + stale-subscription reconcile
│   └── AgentLaunch.ps1                       # Render actor config + launch/instruct per mode
├── agent/
│   ├── analyst.rules.template.json           # Deterministic approver actor (tokenised)
│   ├── analyst.ai.template.json              # AI-persona approver actor (tokenised)
│   └── analyst.persona.md                    # AI persona prompt
├── blueprints/
│   └── assured-identity.template.json        # Blueprint w/ {{issuerName}} token (from walkthrough)
└── tests/                                     # Pester unit tests
    ├── NodeInventory.Tests.ps1
    ├── AgencyNaming.Tests.ps1
    ├── Readiness.Tests.ps1
    └── Idempotency.Tests.ps1

walkthroughs/modules/SorchaWalkthrough/      # REUSED unchanged (shared helpers)

# Retired in the final cleanup phase (FR-021), gated on a green run:
#   walkthroughs/AssuredIdentity/**  (setup.ps1, run-phase1-identity.ps1, run-phase2-licence.ps1,
#                                     run-agents.ps1, run-crossnode-*.ps1, run-multi-peer.ps1, scratch)
#   deploy/twoinstall-issuer.ps1, deploy/twoinstall-citizen-n1.ps1, twoinstall-*state.json

# Updated for alignment (FR-022):
#   .claude/skills/walkthrough-builder/SKILL.md   (demo = mature walkthrough concept)
#   .claude/skills/sorcha-architecture/SKILL.md   (F143 section → points at the demo)
#   .claude/skills/n1-deploy/ + network-bootstrap  (AssuredIdentity refs → demos/AssuredIdentity)
#   CLAUDE.md  (brief demos/ taxonomy line, if warranted)
#   memory: f143-two-installation-demo.md + MEMORY.md
```

**Structure Decision**: A new top-level `demos/AssuredIdentity/` tree (confirms the design's assumed path) hosts the toolkit, config templates, tests, and runbook. It imports the existing `SorchaWalkthrough` helper module rather than duplicating it. The legacy `walkthroughs/AssuredIdentity/` + `deploy/twoinstall-*` scripts are retired only in the final, green-run-gated cleanup phase.

## Complexity Tracking

> No constitution violations. Table intentionally empty.
