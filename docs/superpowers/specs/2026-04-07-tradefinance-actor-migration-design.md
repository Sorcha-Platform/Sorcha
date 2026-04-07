# TradeFinance Actor Migration Design

**Date:** 2026-04-07
**Status:** Draft
**Scope:** Migrate TradeFinance walkthrough to sorcha-agent actor-based execution

---

## Problem

TradeFinance is the most complex walkthrough: 4 orgs, 6 participants, 2 registers, 10 actions, cross-register VCs, dispute loops, and field-level encryption. Currently runs as either a single-threaded PowerShell script or via 2 MCP-based Claude Code sessions.

## Solution

6 actor definition files with golden-path payloads. Launcher creates both blueprint instances and starts all actors. Cross-register credential requirements (VerifiedInvoiceCredential) enforce ordering. No Sorcha.Agent code changes needed.

### Key Design Points

- **Per-role credentials** from state.json (unlike SelfBuildHouse's single-admin model)
- **4 orgs, 6 actors** — each actor authenticates as its own org user
- **Cross-register VC flow**: Procurement Action 6 issues VerifiedInvoiceCredential → Finance Action 1 requires it
- **Dispute loop** not exercised in actor mode (golden path only) — `run.ps1` remains for disputed/declined scenarios
- **Complements existing MCP model** — actors provide deterministic rules-based execution alongside the AI-driven MCP agents

### Out of Scope

- Dispute/resubmit scenario in actor mode
- Declined financing scenario in actor mode
- FLE (field-level encryption) transition
- MCP-based agent replacement (coexists)
