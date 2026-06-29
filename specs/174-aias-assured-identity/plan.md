# Implementation Plan: AIAS Assured Identity with photo + autonomous Assure-ID agent

**Branch**: `174-aias-assured-identity` | **Date**: 2026-06-29 | **Spec**: [spec.md](./spec.md)

**Input**: Feature specification from `/specs/174-aias-assured-identity/spec.md`

> Program north-star: `docs/superpowers/specs/2026-06-29-aias-conference-demo-design.md` (M1 of M0–M5).

## Summary

Stand up a fictional assurance provider **Acme Identity Assurance Services (AIAS)** and deliver the
anonymous→assured journey end to end, issuing an **Assured Identity** credential that carries the
applicant's photo. The work is **~80% assembly of proven parts** (the `demos/AssuredIdentity`
blueprint + `Sorcha.Agent` + F107 portrait capture/embed + HAIP issuance) plus **one genuine code
addition**: an **external-check hook** on the agent's rules engine so the autonomous Assure-ID agent
can evaluate real signals (email-verified, photo-present, **postcode existence**, **profanity**) and
**reject** with an on-brand reason. Everything is reproduced from a clean network by a single
idempotent provisioning module under `demos/AIAS/`.

## Technical Context

**Language/Version**: C# 14 / .NET 10 (services + agent); PowerShell 7 (provisioning + rehearsal).

**Primary Dependencies (all existing, reused)**:
- `Sorcha.Agent` CLI — `RulesDecisionEngine` (JSON Logic), `AiDecisionEngine`, dual SignalR+polling listeners.
- Blueprint Service `ActionExecutionService` — `credentialIssuanceConfig` + `claimMappings` + routing.
- HAIP Service `/api/v1/offers` — OpenID4VCI credential issuance.
- Tenant Service — organisation create; existing anonymous signup + email verification.
- `Sorcha.UI.Components.User` `FileRenderer` + `PhotoTokenResizer` (F107) — portrait capture (camera/upload) + embed.
- **New external dependency**: UK **postcodes.io** (public, read-only) for the address-existence check, with a bundled offline fixture fallback.

**Storage**: No new storage. Reuses existing register (Mongo) for the workflow/decision record and Tenant (Postgres) for the org. No persona portrait persistence (out of scope per the settled model).

**Testing**: xUnit v3 for the agent external-check unit tests; a PowerShell **rehearsal hook** under `demos/AIAS/` exercising one approval + one rejection end to end.

**Target Platform**: Docker-first (`docker-compose`), then n1. Agent runs as a CLI process; UI is Blazor WASM behind the gateway.

**Project Type**: Web (Blazor WASM front + microservices back) plus a long-running CLI agent.

**Performance Goals**: Agent reaches + records a decision within **30 s** of submission (SC-003); applicant completes application incl. photo in **< 2 min** (SC-002).

**Constraints**: **Offline-capable** assurance — the postcode check must fall back to a bundled fixture when postcodes.io is unreachable (SC-007). Idempotent, reboot-proof provisioning (SC-001/SC-006).

**Scale/Scope**: Demo scale (single org, handfuls of applications); not a load-bearing production path.

## Constitution Check

*GATE: Must pass before Phase 0 research. Re-check after Phase 1 design.*

| Principle | Assessment |
|-----------|------------|
| Microservices-first; no new service | ✅ No new service. The external-check hook lives in the existing `Sorcha.Agent` CLI; issuance/org/signup reuse existing services. |
| Internal comms gRPC / external REST via gateway | ✅ Unchanged. The agent uses existing client paths; the only new outbound call is postcodes.io (external, read-only, over HTTPS). |
| Security / no new secrets | ✅ postcodes.io needs no key; profanity check is local; email-verified reuses existing signup state. No secrets added. |
| Blueprint as JSON/YAML (not Fluent) | ✅ The AIAS blueprint is a JSON template (reuses the `demos/AssuredIdentity` template + adds a reject route). |
| Testing (xUnit, integration/e2e) | ✅ xUnit unit tests for the checks + a PowerShell rehearsal hook (approval + rejection). |
| Docs (READMEs, specs, XML) | ✅ `demos/AIAS/README.md`, XML docs on new agent types, north-star + this spec/plan. |
| License headers, .NET 10 | ✅ Followed. |

**Result: PASS — no violations, Complexity Tracking not required.**

## Project Structure

### Documentation (this feature)

```text
specs/174-aias-assured-identity/
├── plan.md              # This file
├── research.md          # Phase 0 — reuse map + resolved decisions
├── data-model.md        # Phase 1 — entities (org, application, decision, agent config, credential)
├── quickstart.md        # Phase 1 — provision + run the AIAS demo (Docker then n1)
├── contracts/
│   ├── external-checks.md       # the rules-engine external-check hook contract
│   └── aias-rules.md            # the AIAS Assure-ID rules config shape (JSON Logic facts)
└── tasks.md             # Phase 2 — created by /speckit.tasks (NOT here)
```

### Source Code (repository root)

```text
src/Apps/Sorcha.Agent/
├── Decision/
│   ├── RulesDecisionEngine.cs        # EXTEND: invoke external checks, surface results as JSON-Logic facts
│   └── Checks/                       # NEW — the external-check hook
│       ├── IExternalCheck.cs         # one check = (application payload) -> named boolean fact(s)
│       ├── ExternalCheckRunner.cs    # runs configured checks, merges facts into the rules context
│       ├── PostcodeExistsCheck.cs    # postcodes.io + bundled-fixture offline fallback (config toggle)
│       ├── ProfanityCheck.cs         # local wordlist scan of submitted details
│       └── EmailVerifiedCheck.cs     # asserts the applicant's email-verified signal
└── (existing Inbox/Execution/Auth unchanged)

demos/AIAS/                            # NEW — the reboot-proof provisioning slice
├── AiasDemo.psm1                      # idempotent: org + branding + blueprint + agent config
├── blueprints/
│   └── aias-assured-identity.template.json   # base AssuredIdentity template + AIAS branding + reject route
├── agent/
│   ├── assure-id.rules.json          # AIAS Assure-ID rules (approve/reject on the check facts)
│   └── assure-id.checks.json         # which external checks to run + postcode offline fixture path
├── fixtures/
│   └── postcodes.offline.json        # bundled fallback dataset for offline venues
├── run-demo.ps1                      # provision (Docker-first / n1) + launch the agent
├── rehearse.ps1                      # test hook: one approval + one rejection end to end
└── README.md

tests/Sorcha.Agent.Tests/             # NEW or extend
└── Decision/Checks/                   # unit tests per check + the runner (fact merging, offline fallback)
```

**Structure Decision**: Single new code surface — the **external-check hook** under
`Sorcha.Agent/Decision/Checks/` — keeps the one genuine addition isolated and unit-testable. All
demo wiring (org, branding, blueprint, agent config, fixtures, rehearsal) lives under `demos/AIAS/`,
mirroring the proven `demos/AssuredIdentity` layout so provisioning is one idempotent module. No
service, storage, or schema changes.

## Complexity Tracking

> Not required — Constitution Check passed with no violations.
