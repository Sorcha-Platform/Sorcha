# TradeFinance Actor Agents

Autonomous actor definitions for the TradeFinance walkthrough — 6 actors across 2 registers (procurement + invoice finance) with cross-register verifiable credentials.

## Architecture

```
Procurement-to-Pay Register          Invoice Finance Register
───────────────────────────          ────────────────────────
1. Raise PO (procurement-mgr)        1. Request Financing (finance-director)
2. Acknowledge PO (sales-mgr)           ← requires VerifiedInvoiceCredential
3. Confirm Delivery (sales-mgr)       2. Credit Assessment (assessment-svc)
4. Goods Received (site-mgr)          3. Evaluate Application (credit-analyst)
5. Raise Invoice (sales-mgr)          4. Approve Financing (credit-analyst)
6. Approve Invoice (procurement-mgr)     → issues TradeFinanceCredential
   → issues VerifiedInvoiceCredential
```

## Cross-Register Credential Flow

1. Procurement Action 6 approves invoice → **VerifiedInvoiceCredential** issued to sales-mgr
2. Finance Action 1 requires the credential (FailOpen revocation check)
3. Platform validates credential automatically — finance-director presents it when submitting
4. Finance completes → **TradeFinanceCredential** issued to finance-director

Actors are stateless. The platform enforces ordering via credential requirements.

## Actors

| File | Role | Org | Register | Actions |
|------|------|-----|----------|---------|
| procurement-mgr.json | Procurement Manager | Cairngorm | Procurement | 1, 6 |
| sales-mgr.json | Sales Manager | Highland Timber | Procurement | 2, 3, 5 |
| site-mgr.json | Site Manager | Cairngorm | Procurement | 4 |
| finance-director.json | Finance Director | Highland Timber | Finance | 1 |
| assessment-svc.json | Assessment Service | Trade Credit Bureau | Finance | 2 |
| credit-analyst.json | Credit Analyst | ScotTrade Finance | Finance | 3, 4 |

## Running

```powershell
# Setup (creates 4 orgs, 6 wallets, 2 registers, 2 blueprints)
pwsh walkthroughs/TradeFinance/setup.ps1

# Run with autonomous actors (creates both instances, starts 6 agents)
pwsh walkthroughs/TradeFinance/run-agents.ps1
```

## Relationship to MCP Agent Model

The TradeFinance walkthrough was originally designed for MCP-based Claude Code agents (2 sessions, 3 MCP connections each). The sorcha-agent actor model is simpler:

| Aspect | MCP Model | Actor Model |
|--------|-----------|-------------|
| Processes | 2 Claude sessions | 6 lightweight agents |
| Decision | AI-generated | Rules-based (deterministic) |
| Discovery | MCP polling (10s) | SignalR + polling fallback |
| Coordination | Register replication | Register replication (same) |
| Deployment | Requires Claude Code | Standalone binary |

The existing agent prompts (`prompts/buyer-agent.md`, `prompts/supplier-agent.md`) can be used with the `ai` mode if non-deterministic responses are desired.
