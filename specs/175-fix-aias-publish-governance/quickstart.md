# Quickstart: Verify the AIAS Publish-Governance Fix

This is a validation/run guide. It proves the feature end-to-end against a clean Docker stack. Implementation details live in `tasks.md`; governance entities/relationships live in [data-model.md](./data-model.md); the root-cause and pattern decision live in [research.md](./research.md).

## Prerequisites

- .NET 10 SDK and Docker Desktop installed.
- PowerShell 7+.
- Repo checked out on branch `175-fix-aias-publish-governance`.
- Shared module available: `walkthroughs/modules/SorchaWalkthrough/SorchaWalkthrough.psm1` (consumed unchanged).
- Reference for the established pattern: `demos/AssuredIdentity/AssuredIdentityDemo.psm1` (Pattern A — register owned by the issuer wallet).

## Setup — clean Docker stack

```bash
# From repo root
docker-compose down -v          # ensure NO prior AIAS state
docker-compose up -d            # API gateway :80, services on Docker ports
# Wait for health: http://localhost/health (gateway) reports healthy
```

## Scenario 1 (P1) — Provisioning reaches blueprint publish with no 403

```powershell
pwsh demos/AIAS/run-demo.ps1
```

**Expected outcomes** (maps to FR-001..FR-003, FR-006, FR-007, SC-001, SC-002):
- The AIAS register is created with the **verification-admin (issuer) wallet as an owner** on the roster (not the sysadmin/docker account).
- The blueprint-publish step completes with **HTTP 2xx — zero `403 "caller lacks a publish-governance role on register"`**.
- The AIAS **agent configuration file is written** and the demo reports an authority-ready state.

## Scenario 2 (P2) — Participant publish & public-org subscription have no governance failures

Observed during the **same** run as Scenario 1.

**Expected outcomes** (maps to FR-004, FR-005, SC-003, SC-004):
- The participant-publish step **seals within the normal readiness window** — no ~90s seal timeout (0 occurrences).
- The Sorcha public-organisation subscription to the AIAS register **succeeds with no HTTP 500**.

## Scenario 3 (P3) — Idempotent re-run

```powershell
pwsh demos/AIAS/run-demo.ps1          # run again, same stack
```

**Expected outcomes** (maps to FR-008):
- The second run **reuses the existing AIAS authority/register** (or cleanly re-provisions) and reaches an authority-ready state **without ownership/governance errors**.

## Non-regression check (maps to SC-005, SC-006, FR-009, FR-010)

```powershell
pwsh demos/AssuredIdentity/run-demo.ps1   # or its documented entry script
# Membership renderer (template-only) still renders as before.
```

- AssuredIdentity (and the Membership renderer) provision/render successfully — confirming any shared-helper interaction is non-breaking.
- Confirm the diff is contained: **all changes under `demos/AIAS/`** (and, only if strictly required, the single shared walkthrough governance helper). **No changes under `src/`.**

```bash
git diff --name-only master...HEAD     # expect only demos/AIAS/** (+ specs/175-*) ; never src/**
```

## Pass criteria summary

| Check | Source |
|-------|--------|
| Blueprint publish: 0 × HTTP 403 governance failures | SC-001 |
| Agent config written (authority-ready) | SC-002 |
| Participant publish: 0 × ~90s seal timeout | SC-003 |
| Public-org subscription: 0 × HTTP 500 | SC-004 |
| AssuredIdentity + Membership unchanged-or-passing | SC-005 |
| All changes within `demos/AIAS/`; none under `src/` | SC-006 |
