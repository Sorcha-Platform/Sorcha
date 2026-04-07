# SelfBuildHouse Actor Agents

Autonomous actor definitions for the SelfBuildHouse walkthrough — 7 actors across 2 registers (planning + building standards) with cross-register credential chains.

## Architecture

```
Planning Register                    Building Standards Register
─────────────────                    ──────────────────────────
1. Submit Application (self-builder)  1. Submit Warrant App (self-builder) ← requires PlanningPermissionCredential
2. Site Investigation (structural)    2. Structural Calcs (structural)
3. Ecological Survey (ecologist)      3. Standards Review (bso)
4. Species Mitigation (ecologist)*    4. Issue Warrant (bso) → issues BuildingWarrantCredential
5. Utilities Consultation (utilities) 5. Foundation Inspection (inspector) ← requires BuildingWarrantCredential
6. Planning Review (planning)         6. Structure & Weathertight (inspector)
7. Issue Permission (planning)        7. Final Completion (inspector) → issues CompletionCertificateCredential
   → issues PlanningPermissionCredential
```

*Action 4 only triggers if protected species found (Scenario B)

## How Cross-Register Ordering Works

Actors don't need to know about register boundaries. The platform handles it:

1. Both blueprint instances are created at startup
2. All actors listen on their wallet (inbox spans all subscribed registers)
3. Building warrant Action 1 has a `credentialRequirement` for `PlanningPermissionCredential`
4. The platform blocks submission until the VC exists (issued by planning Action 7)
5. Once planning completes → VC issued → building warrant unblocks naturally

## Actors

| File | Role | Register | Actions |
|------|------|----------|---------|
| self-builder.json | Self-Builder | Both | Planning 1, Warrant 1 |
| structural-engineer.json | Structural Engineer | Both | Planning 2, Warrant 2 |
| ecologist.json | Ecologist | Planning | 3, 4 (conditional) |
| utilities-officer.json | Utilities Officer | Planning | 5 |
| planning-officer.json | Planning Officer | Planning | 6, 7 |
| building-standards-officer.json | Building Standards Officer | Building | 3, 4 |
| building-inspector.json | Building Inspector | Building | 5, 6, 7 |

## Running

```powershell
# Setup (creates orgs, wallets, registers, blueprints)
pwsh walkthroughs/SelfBuildHouse/setup.ps1

# Run with autonomous actors (creates both instances, starts 7 agents)
pwsh walkthroughs/SelfBuildHouse/run-agents.ps1
```

The launcher creates both blueprint instances then starts all 7 actors. The full flow (13 actions for happy path) completes within the 10-minute timeout.

## Authentication Model

All actors share a single admin identity (`$env:ADMIN_EMAIL` / `$env:ADMIN_PASSWORD`) but use different wallet addresses. The wallet address differentiates which participant each actor represents.
