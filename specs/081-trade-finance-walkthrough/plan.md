# Implementation Plan: Trade Finance Walkthrough

**Branch**: `081-trade-finance-walkthrough` | **Date**: 2026-04-02 | **Spec**: [spec.md](spec.md)
**Input**: Feature specification from `/specs/081-trade-finance-walkthrough/spec.md`

## Summary

Build a multi-organisation, multi-peer walkthrough demonstrating Sorcha's procurement-to-pay capabilities for SME trade finance. The walkthrough uses two blueprint workflows across two registers with a cross-register verifiable credential chain, agent-driven participants via MCP Server, and a DevMode-to-FLE transition. It is driven by a manifest file and setup wizard that supports both single-machine and multi-machine deployments.

## Technical Context

**Language/Version**: PowerShell 7+ (setup scripts), JSON (blueprints, manifests, scenarios, configs), Markdown (agent prompts, personas, documentation)
**Primary Dependencies**: Sorcha CLI (v1.1.0+), Sorcha MCP Server, SorchaWalkthrough PowerShell module (shared)
**Storage**: N/A — file-based walkthrough artifacts; platform state managed by Sorcha services
**Testing**: Scripted golden-path scenarios provide deterministic verification; manual multi-peer execution for demo validation
**Target Platform**: Windows/Linux with Claude Code CLI and remote Sorcha instance access
**Project Type**: Walkthrough (content + scripts) — no new services, no C# code
**Performance Goals**: N/A — demo walkthrough, not performance-critical
**Constraints**: Must work with existing Sorcha platform APIs; remote access only (no local Docker assumption); blueprints must follow existing JSON template patterns
**Scale/Scope**: 4 organisations, 6 participants, 2 registers, 2 blueprints, 3 scenarios, 6 agent prompts, 4 personas

## Constitution Check

*GATE: Must pass before Phase 0 research. Re-check after Phase 1 design.*

| Principle | Status | Notes |
|-----------|--------|-------|
| I. Microservices-First | N/A | Not adding services — walkthrough content only |
| II. Security First | PASS | Blueprints define FLE disclosure rules; no secrets committed; JWT tokens generated at runtime by setup wizard |
| III. API Documentation | N/A | Not adding APIs — consuming existing endpoints |
| IV. Testing Requirements | PASS | 3 scripted scenarios serve as deterministic test cases; golden path validates full 10-action flow |
| V. Code Quality | PASS | PowerShell scripts follow existing walkthrough patterns; JSON validated by blueprint publish |
| VI. Blueprint Standards | PASS | Blueprints authored as JSON files (primary format); follows existing template patterns from ConstructionPermit/SelfBuildHouse |
| VII. DDD Terminology | PASS | Uses correct terms: Blueprint, Action, Participant, Disclosure, Publish |
| VIII. Observability | N/A | Not adding services — platform observability already in place |

**Gate result: PASS** — no violations requiring justification.

## Project Structure

### Documentation (this feature)

```text
specs/081-trade-finance-walkthrough/
├── plan.md              # This file
├── spec.md              # Feature specification
├── research.md          # Phase 0: research findings
├── data-model.md        # Phase 1: manifest and scenario data structures
├── quickstart.md        # Phase 1: operator quick start guide
└── checklists/
    └── requirements.md  # Specification quality checklist
```

### Source Code (repository root)

```text
walkthroughs/TradeFinance/
├── config.json                          # Extended manifest (orgs, participants, registers, scenarios)
├── setup.ps1                            # PowerShell bootstrap (CI/legacy compatibility)
├── run.ps1                              # Scripted scenario runner
├── procurement-to-pay-template.json     # Blueprint 1: PO → Invoice (6 actions)
├── invoice-finance-template.json        # Blueprint 2: Finance request → Approval (4 actions)
├── data/
│   ├── scenario-golden-path.json        # Scripted: full happy path
│   ├── scenario-disputed.json           # Scripted: invoice disputed then approved
│   ├── scenario-declined.json           # Scripted: financing declined (low credit)
│   └── credit-scores.json              # Scripted buyer credit data for Credit Insurer
├── prompts/
│   ├── setup-wizard.md                  # Claude prompt: setup wizard instructions
│   ├── buyer-agent.md                   # Claude prompt: Box 1 (Buyer + Credit Insurer)
│   ├── supplier-agent.md               # Claude prompt: Box 2 (Supplier + Funder)
│   └── personas/
│       ├── cairngorm.md                 # Buyer persona for improvised mode
│       ├── highland-timber.md           # Supplier persona
│       ├── scottrade.md                # Funder persona
│       └── trade-credit.md             # Credit Insurer persona
├── mcp-configs/
│   └── template.json                   # MCP server config template (JWT placeholder)
└── docs/
    └── Trade-Finance-Walkthrough.md     # Full narrative documentation
```

**Structure Decision**: Follows existing walkthrough convention (`walkthroughs/<Name>/`). Blueprint template files sit in the walkthrough root (matching ConstructionPermit/SelfBuildHouse patterns — not in a subdirectory). The proposed `manifest.json` is merged into `config.json` (following the SelfBuildHouse extended config pattern with `registers` array and `templates` array). Agent prompts, personas, and MCP configs are additions specific to this walkthrough.
