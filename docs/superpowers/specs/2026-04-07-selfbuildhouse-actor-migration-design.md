# SelfBuildHouse Actor Migration Design

**Date:** 2026-04-07
**Status:** Draft
**Scope:** Migrate SelfBuildHouse walkthrough to sorcha-agent actor-based execution

---

## Problem

SelfBuildHouse runs as a single-threaded PowerShell script orchestrating 7 participants across 2 registers (planning + building standards) with 14 total actions. Needs migration to independent actor processes.

## Solution

7 actor definition files, each containing rules for their actions across both blueprints. Actors are completely stateless — cross-register credential requirements are enforced by the platform's credential validation layer, not by actor logic.

### Key Insight

The actor inbox is wallet-scoped, not register-scoped. When an org is subscribed to both registers, actions from both blueprints appear in the actor's inbox. The launcher creates both blueprint instances upfront; credential requirements (PlanningPermissionCredential gates building warrant Action 1) naturally enforce execution ordering.

### Actor Definitions

| Actor | Planning Actions | Warrant Actions |
|-------|-----------------|-----------------|
| self-builder | Submit Application (1) | Submit Warrant App (1) |
| structural-engineer | Site Investigation (2) | Structural Calcs (2) |
| ecologist | Ecological Survey (3), Species Mitigation (4) | — |
| utilities-officer | Utilities Consultation (5) | — |
| planning-officer | Planning Review (6), Issue Permission (7) | — |
| building-standards-officer | — | Standards Review (3), Issue Warrant (4) |
| building-inspector | — | Foundation (5), Structure (6), Final (7) |

### Launcher

`run-agents.ps1`:
1. Read state.json for both blueprint IDs
2. Create two instances (planning + building warrant) via Blueprint Service API
3. Set password env vars from state
4. Start 7 actor processes
5. Wait for completion or timeout (10 min for full 14-action flow)

### What Changes

No changes to Sorcha.Agent source code. Only new walkthrough files:

| File | Purpose |
|------|---------|
| `walkthroughs/SelfBuildHouse/actors/*.json` | 7 actor definition files |
| `walkthroughs/SelfBuildHouse/run-agents.ps1` | Launcher with dual-instance creation |
| `walkthroughs/SelfBuildHouse/actors/README.md` | Usage docs |

### Scenario Coverage

Actor files use Scenario A (happy path) payloads — the simplest complete flow (6 planning + 7 building = 13 actions, skipping conditional Species Mitigation). Scenarios B and C remain in `run.ps1` for detailed testing.

### Out of Scope

- Scenario B/C in actor mode (conditional routing works but needs different payload data)
- File uploads (SelfBuildHouse doesn't use file-reference fields)
- Changes to setup.ps1
