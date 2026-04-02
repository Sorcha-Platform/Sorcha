# Quick Start: Trade Finance Walkthrough

## Prerequisites

- Sorcha CLI v1.1.0+ installed and on PATH
- Claude Code CLI installed
- Access to at least one remote Sorcha instance (e.g., `https://n1.sorcha.dev`)
- System admin credentials for the Sorcha instance

## Single-Machine Demo (Development)

### 1. Configure CLI Profile

```bash
sorcha config init --name demo --gateway https://n1.sorcha.dev
sorcha auth login --profile demo --username admin@sorcha.dev --password <admin-password>
```

### 2. Run Setup

```powershell
cd walkthroughs/TradeFinance
./setup.ps1 -Profile demo
# Creates: 4 orgs, 6 users, 6 wallets, 6 participants, 2 registers, 2 blueprints
# Outputs: state.json + MCP configs in mcp-configs/generated/
```

### 3. Configure MCP Connections

Copy the generated MCP configs into your Claude Code settings:
```bash
# The setup script outputs the path to the generated configs
# Merge them into ~/.claude/settings.json under "mcpServers"
```

### 4. Run with Claude Agent (Single Machine)

Start a Claude Code session with the buyer-agent prompt:
```bash
claude --prompt-file walkthroughs/TradeFinance/prompts/buyer-agent.md
```

Or run the traditional scripted walkthrough:
```powershell
./run.ps1 -Profile demo -Scenario golden-path
```

## Multi-Machine Demo (Two Peers)

### Box 1 (Buyer + Credit Insurer)

```powershell
# 1. Configure CLI
sorcha config init --name n1 --gateway https://n1.sorcha.dev
sorcha auth login --profile n1

# 2. Run setup selecting buyer-side orgs
./setup.ps1 -Profile n1 -Organizations cairngorm,trade-credit

# 3. Configure MCP and start agent
# Merge generated MCP configs for: procurement-mgr, site-mgr, assessment-svc
claude --prompt-file prompts/buyer-agent.md
```

### Box 2 (Supplier + Funder)

```powershell
# 1. Configure CLI
sorcha config init --name azure --gateway https://peer2.eastus.cloudapp.azure.com
sorcha auth login --profile azure

# 2. Run setup selecting supplier/funder-side orgs
./setup.ps1 -Profile azure -Organizations highland-timber,scottrade

# 3. Configure MCP and start agent
# Merge generated MCP configs for: sales-mgr, finance-director, credit-analyst
claude --prompt-file prompts/supplier-agent.md
```

## DevMode → FLE Transition

After running the golden path in DevMode:

```bash
# Disable DevMode on both registers (irreversible)
sorcha register update --id <trade-register-id> --devmode disable --profile demo
sorcha register update --id <finance-register-id> --devmode disable --profile demo

# Run the golden path again — now with field-level encryption
```

## Execution Modes

| Mode | How to Select | Use Case |
|------|--------------|----------|
| Scripted | `./run.ps1 -Scenario golden-path` or agent prompt with `Mode: scripted` | CI, deterministic testing |
| Persona | Agent prompt with `Mode: persona` | Live demos, realistic data |

## Scenarios

| Scenario | Command | Outcome |
|----------|---------|---------|
| Golden Path | `-Scenario golden-path` | Full flow, both VCs issued, financing approved |
| Disputed Invoice | `-Scenario disputed` | Invoice disputed, resubmitted, then approved |
| Declined Finance | `-Scenario declined` | Financing declined (low credit score) |
