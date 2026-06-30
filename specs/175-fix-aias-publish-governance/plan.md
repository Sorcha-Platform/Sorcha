# Implementation Plan: Fix AIAS Demo Blueprint-Publish Governance Gap

**Branch**: `175-fix-aias-publish-governance` | **Date**: 2026-06-30 | **Spec**: [spec.md](./spec.md)

**Input**: Feature specification from `/specs/175-fix-aias-publish-governance/spec.md`

## Summary

The AIAS demo provisioning fails because its register is created **owned by the sysadmin (docker) bootstrap account**, while the AIAS blueprint, participant, and public-org subscription are driven by the **verification-admin (issuer) wallet** — which holds no publish-governance role on that register. The F142 PublishGate therefore rejects the publish with `403 "caller lacks a publish-governance role on register"`, and the same missing wallet↔register governance relationship surfaces downstream as the ~90s participant-publish seal timeout and the public-org auto-subscribe 500.

**Technical approach**: Mirror the **AssuredIdentity demo (Pattern A)**. Create the AIAS register so the verification-admin (issuer) wallet is its on-ledger owner — `New-SorchaRegister -OwnerUserId <vAdmin.UserId> -OwnerWalletAddress <vWallet.Address> -Headers <vAdmin.Headers>` — and publish the blueprint/participant with a **freshly minted verification-admin session** so the JWT carries the `wallet_address` claim the PublishGate matches against the register roster. The shared helper `New-SorchaRegister` already supports owner-wallet ownership and a distinct `-WalletSignerHeaders` signer context, so **no shared-helper change is required**. The change is confined to `demos/AIAS/`.

## Technical Context

**Language/Version**: PowerShell 7+ (`.psm1` / `.ps1` provisioning modules and entry scripts)

**Primary Dependencies**: Shared module `walkthroughs/modules/SorchaWalkthrough/SorchaWalkthrough.psm1` — specifically `New-SorchaRegister`, `Publish-SorchaBlueprint`, `New-SorchaRegisterSubscription`, `Connect-SorchaUser`, `Get-SorchaRegisterByName`. Backing platform endpoints (Register, Blueprint, Tenant, Wallet services) consumed unchanged.

**Storage**: N/A (provisioning script; no new persistence). State is the platform's registers/blueprints created against a running Docker stack, plus the written AIAS agent-config artefact.

**Testing**: Manual end-to-end verification by running `demos/AIAS/run-demo.ps1` against a clean Docker stack (per spec Assumptions — no automated unit test required for this provisioning-script fix). Non-regression check: AssuredIdentity (and Membership renderer) still run clean.

**Target Platform**: Local developer machine driving a clean `docker-compose up -d` Sorcha stack (API gateway on :80, services on Docker ports).

**Project Type**: Demo provisioning script (PowerShell), not application source.

**Performance Goals**: Participant-publish step completes within the normal readiness/seal window — eliminating the previously observed ~90s seal timeout (0 occurrences).

**Constraints**:
- EDIT ONLY `demos/AIAS/` (primarily `AiasDemo.psm1`, plus `run-demo.ps1` as the entry surface). The shared walkthrough governance helper MAY be touched **only if strictly required**, and any such change MUST preserve existing callers (AssuredIdentity, Membership, ForestryCertification, TradeFinance).
- MUST NOT modify `src/` — no `Sorcha.Agent`, no verify path, no service code.
- MUST preserve idempotent re-run behaviour (register reuse by name).

**Scale/Scope**: One demo's provisioning flow — a single register, one published blueprint, one published participant, one public-org subscription, one agent-config artefact. Net edit footprint is small and contained to `demos/AIAS/`.

## Constitution Check

*GATE: Must pass before Phase 0 research. Re-check after Phase 1 design.*

This change is a demo/provisioning-script governance fix, not application code. Most constitution principles (microservices architecture, API documentation, DDD, observability) govern `src/` services and do not apply. The relevant gates:

| Principle | Status | Notes |
|-----------|--------|-------|
| II. Security First | ✅ PASS | Establishes correct register ownership/governance (least-surprise authority). Signs the ownership attestation with the owning wallet. No secrets committed; uses the standard bootstrap account and demo credentials already in use by sibling demos. |
| IV. Testing Requirements | ✅ PASS (scoped) | Provisioning scripts are verified by end-to-end run, consistent with how AssuredIdentity/Membership are exercised. Spec Assumptions explicitly waive automated unit tests for this script fix. |
| V. Code Quality | ✅ PASS | Mirrors an established, reviewed pattern (AssuredIdentity Pattern A); no new abstractions. |
| VI. Blueprint Standards | ✅ PASS | Blueprint remains a JSON/YAML template published via the existing publish flow; unchanged. |
| VII. Domain-Driven Design | ✅ PASS | Uses ubiquitous terms (Register, Blueprint, Participant, Publish) consistently. |
| Branch & PR policy | ✅ PASS | Work is on feature branch `175-fix-aias-publish-governance`; will land via PR. |

**No violations.** Complexity Tracking section below is intentionally empty.

## Project Structure

### Documentation (this feature)

```text
specs/175-fix-aias-publish-governance/
├── plan.md              # This file (/speckit-plan command output)
├── research.md          # Phase 0 output — root-cause + pattern decision
├── data-model.md        # Phase 1 output — governance entities & relationships
├── quickstart.md        # Phase 1 output — clean-stack verification guide
├── checklists/          # Pre-existing spec checklist(s)
└── tasks.md             # Phase 2 output (/speckit-tasks — NOT created here)
```

### Source Code (repository root)

The change is confined to the AIAS demo directory. AIAS demo assets are not yet present in this working tree (per spec Assumptions); they are created/corrected mirroring the AssuredIdentity reference layout.

```text
demos/
├── AIAS/                              # ← ONLY edit surface
│   ├── AiasDemo.psm1                  # provisioning module (primary fix target)
│   ├── run-demo.ps1                   # entry script (clean-stack verification driver)
│   └── (blueprint template + README as needed, mirroring AssuredIdentity)
│
├── AssuredIdentity/                   # canonical reference — DO NOT MODIFY
│   └── AssuredIdentityDemo.psm1       # Pattern A: register owned by issuer wallet (:171, :186, :288)
└── Membership/                        # template renderer only — non-regression check

walkthroughs/modules/SorchaWalkthrough/
└── SorchaWalkthrough.psm1             # shared helper — NO CHANGE EXPECTED
    # New-SorchaRegister (:1184) already exposes -OwnerWalletAddress + -WalletSignerHeaders
    # Publish-SorchaBlueprint (:1397), New-SorchaRegisterSubscription, Connect-SorchaUser
```

**Structure Decision**: Single-directory provisioning fix under `demos/AIAS/`. No `src/` changes. The shared `SorchaWalkthrough.psm1` is referenced (consumed) but not modified — it already supports the owner-wallet + distinct-signer governance shape this fix needs. AssuredIdentity is the reference pattern and remains untouched.

## Complexity Tracking

> No constitution violations — section intentionally empty.
